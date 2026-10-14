using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using PDF_umbenenen.Models;

namespace PDF_umbenenen.Services
{
    public sealed class CustomerProfileDescriptionParseResult
    {
        public string CustomerName { get; set; } = string.Empty;

        public string DescriptionNotes { get; set; } = string.Empty;

        public string DrawingNumberPattern { get; set; } = CustomerProfile.DefaultDrawingPattern;

        public List<string> SampleDrawingNumbers { get; set; } = new List<string>();

        public List<string> ExcelDrawingHeaders { get; set; } = new List<string>();

        public List<string> ExcelAmountHeaders { get; set; } = new List<string>();

        public CustomerWorkflowKind? WorkflowKind { get; set; }

        public bool? RequiresExcel { get; set; }

        public bool? RequiresOrderPdf { get; set; }

        public List<string> AnalysisLog { get; set; } = new List<string>();

        public bool HasActionableData =>
            !string.IsNullOrWhiteSpace(CustomerName)
            || SampleDrawingNumbers.Count > 0
            || ExcelDrawingHeaders.Count > 0
            || ExcelAmountHeaders.Count > 0
            || !string.IsNullOrWhiteSpace(DrawingNumberPattern);

        public CustomerProfileDraft ToDraft()
        {
            var draft = new CustomerProfileDraft
            {
                DrawingNumberPattern = string.IsNullOrWhiteSpace(DrawingNumberPattern)
                    ? CustomerProfile.DefaultDrawingPattern
                    : DrawingNumberPattern.Trim(),
                SampleDrawingNumbers = SampleDrawingNumbers?.ToList() ?? new List<string>(),
                ExcelDrawingHeaders = ExcelDrawingHeaders?.ToList() ?? new List<string>(),
                ExcelAmountHeaders = ExcelAmountHeaders?.ToList() ?? new List<string>(),
                AnalysisLog = AnalysisLog?.ToList() ?? new List<string>()
            };

            if (WorkflowKind.HasValue)
                draft.WorkflowKind = WorkflowKind.Value;
            if (RequiresExcel.HasValue)
                draft.RequiresExcel = RequiresExcel.Value;
            if (RequiresOrderPdf.HasValue)
                draft.RequiresOrderPdf = RequiresOrderPdf.Value;

            if (!string.IsNullOrWhiteSpace(DescriptionNotes))
                draft.AnalysisLog.Insert(0, "KI-Beschreibung: " + DescriptionNotes.Trim());

            return draft;
        }
    }

    internal static class CustomerProfileDescriptionParser
    {
        private static readonly Regex DrawingTokenRegex = new Regex(
            @"\b((?:[A-Z]{2,8}[-_./]?\d{2,8}(?:[-_./]\d{2,8})*(?:[-_.][A-Z0-9]+)?)|(?:\d{4,8}[-_./]\d{2,8}(?:[-_.][A-Z0-9]+)?))\b",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex PrefixHintRegex = new Regex(
            @"(?:präfix|prefix|format|beginnt\s+mit|beginnen\s+mit|startet\s+mit|starten\s+mit|nummern\s+mit)\s+([A-Z]{2,6}-?)",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex CustomerNameRegex = new Regex(
            @"(?:neuer\s+)?kunde(?:nprofil)?\s+(?:für\s+|name\s+|heißt\s+|ist\s+)?[""']?([^""'\r\n,.;]+)[""']?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex FirmaNameRegex = new Regex(
            @"(?:firma|unternehmen|kundenname)\s+[""']?([^""'\r\n,.;]+)[""']?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex DrawingColumnRegex = new Regex(
            @"(?:zeichnungs(?:nummer|nr\.?)|drawing|teilenummer|artikelnummer|part\s*number)[^\r\n]{0,40}?(?:spalte|column|header|heißt|heisst|lautet)\s+[""']?([^""'\r\n,;]+)[""']?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex DrawingColumnReverseRegex = new Regex(
            @"(?:spalte|column|header)\s+[""']?([^""'\r\n,;]+)[""']?\s+(?:für|fuer|mit)\s+(?:zeichnungs(?:nummer|nr\.?)|drawing|teilenummer)",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex AmountColumnRegex = new Regex(
            @"(?:menge|bestellmenge|stück|stueck|qty|quantity|amount)[^\r\n]{0,40}?(?:spalte|column|header|heißt|heisst|lautet)\s+[""']?([^""'\r\n,;]+)[""']?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex AmountColumnReverseRegex = new Regex(
            @"(?:spalte|column|header)\s+[""']?([^""'\r\n,;]+)[""']?\s+(?:für|fuer|mit)\s+(?:menge|bestellmenge|stück|stueck|qty|quantity|amount)",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        public static CustomerProfileDescriptionParseResult Parse(string description)
        {
            var result = new CustomerProfileDescriptionParseResult
            {
                DescriptionNotes = (description ?? string.Empty).Trim()
            };

            if (string.IsNullOrWhiteSpace(result.DescriptionNotes))
                return result;

            result.CustomerName = ExtractCustomerName(result.DescriptionNotes);
            result.SampleDrawingNumbers = ExtractDrawingSamples(result.DescriptionNotes);
            result.ExcelDrawingHeaders = ExtractColumnNames(result.DescriptionNotes, DrawingColumnRegex, DrawingColumnReverseRegex,
                new[] { "drawing", "zeichnung", "zeichnungsnummer", "zeichnungsnr", "teilenummer", "part number" });
            result.ExcelAmountHeaders = ExtractColumnNames(result.DescriptionNotes, AmountColumnRegex, AmountColumnReverseRegex,
                new[] { "requested amount", "menge", "bestellmenge", "qty", "quantity", "stück", "stueck", "amount" });

            ApplyWorkflowHints(result);
            ApplyPrefixHint(result);

            if (result.SampleDrawingNumbers.Count > 0)
            {
                result.DrawingNumberPattern = CustomerProfilePatternBuilder.BuildDrawingPattern(result.SampleDrawingNumbers);
                result.AnalysisLog.Add("Zeichnungsmuster aus Beschreibung: " + result.DrawingNumberPattern);
                result.AnalysisLog.Add("Beispiele: " + string.Join(", ", result.SampleDrawingNumbers.Take(6)));
            }

            if (result.ExcelDrawingHeaders.Count > 0)
                result.AnalysisLog.Add("Excel Zeichnungsspalte: " + string.Join(", ", result.ExcelDrawingHeaders));
            if (result.ExcelAmountHeaders.Count > 0)
                result.AnalysisLog.Add("Excel Mengenspalte: " + string.Join(", ", result.ExcelAmountHeaders));
            if (!string.IsNullOrWhiteSpace(result.CustomerName))
                result.AnalysisLog.Insert(0, "Kunde: " + result.CustomerName);

            return result;
        }

        private static string ExtractCustomerName(string text)
        {
            foreach (var regex in new[] { CustomerNameRegex, FirmaNameRegex })
            {
                var match = regex.Match(text);
                if (match.Success)
                {
                    var name = CleanName(match.Groups[1].Value);
                    if (!string.IsNullOrWhiteSpace(name))
                        return name;
                }
            }

            return string.Empty;
        }

        private static List<string> ExtractDrawingSamples(string text)
        {
            var samples = new List<string>();
            foreach (Match match in DrawingTokenRegex.Matches(text ?? string.Empty))
            {
                var token = match.Groups[1].Value.Trim().ToUpperInvariant();
                if (!string.IsNullOrWhiteSpace(token) && OrderVerificationDrawingKey.IsLikelyDrawingToken(token))
                    samples.Add(token);
            }

            return samples
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(20)
                .ToList();
        }

        private static List<string> ExtractColumnNames(string text, Regex forwardRegex, Regex reverseRegex, string[] defaults)
        {
            var columns = new List<string>();
            foreach (var regex in new[] { forwardRegex, reverseRegex })
            {
                var match = regex.Match(text ?? string.Empty);
                if (match.Success)
                {
                    var value = CleanColumnName(match.Groups[1].Value);
                    if (!string.IsNullOrWhiteSpace(value))
                        columns.Add(value);
                }
            }

            var lower = (text ?? string.Empty).ToLowerInvariant();
            foreach (var def in defaults)
            {
                if (lower.Contains(def) && !columns.Any(c => c.Equals(def, StringComparison.OrdinalIgnoreCase)))
                    columns.Add(TitleCaseHeader(def));
            }

            return columns
                .Where(c => !string.IsNullOrWhiteSpace(c))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(4)
                .ToList();
        }

        private static void ApplyWorkflowHints(CustomerProfileDescriptionParseResult result)
        {
            var lower = result.DescriptionNotes.ToLowerInvariant();
            if (lower.Contains("ohne excel") || lower.Contains("keine excel") || lower.Contains("nur bestell-pdf")
                || lower.Contains("nur bestell pdf") || lower.Contains("bestell-pdf statt excel"))
            {
                result.WorkflowKind = CustomerWorkflowKind.PdfOrderListPrimary;
                result.RequiresExcel = false;
                result.RequiresOrderPdf = true;
                result.AnalysisLog.Add("Workflow: Bestell-PDF (ohne Excel)");
                return;
            }

            if (lower.Contains("bestell-pdf") || lower.Contains("bestell pdf") || lower.Contains("auftrags-pdf"))
            {
                result.RequiresOrderPdf = true;
                result.AnalysisLog.Add("Bestell-PDF wird zusätzlich erwartet.");
            }

            if (lower.Contains("mit excel") || lower.Contains("excel als"))
            {
                result.WorkflowKind = CustomerWorkflowKind.ExcelPrimary;
                result.RequiresExcel = true;
                result.AnalysisLog.Add("Workflow: Excel als Hauptquelle");
            }
        }

        private static void ApplyPrefixHint(CustomerProfileDescriptionParseResult result)
        {
            var match = PrefixHintRegex.Match(result.DescriptionNotes ?? string.Empty);
            if (!match.Success)
                return;

            var prefix = match.Groups[1].Value.Trim().ToUpperInvariant();
            if (!prefix.EndsWith("-", StringComparison.Ordinal))
                prefix += "-";

            result.DrawingNumberPattern = @"\b" + Regex.Escape(prefix) + @"\d{2,6}-\d{2,6}(?:-[A-Z0-9]+)?\b";
            result.AnalysisLog.Add("Präfix aus Beschreibung: " + prefix);
        }

        private static string CleanName(string value)
        {
            var cleaned = (value ?? string.Empty).Trim().Trim('"', '\'', '.', ',', ';');
            if (cleaned.Length > 60)
                cleaned = cleaned.Substring(0, 60).Trim();
            return cleaned;
        }

        private static string CleanColumnName(string value)
        {
            return (value ?? string.Empty).Trim().Trim('"', '\'', '.', ',', ';');
        }

        private static string TitleCaseHeader(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;

            if (value.Equals("drawing", StringComparison.OrdinalIgnoreCase))
                return "Drawing";
            if (value.Equals("requested amount", StringComparison.OrdinalIgnoreCase))
                return "Requested Amount";

            return char.ToUpperInvariant(value[0]) + value.Substring(1);
        }
    }
}
