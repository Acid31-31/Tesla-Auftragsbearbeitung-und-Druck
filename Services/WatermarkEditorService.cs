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
            IEnumerable<string> orderedPdfPathsForPageNumbers = null)
        {
            if (item == null)
                throw new ArgumentNullException(nameof(item));

            settings = settings ?? WatermarkSettingsStore.Load();
            placements = NormalizePlacementsForSave(placements);
            var placementList = placements?.ToList() ?? new List<WatermarkTextPlacement>();

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

                // LT-Position nicht an die globale Links/Rechts-Einstellung zurückzwingen.
                SmartWatermarkLayoutService.EnforceFixedTopBandPlacements(
                    placementList,
                    pageWidth,
                    pageHeight,
                    sourcePdfPath,
                    1,
                    preserveTermPositions: true);
                SmartWatermarkLayoutService.EnforceSurfaceTreatmentPlacements(
                    placementList,
                    sourcePdfPath,
                    1,
                    pageWidth,
                    pageHeight);
                SmartWatermarkLayoutService.PreserveMaterialFontSizes(
                    placementList,
                    pageWidth,
                    pageHeight);
                SmartWatermarkLayoutService.PreserveSurfaceTreatmentFontSizes(
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
            WatermarkLearningStore.SaveCategoryDefaultsFromPlacements(
                placementList,
                pageWidth,
                pageHeight,
                includeLayoutCategories: true);

            try
            {
                if (!string.IsNullOrWhiteSpace(item.DrawingNumber))
                {
                    foreach (var placement in placementList)
                    {
                        if (placement == null || string.IsNullOrWhiteSpace(placement.Text))
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
                    || string.Equals(existing.Category, "Anodizing", StringComparison.OrdinalIgnoreCase))
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

            if (!string.IsNullOrWhiteSpace(source.Text) && ShouldUpdatePlacementText(target.Text, source.Text))
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

            try
            {
                var layout = PdfLayoutAnalysisService.AnalyzePage(pdfPath, 1);
                foreach (var rect in layout.TextRects ?? new List<PdfTextRect>())
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

            return MergeSplitQuantityExtractions(result);
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

            return false;
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
            IList<WatermarkTextPlacement> extractedFromPdf = null)
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

            var materialInfo = PdfMaterialExtractionService.Extract(item.PreWatermarkPath, 1);
            if (materialInfo.IsAssembly || materialInfo.IsTube)
                return;

            var extracted = TryExtractPlacementsFromWatermarkedPdf(item.PdfPath)
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
            if (string.IsNullOrWhiteSpace(existingText))
                existingText = materialInfo.MaterialWatermarkText;

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
                material.FontSize = SmartWatermarkLayoutService.ResolveMaterialFontSizeForEditor(
                    material,
                    material.Text,
                    item.PageWidth,
                    item.PageHeight);
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
            foreach (var category in new[] { "PowderCoating", "Anodizing" })
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

                if (string.IsNullOrWhiteSpace(text))
                    continue;

                if (existing == null)
                {
                    placements.Add(new WatermarkTextPlacement
                    {
                        Category = category,
                        Text = text,
                        FontSize = 22f,
                        Alignment = 1
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
