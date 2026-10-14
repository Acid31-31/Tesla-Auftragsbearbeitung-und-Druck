using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using PDF_umbenenen;
using PDF_umbenenen.Models;

namespace PDF_umbenenen.Services.Ai
{
    internal sealed class AiChatService
    {
        private readonly OfflineAiOrchestrator _orchestrator;
        private readonly List<AiChatMessage> _history = new List<AiChatMessage>();
        private AiQualityReport _lastQualityReport;
        private string _appContext = string.Empty;

        public AiChatService(OfflineAiOrchestrator orchestrator)
        {
            _orchestrator = orchestrator;
        }

        public IReadOnlyList<AiChatMessage> History => _history;

        public void SetAppContext(string context)
        {
            _appContext = context ?? string.Empty;
        }

        public void SetLastQualityReport(AiQualityReport report)
        {
            _lastQualityReport = report;
        }

        public async Task<AiChatMessage> SendAsync(string userMessage, CancellationToken cancellationToken)
        {
            var user = new AiChatMessage("user", userMessage);
            _history.Add(user);

            string responseText = null;

            if (_orchestrator != null && _orchestrator.Settings.OllamaEnabled)
            {
                if (!_orchestrator.CanUseOllama())
                    await _orchestrator.RefreshAvailabilityAsync(cancellationToken).ConfigureAwait(false);

                if (_orchestrator.CanUseOllama())
                {
                    var systemPrompt = BuildSystemPrompt();
                    var conversationPrompt = BuildConversationPrompt(userMessage);
                    responseText = await _orchestrator.ChatAsync(systemPrompt, conversationPrompt, cancellationToken)
                        .ConfigureAwait(false);

                    if (LooksLikeGenericFallback(responseText, userMessage) || LooksLikeEnglish(responseText))
                        responseText = null;
                }
            }

            if (string.IsNullOrWhiteSpace(responseText))
                responseText = AiHelpKnowledge.TryGetAnswer(userMessage, _appContext);

            if (string.IsNullOrWhiteSpace(responseText))
                responseText = AiHelpKnowledge.GetGeneralHelp();

            var assistant = new AiChatMessage("assistant", responseText.Trim());
            _history.Add(assistant);
            return assistant;
        }

        private static bool LooksLikeGenericFallback(string response, string userMessage)
        {
            if (string.IsNullOrWhiteSpace(response))
                return true;

            return response.Contains("KI-Offline-Modus (Regeln)")
                   || response.Contains("Ihre Frage:")
                   || (response.Contains("Prüfen Sie den Wasserzeichen-Schalter") && !(userMessage ?? string.Empty).ToLowerInvariant().Contains("manuell"));
        }

        public static bool LooksLikeEnglish(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return false;

            var lower = text.ToLowerInvariant();
            if (lower.Contains("ä") || lower.Contains("ö") || lower.Contains("ü") || lower.Contains("ß")
                || lower.Contains(" und ") || lower.Contains(" der ") || lower.Contains(" die ")
                || lower.Contains(" bitte ") || lower.Contains(" zeichnung"))
                return false;

            var englishHits = 0;
            if (lower.Contains(" the ")) englishHits++;
            if (lower.Contains(" and ")) englishHits++;
            if (lower.Contains(" you ")) englishHits++;
            if (lower.Contains(" please ")) englishHits++;
            if (lower.Contains(" drawing ")) englishHits++;
            return englishHits >= 2;
        }

        private string BuildSystemPrompt()
        {
            var sb = new StringBuilder();
            sb.AppendLine("Du bist der Hilfe-Assistent der App '" + AppInfo.ProductName + "'.");
            sb.AppendLine("Antworte auf Deutsch, konkret auf die gestellte Frage – keine generischen Standardtexte wiederholen.");
            sb.AppendLine("Wenn nach manuellen Wasserzeichen gefragt wird:");
            sb.AppendLine("- Manuell: Schalter An/Aus und Feld Auftragsnummer.");
            sb.AppendLine("- Automatisch aus Excel: Menge, Lieferdatum, Lieferort (nicht einzeln im Dialog editierbar).");
            sb.AppendLine("- Werte ändern: Excel anpassen und erneut verarbeiten.");
            sb.AppendLine();
            sb.AppendLine(LoadManualExcerpt());
            if (!string.IsNullOrWhiteSpace(_appContext))
            {
                sb.AppendLine();
                sb.AppendLine("Aktueller App-Kontext:");
                sb.AppendLine(_appContext);
            }

            if (_lastQualityReport != null)
            {
                sb.AppendLine();
                sb.AppendLine("Letzter Qualitätsreport:");
                sb.AppendLine(_lastQualityReport.ToDisplayText());
            }

            return sb.ToString();
        }

        private string BuildConversationPrompt(string userMessage)
        {
            var sb = new StringBuilder();
            var priorCount = Math.Max(0, _history.Count - 1);
            var skip = Math.Max(0, priorCount - 8);
            foreach (var msg in _history.Take(priorCount).Skip(skip))
            {
                sb.Append(msg.Role).Append(": ").AppendLine(msg.Content);
            }

            sb.Append("user: ").Append(userMessage ?? string.Empty);
            return sb.ToString();
        }

        private static string LoadManualExcerpt()
        {
            var candidates = new[]
            {
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "BEDIENUNGSANLEITUNG.md"),
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "BEDIENUNGSANLEITUNG.md"),
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "BEDIENUNGSANLEITUNG.md")
            };

            foreach (var path in candidates.Select(Path.GetFullPath))
            {
                if (!File.Exists(path))
                    continue;

                var text = File.ReadAllText(path);
                return text.Length <= 3500 ? text : text.Substring(0, 3500);
            }

            return "Bedienungsanleitung nicht gefunden.";
        }
    }
}
