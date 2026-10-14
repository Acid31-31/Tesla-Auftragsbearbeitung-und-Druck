using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using PDF_umbenenen.Models;
using PDF_umbenenen.Services.Ai;

namespace PDF_umbenenen.Services
{
    internal static class CustomerProfileEditorChatService
    {
        public static async Task<string> SendAsync(
            string userMessage,
            CustomerProfileEditorContext context,
            OfflineAiOrchestrator orchestrator,
            IReadOnlyList<AiChatMessage> chatHistory,
            CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(userMessage))
                return string.Empty;

            var ruleAnswer = TryGetRuleAnswer(userMessage);
            if (orchestrator == null || !orchestrator.Settings.AiEnabled)
                return ruleAnswer ?? "KI ist deaktiviert. Bitte in den KI-Einstellungen aktivieren.";

            var actionAnswer = TryHandleWatermarkAction(userMessage, chatHistory);
            if (actionAnswer != null)
                return actionAnswer;

            // Nur reine Begrüßungen / Kurzfragen – nicht „hallo, kannst du … umsetzen?“
            if (ruleAnswer != null && IsQuickRuleOnly(userMessage))
                return ruleAnswer;

            // Echtes Sprachmodell hat Vorrang – Regeln nur wenn Ollama fehlt.
            if (orchestrator.Settings.OllamaEnabled)
            {
                using (var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
                {
                    timeoutCts.CancelAfter(TimeSpan.FromSeconds(40));
                    try
                    {
                        var systemPrompt = BuildSystemPrompt(context);
                        var priorMessages = BuildPriorMessages(chatHistory);
                        var response = await orchestrator.ChatOllamaOnlyAsync(
                                systemPrompt,
                                userMessage.Trim(),
                                priorMessages,
                                timeoutCts.Token)
                            .ConfigureAwait(false);

                        if (!string.IsNullOrWhiteSpace(response))
                        {
                            if (IsLanguageLoopMessage(response) || AiChatService.LooksLikeEnglish(response))
                                return "Alles klar – ich helfe auf Deutsch. Nächster Schritt: Analyse prüfen, dann «Sortier-Vorschau» oder «Speichern».";
                            return response.Trim();
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        var detail = orchestrator.OllamaLastError;
                        var msg = "Die KI ist aktiv, hat aber zu lange gebraucht (Timeout ~40 Sekunden). "
                                  + "Das ist kein Aktivierungsproblem – Ollama antwortet nur langsam oder hängt.\r\n";
                        if (!string.IsNullOrWhiteSpace(detail))
                            msg += "Technik: " + detail + "\r\n";
                        msg += "Du kannst trotzdem weiterarbeiten: konkrete Korrektur schreiben "
                               + "(z. B. «Zeichnungen beginnen mit TSL-») oder Sortier-Vorschau nutzen.";
                        return msg;
                    }
                }

                return orchestrator.CanUseOllama()
                    ? BuildOllamaEmptyResponseMessage(orchestrator)
                    : (ruleAnswer ?? BuildOllamaUnavailableMessage(orchestrator));
            }

            if (ruleAnswer != null)
                return ruleAnswer;

            return "Ollama-Schalter ist aus. In den KI-Einstellungen «Ollama Sprachmodell» anhaken "
                   + "(KI-Ordner AI_DOK-V01 neben DOK).";
        }

        public static bool IsQuickRuleOnly(string userMessage)
        {
            var lower = (userMessage ?? string.Empty).ToLowerInvariant().Trim();
            if (string.IsNullOrWhiteSpace(lower))
                return true;
            if (IsPureGreeting(lower))
                return true;
            if (lower.Contains("was jetzt") || lower.Contains("was nun") || lower.Contains("wie weiter")
                || lower.Contains("unterlagen da") || lower.Contains("alles da") || lower.Contains("fertig"))
                return true;
            if (lower.Contains("sortier") && lower.Contains("vorschau"))
                return true;
            if (lower.Contains("ollama") && (lower.Contains("nicht") || lower.Contains("fehlt") || lower.Contains("status")))
                return true;
            if (LooksLikeWatermarkAction(lower))
                return true;
            return false;
        }

        private static bool LooksLikeWatermarkAction(string lower)
        {
            var aboutAmount = MentionsAmount(lower);
            var aboutPlace = MentionsPlaceChange(lower);
            var wantsImplement = lower.Contains("umsetz") || lower.Contains("mach das")
                                 || lower.Contains("mach es") || lower.Contains("bitte ändern")
                                 || lower.Contains("bitte aendern") || lower.Contains("jetzt ändern")
                                 || lower.Contains("tu das") || lower.Contains("erledig");
            return (aboutAmount && aboutPlace) || (aboutAmount && wantsImplement) || wantsImplement;
        }

        /// <summary>
        /// Setzt Wasserzeichen wirklich um (nicht nur erklären) – z. B. Stückzahl/LT-Position wechseln.
        /// </summary>
        public static string TryHandleWatermarkAction(
            string userMessage,
            IReadOnlyList<AiChatMessage> chatHistory = null)
        {
            var lower = (userMessage ?? string.Empty).ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(lower))
                return null;

            var aboutAmount = MentionsAmount(lower);
            var aboutPlace = MentionsPlaceChange(lower);
            var wantsImplement = lower.Contains("umsetz") || lower.Contains("mach das")
                                 || lower.Contains("mach es") || lower.Contains("bitte ändern")
                                 || lower.Contains("bitte aendern") || lower.Contains("jetzt ändern")
                                 || lower.Contains("tu das") || lower.Contains("erledig");

            // Folgefrage „kannst du es umsetzen?“ nach Stückzahl-Diskussion
            if (!aboutAmount && wantsImplement && HistoryMentionsAmount(chatHistory))
                aboutAmount = true;

            if (!aboutAmount || (!aboutPlace && !wantsImplement))
                return null;

            var settings = WatermarkSettingsStore.Load();
            var before = WatermarkSettings.GetDeliveryDatePositionLabel(settings.DeliveryDatePosition);

            settings.UseSmartPlacement = true;
            settings.WatermarksEnabled = true;
            // Andere Stelle: TopLeft <-> TopRight (dort sitzen LT/FT inkl. Menge)
            settings.DeliveryDatePosition = settings.DeliveryDatePosition == DeliveryDatePosition.TopLeft
                ? DeliveryDatePosition.TopRight
                : DeliveryDatePosition.TopLeft;

            WatermarkSettingsStore.Save(settings);
            var after = WatermarkSettings.GetDeliveryDatePositionLabel(settings.DeliveryDatePosition);

            return "Umgesetzt (nicht nur erklärt):\r\n"
                   + "• Intelligente Platzierung: an\r\n"
                   + "• Liefertermin/Stückzahl-Band: " + before + " → " + after + "\r\n\r\n"
                   + "Bitte «Sortier-Vorschau» erneut öffnen – dort erscheinen die Wasserzeichen "
                   + "auf den Zeichnungen. Eine Prüf-PDF ist nicht nötig, wenn eine Excel als Bestellung vorliegt. "
                   + "Beim Speichern des Kundenprofils werden diese Einstellungen mitgeschrieben.";
        }

        private static bool MentionsAmount(string lower)
        {
            return lower.Contains("stück") || lower.Contains("stueck") || lower.Contains("menge")
                   || lower.Contains("qty") || lower.Contains("anzahl") || lower.Contains("stückzahl")
                   || lower.Contains("stueckzahl");
        }

        private static bool MentionsPlaceChange(string lower)
        {
            return lower.Contains("setz") || lower.Contains("platz") || lower.Contains("position")
                   || lower.Contains("woanders") || lower.Contains("wo anders") || lower.Contains("verschieb")
                   || lower.Contains("links") || lower.Contains("rechts") || lower.Contains("oben")
                   || lower.Contains("unten") || lower.Contains("stelle") || lower.Contains("einbau")
                   || lower.Contains("anders") || lower.Contains("andere") || lower.Contains("ort")
                   || lower.Contains("seite") || lower.Contains("verleg");
        }

        private static bool HistoryMentionsAmount(IReadOnlyList<AiChatMessage> chatHistory)
        {
            if (chatHistory == null)
                return false;
            foreach (var msg in chatHistory)
            {
                if (msg == null || string.IsNullOrWhiteSpace(msg.Content))
                    continue;
                var c = msg.Content.ToLowerInvariant();
                if (MentionsAmount(c) || c.Contains("wasserzeichen") || c.Contains("deliverydate")
                    || c.Contains("liefertermin/stück") || c.Contains("stückzahl"))
                    return true;
            }

            return false;
        }

        // obsolete helper removed – see TryHandleWatermarkAction


        private static string BuildOllamaEmptyResponseMessage(OfflineAiOrchestrator orchestrator)
        {
            var message = "Ollama ist aktiv, hat aber keine Text-Antwort geliefert.";
            if (orchestrator != null && !string.IsNullOrWhiteSpace(orchestrator.OllamaLastError))
                message += " " + orchestrator.OllamaLastError;
            message += "\r\nDu kannst trotzdem Korrekturen schreiben oder die Sortier-Vorschau nutzen.";
            return message;
        }

        private static string TryGetRuleAnswer(string userMessage)
        {
            var lower = (userMessage ?? string.Empty).ToLowerInvariant().Trim();
            if (string.IsNullOrWhiteSpace(lower))
                return null;

            if (lower.Contains("ollama") && (lower.Contains("nicht") || lower.Contains("fehlt") || lower.Contains("status")))
                return "Ollama in KI-Einstellungen aktivieren oder Ordner AI_DOK-V01 neben der EXE ablegen.";

            if (IsPureGreeting(lower))
                return "Mir geht’s gut – bereit für dein Kundenprofil. Lade Excel und Zeichnungs-PDFs, klicke «Analysieren», prüfe die erkannte Struktur, dann «Speichern» oder «Sortier-Vorschau».";

            if (lower.Contains("was jetzt") || lower.Contains("was nun") || lower.Contains("wie weiter")
                || lower.Contains("unterlagen da") || lower.Contains("alles da") || lower.Contains("fertig"))
            {
                return "Als Nächstes: «Analysieren» (falls noch nicht), die erkannte Struktur prüfen, dann «Sortier-Vorschau» zum Testen oder «Speichern» für das Kundenprofil.";
            }

            if (lower.Contains("sortier") && lower.Contains("vorschau"))
                return "«Sortier-Vorschau» nutzt die Dateien aus diesem Fenster (Excel + PDFs). Wenn keine PDFs erscheinen: Zeichnungs-PDFs oder den Ordner per Drag & Drop hier hineinziehen.";

            return null;
        }

        private static bool IsPureGreeting(string lower)
        {
            var t = (lower ?? string.Empty).Trim().TrimEnd('?', '!', '.', ' ');
            while (t.EndsWith("?"))
                t = t.TrimEnd('?', ' ');
            return t == "hi" || t == "hallo" || t == "hey" || t == "moin"
                   || t == "guten tag" || t == "servus"
                   || t == "wie gehts" || t == "wie geht's" || t == "wie geht es"
                   || t == "wie geht es dir";
        }

        private static bool IsGreeting(string lower) => IsPureGreeting(lower);

        private static List<(string role, string content)> BuildPriorMessages(IReadOnlyList<AiChatMessage> chatHistory)
        {
            var messages = new List<(string role, string content)>();
            if (chatHistory == null || chatHistory.Count == 0)
                return messages;

            foreach (var message in chatHistory)
            {
                if (message == null || string.IsNullOrWhiteSpace(message.Content))
                    continue;

                if (message.IsUser)
                    messages.Add(("user", message.Content.Trim()));
                else if (message.IsAssistant && !IsWelcomeMessage(message.Content) && !IsLanguageLoopMessage(message.Content))
                    messages.Add(("assistant", message.Content.Trim()));
            }

            if (messages.Count > 0 && messages[messages.Count - 1].role == "user")
                messages.RemoveAt(messages.Count - 1);

            const int maxMessages = 12;
            if (messages.Count > maxMessages)
                messages = messages.Skip(messages.Count - maxMessages).ToList();

            return messages;
        }

        private static bool IsLanguageLoopMessage(string content)
        {
            if (string.IsNullOrWhiteSpace(content))
                return false;

            var lower = content.ToLowerInvariant();
            return lower.Contains("nur auf deutsch")
                   || lower.Contains("nur deutsch")
                   || lower.Contains("keine englischen")
                   || lower.Contains("deutsch sprechen")
                   || lower.Contains("deutschsprechen")
                   || lower.Contains("auffindungslosigkeit");
        }

        private static bool IsWelcomeMessage(string content)
        {
            return !string.IsNullOrWhiteSpace(content)
                   && content.StartsWith("Fragen Sie mich zu diesem Kundenprofil", StringComparison.Ordinal);
        }

        private static string BuildOllamaUnavailableMessage(OfflineAiOrchestrator orchestrator)
        {
            if (orchestrator == null)
                return "Ollama-Verbindung fehlt (Hauptfenster-KI).";

            if (!orchestrator.Settings.OllamaEnabled)
                return "Der Schalter «Ollama Sprachmodell» ist aus. In KI-Einstellungen anhaken – "
                       + "«KI aktiv» allein reicht nicht für den Text-Chat.";

            var message = "Ollama ist eingeschaltet, aber gerade nicht erreichbar "
                          + "(Dienst startet langsam oder KI-Ordner unvollständig).";
            if (!string.IsNullOrWhiteSpace(orchestrator.OllamaLastError))
                message += "\r\n" + orchestrator.OllamaLastError;
            message += "\r\nKein Extra-Aktivieren nötig – ggf. AI-starten.bat im Ordner AI_DOK-V01 ausführen.";
            return message;
        }

        private static string BuildSystemPrompt(CustomerProfileEditorContext context)
        {
            var sb = new StringBuilder();
            sb.AppendLine("Du bist der Assistent in DOK-V01 beim Anlegen/Bearbeiten eines Kunden.");
            sb.AppendLine("Antworte kurz auf Deutsch und hilf konkret bei Analyse, Excel-Spalten, Zeichnungsnummern und Sortier-Vorschau.");
            sb.AppendLine("Erfinde keine Sprachregeln und ermahne den Nutzer nicht wegen der Sprache.");
            sb.AppendLine();

            if (context != null)
            {
                if (!string.IsNullOrWhiteSpace(context.CustomerName))
                    sb.AppendLine("Kunde: " + context.CustomerName);
                if (!string.IsNullOrWhiteSpace(context.DescriptionNotes))
                    sb.AppendLine("Profil-Notizen: " + context.DescriptionNotes);
                if (context.Draft != null)
                {
                    sb.AppendLine("Workflow: " + (context.Draft.RequiresExcel ? "Excel erforderlich" : "Bestell-PDF (ohne Excel)"));
                    if (!string.IsNullOrWhiteSpace(context.Draft.DrawingNumberPattern))
                        sb.AppendLine("Zeichnungsmuster: " + context.Draft.DrawingNumberPattern);
                    if (context.Draft.SampleDrawingNumbers?.Count > 0)
                        sb.AppendLine("Beispiel-Zeichnungsnummern: " + string.Join(", ", context.Draft.SampleDrawingNumbers.Take(8)));
                    if (context.Draft.ExcelDrawingHeaders?.Count > 0)
                        sb.AppendLine("Excel Zeichnungsspalte: " + string.Join(", ", context.Draft.ExcelDrawingHeaders));
                    if (context.Draft.ExcelAmountHeaders?.Count > 0)
                        sb.AppendLine("Excel Mengenspalte: " + string.Join(", ", context.Draft.ExcelAmountHeaders));
                    if (context.Draft.AnalysisLog?.Count > 0)
                        sb.AppendLine("Analyse:\r\n" + string.Join("\r\n", context.Draft.AnalysisLog.Take(8)));
                }

                if (context.SampleFileNames != null && context.SampleFileNames.Count > 0)
                    sb.AppendLine("Beispieldateien: " + string.Join(", ", context.SampleFileNames.Take(8)));
            }

            return sb.ToString();
        }
    }

    internal sealed class CustomerProfileEditorContext
    {
        public string CustomerName { get; set; } = string.Empty;

        public string DescriptionNotes { get; set; } = string.Empty;

        public CustomerProfileDraft Draft { get; set; }

        public IList<string> SampleFileNames { get; set; } = new List<string>();

        /// <summary>Vollpfade der Staging-Dateien (ZIP-Inhalt inkl. Zeichnungs-PDFs).</summary>
        public IList<string> StagingFiles { get; set; } = new List<string>();

        public bool DeclaresDrawings { get; set; }

        public bool DeclaresExcel { get; set; }

        public bool DeclaresOrderPdf { get; set; }

        public bool DeclaresOrderText { get; set; }
    }
}
