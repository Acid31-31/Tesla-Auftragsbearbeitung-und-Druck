using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using PDF_umbenenen.Models;

namespace PDF_umbenenen.Services.Ai
{
    internal sealed class RuleBasedFallback : IAiBackend
    {
        public string Name => "Regeln";

        public Task<bool> CheckAvailabilityAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult(true);
        }

        public Task<string> CompleteAsync(string prompt, bool jsonFormat, CancellationToken cancellationToken)
        {
            return Task.FromResult(string.Empty);
        }

        public Task<string> ChatAsync(string systemPrompt, string userPrompt, CancellationToken cancellationToken)
        {
            var answer = AiHelpKnowledge.TryGetAnswer(userPrompt, string.Empty);
            if (!string.IsNullOrWhiteSpace(answer))
                return Task.FromResult(answer);

            return Task.FromResult(AiHelpKnowledge.GetGeneralHelp());
        }

        public AiMatchSuggestion SuggestMatch(AiPdfInfo pdf, IList<AiExcelRowInfo> excelRows)
        {
            if (pdf == null || excelRows == null || excelRows.Count == 0)
                return null;

            AiMatchSuggestion best = null;
            var sources = new[] { pdf.SearchKey, pdf.OriginalName, AiTextHelper.NormalizeKey(pdf.ExtractedText) };

            foreach (var row in excelRows)
            {
                var candidates = new List<string>();
                if (!string.IsNullOrWhiteSpace(row.RawDrawingNumber))
                    candidates.Add(row.RawDrawingNumber);
                if (row.NormalizedCells != null)
                    candidates.AddRange(row.NormalizedCells);
                if (row.DetectedArticleNumbers != null)
                    candidates.AddRange(row.DetectedArticleNumbers);

                foreach (var source in sources)
                {
                    if (string.IsNullOrWhiteSpace(source))
                        continue;

                    foreach (var candidate in candidates.Where(c => !string.IsNullOrWhiteSpace(c)))
                    {
                        var score = AiTextHelper.SimilarityScore(source, candidate);
                        if (best == null || score > best.Confidence)
                        {
                            best = new AiMatchSuggestion
                            {
                                PdfPath = pdf.FullPath,
                                PdfDisplayName = pdf.DisplayName,
                                ExcelRowIndex = row.RowIndex,
                                DrawingNumber = row.RawDrawingNumber,
                                Confidence = score,
                                Reason = "Regelbasierter Abgleich: " + candidate
                            };
                        }
                    }
                }
            }

            return best != null && best.Confidence >= 0.5 ? best : null;
        }

        public AiExtractedFields ExtractFields(string pdfText)
        {
            if (string.IsNullOrWhiteSpace(pdfText))
                return null;

            var fields = new AiExtractedFields { Confidence = 0.4 };
            var upper = pdfText.ToUpperInvariant();

            var drawingMatch = System.Text.RegularExpressions.Regex.Match(
                upper,
                @"\b[A-Z]{1,4}\d{2,6}-\d{2,6}(?:-[A-Z0-9]+)?\b");
            if (drawingMatch.Success)
            {
                fields.DrawingNumber = drawingMatch.Value;
                fields.Confidence = 0.55;
            }

            var amountMatch = System.Text.RegularExpressions.Regex.Match(
                pdfText,
                @"(?:Menge|Amount|Qty|Quantity)\s*[:\-]?\s*(\d+)",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (amountMatch.Success)
                fields.RequestedAmount = amountMatch.Groups[1].Value;

            var dateMatch = System.Text.RegularExpressions.Regex.Match(
                pdfText,
                @"(?:Lieferdatum|Delivery Date|Date)\s*[:\-]?\s*(\d{4}[-/.]\d{1,2}[-/.]\d{1,2}|\d{1,2}[./]\d{1,2}[./]\d{2,4})",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (dateMatch.Success)
                fields.RequestedDeliveryDate = dateMatch.Groups[1].Value;

            var locationMatch = System.Text.RegularExpressions.Regex.Match(
                pdfText,
                @"(?:Lieferort|Delivery Location|Location)\s*[:\-]?\s*([A-Za-zÄÖÜäöüß \-]{2,40})",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (locationMatch.Success)
                fields.RequestedDeliveryLocation = locationMatch.Groups[1].Value.Trim();

            return fields;
        }
    }
}
