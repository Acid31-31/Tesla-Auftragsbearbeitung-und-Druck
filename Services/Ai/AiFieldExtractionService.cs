using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using PDF_umbenenen.Models;

namespace PDF_umbenenen.Services.Ai
{
    internal sealed class AiFieldExtractionService
    {
        private readonly OfflineAiOrchestrator _orchestrator;
        private readonly PdfTextExtractionService _pdfTextService;

        public AiFieldExtractionService(OfflineAiOrchestrator orchestrator, PdfTextExtractionService pdfTextService)
        {
            _orchestrator = orchestrator;
            _pdfTextService = pdfTextService;
        }

        public Task<AiExtractedFields> ExtractFieldsAsync(string pdfPath, CancellationToken cancellationToken)
        {
            var text = _pdfTextService.ExtractText(pdfPath);
            var ruleFields = _orchestrator.Fallback.ExtractFields(text);
            return Task.FromResult(ruleFields);
        }

        public List<string> CompareWithExcel(AiExtractedFields fields, AiExcelRowInfo excelRow)
        {
            var mismatches = new List<string>();
            if (fields == null || excelRow == null)
                return mismatches;

            CompareField(mismatches, "Menge", excelRow.RequestedAmount, fields.RequestedAmount);
            CompareField(mismatches, "Lieferdatum", excelRow.RequestedDeliveryDate, fields.RequestedDeliveryDate);
            CompareField(mismatches, "Lieferort", excelRow.RequestedDeliveryLocation, fields.RequestedDeliveryLocation);

            if (!string.IsNullOrWhiteSpace(fields.DrawingNumber)
                && !string.IsNullOrWhiteSpace(excelRow.RawDrawingNumber)
                && AiTextHelper.SimilarityScore(fields.DrawingNumber, excelRow.RawDrawingNumber) < 0.7)
            {
                mismatches.Add(string.Format(
                    "Zeichnungsnummer: Excel='{0}' vs PDF='{1}'",
                    excelRow.RawDrawingNumber,
                    fields.DrawingNumber));
            }

            return mismatches;
        }

        private static void CompareField(List<string> mismatches, string label, string excelValue, string pdfValue)
        {
            if (string.IsNullOrWhiteSpace(pdfValue) || string.IsNullOrWhiteSpace(excelValue))
                return;

            var excelNorm = AiTextHelper.NormalizeKey(excelValue);
            var pdfNorm = AiTextHelper.NormalizeKey(pdfValue);
            if (excelNorm != pdfNorm && !excelNorm.Contains(pdfNorm) && !pdfNorm.Contains(excelNorm))
            {
                mismatches.Add(string.Format("{0}: Excel='{1}' vs PDF='{2}'", label, excelValue, pdfValue));
            }
        }

        private static string Truncate(string value, int maxLength)
        {
            if (string.IsNullOrEmpty(value))
                return string.Empty;
            return value.Length <= maxLength ? value : value.Substring(0, maxLength);
        }
    }
}
