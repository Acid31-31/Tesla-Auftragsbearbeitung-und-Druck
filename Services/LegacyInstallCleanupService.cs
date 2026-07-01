using System;
using System.IO;

namespace PDF_umbenenen.Services
{
    internal static class LegacyInstallCleanupService
    {
        public static void RemovePortableBinariesFromUserData()
        {
            var userDataDir = AppInfo.UserDataDirectory;
            if (!Directory.Exists(userDataDir))
                return;

            RemoveMatchingFiles(userDataDir, ".exe", ".dll", ".config", ".cer", ".ico");

            var assetsDir = Path.Combine(userDataDir, "Assets");
            if (Directory.Exists(assetsDir))
            {
                try
                {
                    Directory.Delete(assetsDir, true);
                }
                catch
                {
                }
            }
        }

        private static void RemoveMatchingFiles(string directory, params string[] extensions)
        {
            foreach (var file in Directory.GetFiles(directory))
            {
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

                try
                {
                    File.Delete(file);
                }
                catch
                {
                }
            }
        }
    }
}
