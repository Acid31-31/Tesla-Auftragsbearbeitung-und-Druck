using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using iTextSharp.text.pdf;
using PDF_umbenenen.Services;
using PDF_umbenenen.Services.Ai;

namespace PdfDiag
{
    internal static class Program
    {
        private static readonly Regex TeslaPartRegex = new Regex(
            @"TSL-\d{5}-\d{3}",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static int Main(string[] args)
        {
            var rulesOnly = args.Contains("--rules-only", StringComparer.OrdinalIgnoreCase);
            var dumpText = args.Contains("--dump-text", StringComparer.OrdinalIgnoreCase);
            var batchFolder = GetArgValue(args, "--batch");
            if (!string.IsNullOrWhiteSpace(batchFolder))
            {
                return RunBatch(batchFolder, rulesOnly);
            }

            var pdfPath = args.FirstOrDefault(a => !a.StartsWith("--", StringComparison.Ordinal))
                ?? @"E:\Materialleiter Programierung\Zeiterfasung_R00\herko-r00\PDF-umbenenen\bin\Debug\Ablage\_GESAMT_20260619_155625.pdf";

            HashSet<int> onlyPages = null;
            var pageArgs = args
                .Where(a => !a.StartsWith("--", StringComparison.Ordinal))
                .Skip(1)
                .ToList();
            if (pageArgs.Count > 0)
                onlyPages = new HashSet<int>(pageArgs.Select(int.Parse));

            PdfMaterialExtractionService.ClearCache();
            PdfLayoutAnalysisService.ClearCache();
            if (!rulesOnly)
                OllamaMaterialThicknessService.ClearCache();
            PdfRasterLayoutService.ClearCache();

            Console.WriteLine("Ollama: " + (rulesOnly ? "SKIP" : (CheckOllama() ? "OK" : "NICHT ERREICHBAR")));
            Console.WriteLine("Raster: " + PdfRasterLayoutService.IsAvailable);
            Console.WriteLine();

            using (var reader = new PdfReader(pdfPath))
            {
                Console.WriteLine("PDF: " + pdfPath);
                Console.WriteLine("Pages: " + reader.NumberOfPages);
                Console.WriteLine(new string('=', 110));

                var ollamaHits = 0;
                var finalHits = 0;
                var materialHits = 0;
                var classHits = 0;
                var assembly = 0;

                for (var page = 1; page <= reader.NumberOfPages; page++)
                {
                    if (onlyPages != null && !onlyPages.Contains(page))
                        continue;

                    var stats = DumpPage(pdfPath, page, rulesOnly, dumpText);
                    if (stats.OllamaUsed) ollamaHits++;
                    if (stats.HasFinalThickness) finalHits++;
                    if (stats.HasMaterial) materialHits++;
                    if (stats.HasClass) classHits++;
                    if (stats.IsAssembly) assembly++;
                }

                Console.WriteLine(new string('=', 110));
                Console.WriteLine($"Summary | thickness: {finalHits} | material: {materialHits} | class ST/VA/ALU: {classHits} | assembly empty: {assembly} | ollama: {ollamaHits}");
            }

            return 0;
        }

        private static bool CheckOllama()
        {
            try
            {
                using (var client = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(5) })
                {
                    var resp = client.GetAsync("http://localhost:11434/api/tags").GetAwaiter().GetResult();
                    return resp.IsSuccessStatusCode;
                }
            }
            catch
            {
                return false;
            }
        }

        private struct PageStats
        {
            public bool OllamaUsed;
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

        private static int RunBatch(string folder, bool rulesOnly)
        {
            if (!Directory.Exists(folder))
            {
                Console.WriteLine("Ordner nicht gefunden: " + folder);
                return 1;
            }

            PdfMaterialExtractionService.ClearCache();
            PdfLayoutAnalysisService.ClearCache();
            if (!rulesOnly)
                OllamaMaterialThicknessService.ClearCache();
            PdfRasterLayoutService.ClearCache();

            Console.WriteLine("Batch | Ordner: " + folder);
            Console.WriteLine("Modus: " + (rulesOnly ? "Regeln+Raster (schnell)" : "inkl. Ollama"));
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

            var ollamaOnly = rulesOnly ? null : OllamaMaterialThicknessService.TryExtract(pdfPath, page);
            var combined = PdfMaterialExtractionService.Extract(pdfPath, page);

            if (dumpText)
                DumpRelevantText(pdfPath, page, combined);

            var stats = new PageStats
            {
                HasFinalThickness = !string.IsNullOrWhiteSpace(combined.ThicknessText),
                HasMaterial = !string.IsNullOrWhiteSpace(combined.MaterialType),
                HasClass = !string.IsNullOrWhiteSpace(combined.MaterialAlloyPrefix),
                IsAssembly = ollamaOnly != null && ollamaOnly.IsAssembly && string.IsNullOrWhiteSpace(combined.ThicknessText),
                OllamaUsed = ollamaOnly != null && (!string.IsNullOrWhiteSpace(ollamaOnly.ThicknessText) || !string.IsNullOrWhiteSpace(ollamaOnly.MaterialType))
            };

            Console.WriteLine($"Page {page,2} | {primaryTsl}");
            Console.WriteLine($"         Assembly: {PdfMaterialExtractionService.DiagnoseIsAssemblyDrawing(pdfPath, page)}");
            Console.WriteLine($"         Ollama -> assy:{(ollamaOnly?.IsAssembly == true ? "yes" : "no")} | thickness:'{ollamaOnly?.ThicknessText ?? ""}' | material:'{ollamaOnly?.MaterialType ?? ""}'");
            if (string.Equals(Environment.GetEnvironmentVariable("PDF_OLLAMA_DEBUG"), "1", StringComparison.OrdinalIgnoreCase))
            {
                var raw = OllamaMaterialThicknessService.LastRawResponse ?? string.Empty;
                var err = OllamaMaterialThicknessService.LastParseError ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(err))
                    Console.WriteLine("         Debug  -> error: " + err);
                if (!string.IsNullOrWhiteSpace(raw))
                {
                    var preview = raw.Length > 220 ? raw.Substring(0, 220) + "..." : raw;
                    Console.WriteLine("         Debug  -> raw: " + preview.Replace("\r", " ").Replace("\n", " "));
                }
            }
            Console.WriteLine($"         Final  -> watermark:'{combined.MaterialWatermarkText ?? ""}' | thickness:'{combined.ThicknessText ?? ""}' | material:'{combined.MaterialType ?? ""}' | class:'{combined.MaterialAlloyPrefix ?? ""}'");
            Console.WriteLine(new string('-', 110));
            return stats;
        }
    }
}
