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
        private static readonly string StoreDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            AppInfo.ProductName,
            "Customers");

        private static readonly string IndexPath = Path.Combine(StoreDirectory, "profiles.xml");

        public static IReadOnlyList<CustomerProfile> LoadAll()
        {
            EnsureBuiltInProfiles();
            var profiles = new List<CustomerProfile> { CreateBuiltInTesla() };
            profiles.AddRange(LoadCustomProfiles());
            return profiles;
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
                        && !profile.IsBuiltIn
                        && !string.Equals(profile.Id, CustomerProfile.TeslaId, StringComparison.OrdinalIgnoreCase))
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

        public static CustomerProfile GetBuiltInTesla()
        {
            return CreateBuiltInTesla();
        }

        public static void Save(CustomerProfile profile)
        {
            if (profile == null || string.IsNullOrWhiteSpace(profile.DisplayName))
                throw new InvalidOperationException("Kundenprofil ohne Namen kann nicht gespeichert werden.");

            if (profile.IsBuiltIn)
                throw new InvalidOperationException("Eingebaute Profile können nicht überschrieben werden.");

            if (string.IsNullOrWhiteSpace(profile.Id))
                profile.Id = CustomerProfileDraftSlug(profile.DisplayName);

            EnsureBuiltInProfiles();

            var profiles = LoadCustomProfiles()
                .Where(p => !string.Equals(p.Id, profile.Id, StringComparison.OrdinalIgnoreCase))
                .ToList();

            profiles.Add(profile);
            WriteAll(profiles);
        }

        public static bool Delete(string id)
        {
            if (string.IsNullOrWhiteSpace(id)
                || string.Equals(id, CustomerProfile.TeslaId, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            var profiles = LoadCustomProfiles()
                .Where(p => !string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (profiles.Count == LoadCustomProfiles().Count)
                return false;

            WriteAll(profiles);
            return true;
        }

        public static string CustomerProfileDraftSlug(string displayName)
        {
            return new CustomerProfileDraft().ToProfile(displayName).Id;
        }

        private static void EnsureBuiltInProfiles()
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

        private static CustomerProfile CreateBuiltInTesla()
        {
            return new CustomerProfile
            {
                Id = CustomerProfile.TeslaId,
                DisplayName = "Tesla",
                IsBuiltIn = true,
                WorkflowKind = CustomerWorkflowKind.ExcelPrimary,
                RequiresExcel = true,
                RequiresOrderPdf = false,
                DrawingNumberPattern = CustomerProfile.DefaultDrawingPattern,
                ArticleNumberPattern = @"\bTGA\d+\b",
                SampleDrawingNumbers = new List<string> { "TSL-1912-053", "TSL-11122-393" },
                ExcelDrawingHeaders = new List<string> { "Drawing", "Zeichnung" },
                ExcelAmountHeaders = new List<string> { "Qty", "Requested Amount", "Menge" },
                LearningSummary = "Standardprofil für Tesla (Excel + optional Bestell-PDF)."
            };
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
                new XElement("IsBuiltIn", profile.IsBuiltIn ? "true" : "false"),
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
