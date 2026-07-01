using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;

namespace PDF_umbenenen.Services
{
    internal static class UpdateApplyRunner
    {
        public static bool TryHandle(string[] args)
        {
            if (args == null || args.Length < 3)
                return false;

            if (!string.Equals(args[0], "--apply-update", StringComparison.OrdinalIgnoreCase))
                return false;

            var stagedRoot = args[1];
            var targetRoot = args[2];
            if (string.IsNullOrWhiteSpace(stagedRoot) || string.IsNullOrWhiteSpace(targetRoot))
                return false;

            stagedRoot = Path.GetFullPath(stagedRoot);
            targetRoot = Path.GetFullPath(targetRoot);

            if (!Directory.Exists(stagedRoot))
                return false;

            if (AppInfo.IsProtectedInstallDirectory(targetRoot)
                && !AdminElevationService.IsRunningAsAdministrator())
            {
                AdminElevationService.TryRelaunchAsAdministrator();
                return true;
            }

            WaitForTargetUnlock(targetRoot);

            string backupRoot = null;
            try
            {
                backupRoot = AppSecurityService.CreateInstallBackup(targetRoot);
                CopyApplicationFiles(stagedRoot, targetRoot);
            }
            catch
            {
                if (!string.IsNullOrWhiteSpace(backupRoot))
                {
                    try
                    {
                        AppSecurityService.RestoreInstallBackup(backupRoot, targetRoot);
                    }
                    catch
                    {
                    }
                }

                throw;
            }
            finally
            {
                AppSecurityService.DeleteDirectorySafe(backupRoot);
            }

            var exePath = Path.Combine(targetRoot, AppInfo.ExeFileName);
            if (File.Exists(exePath))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = exePath,
                    WorkingDirectory = targetRoot,
                    UseShellExecute = true
                });
            }

            return true;
        }

        private static void WaitForTargetUnlock(string targetRoot)
        {
            var lockFile = Path.Combine(targetRoot, AppInfo.ExeFileName);
            for (var attempt = 0; attempt < 40; attempt++)
            {
                try
                {
                    if (!File.Exists(lockFile))
                    {
                        Thread.Sleep(250);
                        return;
                    }

                    using (File.Open(lockFile, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                    {
                        return;
                    }
                }
                catch
                {
                    Thread.Sleep(250);
                }
            }
        }

        private static void CopyApplicationFiles(string sourceRoot, string targetRoot)
        {
            Directory.CreateDirectory(targetRoot);

            foreach (var file in Directory.GetFiles(sourceRoot, "*", SearchOption.AllDirectories))
            {
                var relative = file.Substring(sourceRoot.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                if (ShouldSkipRelativePath(relative))
                    continue;

                var extension = Path.GetExtension(file);
                if (!IsApplicationFileExtension(extension))
                    continue;

                var destination = Path.Combine(targetRoot, relative);
                var destinationDir = Path.GetDirectoryName(destination);
                if (!string.IsNullOrWhiteSpace(destinationDir))
                    Directory.CreateDirectory(destinationDir);

                File.Copy(file, destination, true);
            }
        }

        private static bool ShouldSkipRelativePath(string relativePath)
        {
            var fileName = Path.GetFileName(relativePath);
            return fileName.Equals(AppInfo.UsbLauncherFileName, StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsApplicationFileExtension(string extension)
        {
            return extension.Equals(".exe", StringComparison.OrdinalIgnoreCase)
                || extension.Equals(".dll", StringComparison.OrdinalIgnoreCase)
                || extension.Equals(".config", StringComparison.OrdinalIgnoreCase)
                || extension.Equals(".cer", StringComparison.OrdinalIgnoreCase)
                || extension.Equals(".ico", StringComparison.OrdinalIgnoreCase);
        }
    }
}
