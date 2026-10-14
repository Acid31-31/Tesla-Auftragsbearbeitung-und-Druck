using System;
using System.IO;
using System.Reflection;

namespace PDF_umbenenen.Services
{
    internal static class DesktopShortcutService
    {
        public static bool TryCreate(string targetExePath, out string message)
        {
            message = string.Empty;
            try
            {
                if (string.IsNullOrWhiteSpace(targetExePath) || !File.Exists(targetExePath))
                {
                    message = "Programmdatei nicht gefunden.";
                    return false;
                }

                var desktopPath = ResolveDesktopPath();
                if (string.IsNullOrWhiteSpace(desktopPath))
                {
                    message = "Desktop-Ordner nicht gefunden.";
                    return false;
                }

                var baseDir = Path.GetDirectoryName(targetExePath);
                var shortcutPath = Path.Combine(desktopPath, AppInfo.ShortcutFileName);
                // Icon immer aus dem Zielordner der EXE – nie aus dem laufenden Debug-Build.
                var iconPath = ResolveIconBesideExe(baseDir) ?? targetExePath;

                var wshShellType = Type.GetTypeFromProgID("WScript.Shell");
                if (wshShellType == null)
                {
                    message = "Windows-Shell nicht verfügbar.";
                    return false;
                }

                dynamic wshShell = Activator.CreateInstance(wshShellType);
                dynamic shortcut = wshShell.CreateShortcut(shortcutPath);
                shortcut.TargetPath = targetExePath;
                shortcut.WorkingDirectory = baseDir;
                shortcut.IconLocation = iconPath + ",0";
                shortcut.Description = AppInfo.ProductName + " starten";
                shortcut.Save();

                if (!File.Exists(shortcutPath))
                {
                    message = "Verknüpfung konnte nicht gespeichert werden.";
                    return false;
                }

                message = shortcutPath;
                return true;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                return false;
            }
        }

        public static string ResolveDesktopPath()
        {
            var candidates = new[]
            {
                Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
                Environment.GetFolderPath(Environment.SpecialFolder.Desktop)
            };

            foreach (var candidate in candidates)
            {
                if (!string.IsNullOrWhiteSpace(candidate) && Directory.Exists(candidate))
                    return candidate;
            }

            return null;
        }

        private static string ResolveIconBesideExe(string baseDir)
        {
            if (string.IsNullOrWhiteSpace(baseDir))
                return null;

            var candidates = new[]
            {
                Path.Combine(baseDir, AppInfo.IconFileName),
                Path.Combine(baseDir, "Assets", AppInfo.IconFileName)
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
