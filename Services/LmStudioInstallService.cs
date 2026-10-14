using System;
using System.Diagnostics;
using System.IO;

namespace PDF_umbenenen.Services
{
    internal static class LmStudioInstallService
    {
        private const string WingetPackageId = "ElementLabs.LMStudio";

        public static bool IsInstalled()
        {
            return !string.IsNullOrWhiteSpace(ResolveExecutablePath());
        }

        public static string ResolveExecutablePath()
        {
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var candidates = new[]
            {
                Path.Combine(localAppData, @"Programs\LM Studio\LM Studio.exe"),
                Path.Combine(localAppData, @"LM Studio\LM Studio.exe"),
                @"C:\Program Files\LM Studio\LM Studio.exe"
            };

            foreach (var candidate in candidates)
            {
                if (File.Exists(candidate))
                    return candidate;
            }

            return string.Empty;
        }

        public static bool TryInstall(out string message)
        {
            message = string.Empty;

            if (IsInstalled())
            {
                message = "LM Studio ist bereits installiert:\r\n" + ResolveExecutablePath();
                return true;
            }

            if (TryInstallViaWinget(out message))
                return IsInstalled();

            message = string.IsNullOrWhiteSpace(message)
                ? "LM Studio konnte nicht automatisch installiert werden."
                : message;
            message += "\r\n\r\nAlternativ: https://lmstudio.ai/download im Browser öffnen, "
                       + "installieren, Modell laden und «Local Server» starten (Port 1234).";
            return false;
        }

        private static bool TryInstallViaWinget(out string message)
        {
            message = string.Empty;
            var winget = ResolveWingetExecutable();
            if (string.IsNullOrWhiteSpace(winget))
            {
                message = "winget nicht gefunden (Windows App Installer).";
                return false;
            }

            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = winget,
                    Arguments = "install --id " + WingetPackageId
                                + " -e --accept-package-agreements --accept-source-agreements",
                    UseShellExecute = true
                };

                var process = Process.Start(startInfo);
                if (process == null)
                {
                    message = "winget konnte nicht gestartet werden.";
                    return false;
                }

                process.WaitForExit(900000);
                if (IsInstalled())
                {
                    message = "LM Studio installiert:\r\n" + ResolveExecutablePath()
                              + "\r\n\r\nBitte LM Studio öffnen, ein Modell laden und «Local Server» starten.";
                    return true;
                }

                message = "Installation abgeschlossen, LM Studio wurde noch nicht gefunden. "
                          + "Bitte Assistenten abschließen und erneut prüfen.";
                return process.ExitCode == 0;
            }
            catch (Exception ex)
            {
                message = ex.Message;
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
