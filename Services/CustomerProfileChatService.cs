using System;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using PDF_umbenenen.Services.Ai;

namespace PDF_umbenenen.Services
{
    internal sealed class CustomerProfileChatResult
    {
        public bool Handled { get; set; }

        public string Message { get; set; } = string.Empty;

        public CustomerProfileDescriptionParseResult Parsed { get; set; }

        public bool OpenNewCustomerDialog { get; set; }
    }

    internal static class CustomerProfileChatService
    {
        private static readonly string[] TriggerKeywords =
        {
            "neuer kunde", "neuen kunden", "kundenprofil", "kunde anlegen", "kunden anlegen",
            "zeichnungsstruktur", "zeichnungsnummer", "zeichnungsnummern", "bestellmenge",
            "kunden beschreib", "kunde beschreib", "kunden lernen", "kunde lernen"
        };

        public static Task<CustomerProfileChatResult> TryHandleAsync(
            string message,
            OfflineAiOrchestrator orchestrator,
            CancellationToken cancellationToken)
        {
            return TryHandleInternalAsync(message, orchestrator, cancellationToken);
        }

        private static async Task<CustomerProfileChatResult> TryHandleInternalAsync(
            string message,
            OfflineAiOrchestrator orchestrator,
            CancellationToken cancellationToken)
        {
            var lower = (message ?? string.Empty).ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(lower))
                return NotHandled();

            if (!LooksLikeCustomerOnboarding(lower))
                return NotHandled();

            var parsed = CustomerProfileDescriptionParser.Parse(message);
            if (orchestrator != null && orchestrator.CanUseOllama())
            {
                var llmParsed = await CustomerProfileLlmService.TryParseAsync(message, orchestrator, cancellationToken)
                    .ConfigureAwait(false);
                parsed = CustomerProfileLlmService.Merge(parsed, llmParsed);
            }

            if (!parsed.HasActionableData)
            {
                return new CustomerProfileChatResult
                {
                    Handled = true,
                    Message =
                        "Neuen Kunden per Beschreibung anlegen:\r\n\r\n" +
                        "Beschreiben Sie den Kunden in einem Satz, z. B.:\r\n" +
                        "• „Neuer Kunde BMW: Zeichnungen beginnen mit MK-1234-567, Excel-Spalte Teilenummer und Menge in Spalte Qty“\r\n" +
                        "• „Kunde Müller ohne Excel, nur Bestell-PDF, Nummern wie ABC-1227-125“\r\n\r\n" +
                        (orchestrator?.CanUseOllama() == true
                            ? "Ollama ist aktiv und wertet Ihre Textbeschreibung aus.\r\n"
                            : "Tipp: Ollama in KI-Einstellungen aktivieren für bessere Textauswertung.\r\n") +
                        "Optional Beispiel-PDFs/Excel per Drag & Drop im Dialog ergänzen.\r\n" +
                        "Menü: Einstellungen → Kundenprofile → Neu."
                };
            }

            var summary = BuildSummary(parsed, orchestrator?.CanUseOllama() == true);
            return new CustomerProfileChatResult
            {
                Handled = true,
                OpenNewCustomerDialog = true,
                Parsed = parsed,
                Message = summary + "\r\n\r\nIch öffne den Dialog „Neuen Kunden anlegen“ mit diesen Vorschlägen."
            };
        }

        private static bool LooksLikeCustomerOnboarding(string lower)
        {
            if (TriggerKeywords.Any(lower.Contains))
                return true;

            return lower.Contains("kunde") && (
                lower.Contains("zeichnung") || lower.Contains("excel") || lower.Contains("spalte")
                || lower.Contains("bestell") || lower.Contains("präfix") || lower.Contains("prefix")
                || lower.Contains("menge") || lower.Contains("anlegen"));
        }

        private static string BuildSummary(CustomerProfileDescriptionParseResult parsed, bool ollamaUsed)
        {
            var sb = new StringBuilder();
            sb.AppendLine("Erkannt aus Ihrer Beschreibung" + (ollamaUsed ? " (Ollama + Regeln):" : ":"));
            if (!string.IsNullOrWhiteSpace(parsed.CustomerName))
                sb.AppendLine("• Kunde: " + parsed.CustomerName);
            if (parsed.SampleDrawingNumbers.Count > 0)
                sb.AppendLine("• Zeichnungsbeispiele: " + string.Join(", ", parsed.SampleDrawingNumbers.Take(5)));
            if (!string.IsNullOrWhiteSpace(parsed.DrawingNumberPattern))
                sb.AppendLine("• Muster: " + parsed.DrawingNumberPattern);
            if (parsed.ExcelDrawingHeaders.Count > 0)
                sb.AppendLine("• Excel Zeichnung: " + string.Join(", ", parsed.ExcelDrawingHeaders));
            if (parsed.ExcelAmountHeaders.Count > 0)
                sb.AppendLine("• Excel Menge: " + string.Join(", ", parsed.ExcelAmountHeaders));
            if (parsed.WorkflowKind.HasValue)
                sb.AppendLine("• Workflow: " + (parsed.RequiresExcel == false ? "Bestell-PDF" : "Excel"));
            return sb.ToString().TrimEnd();
        }

        private static CustomerProfileChatResult NotHandled()
        {
            return new CustomerProfileChatResult { Handled = false };
        }
    }
}
