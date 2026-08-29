using System;
using System.Text.RegularExpressions;
using PDF_umbenenen.Models;
using PDF_umbenenen.Platform.Contracts;
using PDF_umbenenen.Platform.Models;

namespace PDF_umbenenen.Platform.Modules.Extraction
{
    internal sealed class HeuristicDocumentClassifierModule : IDocumentClassifier
    {
        private static readonly Regex DrawingRegex = new Regex(
            @"\bTSL-\d{5}-\d{3}\b",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

        public ClassificationResult Classify(DocumentAnalysisResult partialResult)
        {
            var text = partialResult?.PlainText ?? string.Empty;
            var upper = text.ToUpperInvariant();

            if (DrawingRegex.IsMatch(text)
                || upper.Contains("BLECHDICKE")
                || upper.Contains("WERKSTOFF")
                || upper.Contains("MASSSTAB"))
            {
                return new ClassificationResult
                {
                    Kind = DocumentKind.TechnicalDrawing,
                    Confidence = 0.85d,
                    ProviderId = "heuristic",
                    Rationale = "Zeichnungsmerkmale im Text/Bild"
                };
            }

            if (upper.Contains("BESTELLUNG") || upper.Contains("PURCHASE ORDER"))
                return new ClassificationResult { Kind = DocumentKind.PurchaseOrder, Confidence = 0.75d, ProviderId = "heuristic" };

            if (upper.Contains("LIEFERSCHEIN") || upper.Contains("DELIVERY NOTE"))
                return new ClassificationResult { Kind = DocumentKind.DeliveryNote, Confidence = 0.75d, ProviderId = "heuristic" };

            if (upper.Contains("RECHNUNG") || upper.Contains("INVOICE"))
                return new ClassificationResult { Kind = DocumentKind.Invoice, Confidence = 0.75d, ProviderId = "heuristic" };

            if (upper.Contains("WERKSTOFFPRUEF") || upper.Contains("MATERIAL CERTIFICATE"))
                return new ClassificationResult { Kind = DocumentKind.MaterialCertificate, Confidence = 0.7d, ProviderId = "heuristic" };

            return new ClassificationResult
            {
                Kind = DocumentKind.Other,
                Confidence = 0.4d,
                ProviderId = "heuristic"
            };
        }
    }

    internal sealed class DrawingInformationExtractorModule : IInformationExtractor
    {
        public ExtractedDocumentFields Extract(DocumentAnalysisResult partialResult)
        {
            var fields = new ExtractedDocumentFields
            {
                DocumentKind = partialResult?.Classification?.Kind ?? DocumentKind.Unknown
            };

            if (partialResult?.Document == null || string.IsNullOrWhiteSpace(partialResult.Document.FilePath))
                return fields;

            var path = partialResult.Document.FilePath;
            if (partialResult.Document.Format == Models.DocumentFormat.Pdf)
            {
                var info = Services.PdfMaterialExtractionService.Extract(path, 1);
                fields.Material = info.MaterialType;
                fields.MaterialThickness = info.ThicknessText;
                fields.IsAssembly = info.IsAssembly;
                fields.DrawingNumber = OrderVerificationDrawingKey.ExtractPrimaryFromTexts(
                    partialResult.PlainText,
                    partialResult.PlainText);
                fields.Confidence = string.IsNullOrWhiteSpace(info.MaterialWatermarkText) ? 0.45d : 0.85d;
            }

            if (partialResult.Fields?.Extensions != null)
            {
                foreach (var pair in partialResult.Fields.Extensions)
                    fields.Extensions[pair.Key] = pair.Value;

                if (fields.Extensions.TryGetValue("material", out var material))
                    fields.Material = material;
                if (fields.Extensions.TryGetValue("thickness", out var thickness))
                    fields.MaterialThickness = thickness;
                if (fields.Extensions.TryGetValue("partName", out var part))
                    fields.PartName = part;
                if (fields.Extensions.TryGetValue("drawingNumber", out var drawing))
                    fields.DrawingNumber = drawing;
            }

            return fields;
        }
    }
}
