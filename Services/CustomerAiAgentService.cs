using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using PDF_umbenenen.Models;
using PDF_umbenenen.Services.Ai;

namespace PDF_umbenenen.Services
{
    /// <summary>
    /// Kunden-Assistent: denkt (Ollama), setzt um, speichert Lernen im Profil-Draft.
    /// </summary>
    internal static class CustomerAiAgentService
    {
        public sealed class AgentResult
        {
            public string Reply { get; set; } = string.Empty;

            public bool DidChange { get; set; }

            public bool UsedLlm { get; set; }

            /// <summary>Sortier-Vorschau-Fenster öffnen (ohne Ollama).</summary>
            public bool OpenSortPreview { get; set; }
        }

        public static async Task<AgentResult> HandleAsync(
            string userMessage,
            CustomerProfileEditorContext context,
            OfflineAiOrchestrator orchestrator,
            IReadOnlyList<AiChatMessage> chatHistory,
            CancellationToken cancellationToken)
        {
            var text = (userMessage ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(text))
                return new AgentResult { Reply = string.Empty };

            if (IsPureGreeting(text))
            {
                return new AgentResult
                {
                    Reply = "Hallo – ich bin bereit. Sag mir, was ich ändern soll "
                            + "(z. B. Stückzahl-Position, Excel-Spalte, Zeichnungspräfix) – ich setze es um und merke es mir."
                };
            }

            // App-Befehle zuerst – kein Ollama nötig
            if (WantsSortPreview(text))
            {
                return new AgentResult
                {
                    Reply = "Öffne die Sortier-Vorschau …",
                    OpenSortPreview = true
                };
            }

            // Bekannte Umsetzungen zuerst (schnell + zuverlässig), Ollama ergänzt bei Unklarem
            var fallbackFirst = CustomerProfileEditorChatService.TryHandleWatermarkAction(text, chatHistory);
            if (!string.IsNullOrWhiteSpace(fallbackFirst))
            {
                Learn(context, "Wasserzeichen/Stückzahl-Position angepasst.");
                return new AgentResult { Reply = fallbackFirst, DidChange = true, UsedLlm = false };
            }

            // Zeichnungsnummern aus Staging-Dateinamen nachziehen (ZIP → RT1 10 00 0001.pdf)
            if (LooksLikeDrawingHint(text) && TryHarvestDrawingSamplesFromStaging(context))
            {
                return new AgentResult
                {
                    Reply = "Umgesetzt und gemerkt:\r\n" + BuildProfileChangeSummary(context.Draft)
                            + "\r\nBeispiele: "
                            + string.Join(", ", (context.Draft.SampleDrawingNumbers ?? new List<string>()).Take(6))
                            + "\r\nBitte erneut «Analysieren» oder Sortier-Vorschau prüfen.",
                    DidChange = true,
                    UsedLlm = false
                };
            }

            // Denken: Ollama liefert Aktion + Antwort
            if (orchestrator != null && orchestrator.Settings.AiEnabled && orchestrator.Settings.OllamaEnabled)
            {
                using (var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
                {
                    cts.CancelAfter(TimeSpan.FromSeconds(35));
                    try
                    {
                        var llm = await ThinkAsync(text, context, orchestrator, chatHistory, cts.Token)
                            .ConfigureAwait(false);
                        if (llm != null)
                        {
                            var applied = ApplyDecision(llm, context);
                            return new AgentResult
                            {
                                Reply = string.IsNullOrWhiteSpace(applied.Reply) ? llm.Reply : applied.Reply,
                                DidChange = applied.DidChange,
                                UsedLlm = true,
                                OpenSortPreview = applied.OpenSortPreview
                            };
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        // Fallback unten
                    }
                }
            }

            var parsed = CustomerProfileDescriptionParser.Parse(text);
            if (parsed != null && parsed.HasActionableData && context?.Draft != null)
            {
                var changed = ApplyDescriptionParse(parsed, context);
                if (changed)
                {
                    Learn(context, "Profil aus Textbeschreibung angepasst: " + text);
                    return new AgentResult
                    {
                        Reply = "Umgesetzt und gemerkt:\r\n" + BuildProfileChangeSummary(context.Draft)
                                + "\r\nBitte Sortier-Vorschau prüfen.",
                        DidChange = true,
                        UsedLlm = false
                    };
                }
            }

            return new AgentResult
            {
                Reply = BuildHonestLimitMessage(orchestrator),
                DidChange = false,
                UsedLlm = false
            };
        }

        private static string BuildHonestLimitMessage(OfflineAiOrchestrator orchestrator)
        {
            var sb = new StringBuilder();
            sb.AppendLine("Ollama selbst ist in Ordnung – in dieser App-Version ist die Anbindung eingeschränkt:");
            sb.AppendLine("• Läuft auf CPU (kein GPU) → große Modelle sind langsam");
            sb.AppendLine("• Für den Chat nutzen wir oft «tinyllama» (schnell, aber schwach) oder Timeout");
            sb.AppendLine("• «Lernen» = Einstellungen/Profil speichern, kein Nachtrainieren des Modells");
            sb.AppendLine();
            if (orchestrator != null && !string.IsNullOrWhiteSpace(orchestrator.OllamaLastError))
                sb.AppendLine("Technik: " + orchestrator.OllamaLastError);
            sb.AppendLine();
            sb.AppendLine("Was sofort geht ohne Warten:");
            sb.AppendLine("• «Stückzahl nach oben rechts» / «versetzen»");
            sb.AppendLine("• «zeig mir die Vorschau»");
            sb.AppendLine("• «Zeichnungen beginnen mit TSL-»");
            return sb.ToString().TrimEnd();
        }

        private static bool WantsSortPreview(string text)
        {
            var lower = (text ?? string.Empty).ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(lower))
                return false;
            if (lower.Contains("vorschau"))
                return true;
            if (lower.Contains("zeig") && (lower.Contains("sortier") || lower.Contains("pdf") || lower.Contains("ergebnis")))
                return true;
            if (lower.Contains("öffne") && lower.Contains("sortier"))
                return true;
            return false;
        }

        private sealed class LlmDecision
        {
            public string Action { get; set; } = "explain";
            public string Target { get; set; } = string.Empty;
            public string Value { get; set; } = string.Empty;
            public string Reply { get; set; } = string.Empty;
        }

        private static async Task<LlmDecision> ThinkAsync(
            string userMessage,
            CustomerProfileEditorContext context,
            OfflineAiOrchestrator orchestrator,
            IReadOnlyList<AiChatMessage> chatHistory,
            CancellationToken cancellationToken)
        {
            if (!orchestrator.CanUseOllama())
                await orchestrator.RefreshAvailabilityAsync(cancellationToken).ConfigureAwait(false);
            if (!orchestrator.CanUseOllama())
                return null;

            // Schnelles Modell für Agent-JSON (CPU)
            var previousModel = orchestrator.Settings.OllamaModel;
            try
            {
                if (string.IsNullOrWhiteSpace(previousModel)
                    || previousModel.IndexOf("llama3.2", StringComparison.OrdinalIgnoreCase) >= 0
                    || previousModel.IndexOf("llava", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    orchestrator.Settings.OllamaModel = "tinyllama";
                }

                var prompt = BuildThinkPrompt(userMessage, context, chatHistory);
                var raw = await orchestrator.CompleteAsync(prompt, jsonFormat: true, cancellationToken)
                    .ConfigureAwait(false);
                if (string.IsNullOrWhiteSpace(raw))
                {
                    // Chat-Fallback ohne strict JSON
                    raw = await orchestrator.ChatOllamaOnlyAsync(
                            "Antworte NUR mit einem JSON-Objekt.",
                            prompt,
                            null,
                            cancellationToken)
                        .ConfigureAwait(false);
                }

                return ParseDecision(raw);
            }
            finally
            {
                orchestrator.Settings.OllamaModel = previousModel;
            }
        }

        private static string BuildThinkPrompt(
            string userMessage,
            CustomerProfileEditorContext context,
            IReadOnlyList<AiChatMessage> chatHistory)
        {
            var sb = new StringBuilder();
            sb.AppendLine("Du bist der Kunden-Assistent in DOK-V01 (Blechbearbeitung, PDF-Sortierung).");
            sb.AppendLine("Du denkst, setzt um und lernst. Antworte NUR mit JSON:");
            sb.AppendLine("{");
            sb.AppendLine("  \"action\": \"move_quantity|open_preview|set_prefix|set_excel_drawing|set_excel_amount|set_workflow|set_name|explain\",");
            sb.AppendLine("  \"target\": \"top_left|top_right|toggle|excel|order_pdf\",");
            sb.AppendLine("  \"value\": \"freier Text z.B. MK- oder Spaltenname\",");
            sb.AppendLine("  \"reply\": \"kurze deutsche Bestätigung was du umsetzt/gelernt hast\"");
            sb.AppendLine("}");
            sb.AppendLine("Regeln:");
            sb.AppendLine("- open_preview: Nutzer will Sortier-Vorschau / PDF sehen («zeig mir die Vorschau»)");
            sb.AppendLine("- move_quantity: Stückzahl/Menge/Liefertermin-Band verschieben (target: top_left, top_right oder toggle)");
            sb.AppendLine("- set_prefix: Zeichnungspräfix (value: z.B. TSL- oder MK-)");
            sb.AppendLine("- set_excel_drawing / set_excel_amount: Excel-Spaltenname in value");
            sb.AppendLine("- set_workflow: nur wenn der Nutzer den Workflow ausdrücklich ändert");
            sb.AppendLine("- Die angekreuzten Unterlagen gelten: Excel → excel; Bestell-PDF oder Text-Bestellung → order_pdf. Nicht dagegen arbeiten.");
            sb.AppendLine("- set_name: Kundenname in value");
            sb.AppendLine("- explain: nur erklären, nichts ändern");
            sb.AppendLine("- Wenn Nutzer «umsetzen» sagt nach Stückzahl-Frage → move_quantity toggle");
            sb.AppendLine();
            sb.AppendLine("Aktueller Kontext:");
            if (context != null)
            {
                sb.AppendLine("Kunde: " + (context.CustomerName ?? ""));
                if (context.Draft != null)
                {
                    sb.AppendLine("Zeichnungen gefunden: " + (context.Draft.SampleDrawingNumbers?.Count ?? 0));
                    sb.AppendLine("Muster: " + (context.Draft.DrawingNumberPattern ?? ""));
                    sb.AppendLine("Excel Drawing: " + string.Join(",", context.Draft.ExcelDrawingHeaders ?? new List<string>()));
                    sb.AppendLine("Excel Menge: " + string.Join(",", context.Draft.ExcelAmountHeaders ?? new List<string>()));
                    sb.AppendLine("RequiresExcel: " + context.Draft.RequiresExcel);
                }

                sb.AppendLine("Ankreuzung Unterlagen: Zeichnungen=" + (context.DeclaresDrawings ? "ja" : "nein")
                              + ", Excel=" + (context.DeclaresExcel ? "ja" : "nein")
                              + ", Bestell-PDF=" + (context.DeclaresOrderPdf ? "ja" : "nein")
                              + ", Text-Bestellung=" + (context.DeclaresOrderText ? "ja" : "nein"));
            }

            var wm = WatermarkSettingsStore.Load();
            sb.AppendLine("Wasserzeichen LT/Stückzahl: " + WatermarkSettings.GetDeliveryDatePositionLabel(wm.DeliveryDatePosition));
            sb.AppendLine("SmartPlacement: " + wm.UseSmartPlacement);

            if (chatHistory != null && chatHistory.Count > 0)
            {
                sb.AppendLine("Letzte Chatzeilen:");
                foreach (var msg in chatHistory.Reverse().Take(6).Reverse())
                {
                    if (msg == null || string.IsNullOrWhiteSpace(msg.Content))
                        continue;
                    sb.AppendLine((msg.IsUser ? "User: " : "KI: ") + msg.Content.Trim());
                }
            }

            sb.AppendLine();
            sb.AppendLine("Nutzer jetzt:");
            sb.AppendLine(userMessage);
            return sb.ToString();
        }

        private static LlmDecision ParseDecision(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
                return null;

            var json = SimpleJsonHelper.ExtractJsonObject(raw) ?? raw;
            var action = SimpleJsonHelper.GetJsonString(json, "action");
            var reply = SimpleJsonHelper.GetJsonString(json, "reply");
            if (string.IsNullOrWhiteSpace(action) && string.IsNullOrWhiteSpace(reply))
                return null;

            return new LlmDecision
            {
                Action = string.IsNullOrWhiteSpace(action) ? "explain" : action.Trim().ToLowerInvariant(),
                Target = (SimpleJsonHelper.GetJsonString(json, "target") ?? string.Empty).Trim().ToLowerInvariant(),
                Value = (SimpleJsonHelper.GetJsonString(json, "value") ?? string.Empty).Trim(),
                Reply = string.IsNullOrWhiteSpace(reply) ? string.Empty : reply.Trim()
            };
        }

        private static AgentResult ApplyDecision(LlmDecision decision, CustomerProfileEditorContext context)
        {
            if (decision == null)
                return new AgentResult { Reply = string.Empty };

            var action = decision.Action ?? "explain";
            var changed = false;
            var openPreview = false;
            var parts = new List<string>();

            if (action.Contains("preview") || action.Contains("vorschau") || action == "open_preview")
            {
                return new AgentResult
                {
                    Reply = string.IsNullOrWhiteSpace(decision.Reply)
                        ? "Öffne die Sortier-Vorschau …"
                        : decision.Reply,
                    OpenSortPreview = true
                };
            }

            if (action.Contains("move") || action.Contains("quantity") || action.Contains("stück") || action == "move_quantity")
            {
                var wm = WatermarkSettingsStore.Load();
                var before = WatermarkSettings.GetDeliveryDatePositionLabel(wm.DeliveryDatePosition);
                wm.UseSmartPlacement = true;
                wm.WatermarksEnabled = true;
                if (decision.Target.Contains("left") && !decision.Target.Contains("right"))
                    wm.DeliveryDatePosition = DeliveryDatePosition.TopLeft;
                else if (decision.Target.Contains("right"))
                    wm.DeliveryDatePosition = DeliveryDatePosition.TopRight;
                else
                    wm.DeliveryDatePosition = wm.DeliveryDatePosition == DeliveryDatePosition.TopLeft
                        ? DeliveryDatePosition.TopRight
                        : DeliveryDatePosition.TopLeft;
                WatermarkSettingsStore.Save(wm);
                var after = WatermarkSettings.GetDeliveryDatePositionLabel(wm.DeliveryDatePosition);
                parts.Add("Stückzahl/LT-Band: " + before + " → " + after);
                Learn(context, "Gelernt: Stückzahl/LT-Position = " + after);
                changed = true;
            }
            else if (action.Contains("prefix") || action == "set_prefix")
            {
                var prefix = decision.Value;
                if (string.IsNullOrWhiteSpace(prefix) && !string.IsNullOrWhiteSpace(decision.Target))
                    prefix = decision.Target;
                if (!string.IsNullOrWhiteSpace(prefix) && context?.Draft != null)
                {
                    prefix = prefix.Trim().ToUpperInvariant().TrimEnd('-', ' ');
                    // Leerzeichen-Formate (EcoMark RT1 10 00 0001) vs. Bindestrich (TSL-1227-125)
                    var useSpaced = context.Draft.SampleDrawingNumbers != null
                        && context.Draft.SampleDrawingNumbers.Any(s =>
                            s != null && s.IndexOf(' ') >= 0 && s.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
                    if (!useSpaced
                        && context.StagingFiles != null
                        && context.StagingFiles.Any(f =>
                        {
                            var n = Path.GetFileNameWithoutExtension(f) ?? string.Empty;
                            return n.IndexOf(' ') >= 0
                                   && n.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
                        }))
                    {
                        useSpaced = true;
                    }

                    if (useSpaced)
                    {
                        context.Draft.DrawingNumberPattern =
                            @"\b" + System.Text.RegularExpressions.Regex.Escape(prefix)
                            + @"(?:[\s\-_/]+\d{1,5}){2,5}\b";
                    }
                    else
                    {
                        if (!prefix.EndsWith("-", StringComparison.Ordinal) && prefix.Length <= 8)
                            prefix = prefix + "-";
                        context.Draft.DrawingNumberPattern =
                            @"\b" + System.Text.RegularExpressions.Regex.Escape(prefix)
                            + @"\d{2,8}(?:[-_./]\d{2,8})*(?:[-_.][A-Z0-9]+)?\b";
                    }

                    // Dateinamen der Staging-PDFs als Beispiele übernehmen
                    if (context.StagingFiles != null)
                    {
                        foreach (var f in context.StagingFiles)
                        {
                            var name = Path.GetFileNameWithoutExtension(f);
                            if (OrderVerificationDrawingKey.IsLikelyDrawingToken(name)
                                && !context.Draft.SampleDrawingNumbers.Any(x =>
                                    string.Equals(x, name, StringComparison.OrdinalIgnoreCase)))
                            {
                                context.Draft.SampleDrawingNumbers.Add(name.Trim().ToUpperInvariant());
                            }
                        }

                        if (context.Draft.SampleDrawingNumbers.Count > 0)
                        {
                            context.Draft.DrawingNumberPattern =
                                CustomerProfilePatternBuilder.BuildDrawingPattern(context.Draft.SampleDrawingNumbers);
                        }
                    }

                    parts.Add("Zeichnungspräfix/Muster: " + prefix.TrimEnd('-'));
                    Learn(context, "Gelernt: Zeichnungspräfix " + prefix.TrimEnd('-'));
                    changed = true;
                }
            }
            else if (action.Contains("excel_drawing") || action == "set_excel_drawing")
            {
                if (!string.IsNullOrWhiteSpace(decision.Value) && context?.Draft != null)
                {
                    AddUnique(context.Draft.ExcelDrawingHeaders, decision.Value);
                    parts.Add("Excel Zeichnungsspalte: " + decision.Value);
                    Learn(context, "Gelernt: Excel-Zeichnungsspalte " + decision.Value);
                    changed = true;
                }
            }
            else if (action.Contains("excel_amount") || action == "set_excel_amount")
            {
                if (!string.IsNullOrWhiteSpace(decision.Value) && context?.Draft != null)
                {
                    AddUnique(context.Draft.ExcelAmountHeaders, decision.Value);
                    parts.Add("Excel Mengenspalte: " + decision.Value);
                    Learn(context, "Gelernt: Excel-Mengenspalte " + decision.Value);
                    changed = true;
                }
            }
            else if (action.Contains("workflow") || action == "set_workflow")
            {
                if (context?.Draft != null)
                {
                    if (context.DeclaresExcel)
                    {
                        context.Draft.EnsureExcelWorkflow();
                        parts.Add("Workflow: Excel (laut Auswahl, keine Pflicht-Prüf-PDF)");
                    }
                    else if (context.DeclaresOrderPdf || context.DeclaresOrderText
                             || ((decision.Target.Contains("order") || decision.Target.Contains("pdf")
                                  || decision.Target.Contains("bestell") || decision.Target.Contains("text"))
                                 && !StagingHasExcel(context)))
                    {
                        context.Draft.EnsureOrderPdfWorkflow();
                        parts.Add(context.DeclaresOrderText && !context.DeclaresOrderPdf
                            ? "Workflow: Text-Bestellung"
                            : "Workflow: Bestell-PDF");
                    }
                    else
                    {
                        context.Draft.EnsureExcelWorkflow();
                        parts.Add("Workflow: Excel (keine Prüf-PDF)");
                    }

                    Learn(context, "Gelernt: Workflow " + parts[parts.Count - 1]);
                    changed = true;
                }
            }
            else if (action.Contains("name") || action == "set_name")
            {
                if (!string.IsNullOrWhiteSpace(decision.Value) && context != null)
                {
                    context.CustomerName = decision.Value.Trim();
                    parts.Add("Kundenname: " + context.CustomerName);
                    Learn(context, "Gelernt: Kundenname " + context.CustomerName);
                    changed = true;
                }
            }

            var reply = decision.Reply;
            if (changed)
            {
                var body = "Umgesetzt und gelernt:\r\n• " + string.Join("\r\n• ", parts);
                if (!string.IsNullOrWhiteSpace(reply))
                    body += "\r\n\r\n" + reply;
                body += "\r\n\r\nBitte Sortier-Vorschau prüfen. Beim Speichern bleibt das im Kundenprofil.";
                return new AgentResult { Reply = body, DidChange = true, OpenSortPreview = openPreview };
            }

            if (string.IsNullOrWhiteSpace(reply))
                reply = "Verstanden – keine Einstellung geändert. Formuliere die gewünschte Änderung klarer.";

            return new AgentResult { Reply = reply, DidChange = false, OpenSortPreview = openPreview };
        }

        private static bool ApplyDescriptionParse(CustomerProfileDescriptionParseResult parsed, CustomerProfileEditorContext context)
        {
            if (parsed == null || context?.Draft == null)
                return false;

            var changed = false;
            if (!string.IsNullOrWhiteSpace(parsed.CustomerName))
            {
                context.CustomerName = parsed.CustomerName.Trim();
                changed = true;
            }

            if (parsed.SampleDrawingNumbers != null)
            {
                foreach (var s in parsed.SampleDrawingNumbers)
                {
                    if (string.IsNullOrWhiteSpace(s)) continue;
                    if (context.Draft.SampleDrawingNumbers.Any(x => string.Equals(x, s, StringComparison.OrdinalIgnoreCase)))
                        continue;
                    context.Draft.SampleDrawingNumbers.Add(s.Trim().ToUpperInvariant());
                    changed = true;
                }
            }

            if (!string.IsNullOrWhiteSpace(parsed.DrawingNumberPattern)
                && !string.Equals(parsed.DrawingNumberPattern, CustomerProfile.DefaultDrawingPattern, StringComparison.Ordinal))
            {
                context.Draft.DrawingNumberPattern = parsed.DrawingNumberPattern;
                changed = true;
            }

            if (parsed.ExcelDrawingHeaders != null)
            {
                foreach (var h in parsed.ExcelDrawingHeaders)
                    changed |= AddUnique(context.Draft.ExcelDrawingHeaders, h);
            }

            if (parsed.ExcelAmountHeaders != null)
            {
                foreach (var h in parsed.ExcelAmountHeaders)
                    changed |= AddUnique(context.Draft.ExcelAmountHeaders, h);
            }

            if (parsed.RequiresExcel.HasValue)
            {
                context.Draft.RequiresExcel = parsed.RequiresExcel.Value;
                changed = true;
            }

            if (parsed.RequiresOrderPdf.HasValue)
            {
                context.Draft.RequiresOrderPdf = parsed.RequiresOrderPdf.Value;
                changed = true;
            }

            if (parsed.WorkflowKind.HasValue)
            {
                context.Draft.WorkflowKind = parsed.WorkflowKind.Value;
                changed = true;
            }

            return changed;
        }

        private static string BuildProfileChangeSummary(CustomerProfileDraft draft)
        {
            if (draft == null)
                return "(kein Draft)";
            return "Muster: " + draft.DrawingNumberPattern
                   + "\r\nExcel Drawing: " + string.Join(", ", draft.ExcelDrawingHeaders ?? new List<string>())
                   + "\r\nExcel Menge: " + string.Join(", ", draft.ExcelAmountHeaders ?? new List<string>());
        }

        private static void Learn(CustomerProfileEditorContext context, string note)
        {
            if (context?.Draft == null || string.IsNullOrWhiteSpace(note))
                return;
            context.Draft.AnalysisLog = context.Draft.AnalysisLog ?? new List<string>();
            context.Draft.AnalysisLog.Add(note.Trim());
        }

        private static bool StagingHasExcel(CustomerProfileEditorContext context)
        {
            IEnumerable<string> sources = Enumerable.Empty<string>();
            if (context?.StagingFiles != null)
                sources = sources.Concat(context.StagingFiles);
            if (context?.SampleFileNames != null)
                sources = sources.Concat(context.SampleFileNames);

            return sources.Any(f =>
            {
                var ext = Path.GetExtension(f)?.ToLowerInvariant();
                return ext == ".xlsx" || ext == ".xls";
            });
        }

        private static bool AddUnique(List<string> list, string value)
        {
            if (list == null || string.IsNullOrWhiteSpace(value))
                return false;
            if (list.Any(x => string.Equals(x, value, StringComparison.OrdinalIgnoreCase)))
                return false;
            list.Add(value.Trim());
            return true;
        }

        private static bool IsPureGreeting(string text)
        {
            var t = (text ?? string.Empty).Trim().ToLowerInvariant().TrimEnd('?', '!', '.', ' ');
            return t == "hi" || t == "hallo" || t == "hey" || t == "moin"
                   || t == "guten tag" || t == "servus";
        }

        public static bool IsPureGreetingPublic(string text) => IsPureGreeting(text);

        public static bool LooksLikeInstantAction(string text)
        {
            var lower = (text ?? string.Empty).ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(lower))
                return true;
            if (IsPureGreeting(lower))
                return true;
            return lower.Contains("stück") || lower.Contains("stueck") || lower.Contains("menge")
                   || lower.Contains("umsetz") || lower.Contains("verleg") || lower.Contains("verschieb")
                   || lower.Contains("woanders") || lower.Contains("andere stelle") || lower.Contains("präfix")
                   || lower.Contains("beginnt mit") || lower.Contains("beginnen mit")
                   || lower.Contains("fängt mit") || lower.Contains("faengt mit")
                   || lower.Contains("zeichnungsnummer") || lower.Contains("artikelnummer")
                   || lower.Contains("vorschau") || lower.Contains("zeig mir");
        }

        private static bool LooksLikeDrawingHint(string text)
        {
            var lower = (text ?? string.Empty).ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(lower))
                return false;
            return lower.Contains("zeichnung") || lower.Contains("präfix") || lower.Contains("prefix")
                   || lower.Contains("beginnt mit") || lower.Contains("beginnen mit")
                   || lower.Contains("fängt mit") || lower.Contains("faengt mit")
                   || lower.Contains("artikelnummer") || lower.Contains("rt1")
                   || (lower.Contains("rt") && lower.Contains("an"));
        }

        private static bool TryHarvestDrawingSamplesFromStaging(CustomerProfileEditorContext context)
        {
            if (context?.Draft == null)
                return false;

            var sources = new List<string>();
            if (context.StagingFiles != null)
                sources.AddRange(context.StagingFiles);
            if (context.SampleFileNames != null)
                sources.AddRange(context.SampleFileNames);

            if (sources.Count == 0)
                return false;

            var added = 0;
            context.Draft.SampleDrawingNumbers = context.Draft.SampleDrawingNumbers ?? new List<string>();
            foreach (var pathOrName in sources)
            {
                var name = Path.GetFileNameWithoutExtension(pathOrName) ?? string.Empty;
                if (string.IsNullOrWhiteSpace(name))
                    continue;
                if (OrderVerificationFileHelper.IsOrderVerificationFileName(Path.GetFileName(pathOrName) ?? name + ".pdf"))
                    continue;
                if (!OrderVerificationDrawingKey.IsLikelyDrawingToken(name))
                    continue;

                var token = name.Trim().ToUpperInvariant();
                if (context.Draft.SampleDrawingNumbers.Any(x => string.Equals(x, token, StringComparison.OrdinalIgnoreCase)))
                    continue;
                context.Draft.SampleDrawingNumbers.Add(token);
                added++;
            }

            if (added == 0 && context.Draft.SampleDrawingNumbers.Count == 0)
                return false;

            context.Draft.DrawingNumberPattern =
                CustomerProfilePatternBuilder.BuildDrawingPattern(context.Draft.SampleDrawingNumbers);
            Learn(context, "Zeichnungsnummern aus Dateinamen übernommen (" + context.Draft.SampleDrawingNumbers.Count + ").");
            return true;
        }
    }
}
