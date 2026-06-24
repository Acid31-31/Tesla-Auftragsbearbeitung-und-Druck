using System;
using System.Globalization;
using System.IO;
using System.Xml.Linq;
using PDF_umbenenen.Models;

namespace PDF_umbenenen.Services
{
    internal static class DemoLicenseService
    {
        private static readonly string StorePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            AppInfo.ProductName,
            "demo.xml");

        public static DemoLicenseStatus Evaluate()
        {
            if (!AppInfo.IsDemoEdition)
            {
                return new DemoLicenseStatus
                {
                    IsDemoEdition = false,
                    IsExpired = false,
                    TitleSuffix = string.Empty,
                    SummaryText = AppInfo.EditionLabel,
                    VersionLine = AppInfo.EditionLabel
                };
            }

            var record = LoadOrCreate(DateTime.UtcNow);
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
                if (record == null)
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
            return record != null && !record.WelcomeShown;
        }

        private static DemoRecord LoadOrCreate(DateTime nowUtc)
        {
            var existing = LoadExisting();
            if (existing != null)
            {
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
                WelcomeShown = false
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

                return new DemoRecord
                {
                    FirstRunUtc = firstRun.Value,
                    LastRunUtc = ParseUtc((string)root.Element("LastRunUtc")) ?? firstRun.Value,
                    WelcomeShown = string.Equals((string)root.Element("WelcomeShown"), "true", StringComparison.OrdinalIgnoreCase),
                    IsFirstRun = false
                };
            }
            catch
            {
                return null;
            }
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
                Directory.CreateDirectory(Path.GetDirectoryName(StorePath));
                new XDocument(
                    new XElement("Demo",
                        new XElement("FirstRunUtc", record.FirstRunUtc.ToString("o", CultureInfo.InvariantCulture)),
                        new XElement("LastRunUtc", record.LastRunUtc.ToString("o", CultureInfo.InvariantCulture)),
                        new XElement("WelcomeShown", record.WelcomeShown ? "true" : "false")))
                    .Save(StorePath);
            }
            catch
            {
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
        }
    }
}
