using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using PDF_umbenenen.Models;

namespace PDF_umbenenen.Services.Ai
{
    internal sealed class AiMatchingService
    {
        private readonly OfflineAiOrchestrator _orchestrator;
        private readonly PdfTextExtractionService _pdfTextService;

        public AiMatchingService(OfflineAiOrchestrator orchestrator, PdfTextExtractionService pdfTextService)
        {
            _orchestrator = orchestrator;
            _pdfTextService = pdfTextService;
        }

        public AiMatchSuggestion SuggestMatch(AiPdfInfo pdf, IList<AiExcelRowInfo> excelRows, CancellationToken cancellationToken)
        {
            if (pdf == null || excelRows == null || excelRows.Count == 0)
                return null;

            if (string.IsNullOrWhiteSpace(pdf.ExtractedText))
                pdf.ExtractedText = _pdfTextService.ExtractText(pdf.FullPath);

            return _orchestrator.Fallback.SuggestMatch(pdf, excelRows);
        }

        public async Task<AiMatchSuggestion> SuggestMatchAsync(AiPdfInfo pdf, IList<AiExcelRowInfo> excelRows, CancellationToken cancellationToken)
        {
            if (_orchestrator.Settings.AiEnabled == false)
                return null;

            if (string.IsNullOrWhiteSpace(pdf.ExtractedText))
                pdf.ExtractedText = _pdfTextService.ExtractText(pdf.FullPath);

            var ruleSuggestion = _orchestrator.Fallback.SuggestMatch(pdf, excelRows);
            return await Task.FromResult(ruleSuggestion).ConfigureAwait(false);
        }

        private static string BuildMatchingPrompt(AiPdfInfo pdf, IList<AiExcelRowInfo> excelRows)
        {
            var sb = new StringBuilder();
            sb.AppendLine("Du ordnest PDF-Dateien Excel-Zeilen zu. Antworte NUR als JSON.");
            sb.AppendLine("Format: {\"excelRowIndex\":1,\"drawingNumber\":\"...\",\"confidence\":0.0,\"reason\":\"...\"}");
            sb.AppendLine("confidence zwischen 0 und 1. excelRowIndex muss aus der Liste stammen.");
            sb.AppendLine();
            sb.AppendLine("PDF-Dateiname: " + pdf.DisplayName);
            sb.AppendLine("PDF-Suchschlüssel: " + pdf.SearchKey);
            sb.AppendLine("PDF-Text (Auszug):");
            sb.AppendLine(Truncate(pdf.ExtractedText, 1500));
            sb.AppendLine();
            sb.AppendLine("Excel-Zeilen:");
            foreach (var row in excelRows.Take(40))
            {
                sb.Append("- Zeile ").Append(row.RowIndex).Append(": Drawing=")
                    .Append(row.RawDrawingNumber).Append(", Artikel=")
                    .Append(string.Join(",", row.DetectedArticleNumbers ?? new List<string>()))
                    .AppendLine();
            }

            return sb.ToString();
        }

        private static string Truncate(string value, int maxLength)
        {
            if (string.IsNullOrEmpty(value))
                return string.Empty;
            return value.Length <= maxLength ? value : value.Substring(0, maxLength);
        }
    }
}
