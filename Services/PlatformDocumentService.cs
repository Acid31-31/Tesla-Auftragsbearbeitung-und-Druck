using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using PDF_umbenenen.Platform.Core;
using PDF_umbenenen.Platform.Models;

namespace PDF_umbenenen.Services
{
    /// <summary>
    /// Einheitlicher Einstiegspunkt: Material-Extraktion + Plattform-Entscheidung + Logging.
    /// </summary>
    internal static class PlatformDocumentService
    {
        public static PdfMaterialInfo ExtractMaterial(string pdfPath, int pageNumber = 1)
        {
            if (string.IsNullOrWhiteSpace(pdfPath) || !File.Exists(pdfPath))
                return new PdfMaterialInfo();

            var info = PdfMaterialExtractionService.Extract(pdfPath, pageNumber);

            try
            {
                var analysis = BuildAnalysisResult(pdfPath, info);
                var rules = PlatformHost.Orchestrator
                    .EvaluateRulesAsync(analysis, CancellationToken.None)
                    .GetAwaiter()
                    .GetResult();

                var decision = PlatformHost.Orchestrator
                    .DecideAsync(analysis, rules, CancellationToken.None)
                    .GetAwaiter()
                    .GetResult();

                PlatformHost.Logger?.Info("material", "Material extrahiert", new Dictionary<string, string>
                {
                    ["file"] = Path.GetFileName(pdfPath),
                    ["watermark"] = info.MaterialWatermarkText ?? string.Empty,
                    ["assembly"] = info.IsAssembly.ToString(),
                    ["decision"] = decision?.Decision ?? string.Empty,
                    ["confidence"] = (decision?.Confidence ?? 0d).ToString("0.###"),
                    ["ocr"] = TitleBlockOcrService.LastError ?? "ok"
                });
            }
            catch (Exception ex)
            {
                PlatformHost.Logger?.Error("material", "Plattform-Entscheidung fehlgeschlagen", ex);
            }

            return info;
        }

        public static void IndexBatch(IEnumerable<string> filePaths)
        {
            var list = filePaths?
                .Where(p => !string.IsNullOrWhiteSpace(p) && File.Exists(p))
                .ToList();

            if (list == null || list.Count == 0)
                return;

            PlatformHost.BatchProcessor?.EnqueueBatch(list, "analyze");
        }

        public static IList<string> FindDuplicateFiles(IEnumerable<string> filePaths)
        {
            var detector = PlatformHost.Plugins?.Resolve<PDF_umbenenen.Platform.Contracts.IDuplicateDetector>();
            return detector?.FindDuplicates(filePaths) ?? new List<string>();
        }

        private static DocumentAnalysisResult BuildAnalysisResult(string pdfPath, PdfMaterialInfo info)
        {
            return new DocumentAnalysisResult
            {
                Document = new DocumentReference
                {
                    FilePath = pdfPath,
                    Format = DocumentFormat.Pdf,
                    PageCount = 1
                },
                Classification = new ClassificationResult
                {
                    Kind = info.IsAssembly ? DocumentKind.TechnicalDrawing : DocumentKind.TechnicalDrawing,
                    Confidence = string.IsNullOrWhiteSpace(info.MaterialWatermarkText) ? 0.5d : 0.9d,
                    ProviderId = "material-pipeline"
                },
                Fields = new ExtractedDocumentFields
                {
                    Material = info.MaterialType,
                    MaterialThickness = info.ThicknessText,
                    IsAssembly = info.IsAssembly,
                    Confidence = string.IsNullOrWhiteSpace(info.MaterialWatermarkText) ? 0.5d : 0.9d
                }
            };
        }
    }
}
