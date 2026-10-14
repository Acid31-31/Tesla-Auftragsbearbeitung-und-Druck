using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using PDF_umbenenen.Models;

namespace PDF_umbenenen.Services
{
    internal static class WatermarkEditorService
    {
        public const string PreWatermarkSuffix = ".prewm.pdf";

        private static readonly Regex MaterialWatermarkRegex = new Regex(
            @"^(ST|VA|ALU)-\d+[.,]\d+\s*mm$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

        private static readonly Regex QuantityWatermarkRegex = new Regex(
            @"^\d+X$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

        private static readonly Regex PageNumberRegex = new Regex(
            @"^Seite\s*-?\s*\d+$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

        private static readonly Regex DateQuantityRegex = new Regex(
            @"^(?:(?:LT|FT)-)?\d{1,2}\.\d{1,2}\.\d{4}",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

        public static string GetPreWatermarkPath(string pdfPath) => pdfPath + PreWatermarkSuffix;

        public static void EnsurePreWatermarkBackup(string pdfPath)
        {
            if (string.IsNullOrWhiteSpace(pdfPath) || !File.Exists(pdfPath))
                return;

            var backupPath = GetPreWatermarkPath(pdfPath);
            if (!File.Exists(backupPath))
                File.Copy(pdfPath, backupPath, false);
        }

        public static void MoveSidecars(string oldPdfPath, string newPdfPath)
        {
            if (string.IsNullOrWhiteSpace(oldPdfPath) || string.IsNullOrWhiteSpace(newPdfPath))
                return;

            TryMoveSidecar(GetPreWatermarkPath(oldPdfPath), GetPreWatermarkPath(newPdfPath));
            TryMoveSidecar(WatermarkMetadataStore.GetMetaPath(oldPdfPath), WatermarkMetadataStore.GetMetaPath(newPdfPath));
        }

        private static void TryMoveSidecar(string sourcePath, string targetPath)
        {
            if (!File.Exists(sourcePath))
                return;

            var targetDir = Path.GetDirectoryName(targetPath);
            if (!string.IsNullOrWhiteSpace(targetDir))
                Directory.CreateDirectory(targetDir);

            if (File.Exists(targetPath))
                File.Delete(targetPath);

            File.Move(sourcePath, targetPath);
        }

        public static List<WatermarkEditorItem> LoadItems(IEnumerable<string> pdfPaths)
        {
            var items = new List<WatermarkEditorItem>();
            foreach (var pdfPath in pdfPaths ?? Enumerable.Empty<string>())
            {
                if (string.IsNullOrWhiteSpace(pdfPath) || !File.Exists(pdfPath))
                    continue;

                var preWatermarkPath = GetPreWatermarkPath(pdfPath);
                if (!File.Exists(preWatermarkPath))
                    EnsurePreWatermarkBackup(pdfPath);

                if (!WatermarkMetadataStore.TryLoad(pdfPath, out var drawingNumber, out var encodedWatermarks))
                {
                    drawingNumber = GuessDrawingNumberFromFileName(pdfPath);
                    encodedWatermarks = string.Empty;
                }

                if (!PdfWatermarkService.TryGetPageSize(
                        File.Exists(preWatermarkPath) ? preWatermarkPath : pdfPath,
                        1,
                        out var pageWidth,
                        out var pageHeight))
                {
                    pageWidth = 595f;
                    pageHeight = 842f;
                }

                items.Add(new WatermarkEditorItem
                {
                    PdfPath = pdfPath,
                    PreWatermarkPath = File.Exists(preWatermarkPath) ? preWatermarkPath : pdfPath,
                    DrawingNumber = drawingNumber,
                    EncodedWatermarks = encodedWatermarks,
                    PageWidth = pageWidth,
                    PageHeight = pageHeight
                });
            }

            return items;
        }

        public static List<WatermarkTextPlacement> BuildPlacements(WatermarkEditorItem item, WatermarkSettings settings)
        {
            return BuildPlacements(item, settings, editorMode: false);
        }

        public static List<WatermarkTextPlacement> BuildPlacementsForEditor(
            WatermarkEditorItem item,
            WatermarkSettings settings)
        {
            return BuildPlacements(item, settings, editorMode: true);
        }

        private static List<WatermarkTextPlacement> BuildPlacements(
            WatermarkEditorItem item,
            WatermarkSettings settings,
            bool editorMode)
        {
            if (item == null)
                return new List<WatermarkTextPlacement>();

            settings = settings ?? WatermarkSettingsStore.Load();
            if (!string.IsNullOrWhiteSpace(item.DrawingNumber))
                WatermarkProcessingContext.CurrentDrawingKey = OrderVerificationDrawingKey.Normalize(item.DrawingNumber);

            List<WatermarkTextPlacement> placements;
            if (WatermarkMetadataStore.TryLoadPlacements(
                    item.PdfPath,
                    out _,
                    out _,
                    out var savedPlacements)
                && savedPlacements != null
                && savedPlacements.Count > 0)
            {
                placements = ClonePlacements(savedPlacements);
            }
            else if (editorMode)
            {
                placements = TryExtractPlacementsFromWatermarkedPdf(item.PdfPath);
                if (placements.Count == 0)
                    placements = BuildFallbackEditorPlacements(item, settings);
            }
            else
            {
                var entries = PdfWatermarkService.ParseWatermarkEntries(item.EncodedWatermarks);
                var orderNumbers = entries
                    .Where(e => !string.IsNullOrWhiteSpace(e.orderNumber))
                    .Select(e => e.orderNumber.Trim())
                    .Distinct()
                    .ToList();

                var fieldData = SmartWatermarkLayoutService.BuildFieldData(entries, orderNumbers, item.DrawingNumber);
                try
                {
                    WatermarkProcessingContext.CurrentDrawingKey =
                        OrderVerificationDrawingKey.Normalize(item.DrawingNumber);
                    placements = SmartWatermarkLayoutService.Compute(
                        item.PreWatermarkPath,
                        1,
                        fieldData,
                        item.DrawingNumber,
                        item.PageWidth,
                        item.PageHeight,
                        settings);
                }
                finally
                {
                    WatermarkProcessingContext.CurrentDrawingKey = string.Empty;
                }
            }

            var extractedFromPdf = TryExtractPlacementsFromWatermarkedPdf(item.PdfPath);
            // Keine Rand-Neuskalierung im Editor – sonst wird die Zeichnung bei jeder Bearbeitung kleiner.
            ApplyExistingMetricsFromExtractedPlacements(extractedFromPdf, placements);
            EnsureMaterialPlacement(item, placements, settings, editorMode, extractedFromPdf);
            SmartWatermarkLayoutService.PreserveMaterialFontSizes(placements, item.PageWidth, item.PageHeight);
            EnsureSurfaceTreatmentPlacements(item, placements, settings, editorMode, extractedFromPdf);
            SmartWatermarkLayoutService.EnforceFixedTopBandPlacements(
                placements,
                item.PageWidth,
                item.PageHeight,
                item.PreWatermarkPath,
                1,
                preserveTermPositions: true);
            SmartWatermarkLayoutService.EnforceSurfaceTreatmentPlacements(
                placements,
                item.PreWatermarkPath,
                1,
                item.PageWidth,
                item.PageHeight);
            SmartWatermarkLayoutService.UnifyMaterialAndSurfaceFontSizes(placements, item.PageWidth, item.PageHeight);
            SmartWatermarkLayoutService.EnforceFixedBottomLeftMaterialPlacements(
                placements,
                item.PreWatermarkPath,
                1,
                item.PageWidth,
                item.PageHeight,
                settings);
            SmartWatermarkLayoutService.EnforceFixedRightVerticalBgPlacements(
                placements,
                item.PreWatermarkPath,
                1,
                item.PageWidth,
                item.PageHeight,
                settings);
            if (editorMode)
            {
                WatermarkLearningStore.ApplyCategoryDefaults(
                    placements,
                    item.PageWidth,
                    item.PageHeight,
                    item.DrawingNumber,
                    applyLayoutCategories: true);
                // RAL/Elox bleiben am unteren Blattrand – Learning darf sie nicht nach oben ziehen.
                SmartWatermarkLayoutService.EnforceSurfaceTreatmentPlacements(
                    placements,
                    item.PreWatermarkPath,
                    1,
                    item.PageWidth,
                    item.PageHeight);
                SmartWatermarkLayoutService.EnforceFixedBottomLeftMaterialPlacements(
                    placements,
                    item.PreWatermarkPath,
                    1,
                    item.PageWidth,
                    item.PageHeight,
                    settings);
                SmartWatermarkLayoutService.EnforceFixedRightVerticalBgPlacements(
                    placements,
                    item.PreWatermarkPath,
                    1,
                    item.PageWidth,
                    item.PageHeight,
                    settings);
            }

            WatermarkLearningStore.ApplyLearnedDrawingCorrections(
                item.DrawingNumber,
                placements,
                item.PreWatermarkPath,
                1,
                item.PageWidth,
                item.PageHeight);
            SmartWatermarkLayoutService.EnforceFixedBottomLeftMaterialPlacements(
                placements,
                item.PreWatermarkPath,
                1,
                item.PageWidth,
                item.PageHeight,
                settings);
            SmartWatermarkLayoutService.EnforceFixedRightVerticalBgPlacements(
                placements,
                item.PreWatermarkPath,
                1,
                item.PageWidth,
                item.PageHeight,
                settings);
            return placements;
        }

        private static List<WatermarkTextPlacement> BuildFallbackEditorPlacements(
            WatermarkEditorItem item,
            WatermarkSettings settings)
        {
            var entries = PdfWatermarkService.ParseWatermarkEntries(item.EncodedWatermarks);
            var orderNumbers = entries
                .Where(e => !string.IsNullOrWhiteSpace(e.orderNumber))
                .Select(e => e.orderNumber.Trim())
                .Distinct()
                .ToList();

            var fieldData = SmartWatermarkLayoutService.BuildFieldData(entries, orderNumbers, item.DrawingNumber);
            try
            {
                WatermarkProcessingContext.BatchMode = true;
                WatermarkProcessingContext.CurrentDrawingKey =
                    OrderVerificationDrawingKey.Normalize(item.DrawingNumber);
                return SmartWatermarkLayoutService.Compute(
                    item.PreWatermarkPath,
                    1,
                    fieldData,
                    item.DrawingNumber,
                    item.PageWidth,
                    item.PageHeight,
                    settings);
            }
            finally
            {
                WatermarkProcessingContext.BatchMode = false;
                WatermarkProcessingContext.CurrentDrawingKey = string.Empty;
            }
        }

        public static void SaveLearningFromPlacements(
            WatermarkEditorItem item,
            IList<WatermarkTextPlacement> placements)
        {
            if (item == null)
                return;

            var placementList = NormalizePlacementsForSave(placements)?.ToList()
                ?? new List<WatermarkTextPlacement>();

            WatermarkLearningStore.SaveCategoryDefaultsFromPlacements(
                placementList,
                item.PageWidth,
                item.PageHeight,
                includeLayoutCategories: true);

            var learningEntry = WatermarkLearningStore.CreateFromPlacements(
                item.DrawingNumber,
                placementList,
                item.PageWidth,
                item.PageHeight);
            WatermarkLearningStore.SaveEntry(learningEntry);
        }

        public static void SaveItem(
            WatermarkEditorItem item,
            IList<WatermarkTextPlacement> placements,
            WatermarkSettings settings,
            IEnumerable<string> orderedPdfPathsForPageNumbers = null,
            bool keepEditorPlacements = false)
        {
            if (item == null)
                throw new ArgumentNullException(nameof(item));

            settings = settings ?? WatermarkSettingsStore.Load();
            placements = NormalizePlacementsForSave(placements);
            var placementList = placements?.ToList() ?? new List<WatermarkTextPlacement>();

            if (keepEditorPlacements)
            {
                var previous = LoadExistingPlacements(item);
                PinSurfaceTreatmentPositions(placementList, previous);
                PdfWatermarkService.ReplaceMaterialTextOnExistingPdf(
                    item.PdfPath,
                    previous,
                    placementList);

                WatermarkMetadataStore.Save(
                    item.PdfPath,
                    item.DrawingNumber,
                    item.EncodedWatermarks,
                    placementList);

                var materialLearning = WatermarkLearningStore.CreateFromPlacements(
                    item.DrawingNumber,
                    placementList,
                    item.PageWidth,
                    item.PageHeight);
                WatermarkLearningStore.SaveEntry(materialLearning);

                try
                {
                    var material = placementList.FirstOrDefault(p =>
                        p != null
                        && string.Equals(p.Category, "Material", StringComparison.OrdinalIgnoreCase));
                    if (material != null && !string.IsNullOrWhiteSpace(item.DrawingNumber))
                    {
                        Platform.Core.PlatformHost.Learning?.RecordCorrection(new Platform.Models.UserCorrection
                        {
                            DocumentKey = item.DrawingNumber,
                            FieldName = "Material",
                            NewValue = material.Text
                        });
                    }
                }
                catch
                {
                }

                return;
            }

            if (!keepEditorPlacements)
            {
                EnsureMaterialPlacement(
                    item,
                    placementList,
                    settings,
                    editorMode: false,
                    extractedFromPdf: null,
                    allowDrawingFallback: false);
                EnsureSurfaceTreatmentPlacements(item, placementList, settings, editorMode: false);
            }

            // Wie beim Sortieren: Rand spiegeln auf Temp-Kopie (nie .prewm.pdf in-place).
            // Sonst landen RAL/Seitenzahl am engen Originalrand und rutschen optisch nach oben.
            var sourcePdfPath = item.PreWatermarkPath;
            var pageWidth = item.PageWidth;
            var pageHeight = item.PageHeight;
            string expandedTempPath = null;
            try
            {
                if (settings.UseSmartPlacement
                    && !string.IsNullOrWhiteSpace(sourcePdfPath)
                    && File.Exists(sourcePdfPath))
                {
                    expandedTempPath = sourcePdfPath + ".expand.tmp.pdf";
                    File.Copy(sourcePdfPath, expandedTempPath, true);
                    var expanded = PdfPageMarginExpansionService.TryExpandSourceInPlace(
                            expandedTempPath, 1, pageWidth, pageHeight);
                    if (expanded)
                    {
                        sourcePdfPath = expandedTempPath;
                        if (!PdfWatermarkService.TryGetPageSize(sourcePdfPath, 1, out pageWidth, out pageHeight))
                        {
                            pageWidth = item.PageWidth;
                            pageHeight = item.PageHeight;
                        }
                    }
                    else if (File.Exists(expandedTempPath))
                    {
                        try { File.Delete(expandedTempPath); } catch { /* ignore */ }
                        expandedTempPath = null;
                    }
                }

                if (!keepEditorPlacements)
                {
                    // LT-Position nicht an die globale Links/Rechts-Einstellung zurückzwingen.
                    SmartWatermarkLayoutService.EnforceFixedTopBandPlacements(
                        placementList,
                        pageWidth,
                        pageHeight,
                        sourcePdfPath,
                        1,
                        preserveTermPositions: true);
                    SmartWatermarkLayoutService.PreserveMaterialFontSizes(
                        placementList,
                        pageWidth,
                        pageHeight);
                    SmartWatermarkLayoutService.EnforceFixedBottomLeftMaterialPlacements(
                        placementList,
                        sourcePdfPath,
                        1,
                        pageWidth,
                        pageHeight,
                        settings);
                    SmartWatermarkLayoutService.EnforceFixedRightVerticalBgPlacements(
                        placementList,
                        sourcePdfPath,
                        1,
                        pageWidth,
                        pageHeight,
                        settings);
                    PinMissingSurfaceTreatmentsOnly(
                        placementList,
                        sourcePdfPath,
                        1,
                        pageWidth,
                        pageHeight);
                }

                PdfWatermarkService.ApplyCustomPlacements(
                    sourcePdfPath,
                    item.PdfPath,
                    placementList,
                    item.EncodedWatermarks,
                    item.DrawingNumber,
                    settings);
            }
            finally
            {
                if (!string.IsNullOrWhiteSpace(expandedTempPath) && File.Exists(expandedTempPath))
                {
                    try { File.Delete(expandedTempPath); } catch { /* ignore */ }
                }
            }

            // Seitenzahlen liegen nur auf der finalen PDF, nicht in .prewm.pdf.
            // Nach dem Neuaufbau aus der Pre-Wasserzeichen-Datei wieder einsetzen.
            RestoreBookPageNumber(item.PdfPath, orderedPdfPathsForPageNumbers);

            WatermarkMetadataStore.Save(
                item.PdfPath,
                item.DrawingNumber,
                item.EncodedWatermarks,
                placementList);

            var learningEntry = WatermarkLearningStore.CreateFromPlacements(
                item.DrawingNumber,
                placementList,
                pageWidth,
                pageHeight);
            WatermarkLearningStore.SaveEntry(learningEntry);
            if (!keepEditorPlacements)
            {
                WatermarkLearningStore.SaveCategoryDefaultsFromPlacements(
                    placementList,
                    pageWidth,
                    pageHeight,
                    includeLayoutCategories: true);
            }

            try
            {
                if (!string.IsNullOrWhiteSpace(item.DrawingNumber))
                {
                    foreach (var placement in placementList)
                    {
                        if (placement == null || string.IsNullOrWhiteSpace(placement.Text))
                            continue;
                        if (keepEditorPlacements
                            && !string.Equals(placement.Category, "Material", StringComparison.OrdinalIgnoreCase))
                            continue;

                        Platform.Core.PlatformHost.Learning?.RecordCorrection(new Platform.Models.UserCorrection
                        {
                            DocumentKey = item.DrawingNumber,
                            FieldName = placement.Category ?? "watermark",
                            NewValue = placement.Text
                        });
                    }
                }
            }
            catch
            {
            }
        }

        /// <summary>
        /// RAL/Elox nur setzen, wenn sie fehlen oder im Schriftfeld liegen.
        /// Vorhandene untere Positionen bleiben unverändert.
        /// </summary>
        private static void PinMissingSurfaceTreatmentsOnly(
            List<WatermarkTextPlacement> placements,
            string pdfPath,
            int pageNumber,
            float pageWidth,
            float pageHeight)
        {
            if (placements == null || pageWidth <= 0f || pageHeight <= 0f)
                return;

            var fontSize = WatermarkPageScale.GetPageNumberFontSize(pageWidth, pageHeight);
            var bandTop = WatermarkPageScale.GetSharedBottomBandBaselineY(
                pageWidth, pageHeight, fontSize, pdfPath, pageNumber)
                + Math.Max(36f, fontSize * 3f);

            var preserved = new List<WatermarkTextPlacement>();
            foreach (var placement in placements)
            {
                if (!SmartWatermarkLayoutService.IsLockedSurfaceTreatmentPlacement(placement))
                    continue;
                if (Math.Abs(placement.X) < 0.5f && Math.Abs(placement.Y) < 0.5f)
                    continue;
                if (placement.Y > bandTop)
                    continue;

                preserved.Add(ClonePlacement(placement));
            }

            SmartWatermarkLayoutService.EnforceSurfaceTreatmentPlacements(
                placements,
                pdfPath,
                pageNumber,
                pageWidth,
                pageHeight);

            foreach (var snap in preserved)
            {
                var current = placements.FirstOrDefault(p =>
                    p != null
                    && string.Equals(p.Category, snap.Category, StringComparison.OrdinalIgnoreCase));
                if (current == null)
                    continue;

                current.X = snap.X;
                current.Y = snap.Y;
                current.Alignment = snap.Alignment;
                current.FontSize = snap.FontSize;
                current.Angle = snap.Angle;
            }
        }

        /// <summary>
        /// Schreibt vorhandene Placements aus .prewm.pdf neu, ohne Layout neu zu berechnen
        /// und ohne den Lernspeicher anzufassen.
        /// </summary>
        public static void SaveItemFast(
            WatermarkEditorItem item,
            IList<WatermarkTextPlacement> placements,
            WatermarkSettings settings)
        {
            if (item == null)
                throw new ArgumentNullException(nameof(item));

            settings = settings ?? WatermarkSettingsStore.Load();
            var placementList = NormalizePlacementsForSave(placements)?.ToList()
                ?? new List<WatermarkTextPlacement>();
            ApplyManualMaterialOverride(item, placementList);
            EnsureSurfaceTreatmentPlacements(item, placementList, settings, editorMode: false);

            var sourcePdfPath = item.PreWatermarkPath;
            var pageWidth = item.PageWidth;
            var pageHeight = item.PageHeight;
            string expandedTempPath = null;
            try
            {
                if (settings.UseSmartPlacement
                    && !string.IsNullOrWhiteSpace(sourcePdfPath)
                    && File.Exists(sourcePdfPath)
                    && PdfPageMarginExpansionService.TryBuildPlan(
                        sourcePdfPath, 1, pageWidth, pageHeight, out _))
                {
                    expandedTempPath = sourcePdfPath + ".expand.tmp.pdf";
                    File.Copy(sourcePdfPath, expandedTempPath, true);
                    if (PdfPageMarginExpansionService.TryExpandSourceInPlace(
                            expandedTempPath, 1, pageWidth, pageHeight))
                    {
                        sourcePdfPath = expandedTempPath;
                        if (!PdfWatermarkService.TryGetPageSize(sourcePdfPath, 1, out pageWidth, out pageHeight))
                        {
                            pageWidth = item.PageWidth;
                            pageHeight = item.PageHeight;
                        }
                    }
                    else if (File.Exists(expandedTempPath))
                    {
                        try { File.Delete(expandedTempPath); } catch { /* ignore */ }
                        expandedTempPath = null;
                    }
                }

                PinMissingSurfaceTreatmentsOnly(
                    placementList,
                    sourcePdfPath,
                    1,
                    pageWidth,
                    pageHeight);

                PdfWatermarkService.ApplyCustomPlacements(
                    sourcePdfPath,
                    item.PdfPath,
                    placementList,
                    item.EncodedWatermarks,
                    item.DrawingNumber,
                    settings);
            }
            finally
            {
                if (!string.IsNullOrWhiteSpace(expandedTempPath) && File.Exists(expandedTempPath))
                {
                    try { File.Delete(expandedTempPath); } catch { /* ignore */ }
                }
            }

            WatermarkMetadataStore.Save(
                item.PdfPath,
                item.DrawingNumber,
                item.EncodedWatermarks,
                placementList);
        }

        public static List<WatermarkTextPlacement> LoadExistingPlacements(WatermarkEditorItem item)
        {
            if (item == null)
                return new List<WatermarkTextPlacement>();

            List<WatermarkTextPlacement> saved = null;
            if (WatermarkMetadataStore.TryLoadPlacements(
                    item.PdfPath,
                    out _,
                    out _,
                    out saved)
                && saved != null
                && saved.Count > 0)
            {
                var placements = ClonePlacements(saved);
                MergeVolatilePlacementsFromPdf(item.PdfPath, placements);
                return placements;
            }

            return FilterTitleBlockMaterialLookalikes(TryExtractPlacementsFromWatermarkedPdf(item.PdfPath));
        }

        private static void MergeVolatilePlacementsFromPdf(
            string pdfPath,
            List<WatermarkTextPlacement> placements)
        {
            if (placements == null)
                return;

            var extracted = FilterTitleBlockMaterialLookalikes(
                TryExtractPlacementsFromWatermarkedPdf(pdfPath));

            foreach (var item in extracted)
            {
                if (item == null || !WatermarkMetadataStore.IsVolatilePlacementCategory(item.Category))
                    continue;

                var exists = placements.Any(p =>
                    p != null
                    && string.Equals(p.Category, item.Category, StringComparison.OrdinalIgnoreCase)
                    && string.Equals((p.Text ?? string.Empty).Trim(), (item.Text ?? string.Empty).Trim(), StringComparison.OrdinalIgnoreCase));
                if (!exists)
                    placements.Add(ClonePlacement(item));
            }
        }

        /// <summary>
        /// Schriftfeld-Werte wie „VA-1,5mm“ nicht als Wasserzeichen übernehmen.
        /// Den großen Stempel aber nicht mitlöschen, wenn die geschätzte Schrift unter 13 pt liegt.
        /// </summary>
        private static List<WatermarkTextPlacement> FilterTitleBlockMaterialLookalikes(
            List<WatermarkTextPlacement> extracted)
        {
            var list = (extracted ?? new List<WatermarkTextPlacement>())
                .Where(p => p != null)
                .ToList();

            const float minWatermarkFont = 13f;
            var dropped = list
                .Where(p =>
                    WatermarkMetadataStore.IsVolatilePlacementCategory(p.Category)
                    && p.FontSize < minWatermarkFont)
                .Select(ClonePlacement)
                .ToList();

            list.RemoveAll(p =>
                WatermarkMetadataStore.IsVolatilePlacementCategory(p.Category)
                && p.FontSize < minWatermarkFont);

            RestoreDroppedVolatileIfMissing(list, dropped, "Material");

            var materials = list
                .Where(p => string.Equals(p.Category, "Material", StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (materials.Count > 1)
            {
                var maxFont = materials.Max(p => p.FontSize);
                list.RemoveAll(p =>
                    string.Equals(p.Category, "Material", StringComparison.OrdinalIgnoreCase)
                    && p.FontSize < maxFont - 2f);
            }

            return list;
        }

        private static void PinSurfaceTreatmentPositions(
            List<WatermarkTextPlacement> target,
            IList<WatermarkTextPlacement> source)
        {
            if (target == null || source == null)
                return;

            foreach (var category in new[] { "Brushed", "PowderCoating", "Anodizing", "NickelPlating", "SurfaceTreatment" })
            {
                var from = source.FirstOrDefault(p =>
                    p != null
                    && string.Equals(p.Category, category, StringComparison.OrdinalIgnoreCase)
                    && !string.IsNullOrWhiteSpace(p.Text));
                if (from == null)
                    continue;

                var dest = target.FirstOrDefault(p =>
                    p != null && string.Equals(p.Category, category, StringComparison.OrdinalIgnoreCase));
                if (dest == null)
                {
                    target.Add(ClonePlacement(from));
                    continue;
                }

                dest.X = from.X;
                dest.Y = from.Y;
                dest.Alignment = from.Alignment;
                dest.Angle = from.Angle;
                if (from.FontSize > 0f)
                    dest.FontSize = from.FontSize;
                dest.Text = string.Equals(category, "Brushed", StringComparison.OrdinalIgnoreCase)
                    ? "Geb\u00FCrstet"
                    : from.Text;
            }
        }

        private static void RestoreDroppedVolatileIfMissing(
            List<WatermarkTextPlacement> list,
            IList<WatermarkTextPlacement> dropped,
            string category)
        {
            if (list == null || dropped == null || string.IsNullOrWhiteSpace(category))
                return;

            if (list.Any(p =>
                p != null
                && string.Equals(p.Category, category, StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrWhiteSpace(p.Text)))
            {
                return;
            }

            var best = dropped
                .Where(p =>
                    p != null
                    && string.Equals(p.Category, category, StringComparison.OrdinalIgnoreCase)
                    && !string.IsNullOrWhiteSpace(p.Text))
                .OrderByDescending(p => p.FontSize)
                .ThenBy(p => p.X)
                .FirstOrDefault();
            if (best == null)
                return;

            if (best.FontSize < 13f)
                best.FontSize = 16f;
            list.Add(best);
        }

        /// <summary>
        /// Stellt die Buch-Seitenzahl nach einem Neuaufbau aus .prewm.pdf wieder her.
        /// </summary>
        public static void RestoreBookPageNumber(string pdfPath, IEnumerable<string> orderedPdfPaths)
        {
            if (string.IsNullOrWhiteSpace(pdfPath) || !File.Exists(pdfPath))
                return;

            var ordered = (orderedPdfPaths ?? Enumerable.Empty<string>())
                .Where(p => !string.IsNullOrWhiteSpace(p))
                .Select(p =>
                {
                    try { return Path.GetFullPath(p); }
                    catch { return p; }
                })
                .ToList();

            var targetFull = pdfPath;
            try { targetFull = Path.GetFullPath(pdfPath); }
            catch { }

            var startPageNumber = 1;
            if (ordered.Count > 0)
            {
                var found = false;
                foreach (var path in ordered)
                {
                    if (!File.Exists(path))
                        continue;

                    if (string.Equals(path, targetFull, StringComparison.OrdinalIgnoreCase))
                    {
                        found = true;
                        break;
                    }

                    startPageNumber += Math.Max(1, PdfWatermarkService.GetPageCount(path));
                }

                if (!found)
                    startPageNumber = 1;
            }

            PdfWatermarkService.AddPageNumbers(pdfPath, startPageNumber);
        }

        public static string RemergeFolder(string pdfFolder, IEnumerable<string> sourcePdfPaths)
        {
            var orderedFiles = (sourcePdfPaths ?? Enumerable.Empty<string>())
                .Where(File.Exists)
                .ToList();

            if (orderedFiles.Count == 0)
                throw new InvalidOperationException("Keine PDF-Dateien zum Zusammenführen gefunden.");

            var outputPath = Path.Combine(
                pdfFolder,
                "_GESAMT_" + DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture) + ".pdf");

            PdfWatermarkService.MergePdfFiles(orderedFiles, outputPath);
            var pageMap = MergedPdfPageMapService.Build(orderedFiles);
            MergedPdfPageMapService.Save(outputPath, pageMap);
            return outputPath;
        }

        public static void RefreshPageNumbers(IEnumerable<string> pdfPaths)
        {
            var nextPageNumber = 1;
            foreach (var pdfPath in pdfPaths ?? Enumerable.Empty<string>())
            {
                if (!File.Exists(pdfPath))
                    continue;

                nextPageNumber = PdfWatermarkService.AddPageNumbers(pdfPath, nextPageNumber);
            }
        }

        private static void ApplyExistingMetricsFromWatermarkedPdf(
            string pdfPath,
            List<WatermarkTextPlacement> placements)
        {
            if (placements == null || string.IsNullOrWhiteSpace(pdfPath) || !File.Exists(pdfPath))
                return;

            var extracted = TryExtractPlacementsFromWatermarkedPdf(pdfPath);
            ApplyExistingMetricsFromExtractedPlacements(extracted, placements);
        }

        private static void ApplyExistingMetricsFromExtractedPlacements(
            IList<WatermarkTextPlacement> extracted,
            List<WatermarkTextPlacement> placements)
        {
            if (placements == null || extracted == null || extracted.Count == 0)
                return;

            foreach (var existing in extracted)
            {
                if (string.IsNullOrWhiteSpace(existing?.Category))
                    continue;

                if (string.Equals(existing.Category, "Material", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(existing.Category, "PowderCoating", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(existing.Category, "Anodizing", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(existing.Category, "NickelPlating", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(existing.Category, "SurfaceTreatment", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (string.Equals(existing.Category, "Date", StringComparison.OrdinalIgnoreCase)
                    && placements.Any(p =>
                        p != null
                        && string.Equals(p.Category, "Quantity", StringComparison.OrdinalIgnoreCase)
                        && ContainsDeliveryDateToken(p.Text)))
                {
                    continue;
                }

                if (string.Equals(existing.Category, "Quantity", StringComparison.OrdinalIgnoreCase))
                {
                    var target = placements.FirstOrDefault(p =>
                            p != null
                            && string.Equals(p.Category, "Quantity", StringComparison.OrdinalIgnoreCase)
                            && string.Equals((p.Text ?? string.Empty).Trim(), (existing.Text ?? string.Empty).Trim(), StringComparison.OrdinalIgnoreCase))
                        ?? placements.FirstOrDefault(p =>
                            p != null
                            && string.Equals(p.Category, "Quantity", StringComparison.OrdinalIgnoreCase)
                            && ContainsQuantityToken(p.Text, existing.Text))
                        ?? placements.FirstOrDefault(p =>
                            p != null && string.Equals(p.Category, "Quantity", StringComparison.OrdinalIgnoreCase));

                    if (target != null)
                        CopyPlacementMetrics(existing, target, placements);
                    else
                        placements.Add(ClonePlacement(existing));

                    continue;
                }

                var single = placements.FirstOrDefault(p =>
                    p != null && string.Equals(p.Category, existing.Category, StringComparison.OrdinalIgnoreCase));

                if (single != null)
                    CopyPlacementMetrics(existing, single, placements);
                else
                    placements.Add(ClonePlacement(existing));
            }
        }

        private static bool ContainsDeliveryDateToken(string text)
        {
            return !string.IsNullOrWhiteSpace(text) && DateQuantityRegex.IsMatch(text.Trim());
        }

        private static bool ContainsQuantityToken(string combinedText, string fragmentText)
        {
            if (string.IsNullOrWhiteSpace(combinedText) || string.IsNullOrWhiteSpace(fragmentText))
                return false;

            var combined = combinedText.Trim();
            var fragment = fragmentText.Trim();
            if (combined.IndexOf(fragment, StringComparison.OrdinalIgnoreCase) >= 0)
                return true;

            return QuantityWatermarkRegex.IsMatch(fragment) && ContainsDeliveryDateToken(combined);
        }

        private static void CopyPlacementMetrics(
            WatermarkTextPlacement source,
            WatermarkTextPlacement target,
            IList<WatermarkTextPlacement> allPlacements = null)
        {
            if (source == null || target == null)
                return;

            var savedFontSize = target.FontSize > 0f ? target.FontSize : 0f;
            var lockPosition = SmartWatermarkLayoutService.IsLockedTopBandPlacement(target, allPlacements)
                || SmartWatermarkLayoutService.IsLockedSurfaceTreatmentPlacement(target);

            if (!lockPosition)
            {
                target.X = source.X;
                target.Y = source.Y;
                target.Alignment = source.Alignment;
                target.Angle = source.Angle;
                target.UseRedText = source.UseRedText;
            }

            if (SmartWatermarkLayoutService.IsLockedSurfaceTreatmentPlacement(target)
                || string.Equals(target.Category, "Material", StringComparison.OrdinalIgnoreCase))
            {
                if (savedFontSize >= 13f)
                    target.FontSize = savedFontSize;
                else if (source.FontSize >= 13f)
                    target.FontSize = source.FontSize;
                else
                    target.FontSize = 22f;
            }
            else if (savedFontSize > 0f)
                target.FontSize = savedFontSize;
            else if (source.FontSize > 0f)
                target.FontSize = source.FontSize;

            if (!string.IsNullOrWhiteSpace(source.Text)
                && !string.Equals(target.Category, "Material", StringComparison.OrdinalIgnoreCase)
                && ShouldUpdatePlacementText(target.Text, source.Text))
                target.Text = source.Text;
        }

        private static bool ShouldUpdatePlacementText(string targetText, string sourceText)
        {
            if (string.IsNullOrWhiteSpace(sourceText))
                return false;

            var target = (targetText ?? string.Empty).Trim();
            var source = sourceText.Trim();
            if (target.Length == 0)
                return true;

            if (string.Equals(target, source, StringComparison.OrdinalIgnoreCase))
                return false;

            if (target.IndexOf(source, StringComparison.OrdinalIgnoreCase) >= 0)
                return false;

            if (source.IndexOf(target, StringComparison.OrdinalIgnoreCase) >= 0)
                return true;

            return source.Length > target.Length;
        }

        private static List<WatermarkTextPlacement> TryExtractPlacementsFromWatermarkedPdf(string pdfPath)
        {
            var result = new List<WatermarkTextPlacement>();
            if (string.IsNullOrWhiteSpace(pdfPath) || !File.Exists(pdfPath))
                return result;

            IList<PdfTextRect> rects = null;
            try
            {
                var layout = PdfLayoutAnalysisService.AnalyzePage(pdfPath, 1);
                rects = layout.TextRects;
                foreach (var rect in rects ?? new List<PdfTextRect>())
                {
                    var text = (rect.Text ?? string.Empty).Trim();
                    if (string.IsNullOrWhiteSpace(text) || PageNumberRegex.IsMatch(text))
                        continue;

                    if (!TryClassifyWatermarkText(text, out var category, out var useRedText))
                        continue;

                    result.Add(new WatermarkTextPlacement
                    {
                        Category = category,
                        Text = text,
                        FontSize = EstimateFontSizeFromRect(rect),
                        X = (rect.Left + rect.Right) / 2f,
                        Y = rect.Bottom,
                        Alignment = string.Equals(category, "Material", StringComparison.OrdinalIgnoreCase) ? 1 : 0,
                        UseRedText = useRedText
                    });
                }
            }
            catch
            {
            }

            return MergeSplitMaterialExtractions(rects, MergeSplitQuantityExtractions(result));
        }

        private static List<WatermarkTextPlacement> MergeSplitQuantityExtractions(List<WatermarkTextPlacement> extracted)
        {
            if (extracted == null || extracted.Count == 0)
                return extracted ?? new List<WatermarkTextPlacement>();

            var date = extracted.FirstOrDefault(p =>
                p != null && string.Equals(p.Category, "Date", StringComparison.OrdinalIgnoreCase));
            var qtyOnly = extracted
                .Where(p =>
                    p != null
                    && string.Equals(p.Category, "Quantity", StringComparison.OrdinalIgnoreCase)
                    && QuantityWatermarkRegex.IsMatch((p.Text ?? string.Empty).Trim()))
                .ToList();

            if (date == null || qtyOnly.Count != 1)
                return extracted;

            var combined = ClonePlacement(qtyOnly[0]);
            combined.Category = "Quantity";
            combined.Text = date.Text.Trim() + "   " + qtyOnly[0].Text.Trim();
            combined.UseRedText = true;
            if (date.Y > combined.Y)
                combined.Y = date.Y;

            var merged = extracted
                .Where(p => p != date && p != qtyOnly[0])
                .Select(ClonePlacement)
                .ToList();
            merged.Add(combined);
            return merged;
        }

        private static readonly Regex MaterialPrefixRegex = new Regex(
            @"^(ST|VA|ALU)-?$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

        private static readonly Regex MaterialGaugeRegex = new Regex(
            @"^\d+[.,]\d{1,2}\s*mm$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

        private static readonly Regex RalNumberRegex = new Regex(
            @"^\d{4}$",
            RegexOptions.CultureInvariant | RegexOptions.Compiled);

        private static List<WatermarkTextPlacement> MergeSplitMaterialExtractions(
            IList<PdfTextRect> rects,
            List<WatermarkTextPlacement> extracted)
        {
            var list = extracted ?? new List<WatermarkTextPlacement>();
            if (rects == null || rects.Count == 0)
                return list;

            if (!list.Any(p =>
                p != null
                && string.Equals(p.Category, "Material", StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrWhiteSpace(p.Text)))
            {
                var combined = TryCombineAdjacentRects(
                    rects,
                    prefix => MaterialPrefixRegex.IsMatch(prefix),
                    suffix => MaterialGaugeRegex.IsMatch(suffix),
                    (prefix, suffix) =>
                    {
                        var head = prefix.TrimEnd('-', ' ');
                        return head + "-" + suffix.Trim();
                    });
                if (combined != null)
                {
                    combined.Category = "Material";
                    combined.Alignment = 1;
                    list.Add(combined);
                }
            }

            var ral = list.FirstOrDefault(p =>
                p != null
                && string.Equals(p.Category, "PowderCoating", StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrWhiteSpace(p.Text));
            if (ral == null || string.Equals(ral.Text.Trim(), "RAL", StringComparison.OrdinalIgnoreCase))
            {
                var combinedRal = TryCombineAdjacentRects(
                    rects,
                    prefix => string.Equals(prefix, "RAL", StringComparison.OrdinalIgnoreCase),
                    suffix => RalNumberRegex.IsMatch(suffix),
                    (prefix, suffix) => prefix.Trim() + " " + suffix.Trim());
                if (combinedRal != null)
                {
                    combinedRal.Category = "PowderCoating";
                    combinedRal.Alignment = 1;
                    if (ral != null)
                    {
                        ral.Text = combinedRal.Text;
                        var combinedIsTitleBlock = combinedRal.FontSize < 13f
                            || combinedRal.Y > ral.Y + 12f;
                        if (!combinedIsTitleBlock || ral.FontSize < 13f)
                        {
                            ral.X = combinedRal.X;
                            ral.Y = combinedRal.Y;
                            ral.Alignment = combinedRal.Alignment;
                        }

                        if (combinedRal.FontSize > ral.FontSize && combinedRal.FontSize >= 13f)
                            ral.FontSize = combinedRal.FontSize;
                    }
                    else if (combinedRal.FontSize >= 13f)
                    {
                        list.Add(combinedRal);
                    }
                }
            }

            return list;
        }

        private static WatermarkTextPlacement TryCombineAdjacentRects(
            IList<PdfTextRect> rects,
            Func<string, bool> isPrefix,
            Func<string, bool> isSuffix,
            Func<string, string, string> combine)
        {
            if (rects == null || isPrefix == null || isSuffix == null || combine == null)
                return null;

            foreach (var prefix in rects)
            {
                var prefixText = (prefix.Text ?? string.Empty).Trim();
                if (!isPrefix(prefixText))
                    continue;

                PdfTextRect best = null;
                var bestGap = float.MaxValue;
                foreach (var suffix in rects)
                {
                    if (ReferenceEquals(suffix, prefix))
                        continue;

                    var suffixText = (suffix.Text ?? string.Empty).Trim();
                    if (!isSuffix(suffixText))
                        continue;

                    var gap = suffix.Left - prefix.Right;
                    var sameLine = Math.Abs(prefix.Bottom - suffix.Bottom) <= 10f;
                    if (!sameLine || gap < -4f || gap > 55f)
                        continue;

                    if (gap < bestGap)
                    {
                        bestGap = gap;
                        best = suffix;
                    }
                }

                if (best == null)
                    continue;

                var text = combine(prefixText, (best.Text ?? string.Empty).Trim());
                return new WatermarkTextPlacement
                {
                    Text = text,
                    FontSize = Math.Max(EstimateFontSizeFromRect(prefix), EstimateFontSizeFromRect(best)),
                    X = (prefix.Left + best.Right) / 2f,
                    Y = Math.Max(prefix.Bottom, best.Bottom),
                    Alignment = 1
                };
            }

            return null;
        }

        private static bool TryClassifyWatermarkText(string text, out string category, out bool useRedText)
        {
            category = string.Empty;
            useRedText = false;

            if (MaterialWatermarkRegex.IsMatch(text))
            {
                category = "Material";
                return true;
            }

            if (QuantityWatermarkRegex.IsMatch(text))
            {
                category = "Quantity";
                useRedText = true;
                return true;
            }

            if (DateQuantityRegex.IsMatch(text))
            {
                category = "Quantity";
                useRedText = true;
                return true;
            }

            if (text.StartsWith("BG:", StringComparison.OrdinalIgnoreCase)
                || text.StartsWith("BG ", StringComparison.OrdinalIgnoreCase))
            {
                category = "Bg";
                return true;
            }

            if (text.StartsWith("FT-", StringComparison.OrdinalIgnoreCase)
                || text.StartsWith("LT-", StringComparison.OrdinalIgnoreCase))
            {
                category = "Date";
                useRedText = true;
                return true;
            }

            if (text.StartsWith("RAL", StringComparison.OrdinalIgnoreCase))
            {
                category = "PowderCoating";
                return true;
            }

            if (string.Equals(text, "ELOX", StringComparison.OrdinalIgnoreCase))
            {
                category = "Anodizing";
                return true;
            }

            if (string.Equals(text, "Gebürstet", StringComparison.OrdinalIgnoreCase)
                || string.Equals(text, "Gebuerstet", StringComparison.OrdinalIgnoreCase))
            {
                category = "Brushed";
                return true;
            }

            if (string.Equals(text, "Vernickelt", StringComparison.OrdinalIgnoreCase)
                || text.StartsWith("VERNICKELT", StringComparison.OrdinalIgnoreCase))
            {
                category = "NickelPlating";
                return true;
            }

            if (LooksLikeGenericSurfaceTreatment(text))
            {
                category = "SurfaceTreatment";
                return true;
            }

            return false;
        }

        private static bool LooksLikeGenericSurfaceTreatment(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return false;

            var trimmed = text.Trim();
            if (trimmed.Length < 3 || trimmed.Length > 56)
                return false;

            if (trimmed.StartsWith("VA-", StringComparison.OrdinalIgnoreCase)
                || trimmed.StartsWith("ST-", StringComparison.OrdinalIgnoreCase)
                || trimmed.StartsWith("AL-", StringComparison.OrdinalIgnoreCase)
                || trimmed.IndexOf("mm", StringComparison.OrdinalIgnoreCase) >= 0
                || trimmed.IndexOf("EUROPEAN", StringComparison.OrdinalIgnoreCase) >= 0
                || trimmed.IndexOf("PROJECTION", StringComparison.OrdinalIgnoreCase) >= 0
                || trimmed.IndexOf("FORMAT", StringComparison.OrdinalIgnoreCase) >= 0
                || Regex.IsMatch(trimmed, @"^(?:rohe\s*)?oberfl(?:ae|[äa])che$", RegexOptions.IgnoreCase)
                || Regex.IsMatch(trimmed, @"\bR[az]\b", RegexOptions.IgnoreCase))
            {
                return false;
            }

            return trimmed.IndexOf("µm", StringComparison.OrdinalIgnoreCase) >= 0
                   || Regex.IsMatch(trimmed, @"\d\s*um\b", RegexOptions.IgnoreCase)
                   || trimmed.IndexOf("verzink", StringComparison.OrdinalIgnoreCase) >= 0
                   || trimmed.IndexOf("phosphat", StringComparison.OrdinalIgnoreCase) >= 0
                   || trimmed.IndexOf("passiv", StringComparison.OrdinalIgnoreCase) >= 0
                   || trimmed.IndexOf("chromat", StringComparison.OrdinalIgnoreCase) >= 0
                   || trimmed.IndexOf("elox", StringComparison.OrdinalIgnoreCase) >= 0
                   || trimmed.IndexOf("nickel", StringComparison.OrdinalIgnoreCase) >= 0
                   || trimmed.IndexOf("lack", StringComparison.OrdinalIgnoreCase) >= 0
                   || trimmed.IndexOf("beschicht", StringComparison.OrdinalIgnoreCase) >= 0
                   || trimmed.IndexOf("brüniert", StringComparison.OrdinalIgnoreCase) >= 0
                   || trimmed.IndexOf("brueniert", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static float EstimateFontSizeFromRect(PdfTextRect rect)
        {
            if (rect == null || rect.Height <= 0f)
                return 22f;

            return Math.Max(9f, Math.Min(60f, rect.Height * 0.92f));
        }

        private static void EnsureMaterialPlacement(
            WatermarkEditorItem item,
            List<WatermarkTextPlacement> placements,
            WatermarkSettings settings,
            bool editorMode = false,
            IList<WatermarkTextPlacement> extractedFromPdf = null,
            bool allowDrawingFallback = true)
        {
            if (placements == null || item == null)
                return;

            if (settings != null && !settings.ExtractMaterialThickness && !settings.ExtractMaterialType)
                return;

            if (editorMode)
            {
                EnsureMaterialPlacementFromPdf(item, placements, extractedFromPdf);
                return;
            }

            var material = placements.FirstOrDefault(p =>
                string.Equals(p.Category, "Material", StringComparison.OrdinalIgnoreCase));

            if (material != null && !string.IsNullOrWhiteSpace(material.Text) && material.FontSize > 0f)
                return;

            if (WatermarkLearningStore.ShouldSuppressAutoMaterial(item.DrawingNumber))
                return;

            if (WatermarkLearningStore.HasLearnedMaterialPlacement(item.DrawingNumber))
                return;

            var extracted = (extractedFromPdf ?? TryExtractPlacementsFromWatermarkedPdf(item.PdfPath))
                .FirstOrDefault(p => string.Equals(p.Category, "Material", StringComparison.OrdinalIgnoreCase));

            if (extracted != null)
            {
                if (material == null)
                    placements.Add(ClonePlacement(extracted));
                else
                    CopyPlacementMetrics(extracted, material, placements);
                return;
            }

            var existingText = PdfMaterialExtractionService.TryReadMaterialWatermarkFromPdf(item.PdfPath);
            if (string.IsNullOrWhiteSpace(existingText) && allowDrawingFallback)
            {
                var materialInfo = PdfMaterialExtractionService.Extract(item.PreWatermarkPath, 1);
                if (materialInfo.IsAssembly || materialInfo.IsTube)
                    return;
                existingText = materialInfo.MaterialWatermarkText;
            }

            if (string.IsNullOrWhiteSpace(existingText))
                return;

            if (material == null)
            {
                if (SmartWatermarkLayoutService.IsFixedBottomLeftMaterialPlacement(settings))
                {
                    placements.Add(new WatermarkTextPlacement
                    {
                        Text = existingText,
                        Category = "Material",
                        FontSize = WatermarkPageScale.GetPageNumberFontSize(item.PageWidth, item.PageHeight)
                    });
                    SmartWatermarkLayoutService.EnforceFixedBottomLeftMaterialPlacements(
                        placements,
                        item.PreWatermarkPath,
                        1,
                        item.PageWidth,
                        item.PageHeight,
                        settings);
                }
                else
                {
                    var dynamic = MaterialWatermarkPlacementService.PlaceDynamic(
                        new[] { existingText },
                        item.PreWatermarkPath,
                        1,
                        item.PageWidth,
                        item.PageHeight,
                        Array.Empty<PdfTextRect>());
                    if (dynamic.Count > 0)
                    {
                        dynamic[0].Category = "Material";
                        placements.Add(dynamic[0]);
                    }
                }
            }
            else
            {
                material.Text = existingText;
            }
        }

        private static void ApplyManualMaterialOverride(
            WatermarkEditorItem item,
            List<WatermarkTextPlacement> placements)
        {
            if (item == null || placements == null)
                return;

            string overrideText;
            if (!WatermarkLearningStore.TryGetMaterialOverrideText(item.DrawingNumber, out overrideText)
                || string.IsNullOrWhiteSpace(overrideText))
            {
                return;
            }

            var material = placements.FirstOrDefault(p =>
                string.Equals(p.Category, "Material", StringComparison.OrdinalIgnoreCase));
            if (material != null)
            {
                material.Text = overrideText;
                if (material.FontSize <= 0f)
                    material.FontSize = WatermarkPageScale.GetPageNumberFontSize(item.PageWidth, item.PageHeight);
                return;
            }

            placements.Add(new WatermarkTextPlacement
            {
                Category = "Material",
                Text = overrideText,
                Alignment = 1,
                FontSize = WatermarkPageScale.GetPageNumberFontSize(item.PageWidth, item.PageHeight)
            });
        }

        private static void EnsureMaterialPlacementFromPdf(
            WatermarkEditorItem item,
            List<WatermarkTextPlacement> placements,
            IList<WatermarkTextPlacement> extractedFromPdf = null)
        {
            var extracted = extractedFromPdf ?? TryExtractPlacementsFromWatermarkedPdf(item.PdfPath);
            var material = placements.FirstOrDefault(p =>
                string.Equals(p.Category, "Material", StringComparison.OrdinalIgnoreCase));
            var extractedMaterial = extracted
                .FirstOrDefault(p => string.Equals(p.Category, "Material", StringComparison.OrdinalIgnoreCase));

            if (material != null)
            {
                if (extractedMaterial != null)
                    CopyPlacementMetrics(extractedMaterial, material, placements);
                ApplyManualMaterialOverride(item, placements);
                var current = placements.FirstOrDefault(p =>
                    string.Equals(p.Category, "Material", StringComparison.OrdinalIgnoreCase));
                if (current != null)
                {
                    current.FontSize = SmartWatermarkLayoutService.ResolveMaterialFontSizeForEditor(
                        current,
                        current.Text,
                        item.PageWidth,
                        item.PageHeight);
                }
                return;
            }

            ApplyManualMaterialOverride(item, placements);
            if (placements.Any(p =>
                p != null
                && string.Equals(p.Category, "Material", StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrWhiteSpace(p.Text)))
            {
                return;
            }

            var existingText = PdfMaterialExtractionService.TryReadMaterialWatermarkFromPdf(item.PdfPath);
            if (string.IsNullOrWhiteSpace(existingText))
                return;

            if (extractedMaterial != null)
            {
                var clone = ClonePlacement(extractedMaterial);
                clone.Text = existingText;
                clone.FontSize = SmartWatermarkLayoutService.ResolveMaterialFontSizeForEditor(
                    clone,
                    existingText,
                    item.PageWidth,
                    item.PageHeight);
                placements.Add(clone);
                return;
            }

            placements.Add(new WatermarkTextPlacement
            {
                Category = "Material",
                Text = existingText,
                FontSize = 22f,
                Alignment = 1
            });
        }

        private static void EnsureSurfaceTreatmentPlacements(
            WatermarkEditorItem item,
            List<WatermarkTextPlacement> placements,
            WatermarkSettings settings,
            bool editorMode = false,
            IList<WatermarkTextPlacement> extractedFromPdf = null)
        {
            if (placements == null || item == null)
                return;

            var extracted = extractedFromPdf ?? TryExtractPlacementsFromWatermarkedPdf(item.PdfPath);
            foreach (var category in new[] { "PowderCoating", "Anodizing", "NickelPlating", "SurfaceTreatment", "Brushed" })
            {
                var existing = placements.FirstOrDefault(p =>
                    string.Equals(p.Category, category, StringComparison.OrdinalIgnoreCase));

                if (existing != null && !string.IsNullOrWhiteSpace(existing.Text) && existing.FontSize > 0f)
                    continue;

                var fromPdf = extracted.FirstOrDefault(p =>
                    string.Equals(p.Category, category, StringComparison.OrdinalIgnoreCase));

                if (fromPdf != null)
                {
                    if (existing == null)
                        placements.Add(ClonePlacement(fromPdf));
                    else
                        CopyPlacementMetrics(fromPdf, existing, placements);
                    continue;
                }

                if (editorMode || settings == null)
                    continue;

                var surfaceInfo = PdfSurfaceTreatmentExtractionService.Extract(item.PreWatermarkPath, 1);
                string text = null;
                if (string.Equals(category, "PowderCoating", StringComparison.OrdinalIgnoreCase)
                    && settings.ExtractPowderCoating
                    && surfaceInfo != null
                    && surfaceInfo.HasPowderCoating)
                {
                    text = settings.PowderCoatingShowRalNumber && !string.IsNullOrWhiteSpace(surfaceInfo.RalCode)
                        ? "RAL " + surfaceInfo.RalCode
                        : "RAL";
                }
                else if (string.Equals(category, "Anodizing", StringComparison.OrdinalIgnoreCase)
                         && settings.ExtractAnodizing
                         && surfaceInfo != null
                         && surfaceInfo.HasAnodizing)
                {
                    text = "ELOX";
                }
                else if (string.Equals(category, "Brushed", StringComparison.OrdinalIgnoreCase)
                         && surfaceInfo != null
                         && surfaceInfo.HasBrushed)
                {
                    text = "Geb\u00FCrstet";
                }
                else if (string.Equals(category, "NickelPlating", StringComparison.OrdinalIgnoreCase)
                         && surfaceInfo != null
                         && surfaceInfo.HasNickelPlating)
                {
                    text = PdfSurfaceTreatmentExtractionService.ResolveNickelPlatingStampText(surfaceInfo);
                }
                else if (string.Equals(category, "SurfaceTreatment", StringComparison.OrdinalIgnoreCase)
                         && surfaceInfo != null
                         && surfaceInfo.HasGenericSurfaceTreatment)
                {
                    text = PdfSurfaceTreatmentExtractionService.ResolveGenericSurfaceStampText(surfaceInfo);
                }

                if (string.IsNullOrWhiteSpace(text))
                    continue;

                if (existing == null)
                {
                    placements.Add(new WatermarkTextPlacement
                    {
                        Category = category,
                        Text = text,
                        FontSize = 22f,
                        Alignment = string.Equals(category, "Brushed", StringComparison.OrdinalIgnoreCase) ? 0 : 1
                    });
                }
                else
                {
                    existing.Text = text;
                }
            }
        }

        private static IList<WatermarkTextPlacement> NormalizePlacementsForSave(IList<WatermarkTextPlacement> placements)
        {
            return (placements ?? new List<WatermarkTextPlacement>())
                .Where(p => p != null)
                .Select(p =>
                {
                    if (p.FontSize > 0f)
                        return p;

                    return new WatermarkTextPlacement
                    {
                        Text = p.Text,
                        X = p.X,
                        Y = p.Y,
                        Alignment = p.Alignment,
                        Angle = p.Angle,
                        FontSize = 22f,
                        UseRedText = p.UseRedText,
                        Category = p.Category
                    };
                })
                .ToList();
        }

        private static List<WatermarkTextPlacement> ClonePlacements(IEnumerable<WatermarkTextPlacement> source)
        {
            return (source ?? Enumerable.Empty<WatermarkTextPlacement>())
                .Select(ClonePlacement)
                .ToList();
        }

        private static WatermarkTextPlacement ClonePlacement(WatermarkTextPlacement p)
        {
            return new WatermarkTextPlacement
            {
                Text = p.Text,
                X = p.X,
                Y = p.Y,
                Alignment = p.Alignment,
                Angle = p.Angle,
                FontSize = p.FontSize > 0 ? p.FontSize : 22f,
                UseRedText = p.UseRedText,
                Category = p.Category
            };
        }

        private static string GuessDrawingNumberFromFileName(string pdfPath)
        {
            var fileName = Path.GetFileNameWithoutExtension(pdfPath) ?? string.Empty;
            var match = Regex.Match(fileName, @"^\d+[_\-\s]+(.+)$");
            return match.Success ? match.Groups[1].Value.Trim() : fileName.Trim();
        }
    }
}
