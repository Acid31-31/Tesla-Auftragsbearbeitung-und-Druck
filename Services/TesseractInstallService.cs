using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using PDF_umbenenen.Models;
using PDF_umbenenen.Services.Cli;

namespace PDF_umbenenen.Services
{
    internal static class TesseractInstallService
    {
        public const string InstallerFileName = "tesseract-ocr-w64-setup-5.4.0.20240606.exe";
        private const string InstallerUrl =
            "https://digi.bib.uni-mannheim.de/tesseract/tesseract-ocr-w64-setup-5.4.0.20240606.exe";

        public static string UserToolsDirectory => Path.Combine(AppInfo.UserDataDirectory, "Tools");

        public static string InstallerDirectory => Path.Combine(UserToolsDirectory, "installers");

        public static string TesseractInstallDirectory => Path.Combine(UserToolsDirectory, "tesseract");

        public static string InstalledExecutablePath =>
            Path.Combine(TesseractInstallDirectory, "tesseract.exe");

        /// <summary>
        /// Richtet Tesseract im Installationsordner ein: mitgeliefertes Bundle oder Download.
        /// </summary>
        public static bool TryEnsureForInstall(
            string sourceDir,
            string targetDir,
            IProgress<string> progress,
            out string executablePath,
            out string message)
        {
            executablePath = string.Empty;
            message = string.Empty;

            var existing = FindBundledExecutable(targetDir) ?? FindBundledExecutable(sourceDir);
            if (!string.IsNullOrWhiteSpace(existing))
            {
                executablePath = existing;
                message = existing;
                return true;
            }

            var installDir = Path.Combine(targetDir ?? string.Empty, "Tools", "tesseract");
            var installed = Path.Combine(installDir, "tesseract.exe");
            if (File.Exists(installed))
            {
                executablePath = installed;
                message = installed;
                return true;
            }

            try
            {
                Directory.CreateDirectory(Path.Combine(targetDir, "Tools", "installers"));
                Directory.CreateDirectory(installDir);

                var installerPath = Path.Combine(targetDir, "Tools", "installers", InstallerFileName);
                if (!File.Exists(installerPath))
                {
                    progress?.Report("Tesseract-Installer wird heruntergeladen…");
                    AppSecurityService.EnsureSecureTransport();
                    using (var client = new WebClient())
                        client.DownloadFile(InstallerUrl, installerPath);
                }

                if (!File.Exists(installerPath))
                {
                    message = "Tesseract-Installer konnte nicht heruntergeladen werden.";
                    return false;
                }

                progress?.Report("Tesseract OCR wird installiert…");
                if (TrySilentInstallTo(installerPath, installDir) && File.Exists(installed))
                {
                    executablePath = installed;
                    message = installed;
                    return true;
                }

                message = "Tesseract-Installer konnte nicht automatisch ausgeführt werden.";
                return false;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                return false;
            }
        }

        public static string FindBundledExecutable(string root)
        {
            if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
                return null;

            var candidates = new[]
            {
                Path.Combine(root, "tesseract", "tesseract.exe"),
                Path.Combine(root, "native", "win-x64", "tesseract", "tesseract.exe"),
                Path.Combine(root, "Tools", "tesseract", "tesseract.exe")
            };

            foreach (var candidate in candidates)
            {
                if (File.Exists(candidate))
                    return candidate;
            }

            return null;
        }

        public static bool TryInstall(out string message)
        {
            message = string.Empty;

            if (TesseractCliService.IsAvailable())
            {
                message = TesseractCliService.ResolveExecutable();
                return true;
            }

            try
            {
                Directory.CreateDirectory(InstallerDirectory);
                Directory.CreateDirectory(TesseractInstallDirectory);

                var installerPath = Path.Combine(InstallerDirectory, InstallerFileName);
                if (!File.Exists(installerPath))
                {
                    AppSecurityService.EnsureSecureTransport();
                    using (var client = new WebClient())
                        client.DownloadFile(InstallerUrl, installerPath);
                }

                if (!File.Exists(installerPath))
                {
                    message = "Installer konnte nicht heruntergeladen werden.";
                    return false;
                }

                if (TrySilentInstall(installerPath) && TesseractCliService.IsAvailable())
                {
                    PersistDetectedPath();
                    message = TesseractCliService.ResolveExecutable();
                    return true;
                }

                if (TryInteractiveInstall(installerPath))
                {
                    if (TesseractCliService.IsAvailable())
                    {
                        PersistDetectedPath();
                        message = TesseractCliService.ResolveExecutable();
                        return true;
                    }

                    message = "Der Tesseract-Installer wurde gestartet. Bitte Assistenten abschließen "
                        + "und danach dieses Fenster erneut öffnen.";
                    return false;
                }

                if (TryInstallViaWinget())
                {
                    if (TesseractCliService.IsAvailable())
                    {
                        PersistDetectedPath();
                        message = TesseractCliService.ResolveExecutable();
                        return true;
                    }
                }

                message = "Tesseract wurde noch nicht erkannt. Bitte Installation abschließen und erneut prüfen.";
                return false;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                return false;
            }
        }

        public static void PersistDetectedPath()
        {
            var path = TesseractCliService.ResolveExecutable();
            if (string.IsNullOrWhiteSpace(path))
                return;

            PersistPath(path);
        }

        public static void PersistUserInstallPath()
        {
            if (!File.Exists(InstalledExecutablePath))
                return;

            PersistPath(InstalledExecutablePath);
        }

        public static async Task<string> UpdateToLatestAsync(
            TesseractUpdateInfo info,
            IProgress<UpdateProgressInfo> progress,
            CancellationToken cancellationToken)
        {
            if (info == null || string.IsNullOrWhiteSpace(info.LatestFileName))
                throw new InvalidOperationException("Keine Tesseract-Update-Datei gefunden.");

            var fileName = AppSecurityService.SanitizeFileName(info.LatestFileName);
            if (!IsSafeInstallerFileName(fileName))
                throw new InvalidOperationException("Ungültiger Tesseract-Installer-Name.");

            var downloadUrl = string.IsNullOrWhiteSpace(info.DownloadUrl)
                ? TesseractUpdateCheckService.IndexUrl + fileName
                : info.DownloadUrl.Trim();

            if (!Uri.TryCreate(downloadUrl, UriKind.Absolute, out var uri)
                || !AppSecurityService.IsTrustedDownloadUrl(uri))
            {
                throw new InvalidOperationException("Tesseract-Download-URL ist nicht vertrauenswürdig.");
            }

            Directory.CreateDirectory(InstallerDirectory);
            Directory.CreateDirectory(TesseractInstallDirectory);
            var installerPath = Path.Combine(InstallerDirectory, fileName);

            Report(progress, 2, "Installer wird heruntergeladen…");
            AppSecurityService.EnsureSecureTransport();
            await DownloadInstallerAsync(uri, installerPath, progress, cancellationToken).ConfigureAwait(false);

            if (!File.Exists(installerPath) || new FileInfo(installerPath).Length < 1024 * 1024)
                throw new InvalidOperationException("Tesseract-Installer konnte nicht heruntergeladen werden.");

            Report(progress, 82, "Signatur wird geprüft…");
            if (!AppSecurityService.TryVerifyFileSignature(installerPath, out var signatureMessage))
            {
                try { File.Delete(installerPath); } catch { }
                throw new InvalidOperationException(
                    "Tesseract-Installer ist nicht gültig signiert. Installation abgebrochen.\n" + signatureMessage);
            }

            Report(progress, 88, "Tesseract wird installiert…");
            if (!TrySilentInstall(installerPath, cancellationToken) || !File.Exists(InstalledExecutablePath))
            {
                throw new InvalidOperationException(
                    "Die Tesseract-Installation ist fehlgeschlagen. Bitte später erneut versuchen.");
            }

            PersistUserInstallPath();
            Report(progress, 100, "Tesseract wurde aktualisiert.");
            return InstalledExecutablePath;
        }

        private static void PersistPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                return;

            var settings = Ai.AiSettingsStore.Load();
            settings.TesseractPath = path;
            Ai.AiSettingsStore.Save(settings);
        }

        private static bool IsSafeInstallerFileName(string fileName)
        {
            if (string.IsNullOrWhiteSpace(fileName) || fileName != Path.GetFileName(fileName))
                return false;

            return Regex.IsMatch(
                fileName,
                @"^tesseract-ocr-w64-setup-v?\d[\w.\-]*\.exe$",
                RegexOptions.IgnoreCase);
        }

        private static async Task DownloadInstallerAsync(
            Uri uri,
            string installerPath,
            IProgress<UpdateProgressInfo> progress,
            CancellationToken cancellationToken)
        {
            using (var client = new HttpClient())
            {
                client.Timeout = TimeSpan.FromMinutes(10);
                client.DefaultRequestHeaders.UserAgent.ParseAdd("DOK-V01-TesseractUpdate");

                using (var response = await client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                    .ConfigureAwait(false))
                {
                    response.EnsureSuccessStatusCode();
                    var total = response.Content.Headers.ContentLength ?? 0L;
                    using (var source = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
                    using (var destination = File.Create(installerPath))
                    {
                        var buffer = new byte[81920];
                        long readTotal = 0;
                        int read;
                        while ((read = await source.ReadAsync(buffer, 0, buffer.Length, cancellationToken).ConfigureAwait(false)) > 0)
                        {
                            await destination.WriteAsync(buffer, 0, read, cancellationToken).ConfigureAwait(false);
                            readTotal += read;
                            var percent = total > 0
                                ? 2 + (int)(readTotal * 78L / total)
                                : 40;
                            Report(progress, percent, "Installer wird heruntergeladen…", readTotal, total);
                        }
                    }
                }
            }
        }

        private static void Report(
            IProgress<UpdateProgressInfo> progress,
            int percent,
            string message,
            long bytesRead = 0,
            long totalBytes = 0)
        {
            progress?.Report(new UpdateProgressInfo(percent, message, bytesRead, totalBytes));
        }

        private static bool TrySilentInstall(string installerPath, CancellationToken cancellationToken = default)
        {
            return TrySilentInstallTo(installerPath, TesseractInstallDirectory, cancellationToken);
        }

        private static bool TrySilentInstallTo(
            string installerPath,
            string installDirectory,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(installDirectory))
                return false;

            Directory.CreateDirectory(installDirectory);
            var args = "/VERYSILENT /NORESTART /SUPPRESSMSGBOXES /DIR=\"" + installDirectory + "\"";
            return RunInstaller(installerPath, args, wait: true, cancellationToken)
                   && File.Exists(Path.Combine(installDirectory, "tesseract.exe"));
        }

        private static bool TryInteractiveInstall(string installerPath)
        {
            var args = "/DIR=\"" + TesseractInstallDirectory + "\"";
            return RunInstaller(installerPath, args, wait: false);
        }

        private static bool RunInstaller(
            string installerPath,
            string arguments,
            bool wait,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = installerPath,
                    Arguments = arguments,
                    UseShellExecute = true
                };

                var process = Process.Start(startInfo);
                if (process == null)
                    return false;

                if (wait)
                {
                    while (!process.HasExited)
                    {
                        if (cancellationToken.IsCancellationRequested)
                        {
                            try
                            {
                                if (!process.HasExited)
                                    process.Kill();
                            }
                            catch
                            {
                            }

                            cancellationToken.ThrowIfCancellationRequested();
                        }

                        process.WaitForExit(400);
                    }

                    return process.ExitCode == 0;
                }

                return true;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                return false;
            }
        }

        private static bool TryInstallViaWinget()
        {
            try
            {
                var winget = ResolveWingetExecutable();
                if (string.IsNullOrWhiteSpace(winget))
                    return false;

                var startInfo = new ProcessStartInfo
                {
                    FileName = winget,
                    Arguments = "install UB-Mannheim.TesseractOCR --accept-package-agreements --accept-source-agreements",
                    UseShellExecute = true
                };

                Process.Start(startInfo)?.WaitForExit(300000);
                return File.Exists(@"C:\Program Files\Tesseract-OCR\tesseract.exe")
                    || File.Exists(@"C:\Program Files (x86)\Tesseract-OCR\tesseract.exe");
            }
            catch
            {
                return false;
            }
        }

        private static string ResolveWingetExecutable()
        {
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var candidates = new[]
            {
                Path.Combine(localAppData, @"Microsoft\WindowsApps\winget.exe"),
                Path.Combine(localAppData, @"Microsoft\WindowsApps\Microsoft.DesktopAppInstaller_8wekyb3d8bbwe\winget.exe")
            };

            foreach (var candidate in candidates)
            {
                if (File.Exists(candidate))
                    return candidate;
            }

            return null;
        }
    }
}
