using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using PDF_umbenenen.Platform.Contracts;
using PDF_umbenenen.Platform.Models;

namespace PDF_umbenenen.Platform.Modules.Decisions
{
    internal sealed class SemanticClassifierModule : ISemanticClassifier
    {
        public ClassificationResult ClassifySemantic(string text, DocumentKind hint = DocumentKind.Unknown)
        {
            var upper = (text ?? string.Empty).ToUpperInvariant();
            if (upper.Contains("INVOICE") || upper.Contains("RECHNUNG"))
                return Result(DocumentKind.Invoice, 0.82d, "semantic-invoice");

            if (upper.Contains("DELIVERY") || upper.Contains("LIEFERSCHEIN"))
                return Result(DocumentKind.DeliveryNote, 0.8d, "semantic-delivery");

            if (upper.Contains("PURCHASE ORDER") || upper.Contains("BESTELLUNG"))
                return Result(DocumentKind.PurchaseOrder, 0.83d, "semantic-order");

            if (upper.Contains("MATERIAL CERTIFICATE") || upper.Contains("MATERIALZEUGNIS"))
                return Result(DocumentKind.MaterialCertificate, 0.81d, "semantic-certificate");

            if (upper.Contains("DRAWING") || upper.Contains("ZEICHNUNG") || Regex.IsMatch(upper, @"\b[A-Z]{1,3}-\d{4,}\b"))
                return Result(DocumentKind.TechnicalDrawing, 0.78d, "semantic-drawing");

            if (hint != DocumentKind.Unknown)
                return Result(hint, 0.55d, "semantic-hint");

            return Result(DocumentKind.Other, 0.4d, "semantic-other");
        }

        private static ClassificationResult Result(DocumentKind kind, double confidence, string provider)
            => new ClassificationResult
            {
                Kind = kind,
                Confidence = confidence,
                ProviderId = provider,
                Rationale = "Semantische Offline-Klassifizierung"
            };
    }

    internal sealed class ConflictResolverModule : IConflictResolver
    {
        public ExtractedDocumentFields Resolve(ExtractedDocumentFields primary, ExtractedDocumentFields secondary)
        {
            if (primary == null)
                return secondary ?? new ExtractedDocumentFields();
            if (secondary == null)
                return primary;

            var merged = Clone(primary);
            merged.Material = PickValue(primary.Material, secondary.Material, primary.Confidence, secondary.Confidence);
            merged.MaterialThickness = PickValue(primary.MaterialThickness, secondary.MaterialThickness, primary.Confidence, secondary.Confidence);
            merged.DrawingNumber = PickValue(primary.DrawingNumber, secondary.DrawingNumber, primary.Confidence, secondary.Confidence);
            merged.Customer = PickValue(primary.Customer, secondary.Customer, primary.Confidence, secondary.Confidence);
            merged.Supplier = PickValue(primary.Supplier, secondary.Supplier, primary.Confidence, secondary.Confidence);
            merged.Confidence = Math.Max(primary.Confidence, secondary.Confidence);
            return merged;
        }

        private static string PickValue(string a, string b, double confA, double confB)
        {
            if (!string.IsNullOrWhiteSpace(a) && string.IsNullOrWhiteSpace(b))
                return a;
            if (string.IsNullOrWhiteSpace(a) && !string.IsNullOrWhiteSpace(b))
                return b;
            return confA >= confB ? a : b;
        }

        private static ExtractedDocumentFields Clone(ExtractedDocumentFields source)
            => new ExtractedDocumentFields
            {
                DocumentKind = source.DocumentKind,
                Customer = source.Customer,
                Supplier = source.Supplier,
                Project = source.Project,
                DrawingNumber = source.DrawingNumber,
                ArticleNumber = source.ArticleNumber,
                Material = source.Material,
                MaterialThickness = source.MaterialThickness,
                Quantity = source.Quantity,
                Revision = source.Revision,
                DeliveryDate = source.DeliveryDate,
                DeliveryLocation = source.DeliveryLocation,
                Surface = source.Surface,
                Weight = source.Weight,
                Remarks = source.Remarks,
                IsAssembly = source.IsAssembly,
                PartName = source.PartName,
                Confidence = source.Confidence
            };
    }

    internal sealed class RecommendationEngineModule : IRecommendationEngine
    {
        public IList<string> RecommendActions(DocumentAnalysisResult analysis, RuleEvaluationResult rules)
        {
            var list = new List<string>();
            if (rules?.Matched == true && !string.IsNullOrWhiteSpace(rules.Action))
                list.Add("rule:" + rules.Action);

            if (analysis?.Fields != null)
            {
                if (analysis.Fields.IsAssembly)
                    list.Add("skip-material-watermark");
                else if (string.IsNullOrWhiteSpace(analysis.Fields.MaterialThickness))
                    list.Add("review-material-thickness");
            }

            if ((analysis?.Fields?.Confidence ?? 0d) < 0.7d)
                list.Add("manual-review");

            if (analysis?.Classification?.Kind == DocumentKind.PurchaseOrder)
                list.Add("verify-against-excel");

            return list.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }
    }

    internal sealed class ConfidenceScorerModule : IConfidenceScorer
    {
        public double ScoreFields(ExtractedDocumentFields fields)
        {
            if (fields == null)
                return 0d;

            var score = 0.2d;
            if (!string.IsNullOrWhiteSpace(fields.DrawingNumber)) score += 0.15d;
            if (!string.IsNullOrWhiteSpace(fields.Material)) score += 0.15d;
            if (!string.IsNullOrWhiteSpace(fields.MaterialThickness)) score += 0.15d;
            if (!string.IsNullOrWhiteSpace(fields.Customer)) score += 0.1d;
            if (!string.IsNullOrWhiteSpace(fields.Quantity)) score += 0.1d;
            if (!string.IsNullOrWhiteSpace(fields.DeliveryDate)) score += 0.1d;
            return Math.Min(0.98d, score);
        }

        public double ScoreDecision(AiDecisionRecord record)
        {
            if (record == null)
                return 0d;

            var score = record.Confidence;
            if (record.Evidence != null && record.Evidence.Count > 0)
                score += 0.05d;

            return Math.Min(0.99d, score);
        }
    }
}
