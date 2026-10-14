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
            @"(?<![A-Z0-9])(?:[A-Z]{2,6}-)?\d{4,6}-\d{2,6}(?:-[A-Z0-9]+)?(?![A-Z0-9])";

        public const string DefaultQuantityPattern =
            @"\b(\d+(?:[.,]\d+)?)\s*(?:EA|Stk\.?|PCS|Stück)\b";

        public const string TeslaId = "tesla";

        public const string EcoMarkId = "ecomark";

        public string Id { get; set; } = string.Empty;

        public string DisplayName { get; set; } = string.Empty;

        public CustomerWorkflowKind WorkflowKind { get; set; } = CustomerWorkflowKind.ExcelPrimary;

        public bool RequiresExcel { get; set; } = true;

        public bool RequiresOrderPdf { get; set; }

        public bool IsBuiltIn { get; set; }

        /// <summary>
        /// Excel + Zeichnungen (Tesla-artig): nie eine Prüf-/Bestell-PDF erzwingen.
        /// Nur eingebaute Bestell-PDF-Kunden (EcoMark) bleiben ohne Excel auf der Prüfliste.
        /// </summary>
        public bool ShouldSortFromExcel(bool excelFileAvailable)
        {
            if (RequiresExcel || WorkflowKind == CustomerWorkflowKind.ExcelPrimary)
                return true;
            return excelFileAvailable && !IsBuiltIn;
        }

        public string DrawingNumberPattern { get; set; } = DefaultDrawingPattern;

        public string ArticleNumberPattern { get; set; } = string.Empty;

        /// <summary>Einheit ist Pflicht – sonst greift „10“ aus RT1 10 00 0001 statt 1,00 Stk.</summary>
        public string QuantityPattern { get; set; } = DefaultQuantityPattern;

        /// <summary>Fester Lieferort im Wasserzeichen (z. B. EcoMark). Leer = aus Excel/PDF.</summary>
        public string FixedDeliveryLocation { get; set; } = string.Empty;

        public List<string> SampleDrawingNumbers { get; set; } = new List<string>();

        public List<string> ExcelDrawingHeaders { get; set; } = new List<string>();

        public List<string> ExcelAmountHeaders { get; set; } = new List<string>();

        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        public string LearningSummary { get; set; } = string.Empty;

        /// <summary>
        /// Fester Tesla-Kunde: Sortierparameter sind im Code verdrahtet und nicht per KI überschreibbar.
        /// </summary>
        public static CustomerProfile CreateBuiltInTesla()
        {
            return new CustomerProfile
            {
                Id = TeslaId,
                DisplayName = "Tesla",
                WorkflowKind = CustomerWorkflowKind.ExcelPrimary,
                RequiresExcel = true,
                RequiresOrderPdf = false,
                IsBuiltIn = true,
                DrawingNumberPattern =
                    @"(?<![A-Z0-9])TSL[-_./]?\d{2,8}(?:[-_./]\d{2,8})*(?:[-_.][A-Z0-9]+)?(?![A-Z0-9])",
                ArticleNumberPattern = @"\bTGA\d+\b",
                QuantityPattern = DefaultQuantityPattern,
                FixedDeliveryLocation = string.Empty,
                SampleDrawingNumbers = new List<string>
                {
                    "TSL-10108-093",
                    "TSL-10322-243",
                    "TSL-10522-001",
                    "TSL-1227-125"
                },
                ExcelDrawingHeaders = new List<string>
                {
                    "Drawing",
                    "Zeichnung",
                    "Zeichnungsnummer"
                },
                ExcelAmountHeaders = new List<string>
                {
                    "Requested Amount",
                    "Menge",
                    "Qty",
                    "Quantity",
                    "Amount"
                },
                CreatedUtc = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                LearningSummary =
                    "Fester Kunde Tesla (eingebaut).\r\n"
                    + "Zeichnungen: TSL-…\r\n"
                    + "Artikel/BG: TGA… / „verbaut in TGA…“\r\n"
                    + "Excel: Drawing + Requested Amount\r\n"
                    + "Sortierung und BG-Logik sind fest – nicht per KI-Kundenanlage ändern."
            };
        }

        /// <summary>
        /// Fester EcoMark-Kunde: Bestell-PDF-Workflow, RT1-Nummern, Lieferort EcoMark.
        /// </summary>
        public static CustomerProfile CreateBuiltInEcoMark()
        {
            return new CustomerProfile
            {
                Id = EcoMarkId,
                DisplayName = "EcoMark",
                WorkflowKind = CustomerWorkflowKind.PdfOrderListPrimary,
                RequiresExcel = false,
                RequiresOrderPdf = true,
                IsBuiltIn = true,
                DrawingNumberPattern = @"\bRT1(?:[\s\-_/]+\d{1,5}){3}\b",
                ArticleNumberPattern = string.Empty,
                QuantityPattern = @"\b(\d+(?:[.,]\d{1,2})?)\s*(?:EA|Stk\.?|PCS|Stück)\b",
                FixedDeliveryLocation = "EcoMark",
                SampleDrawingNumbers = new List<string>
                {
                    "RT1 10 00 0001",
                    "RT1 10 00 0005",
                    "RT1 10 00 0007",
                    "RT1 20 00 0006"
                },
                ExcelDrawingHeaders = new List<string>(),
                ExcelAmountHeaders = new List<string>(),
                CreatedUtc = new DateTime(2026, 9, 5, 11, 43, 0, DateTimeKind.Utc),
                LearningSummary =
                    "Fester Kunde EcoMark (eingebaut).\r\n"
                    + "Workflow: Bestell-PDF (keine Excel).\r\n"
                    + "Zeichnungen: RT1 … … …\r\n"
                    + "Auftragsnummer: „Bestellung …“ aus Bestell-PDF.\r\n"
                    + "Lieferort-Wasserzeichen: EcoMark.\r\n"
                    + "Stückzahl: Menge mit Einheit nach der Zeichnungsnummer.\r\n"
                    + "Gebürstet: Oberflächenbehandlung „einseitig gebürstet“ → Wasserzeichen neben Material."
            };
        }

        public static bool IsBuiltInTeslaId(string id)
        {
            return !string.IsNullOrWhiteSpace(id)
                   && string.Equals(id.Trim(), TeslaId, StringComparison.OrdinalIgnoreCase);
        }

        public static bool IsBuiltInEcoMarkId(string id)
        {
            return !string.IsNullOrWhiteSpace(id)
                   && string.Equals(id.Trim(), EcoMarkId, StringComparison.OrdinalIgnoreCase);
        }

        public static bool IsBuiltInCustomerId(string id)
        {
            return IsBuiltInTeslaId(id) || IsBuiltInEcoMarkId(id);
        }

        public static bool IsBuiltInCustomerName(string displayName)
        {
            if (string.IsNullOrWhiteSpace(displayName))
                return false;

            var name = displayName.Trim();
            return string.Equals(name, "Tesla", StringComparison.OrdinalIgnoreCase)
                   || string.Equals(name, "EcoMark", StringComparison.OrdinalIgnoreCase);
        }
    }

    public sealed class CustomerProfileDraft
    {
        public CustomerWorkflowKind WorkflowKind { get; set; } = CustomerWorkflowKind.ExcelPrimary;

        public bool RequiresExcel { get; set; } = true;

        public bool RequiresOrderPdf { get; set; }

        public string DrawingNumberPattern { get; set; } = CustomerProfile.DefaultDrawingPattern;

        public string ArticleNumberPattern { get; set; } = string.Empty;

        public string QuantityPattern { get; set; } = CustomerProfile.DefaultQuantityPattern;

        /// <summary>Fester Lieferort; leer = Kundenname bei Bestell-PDF-Workflow.</summary>
        public string FixedDeliveryLocation { get; set; } = string.Empty;

        public List<string> SampleDrawingNumbers { get; set; } = new List<string>();

        public List<string> ExcelDrawingHeaders { get; set; } = new List<string>();

        public List<string> ExcelAmountHeaders { get; set; } = new List<string>();

        public List<string> AnalysisLog { get; set; } = new List<string>();

        public void EnsureExcelWorkflow()
        {
            WorkflowKind = CustomerWorkflowKind.ExcelPrimary;
            RequiresExcel = true;
            RequiresOrderPdf = false;
        }

        public void EnsureOrderPdfWorkflow()
        {
            WorkflowKind = CustomerWorkflowKind.PdfOrderListPrimary;
            RequiresExcel = false;
            RequiresOrderPdf = true;
        }

        public CustomerProfile ToProfile(string displayName)
        {
            var cleanName = (displayName ?? string.Empty).Trim();
            var id = SlugifyId(cleanName);
            var fixedLocation = (FixedDeliveryLocation ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(fixedLocation)
                && WorkflowKind == CustomerWorkflowKind.PdfOrderListPrimary
                && !string.IsNullOrWhiteSpace(cleanName))
            {
                fixedLocation = cleanName;
            }

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
                    ? CustomerProfile.DefaultQuantityPattern
                    : QuantityPattern.Trim(),
                FixedDeliveryLocation = fixedLocation,
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
