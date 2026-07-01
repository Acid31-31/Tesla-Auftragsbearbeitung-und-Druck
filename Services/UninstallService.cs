using System;
using System.IO;

namespace PDF_umbenenen.Services
{
    internal sealed class UninstallResult
    {
        public bool Success { get; set; }

        public string Message { get; set; } = string.Empty;
    }

    internal static class UninstallService
    {
        public static bool IsInstalled()
        {
            return File.Exists(Path.Combine(AppInfo.DefaultInstallDirectory, AppInfo.ExeFileName));
        }

        public static UninstallResult RunUninstall(bool removeUserData, IProgress<string> progress = null)
        {
            var result = new UninstallResult();
            try
            {
                if (!AdminElevationService.IsRunningAsAdministrator())
                    throw new InvalidOperationException("Administratorrechte erforderlich.");

                progress?.Report("Desktop-Verknüpfung wird entfernt…");
                RemoveDesktopShortcut();

                var installDir = AppInfo.DefaultInstallDirectory;
                if (Directory.Exists(installDir))
                {
                    progress?.Report("Programmdateien werden entfernt…");
                    Directory.Delete(installDir, true);
                }

                if (removeUserData)
                {
                    progress?.Report("Benutzerdaten werden entfernt…");
                    RemoveUserData();
                }

                InstallSessionStore.Clear();
                result.Success = true;
                result.Message = removeUserData
                    ? "DOK-V01 wurde vollständig deinstalliert."
                    : "DOK-V01 wurde deinstalliert. Benutzerdaten (Ablage) wurden beibehalten.";
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.Message = ex.Message;
            }

            return result;
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

        private static void RemoveUserData()
        {
            var userData = AppInfo.UserDataDirectory;
            if (!Directory.Exists(userData))
                return;

            Directory.Delete(userData, true);
        }
    }
}
