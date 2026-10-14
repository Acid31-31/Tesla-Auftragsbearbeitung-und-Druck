using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace PDF_umbenenen.Services
{
    /// <summary>
    /// Registriert eine ordnungsgemäße Desktop-Installation (DPAPI-geschützt).
    /// </summary>
    internal static class InstallRegistrationService
    {
        private const string IntegritySalt = "DOK-V01-Install-Registration-v1";

        public static void RegisterSuccessfulInstall(string installDirectory)
        {
            if (string.IsNullOrWhiteSpace(installDirectory) || !Directory.Exists(installDirectory))
                return;

            try
            {
                var manifestPath = GetManifestPath(installDirectory);
                var payload = BuildPayload(installDirectory);
                var protectedBytes = ProtectedData.Protect(
                    Encoding.UTF8.GetBytes(payload),
                    GetOptionalEntropy(installDirectory),
                    DataProtectionScope.CurrentUser);

                File.WriteAllBytes(manifestPath, protectedBytes);
            }
            catch
            {
            }
        }

        public static bool IsRegisteredInstall(string installDirectory)
        {
            if (string.IsNullOrWhiteSpace(installDirectory))
                return false;

            var manifestPath = GetManifestPath(installDirectory);
            if (!File.Exists(manifestPath))
                return true;

            try
            {
                var protectedBytes = File.ReadAllBytes(manifestPath);
                var plain = ProtectedData.Unprotect(
                    protectedBytes,
                    GetOptionalEntropy(installDirectory),
                    DataProtectionScope.CurrentUser);
                var payload = Encoding.UTF8.GetString(plain);
                return ValidatePayload(payload, installDirectory);
            }
            catch
            {
                return false;
            }
        }

        public static void EnsureRegisteredInstall(string installDirectory)
        {
            if (string.IsNullOrWhiteSpace(installDirectory) || !AppInfo.IsRegisteredInstallDirectory(installDirectory))
                return;

            if (IsRegisteredInstall(installDirectory))
                return;

            if (AppSecurityService.TryVerifyFileSignature(
                    Path.Combine(installDirectory, AppInfo.ExeFileName),
                    out _))
            {
                RegisterSuccessfulInstall(installDirectory);
            }
        }

        private static string GetManifestPath(string installDirectory)
        {
            return Path.Combine(installDirectory, AppInfo.InstallManifestFileName);
        }

        private static string BuildPayload(string installDirectory)
        {
            var installRoot = Path.GetFullPath(installDirectory)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var fingerprint = DemoLicenseService.GetMachineFingerprintPublic();
            var utc = DateTime.UtcNow.ToString("o");
            var signature = ComputePayloadSignature(installRoot, fingerprint, utc);
            return installRoot + "|" + fingerprint + "|" + utc + "|" + signature;
        }

        private static bool ValidatePayload(string payload, string installDirectory)
        {
            if (string.IsNullOrWhiteSpace(payload))
                return false;

            var parts = payload.Split('|');
            if (parts.Length != 4)
                return false;

            var installRoot = Path.GetFullPath(installDirectory)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (!string.Equals(parts[0], installRoot, StringComparison.OrdinalIgnoreCase))
                return false;

            if (!string.Equals(parts[1], DemoLicenseService.GetMachineFingerprintPublic(), StringComparison.Ordinal))
                return false;

            return string.Equals(
                parts[3],
                ComputePayloadSignature(parts[0], parts[1], parts[2]),
                StringComparison.Ordinal);
        }

        private static string ComputePayloadSignature(string installRoot, string fingerprint, string utc)
        {
            var material = AppInfo.ProductName + "|" + IntegritySalt + "|" + installRoot + "|" + fingerprint + "|" + utc;
            using (var sha = SHA256.Create())
            {
                return Convert.ToBase64String(sha.ComputeHash(Encoding.UTF8.GetBytes(material)));
            }
        }

        private static byte[] GetOptionalEntropy(string installDirectory)
        {
            var material = AppInfo.ProductName + "|" + Path.GetFullPath(installDirectory);
            using (var sha = SHA256.Create())
            {
                return sha.ComputeHash(Encoding.UTF8.GetBytes(material));
            }
        }
    }
}
