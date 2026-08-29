using System;
using System.IO;

namespace PDF_umbenenen.Services
{
    internal static class UserSettingsPath
    {
        public static string GetFilePath(string fileName)
        {
            if (string.IsNullOrWhiteSpace(fileName))
                throw new ArgumentException("fileName");

            var directory = AppInfo.UserDataDirectory;
            Directory.CreateDirectory(directory);
            MigrateLegacyFile(fileName, directory);
            return Path.Combine(directory, fileName);
        }

        private static void MigrateLegacyFile(string fileName, string targetDirectory)
        {
            try
            {
                var legacyDirectory = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    AppInfo.ProductName);

                var legacyPath = Path.Combine(legacyDirectory, fileName);
                var targetPath = Path.Combine(targetDirectory, fileName);

                if (!File.Exists(targetPath) && File.Exists(legacyPath))
                    File.Copy(legacyPath, targetPath);
            }
            catch
            {
            }
        }
    }
}
