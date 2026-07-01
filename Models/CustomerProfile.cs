using System;
using System.Collections.Generic;

namespace PDF_umbenenen.Models
{
    public enum CustomerWorkflowKind
    {
        ExcelPrimary = 0,
        PdfOrderListPrimary = 1
    }

    public sealed class CustomerProfile
    {
        public const string DefaultDrawingPattern =
            @"\b(?:[A-Z]{2,6}-)?\d{4,6}-\d{2,6}(?:-[A-Z0-9]+)?\b";

        public const string TeslaId = "tesla";

        public string Id { get; set; } = string.Empty;

        public string DisplayName { get; set; } = string.Empty;

        public CustomerWorkflowKind WorkflowKind { get; set; } = CustomerWorkflowKind.ExcelPrimary;

        public bool RequiresExcel { get; set; } = true;

        public bool RequiresOrderPdf { get; set; }

        public bool IsBuiltIn { get; set; }

        public string DrawingNumberPattern { get; set; } = DefaultDrawingPattern;

        public string ArticleNumberPattern { get; set; } = string.Empty;

        public string QuantityPattern { get; set; } = @"\b(\d+(?:[.,]\d+)?)\s*(?:EA|Stk\.?|PCS|Stück)?\b";

        public List<string> SampleDrawingNumbers { get; set; } = new List<string>();

        public List<string> ExcelDrawingHeaders { get; set; } = new List<string>();

        public List<string> ExcelAmountHeaders { get; set; } = new List<string>();

        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        public string LearningSummary { get; set; } = string.Empty;
    }

    public sealed class CustomerProfileDraft
    {
        public CustomerWorkflowKind WorkflowKind { get; set; } = CustomerWorkflowKind.ExcelPrimary;

        public bool RequiresExcel { get; set; } = true;

        public bool RequiresOrderPdf { get; set; }

        public string DrawingNumberPattern { get; set; } = CustomerProfile.DefaultDrawingPattern;

        public string ArticleNumberPattern { get; set; } = string.Empty;

        public string QuantityPattern { get; set; } = @"\b(\d+(?:[.,]\d+)?)\s*(?:EA|Stk\.?|PCS|Stück)?\b";

        public List<string> SampleDrawingNumbers { get; set; } = new List<string>();

        public List<string> ExcelDrawingHeaders { get; set; } = new List<string>();

        public List<string> ExcelAmountHeaders { get; set; } = new List<string>();

        public List<string> AnalysisLog { get; set; } = new List<string>();

        public CustomerProfile ToProfile(string displayName)
        {
            var cleanName = (displayName ?? string.Empty).Trim();
            var id = SlugifyId(cleanName);
            return new CustomerProfile
            {
                Id = id,
                DisplayName = displayName.Trim(),
                WorkflowKind = WorkflowKind,
                RequiresExcel = RequiresExcel,
                RequiresOrderPdf = RequiresOrderPdf,
                DrawingNumberPattern = string.IsNullOrWhiteSpace(DrawingNumberPattern)
                    ? CustomerProfile.DefaultDrawingPattern
                    : DrawingNumberPattern.Trim(),
                ArticleNumberPattern = ArticleNumberPattern ?? string.Empty,
                QuantityPattern = string.IsNullOrWhiteSpace(QuantityPattern)
                    ? @"\b(\d+(?:[.,]\d+)?)\s*(?:EA|Stk\.?|PCS|Stück)?\b"
                    : QuantityPattern.Trim(),
                SampleDrawingNumbers = SampleDrawingNumbers ?? new List<string>(),
                ExcelDrawingHeaders = ExcelDrawingHeaders ?? new List<string>(),
                ExcelAmountHeaders = ExcelAmountHeaders ?? new List<string>(),
                CreatedUtc = DateTime.UtcNow,
                LearningSummary = string.Join(Environment.NewLine, AnalysisLog ?? new List<string>())
            };
        }

        private static string SlugifyId(string displayName)
        {
            if (string.IsNullOrWhiteSpace(displayName))
                return "kunde";

            var chars = new List<char>();
            var lastDash = false;
            foreach (var ch in displayName.Trim().ToLowerInvariant())
            {
                if (char.IsLetterOrDigit(ch))
                {
                    chars.Add(ch);
                    lastDash = false;
                    continue;
                }

                if (!lastDash)
                {
                    chars.Add('-');
                    lastDash = true;
                }
            }

            var slug = new string(chars.ToArray()).Trim('-');
            return string.IsNullOrWhiteSpace(slug) ? "kunde" : slug;
        }
    }

    public sealed class CustomerDetectionResult
    {
        public CustomerProfile Profile { get; set; }

        public int MatchCount { get; set; }

        public int TotalCount { get; set; }

        public int ConfidencePercent
        {
            get
            {
                if (TotalCount <= 0)
                    return 0;

                return (int)Math.Round(100.0 * MatchCount / TotalCount);
            }
        }
    }
}
