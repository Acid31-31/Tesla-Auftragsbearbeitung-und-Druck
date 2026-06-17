using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using PDF_umbenenen.Models;

namespace PDF_umbenenen.Services.Ai
{
    internal sealed class AiQualityCheckService
    {
        private readonly OfflineAiOrchestrator _orchestrator;
        private readonly AiMatchingService _matchingService;
        private readonly AiFieldExtractionService _fieldExtractionService;

        public AiQualityCheckService(
            OfflineAiOrchestrator orchestrator,
            AiMatchingService matchingService,
            AiFieldExtractionService fieldExtractionService)
        {
            _orchestrator = orchestrator;
            _matchingService = matchingService;
            _fieldExtractionService = fieldExtractionService;
        }

        public async Task<AiQualityReport> AnalyzeAsync(
            IList<AiPdfInfo> pdfs,
            IList<AiExcelRowInfo> excelRows,
            CancellationToken cancellationToken)
        {
            var report = new AiQualityReport();
            report.InfoMessages.Add("Backend: " + _orchestrator.GetStatusText());

            if (pdfs == null || pdfs.Count == 0)
            {
                report.InfoMessages.Add("Keine PDFs im Ablage-Ordner gefunden.");
                return report;
            }

            if (excelRows == null || excelRows.Count == 0)
            {
                report.InfoMessages.Add("Keine Excel-Zeilen gefunden.");
                return report;
            }

            var matchedExcelRows = new HashSet<int>();
            var pdfToRow = new Dictionary<string, int>();

            foreach (var pdf in pdfs)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var exactRows = excelRows
                    .Where(row => row.NormalizedCells != null && row.NormalizedCells.Any(cell => cell == pdf.SearchKey))
                    .ToList();

                if (exactRows.Count > 1)
                {
                    report.AmbiguousMatches.Add(
                        pdf.DisplayName + " -> Zeilen " + string.Join(", ", exactRows.Select(r => r.RowIndex)));
                    continue;
                }

                if (exactRows.Count == 1)
                {
                    matchedExcelRows.Add(exactRows[0].RowIndex);
                    pdfToRow[pdf.FullPath] = exactRows[0].RowIndex;
                    await CheckFieldMismatchesAsync(report, pdf, exactRows[0], cancellationToken).ConfigureAwait(false);
                    continue;
                }

                var suggestion = await _matchingService.SuggestMatchAsync(pdf, excelRows, cancellationToken).ConfigureAwait(false);
                if (suggestion == null)
                {
                    report.UnmatchedPdfs.Add(pdf.DisplayName);
                    continue;
                }

                if (suggestion.IsAutoAccepted)
                {
                    matchedExcelRows.Add(suggestion.ExcelRowIndex);
                    pdfToRow[pdf.FullPath] = suggestion.ExcelRowIndex;
                    var row = excelRows.FirstOrDefault(r => r.RowIndex == suggestion.ExcelRowIndex);
                    if (row != null)
                        await CheckFieldMismatchesAsync(report, pdf, row, cancellationToken).ConfigureAwait(false);
                }
                else if (suggestion.IsWarning)
                {
                    report.UncertainMatches.Add(suggestion);
                }
                else
                {
                    report.UnmatchedPdfs.Add(pdf.DisplayName);
                }
            }

            foreach (var row in excelRows.Where(r => !string.IsNullOrWhiteSpace(r.RawDrawingNumber)))
            {
                if (!matchedExcelRows.Contains(row.RowIndex))
                {
                    report.MissingPdfsForExcel.Add(
                        "Zeile " + row.RowIndex + ": " + row.RawDrawingNumber);
                }
            }

            return report;
        }

        private async Task CheckFieldMismatchesAsync(
            AiQualityReport report,
            AiPdfInfo pdf,
            AiExcelRowInfo row,
            CancellationToken cancellationToken)
        {
            var fields = await _fieldExtractionService.ExtractFieldsAsync(pdf.FullPath, cancellationToken).ConfigureAwait(false);
            var mismatches = _fieldExtractionService.CompareWithExcel(fields, row);
            foreach (var mismatch in mismatches)
            {
                report.FieldMismatches.Add(pdf.DisplayName + ": " + mismatch);
            }
        }
    }
}
