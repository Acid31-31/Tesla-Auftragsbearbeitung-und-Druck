using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
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
                || host.Equals("raw.githubusercontent.com", StringComparison.OrdinalIgnoreCase)
                || host.Equals("api.github.com", StringComparison.OrdinalIgnoreCase)
                || host.Equals("objects.githubusercontent.com", StringComparison.OrdinalIgnoreCase)
                || host.Equals("digi.bib.uni-mannheim.de", StringComparison.OrdinalIgnoreCase)
                || host.Equals("ollama.com", StringComparison.OrdinalIgnoreCase)
                || host.Equals("www.ollama.com", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>Nur Ollama-Hersteller: ollama.com und deren offizielle Windows-Datei.</summary>
        public static bool IsTrustedOllamaManufacturerUrl(Uri uri)
        {
            if (uri == null || !uri.IsAbsoluteUri)
                return false;

            if (!string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
                return false;

            var host = uri.Host ?? string.Empty;
            if (host.Equals("ollama.com", StringComparison.OrdinalIgnoreCase)
                || host.Equals("www.ollama.com", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (host.Equals("github.com", StringComparison.OrdinalIgnoreCase)
                || host.Equals("www.github.com", StringComparison.OrdinalIgnoreCase))
            {
                var path = uri.AbsolutePath ?? string.Empty;
                return path.IndexOf("/ollama/ollama/", StringComparison.OrdinalIgnoreCase) >= 0;
            }

            return host.Equals("release-assets.githubusercontent.com", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>Nur Ollama-Registry zum Prüfen des Vision-Moduls (HTTPS, library-Manifests).</summary>
        public static bool IsTrustedOllamaRegistryUrl(Uri uri)
        {
            if (uri == null || !uri.IsAbsoluteUri)
                return false;

            if (!string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
                return false;

            var host = uri.Host ?? string.Empty;
            if (!host.Equals("registry.ollama.ai", StringComparison.OrdinalIgnoreCase))
                return false;

            var path = uri.AbsolutePath ?? string.Empty;
            return path.StartsWith("/v2/library/", StringComparison.OrdinalIgnoreCase);
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
            return TryVerifyFileSignature(path, out message, null);
        }

        public static bool TryVerifyFileSignature(string filePath, out string message, string trustCerDirectory = null)
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

                var signedCert = new X509Certificate2(cert);
                if (!TryValidateSignedCertificate(signedCert, trustCerDirectory, out message))
                    return false;

                message = signedCert.Subject;
                return true;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                return false;
            }
        }

        public static bool TryVerifyFileSignature(string filePath, out string message)
        {
            return TryVerifyFileSignature(filePath, out message, null);
        }

        /// <summary>Prüft nur die Haupt-EXE; Paketintegrität zusätzlich per SHA256 beim Download.</summary>
        public static bool TryVerifyApplicationPackage(string packageRoot, out string message, string trustCerDirectory = null)
        {
            message = string.Empty;
            if (string.IsNullOrWhiteSpace(packageRoot) || !Directory.Exists(packageRoot))
            {
                message = "Update-Paket nicht gefunden.";
                return false;
            }

            var cerDirectory = string.IsNullOrWhiteSpace(trustCerDirectory) ? packageRoot : trustCerDirectory;
            var mainExe = Path.Combine(packageRoot, AppInfo.ExeFileName);
            if (!File.Exists(mainExe))
            {
                message = AppInfo.ExeFileName + " fehlt im Update-Paket.";
                return false;
            }

            if (!TryVerifyFileSignature(mainExe, out message, cerDirectory))
                return false;

            // Integrität des Pakets: SHA256 beim Download + Signatur der Haupt-EXE.
            // Tesseract/OCR-Hilfsprogramme (ambiguous_words.exe usw.) sind Drittanbieter-Bundle.
            return true;
        }

        private static string GetRelativePathSafe(string root, string fullPath)
        {
            try
            {
                var rootFull = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                               + Path.DirectorySeparatorChar;
                var fileFull = Path.GetFullPath(fullPath);
                if (!fileFull.StartsWith(rootFull, StringComparison.OrdinalIgnoreCase))
                    return null;

                return fileFull.Substring(rootFull.Length);
            }
            catch
            {
                return null;
            }
        }

        public static bool TryVerifyApplicationPackage(string packageRoot, out string message)
        {
            return TryVerifyApplicationPackage(packageRoot, out message, null);
        }

        private static bool TryValidateSignedCertificate(
            X509Certificate2 signedCert,
            string trustCerDirectory,
            out string message)
        {
            message = string.Empty;
            if (signedCert == null)
            {
                message = "Keine Signatur.";
                return false;
            }

            if (TryGetTrustedPublisherThumbprint(trustCerDirectory, out var trustedThumbprint)
                && string.Equals(signedCert.Thumbprint, trustedThumbprint, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (HasValidCertificateChain(signedCert, out message))
                return true;

            if (!string.IsNullOrWhiteSpace(trustedThumbprint))
                message = "Signatur stammt nicht vom autorisierten Herausgeber (Zertifikat).";

            return false;
        }

        private static bool TryGetTrustedPublisherThumbprint(string trustCerDirectory, out string thumbprint)
        {
            thumbprint = string.Empty;
            var directories = new[]
            {
                trustCerDirectory,
                AppDomain.CurrentDomain.BaseDirectory
            };

            foreach (var directory in directories)
            {
                if (string.IsNullOrWhiteSpace(directory))
                    continue;

                var cerPath = Path.Combine(directory, AppInfo.CodeSigningCerFileName);
                if (!File.Exists(cerPath))
                    continue;

                try
                {
                    var trusted = new X509Certificate2(cerPath);
                    thumbprint = trusted.Thumbprint;
                    if (!string.IsNullOrWhiteSpace(thumbprint))
                        return true;
                }
                catch
                {
                }
            }

            return false;
        }

        private static bool TryGetTrustedPublisherThumbprint(out string thumbprint)
        {
            return TryGetTrustedPublisherThumbprint(null, out thumbprint);
        }

        private static bool HasValidCertificateChain(X509Certificate2 certificate, out string message)
        {
            message = string.Empty;
            try
            {
                using (var chain = new X509Chain())
                {
                    chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
                    chain.ChainPolicy.VerificationFlags = X509VerificationFlags.NoFlag;
                    if (!chain.Build(certificate))
                    {
                        message = "Zertifikatskette ungültig.";
                        return false;
                    }
                }

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
                if (ShouldSkipWhenCopyingInstallTree(relative))
                    continue;

                var destination = Path.Combine(targetRoot, relative);
                var destinationDir = Path.GetDirectoryName(destination);
                if (!string.IsNullOrWhiteSpace(destinationDir))
                    Directory.CreateDirectory(destinationDir);

                File.Copy(file, destination, true);
            }
        }

        internal static bool ShouldSkipWhenCopyingInstallTree(string relativePath)
        {
            if (string.IsNullOrWhiteSpace(relativePath))
                return true;

            var fileName = Path.GetFileName(relativePath);
            if (fileName.Equals(AppInfo.UsbLauncherFileName, StringComparison.OrdinalIgnoreCase)
                || fileName.Equals(AppInfo.UsbUninstallerFileName, StringComparison.OrdinalIgnoreCase)
                || fileName.Equals(AppInfo.InstallManifestFileName, StringComparison.OrdinalIgnoreCase)
                || fileName.Equals("paths.xml", StringComparison.OrdinalIgnoreCase)
                || fileName.Equals("ai-settings.xml", StringComparison.OrdinalIgnoreCase)
                || fileName.Equals("watermark-settings.xml", StringComparison.OrdinalIgnoreCase)
                || fileName.Equals("demo.xml", StringComparison.OrdinalIgnoreCase)
                || fileName.Equals("github.token", StringComparison.OrdinalIgnoreCase)
                || fileName.Equals("STARTEN.bat", StringComparison.OrdinalIgnoreCase)
                || fileName.Equals("DEINSTALLIEREN.bat", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            var firstSep = relativePath.IndexOfAny(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar });
            var rootSegment = firstSep < 0 ? relativePath : relativePath.Substring(0, firstSep);
            return rootSegment.Equals("Ablage", StringComparison.OrdinalIgnoreCase)
                || rootSegment.Equals("Updates", StringComparison.OrdinalIgnoreCase)
                || rootSegment.Equals("Learning", StringComparison.OrdinalIgnoreCase)
                || rootSegment.Equals("Customers", StringComparison.OrdinalIgnoreCase)
                || rootSegment.Equals("Logs", StringComparison.OrdinalIgnoreCase)
                || rootSegment.Equals("Data", StringComparison.OrdinalIgnoreCase)
                || rootSegment.Equals("Backups", StringComparison.OrdinalIgnoreCase);
        }
    }
}
