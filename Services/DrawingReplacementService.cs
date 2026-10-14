using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using PDF_umbenenen.Models;

namespace PDF_umbenenen.Services
{
    internal enum DrawingReplacementStatus
    {
        Replaced,
        NoSortedDrawings,
        NoDrawingNumber,
        NotInOrder
    }

    internal sealed class DrawingReplacementResult
    {
        public DrawingReplacementStatus Status { get; set; }

        public bool Replaced => Status == DrawingReplacementStatus.Replaced;

        public string TargetPdfPath { get; set; }

        public string IncomingDrawingNumber { get; set; }

        public string TargetDrawingNumber { get; set; }

        public string Message { get; set; }
    }

    internal static class DrawingReplacementService
    {
        private static readonly Regex NumberedSortedNameRegex = new Regex(
            @"^\d{1,3}_",
            RegexOptions.CultureInvariant | RegexOptions.Compiled);

        public static bool IsWorkspaceDrawingPdf(string path)
        {
            var name = Path.GetFileName(path) ?? string.Empty;
            if (string.IsNullOrWhiteSpace(name))
                return false;
            if (!name.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
                return false;
            if (name.EndsWith(WatermarkEditorService.PreWatermarkSuffix, StringComparison.OrdinalIgnoreCase))
                return false;
            if (name.StartsWith("_", StringComparison.OrdinalIgnoreCase))
                return false;
            if (OrderVerificationFileHelper.IsOrderVerificationFileName(name))
                return false;
            return true;
        }

        public static List<string> ListOrderDrawingPdfs(string folder)
        {
            var result = new List<string>();
            if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
                return result;

            foreach (var path in Directory.GetFiles(folder, "*.pdf", SearchOption.TopDirectoryOnly))
            {
                if (IsWorkspaceDrawingPdf(path))
                    result.Add(path);
            }

            return result;
        }

        public static List<string> ListSortedDrawingPdfs(string folder)
        {
            return ListOrderDrawingPdfs(folder)
                .Where(path => NumberedSortedNameRegex.IsMatch(Path.GetFileName(path) ?? string.Empty))
                .ToList();
        }

        public static string GuessDrawingNumber(string pdfPath)
        {
            if (string.IsNullOrWhiteSpace(pdfPath))
                return string.Empty;

            var fromName = OrderVerificationDrawingKey.ExtractPrimaryFromTexts(FileNameSearchText(pdfPath));
            if (!string.IsNullOrWhiteSpace(fromName))
                return fromName;

            if (File.Exists(pdfPath)
                && WatermarkMetadataStore.TryLoad(pdfPath, out var metaNumber, out _)
                && !string.IsNullOrWhiteSpace(metaNumber))
            {
                var fromMeta = OrderVerificationDrawingKey.ExtractPrimaryFromTexts(metaNumber);
                if (!string.IsNullOrWhiteSpace(fromMeta))
                    return fromMeta;
            }

            try
            {
                var pageText = PdfLayoutAnalysisService.ExtractPageText(pdfPath, 1);
                var fromPdf = OrderVerificationDrawingKey.ExtractPrimaryFromTexts(pageText);
                if (!string.IsNullOrWhiteSpace(fromPdf))
                    return fromPdf;
            }
            catch
            {
            }

            return string.Empty;
        }

        public static string FindMatchingSortedPdf(IEnumerable<string> sortedPdfs, string incomingPdfPath)
        {
            var incomingNumber = GuessDrawingNumber(incomingPdfPath);
            if (string.IsNullOrWhiteSpace(incomingNumber))
                return null;

            foreach (var path in sortedPdfs ?? Enumerable.Empty<string>())
            {
                if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                    continue;

                if (OrderVerificationDrawingKey.Equal(incomingNumber, GuessDrawingNumber(path)))
                    return path;
            }

            return null;
        }

        private static string FileNameSearchText(string pdfPath)
        {
            var name = Path.GetFileName(pdfPath) ?? string.Empty;
            if (name.EndsWith(WatermarkEditorService.PreWatermarkSuffix, StringComparison.OrdinalIgnoreCase))
                name = name.Substring(0, name.Length - WatermarkEditorService.PreWatermarkSuffix.Length) + ".pdf";

            var stem = Path.GetFileNameWithoutExtension(name) ?? string.Empty;
            while (stem.Length >= 3)
            {
                var us = stem.IndexOf('_');
                if (us < 1 || us > 3)
                    break;
                var prefix = stem.Substring(0, us);
                var allDigits = true;
                for (var i = 0; i < prefix.Length; i++)
                {
                    if (!char.IsDigit(prefix[i]))
                    {
                        allDigits = false;
                        break;
                    }
                }

                if (!allDigits)
                    break;

                stem = stem.Substring(us + 1);
            }

            return stem.Replace('_', ' ');
        }

        public static void ReplaceKeepingWatermarks(
            string sortedPdfPath,
            string incomingPdfPath,
            IEnumerable<string> orderedPdfPathsForPageNumbers = null)
        {
            if (string.IsNullOrWhiteSpace(sortedPdfPath) || !File.Exists(sortedPdfPath))
                throw new InvalidOperationException("Die vorhandene Zeichnung wurde nicht gefunden.");
            if (string.IsNullOrWhiteSpace(incomingPdfPath) || !File.Exists(incomingPdfPath))
                throw new InvalidOperationException("Die neue Zeichnung wurde nicht gefunden.");
            if (!string.Equals(Path.GetExtension(incomingPdfPath), ".pdf", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Nur PDF-Dateien können eine Zeichnung ersetzen.");

            var items = WatermarkEditorService.LoadItems(new[] { sortedPdfPath });
            var item = items.FirstOrDefault();
            if (item == null)
                throw new InvalidOperationException("Die vorhandene Zeichnung konnte nicht gelesen werden.");

            var placements = WatermarkEditorService.LoadExistingPlacements(item);
            var settings = WatermarkSettingsStore.Load();
            var prewm = WatermarkEditorService.GetPreWatermarkPath(sortedPdfPath);
            var tempPrewm = prewm + ".replace.tmp.pdf";

            PdfRasterLayoutService.ClearCache();
            PdfLayoutAnalysisService.ClearCache();

            File.Copy(incomingPdfPath, tempPrewm, true);
            if (File.Exists(prewm))
                File.Delete(prewm);
            File.Move(tempPrewm, prewm);

            if (PdfWatermarkService.TryGetPageSize(prewm, 1, out var pageWidth, out var pageHeight))
            {
                item.PageWidth = pageWidth;
                item.PageHeight = pageHeight;
            }

            item.PreWatermarkPath = prewm;
            WatermarkEditorService.SaveItem(
                item,
                placements,
                settings,
                orderedPdfPathsForPageNumbers,
                keepEditorPlacements: true);
        }

        public static DrawingReplacementResult TryReplaceByDrawingNumber(
            string folder,
            string incomingPdfPath,
            IEnumerable<string> orderedPdfPathsForPageNumbers = null)
        {
            var result = new DrawingReplacementResult
            {
                IncomingDrawingNumber = GuessDrawingNumber(incomingPdfPath)
            };

            var drawings = ListOrderDrawingPdfs(folder);
            if (drawings.Count == 0)
            {
                result.Status = DrawingReplacementStatus.NoSortedDrawings;
                result.Message = "Keine Zeichnungen in der Ablage.";
                return result;
            }

            if (string.IsNullOrWhiteSpace(result.IncomingDrawingNumber))
            {
                result.Status = DrawingReplacementStatus.NoDrawingNumber;
                result.Message = "Auf der Datei wurde keine Zeichnungsnummer erkannt.\nDie defekte Zeichnung wird nicht ersetzt.";
                return result;
            }

            var target = FindMatchingSortedPdf(drawings, incomingPdfPath);
            if (string.IsNullOrWhiteSpace(target))
            {
                result.Status = DrawingReplacementStatus.NotInOrder;
                result.Message = "Die Zeichnungsnummer "
                    + result.IncomingDrawingNumber
                    + " ist in der Bestellung nicht vorhanden.\nDie defekte Zeichnung wird nicht ersetzt.";
                return result;
            }

            result.TargetPdfPath = target;
            result.TargetDrawingNumber = GuessDrawingNumber(target);
            var keepWatermarks = NumberedSortedNameRegex.IsMatch(Path.GetFileName(target) ?? string.Empty)
                || File.Exists(WatermarkEditorService.GetPreWatermarkPath(target));
            if (keepWatermarks)
            {
                ReplaceKeepingWatermarks(
                    target,
                    incomingPdfPath,
                    orderedPdfPathsForPageNumbers ?? ListSortedDrawingPdfs(folder));
            }
            else
            {
                File.Copy(incomingPdfPath, target, true);
            }

            result.Status = DrawingReplacementStatus.Replaced;
            result.Message = Path.GetFileName(target);
            return result;
        }

        public static string FormatReplacedConfirmation(string drawingNumber, string fileName)
        {
            if (!string.IsNullOrWhiteSpace(drawingNumber))
                return "Die Zeichnung " + drawingNumber + " wurde ersetzt.";

            if (!string.IsNullOrWhiteSpace(fileName))
                return "Die Zeichnung " + fileName + " wurde ersetzt.";

            return "Die Zeichnung wurde ersetzt.";
        }

        public static string FormatMismatchPageWarning(string incomingNumber, string pageNumber)
        {
            return "Die Datei ist "
                + incomingNumber
                + ", die Seite zeigt "
                + pageNumber
                + ".\nDie falsche Zeichnung wird nicht ersetzt.\nNur eine Datei mit derselben Zeichnungsnummer kann die defekte ersetzen.";
        }
    }
}
