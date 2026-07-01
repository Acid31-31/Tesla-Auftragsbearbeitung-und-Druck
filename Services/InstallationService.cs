using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;

namespace PDF_umbenenen.Services
{
    internal sealed class InstallationResult
    {
        public bool Success { get; set; }

        public string InstalledExePath { get; set; } = string.Empty;

        public string Message { get; set; } = string.Empty;
    }

    internal static class InstallationService
    {
        private static readonly HashSet<string> SkipFileNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            AppInfo.UsbLauncherFileName,
            AppInfo.UsbUninstallerFileName,
            "STARTEN.bat",
            "DEINSTALLIEREN.bat",
            "README_USB.txt",
            "Deinstallieren.ps1"
        };

        public static string GetSourceDirectory()
        {
            return Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? string.Empty;
        }

        public static InstallationResult RunInstall(
            string sourceDir,
            bool installPublisherCertificate,
            bool launchAfterInstall,
            IProgress<string> progress = null)
        {
            var result = new InstallationResult();
            try
            {
                if (string.IsNullOrWhiteSpace(sourceDir) || !Directory.Exists(sourceDir))
                    throw new InvalidOperationException("Installationsquelle nicht gefunden.");

                if (!AdminElevationService.IsRunningAsAdministrator())
                    throw new InvalidOperationException("Administratorrechte erforderlich.");

                var targetDir = AppInfo.DefaultInstallDirectory;
                progress?.Report("Installationsordner wird vorbereitet…");
                Directory.CreateDirectory(targetDir);

                if (!IsSameDirectory(sourceDir, targetDir))
                {
                    progress?.Report("Programmdateien werden kopiert…");
                    CopyInstallFiles(sourceDir, targetDir);
                }

                progress?.Report("Alte portable Dateien werden entfernt…");
                LegacyInstallCleanupService.RemovePortableBinariesFromUserData();

                if (installPublisherCertificate)
                {
                    progress?.Report("Herausgeber-Zertifikat wird installiert…");
                    CertificateTrustService.TryInstallPublisherCertificate();
                }

                var installedExe = Path.Combine(targetDir, AppInfo.ExeFileName);
                progress?.Report("Desktop-Verknüpfung wird erstellt…");
                DesktopShortcutService.TryCreate(installedExe, out _);

                result.Success = true;
                result.InstalledExePath = installedExe;
                result.Message = "Installation abgeschlossen.";

                if (launchAfterInstall && File.Exists(installedExe))
                {
                    progress?.Report("Anwendung wird gestartet…");
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = installedExe,
                        WorkingDirectory = targetDir,
                        UseShellExecute = true
                    });
                }
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.Message = ex.Message;
            }

            return result;
        }

        private static void CopyInstallFiles(string sourceDir, string targetDir)
        {
            foreach (var file in Directory.GetFiles(sourceDir))
            {
                var name = Path.GetFileName(file);
                if (SkipFileNames.Contains(name))
                    continue;

                var ext = Path.GetExtension(name);
                if (!IsInstallFileExtension(ext))
                    continue;

                var targetPath = AppSecurityService.ResolveSafeTargetPath(targetDir, name);
                File.Copy(file, targetPath, true);
            }

            var assetsSource = Path.Combine(sourceDir, "Assets");
            if (!Directory.Exists(assetsSource))
                return;

            var assetsTarget = Path.Combine(targetDir, "Assets");
            Directory.CreateDirectory(assetsTarget);

            foreach (var file in Directory.GetFiles(assetsSource))
            {
                var name = Path.GetFileName(file);
                var targetPath = AppSecurityService.ResolveSafeTargetPath(assetsTarget, name);
                File.Copy(file, targetPath, true);
            }

            var licenseSource = Path.Combine(sourceDir, "LICENSE_DE.txt");
            if (File.Exists(licenseSource))
            {
                File.Copy(
                    licenseSource,
                    AppSecurityService.ResolveSafeTargetPath(targetDir, "LICENSE_DE.txt"),
                    true);
            }
        }

        private static bool IsInstallFileExtension(string extension)
        {
            return extension.Equals(".exe", StringComparison.OrdinalIgnoreCase)
                || extension.Equals(".dll", StringComparison.OrdinalIgnoreCase)
                || extension.Equals(".config", StringComparison.OrdinalIgnoreCase)
                || extension.Equals(".cer", StringComparison.OrdinalIgnoreCase)
                || extension.Equals(".txt", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsSameDirectory(string left, string right)
        {
            return string.Equals(
                Path.GetFullPath(left).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                Path.GetFullPath(right).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                StringComparison.OrdinalIgnoreCase);
        }
    }
}
