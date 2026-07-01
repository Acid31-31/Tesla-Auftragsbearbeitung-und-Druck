using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using PDF_umbenenen.Models;

namespace PDF_umbenenen.Services
{
    internal static class WatermarkLearningStore
    {
        private static readonly string StorePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "DOK-V01",
            "Learning",
            "watermark-placements.xml");

        public static string NormalizeDrawingKey(string drawingNumber)
        {
            if (string.IsNullOrWhiteSpace(drawingNumber))
                return string.Empty;

            return drawingNumber.Trim().ToUpperInvariant();
        }

        public static void SaveEntry(WatermarkLearningEntry entry)
        {
            if (entry == null || string.IsNullOrWhiteSpace(entry.DrawingKey))
                return;

            entry.DrawingKey = NormalizeDrawingKey(entry.DrawingKey);
            var document = LoadDocument();
            var entries = LoadEntries(document)
                .Where(e => !string.Equals(e.DrawingKey, entry.DrawingKey, StringComparison.OrdinalIgnoreCase))
                .ToList();
            entries.Add(entry);
            SaveDocument(entries, LoadCategoryDefaults(document));
        }

        public static void SaveCategoryDefaultsFromPlacements(
            IEnumerable<WatermarkTextPlacement> placements,
            float pageWidth,
            float pageHeight)
        {
            if (pageWidth <= 0 || pageHeight <= 0)
                return;

            var document = LoadDocument();
            var defaults = LoadCategoryDefaults(document)
                .ToDictionary(d => d.Category ?? string.Empty, StringComparer.OrdinalIgnoreCase);

            foreach (var placement in placements ?? Enumerable.Empty<WatermarkTextPlacement>())
            {
                if (placement == null || string.IsNullOrWhiteSpace(placement.Category))
                    continue;

                defaults[placement.Category] = new LearnedCategoryDefault
                {
                    Category = placement.Category,
                    RelativeX = placement.X / pageWidth,
                    RelativeY = placement.Y / pageHeight,
                    Alignment = placement.Alignment,
                    Angle = placement.Angle,
                    FontSize = placement.FontSize > 0 ? placement.FontSize : 22f,
                    UseRedText = placement.UseRedText
                };
            }

            SaveDocument(LoadEntries(document), defaults.Values.ToList());
        }

        public static WatermarkLearningEntry TryGet(string drawingNumber)
        {
            var key = NormalizeDrawingKey(drawingNumber);
            if (string.IsNullOrWhiteSpace(key))
                return null;

            return LoadEntries(LoadDocument())
                .FirstOrDefault(e => string.Equals(e.DrawingKey, key, StringComparison.OrdinalIgnoreCase));
        }

        public static HashSet<string> ApplyToPlacements(string drawingNumber, List<WatermarkTextPlacement> placements)
        {
            var appliedCategories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (placements == null || placements.Count == 0)
                return appliedCategories;

            var learned = TryGet(drawingNumber);
            if (learned == null || learned.Placements == null || learned.Placements.Count == 0)
                return appliedCategories;

            foreach (var learnedPlacement in learned.Placements)
            {
                if (string.IsNullOrWhiteSpace(learnedPlacement.Category))
                    continue;

                if (string.Equals(learnedPlacement.Category, "Quantity", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(learnedPlacement.Category, "Date", StringComparison.OrdinalIgnoreCase))
                    continue;

                var target = placements.FirstOrDefault(p =>
                    string.Equals(p.Category, learnedPlacement.Category, StringComparison.OrdinalIgnoreCase)
                    && (string.IsNullOrWhiteSpace(learnedPlacement.Text)
                        || string.Equals((p.Text ?? string.Empty).Trim(), learnedPlacement.Text.Trim(), StringComparison.OrdinalIgnoreCase)
                        || learnedPlacement.Category.Equals("Material", StringComparison.OrdinalIgnoreCase)));

                if (target == null && learnedPlacement.Category.Equals("Material", StringComparison.OrdinalIgnoreCase))
                {
                    target = placements.FirstOrDefault(p =>
                        p.Category != null && p.Category.Equals("Material", StringComparison.OrdinalIgnoreCase));
                }

                if (target == null)
                    continue;

                if (!string.IsNullOrWhiteSpace(learnedPlacement.Text))
                    target.Text = learnedPlacement.Text.Trim();

                target.X = learnedPlacement.X;
                target.Y = learnedPlacement.Y;
                target.Alignment = learnedPlacement.Alignment;
                target.Angle = learnedPlacement.Angle;
                target.FontSize = learnedPlacement.FontSize > 0 ? learnedPlacement.FontSize : target.FontSize;
                target.UseRedText = learnedPlacement.UseRedText;
                appliedCategories.Add(learnedPlacement.Category);
            }

            return appliedCategories;
        }

        public static void ApplyCategoryDefaults(
            List<WatermarkTextPlacement> placements,
            float pageWidth,
            float pageHeight,
            string drawingNumber = null)
        {
            if (placements == null || placements.Count == 0 || pageWidth <= 0 || pageHeight <= 0)
                return;

            var appliedCategories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var learned = TryGet(drawingNumber);
            if (learned?.Placements != null)
            {
                foreach (var placement in learned.Placements)
                {
                    if (!string.IsNullOrWhiteSpace(placement?.Category))
                        appliedCategories.Add(placement.Category);
                }
            }

            var defaults = LoadCategoryDefaults(LoadDocument());

            foreach (var placement in placements)
            {
                if (placement == null || string.IsNullOrWhiteSpace(placement.Category))
                    continue;

                if (appliedCategories.Contains(placement.Category))
                    continue;

                if (placement.Category.Equals("Quantity", StringComparison.OrdinalIgnoreCase)
                    || placement.Category.Equals("Date", StringComparison.OrdinalIgnoreCase))
                    continue;

                var categoryDefault = defaults.FirstOrDefault(d =>
                    string.Equals(d.Category, placement.Category, StringComparison.OrdinalIgnoreCase));

                if (categoryDefault == null)
                    continue;

                placement.X = categoryDefault.RelativeX * pageWidth;
                placement.Y = categoryDefault.RelativeY * pageHeight;
                placement.Alignment = categoryDefault.Alignment;
                placement.Angle = categoryDefault.Angle;
                if (categoryDefault.FontSize > 0)
                    placement.FontSize = categoryDefault.FontSize;
                placement.UseRedText = categoryDefault.UseRedText;
            }
        }

        public static WatermarkLearningEntry CreateFromPlacements(string drawingNumber, IEnumerable<WatermarkTextPlacement> placements)
        {
            var entry = new WatermarkLearningEntry
            {
                DrawingKey = NormalizeDrawingKey(drawingNumber),
                Placements = (placements ?? Enumerable.Empty<WatermarkTextPlacement>())
                    .Where(p => !string.IsNullOrWhiteSpace(p?.Category))
                    .Select(p => new LearnedWatermarkPlacement
                    {
                        Category = p.Category,
                        Text = p.Text ?? string.Empty,
                        X = p.X,
                        Y = p.Y,
                        Alignment = p.Alignment,
                        Angle = p.Angle,
                        FontSize = p.FontSize,
                        UseRedText = p.UseRedText
                    })
                    .ToList()
            };

            var material = entry.Placements.FirstOrDefault(p =>
                p.Category.Equals("Material", StringComparison.OrdinalIgnoreCase));
            if (material != null)
                entry.MaterialOverride = material.Text ?? string.Empty;

            return entry;
        }

        private static XDocument LoadDocument()
        {
            try
            {
                if (!File.Exists(StorePath))
                    return new XDocument(new XElement("WatermarkLearning"));

                return XDocument.Load(StorePath);
            }
            catch
            {
                return new XDocument(new XElement("WatermarkLearning"));
            }
        }

        private static List<WatermarkLearningEntry> LoadEntries(XDocument document)
        {
            var root = document?.Root;
            if (root == null)
                return new List<WatermarkLearningEntry>();

            return root.Elements("Entry")
                .Select(ReadEntry)
                .Where(e => e != null)
                .ToList();
        }

        private static List<LearnedCategoryDefault> LoadCategoryDefaults(XDocument document)
        {
            var root = document?.Root?.Element("CategoryDefaults");
            if (root == null)
                return new List<LearnedCategoryDefault>();

            return root.Elements("Placement")
                .Select(element => new LearnedCategoryDefault
                {
                    Category = element.Attribute("category")?.Value ?? string.Empty,
                    RelativeX = ParseFloat(element.Attribute("relativeX")?.Value),
                    RelativeY = ParseFloat(element.Attribute("relativeY")?.Value),
                    Alignment = ParseInt(element.Attribute("alignment")?.Value),
                    Angle = ParseFloat(element.Attribute("angle")?.Value),
                    FontSize = ParseFloat(element.Attribute("fontSize")?.Value, 22f),
                    UseRedText = string.Equals(element.Attribute("useRed")?.Value, "true", StringComparison.OrdinalIgnoreCase)
                })
                .Where(d => !string.IsNullOrWhiteSpace(d.Category))
                .ToList();
        }

        private static WatermarkLearningEntry ReadEntry(XElement element)
        {
            if (element == null)
                return null;

            var entry = new WatermarkLearningEntry
            {
                DrawingKey = element.Attribute("drawingKey")?.Value ?? string.Empty,
                CustomerProfileId = element.Attribute("customerProfileId")?.Value ?? string.Empty,
                MaterialOverride = element.Element("MaterialOverride")?.Value ?? string.Empty
            };

            entry.Placements = element.Elements("Placement")
                .Select(p => new LearnedWatermarkPlacement
                {
                    Category = p.Attribute("category")?.Value ?? string.Empty,
                    Text = p.Element("Text")?.Value ?? string.Empty,
                    X = ParseFloat(p.Attribute("x")?.Value),
                    Y = ParseFloat(p.Attribute("y")?.Value),
                    Alignment = ParseInt(p.Attribute("alignment")?.Value),
                    Angle = ParseFloat(p.Attribute("angle")?.Value),
                    FontSize = ParseFloat(p.Attribute("fontSize")?.Value, 22f),
                    UseRedText = string.Equals(p.Attribute("useRed")?.Value, "true", StringComparison.OrdinalIgnoreCase)
                })
                .ToList();

            return entry;
        }

        private static void SaveDocument(IEnumerable<WatermarkLearningEntry> entries, IEnumerable<LearnedCategoryDefault> categoryDefaults)
        {
            var directory = Path.GetDirectoryName(StorePath);
            if (!string.IsNullOrWhiteSpace(directory))
                Directory.CreateDirectory(directory);

            var root = new XElement("WatermarkLearning");
            var defaultsElement = new XElement("CategoryDefaults");

            foreach (var categoryDefault in categoryDefaults ?? Enumerable.Empty<LearnedCategoryDefault>())
            {
                if (categoryDefault == null || string.IsNullOrWhiteSpace(categoryDefault.Category))
                    continue;

                defaultsElement.Add(new XElement("Placement",
                    new XAttribute("category", categoryDefault.Category),
                    new XAttribute("relativeX", categoryDefault.RelativeX.ToString(CultureInfo.InvariantCulture)),
                    new XAttribute("relativeY", categoryDefault.RelativeY.ToString(CultureInfo.InvariantCulture)),
                    new XAttribute("alignment", categoryDefault.Alignment.ToString(CultureInfo.InvariantCulture)),
                    new XAttribute("angle", categoryDefault.Angle.ToString(CultureInfo.InvariantCulture)),
                    new XAttribute("fontSize", categoryDefault.FontSize.ToString(CultureInfo.InvariantCulture)),
                    new XAttribute("useRed", categoryDefault.UseRedText ? "true" : "false")));
            }

            root.Add(defaultsElement);

            foreach (var entry in entries ?? Enumerable.Empty<WatermarkLearningEntry>())
            {
                if (entry == null || string.IsNullOrWhiteSpace(entry.DrawingKey))
                    continue;

                var element = new XElement("Entry",
                    new XAttribute("drawingKey", entry.DrawingKey),
                    new XAttribute("customerProfileId", entry.CustomerProfileId ?? string.Empty),
                    string.IsNullOrWhiteSpace(entry.MaterialOverride)
                        ? null
                        : new XElement("MaterialOverride", entry.MaterialOverride));

                foreach (var placement in entry.Placements ?? new List<LearnedWatermarkPlacement>())
                {
                    element.Add(new XElement("Placement",
                        new XAttribute("category", placement.Category ?? string.Empty),
                        new XAttribute("x", placement.X.ToString(CultureInfo.InvariantCulture)),
                        new XAttribute("y", placement.Y.ToString(CultureInfo.InvariantCulture)),
                        new XAttribute("alignment", placement.Alignment.ToString(CultureInfo.InvariantCulture)),
                        new XAttribute("angle", placement.Angle.ToString(CultureInfo.InvariantCulture)),
                        new XAttribute("fontSize", placement.FontSize.ToString(CultureInfo.InvariantCulture)),
                        new XAttribute("useRed", placement.UseRedText ? "true" : "false"),
                        new XElement("Text", placement.Text ?? string.Empty)));
                }

                root.Add(element);
            }

            new XDocument(root).Save(StorePath);
        }

        private static float ParseFloat(string value, float fallback = 0f)
        {
            return float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var result)
                ? result
                : fallback;
        }

        private static int ParseInt(string value, int fallback = 0)
        {
            return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result)
                ? result
                : fallback;
        }
    }
}
