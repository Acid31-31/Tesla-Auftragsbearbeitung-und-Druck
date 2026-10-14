using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using PDF_umbenenen.Models;
using PDF_umbenenen.Services.Ai;

namespace PDF_umbenenen.Services
{
    internal static class CustomerProfileLlmService
    {
        public static async Task<CustomerProfileDescriptionParseResult> TryParseAsync(
            string description,
            OfflineAiOrchestrator orchestrator,
            CancellationToken cancellationToken)
        {
            if (orchestrator == null || !orchestrator.CanUseOllama() || string.IsNullOrWhiteSpace(description))
                return null;

            var prompt = BuildPrompt(description);
            var raw = await orchestrator.CompleteAsync(prompt, jsonFormat: true, cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(raw))
                return null;

            var json = SimpleJsonHelper.ExtractJsonObject(raw);
            if (string.IsNullOrWhiteSpace(json))
                json = raw;

            var parsed = MapJsonToResult(json, description);
            return parsed?.HasActionableData == true ? parsed : null;
        }

        private static string BuildPrompt(string description)
        {
            var sb = new StringBuilder();
            sb.AppendLine("Du extrahierst ein Kundenprofil für eine PDF-Sortier-App (Blechbearbeitung).");
            sb.AppendLine("Antworte NUR mit einem JSON-Objekt, ohne Erklärung:");
            sb.AppendLine("{");
            sb.AppendLine("  \"customerName\": \"Anzeigename\",");
            sb.AppendLine("  \"sampleDrawingNumbers\": [\"BEISPIEL-123-456\"],");
            sb.AppendLine("  \"excelDrawingHeaders\": [\"Drawing\"],");
            sb.AppendLine("  \"excelAmountHeaders\": [\"Requested Amount\"],");
            sb.AppendLine("  \"requiresExcel\": true,");
            sb.AppendLine("  \"requiresOrderPdf\": false,");
            sb.AppendLine("  \"drawingNumberPattern\": \"optional regex\",");
            sb.AppendLine("  \"notes\": \"kurze Zusammenfassung\"");
            sb.AppendLine("}");
            sb.AppendLine("Regeln:");
            sb.AppendLine("- sampleDrawingNumbers: konkrete Beispiele aus dem Text");
            sb.AppendLine("- requiresExcel=false wenn nur Bestell-PDF");
            sb.AppendLine("- Spaltennamen exakt wie vom Nutzer genannt");
            sb.AppendLine();
            sb.AppendLine("Nutzerbeschreibung:");
            sb.AppendLine(description.Trim());
            return sb.ToString();
        }

        private static CustomerProfileDescriptionParseResult MapJsonToResult(string json, string originalDescription)
        {
            if (string.IsNullOrWhiteSpace(json))
                return null;

            var result = new CustomerProfileDescriptionParseResult
            {
                DescriptionNotes = originalDescription?.Trim() ?? string.Empty,
                CustomerName = SimpleJsonHelper.GetJsonString(json, "customerName"),
                DrawingNumberPattern = SimpleJsonHelper.GetJsonString(json, "drawingNumberPattern")
            };

            result.SampleDrawingNumbers = ReadStringArray(json, "sampleDrawingNumbers");
            result.ExcelDrawingHeaders = ReadStringArray(json, "excelDrawingHeaders");
            result.ExcelAmountHeaders = ReadStringArray(json, "excelAmountHeaders");

            if (SimpleJsonHelper.GetJsonBool(json, "requiresExcel", true) == false)
            {
                result.RequiresExcel = false;
                result.WorkflowKind = CustomerWorkflowKind.PdfOrderListPrimary;
            }
            else
            {
                result.RequiresExcel = true;
                result.WorkflowKind = CustomerWorkflowKind.ExcelPrimary;
            }

            result.RequiresOrderPdf = SimpleJsonHelper.GetJsonBool(json, "requiresOrderPdf", false);

            var notes = SimpleJsonHelper.GetJsonString(json, "notes");
            if (!string.IsNullOrWhiteSpace(notes))
                result.AnalysisLog.Add("Ollama: " + notes);

            if (result.SampleDrawingNumbers.Count > 0)
            {
                result.DrawingNumberPattern = CustomerProfilePatternBuilder.BuildDrawingPattern(result.SampleDrawingNumbers);
                result.AnalysisLog.Add("Zeichnungsmuster (Ollama): " + result.DrawingNumberPattern);
            }

            if (!string.IsNullOrWhiteSpace(result.CustomerName))
                result.AnalysisLog.Insert(0, "Kunde (Ollama): " + result.CustomerName);

            return result;
        }

        public static CustomerProfileDescriptionParseResult Merge(
            CustomerProfileDescriptionParseResult rules,
            CustomerProfileDescriptionParseResult llm)
        {
            rules = rules ?? new CustomerProfileDescriptionParseResult();
            if (llm == null)
                return rules;

            if (string.IsNullOrWhiteSpace(rules.CustomerName) && !string.IsNullOrWhiteSpace(llm.CustomerName))
                rules.CustomerName = llm.CustomerName;

            rules.SampleDrawingNumbers = rules.SampleDrawingNumbers
                .Concat(llm.SampleDrawingNumbers ?? new List<string>())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(20)
                .ToList();

            rules.ExcelDrawingHeaders = rules.ExcelDrawingHeaders
                .Concat(llm.ExcelDrawingHeaders ?? new List<string>())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            rules.ExcelAmountHeaders = rules.ExcelAmountHeaders
                .Concat(llm.ExcelAmountHeaders ?? new List<string>())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (llm.WorkflowKind.HasValue)
                rules.WorkflowKind = llm.WorkflowKind;
            if (llm.RequiresExcel.HasValue)
                rules.RequiresExcel = llm.RequiresExcel;
            if (llm.RequiresOrderPdf.HasValue)
                rules.RequiresOrderPdf = llm.RequiresOrderPdf;

            if (rules.SampleDrawingNumbers.Count > 0)
                rules.DrawingNumberPattern = CustomerProfilePatternBuilder.BuildDrawingPattern(rules.SampleDrawingNumbers);
            else if (!string.IsNullOrWhiteSpace(llm.DrawingNumberPattern))
                rules.DrawingNumberPattern = llm.DrawingNumberPattern;

            foreach (var line in llm.AnalysisLog ?? new List<string>())
            {
                if (!rules.AnalysisLog.Contains(line))
                    rules.AnalysisLog.Add(line);
            }

            return rules;
        }

        private static List<string> ReadStringArray(string json, string key)
        {
            var match = Regex.Match(
                json ?? string.Empty,
                "\"" + Regex.Escape(key) + "\"\\s*:\\s*\\[(.*?)\\]",
                RegexOptions.Singleline | RegexOptions.IgnoreCase);
            if (!match.Success)
                return new List<string>();

            var values = new List<string>();
            foreach (Match item in Regex.Matches(match.Groups[1].Value, "\"((?:\\\\.|[^\"\\\\])*)\""))
            {
                var value = item.Groups[1].Value
                    .Replace("\\\"", "\"")
                    .Replace("\\\\", "\\")
                    .Trim();
                if (!string.IsNullOrWhiteSpace(value))
                    values.Add(value);
            }

            return values;
        }
    }
}
