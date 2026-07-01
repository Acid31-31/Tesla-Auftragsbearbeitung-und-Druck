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

        public static string GetPreWatermarkPath(string pdfPath)
        {
            return pdfPath + PreWatermarkSuffix;
        }

        public static void EnsurePreWatermarkBackup(string pdfPath)
        {
            if (string.IsNullOrWhiteSpace(pdfPath) || !File.Exists(pdfPath))
                return;

            var backupPath = GetPreWatermarkPath(pdfPath);
            if (!File.Exists(backupPath))
                File.Copy(pdfPath, backupPath, false);
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
            if (item == null)
                return new List<WatermarkTextPlacement>();

            settings = settings ?? WatermarkSettingsStore.Load();
            var entries = PdfWatermarkService.ParseWatermarkEntries(item.EncodedWatermarks);
            var orderNumbers = entries
                .Where(e => !string.IsNullOrWhiteSpace(e.orderNumber))
                .Select(e => e.orderNumber.Trim())
                .Distinct()
                .ToList();

            var fieldData = SmartWatermarkLayoutService.BuildFieldData(entries, orderNumbers);
            return SmartWatermarkLayoutService.Compute(
                item.PreWatermarkPath,
                1,
                fieldData,
                item.DrawingNumber,
                item.PageWidth,
                item.PageHeight,
                settings);
        }

        public static void SaveItem(
            WatermarkEditorItem item,
            IList<WatermarkTextPlacement> placements,
            WatermarkSettings settings)
        {
            if (item == null)
                throw new ArgumentNullException(nameof(item));

            settings = settings ?? WatermarkSettingsStore.Load();
            placements = placements ?? new List<WatermarkTextPlacement>();

            PdfWatermarkService.ApplyCustomPlacements(
                item.PreWatermarkPath,
                item.PdfPath,
                placements?.ToList() ?? new List<WatermarkTextPlacement>(),
                item.EncodedWatermarks,
                item.DrawingNumber,
                settings);

            var learningEntry = WatermarkLearningStore.CreateFromPlacements(item.DrawingNumber, placements);
            WatermarkLearningStore.SaveEntry(learningEntry);
            WatermarkLearningStore.SaveCategoryDefaultsFromPlacements(placements, item.PageWidth, item.PageHeight);
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

        private static string GuessDrawingNumberFromFileName(string pdfPath)
        {
            var fileName = Path.GetFileNameWithoutExtension(pdfPath) ?? string.Empty;
            var match = Regex.Match(fileName, @"^\d+[_\-\s]+(.+)$");
            return match.Success ? match.Groups[1].Value.Trim() : fileName.Trim();
        }
    }
}
