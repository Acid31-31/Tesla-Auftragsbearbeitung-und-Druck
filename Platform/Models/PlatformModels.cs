using System;
using System.Collections.Generic;

namespace PDF_umbenenen.Platform.Models
{
    public enum DocumentFormat
    {
        Unknown,
        Pdf,
        Word,
        Excel,
        Image,
        Zip,
        Text
    }

    public enum DocumentKind
    {
        Unknown,
        TechnicalDrawing,
        PurchaseOrder,
        DeliveryNote,
        MaterialCertificate,
        Invoice,
        Other
    }

    public enum ModelCapability
    {
        TextCompletion,
        Chat,
        Vision,
        Embedding,
        Classification
    }

    public sealed class DocumentReference
    {
        public string FilePath { get; set; }

        public DocumentFormat Format { get; set; }

        public int PageCount { get; set; } = 1;
    }

    public sealed class ClassificationResult
    {
        public DocumentKind Kind { get; set; }

        public double Confidence { get; set; }

        public string ProviderId { get; set; }

        public string Rationale { get; set; }
    }

    public sealed class ExtractedDocumentFields
    {
        public DocumentKind DocumentKind { get; set; }
        public string Customer { get; set; }
        public string Supplier { get; set; }
        public string Project { get; set; }
        public string DrawingNumber { get; set; }
        public string ArticleNumber { get; set; }
        public string Material { get; set; }
        public string MaterialThickness { get; set; }
        public string Quantity { get; set; }
        public string Revision { get; set; }
        public string DeliveryDate { get; set; }
        public string DeliveryLocation { get; set; }
        public string Surface { get; set; }
        public string Weight { get; set; }
        public string Remarks { get; set; }
        public bool IsAssembly { get; set; }
        public string PartName { get; set; }
        public double Confidence { get; set; }
        public IDictionary<string, string> Extensions { get; set; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    }

    public sealed class DocumentAnalysisResult
    {
        public DocumentReference Document { get; set; }

        public ClassificationResult Classification { get; set; }

        public ExtractedDocumentFields Fields { get; set; }

        public string PlainText { get; set; }

        public IList<string> ProcessingSteps { get; set; } = new List<string>();

        public IList<string> Warnings { get; set; } = new List<string>();
    }

    public sealed class RuleEvaluationResult
    {
        public bool Matched { get; set; }

        public string Action { get; set; }

        public string TargetFolder { get; set; }

        public string TargetFileName { get; set; }

        public int Priority { get; set; }

        public string RuleId { get; set; }

        public string Message { get; set; }
    }

    public sealed class AiDecisionRecord
    {
        public string DecisionId { get; set; } = Guid.NewGuid().ToString("N");

        public DateTime TimestampUtc { get; set; } = DateTime.UtcNow;

        public string Subject { get; set; }

        public string Decision { get; set; }

        public double Confidence { get; set; }

        public string ProviderId { get; set; }

        public string Rationale { get; set; }

        public IDictionary<string, string> Evidence { get; set; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    }

    public sealed class ModelRequest
    {
        public string ModelHint { get; set; }

        public string SystemPrompt { get; set; }

        public string UserPrompt { get; set; }

        public IList<string> ImageBase64 { get; set; } = new List<string>();

        public bool JsonFormat { get; set; }
    }

    public sealed class ModelResponse
    {
        public bool Success { get; set; }

        public string Content { get; set; }

        public double Confidence { get; set; }

        public string ProviderId { get; set; }

        public string Error { get; set; }
    }

    public sealed class PlatformJob
    {
        public string JobId { get; set; } = Guid.NewGuid().ToString("N");

        public string FilePath { get; set; }

        public string JobType { get; set; }

        public int ProgressPercent { get; set; }

        public string Status { get; set; }
    }

    public sealed class KnowledgeEntry
    {
        public string Category { get; set; }

        public string Key { get; set; }

        public string Value { get; set; }

        public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;
    }

    public sealed class UserCorrection
    {
        public string DocumentKey { get; set; }

        public string FieldName { get; set; }

        public string OldValue { get; set; }

        public string NewValue { get; set; }

        public DateTime TimestampUtc { get; set; } = DateTime.UtcNow;
    }

    public sealed class SimilarityMatch
    {
        public string DocumentKey { get; set; }

        public double Score { get; set; }

        public string Summary { get; set; }
    }
}
