using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using PDF_umbenenen.Models;
using PDF_umbenenen.Services.Ai;

namespace PDF_umbenenen.Services
{
    internal sealed class CustomerProfileLearningService
    {
        private static readonly XNamespace SpreadsheetNamespace = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

        private readonly PdfTextExtractionService _pdfTextService;

        public CustomerProfileLearningService(PdfTextExtractionService pdfTextService)
        {
            _pdfTextService = pdfTextService ?? new PdfTextExtractionService();
        }

        public CustomerProfileDraft AnalyzeFiles(string customerName, IEnumerable<string> filePaths)
        {
            var draft = new CustomerProfileDraft();
            var paths = (filePaths ?? Enumerable.Empty<string>())
                .Where(File.Exists)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (paths.Count == 0)
            {
                draft.AnalysisLog.Add("Keine Dateien zum Analysieren.");
                return draft;
            }

            var drawingSamples = new List<string>();
            var articleSamples = new List<string>();
            var hasExcel = false;
            var hasOrderPdf = false;
            var drawingPdfCount = 0;

            foreach (var path in paths)
            {
                var ext = Path.GetExtension(path)?.ToLowerInvariant() ?? string.Empty;
                if (ext == ".xlsx" || ext == ".xls")
                {
                    hasExcel = true;
                    AnalyzeExcel(path, draft, drawingSamples);
                    continue;
                }

                if (ext != ".pdf")
                    continue;

                var fileName = Path.GetFileNameWithoutExtension(path) ?? string.Empty;
                var text = _pdfTextService.ExtractText(path, 20);
                var fromName = OrderVerificationDrawingKey.ExtractPrimaryFromTexts(fileName);
                var fromPdf = OrderVerificationDrawingKey.ExtractPrimaryFromTexts(text);
                var tokens = OrderVerificationDrawingKey.ExtractAllFromText(text + " " + fileName);

                if (!string.IsNullOrWhiteSpace(fromName))
                    drawingSamples.Add(fromName);
                if (!string.IsNullOrWhiteSpace(fromPdf))
                    drawingSamples.Add(fromPdf);
                drawingSamples.AddRange(tokens);

                foreach (Match m in Regex.Matches(text ?? string.Empty, @"\bTGA\d+\b", RegexOptions.IgnoreCase))
                    articleSamples.Add(m.Value.ToUpperInvariant());

                if (LooksLikeOrderListPdf(path, text))
                {
                    hasOrderPdf = true;
                    AnalyzeOrderPdfText(text, draft, drawingSamples);
                    draft.AnalysisLog.Add("Bestell-PDF erkannt: " + Path.GetFileName(path));
                }
                else
                {
                    drawingPdfCount++;
                    draft.AnalysisLog.Add("Zeichnungs-PDF: " + Path.GetFileName(path));
                }
            }

            drawingSamples = drawingSamples
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Select(s => s.Trim().ToUpperInvariant())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(40)
                .ToList();

            draft.SampleDrawingNumbers = drawingSamples;
            draft.DrawingNumberPattern = CustomerProfilePatternBuilder.BuildDrawingPattern(drawingSamples);
            draft.ArticleNumberPattern = CustomerProfilePatternBuilder.BuildArticlePattern(articleSamples);

            if (hasExcel)
            {
                draft.WorkflowKind = CustomerWorkflowKind.ExcelPrimary;
                draft.RequiresExcel = true;
                draft.RequiresOrderPdf = hasOrderPdf;
                draft.AnalysisLog.Add("Workflow: Excel als Hauptquelle" + (hasOrderPdf ? " + Bestell-PDF" : string.Empty));
            }
            else if (hasOrderPdf)
            {
                draft.WorkflowKind = CustomerWorkflowKind.PdfOrderListPrimary;
                draft.RequiresExcel = false;
                draft.RequiresOrderPdf = true;
                draft.AnalysisLog.Add("Workflow: Bestell-PDF (ohne Excel)");
            }
            else if (drawingSamples.Count > 0)
            {
                // Nur Zeichnungs-PDFs: Muster lernen, Excel nicht erzwingen.
                draft.WorkflowKind = CustomerWorkflowKind.PdfOrderListPrimary;
                draft.RequiresExcel = false;
                draft.RequiresOrderPdf = false;
                draft.AnalysisLog.Add(
                    "Workflow: nur Zeichnungs-PDFs – Zeichnungsnummern-Muster gelernt (ohne Excel).");
                draft.AnalysisLog.Add(
                    "Hinweis: Zum späteren Sortieren brauchen Sie eine Bestell-PDF oder Excel mit denselben Zeichnungsnummern.");
            }
            else
            {
                draft.WorkflowKind = CustomerWorkflowKind.ExcelPrimary;
                draft.RequiresExcel = false;
                draft.AnalysisLog.Add(
                    "Keine Zeichnungsnummern erkannt. Bitte 1–3 Zeichnungs-PDFs mit Nummern im Dateinamen oder Schriftfeld "
                    + "(z. B. TSL-1227-125), eine Bestell-PDF oder eine Excel hinzufügen.");
            }

            if (drawingSamples.Count > 0)
                draft.AnalysisLog.Add("Zeichnungsmuster: " + draft.DrawingNumberPattern);
            if (drawingSamples.Count > 0)
                draft.AnalysisLog.Add("Beispiele: " + string.Join(", ", drawingSamples.Take(6)));

            if (!string.IsNullOrWhiteSpace(customerName))
                draft.AnalysisLog.Insert(0, "Kunde: " + customerName.Trim());

            return draft;
        }

        private static bool LooksLikeOrderListPdf(string path, string text)
        {
            var name = (Path.GetFileName(path) ?? string.Empty).ToLowerInvariant();
            if (name.Contains("bestell") || name.Contains("order") || name.Contains("auftrag") || name.Contains("po"))
                return true;

            if (string.IsNullOrWhiteSpace(text))
                return false;

            var drawingHits = OrderVerificationDrawingKey.ExtractAllFromText(text).Count;
            var qtyHits = Regex.Matches(text, @"\b\d+(?:[.,]\d+)?\s*(?:EA|Stk\.?|PCS)\b", RegexOptions.IgnoreCase).Count;
            return drawingHits >= 3 && qtyHits >= 2;
        }

        private static void AnalyzeOrderPdfText(string text, CustomerProfileDraft draft, ICollection<string> drawingSamples)
        {
            if (string.IsNullOrWhiteSpace(text))
                return;

            var lines = text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var line in lines)
            {
                foreach (var token in OrderVerificationDrawingKey.ExtractAllFromText(line))
                    drawingSamples.Add(token);

                var qty = Regex.Match(line, @"\b(\d+(?:[.,]\d+)?)\s*(?:EA|Stk\.?|PCS|Stück)\b", RegexOptions.IgnoreCase);
                if (qty.Success && draft.AnalysisLog.Count(l => l.StartsWith("Mengenformat", StringComparison.Ordinal)) == 0)
                    draft.AnalysisLog.Add("Mengenformat in Bestell-PDF erkannt.");
            }
        }

        private void AnalyzeExcel(string excelPath, CustomerProfileDraft draft, ICollection<string> drawingSamples)
        {
            draft.AnalysisLog.Add("Excel analysiert: " + Path.GetFileName(excelPath));

            if (string.Equals(Path.GetExtension(excelPath), ".xls", StringComparison.OrdinalIgnoreCase))
            {
                draft.AnalysisLog.Add("Hinweis: .xls wird beim Anlegen vereinfacht ausgelesen – .xlsx liefert genauere Spalten.");
                return;
            }

            try
            {
                using (var archive = ZipFile.OpenRead(excelPath))
                {
                    var sharedStrings = LoadSharedStrings(archive);
                    var workbook = LoadXml(archive, "xl/workbook.xml");
                    var rels = LoadXml(archive, "xl/_rels/workbook.xml.rels");
                    var relMap = rels.Descendants()
                        .Where(e => e.Name.LocalName == "Relationship")
                        .ToDictionary(
                            e => (string)e.Attribute("Id"),
                            e => (string)e.Attribute("Target"),
                            StringComparer.OrdinalIgnoreCase);

                    var sheet = workbook.Descendants(SpreadsheetNamespace + "sheet").FirstOrDefault();
                    var relId = sheet?.Attribute("{http://schemas.openxmlformats.org/officeDocument/2006/relationships}id")?.Value;
                    if (string.IsNullOrWhiteSpace(relId) || !relMap.TryGetValue(relId, out var target))
                        return;

                    var sheetPath = "xl/" + target.Replace('\\', '/').TrimStart('/');
                    var sheetXml = LoadXml(archive, sheetPath);
                    var rows = sheetXml.Descendants(SpreadsheetNamespace + "row").Take(80).ToList();

                    string drawingHeader = null;
                    string amountHeader = null;

                    foreach (var row in rows.Take(15))
                    {
                        foreach (var cell in row.Elements(SpreadsheetNamespace + "c"))
                        {
                            var text = GetCellText(cell, sharedStrings).Trim();
                            if (IsDrawingHeader(text) && drawingHeader == null)
                                drawingHeader = text;
                            if (IsAmountHeader(text) && amountHeader == null)
                                amountHeader = text;
                        }
                    }

                    if (!string.IsNullOrWhiteSpace(drawingHeader))
                    {
                        draft.ExcelDrawingHeaders.Add(drawingHeader);
                        draft.AnalysisLog.Add("Zeichnungsspalte: " + drawingHeader);
                    }

                    if (!string.IsNullOrWhiteSpace(amountHeader))
                    {
                        draft.ExcelAmountHeaders.Add(amountHeader);
                        draft.AnalysisLog.Add("Mengenspalte: " + amountHeader);
                    }

                    foreach (var row in rows.Skip(1).Take(25))
                    {
                        foreach (var cell in row.Elements(SpreadsheetNamespace + "c"))
                        {
                            var text = GetCellText(cell, sharedStrings).Trim();
                            if (OrderVerificationDrawingKey.IsLikelyDrawingToken(text))
                                drawingSamples.Add(text.ToUpperInvariant());
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                draft.AnalysisLog.Add("Excel-Analyse fehlgeschlagen: " + ex.Message);
            }
        }

        private static bool IsDrawingHeader(string value)
        {
            var n = NormalizeHeader(value);
            return n.Contains("drawing") || n.Contains("zeichnung") || n == "part number" || n.Contains("teilenummer");
        }

        private static bool IsAmountHeader(string value)
        {
            var n = NormalizeHeader(value);
            return n == "qty" || n == "quantity" || n == "menge" || n.Contains("amount") || n.Contains("requested amount");
        }

        private static string NormalizeHeader(string value)
        {
            return string.IsNullOrWhiteSpace(value)
                ? string.Empty
                : Regex.Replace(value.Trim().ToLowerInvariant(), @"\s+", " ");
        }

        private static XDocument LoadXml(ZipArchive archive, string path)
        {
            var entry = archive.GetEntry(path.Replace('\\', '/'));
            if (entry == null)
                throw new FileNotFoundException(path);

            using (var stream = entry.Open())
                return XDocument.Load(stream);
        }

        private static List<string> LoadSharedStrings(ZipArchive archive)
        {
            var entry = archive.GetEntry("xl/sharedStrings.xml");
            if (entry == null)
                return new List<string>();

            using (var stream = entry.Open())
            {
                var doc = XDocument.Load(stream);
                return doc.Descendants(SpreadsheetNamespace + "si")
                    .Select(si => string.Concat(si.Descendants(SpreadsheetNamespace + "t").Select(t => (string)t)))
                    .ToList();
            }
        }

        private static string GetCellText(XElement cell, IList<string> sharedStrings)
        {
            var type = cell.Attribute("t")?.Value;
            var value = cell.Element(SpreadsheetNamespace + "v")?.Value ?? string.Empty;
            if (type == "s" && int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var index)
                && index >= 0 && index < sharedStrings.Count)
            {
                return sharedStrings[index];
            }

            var inline = cell.Element(SpreadsheetNamespace + "is");
            if (inline != null)
                return string.Concat(inline.Descendants(SpreadsheetNamespace + "t").Select(t => (string)t));

            return value;
        }
    }
}
