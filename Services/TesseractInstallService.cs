using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using PDF_umbenenen.Services.Cli;

namespace PDF_umbenenen.Services
{
    internal static class TesseractInstallService
    {
        public const string InstallerFileName = "tesseract-ocr-w64-setup-5.4.0.20240606.exe";
        private const string InstallerUrl =
            "https://digi.bib.uni-mannheim.de/tesseract/tesseract-ocr-w64-setup-5.4.0.20240606.exe";
        private const string ExpectedInstallerSha256 =
            "c885fff6998e0608ba4bb8ab51436e1c6775c2bafc2559a19b423e18678b60c9";

        public static string UserToolsDirectory => Path.Combine(AppInfo.UserDataDirectory, "Tools");

        public static string InstallerDirectory => Path.Combine(UserToolsDirectory, "installers");

        public static string TesseractInstallDirectory => Path.Combine(UserToolsDirectory, "tesseract");

        public static string InstalledExecutablePath =>
            Path.Combine(TesseractInstallDirectory, "tesseract.exe");

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
                    var installerUri = new Uri(InstallerUrl);
                    if (!AppSecurityService.IsTrustedDownloadUrl(installerUri))
                    {
                        message = "Download-Quelle nicht vertrauenswürdig.";
                        return false;
                    }

                    using (var client = new WebClient())
                        client.DownloadFile(installerUri, installerPath);
                }

                if (!AppSecurityService.TryVerifyFileSha256(installerPath, ExpectedInstallerSha256, out var hashMessage))
                {
                    try { File.Delete(installerPath); } catch { }
                    message = "Tesseract-Installer ungültig: " + hashMessage;
                    return false;
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

            var settings = Ai.AiSettingsStore.Load();
            settings.TesseractPath = path;
            Ai.AiSettingsStore.Save(settings);
        }

        private static bool TrySilentInstall(string installerPath)
        {
            var args = "/VERYSILENT /NORESTART /SUPPRESSMSGBOXES /DIR=\"" + TesseractInstallDirectory + "\"";
            return RunInstaller(installerPath, args, wait: true);
        }

        private static bool TryInteractiveInstall(string installerPath)
        {
            var args = "/DIR=\"" + TesseractInstallDirectory + "\"";
            return RunInstaller(installerPath, args, wait: false);
        }

        private static bool RunInstaller(string installerPath, string arguments, bool wait)
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
                    process.WaitForExit(120000);
                    return process.ExitCode == 0 && File.Exists(InstalledExecutablePath);
                }

                return true;
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
