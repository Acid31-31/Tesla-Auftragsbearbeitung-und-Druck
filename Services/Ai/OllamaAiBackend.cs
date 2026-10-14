using System;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using PDF_umbenenen.Services;

namespace PDF_umbenenen.Services.Ai
{
    internal sealed class OllamaAiBackend : IAiBackend
    {
        private static readonly HttpClient SharedClient = CreateClient();

        private string _baseUrl = OllamaUrlHelper.DefaultBaseUrl;
        private string _model = "tinyllama";
        private string _lastError = string.Empty;
        private AiSettings _settings = new AiSettings();

        public string Name => "Ollama";

        public string LastError => _lastError ?? string.Empty;

        public void Configure(AiSettings settings)
        {
            _settings = settings ?? new AiSettings();
            _baseUrl = OllamaUrlHelper.NormalizeBaseUrl(_settings.OllamaBaseUrl);
            _model = string.IsNullOrWhiteSpace(_settings.OllamaModel)
                ? "tinyllama"
                : _settings.OllamaModel.Trim();
        }

        public async Task<bool> CheckAvailabilityAsync(CancellationToken cancellationToken)
        {
            _lastError = string.Empty;
            if (!OllamaUrlHelper.IsTrustedLocalUrl(_baseUrl))
            {
                _lastError = "Nur localhost-URLs erlaubt (127.0.0.1 / localhost).";
                return false;
            }

            if (_settings.OllamaUsePortableFolder)
            {
                if (!OllamaPortableService.IsPortableLayoutReady(_settings))
                {
                    _lastError = "KI-Ordner fehlt: " + OllamaPortableService.GetOllamaExecutablePath(_settings);
                    return false;
                }

                if (!await OllamaPortableService.EnsureServerRunningAsync(_settings, cancellationToken).ConfigureAwait(false))
                {
                    _lastError = OllamaPortableService.LastError;
                    return false;
                }
            }

            try
            {
                using (var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
                {
                    cts.CancelAfter(TimeSpan.FromSeconds(6));
                    var response = await SharedClient.GetAsync(_baseUrl + "/api/tags", cts.Token).ConfigureAwait(false);
                    if (!response.IsSuccessStatusCode)
                    {
                        _lastError = "Ollama antwortet nicht (HTTP " + (int)response.StatusCode + ").";
                        return false;
                    }

                    var tagsJson = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                    if (!IsModelListed(tagsJson, _model))
                    {
                        var available = ExtractListedModelNames(tagsJson);
                        var fallback = PickBestTextModel(available);
                        if (!string.IsNullOrWhiteSpace(fallback))
                        {
                            _model = fallback;
                            _settings.OllamaModel = fallback;
                        }
                        else
                        {
                            _lastError = "Modell \"" + _model + "\" fehlt in Ollama."
                                          + (available.Count > 0
                                              ? " Installiert: " + string.Join(", ", available) + "."
                                              : string.Empty)
                                          + " Bitte im KI-Ordner: ollama pull " + _model;
                            return false;
                        }
                    }

                    // Kein Inference-Probe hier: verhindert falsche „Modell fehlt“-Meldungen
                    // und lange Timeouts beim Start. Chat zeigt echte Fehler bei Bedarf.
                    return true;
                }
            }
            catch (OperationCanceledException)
            {
                _lastError = "Ollama-Timeout – läuft der Dienst?";
                return false;
            }
            catch (Exception ex)
            {
                _lastError = ex.Message;
                return false;
            }
        }

        public async Task<string> CompleteAsync(string prompt, bool jsonFormat, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(prompt) || !OllamaUrlHelper.IsTrustedLocalUrl(_baseUrl))
                return string.Empty;

            var primaryModel = _model;
            var result = await CompleteAsyncWithModel(prompt, jsonFormat, primaryModel, cancellationToken)
                .ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(result))
                return result;

            if (!IsModelCrashError(_lastError))
                return string.Empty;

            result = await TryFallbackModelsAsync(
                    async model => await CompleteAsyncWithModel(prompt, jsonFormat, model, cancellationToken)
                        .ConfigureAwait(false),
                    primaryModel,
                    cancellationToken)
                .ConfigureAwait(false);

            if (string.IsNullOrWhiteSpace(result) && IsModelCrashError(_lastError))
                _lastError = BuildModelCrashHelpMessage(_lastError);

            return result ?? string.Empty;
        }

        public async Task<string> ChatAsync(string systemPrompt, string userPrompt, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(userPrompt))
                return string.Empty;

            var messages = new System.Collections.Generic.List<(string role, string content)>
            {
                ("user", userPrompt)
            };
            return await ChatAsync(systemPrompt, messages, cancellationToken).ConfigureAwait(false);
        }

        public async Task<string> ChatAsync(
            string systemPrompt,
            System.Collections.Generic.IReadOnlyList<(string role, string content)> messages,
            CancellationToken cancellationToken)
        {
            if (messages == null || messages.Count == 0 || !OllamaUrlHelper.IsTrustedLocalUrl(_baseUrl))
                return string.Empty;

            var primaryModel = _model;
            var result = await ChatAsyncWithModel(systemPrompt, messages, primaryModel, cancellationToken)
                .ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(result))
                return result;

            if (!IsModelCrashError(_lastError))
                return string.Empty;

            result = await TryFallbackModelsAsync(
                    async model => await ChatAsyncWithModel(systemPrompt, messages, model, cancellationToken)
                        .ConfigureAwait(false),
                    primaryModel,
                    cancellationToken)
                .ConfigureAwait(false);

            if (string.IsNullOrWhiteSpace(result) && IsModelCrashError(_lastError))
                _lastError = BuildModelCrashHelpMessage(_lastError);

            return result ?? string.Empty;
        }

        private async Task<string> TryFallbackModelsAsync(
            System.Func<string, Task<string>> attempt,
            string primaryModel,
            CancellationToken cancellationToken)
        {
            if (_settings.OllamaUsePortableFolder)
            {
                await OllamaPortableService.RestartServerAsync(_settings, cancellationToken)
                    .ConfigureAwait(false);
            }

            var available = await ListAvailableModelsAsync(cancellationToken).ConfigureAwait(false);
            foreach (var fallback in BuildFallbackModels(available, primaryModel))
            {
                var result = await attempt(fallback).ConfigureAwait(false);
                if (string.IsNullOrWhiteSpace(result))
                    continue;

                PersistFallbackModel(fallback);
                return result;
            }

            return string.Empty;
        }

        private void PersistFallbackModel(string fallback)
        {
            if (string.IsNullOrWhiteSpace(fallback))
                return;

            _model = fallback;
            _settings.OllamaModel = fallback;
            try
            {
                AiSettingsStore.Save(_settings);
            }
            catch
            {
            }
        }

        private async Task<string> ChatAsyncWithModel(
            string systemPrompt,
            System.Collections.Generic.IReadOnlyList<(string role, string content)> messages,
            string model,
            CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(model))
                return string.Empty;

            try
            {
                var payload = new System.Collections.Generic.List<(string role, string content)>();
                if (!string.IsNullOrWhiteSpace(systemPrompt))
                    payload.Add(("system", systemPrompt));
                payload.AddRange(messages);

                var body = SimpleJsonHelper.BuildChatRequest(model, payload);
                using (var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
                {
                    cts.CancelAfter(TimeSpan.FromSeconds(35));
                    var content = new StringContent(body, Encoding.UTF8, "application/json");
                    var response = await SharedClient.PostAsync(_baseUrl + "/api/chat", content, cts.Token)
                        .ConfigureAwait(false);
                    var raw = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                    if (!response.IsSuccessStatusCode)
                    {
                        _lastError = BuildHttpError("Ollama chat", response.StatusCode, raw);
                        if ((int)response.StatusCode == 404)
                            return string.Empty;

                        var fallbackPrompt = BuildPromptFromMessages(systemPrompt, payload);
                        var fallback = await CompleteAsyncWithModel(fallbackPrompt, false, model, cancellationToken)
                            .ConfigureAwait(false);
                        if (!string.IsNullOrWhiteSpace(fallback))
                            return fallback.Trim();

                        return string.Empty;
                    }

                    return SimpleJsonHelper.ExtractResponseField(raw)?.Trim() ?? string.Empty;
                }
            }
            catch (OperationCanceledException)
            {
                _lastError = "Ollama-Antwort-Timeout.";
                return string.Empty;
            }
            catch (Exception ex)
            {
                _lastError = ex.Message;
                return string.Empty;
            }
        }

        private async Task<string> CompleteAsyncWithModel(
            string prompt,
            bool jsonFormat,
            string model,
            CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(prompt) || string.IsNullOrWhiteSpace(model))
                return string.Empty;

            try
            {
                var useJson = jsonFormat && SimpleJsonHelper.ShouldUseStrictJsonFormat(model);
                var body = SimpleJsonHelper.BuildGenerateRequest(model, prompt, useJson);
                using (var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
                {
                    cts.CancelAfter(TimeSpan.FromSeconds(35));
                    var content = new StringContent(body, Encoding.UTF8, "application/json");
                    var response = await SharedClient.PostAsync(_baseUrl + "/api/generate", content, cts.Token)
                        .ConfigureAwait(false);
                    var raw = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                    if (!response.IsSuccessStatusCode)
                    {
                        _lastError = BuildHttpError("Ollama generate", response.StatusCode, raw);
                        return string.Empty;
                    }

                    return SimpleJsonHelper.ExtractResponseField(raw);
                }
            }
            catch (OperationCanceledException)
            {
                _lastError = "Ollama-Antwort-Timeout.";
                return string.Empty;
            }
            catch (Exception ex)
            {
                _lastError = ex.Message;
                return string.Empty;
            }
        }

        private async Task<System.Collections.Generic.List<string>> ListAvailableModelsAsync(CancellationToken cancellationToken)
        {
            try
            {
                using (var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
                {
                    cts.CancelAfter(TimeSpan.FromSeconds(6));
                    var response = await SharedClient.GetAsync(_baseUrl + "/api/tags", cts.Token).ConfigureAwait(false);
                    if (!response.IsSuccessStatusCode)
                        return new System.Collections.Generic.List<string>();

                    var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                    return ExtractListedModelNames(json);
                }
            }
            catch
            {
                return new System.Collections.Generic.List<string>();
            }
        }

        private static System.Collections.Generic.List<string> BuildFallbackModels(
            System.Collections.Generic.IList<string> available,
            string primaryModel)
        {
            var result = new System.Collections.Generic.List<string>();

            // Immer zuerst tinyllama versuchen – auch wenn /api/tags nach Crash leer ist.
            if (!string.Equals(OllamaPortableService.DefaultTextModel, primaryModel, StringComparison.OrdinalIgnoreCase))
                result.Add(OllamaPortableService.DefaultTextModel);

            if (available == null || available.Count == 0)
                return result;

            foreach (var preferred in new[] { "tinyllama", "phi3", "qwen2.5", "qwen", "mistral", "llama3.2", "llama3.1", "llama3" })
            {
                foreach (var name in available)
                {
                    if (string.IsNullOrWhiteSpace(name))
                        continue;
                    if (string.Equals(name, primaryModel, StringComparison.OrdinalIgnoreCase))
                        continue;
                    if (name.IndexOf(preferred, StringComparison.OrdinalIgnoreCase) >= 0
                        && !result.Contains(name))
                        result.Add(name);
                }
            }

            foreach (var name in available)
            {
                if (string.IsNullOrWhiteSpace(name))
                    continue;
                if (string.Equals(name, primaryModel, StringComparison.OrdinalIgnoreCase))
                    continue;
                if (!result.Contains(name))
                    result.Add(name);
            }

            return result;
        }

        private static bool IsModelCrashError(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
                return false;

            return message.IndexOf("0xc0000005", StringComparison.OrdinalIgnoreCase) >= 0
                   || message.IndexOf("terminated", StringComparison.OrdinalIgnoreCase) >= 0
                   || message.IndexOf("HTTP 500", StringComparison.OrdinalIgnoreCase) >= 0
                   || message.IndexOf("wsarecv", StringComparison.OrdinalIgnoreCase) >= 0
                   || message.IndexOf("forcibly closed", StringComparison.OrdinalIgnoreCase) >= 0
                   || message.IndexOf("connection was aborted", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static string BuildModelCrashHelpMessage(string detail)
        {
            return detail
                   + "\r\n\r\nOllama stuerzt beim Modell-Laden ab."
                   + "\r\nAutomatischer Fallback auf tinyllama (CPU) wurde versucht."
                   + "\r\n1. Visual C++ installieren: https://aka.ms/vs/17/release/vc_redist.x64.exe"
                   + "\r\n2. PC neu starten"
                   + "\r\n3. Im KI-Ordner AI-starten.bat ausfuehren"
                   + "\r\n4. In KI-Einstellungen Modell \"tinyllama\" waehlen";
        }

        private static HttpClient CreateClient()
        {
            var handler = new HttpClientHandler
            {
                UseProxy = false
            };
            return new HttpClient(handler)
            {
                Timeout = TimeSpan.FromSeconds(40)
            };
        }

        private static bool IsModelListed(string tagsJson, string modelName)
        {
            if (string.IsNullOrWhiteSpace(tagsJson) || string.IsNullOrWhiteSpace(modelName))
                return false;

            var wanted = modelName.Trim();
            foreach (var name in ExtractListedModelNames(tagsJson))
            {
                if (string.Equals(name, wanted, StringComparison.OrdinalIgnoreCase))
                    return true;

                var wantedBase = wanted.Split(':')[0];
                var nameBase = name.Split(':')[0];
                if (string.Equals(nameBase, wantedBase, StringComparison.OrdinalIgnoreCase)
                    && wanted.IndexOf(':') >= 0)
                    return true;
            }

            return false;
        }

        private static System.Collections.Generic.List<string> ExtractListedModelNames(string tagsJson)
        {
            var names = new System.Collections.Generic.List<string>();
            if (string.IsNullOrWhiteSpace(tagsJson))
                return names;

            var matches = System.Text.RegularExpressions.Regex.Matches(
                tagsJson,
                "\"name\"\\s*:\\s*\"([^\"]+)\"",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            foreach (System.Text.RegularExpressions.Match match in matches)
            {
                var value = match.Groups[1].Value?.Trim();
                if (!string.IsNullOrWhiteSpace(value) && !names.Contains(value))
                    names.Add(value);
            }

            return names;
        }

        private static string PickBestTextModel(System.Collections.Generic.IList<string> available)
        {
            if (available == null || available.Count == 0)
                return string.Empty;

            foreach (var preferred in new[] { "tinyllama", "phi3", "qwen2.5", "qwen", "llama3.2", "llama3.1", "llama3", "mistral" })
            {
                foreach (var name in available)
                {
                    if (name.IndexOf(preferred, StringComparison.OrdinalIgnoreCase) >= 0
                        && name.IndexOf("llava", StringComparison.OrdinalIgnoreCase) < 0
                        && name.IndexOf("moondream", StringComparison.OrdinalIgnoreCase) < 0
                        && name.IndexOf("vision", StringComparison.OrdinalIgnoreCase) < 0)
                        return name;
                }
            }

            foreach (var name in available)
            {
                if (name.IndexOf("llava", StringComparison.OrdinalIgnoreCase) < 0
                    && name.IndexOf("moondream", StringComparison.OrdinalIgnoreCase) < 0
                    && name.IndexOf("vision", StringComparison.OrdinalIgnoreCase) < 0)
                    return name;
            }

            return available[0];
        }

        private static string BuildHttpError(string prefix, System.Net.HttpStatusCode statusCode, string rawBody)
        {
            var message = prefix + ": HTTP " + (int)statusCode;
            var detail = ExtractOllamaError(rawBody);
            if (!string.IsNullOrWhiteSpace(detail))
                message += " – " + detail;
            return message;
        }

        private static string ExtractOllamaError(string rawBody)
        {
            if (string.IsNullOrWhiteSpace(rawBody))
                return string.Empty;

            var errorMatch = System.Text.RegularExpressions.Regex.Match(
                rawBody,
                "\"error\"\\s*:\\s*\"((?:\\\\.|[^\"\\\\])*)\"",
                System.Text.RegularExpressions.RegexOptions.Singleline);
            if (errorMatch.Success)
                return SimpleJsonHelper.Unescape(errorMatch.Groups[1].Value);

            var trimmed = rawBody.Trim();
            return trimmed.Length > 220 ? trimmed.Substring(0, 220) + "…" : trimmed;
        }

        private static string BuildPromptFromMessages(
            string systemPrompt,
            System.Collections.Generic.IReadOnlyList<(string role, string content)> messages)
        {
            var sb = new StringBuilder();
            if (!string.IsNullOrWhiteSpace(systemPrompt))
                sb.AppendLine(systemPrompt.Trim());

            if (messages != null)
            {
                foreach (var message in messages)
                {
                    if (string.IsNullOrWhiteSpace(message.content))
                        continue;

                    if (string.Equals(message.role, "assistant", StringComparison.OrdinalIgnoreCase))
                        sb.AppendLine("Assistent: " + message.content.Trim());
                    else
                        sb.AppendLine("Nutzer: " + message.content.Trim());
                }
            }

            sb.AppendLine("Assistent:");
            return sb.ToString();
        }

        private async Task<bool> ProbeModelInferenceAsync(CancellationToken cancellationToken)
        {
            try
            {
                var body = "{\"model\":\"" + SimpleJsonHelper.Escape(_model)
                           + "\",\"prompt\":\"OK\",\"stream\":false,\"options\":{\"num_predict\":1}}";
                using (var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
                {
                    cts.CancelAfter(TimeSpan.FromSeconds(45));
                    var content = new StringContent(body, Encoding.UTF8, "application/json");
                    var response = await SharedClient.PostAsync(_baseUrl + "/api/generate", content, cts.Token)
                        .ConfigureAwait(false);
                    var raw = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                    if (response.IsSuccessStatusCode)
                        return true;

                    _lastError = BuildInferenceProbeError(response.StatusCode, raw);
                    return false;
                }
            }
            catch (OperationCanceledException)
            {
                _lastError = "Ollama-Test-Timeout – Modell \"" + _model + "\" antwortet nicht rechtzeitig.";
                return false;
            }
            catch (Exception ex)
            {
                _lastError = "Ollama-Test fehlgeschlagen: " + ex.Message;
                return false;
            }
        }

        private static string BuildInferenceProbeError(System.Net.HttpStatusCode statusCode, string rawBody)
        {
            var detail = ExtractOllamaError(rawBody);
            if ((int)statusCode == 404)
                return "Modell nicht gefunden (HTTP 404). Bitte: ollama pull <modell>";

            if (!string.IsNullOrWhiteSpace(detail)
                && (detail.IndexOf("terminated", StringComparison.OrdinalIgnoreCase) >= 0
                    || detail.IndexOf("0xc0000005", StringComparison.OrdinalIgnoreCase) >= 0
                    || detail.IndexOf("memory", StringComparison.OrdinalIgnoreCase) >= 0))
            {
                return "Ollama-Modell stürzt beim Laden ab: " + detail
                       + "\r\n\r\nMögliche Ursachen: veraltete ollama.exe im KI-Ordner, GPU-Treiber, oder Modell zu groß."
                       + "\r\nVersuch: ollama.exe im KI-Ordner aktualisieren oder kleineres Modell wählen (z. B. phi3:mini).";
            }

            return BuildHttpError("Ollama-Test", statusCode, rawBody);
        }
    }
}
