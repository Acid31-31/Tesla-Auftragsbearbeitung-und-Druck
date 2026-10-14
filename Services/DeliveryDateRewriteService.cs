using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using PDF_umbenenen.Models;

namespace PDF_umbenenen.Services
{
    internal enum DeliveryDatePrefixMode
    {
        KeepExisting,
        Liefertermin,
        Fertigungstermin
    }

    internal sealed class DeliveryDateRewriteResult
    {
        public int UpdatedCount { get; set; }

        public int SkippedCount { get; set; }

        public string MergedPdfPath { get; set; } = string.Empty;

        public List<string> OrderedFiles { get; set; } = new List<string>();
    }

    internal static class DeliveryDateRewriteService
    {
        private static readonly Regex DateTokenRegex = new Regex(
            @"(?:(?:LT|FT)-)?\d{1,2}\.\d{1,2}\.\d{4}",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

        public static bool TryDetectCurrent(
            IEnumerable<string> pdfPaths,
            out DateTime currentDate,
            out bool isFertigungstermin,
            out bool mixedPrefixes)
        {
            currentDate = DateTime.Today;
            isFertigungstermin = false;
            mixedPrefixes = false;

            var seenLt = false;
            var seenFt = false;
            DateTime? parsed = null;

            foreach (var pdfPath in pdfPaths ?? Enumerable.Empty<string>())
            {
                if (string.IsNullOrWhiteSpace(pdfPath) || !File.Exists(pdfPath))
                    continue;

                if (!WatermarkMetadataStore.TryLoad(pdfPath, out _, out var encoded))
                    continue;

                foreach (var entry in PdfWatermarkService.ParseWatermarkEntries(encoded))
                {
                    var token = entry.deliveryDate ?? string.Empty;
                    if (LooksLikeFertigung(token))
                        seenFt = true;
                    else if (DateTokenRegex.IsMatch(token) || LooksLikeLiefer(token))
                        seenLt = true;

                    var eu = OrderVerificationDateHelper.TryNormalizeExcelDate(token);
                    if (string.IsNullOrWhiteSpace(eu))
                        continue;

                    if (DateTime.TryParseExact(
                            eu,
                            "dd.MM.yyyy",
                            CultureInfo.GetCultureInfo("de-DE"),
                            DateTimeStyles.None,
                            out var dt))
                    {
                        parsed = dt;
                        break;
                    }
                }

                if (parsed.HasValue)
                    break;
            }

            mixedPrefixes = seenLt && seenFt;
            isFertigungstermin = seenFt && !seenLt;
            if (parsed.HasValue)
            {
                currentDate = parsed.Value;
                return true;
            }

            return false;
        }

        public static DeliveryDateRewriteResult Rewrite(
            string pdfFolder,
            IList<string> filesToUpdate,
            DateTime newDate,
            DeliveryDatePrefixMode prefixMode,
            IList<string> allOrderedFilesForMerge = null)
        {
            if (string.IsNullOrWhiteSpace(pdfFolder) || !Directory.Exists(pdfFolder))
                throw new InvalidOperationException("Ablage-Ordner nicht gefunden.");

            var toUpdate = (filesToUpdate ?? Array.Empty<string>())
                .Where(File.Exists)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (toUpdate.Count == 0)
                throw new InvalidOperationException("Keine sortierten Zeichnungen gefunden. Bitte zuerst sortieren.");

            var mergeList = (allOrderedFilesForMerge ?? toUpdate)
                .Where(File.Exists)
                .ToList();
            if (mergeList.Count == 0)
                mergeList = toUpdate;

            var euDate = newDate.ToString("dd.MM.yyyy", CultureInfo.GetCultureInfo("de-DE"));
            var updated = 0;
            var skipped = 0;
            var previousBatch = WatermarkProcessingContext.BatchMode;
            WatermarkProcessingContext.BatchMode = true;
            try
            {
                foreach (var pdfPath in toUpdate)
                {
                    var items = WatermarkEditorService.LoadItems(new[] { pdfPath });
                    var item = items.FirstOrDefault();
                    if (item == null)
                    {
                        skipped++;
                        continue;
                    }

                    item.EncodedWatermarks = RewriteEncoded(item.EncodedWatermarks, euDate, prefixMode);
                    var placements = WatermarkEditorService.LoadExistingPlacements(item);
                    var previous = ClonePlacements(placements);
                    ApplyDateToPlacements(placements, item.EncodedWatermarks, euDate, prefixMode);
                    PdfWatermarkService.ReplacePlacementTextsOnExistingPdf(
                        item.PdfPath,
                        previous,
                        placements,
                        "Date",
                        "Quantity");
                    WatermarkMetadataStore.Save(
                        item.PdfPath,
                        item.DrawingNumber,
                        item.EncodedWatermarks,
                        placements);
                    updated++;
                }
            }
            finally
            {
                WatermarkProcessingContext.BatchMode = previousBatch;
            }

            if (updated == 0)
            {
                throw new InvalidOperationException(
                    "Kein Liefertermin geändert. Es wurden keine sortierten Zeichnungen gefunden.");
            }

            foreach (var pdfPath in toUpdate)
                WatermarkEditorService.RestoreBookPageNumber(pdfPath, mergeList);

            var mergedPath = WatermarkEditorService.RemergeFolder(pdfFolder, mergeList);

            return new DeliveryDateRewriteResult
            {
                UpdatedCount = updated,
                SkippedCount = skipped,
                MergedPdfPath = mergedPath,
                OrderedFiles = mergeList
            };
        }

        private static List<WatermarkTextPlacement> ClonePlacements(IList<WatermarkTextPlacement> source)
        {
            return (source ?? new List<WatermarkTextPlacement>())
                .Where(p => p != null)
                .Select(p => new WatermarkTextPlacement
                {
                    Category = p.Category,
                    Text = p.Text,
                    X = p.X,
                    Y = p.Y,
                    Alignment = p.Alignment,
                    FontSize = p.FontSize,
                    Angle = p.Angle,
                    UseRedText = p.UseRedText
                })
                .ToList();
        }

        private static void ApplyDateToPlacements(
            IList<WatermarkTextPlacement> placements,
            string encodedWatermarks,
            string euDate,
            DeliveryDatePrefixMode prefixMode)
        {
            if (placements == null)
                return;

            var entries = PdfWatermarkService.ParseWatermarkEntries(encodedWatermarks);
            var quantityLines = BuildQuantityLines(entries, euDate, prefixMode);
            var quantityIndex = 0;

            foreach (var placement in placements)
            {
                if (placement == null)
                    continue;

                if (string.Equals(placement.Category, "Quantity", StringComparison.OrdinalIgnoreCase))
                {
                    if (quantityIndex < quantityLines.Count)
                    {
                        placement.Text = quantityLines[quantityIndex];
                        quantityIndex++;
                    }
                    else
                    {
                        placement.Text = ReplaceDateToken(placement.Text, euDate, prefixMode);
                    }

                    continue;
                }

                if (string.Equals(placement.Category, "Date", StringComparison.OrdinalIgnoreCase))
                {
                    var source = !string.IsNullOrWhiteSpace(placement.Text)
                        ? placement.Text
                        : "LT-" + euDate;
                    placement.Text = ComposeDateToken(source, euDate, prefixMode);
                }
            }
        }

        private static List<string> BuildQuantityLines(
            IList<(string quantity, string deliveryDate, string deliveryLocation, string bgDrawingNumber, string orderNumber)> entries,
            string euDate,
            DeliveryDatePrefixMode prefixMode)
        {
            var lines = new List<string>();
            if (entries == null)
                return lines;

            foreach (var entry in entries)
            {
                var qty = NormalizeQuantity(entry.quantity);
                if (string.IsNullOrWhiteSpace(qty))
                    continue;

                var token = ComposeDateToken(entry.deliveryDate, euDate, prefixMode);
                lines.Add(string.IsNullOrWhiteSpace(token) ? qty : token + "   " + qty);
            }

            return lines;
        }

        private static string RewriteEncoded(string encoded, string euDate, DeliveryDatePrefixMode prefixMode)
        {
            var entries = PdfWatermarkService.ParseWatermarkEntries(encoded);
            if (entries.Count == 0)
                return encoded ?? string.Empty;

            return string.Join("§§§", entries.Select(e => string.Join("|",
                Escape(e.quantity),
                Escape(ComposeDateToken(e.deliveryDate, euDate, prefixMode)),
                Escape(e.deliveryLocation),
                Escape(e.bgDrawingNumber),
                Escape(e.orderNumber))));
        }

        private static string ComposeDateToken(string existing, string euDate, DeliveryDatePrefixMode prefixMode)
        {
            var manufacturing = prefixMode == DeliveryDatePrefixMode.Fertigungstermin
                || (prefixMode == DeliveryDatePrefixMode.KeepExisting && LooksLikeFertigung(existing));

            return (manufacturing ? "FT-" : "LT-") + euDate;
        }

        private static string ReplaceDateToken(string text, string euDate, DeliveryDatePrefixMode prefixMode)
        {
            if (string.IsNullOrWhiteSpace(text))
                return text;

            var token = ComposeDateToken(text, euDate, prefixMode);
            if (DateTokenRegex.IsMatch(text))
                return DateTokenRegex.Replace(text, token);

            return token + "   " + text.Trim();
        }

        private static bool LooksLikeFertigung(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return false;

            var t = value.Trim();
            return t.StartsWith("FT-", StringComparison.OrdinalIgnoreCase)
                || t.StartsWith("FT:", StringComparison.OrdinalIgnoreCase);
        }

        private static bool LooksLikeLiefer(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return false;

            var t = value.Trim();
            return t.StartsWith("LT-", StringComparison.OrdinalIgnoreCase)
                || t.StartsWith("LT:", StringComparison.OrdinalIgnoreCase);
        }

        private static string NormalizeQuantity(string quantity)
        {
            if (string.IsNullOrWhiteSpace(quantity))
                return string.Empty;

            var q = quantity.Trim();
            if (q.EndsWith("X", StringComparison.OrdinalIgnoreCase))
                q = q.Substring(0, q.Length - 1) + "x";
            else
                q += "x";

            return q;
        }

        private static string Escape(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;

            return value.Replace("\\", "\\\\").Replace("|", "\\|").Trim();
        }
    }
}
