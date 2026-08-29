using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using PDF_umbenenen.Models;

namespace PDF_umbenenen.Services
{
    internal static class CustomerProfileStore
    {
        private static string StoreDirectory => Path.Combine(AppInfo.UserDataDirectory, "Customers");

        private static string IndexPath => Path.Combine(StoreDirectory, "profiles.xml");

        public static IReadOnlyList<CustomerProfile> LoadAll()
        {
            EnsureStoreDirectory();
            return LoadCustomProfiles();
        }

        public static bool HasAny()
        {
            return LoadAll().Count > 0;
        }

        private static List<CustomerProfile> LoadCustomProfiles()
        {
            var profiles = new List<CustomerProfile>();

            try
            {
                if (!File.Exists(IndexPath))
                    return profiles;

                var doc = XDocument.Load(IndexPath);
                foreach (var element in doc.Root?.Elements("Profile") ?? Enumerable.Empty<XElement>())
                {
                    var profile = ReadProfile(element);
                    if (profile != null
                        && !string.IsNullOrWhiteSpace(profile.Id)
                        && !profile.IsBuiltIn)
                    {
                        profiles.Add(profile);
                    }
                }
            }
            catch
            {
            }

            return profiles;
        }

        public static CustomerProfile GetById(string id)
        {
            if (string.IsNullOrWhiteSpace(id))
                return null;

            return LoadAll().FirstOrDefault(p => string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase));
        }

        public static void Save(CustomerProfile profile)
        {
            if (profile == null || string.IsNullOrWhiteSpace(profile.DisplayName))
                throw new InvalidOperationException("Kundenprofil ohne Namen kann nicht gespeichert werden.");

            profile.IsBuiltIn = false;

            if (string.IsNullOrWhiteSpace(profile.Id))
                profile.Id = CustomerProfileDraftSlug(profile.DisplayName);

            EnsureStoreDirectory();

            var profiles = LoadCustomProfiles()
                .Where(p => !string.Equals(p.Id, profile.Id, StringComparison.OrdinalIgnoreCase))
                .ToList();

            profiles.Add(profile);
            WriteAll(profiles);
        }

        public static bool Delete(string id)
        {
            if (string.IsNullOrWhiteSpace(id))
                return false;

            var profiles = LoadCustomProfiles()
                .Where(p => !string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (profiles.Count == LoadCustomProfiles().Count)
                return false;

            WriteAll(profiles);
            CustomerProfileSettingsStore.DeleteForProfile(id);
            return true;
        }

        public static string CustomerProfileDraftSlug(string displayName)
        {
            return new CustomerProfileDraft().ToProfile(displayName).Id;
        }

        private static void EnsureStoreDirectory()
        {
            try
            {
                Directory.CreateDirectory(StoreDirectory);
            }
            catch
            {
            }
        }

        private static void WriteAll(IEnumerable<CustomerProfile> profiles)
        {
            Directory.CreateDirectory(StoreDirectory);

            var customProfiles = profiles
                .Where(p => !p.IsBuiltIn)
                .GroupBy(p => p.Id, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.Last())
                .ToList();

            var root = new XElement("CustomerProfiles");
            foreach (var profile in customProfiles)
                root.Add(WriteProfile(profile));

            new XDocument(root).Save(IndexPath);
        }

        private static CustomerProfile ReadProfile(XElement element)
        {
            if (element == null)
                return null;

            Enum.TryParse(element.Element("WorkflowKind")?.Value, out CustomerWorkflowKind workflow);

            return new CustomerProfile
            {
                Id = element.Element("Id")?.Value ?? string.Empty,
                DisplayName = element.Element("DisplayName")?.Value ?? string.Empty,
                WorkflowKind = workflow,
                RequiresExcel = !string.Equals(element.Element("RequiresExcel")?.Value, "false", StringComparison.OrdinalIgnoreCase),
                RequiresOrderPdf = string.Equals(element.Element("RequiresOrderPdf")?.Value, "true", StringComparison.OrdinalIgnoreCase),
                IsBuiltIn = string.Equals(element.Element("IsBuiltIn")?.Value, "true", StringComparison.OrdinalIgnoreCase),
                DrawingNumberPattern = element.Element("DrawingNumberPattern")?.Value ?? CustomerProfile.DefaultDrawingPattern,
                ArticleNumberPattern = element.Element("ArticleNumberPattern")?.Value ?? string.Empty,
                QuantityPattern = element.Element("QuantityPattern")?.Value ?? string.Empty,
                SampleDrawingNumbers = ReadList(element, "SampleDrawingNumbers", "Drawing"),
                ExcelDrawingHeaders = ReadList(element, "ExcelDrawingHeaders", "Header"),
                ExcelAmountHeaders = ReadList(element, "ExcelAmountHeaders", "Header"),
                CreatedUtc = ParseDate(element.Element("CreatedUtc")?.Value),
                LearningSummary = element.Element("LearningSummary")?.Value ?? string.Empty
            };
        }

        private static XElement WriteProfile(CustomerProfile profile)
        {
            return new XElement("Profile",
                new XElement("Id", profile.Id ?? string.Empty),
                new XElement("DisplayName", profile.DisplayName ?? string.Empty),
                new XElement("WorkflowKind", profile.WorkflowKind.ToString()),
                new XElement("RequiresExcel", profile.RequiresExcel ? "true" : "false"),
                new XElement("RequiresOrderPdf", profile.RequiresOrderPdf ? "true" : "false"),
                new XElement("IsBuiltIn", "false"),
                new XElement("DrawingNumberPattern", profile.DrawingNumberPattern ?? string.Empty),
                new XElement("ArticleNumberPattern", profile.ArticleNumberPattern ?? string.Empty),
                new XElement("QuantityPattern", profile.QuantityPattern ?? string.Empty),
                WriteList("SampleDrawingNumbers", "Drawing", profile.SampleDrawingNumbers),
                WriteList("ExcelDrawingHeaders", "Header", profile.ExcelDrawingHeaders),
                WriteList("ExcelAmountHeaders", "Header", profile.ExcelAmountHeaders),
                new XElement("CreatedUtc", profile.CreatedUtc.ToString("o", CultureInfo.InvariantCulture)),
                new XElement("LearningSummary", profile.LearningSummary ?? string.Empty));
        }

        private static List<string> ReadList(XElement parent, string containerName, string itemName)
        {
            var container = parent?.Element(containerName);
            if (container == null)
                return new List<string>();

            return container.Elements(itemName)
                .Select(e => e.Value)
                .Where(v => !string.IsNullOrWhiteSpace(v))
                .ToList();
        }

        private static XElement WriteList(string containerName, string itemName, IEnumerable<string> values)
        {
            var container = new XElement(containerName);
            foreach (var value in values ?? Enumerable.Empty<string>())
            {
                if (!string.IsNullOrWhiteSpace(value))
                    container.Add(new XElement(itemName, value));
            }

            return container;
        }

        private static DateTime ParseDate(string raw)
        {
            if (DateTime.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var dt))
                return dt;

            return DateTime.UtcNow;
        }
    }
}
