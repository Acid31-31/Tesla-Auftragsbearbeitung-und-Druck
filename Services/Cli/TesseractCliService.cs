using System;
using System.Diagnostics;
using System.IO;

namespace PDF_umbenenen.Services.Cli
{
    /// <summary>
    /// Offline-OCR über Tesseract-Kommandozeile (kein Netzwerk, kein Ollama).
    /// </summary>
    internal static class TesseractCliService
    {
        public static string LastError { get; private set; }

        public static string RecognizeImage(string imagePath, string languages = "deu+eng")
        {
            LastError = string.Empty;
            if (!File.Exists(imagePath))
            {
                LastError = "bild-nicht-gefunden";
                return string.Empty;
            }

            var tesseract = ResolveExecutable();
            if (string.IsNullOrWhiteSpace(tesseract))
            {
                LastError = "tesseract-nicht-installiert";
                return string.Empty;
            }

            var outputBase = Path.Combine(Path.GetTempPath(), "dok-ocr-" + Guid.NewGuid().ToString("N"));
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = tesseract,
                    Arguments = "\"" + imagePath + "\" \"" + outputBase + "\" -l " + languages + " --psm 6",
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    RedirectStandardError = true,
                    RedirectStandardOutput = true
                };

                var tessDataPrefix = ResolveTessDataPrefix(tesseract);
                if (!string.IsNullOrWhiteSpace(tessDataPrefix))
                    psi.EnvironmentVariables["TESSDATA_PREFIX"] = tessDataPrefix;

                using (var process = Process.Start(psi))
                {
                    if (process == null)
                    {
                        LastError = "tesseract-start-failed";
                        return string.Empty;
                    }

                    var timeoutMs = WatermarkProcessingContext.BatchMode ? 12000 : 20000;
                    if (!process.WaitForExit(timeoutMs))
                    {
                        try
                        {
                            process.Kill();
                            process.WaitForExit(2000);
                        }
                        catch
                        {
                        }

                        LastError = "tesseract-timeout";
                        return string.Empty;
                    }
                }

                var txtPath = outputBase + ".txt";
                if (!File.Exists(txtPath))
                {
                    LastError = "tesseract-kein-output";
                    return string.Empty;
                }

                return File.ReadAllText(txtPath).Trim();
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
                return string.Empty;
            }
            finally
            {
                try { File.Delete(outputBase + ".txt"); } catch { }
            }
        }

        public static string ResolveExecutable()
        {
            var settings = Ai.AiSettingsStore.Load();
            if (!string.IsNullOrWhiteSpace(settings.TesseractPath) && File.Exists(settings.TesseractPath))
                return settings.TesseractPath;

            var candidates = new[]
            {
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "tesseract", "tesseract.exe"),
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "native", "win-x64", "tesseract", "tesseract.exe"),
                Path.Combine(AppInfo.UserDataDirectory, "Tools", "tesseract", "tesseract.exe"),
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Tools", "tesseract", "tesseract.exe"),
                @"C:\Program Files\Tesseract-OCR\tesseract.exe",
                @"C:\Program Files (x86)\Tesseract-OCR\tesseract.exe"
            };

            foreach (var path in candidates)
            {
                if (File.Exists(path))
                    return path;
            }

            var pathEnv = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
            foreach (var dir in pathEnv.Split(';'))
            {
                if (string.IsNullOrWhiteSpace(dir))
                    continue;

                var candidate = Path.Combine(dir.Trim(), "tesseract.exe");
                if (File.Exists(candidate))
                    return candidate;
            }

            return null;
        }

        public static bool IsAvailable() => !string.IsNullOrWhiteSpace(ResolveExecutable());

        public static string TryReadVersion()
        {
            var exe = ResolveExecutable();
            if (string.IsNullOrWhiteSpace(exe) || !File.Exists(exe))
                return null;

            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = exe,
                    Arguments = "--version",
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    RedirectStandardError = true,
                    RedirectStandardOutput = true
                };

                using (var process = Process.Start(psi))
                {
                    if (process == null)
                        return null;

                    var stdout = process.StandardOutput.ReadToEnd();
                    var stderr = process.StandardError.ReadToEnd();
                    if (!process.WaitForExit(4000))
                    {
                        try { process.Kill(); } catch { }
                        return null;
                    }

                    var text = (stdout + Environment.NewLine + stderr) ?? string.Empty;
                    var match = System.Text.RegularExpressions.Regex.Match(
                        text,
                        @"tesseract\s+v?(\d+\.\d+\.\d+)",
                        System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                    if (match.Success)
                        return match.Groups[1].Value;

                    var first = text.Trim().Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                    if (first.Length > 0)
                        return first[0].Trim();
                }
            }
            catch
            {
            }

            return null;
        }

        public static string ResolveTessDataPrefix(string tesseractPath)
        {
            if (string.IsNullOrWhiteSpace(tesseractPath))
                return string.Empty;

            var installDir = Path.GetDirectoryName(tesseractPath);
            if (string.IsNullOrWhiteSpace(installDir))
                return string.Empty;

            if (Directory.Exists(Path.Combine(installDir, "tessdata")))
                return installDir + Path.DirectorySeparatorChar;

            var parent = Directory.GetParent(installDir)?.FullName;
            if (!string.IsNullOrWhiteSpace(parent) && Directory.Exists(Path.Combine(parent, "tessdata")))
                return parent + Path.DirectorySeparatorChar;

            return string.Empty;
        }
    }
}
