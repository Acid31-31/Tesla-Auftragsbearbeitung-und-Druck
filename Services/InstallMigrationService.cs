using System;
using System.IO;

namespace PDF_umbenenen.Services
{
    internal static class InstallMigrationService
    {
        public static void MigrateLegacyUserData(string targetInstallDirectory, IProgress<string> progress = null)
        {
            if (string.IsNullOrWhiteSpace(targetInstallDirectory))
                return;

            var legacyRoot = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                AppInfo.ProductName);

            if (!Directory.Exists(legacyRoot))
                return;

            progress?.Report("Alte Benutzerdaten werden übernommen…");
            CopyIfMissing(Path.Combine(legacyRoot, "Ablage"), Path.Combine(targetInstallDirectory, "Ablage"));
            CopyIfMissing(Path.Combine(legacyRoot, "Learning"), Path.Combine(targetInstallDirectory, "Learning"));
            CopyIfMissing(Path.Combine(legacyRoot, "Customers"), Path.Combine(targetInstallDirectory, "Customers"));
            CopyIfMissing(Path.Combine(legacyRoot, "Data"), Path.Combine(targetInstallDirectory, "Data"));
            CopyIfMissing(Path.Combine(legacyRoot, "Logs"), Path.Combine(targetInstallDirectory, "Logs"));
            CopyIfMissing(Path.Combine(legacyRoot, "Updates"), Path.Combine(targetInstallDirectory, "Updates"));
            CopyIfMissing(Path.Combine(legacyRoot, "Tools"), Path.Combine(targetInstallDirectory, "Tools"));

            CopyFileIfMissing(Path.Combine(legacyRoot, "paths.xml"), Path.Combine(targetInstallDirectory, "paths.xml"));
            CopyFileIfMissing(Path.Combine(legacyRoot, "demo.xml"), Path.Combine(targetInstallDirectory, "demo.xml"));
            CopyFileIfMissing(Path.Combine(legacyRoot, "github.token"), Path.Combine(targetInstallDirectory, "github.token"));

            foreach (var file in Directory.GetFiles(legacyRoot, "*.xml"))
            {
                var name = Path.GetFileName(file);
                if (string.Equals(name, "install-session.xml", StringComparison.OrdinalIgnoreCase))
                    continue;

                CopyFileIfMissing(file, Path.Combine(targetInstallDirectory, name));
            }
        }

        private static void CopyIfMissing(string sourceDir, string targetDir)
        {
            if (!Directory.Exists(sourceDir))
                return;

            Directory.CreateDirectory(targetDir);
            foreach (var file in Directory.GetFiles(sourceDir, "*", SearchOption.AllDirectories))
            {
                var relative = file.Substring(sourceDir.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                var destination = Path.Combine(targetDir, relative);
                var destinationDir = Path.GetDirectoryName(destination);
                if (!string.IsNullOrWhiteSpace(destinationDir))
                    Directory.CreateDirectory(destinationDir);

                if (!File.Exists(destination))
                    File.Copy(file, destination);
            }
        }

        private static void CopyFileIfMissing(string sourceFile, string targetFile)
        {
            if (!File.Exists(sourceFile) || File.Exists(targetFile))
                return;

            var targetDir = Path.GetDirectoryName(targetFile);
            if (!string.IsNullOrWhiteSpace(targetDir))
                Directory.CreateDirectory(targetDir);

            File.Copy(sourceFile, targetFile);
        }
    }
}
