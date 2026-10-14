using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Xml.Linq;

namespace PDF_umbenenen.Services
{
    internal sealed class InstallationResult
    {
        public bool Success { get; set; }

        public string InstalledExePath { get; set; } = string.Empty;

        public string Message { get; set; } = string.Empty;

        public string TesseractStatus { get; set; } = string.Empty;
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
            string targetDir,
            bool installPublisherCertificate,
            bool launchAfterInstall,
            bool installTesseractOcr = true,
            IProgress<string> progress = null)
        {
            var result = new InstallationResult();
            try
            {
                if (string.IsNullOrWhiteSpace(sourceDir) || !Directory.Exists(sourceDir))
                    throw new InvalidOperationException("Installationsquelle nicht gefunden.");

                if (string.IsNullOrWhiteSpace(targetDir))
                    throw new InvalidOperationException("Kein Zielordner ausgewählt.");

                targetDir = Path.GetFullPath(targetDir);

                progress?.Report("Installationsordner wird vorbereitet…");
                Directory.CreateDirectory(targetDir);
                Directory.CreateDirectory(Path.Combine(targetDir, "Ablage"));

                if (!IsSameDirectory(sourceDir, targetDir))
                {
                    progress?.Report("Programmdateien werden kopiert…");
                    CopyInstallFiles(sourceDir, targetDir);
                    SyncUpdateToken(sourceDir, targetDir);
                }

                progress?.Report("Alte Benutzerdaten werden übernommen…");
                InstallMigrationService.MigrateLegacyUserData(targetDir, progress);

                if (installPublisherCertificate)
                {
                    progress?.Report("Herausgeber-Zertifikat wird installiert…");
                    CertificateTrustService.TryInstallPublisherCertificate();
                }

                if (installTesseractOcr)
                {
                    progress?.Report("Tesseract OCR wird eingerichtet…");
                    if (TesseractInstallService.TryEnsureForInstall(sourceDir, targetDir, progress, out var tessPath, out var tessMessage))
                    {
                        WriteTargetAiSettings(targetDir, tessPath, ocrEnabled: true);
                        result.TesseractStatus = "eingerichtet (" + tessPath + ")";
                    }
                    else
                    {
                        WriteTargetAiSettings(targetDir, string.Empty, ocrEnabled: true);
                        result.TesseractStatus = tessMessage
                            ?? "nicht eingerichtet – bitte Internet prüfen oder Setup erneut ausführen";
                    }
                }
                else
                {
                    WriteTargetAiSettings(targetDir, string.Empty, ocrEnabled: false);
                    result.TesseractStatus = "nicht ausgewählt";
                }

                var installedExe = Path.Combine(targetDir, AppInfo.ExeFileName);
                progress?.Report("Desktop-Verknüpfung wird erstellt…");
                DesktopShortcutService.TryCreate(installedExe, out _);

                result.Success = true;
                result.InstalledExePath = installedExe;
                result.Message = "Installation abgeschlossen.";

                InstallRegistrationService.RegisterSuccessfulInstall(targetDir);
                AppInfo.SaveInstallLocation(targetDir);

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
            if (Directory.Exists(assetsSource))
            {
                var assetsTarget = Path.Combine(targetDir, "Assets");
                Directory.CreateDirectory(assetsTarget);

                foreach (var file in Directory.GetFiles(assetsSource))
                {
                    var name = Path.GetFileName(file);
                    var targetPath = AppSecurityService.ResolveSafeTargetPath(assetsTarget, name);
                    File.Copy(file, targetPath, true);
                }
            }

            var licenseSource = Path.Combine(sourceDir, "LICENSE_DE.txt");
            if (File.Exists(licenseSource))
            {
                File.Copy(
                    licenseSource,
                    AppSecurityService.ResolveSafeTargetPath(targetDir, "LICENSE_DE.txt"),
                    true);
            }

            CopyNativeDependencies(sourceDir, targetDir);
            CopyBundledToolDirectory(sourceDir, targetDir, "tesseract");
        }

        private static void WriteTargetAiSettings(string targetDir, string tesseractPath, bool ocrEnabled)
        {
            if (string.IsNullOrWhiteSpace(targetDir))
                return;

            try
            {
                Directory.CreateDirectory(targetDir);
                var path = Path.Combine(targetDir, "ai-settings.xml");
                var existed = File.Exists(path);
                var settings = existed
                    ? Ai.AiSettingsStore.ReadFrom(path)
                    : new Ai.AiSettings();

                settings.LocalOcrEnabled = ocrEnabled;
                settings.LocalVisionEnabled = true;
                settings.AiEnabled = true;
                if (!existed)
                    settings.OllamaEnabled = false;
                if (!string.IsNullOrWhiteSpace(tesseractPath))
                    settings.TesseractPath = tesseractPath;

                Ai.AiSettingsStore.WriteTo(path, settings);
            }
            catch
            {
            }
        }

        private static void CopyBundledToolDirectory(string sourceDir, string targetDir, string folderName)
        {
            var toolSource = Path.Combine(sourceDir, folderName);
            if (!Directory.Exists(toolSource))
                return;

            var toolTarget = Path.Combine(targetDir, folderName);
            Directory.CreateDirectory(toolTarget);

            foreach (var file in Directory.GetFiles(toolSource, "*", SearchOption.AllDirectories))
            {
                var relative = file.Substring(toolSource.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                var destination = Path.Combine(toolTarget, relative);
                var destinationDir = Path.GetDirectoryName(destination);
                if (!string.IsNullOrWhiteSpace(destinationDir))
                    Directory.CreateDirectory(destinationDir);

                File.Copy(file, destination, true);
            }
        }

        private static void CopyNativeDependencies(string sourceDir, string targetDir)
        {
            var nativeSource = Path.Combine(sourceDir, "native");
            if (!Directory.Exists(nativeSource))
                return;

            var nativeTarget = Path.Combine(targetDir, "native");
            Directory.CreateDirectory(nativeTarget);

            foreach (var file in Directory.GetFiles(nativeSource, "*", SearchOption.AllDirectories))
            {
                var relative = file.Substring(nativeSource.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                var destination = Path.Combine(nativeTarget, relative);
                var destinationDir = Path.GetDirectoryName(destination);
                if (!string.IsNullOrWhiteSpace(destinationDir))
                    Directory.CreateDirectory(destinationDir);

                File.Copy(file, destination, true);
            }
        }

        private static void SyncUpdateToken(string sourceDir, string targetDir)
        {
            if (AppInfo.GitHubUpdatesArePublic)
                return;

            var tokenSource = Path.Combine(sourceDir, "github.token");
            if (!File.Exists(tokenSource))
                return;

            try
            {
                File.Copy(
                    tokenSource,
                    AppSecurityService.ResolveSafeTargetPath(targetDir, "github.token"),
                    true);

                var userDataDir = AppInfo.UserDataDirectory;
                Directory.CreateDirectory(userDataDir);
                File.Copy(
                    tokenSource,
                    Path.Combine(userDataDir, "github.token"),
                    true);
            }
            catch
            {
            }
        }

        private static bool IsInstallFileExtension(string extension)
        {
            return extension.Equals(".exe", StringComparison.OrdinalIgnoreCase)
                || extension.Equals(".dll", StringComparison.OrdinalIgnoreCase)
                || extension.Equals(".config", StringComparison.OrdinalIgnoreCase)
                || extension.Equals(".cer", StringComparison.OrdinalIgnoreCase)
                || extension.Equals(".txt", StringComparison.OrdinalIgnoreCase)
                || extension.Equals(".token", StringComparison.OrdinalIgnoreCase);
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
