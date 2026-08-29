using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using iTextSharp.text.pdf;
using PDF_umbenenen;
using PDF_umbenenen.Services;
using PDF_umbenenen.Services.Cli;

namespace PdfDiag
{
    internal static class Program
    {
        private static readonly Regex TeslaPartRegex = new Regex(
            @"TSL-\d{5}-\d{3}",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static int Main(string[] args)
        {
            if (args.Any(a => string.Equals(a, "--license-key", StringComparison.OrdinalIgnoreCase)))
                return RunLicenseKeyTool(args);

            var frameTestPath = GetArgValue(args, "--frame-test");
            if (!string.IsNullOrWhiteSpace(frameTestPath))
                return RunFrameBoundsTest(frameTestPath);

            var rulesOnly = args.Contains("--rules-only", StringComparer.OrdinalIgnoreCase);
            var dumpText = args.Contains("--dump-text", StringComparer.OrdinalIgnoreCase);
            var watermarkTestFolder = GetArgValue(args, "--watermark-test");
            if (!string.IsNullOrWhiteSpace(watermarkTestFolder))
                return RunWatermarkPlacementTest(watermarkTestFolder);

            var batchFolder = GetArgValue(args, "--batch");
            if (!string.IsNullOrWhiteSpace(batchFolder))
            {
                return RunBatch(batchFolder, rulesOnly);
            }

            var pdfPath = args.FirstOrDefault(a => !a.StartsWith("--", StringComparison.Ordinal))
                ?? @"E:\Programierung\PDF Sortieren\bin\Debug\Ablage\_GESAMT_20260619_155625.pdf";

            HashSet<int> onlyPages = null;
            var pageArgs = args
                .Where(a => !a.StartsWith("--", StringComparison.Ordinal))
                .Skip(1)
                .ToList();
            if (pageArgs.Count > 0)
                onlyPages = new HashSet<int>(pageArgs.Select(int.Parse));

            PdfMaterialExtractionService.ClearCache();
            PdfLayoutAnalysisService.ClearCache();
            PdfVectorOccupancyService.ClearCache();
            PdfRasterLayoutService.ClearCache();

            Console.WriteLine("OCR (Tesseract): " + (TesseractCliService.IsAvailable() ? "OK" : "NICHT GEFUNDEN"));
            Console.WriteLine("Raster: " + PdfRasterLayoutService.IsAvailable);
            Console.WriteLine();

            using (var reader = new PdfReader(pdfPath))
            {
                Console.WriteLine("PDF: " + pdfPath);
                Console.WriteLine("Pages: " + reader.NumberOfPages);
                Console.WriteLine(new string('=', 110));

                var finalHits = 0;
                var materialHits = 0;
                var classHits = 0;
                var assembly = 0;

                for (var page = 1; page <= reader.NumberOfPages; page++)
                {
                    if (onlyPages != null && !onlyPages.Contains(page))
                        continue;

                    var stats = DumpPage(pdfPath, page, rulesOnly, dumpText);
                    if (stats.HasFinalThickness) finalHits++;
                    if (stats.HasMaterial) materialHits++;
                    if (stats.HasClass) classHits++;
                    if (stats.IsAssembly) assembly++;
                }

                Console.WriteLine(new string('=', 110));
                Console.WriteLine($"Summary | thickness: {finalHits} | material: {materialHits} | class ST/VA/ALU: {classHits} | assembly empty: {assembly}");
            }

            return 0;
        }

        private struct PageStats
        {
            public bool HasFinalThickness;
            public bool HasMaterial;
            public bool HasClass;
            public bool IsAssembly;
        }

        private static string GetArgValue(string[] args, string key)
        {
            for (var i = 0; i < args.Length - 1; i++)
            {
                if (string.Equals(args[i], key, StringComparison.OrdinalIgnoreCase))
                    return args[i + 1];
            }

            return null;
        }

        private static int RunLicenseKeyTool(string[] args)
        {
            var machineCode = GetArgValue(args, "--license-key");
            if (string.Equals(machineCode, "--license-key", StringComparison.OrdinalIgnoreCase)
                || string.IsNullOrWhiteSpace(machineCode))
            {
                // Aufruf nur mit --license-key ohne Wert → Schlüssel für diesen PC + Master
                machineCode = null;
            }

            Console.WriteLine("DOK-V01 Lizenzschlüssel");
            Console.WriteLine("PC-Code (dieser Rechner): " + LicenseActivationService.GetMachineCode());
            Console.WriteLine("Maschinen-Schlüssel:     " + LicenseActivationService.GenerateMachineKey(machineCode));
#if DEBUG
            Console.WriteLine("Universal-Schlüssel:     " + LicenseActivationService.GenerateMasterKey());
#endif
            Console.WriteLine();
            Console.WriteLine("Hinweis: Für einen fremden PC den Kunden-PC-Code angeben:");
            Console.WriteLine("  PdfDiag --license-key ABCD-EF01-2345");
            return 0;
        }

        private static int RunWatermarkPlacementTest(string folder)
        {
            if (!Directory.Exists(folder))
            {
                Console.WriteLine("Ordner nicht gefunden: " + folder);
                return 1;
            }

            PdfLayoutAnalysisService.ClearCache();
            PdfVectorOccupancyService.ClearCache();
            PdfRasterLayoutService.ClearCache();

            var settings = WatermarkSettingsStore.Load();
            Console.WriteLine("Wasserzeichen-Testauftrag");
            Console.WriteLine("Ordner: " + folder);
            Console.WriteLine("Intelligente Platzierung: " + (settings.UseSmartPlacement ? "AN" : "AUS"));
            Console.WriteLine("PDF-Bildanalyse (Raster): " + (PdfRasterLayoutService.IsAvailable ? "VERFUEGBAR" : "NICHT VERFUEGBAR"));
            Console.WriteLine(new string('=', 120));

            var files = Directory.GetFiles(folder, "*.pdf")
                .Where(f => !Path.GetFileName(f).StartsWith("_", StringComparison.OrdinalIgnoreCase))
                .Where(f => !f.EndsWith(".prewm.pdf", StringComparison.OrdinalIgnoreCase))
                .Where(f => !string.Equals(Path.GetFileName(f), "Bestellung.pdf", StringComparison.OrdinalIgnoreCase))
                .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (files.Count == 0)
            {
                Console.WriteLine("Keine PDF-Zeichnungen im Ordner.");
                Console.WriteLine("Bitte PDFs und Daten.xlsx in die Ablage legen:");
                Console.WriteLine(folder);
                return 2;
            }

            var sampleEntries = new List<(string quantity, string deliveryDate, string deliveryLocation, string bgDrawingNumber, string orderNumber)>
            {
                ("30X", "22.06.2026", "Neuwied", "BG-12345", "TEST-2026-001")
            };
            var fieldData = SmartWatermarkLayoutService.BuildFieldData(sampleEntries, new List<string> { "TEST-2026-001" });

            var okCount = 0;
            var warnCount = 0;

            WatermarkProcessingContext.BatchMode = true;
            try
            {
            foreach (var file in files)
            {
                var analysisPath = file;
                var prewm = file + ".prewm.pdf";
                if (File.Exists(prewm))
                    analysisPath = prewm;

                var drawing = GuessDrawingNumber(file);
                if (!PdfWatermarkService.TryGetPageSize(analysisPath, 1, out var pageWidth, out var pageHeight))
                {
                    pageWidth = 595f;
                    pageHeight = 842f;
                }

                var placements = SmartWatermarkLayoutService.Compute(analysisPath, 1, fieldData, drawing, pageWidth, pageHeight, settings);
                MaterialWatermarkGuardService.EnforceFinalLockBeforeWrite(
                    placements, analysisPath, 1, pageWidth, pageHeight, drawing);
                var learned = WatermarkLearningStore.TryGet(drawing);
                var source = learned != null ? "Zeichnung+KI" : "KI-Kategorie";

                Console.WriteLine();
                Console.WriteLine(Path.GetFileName(file) + " | Zeichnung: " + drawing + " | Lernquelle: " + source);

                if (placements.Count == 0)
                {
                    Console.WriteLine("  WARNUNG: Keine Wasserzeichen-Positionen berechnet.");
                    warnCount++;
                    continue;
                }

                var fileOk = true;
                var materialPlacements = placements
                    .Where(p => p != null && string.Equals(p.Category, "Material", StringComparison.OrdinalIgnoreCase))
                    .ToList();
                var materialTested = false;

                foreach (var placement in placements)
                {
                    if (string.IsNullOrWhiteSpace(placement?.Text))
                        continue;

                    var isMaterial = string.Equals(placement.Category, "Material", StringComparison.OrdinalIgnoreCase);
                    if (isMaterial)
                    {
                        if (materialTested)
                            continue;

                        materialTested = true;
                        var topLine = materialPlacements
                            .OrderByDescending(p => p.Y)
                            .FirstOrDefault();
                        if (topLine == null)
                            continue;

                        var matVerdict = MaterialWatermarkGuardService.ValidateMaterialStack(
                            materialPlacements, analysisPath, 1, pageWidth, pageHeight, strictDarkness: false);

                        var matDarkness = matVerdict.Darkness;
                        var matOk = matVerdict.IsGeometrySafe;
                        var matStatus = matVerdict.ToShortGerman();
                        var matRect = matVerdict.Bounds?.ToRectString() ?? "-";
                        if (!matOk)
                        {
                            fileOk = false;
                            warnCount++;
                        }

                        Console.WriteLine(string.Format(
                            "  {0,-10} rel=({1:0.00},{2:0.00}) dunkel={3:0.000} {4} rect={5} | {6}",
                            "Material",
                            topLine.X / pageWidth,
                            topLine.Y / pageHeight,
                            matDarkness,
                            matStatus,
                            matRect,
                            TrimText(string.Join(" / ", materialPlacements.Select(p => p.Text)), 40)));
                        continue;
                    }

                    var blockWidth = Math.Max(20f, (placement.Text?.Length ?? 1) * placement.FontSize * 0.55f);
                    var blockHeight = placement.FontSize + 8f;
                    var darkness = PdfRasterLayoutService.IsAvailable
                        ? PdfRasterLayoutService.MeasurePlacementDarkness(
                            analysisPath, 1, placement.X, placement.Y, blockWidth, blockHeight, pageWidth, pageHeight, placement.Alignment)
                        : -1f;

                    var acceptable = !PdfRasterLayoutService.IsAvailable
                        || PdfRasterLayoutService.IsPlacementAcceptable(
                            analysisPath, 1, placement.X, placement.Y, blockWidth, blockHeight, pageWidth, pageHeight, placement.Alignment, isMaterial: false);

                    var status = acceptable ? "OK" : "AUF BAUTEIL";
                    if (!acceptable)
                    {
                        fileOk = false;
                        warnCount++;
                    }

                    Console.WriteLine(string.Format(
                        "  {0,-10} rel=({1:0.00},{2:0.00}) dunkel={3:0.000} {4} | {5}",
                        placement.Category,
                        placement.X / pageWidth,
                        placement.Y / pageHeight,
                        darkness,
                        status,
                        TrimText(placement.Text, 40)));
                }

                if (fileOk)
                    okCount++;
            }
            }
            finally
            {
                WatermarkProcessingContext.BatchMode = false;
            }

            Console.WriteLine();
            Console.WriteLine(new string('=', 120));
            Console.WriteLine("Ergebnis: " + okCount + " PDF(s) frei platziert | " + warnCount + " Problem(e)");
            Console.WriteLine("Tipp: Eine Zeichnung in der Vorschau korrigieren -> KI merkt Position fuer alle weiteren.");
            return warnCount > 0 ? 3 : 0;
        }

        private static string GuessDrawingNumber(string pdfPath)
        {
            var fileName = Path.GetFileNameWithoutExtension(pdfPath) ?? string.Empty;
            var match = Regex.Match(fileName, @"^\d+[_\-\s]+(.+)$");
            return match.Success ? match.Groups[1].Value.Trim() : fileName.Trim();
        }

        private static string TrimText(string text, int max)
        {
            if (string.IsNullOrWhiteSpace(text))
                return string.Empty;

            text = text.Replace("\r", " ").Replace("\n", " ");
            return text.Length <= max ? text : text.Substring(0, max - 3) + "...";
        }

        private static int RunBatch(string folder, bool rulesOnly)
        {
            if (!Directory.Exists(folder))
            {
                Console.WriteLine("Ordner nicht gefunden: " + folder);
                return 1;
            }

            PdfMaterialExtractionService.ClearCache();
            PdfLayoutAnalysisService.ClearCache();
            PdfVectorOccupancyService.ClearCache();
            PdfRasterLayoutService.ClearCache();

            Console.WriteLine("Batch | Ordner: " + folder);
            Console.WriteLine("Modus: " + (rulesOnly ? "nur Regeln (ohne OCR)" : "Regeln + OCR (Tesseract)"));
            Console.WriteLine("OCR (Tesseract): " + (TesseractCliService.IsAvailable() ? "OK" : "NICHT GEFUNDEN"));
            Console.WriteLine(new string('=', 120));
            Console.WriteLine(string.Format("{0,-32} {1,-18} {2,-12} {3}", "Datei", "Watermark", "Dicke", "Material"));
            Console.WriteLine(new string('-', 120));

            var files = Directory.GetFiles(folder, "*.pdf")
                .Where(f => !Path.GetFileName(f).StartsWith("_", StringComparison.OrdinalIgnoreCase))
                .Where(f => !string.Equals(Path.GetFileName(f), "Bestellung.pdf", StringComparison.OrdinalIgnoreCase))
                .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var missing = 0;
            foreach (var file in files)
            {
                var combined = PdfMaterialExtractionService.Extract(file, 1);
                var name = Path.GetFileName(file);
                var wm = combined.MaterialWatermarkText ?? string.Empty;
                var th = combined.ThicknessText ?? string.Empty;
                var mat = combined.MaterialType ?? string.Empty;
                if (string.IsNullOrWhiteSpace(wm))
                    missing++;

                if (mat.Length > 28)
                    mat = mat.Substring(0, 25) + "...";

                Console.WriteLine(string.Format("{0,-32} {1,-18} {2,-12} {3}", name, wm, th, mat));
            }

            Console.WriteLine(new string('=', 120));
            Console.WriteLine("Dateien: " + files.Count + " | ohne Wasserzeichen-Material: " + missing);
            return 0;
        }

        private static void DumpRelevantText(string pdfPath, int page, PDF_umbenenen.Services.PdfMaterialInfo info)
        {
            var layout = PdfLayoutAnalysisService.AnalyzePage(pdfPath, page);
            var pageText = PdfLayoutAnalysisService.ExtractPageText(pdfPath, page) ?? string.Empty;
            Console.WriteLine("--- relevant page text ---");
            foreach (var line in pageText.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var u = line.ToUpperInvariant();
                if (u.Contains("THK") || u.Contains("THICK") || u.Contains("DICKE") || u.Contains(" T=")
                    || u.Contains("MM") || u.Contains("AUSSEN") || u.Contains("MATERIAL") || u.Contains("PLATE")
                    || u.Contains("SHEET") || u.Contains("SHIM") || u.Contains("MAT."))
                    Console.WriteLine("  " + line.Trim());
            }

            Console.WriteLine("--- text rects (numeric / THK) ---");
            var titleTop = layout.PageHeight * 0.26f;
            foreach (var r in layout.TextRects.OrderBy(x => x.Bottom))
            {
                var t = (r.Text ?? string.Empty).Trim();
                if (string.IsNullOrWhiteSpace(t))
                    continue;
                if (!Regex.IsMatch(t, @"\d") && t.IndexOf("THK", StringComparison.OrdinalIgnoreCase) < 0
                    && t.IndexOf("MAT", StringComparison.OrdinalIgnoreCase) < 0)
                    continue;

                var y = (r.Bottom + r.Top) / 2f / layout.PageHeight;
                var x = (r.Left + r.Right) / 2f / layout.PageWidth;
                var zone = r.Bottom <= titleTop ? "title" : "draw";
                Console.WriteLine(string.Format("  {0} x={1:0.00} y={2:0.00} | {3}", zone, x, y, t));
            }

            Console.WriteLine("--- extract ---");
            Console.WriteLine("  watermark: " + (info.MaterialWatermarkText ?? string.Empty));
            Console.WriteLine("  thickness: " + (info.ThicknessText ?? string.Empty));
            Console.WriteLine("  material:  " + (info.MaterialType ?? string.Empty));
        }

        private static PageStats DumpPage(string pdfPath, int page, bool rulesOnly, bool dumpText)
        {
            var text = PdfLayoutAnalysisService.ExtractPageText(pdfPath, page) ?? string.Empty;
            var tslNumbers = TeslaPartRegex.Matches(text)
                .Cast<Match>()
                .Select(m => m.Value.ToUpperInvariant())
                .Distinct()
                .ToArray();
            var primaryTsl = tslNumbers.FirstOrDefault() ?? string.Empty;

            var combined = PdfMaterialExtractionService.Extract(pdfPath, page);

            if (dumpText)
                DumpRelevantText(pdfPath, page, combined);

            var stats = new PageStats
            {
                HasFinalThickness = !string.IsNullOrWhiteSpace(combined.ThicknessText),
                HasMaterial = !string.IsNullOrWhiteSpace(combined.MaterialType),
                HasClass = !string.IsNullOrWhiteSpace(combined.MaterialAlloyPrefix),
                IsAssembly = combined.IsAssembly || PdfMaterialExtractionService.DiagnoseIsAssemblyDrawing(pdfPath, page)
            };

            Console.WriteLine($"Page {page,2} | {primaryTsl}");
            Console.WriteLine($"         Assembly: {PdfMaterialExtractionService.DiagnoseIsAssemblyDrawing(pdfPath, page)} | Tube: {PdfMaterialExtractionService.DiagnoseIsTubePart(pdfPath, page)}");
            Console.WriteLine($"         OCR: {TitleBlockOcrService.LastError ?? "-"}");
            Console.WriteLine($"         Final  -> watermark:'{combined.MaterialWatermarkText ?? ""}' | thickness:'{combined.ThicknessText ?? ""}' | material:'{combined.MaterialType ?? ""}' | class:'{combined.MaterialAlloyPrefix ?? ""}'");
            Console.WriteLine(new string('-', 110));
            return stats;
        }

        private static int RunFrameBoundsTest(string pdfPath)
        {
            if (!File.Exists(pdfPath))
            {
                Console.WriteLine("Datei nicht gefunden: " + pdfPath);
                return 2;
            }

            PdfRasterLayoutService.ClearCache();
            Console.WriteLine("Raster: " + PdfRasterLayoutService.IsAvailable);
            Console.WriteLine("Status: " + PdfRasterLayoutService.GetStatus());
            Console.WriteLine("PDF: " + pdfPath);

            if (!PdfWatermarkService.TryGetPageSize(pdfPath, 1, out var pageWidth, out var pageHeight))
            {
                pageWidth = 595f;
                pageHeight = 842f;
            }

            Console.WriteLine($"PageSize: {pageWidth:0.###} x {pageHeight:0.###}");

            var freeAbove = PdfRasterLayoutService.MeasureFreeTopMarginPt(pdfPath, 1, pageWidth, pageHeight);
            Console.WriteLine($"FreeTopMarginPt: {freeAbove:0.###}");

            PdfRasterLayoutService.DumpTopMarginProfile(pdfPath, 1, pageWidth, pageHeight);

            var ok = PdfRasterLayoutService.TryGetDrawingFrameBounds(
                pdfPath, 1, pageWidth, pageHeight,
                out var left, out var right, out var top);
            Console.WriteLine($"FrameBounds ok={ok} left={left:0.###} right={right:0.###} top={top:0.###}");

            var bandY = WatermarkPageScale.GetSharedTopBandBaselineY(pageWidth, pageHeight, 28f, pdfPath, 1, 1);
            Console.WriteLine($"SharedTopBandBaselineY: {bandY:0.###}");
            Console.WriteLine($"FallbackTopLeftMargin: {WatermarkPageScale.GetTopLeftMargin(pageWidth, pageHeight):0.###}");
            Console.WriteLine($"FallbackMaxTopBaselineY: {WatermarkPageScale.GetMaxTopBaselineY(pageWidth, pageHeight, 28f):0.###}");

            // Liefertermin-Position prüfen (links vs. rechts unter Auftrag).
            // BatchMode: kein schweres Material-/Vision-Layout — nur Top-Band-Reflow.
            WatermarkProcessingContext.BatchMode = true;
            try
            {
                foreach (var side in new[] { DeliveryDatePosition.TopLeft, DeliveryDatePosition.TopRight })
                {
                    WatermarkProcessingContext.DeliveryDatePosition = side;
                    var workPdf = pdfPath;
                    string tempPdf = null;
                    try
                    {
                        if (side == DeliveryDatePosition.TopRight)
                        {
                            tempPdf = Path.Combine(Path.GetTempPath(), "dok-frame-topright-" + Guid.NewGuid().ToString("N") + ".pdf");
                            File.Copy(pdfPath, tempPdf, true);
                            PdfPageMarginExpansionService.TryExpandSourceInPlace(
                                tempPdf, 1, pageWidth, pageHeight);
                            workPdf = tempPdf;
                            PdfRasterLayoutService.ClearCache();
                        }

                        if (!PdfWatermarkService.TryGetPageSize(workPdf, 1, out var w, out var h))
                        {
                            w = pageWidth;
                            h = pageHeight;
                        }

                        var fields = SmartWatermarkLayoutService.BuildFieldData(
                            new List<(string, string, string, string, string)>
                            {
                                ("32X", "02.07.2026", "Neuwied", "", "TESTORDER")
                            },
                            new List<string> { "TESTORDER" });
                        var settings = WatermarkSettingsStore.Load();
                        settings.ExtractMaterialType = false;
                        settings.ExtractMaterialThickness = false;
                        settings.ExtractPowderCoating = false;
                        settings.ExtractAnodizing = false;
                        var placements = SmartWatermarkLayoutService.Compute(
                            workPdf, 1, fields, "TEST", w, h, settings);
                        var q = placements.FirstOrDefault(p => p != null && p.Category == "Quantity");
                        var o = placements.FirstOrDefault(p => p != null && p.Category == "Order");
                        var orderOnPage = o != null && o.Y <= h && o.Y > 0f;
                        var qtyOnPage = q != null && q.Y <= h && q.Y > 0f;
                        var qtyInside = false;
                        if (q != null
                            && PdfRasterLayoutService.TryGetDrawingFrameBounds(
                                workPdf, 1, w, h, out _, out _, out var ft))
                        {
                            qtyInside = q.Y < ft;
                        }

                        Console.WriteLine(
                            $"Delivery={side}: Qty x={q?.X:0.#} y={q?.Y:0.#} align={q?.Alignment} onPage={qtyOnPage} inside={qtyInside} | Order x={o?.X:0.#} y={o?.Y:0.#} align={o?.Alignment} onPage={orderOnPage} pageH={h:0.#}");
                    }
                    finally
                    {
                        if (!string.IsNullOrEmpty(tempPdf) && File.Exists(tempPdf))
                        {
                            try { File.Delete(tempPdf); } catch { /* ignore */ }
                        }
                    }
                }
            }
            finally
            {
                WatermarkProcessingContext.BatchMode = false;
                WatermarkProcessingContext.ClearDeliveryDatePositionOverride();
            }

            return ok ? 0 : 3;
        }
    }
}
