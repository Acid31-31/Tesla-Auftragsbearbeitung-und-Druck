using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using PDF_umbenenen.Models;

namespace PDF_umbenenen.Services
{
    internal static class WatermarkMetadataStore
    {
        public static string GetMetaPath(string pdfPath)
        {
            return pdfPath + ".dok-meta.xml";
        }

        public static void Save(string pdfPath, string drawingNumber, string encodedWatermarks)
        {
            if (string.IsNullOrWhiteSpace(pdfPath))
                return;

            List<WatermarkTextPlacement> existingPlacements = null;
            TryLoadPlacements(pdfPath, out _, out _, out existingPlacements);

            Save(pdfPath, drawingNumber, encodedWatermarks, existingPlacements);
        }

        public static void Save(
            string pdfPath,
            string drawingNumber,
            string encodedWatermarks,
            IEnumerable<WatermarkTextPlacement> placements)
        {
            if (string.IsNullOrWhiteSpace(pdfPath))
                return;

            var root = new XElement("WatermarkMeta",
                new XElement("DrawingNumber", drawingNumber ?? string.Empty),
                new XElement("EncodedWatermarks", encodedWatermarks ?? string.Empty),
                new XElement("SavedUtc", DateTime.UtcNow.ToString("o")));

            if (placements != null)
            {
                var placementElements = placements
                    .Where(p => p != null && !string.IsNullOrWhiteSpace(p.Text))
                    .Select(p => new XElement("Placement",
                        new XAttribute("category", p.Category ?? string.Empty),
                        new XAttribute("text", p.Text ?? string.Empty),
                        new XAttribute("x", p.X.ToString(CultureInfo.InvariantCulture)),
                        new XAttribute("y", p.Y.ToString(CultureInfo.InvariantCulture)),
                        new XAttribute("alignment", p.Alignment.ToString(CultureInfo.InvariantCulture)),
                        new XAttribute("fontSize", (p.FontSize > 0 ? p.FontSize : 22f).ToString(CultureInfo.InvariantCulture)),
                        new XAttribute("angle", p.Angle.ToString(CultureInfo.InvariantCulture)),
                        new XAttribute("useRedText", p.UseRedText.ToString())));

                root.Add(new XElement("Placements", placementElements));
            }

            new XDocument(root).Save(GetMetaPath(pdfPath));
        }

        public static bool TryLoad(string pdfPath, out string drawingNumber, out string encodedWatermarks)
        {
            return TryLoadPlacements(pdfPath, out drawingNumber, out encodedWatermarks, out _);
        }

        public static bool TryLoadPlacements(
            string pdfPath,
            out string drawingNumber,
            out string encodedWatermarks,
            out List<WatermarkTextPlacement> placements)
        {
            drawingNumber = string.Empty;
            encodedWatermarks = string.Empty;
            placements = new List<WatermarkTextPlacement>();

            var metaPath = GetMetaPath(pdfPath);
            if (!File.Exists(metaPath))
                return false;

            try
            {
                var root = XDocument.Load(metaPath).Root;
                if (root == null)
                    return false;

                drawingNumber = root.Element("DrawingNumber")?.Value ?? string.Empty;
                encodedWatermarks = root.Element("EncodedWatermarks")?.Value ?? string.Empty;

                var placementRoot = root.Element("Placements");
                if (placementRoot != null)
                {
                    foreach (var element in placementRoot.Elements("Placement"))
                    {
                        placements.Add(new WatermarkTextPlacement
                        {
                            Category = element.Attribute("category")?.Value ?? string.Empty,
                            Text = element.Attribute("text")?.Value ?? string.Empty,
                            X = ParseFloat(element.Attribute("x")?.Value, 0f),
                            Y = ParseFloat(element.Attribute("y")?.Value, 0f),
                            Alignment = ParseInt(element.Attribute("alignment")?.Value, 0),
                            FontSize = ParseFloat(element.Attribute("fontSize")?.Value, 22f),
                            Angle = ParseFloat(element.Attribute("angle")?.Value, 0f),
                            UseRedText = string.Equals(
                                element.Attribute("useRedText")?.Value,
                                "true",
                                StringComparison.OrdinalIgnoreCase)
                        });
                    }
                }

                return true;
            }
            catch
            {
                placements = new List<WatermarkTextPlacement>();
                return false;
            }
        }

        internal static bool IsVolatilePlacementCategory(string category)
        {
            if (string.IsNullOrWhiteSpace(category))
                return false;

            return string.Equals(category, "Material", StringComparison.OrdinalIgnoreCase)
                   || string.Equals(category, "PowderCoating", StringComparison.OrdinalIgnoreCase)
                   || string.Equals(category, "Anodizing", StringComparison.OrdinalIgnoreCase)
                   || string.Equals(category, "NickelPlating", StringComparison.OrdinalIgnoreCase)
                   || string.Equals(category, "SurfaceTreatment", StringComparison.OrdinalIgnoreCase)
                   || string.Equals(category, "Brushed", StringComparison.OrdinalIgnoreCase);
        }

        private static float ParseFloat(string value, float fallback)
        {
            return float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
                ? parsed
                : fallback;
        }

        private static int ParseInt(string value, int fallback)
        {
            return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
                ? parsed
                : fallback;
        }
    }
}
