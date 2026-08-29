using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace PDF_umbenenen.Services
{
    internal sealed class UninstallResult
    {
        public bool Success { get; set; }

        public string Message { get; set; } = string.Empty;

        public bool DeferredFolderCleanupScheduled { get; set; }
    }

    internal static class UninstallService
    {
        private static readonly HashSet<string> SkipUninstallFileNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            AppInfo.UsbLauncherFileName,
            AppInfo.UsbUninstallerFileName,
            "STARTEN.bat",
            "DEINSTALLIEREN.bat",
            "README_USB.txt"
        };

        public static bool IsInstalled()
        {
            return !string.IsNullOrWhiteSpace(AppInfo.TryReadRegisteredInstallDirectory());
        }

        public static UninstallResult RunUninstall(bool removeUserData, IProgress<string> progress = null)
        {
            var result = new UninstallResult();
            try
            {
                var installDir = AppInfo.TryReadRegisteredInstallDirectory();
                if (string.IsNullOrWhiteSpace(installDir) || !Directory.Exists(installDir))
                    throw new InvalidOperationException("Keine Installation gefunden.");

                progress?.Report("Desktop-Verknüpfung wird entfernt…");
                RemoveDesktopShortcut();

                if (removeUserData)
                {
                    progress?.Report("Installationsordner wird entfernt…");
                    if (IsRunningFromDirectory(installDir))
                    {
                        RemoveProgramFiles(installDir);
                        RemoveUserDataFolders(installDir);
                        ScheduleDeferredFolderRemoval(installDir);
                        result.DeferredFolderCleanupScheduled = true;
                    }
                    else
                    {
                        Directory.Delete(installDir, true);
                    }
                }
                else
                {
                    progress?.Report("Programmdateien werden entfernt…");
                    RemoveProgramFiles(installDir);
                }

                ClearInstallPointer();
                InstallSessionStore.Clear();
                result.Success = true;
                result.Message = BuildSuccessMessage(removeUserData, result.DeferredFolderCleanupScheduled);
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.Message = ex.Message;
            }

            return result;
        }

        private static string BuildSuccessMessage(bool removeUserData, bool deferredCleanup)
        {
            if (!removeUserData)
                return "DOK-V01 wurde deinstalliert. Der Ablage-Ordner wurde beibehalten.";

            if (deferredCleanup)
                return "DOK-V01 wurde deinstalliert. Der Ordner wird nach dem Schließen dieses Fensters vollständig entfernt.";

            return "DOK-V01 wurde vollständig deinstalliert.";
        }

        private static void RemoveProgramFiles(string installDir)
        {
            RemoveMatchingFiles(installDir, ".exe", ".dll", ".config", ".cer", ".ico", ".manifest", ".token");
            RemoveDirectoryIfExists(Path.Combine(installDir, "Assets"));
            RemoveDirectoryIfExists(Path.Combine(installDir, "native"));
            RemoveDirectoryIfExists(Path.Combine(installDir, "tesseract"));
        }

        private static void RemoveUserDataFolders(string installDir)
        {
            RemoveDirectoryIfExists(Path.Combine(installDir, "Ablage"));
            RemoveDirectoryIfExists(Path.Combine(installDir, "Learning"));
            RemoveDirectoryIfExists(Path.Combine(installDir, "Customers"));
            RemoveDirectoryIfExists(Path.Combine(installDir, "Data"));
            RemoveDirectoryIfExists(Path.Combine(installDir, "Logs"));
            RemoveDirectoryIfExists(Path.Combine(installDir, "Updates"));
            RemoveDirectoryIfExists(Path.Combine(installDir, "Tools"));
            RemoveDirectoryIfExists(Path.Combine(installDir, "Diagnostics"));
        }

        private static void RemoveMatchingFiles(string directory, params string[] extensions)
        {
            foreach (var file in Directory.GetFiles(directory))
            {
                if (ShouldSkipUninstallFile(file))
                    continue;

                var extension = Path.GetExtension(file);
                var shouldRemove = false;
                foreach (var allowed in extensions)
                {
                    if (extension.Equals(allowed, StringComparison.OrdinalIgnoreCase))
                    {
                        shouldRemove = true;
                        break;
                    }
                }

                if (!shouldRemove)
                    continue;

                TryDeleteFile(file);
            }
        }

        private static bool ShouldSkipUninstallFile(string path)
        {
            var name = Path.GetFileName(path);
            if (SkipUninstallFileNames.Contains(name))
                return true;

            try
            {
                var currentProcessPath = Process.GetCurrentProcess().MainModule?.FileName;
                if (!string.IsNullOrWhiteSpace(currentProcessPath)
                    && string.Equals(Path.GetFullPath(currentProcessPath), Path.GetFullPath(path), StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            catch
            {
            }

            return false;
        }

        private static void TryDeleteFile(string path)
        {
            try
            {
                if (File.Exists(path))
                    File.Delete(path);
            }
            catch
            {
            }
        }

        private static void RemoveDirectoryIfExists(string path)
        {
            if (!Directory.Exists(path))
                return;

            try
            {
                Directory.Delete(path, true);
            }
            catch
            {
            }
        }

        private static bool IsRunningFromDirectory(string directory)
        {
            try
            {
                var appDir = AppInfo.GetApplicationDirectory();
                return string.Equals(
                    Path.GetFullPath(appDir).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                    Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                    StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        private static void ScheduleDeferredFolderRemoval(string installDir)
        {
            var batchPath = Path.Combine(Path.GetTempPath(), "DOK-V01-uninstall-cleanup.cmd");
            var content = new StringBuilder();
            content.AppendLine("@echo off");
            content.AppendLine(":retry");
            content.AppendLine("timeout /t 1 /nobreak >nul");
            content.AppendLine("rd /s /q \"" + installDir + "\" 2>nul");
            content.AppendLine("if exist \"" + installDir + "\" goto retry");
            content.AppendLine("del \"%~f0\"");

            File.WriteAllText(batchPath, content.ToString(), Encoding.Default);
            Process.Start(new ProcessStartInfo
            {
                FileName = batchPath,
                CreateNoWindow = true,
                UseShellExecute = true,
                WindowStyle = ProcessWindowStyle.Hidden
            });
        }

        private static void RemoveDesktopShortcut()
        {
            var desktop = DesktopShortcutService.ResolveDesktopPath();
            if (string.IsNullOrWhiteSpace(desktop))
                return;

            var shortcut = Path.Combine(desktop, AppInfo.ShortcutFileName);
            if (File.Exists(shortcut))
                File.Delete(shortcut);
        }

        private static void ClearInstallPointer()
        {
            try
            {
                var pointer = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    AppInfo.ProductName,
                    "install-location.txt");
                if (File.Exists(pointer))
                    File.Delete(pointer);
            }
            catch
            {
            }
        }
    }
}
