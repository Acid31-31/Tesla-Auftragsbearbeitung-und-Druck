using System;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Reflection;
using System.Security.Cryptography.X509Certificates;
using System.Windows;

namespace PDF_umbenenen.Services
{
    internal static class AppSecurityService
    {
        private static bool _initialized;

        public static void Initialize()
        {
            if (_initialized)
                return;

            _initialized = true;
            EnsureSecureTransport();
        }

        /// <summary>Prüft Signatur und blockiert manipulierte Release-Builds.</summary>
        public static bool EnforceStartupSecurity()
        {
#if DEBUG
            return true;
#else
            if (!AppInfo.RequireCodeSignature)
                return true;

            if (TryVerifyCurrentExecutable(out _))
                return true;

            MessageBox.Show(
                "Die Anwendung ist nicht digital signiert oder wurde manipuliert.\n\n" +
                "Bitte installieren Sie " + AppInfo.ProductName + " nur über das offizielle Installationspaket " +
                "oder den automatischen GitHub-Updater.",
                AppInfo.ProductName + " – Sicherheit",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            return false;
#endif
        }

        public static void EnsureSecureTransport()
        {
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
        }

        public static bool IsTrustedDownloadUrl(Uri uri)
        {
            if (uri == null || !uri.IsAbsoluteUri)
                return false;

            if (!string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
                return false;

            var host = uri.Host ?? string.Empty;
            return host.Equals("github.com", StringComparison.OrdinalIgnoreCase)
                || host.Equals("api.github.com", StringComparison.OrdinalIgnoreCase)
                || host.Equals("objects.githubusercontent.com", StringComparison.OrdinalIgnoreCase);
        }

        public static string SanitizeFileName(string fileName)
        {
            var name = Path.GetFileName(fileName ?? string.Empty);
            if (string.IsNullOrWhiteSpace(name))
                return "datei";

            foreach (var invalid in Path.GetInvalidFileNameChars())
                name = name.Replace(invalid, '_');

            return name.Trim();
        }

        public static bool IsPathInsideDirectory(string candidatePath, string rootDirectory)
        {
            if (string.IsNullOrWhiteSpace(candidatePath) || string.IsNullOrWhiteSpace(rootDirectory))
                return false;

            try
            {
                var fullCandidate = Path.GetFullPath(candidatePath);
                var fullRoot = Path.GetFullPath(rootDirectory);
                if (!fullRoot.EndsWith(Path.DirectorySeparatorChar.ToString()))
                    fullRoot += Path.DirectorySeparatorChar;

                return fullCandidate.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        public static string ResolveSafeTargetPath(string targetFolder, string fileName)
        {
            var safeName = SanitizeFileName(fileName);
            var targetPath = Path.GetFullPath(Path.Combine(targetFolder, safeName));
            if (!IsPathInsideDirectory(targetPath, targetFolder))
                throw new InvalidOperationException("Ungültiger Zielpfad: " + safeName);

            return targetPath;
        }

        public static void ExtractZipSafely(string zipFilePath, string destinationDirectory)
        {
            if (string.IsNullOrWhiteSpace(zipFilePath) || !File.Exists(zipFilePath))
                throw new FileNotFoundException("ZIP-Datei nicht gefunden.", zipFilePath);

            Directory.CreateDirectory(destinationDirectory);
            var root = Path.GetFullPath(destinationDirectory);
            if (!root.EndsWith(Path.DirectorySeparatorChar.ToString()))
                root += Path.DirectorySeparatorChar;

            using (var archive = ZipFile.OpenRead(zipFilePath))
            {
                foreach (var entry in archive.Entries)
                {
                    if (string.IsNullOrWhiteSpace(entry.Name))
                        continue;

                    var entryPath = Path.GetFullPath(Path.Combine(destinationDirectory, entry.FullName));
                    if (!entryPath.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidOperationException("Unsicherer ZIP-Eintrag blockiert: " + entry.FullName);

                    var entryDir = Path.GetDirectoryName(entryPath);
                    if (!string.IsNullOrWhiteSpace(entryDir))
                        Directory.CreateDirectory(entryDir);

                    entry.ExtractToFile(entryPath, true);
                }
            }
        }

        public static bool TryVerifyCurrentExecutable(out string message)
        {
            var path = Assembly.GetExecutingAssembly().Location;
            return TryVerifyFileSignature(path, out message);
        }

        public static bool TryVerifyFileSignature(string filePath, out string message)
        {
            message = string.Empty;
            try
            {
                if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
                {
                    message = "Datei nicht gefunden.";
                    return false;
                }

                var cert = X509Certificate.CreateFromSignedFile(filePath);
                if (cert == null)
                {
                    message = "Keine Authenticode-Signatur.";
                    return false;
                }

                message = cert.Subject;
                return true;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                return false;
            }
        }

        public static string CreateInstallBackup(string targetRoot)
        {
            if (string.IsNullOrWhiteSpace(targetRoot) || !Directory.Exists(targetRoot))
                return null;

            var backupRoot = targetRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                + ".backup-" + DateTime.Now.ToString("yyyyMMddHHmmss");

            if (Directory.Exists(backupRoot))
                Directory.Delete(backupRoot, true);

            CopyApplicationTree(targetRoot, backupRoot);
            return backupRoot;
        }

        public static void RestoreInstallBackup(string backupRoot, string targetRoot)
        {
            if (string.IsNullOrWhiteSpace(backupRoot) || !Directory.Exists(backupRoot))
                return;

            CopyApplicationTree(backupRoot, targetRoot);
        }

        public static void DeleteDirectorySafe(string directoryPath)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(directoryPath) && Directory.Exists(directoryPath))
                    Directory.Delete(directoryPath, true);
            }
            catch
            {
            }
        }

        private static void CopyApplicationTree(string sourceRoot, string targetRoot)
        {
            Directory.CreateDirectory(targetRoot);

            foreach (var file in Directory.GetFiles(sourceRoot, "*", SearchOption.AllDirectories))
            {
                var relative = file.Substring(sourceRoot.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
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

        private static bool IsApplicationFileExtension(string extension)
        {
            return extension.Equals(".exe", StringComparison.OrdinalIgnoreCase)
                || extension.Equals(".dll", StringComparison.OrdinalIgnoreCase)
                || extension.Equals(".config", StringComparison.OrdinalIgnoreCase)
                || extension.Equals(".cer", StringComparison.OrdinalIgnoreCase)
                || extension.Equals(".ico", StringComparison.OrdinalIgnoreCase)
                || extension.Equals(".token", StringComparison.OrdinalIgnoreCase);
        }
    }
}
