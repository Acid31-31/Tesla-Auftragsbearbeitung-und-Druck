using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using PDF_umbenenen.Services.Ai;
using PDF_umbenenen.Services.Cli;

namespace PDF_umbenenen.Services
{
    /// <summary>
    /// Stellt Tesseract OCR ohne manuelle Installation bereit (gebündelt oder automatischer Download).
    /// </summary>
    internal static class TesseractBootstrapService
    {
        private static readonly object Sync = new object();
        private static volatile bool _bootstrapStarted;

        private static readonly IReadOnlyDictionary<string, string> RequiredTessdataFiles =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["deu.traineddata"] = "https://github.com/tesseract-ocr/tessdata_fast/raw/main/deu.traineddata",
                ["eng.traineddata"] = "https://github.com/tesseract-ocr/tessdata_fast/raw/main/eng.traineddata",
                ["osd.traineddata"] = "https://github.com/tesseract-ocr/tessdata_fast/raw/main/osd.traineddata"
            };

        public static bool IsAvailable => TesseractCliService.IsAvailable();

        public static void ScheduleStartupBootstrap()
        {
            if (_bootstrapStarted)
                return;

            var settings = AiSettingsStore.Load();
            if (!settings.LocalOcrEnabled)
                return;

            _bootstrapStarted = true;
            Task.Run(() =>
            {
                try
                {
                    TryEnsureAvailable(allowDownloadInstall: true, out _);
                }
                catch
                {
                }
            });
        }

        public static Task EnsureAvailableAsync(CancellationToken cancellationToken)
        {
            return Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                TryEnsureAvailable(allowDownloadInstall: true, out _);
            }, cancellationToken);
        }

        public static bool TryEnsureAvailable(bool allowDownloadInstall, out string message)
        {
            lock (Sync)
            {
                if (TesseractCliService.IsAvailable())
                {
                    message = TesseractCliService.ResolveExecutable();
                    TesseractInstallService.PersistDetectedPath();
                    TryEnsureRequiredTessdata(allowDownloadInstall);
                    return true;
                }

                if (TryUseBundledCopy(out message))
                {
                    TryEnsureRequiredTessdata(allowDownloadInstall);
                    return true;
                }

                if (!allowDownloadInstall)
                {
                    message = "Tesseract nicht gefunden.";
                    return false;
                }

                if (TesseractInstallService.TryInstall(out message))
                {
                    TesseractInstallService.PersistDetectedPath();
                    TryEnsureRequiredTessdata(allowDownloadInstall);
                    return true;
                }

                return false;
            }
        }

        private static void TryEnsureRequiredTessdata(bool allowDownload)
        {
            if (!allowDownload)
                return;

            var executable = TesseractCliService.ResolveExecutable();
            var tessdataDir = ResolveTessdataDirectory(executable);
            if (string.IsNullOrWhiteSpace(tessdataDir))
                return;

            try
            {
                Directory.CreateDirectory(tessdataDir);
                AppSecurityService.EnsureSecureTransport();

                foreach (var entry in RequiredTessdataFiles)
                {
                    var target = Path.Combine(tessdataDir, entry.Key);
                    if (File.Exists(target))
                        continue;

                    if (!AppSecurityService.IsTrustedDownloadUrl(new Uri(entry.Value)))
                        continue;

                    using (var client = new WebClient())
                        client.DownloadFile(entry.Value, target);
                }
            }
            catch
            {
            }
        }

        private static string ResolveTessdataDirectory(string executable)
        {
            if (string.IsNullOrWhiteSpace(executable))
                return string.Empty;

            var prefix = TesseractCliService.ResolveTessDataPrefix(executable);
            if (string.IsNullOrWhiteSpace(prefix))
                return string.Empty;

            return Path.Combine(prefix.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar), "tessdata");
        }

        private static bool TryUseBundledCopy(out string message)
        {
            message = string.Empty;
            var baseDir = AppDomain.CurrentDomain.BaseDirectory;
            var bundledRoots = new[]
            {
                Path.Combine(baseDir, "tesseract"),
                Path.Combine(baseDir, "native", "win-x64", "tesseract"),
                Path.Combine(baseDir, "Tools", "tesseract")
            };

            foreach (var root in bundledRoots)
            {
                var exe = Path.Combine(root, "tesseract.exe");
                if (!File.Exists(exe))
                    continue;

                message = exe;
                TesseractInstallService.PersistDetectedPath();
                return true;
            }

            return false;
        }
    }
}
