using System;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace PDF_umbenenen.Services
{
    /// <summary>
    /// Offline-Freischaltung der Demo zur Vollversion (Lizenzschlüssel).
    /// </summary>
    internal static class LicenseActivationService
    {
        private const string IntegritySalt = "DOK-V01-License-Unlock-v1";
        private const string MasterPayload = "ENTERPRISE-UNLOCK|*";

        private static string StorePath => Path.Combine(AppInfo.UserDataDirectory, "license.xml");

        public static bool IsActivated()
        {
            if (!AppInfo.IsDemoEdition)
                return true;

            try
            {
                if (!File.Exists(StorePath))
                    return false;

                var doc = XDocument.Load(StorePath);
                var root = doc.Root;
                if (root == null)
                    return false;

                var key = ((string)root.Element("LicenseKey") ?? string.Empty).Trim();
                var machine = ((string)root.Element("MachineCode") ?? string.Empty).Trim();
                var integrity = ((string)root.Element("Integrity") ?? string.Empty).Trim();
                if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(integrity))
                    return false;

                var currentMachine = GetMachineCode();
                if (!string.IsNullOrWhiteSpace(machine)
                    && !string.Equals(machine, currentMachine, StringComparison.OrdinalIgnoreCase)
                    && !IsValidMasterKey(NormalizeKey(key)))
                {
                    return false;
                }

                if (!string.Equals(integrity, ComputeStoreIntegrity(key, machine), StringComparison.Ordinal))
                    return false;

                return IsValidKey(NormalizeKey(key), currentMachine);
            }
            catch
            {
                return false;
            }
        }

        public static string GetMachineCode()
        {
            var raw = DemoLicenseService.GetMachineFingerprintPublic() ?? string.Empty;
            using (var sha = SHA256.Create())
            {
                var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(raw));
                var hex = BytesToHex(hash, 6);
                return FormatGroups(hex, 4);
            }
        }

        public static bool TryActivate(string licenseKey, out string message)
        {
            message = string.Empty;
            if (!AppInfo.IsDemoEdition)
            {
                message = "Diese Installation ist bereits eine Vollversion.";
                return true;
            }

            var normalized = NormalizeKey(licenseKey);
            if (string.IsNullOrWhiteSpace(normalized))
            {
                message = "Bitte einen Lizenzschlüssel eingeben.";
                return false;
            }

            var machineCode = GetMachineCode();
            if (!IsValidKey(normalized, machineCode))
            {
                message = "Ungültiger Lizenzschlüssel für diesen PC.";
                return false;
            }

            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(StorePath) ?? AppInfo.UserDataDirectory);
                var storeMachine = IsValidMasterKey(normalized) ? string.Empty : machineCode;
                new XDocument(
                    new XElement("License",
                        new XElement("LicenseKey", normalized),
                        new XElement("MachineCode", storeMachine),
                        new XElement("ActivatedUtc", DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture)),
                        new XElement("Integrity", ComputeStoreIntegrity(normalized, storeMachine))))
                    .Save(StorePath);

                message = "Vollversion freigeschaltet.";
                return true;
            }
            catch (Exception ex)
            {
                message = "Freischaltung fehlgeschlagen: " + ex.Message;
                return false;
            }
        }

        /// <summary>Maschinengebundenen Schlüssel erzeugen (für Anbieter).</summary>
        public static string GenerateMachineKey(string machineCode = null)
        {
            var code = NormalizeKey(string.IsNullOrWhiteSpace(machineCode) ? GetMachineCode() : machineCode);
            var digest = ComputeKeyDigest("FULL|" + code);
            return "DOK-" + FormatGroups(digest, 4);
        }

        /// <summary>Universeller Freischalt-Schlüssel (für Anbieter).</summary>
        public static string GenerateMasterKey()
        {
            var digest = ComputeKeyDigest(MasterPayload);
            return "DOK-FULL-" + FormatGroups(digest, 4);
        }

        private static bool IsValidKey(string normalizedKey, string machineCode)
        {
            return IsValidMasterKey(normalizedKey) || IsValidMachineKey(normalizedKey, machineCode);
        }

        private static bool IsValidMasterKey(string normalizedKey)
        {
            var expected = NormalizeKey(GenerateMasterKey());
            return string.Equals(normalizedKey, expected, StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsValidMachineKey(string normalizedKey, string machineCode)
        {
            if (string.IsNullOrWhiteSpace(machineCode))
                return false;

            var expected = NormalizeKey(GenerateMachineKey(machineCode));
            return string.Equals(normalizedKey, expected, StringComparison.OrdinalIgnoreCase);
        }

        private static string ComputeKeyDigest(string payload)
        {
            using (var hmac = new HMACSHA256(GetSecretKey()))
            {
                var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(payload));
                return BytesToHex(hash, 8);
            }
        }

        private static string ComputeStoreIntegrity(string key, string machineCode)
        {
            var payload = (key ?? string.Empty) + "|" + (machineCode ?? string.Empty) + "|" + GetMachineCode();
            using (var hmac = new HMACSHA256(GetSecretKey()))
            {
                return Convert.ToBase64String(hmac.ComputeHash(Encoding.UTF8.GetBytes(payload)));
            }
        }

        private static byte[] GetSecretKey()
        {
            using (var sha = SHA256.Create())
            {
                return sha.ComputeHash(Encoding.UTF8.GetBytes(AppInfo.ProductName + "|" + IntegritySalt));
            }
        }

        private static string NormalizeKey(string key)
        {
            if (string.IsNullOrWhiteSpace(key))
                return string.Empty;

            var cleaned = Regex.Replace(key.Trim().ToUpperInvariant(), @"[^A-Z0-9]", string.Empty);
            if (cleaned.StartsWith("DOKFULL", StringComparison.Ordinal))
                return "DOK-FULL-" + FormatGroups(cleaned.Substring("DOKFULL".Length), 4);

            if (cleaned.StartsWith("DOK", StringComparison.Ordinal))
                return "DOK-" + FormatGroups(cleaned.Substring(3), 4);

            return FormatGroups(cleaned, 4);
        }

        private static string FormatGroups(string hexOrAlnum, int groupSize)
        {
            if (string.IsNullOrEmpty(hexOrAlnum))
                return string.Empty;

            var sb = new StringBuilder();
            for (var i = 0; i < hexOrAlnum.Length; i++)
            {
                if (i > 0 && i % groupSize == 0)
                    sb.Append('-');
                sb.Append(hexOrAlnum[i]);
            }

            return sb.ToString();
        }

        private static string BytesToHex(byte[] bytes, int takeBytes)
        {
            var count = Math.Min(bytes.Length, Math.Max(1, takeBytes));
            var sb = new StringBuilder(count * 2);
            for (var i = 0; i < count; i++)
                sb.Append(bytes[i].ToString("X2", CultureInfo.InvariantCulture));
            return sb.ToString();
        }
    }
}
