using System;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;
using PDF_umbenenen.Models;

namespace PDF_umbenenen.Services
{
    internal static class DemoLicenseService
    {
        private const string IntegritySalt = "DOK-V01-Demo-Integrity-v2";

        private static string StorePath => Path.Combine(AppInfo.UserDataDirectory, "demo.xml");

        public static DemoLicenseStatus Evaluate()
        {
            if (!AppInfo.IsDemoEdition || LicenseActivationService.IsActivated())
            {
                var fullLabel = LicenseActivationService.IsActivated() && AppInfo.IsDemoEdition
                    ? "Vollversion (freigeschaltet)"
                    : AppInfo.EditionLabel;
                return new DemoLicenseStatus
                {
                    IsDemoEdition = false,
                    IsExpired = false,
                    TitleSuffix = string.Empty,
                    SummaryText = fullLabel,
                    VersionLine = fullLabel
                };
            }

            var record = LoadOrCreate(DateTime.UtcNow);
            if (record == null || record.Tampered)
            {
                return BuildTamperedStatus();
            }

            var firstRunLocal = record.FirstRunUtc.ToLocalTime().Date;
            var expiresLocal = firstRunLocal.AddDays(AppInfo.DemoPeriodDays);
            var daysRemaining = (expiresLocal - DateTime.Now.Date).Days;
            if (daysRemaining < 0)
                daysRemaining = 0;

            return BuildStatus(record, daysRemaining <= 0, daysRemaining);
        }

        public static void MarkWelcomeShown()
        {
            if (!AppInfo.IsDemoEdition)
                return;

            try
            {
                var record = LoadExisting();
                if (record == null || record.Tampered)
                    return;

                record.WelcomeShown = true;
                Save(record);
            }
            catch
            {
            }
        }

        public static bool ShouldShowWelcome()
        {
            if (!AppInfo.IsDemoEdition)
                return false;

            var record = LoadExisting();
            return record != null && !record.Tampered && !record.WelcomeShown;
        }

        internal static string GetMachineFingerprintPublic()
        {
            return GetMachineFingerprint();
        }

        private static DemoRecord LoadOrCreate(DateTime nowUtc)
        {
            var existing = LoadExisting();
            if (existing != null)
            {
                if (existing.Tampered)
                    return existing;

                if (IsClockRolledBack(existing, nowUtc))
                {
                    existing.Tampered = true;
                    return existing;
                }

                existing.LastRunUtc = nowUtc;
                existing.IsFirstRun = false;
                Save(existing);
                return existing;
            }

            var created = new DemoRecord
            {
                FirstRunUtc = nowUtc,
                LastRunUtc = nowUtc,
                IsFirstRun = true,
                WelcomeShown = false,
                MachineFingerprint = GetMachineFingerprint()
            };
            Save(created);
            return created;
        }

        private static DemoRecord LoadExisting()
        {
            try
            {
                if (!File.Exists(StorePath))
                    return null;

                var doc = XDocument.Load(StorePath);
                var root = doc.Root;
                if (root == null)
                    return null;

                var firstRun = ParseUtc((string)root.Element("FirstRunUtc"));
                if (!firstRun.HasValue)
                    return null;

                var lastRun = ParseUtc((string)root.Element("LastRunUtc")) ?? firstRun.Value;
                var welcomeShown = string.Equals((string)root.Element("WelcomeShown"), "true", StringComparison.OrdinalIgnoreCase);
                var machineFingerprint = (string)root.Element("MachineFingerprint") ?? string.Empty;
                var integrity = (string)root.Element("Integrity") ?? string.Empty;

                var record = new DemoRecord
                {
                    FirstRunUtc = firstRun.Value,
                    LastRunUtc = lastRun,
                    WelcomeShown = welcomeShown,
                    MachineFingerprint = machineFingerprint,
                    IsFirstRun = false
                };

                if (IsClockTampered(record))
                {
                    record.Tampered = true;
                    return record;
                }

                if (string.IsNullOrWhiteSpace(integrity))
                {
                    record.MachineFingerprint = GetMachineFingerprint();
                    Save(record);
                    return record;
                }

                if (!string.Equals(machineFingerprint, GetMachineFingerprint(), StringComparison.Ordinal))
                {
                    record.Tampered = true;
                    return record;
                }

                if (!string.Equals(integrity, ComputeIntegrity(record), StringComparison.Ordinal))
                {
                    record.Tampered = true;
                    return record;
                }

                return record;
            }
            catch
            {
                return new DemoRecord { Tampered = true };
            }
        }

        private static bool IsClockRolledBack(DemoRecord record, DateTime nowUtc)
        {
            var tolerance = TimeSpan.FromHours(2);
            if (nowUtc.Add(tolerance) < record.LastRunUtc)
                return true;

            if (nowUtc.Add(tolerance) < record.FirstRunUtc)
                return true;

            return false;
        }

        private static bool IsClockTampered(DemoRecord record)
        {
            var now = DateTime.UtcNow;
            var tolerance = TimeSpan.FromHours(2);
            if (record.FirstRunUtc > now.Add(tolerance))
                return true;

            if (record.LastRunUtc > now.Add(tolerance))
                return true;

            return false;
        }

        private static DemoLicenseStatus BuildTamperedStatus()
        {
            return new DemoLicenseStatus
            {
                IsDemoEdition = true,
                IsExpired = true,
                TitleSuffix = " · Demo ungültig",
                SummaryText = "Die Demo-Lizenz ist beschädigt oder wurde manipuliert. Bitte die App neu installieren.",
                VersionLine = AppInfo.EditionLabel + " · ungültig"
            };
        }

        private static DemoLicenseStatus BuildStatus(DemoRecord record, bool expired, int daysRemaining)
        {
            var firstRunLocal = record.FirstRunUtc.ToLocalTime().Date;
            var expiresLocal = firstRunLocal.AddDays(AppInfo.DemoPeriodDays);
            var expiresText = expiresLocal.ToString("dd.MM.yyyy", CultureInfo.GetCultureInfo("de-DE"));

            var titleSuffix = expired
                ? " · Demo abgelaufen"
                : " · Demo (" + daysRemaining + " Tag" + (daysRemaining == 1 ? string.Empty : "e") + ")";

            var summary = expired
                ? "Die Demoversion ist am " + expiresText + " abgelaufen."
                : "Demoversion – noch " + daysRemaining + " Tag" + (daysRemaining == 1 ? string.Empty : "e")
                  + " gültig (bis " + expiresText + ").";

            return new DemoLicenseStatus
            {
                IsDemoEdition = true,
                IsExpired = expired,
                IsFirstRun = record.IsFirstRun,
                FirstRunLocal = firstRunLocal,
                ExpiresLocal = expiresLocal,
                DaysRemaining = daysRemaining,
                TitleSuffix = titleSuffix,
                SummaryText = summary,
                VersionLine = AppInfo.EditionLabel + " · gültig bis " + expiresText
            };
        }

        private static void Save(DemoRecord record)
        {
            try
            {
                record.MachineFingerprint = GetMachineFingerprint();
                Directory.CreateDirectory(Path.GetDirectoryName(StorePath));
                new XDocument(
                    new XElement("Demo",
                        new XElement("FirstRunUtc", record.FirstRunUtc.ToString("o", CultureInfo.InvariantCulture)),
                        new XElement("LastRunUtc", record.LastRunUtc.ToString("o", CultureInfo.InvariantCulture)),
                        new XElement("WelcomeShown", record.WelcomeShown ? "true" : "false"),
                        new XElement("MachineFingerprint", record.MachineFingerprint),
                        new XElement("Integrity", ComputeIntegrity(record))))
                    .Save(StorePath);
            }
            catch
            {
            }
        }

        private static string ComputeIntegrity(DemoRecord record)
        {
            var payload = string.Join("|",
                record.FirstRunUtc.ToString("o", CultureInfo.InvariantCulture),
                record.LastRunUtc.ToString("o", CultureInfo.InvariantCulture),
                record.WelcomeShown ? "1" : "0",
                record.MachineFingerprint ?? string.Empty);

            using (var hmac = new HMACSHA256(GetIntegrityKey()))
            {
                return Convert.ToBase64String(hmac.ComputeHash(Encoding.UTF8.GetBytes(payload)));
            }
        }

        private static byte[] GetIntegrityKey()
        {
            using (var sha = SHA256.Create())
            {
                var material = AppInfo.ProductName + "|" + IntegritySalt + "|" + GetMachineFingerprint();
                return sha.ComputeHash(Encoding.UTF8.GetBytes(material));
            }
        }

        private static string GetMachineFingerprint()
        {
            var raw = Environment.MachineName + "|" + Environment.UserDomainName + "|" + Environment.UserName;
            using (var sha = SHA256.Create())
            {
                return Convert.ToBase64String(sha.ComputeHash(Encoding.UTF8.GetBytes(raw)));
            }
        }

        private static DateTime? ParseUtc(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
                return null;

            if (DateTime.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed))
                return parsed.Kind == DateTimeKind.Utc ? parsed : parsed.ToUniversalTime();

            return null;
        }

        private sealed class DemoRecord
        {
            public DateTime FirstRunUtc { get; set; }

            public DateTime LastRunUtc { get; set; }

            public bool WelcomeShown { get; set; }

            public bool IsFirstRun { get; set; }

            public string MachineFingerprint { get; set; }

            public bool Tampered { get; set; }
        }
    }
}
