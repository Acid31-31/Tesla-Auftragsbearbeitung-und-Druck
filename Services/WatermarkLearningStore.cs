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
        private static string StorePath => Path.Combine(
            AppInfo.UserDataDirectory,
            "Learning",
            "watermark-placements.xml");

        public static string NormalizeDrawingKey(string drawingNumber)
        {
            return OrderVerificationDrawingKey.Normalize(drawingNumber);
        }

        public static bool ShouldSuppressAutoMaterial(string drawingNumber)
        {
            var marker = (TryGet(drawingNumber)?.MaterialOverride ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(marker))
                return false;

            return IsMaterialSuppressMarker(marker);
        }

        /// <summary>
        /// Manuell korrigierter Material-/Stärke-Text je Zeichnungsnummer (aus Wasserzeichen-Editor).
        /// Hat Vorrang vor OCR/Regel-Erkennung beim nächsten Sortieren.
        /// </summary>
        public static bool TryGetMaterialOverrideText(string drawingNumber, out string materialText)
        {
            materialText = null;
            var marker = (TryGet(drawingNumber)?.MaterialOverride ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(marker) || IsMaterialSuppressMarker(marker))
                return false;

            materialText = marker;
            return true;
        }

        private static bool IsMaterialSuppressMarker(string marker)
        {
            return string.Equals(marker, "-", StringComparison.OrdinalIgnoreCase)
                   || string.Equals(marker, "none", StringComparison.OrdinalIgnoreCase)
                   || string.Equals(marker, "off", StringComparison.OrdinalIgnoreCase)
                   || string.Equals(marker, "aus", StringComparison.OrdinalIgnoreCase);
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
            float pageHeight,
            bool includeLayoutCategories = false)
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

                if (IsExcludedFromCategoryDefaults(placement.Category, includeLayoutCategories))
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

        public static bool HasCategoryDefault(string category)
        {
            if (string.IsNullOrWhiteSpace(category))
                return false;

            return LoadCategoryDefaults(LoadDocument()).Any(d =>
                d != null
                && string.Equals(d.Category, category, StringComparison.OrdinalIgnoreCase));
        }

        public static bool HasLearnedPlacementForCategory(string drawingNumber, string category)
        {
            if (string.IsNullOrWhiteSpace(category))
                return false;

            var learned = TryGet(drawingNumber);
            return learned?.Placements?.Any(p =>
                p != null
                && string.Equals(p.Category, category, StringComparison.OrdinalIgnoreCase)) == true;
        }

        public static void ApplyLearnedDrawingCorrections(
            string drawingNumber,
            List<WatermarkTextPlacement> placements,
            string pdfPath,
            int pageNumber,
            float pageWidth,
            float pageHeight)
        {
            if (ShouldSkipLearningAfterPlacementError || placements == null)
                return;

            var learned = TryGet(drawingNumber);
            if (learned?.Placements == null || learned.Placements.Count == 0)
                return;

            foreach (var learnedPlacement in learned.Placements)
            {
                if (learnedPlacement == null || string.IsNullOrWhiteSpace(learnedPlacement.Category))
                    continue;

                if (IsFixedTopBandCategory(learnedPlacement.Category))
                    continue;

                if (string.Equals(learnedPlacement.Category, "PowderCoating", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(learnedPlacement.Category, "Anodizing", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(learnedPlacement.Category, "NickelPlating", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(learnedPlacement.Category, "SurfaceTreatment", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(learnedPlacement.Category, "Brushed", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(learnedPlacement.Category, "Material", StringComparison.OrdinalIgnoreCase))
                    continue;

                var target = placements.FirstOrDefault(p =>
                    string.Equals(p.Category, learnedPlacement.Category, StringComparison.OrdinalIgnoreCase)
                    && !string.IsNullOrWhiteSpace(learnedPlacement.Text)
                    && string.Equals((p.Text ?? string.Empty).Trim(), learnedPlacement.Text.Trim(), StringComparison.OrdinalIgnoreCase));

                if (target == null)
                {
                    target = placements.FirstOrDefault(p =>
                        string.Equals(p.Category, learnedPlacement.Category, StringComparison.OrdinalIgnoreCase));
                }

                if (target == null)
                {
                    target = new WatermarkTextPlacement { Category = learnedPlacement.Category };
                    placements.Add(target);
                }

                if (!string.IsNullOrWhiteSpace(learnedPlacement.Text))
                    target.Text = learnedPlacement.Text.Trim();

                // Manuelle Korrekturen immer übernehmen (relativ bevorzugt, sonst absolut).
                // Keine Raster-Validierung: die hat gelernte Positionen nach Neu-Sortierung verworfen.
                ResolveLearnedCoordinates(
                    learnedPlacement,
                    pageWidth,
                    pageHeight,
                    out var applyX,
                    out var applyY);

                if (pageWidth > 0f)
                    applyX = Math.Max(0f, Math.Min(applyX, pageWidth));
                if (pageHeight > 0f)
                    applyY = Math.Max(0f, Math.Min(applyY, pageHeight));

                target.X = applyX;
                target.Y = applyY;
                target.Alignment = learnedPlacement.Alignment;
                target.Angle = learnedPlacement.Angle;
                if (learnedPlacement.FontSize > 0f)
                    target.FontSize = learnedPlacement.FontSize;
                target.UseRedText = learnedPlacement.UseRedText;
            }
        }

        private static void ResolveLearnedCoordinates(
            LearnedWatermarkPlacement learnedPlacement,
            float pageWidth,
            float pageHeight,
            out float x,
            out float y)
        {
            x = learnedPlacement.X;
            y = learnedPlacement.Y;

            if (pageWidth > 0f
                && pageHeight > 0f
                && (learnedPlacement.RelativeX > 0f || learnedPlacement.RelativeY > 0f)
                && learnedPlacement.RelativeX <= 1.05f
                && learnedPlacement.RelativeY <= 1.05f)
            {
                x = learnedPlacement.RelativeX * pageWidth;
                y = learnedPlacement.RelativeY * pageHeight;
            }
        }

        public static bool HasLearnedMaterialPlacement(string drawingNumber)
        {
            return HasLearnedPlacementForCategory(drawingNumber, "Material");
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

            if (ShouldSkipLearningAfterPlacementError)
                return appliedCategories;

            var learned = TryGet(drawingNumber);
            if (learned == null || learned.Placements == null || learned.Placements.Count == 0)
                return appliedCategories;

            foreach (var learnedPlacement in learned.Placements)
            {
                if (string.IsNullOrWhiteSpace(learnedPlacement.Category))
                    continue;

                if (string.Equals(learnedPlacement.Category, "Quantity", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(learnedPlacement.Category, "Date", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(learnedPlacement.Category, "Order", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(learnedPlacement.Category, "Location", StringComparison.OrdinalIgnoreCase))
                    continue;

                if (string.Equals(learnedPlacement.Category, "Material", StringComparison.OrdinalIgnoreCase))
                    continue;

                if (string.Equals(learnedPlacement.Category, "PowderCoating", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(learnedPlacement.Category, "Anodizing", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(learnedPlacement.Category, "NickelPlating", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(learnedPlacement.Category, "SurfaceTreatment", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(learnedPlacement.Category, "Brushed", StringComparison.OrdinalIgnoreCase))
                    continue;

                var target = placements.FirstOrDefault(p =>
                    string.Equals(p.Category, learnedPlacement.Category, StringComparison.OrdinalIgnoreCase)
                    && (string.IsNullOrWhiteSpace(learnedPlacement.Text)
                        || string.Equals((p.Text ?? string.Empty).Trim(), learnedPlacement.Text.Trim(), StringComparison.OrdinalIgnoreCase)));

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
            string drawingNumber = null,
            bool applyLayoutCategories = false)
        {
            if (placements == null || placements.Count == 0 || pageWidth <= 0 || pageHeight <= 0)
                return;

            if (ShouldSkipLearningAfterPlacementError)
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

            foreach (var categoryDefault in defaults)
            {
                if (categoryDefault == null || string.IsNullOrWhiteSpace(categoryDefault.Category))
                    continue;

                var isLayoutCategory = IsLayoutOnlyCategory(categoryDefault.Category)
                    || string.Equals(categoryDefault.Category, "Bg", StringComparison.OrdinalIgnoreCase);

                // Im Editor sollen Session-Defaults (Material/RAL/Elox/BG) immer greifen;
                // zeichnungsspezifische Korrekturen kommen danach via ApplyLearnedDrawingCorrections.
                if (!applyLayoutCategories || !isLayoutCategory)
                {
                    if (appliedCategories.Contains(categoryDefault.Category))
                        continue;
                }

                if (IsFixedTopBandCategory(categoryDefault.Category))
                    continue;

                if (string.Equals(categoryDefault.Category, "PowderCoating", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(categoryDefault.Category, "Anodizing", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(categoryDefault.Category, "NickelPlating", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(categoryDefault.Category, "SurfaceTreatment", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(categoryDefault.Category, "Brushed", StringComparison.OrdinalIgnoreCase))
                    continue;

                if (!applyLayoutCategories && IsLayoutOnlyCategory(categoryDefault.Category))
                    continue;

                var target = placements.FirstOrDefault(p =>
                    p != null
                    && string.Equals(p.Category, categoryDefault.Category, StringComparison.OrdinalIgnoreCase));

                if (target == null && IsLayoutOnlyCategory(categoryDefault.Category))
                {
                    target = new WatermarkTextPlacement
                    {
                        Category = categoryDefault.Category,
                        FontSize = categoryDefault.FontSize > 0 ? categoryDefault.FontSize : 22f,
                        Alignment = categoryDefault.Alignment
                    };
                    placements.Add(target);
                }

                if (target == null)
                    continue;

                ApplyLayoutFromCategoryDefault(target, categoryDefault, pageWidth, pageHeight);
            }
        }

        private static void ApplyLayoutFromCategoryDefault(
            WatermarkTextPlacement target,
            LearnedCategoryDefault categoryDefault,
            float pageWidth,
            float pageHeight)
        {
            if (target == null || categoryDefault == null)
                return;

            target.X = categoryDefault.RelativeX * pageWidth;
            target.Y = categoryDefault.RelativeY * pageHeight;
            target.Alignment = categoryDefault.Alignment;
            target.Angle = categoryDefault.Angle;
            if (categoryDefault.FontSize > 0)
                target.FontSize = categoryDefault.FontSize;

            if (!IsLayoutOnlyCategory(categoryDefault.Category))
                target.UseRedText = categoryDefault.UseRedText;
        }

        public static void PruneInvalidCategoryDefaults()
        {
            var document = LoadDocument();
            var defaults = LoadCategoryDefaults(document)
                .Where(d => d != null && !string.IsNullOrWhiteSpace(d.Category))
                .Where(d =>
                {
                    if (string.Equals(d.Category, "Order", StringComparison.OrdinalIgnoreCase))
                        return true;

                    var relX = d.RelativeX;
                    var relY = d.RelativeY;
                    if (relX > 0.17f && relX < 0.72f && relY > 0.18f && relY < 0.72f)
                        return false;

                    return true;
                })
                .ToList();

            SaveDocument(LoadEntries(document), defaults);
        }

        /// <summary>
        /// Nach Platzierungsfehler: Lernspeicher leeren und Learning für den laufenden Batch deaktivieren.
        /// </summary>
        public static bool ShouldSkipLearningAfterPlacementError { get; private set; }

        public static bool WasClearedThisBatch { get; private set; }

        public static void ResetBatchLearningState()
        {
            ShouldSkipLearningAfterPlacementError = false;
            WasClearedThisBatch = false;
        }

        public static void ClearOnPlacementError(string context)
        {
            try
            {
                // Lernspeicher NICHT mehr komplett löschen – sonst sind alle manuellen
                // Korrekturen nach einem einzelnen Material-Fehler weg.
                ShouldSkipLearningAfterPlacementError = true;
                WasClearedThisBatch = true;

                var dir = Path.Combine(AppInfo.UserDataDirectory, "Diagnostics");
                Directory.CreateDirectory(dir);

                var line = string.Format(
                    CultureInfo.InvariantCulture,
                    "{0:yyyy-MM-dd HH:mm:ss} | learning-skip-enabled | {1}{2}",
                    DateTime.Now,
                    string.IsNullOrWhiteSpace(context) ? "placement-error" : context.Trim(),
                    Environment.NewLine);

                File.AppendAllText(Path.Combine(dir, "material-placement.log"), line);
            }
            catch
            {
                // Lernspeicher darf Sortierung nicht blockieren.
            }
        }

        public static WatermarkLearningEntry CreateFromPlacements(
            string drawingNumber,
            IEnumerable<WatermarkTextPlacement> placements,
            float pageWidth = 0f,
            float pageHeight = 0f)
        {
            var source = placements ?? Enumerable.Empty<WatermarkTextPlacement>();
            var entry = new WatermarkLearningEntry
            {
                DrawingKey = NormalizeDrawingKey(drawingNumber),
                Placements = source
                    .Where(p => !string.IsNullOrWhiteSpace(p?.Category))
                    .Where(p => !IsFixedTopBandCategory(p.Category))
                    .Select(p =>
                    {
                        var learned = new LearnedWatermarkPlacement
                        {
                            Category = p.Category,
                            Text = p.Text ?? string.Empty,
                            X = p.X,
                            Y = p.Y,
                            Alignment = p.Alignment,
                            Angle = p.Angle,
                            FontSize = p.FontSize,
                            UseRedText = p.UseRedText
                        };

                        if (pageWidth > 0f && pageHeight > 0f)
                        {
                            learned.RelativeX = p.X / pageWidth;
                            learned.RelativeY = p.Y / pageHeight;
                        }

                        return learned;
                    })
                    .ToList()
            };

            var materialText = source
                .FirstOrDefault(p => string.Equals(p?.Category, "Material", StringComparison.OrdinalIgnoreCase))
                ?.Text;
            if (!string.IsNullOrWhiteSpace(materialText))
                entry.MaterialOverride = materialText.Trim();

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
                // Layout-Kategorien (Material/RAL/Elox) müssen ladbar bleiben –
                // Anwendung steuert ApplyCategoryDefaults(applyLayoutCategories).
                .Where(d => !IsFixedTopBandCategory(d.Category))
                .ToList();
        }

        private static bool IsFixedTopBandCategory(string category)
        {
            if (string.IsNullOrWhiteSpace(category))
                return true;

            return string.Equals(category, "Quantity", StringComparison.OrdinalIgnoreCase)
                   || string.Equals(category, "Date", StringComparison.OrdinalIgnoreCase)
                   || string.Equals(category, "Order", StringComparison.OrdinalIgnoreCase)
                   || string.Equals(category, "Location", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsExcludedFromCategoryDefaults(string category, bool includeLayoutCategories = false)
        {
            if (IsFixedTopBandCategory(category))
                return true;

            // Material: feste Unterkante wie Seitenzahl – keine gelernten Y-Koordinaten,
            // wenn der Nutzer „Unten links“ gewählt hat (wird separat erzwungen).
            if (string.Equals(category, "PowderCoating", StringComparison.OrdinalIgnoreCase)
                || string.Equals(category, "Anodizing", StringComparison.OrdinalIgnoreCase)
                || string.Equals(category, "NickelPlating", StringComparison.OrdinalIgnoreCase)
                || string.Equals(category, "SurfaceTreatment", StringComparison.OrdinalIgnoreCase)
                || string.Equals(category, "Brushed", StringComparison.OrdinalIgnoreCase))
                return true;

            if (includeLayoutCategories)
                return false;

            return IsLayoutOnlyCategory(category);
        }

        private static bool IsLayoutOnlyCategory(string category)
        {
            return string.Equals(category, "Material", StringComparison.OrdinalIgnoreCase)
                   || string.Equals(category, "PowderCoating", StringComparison.OrdinalIgnoreCase)
                   || string.Equals(category, "Anodizing", StringComparison.OrdinalIgnoreCase)
                   || string.Equals(category, "NickelPlating", StringComparison.OrdinalIgnoreCase)
                   || string.Equals(category, "SurfaceTreatment", StringComparison.OrdinalIgnoreCase)
                   || string.Equals(category, "Brushed", StringComparison.OrdinalIgnoreCase);
        }

        public static void PurgeNonLearnableCategoryDefaults()
        {
            var document = LoadDocument();
            var defaults = LoadCategoryDefaults(document);
            if (defaults.Count == 0)
                return;

            SaveDocument(LoadEntries(document), defaults);
        }

        public static void PurgeLearnedMaterialCoordinates()
        {
            // Früher wurden gelernte Material-Koordinaten beim Start entfernt.
            // Manuelle Korrekturen bleiben seit R44 erhalten.
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
                    RelativeX = ParseFloat(p.Attribute("relativeX")?.Value),
                    RelativeY = ParseFloat(p.Attribute("relativeY")?.Value),
                    Alignment = ParseInt(p.Attribute("alignment")?.Value),
                    Angle = ParseFloat(p.Attribute("angle")?.Value),
                    FontSize = ParseFloat(p.Attribute("fontSize")?.Value, 22f),
                    UseRedText = string.Equals(p.Attribute("useRed")?.Value, "true", StringComparison.OrdinalIgnoreCase)
                })
                .Where(p => !IsFixedTopBandCategory(p.Category))
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
                        new XAttribute("relativeX", placement.RelativeX.ToString(CultureInfo.InvariantCulture)),
                        new XAttribute("relativeY", placement.RelativeY.ToString(CultureInfo.InvariantCulture)),
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
