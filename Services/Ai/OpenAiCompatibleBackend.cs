using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace PDF_umbenenen.Services.Ai
{
    /// <summary>
    /// OpenAI-kompatible lokale API (LM Studio, llama.cpp server, LocalAI).
    /// </summary>
    internal sealed class OpenAiCompatibleBackend : IAiBackend
    {
        private static readonly HttpClient SharedClient = CreateClient();

        private string _baseUrl = LocalLlmUrlHelper.DefaultBaseUrl;
        private string _model = string.Empty;
        private string _apiKey = string.Empty;
        private string _lastError = string.Empty;
        private string _resolvedModel = string.Empty;

        public string Name => "LM Studio";

        public string LastError => _lastError ?? string.Empty;

        public string ResolvedModel => _resolvedModel ?? string.Empty;

        public void Configure(AiSettings settings)
        {
            _baseUrl = LocalLlmUrlHelper.NormalizeBaseUrl(settings?.LocalLlmBaseUrl);
            _model = settings?.LocalLlmModel?.Trim() ?? string.Empty;
            _apiKey = settings?.LocalLlmApiKey?.Trim() ?? string.Empty;
            _resolvedModel = string.IsNullOrWhiteSpace(_model) ? string.Empty : _model;
        }

        public async Task<bool> CheckAvailabilityAsync(CancellationToken cancellationToken)
        {
            _lastError = string.Empty;
            if (!LocalLlmUrlHelper.IsTrustedLocalUrl(_baseUrl))
            {
                _lastError = "Nur localhost-URLs erlaubt (127.0.0.1 / localhost).";
                return false;
            }

            try
            {
                using (var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
                {
                    cts.CancelAfter(TimeSpan.FromSeconds(6));
                    var request = CreateRequest(HttpMethod.Get, _baseUrl + "/models");
                    var response = await SharedClient.SendAsync(request, cts.Token).ConfigureAwait(false);
                    var raw = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                    if (!response.IsSuccessStatusCode)
                    {
                        _lastError = BuildHttpError("Server", response.StatusCode, raw);
                        return false;
                    }

                    var models = ExtractModelIds(raw);
                    if (models.Count == 0)
                    {
                        _lastError = "Server erreichbar, aber kein Modell geladen. In LM Studio ein Modell laden und Server starten.";
                        return false;
                    }

                    _resolvedModel = ResolveModelName(models);
                    if (string.IsNullOrWhiteSpace(_resolvedModel))
                    {
                        _lastError = string.IsNullOrWhiteSpace(_model)
                            ? "Kein Vision-Modell geladen. In LM Studio z. B. Qwen2.5-VL laden und Server starten. Textmodelle wie tinyllama können Zeichnungen nicht lesen."
                            : "Modell \"" + _model + "\" nicht gefunden. Verfügbar: "
                              + string.Join(", ", models);
                        return false;
                    }

                    return true;
                }
            }
            catch (OperationCanceledException)
            {
                _lastError = "Timeout – läuft LM Studio / lokaler Server auf " + _baseUrl + "?";
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
            if (string.IsNullOrWhiteSpace(prompt))
                return string.Empty;

            var messages = new List<(string role, string content)>
            {
                ("user", prompt)
            };
            return await ChatAsync(string.Empty, messages, jsonFormat, cancellationToken).ConfigureAwait(false);
        }

        public Task<string> ChatAsync(string systemPrompt, string userPrompt, CancellationToken cancellationToken)
        {
            var messages = new List<(string role, string content)>
            {
                ("user", userPrompt)
            };
            return ChatAsync(systemPrompt, messages, false, cancellationToken);
        }

        public async Task<string> ChatAsync(
            string systemPrompt,
            IReadOnlyList<(string role, string content)> messages,
            CancellationToken cancellationToken)
        {
            return await ChatAsync(systemPrompt, messages, false, cancellationToken).ConfigureAwait(false);
        }

        private async Task<string> ChatAsync(
            string systemPrompt,
            IReadOnlyList<(string role, string content)> messages,
            bool jsonFormat,
            CancellationToken cancellationToken)
        {
            if (messages == null || messages.Count == 0 || !LocalLlmUrlHelper.IsTrustedLocalUrl(_baseUrl))
                return string.Empty;

            if (string.IsNullOrWhiteSpace(_resolvedModel))
            {
                if (!await CheckAvailabilityAsync(cancellationToken).ConfigureAwait(false))
                    return string.Empty;
            }

            try
            {
                var body = BuildChatRequest(_resolvedModel, systemPrompt, messages, jsonFormat);
                using (var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
                {
                    cts.CancelAfter(TimeSpan.FromSeconds(120));
                    var content = new StringContent(body, Encoding.UTF8, "application/json");
                    var request = CreateRequest(HttpMethod.Post, _baseUrl + "/chat/completions");
                    request.Content = content;
                    var response = await SharedClient.SendAsync(request, cts.Token).ConfigureAwait(false);
                    var raw = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                    if (!response.IsSuccessStatusCode)
                    {
                        _lastError = BuildHttpError("Chat", response.StatusCode, raw);
                        return string.Empty;
                    }

                    var text = ExtractChatContent(raw);
                    return string.IsNullOrWhiteSpace(text) ? string.Empty : text.Trim();
                }
            }
            catch (OperationCanceledException)
            {
                _lastError = "Antwort-Timeout – Modell antwortet zu langsam.";
                return string.Empty;
            }
            catch (Exception ex)
            {
                _lastError = ex.Message;
                return string.Empty;
            }
        }

        private HttpRequestMessage CreateRequest(HttpMethod method, string url)
        {
            var request = new HttpRequestMessage(method, url);
            if (!string.IsNullOrWhiteSpace(_apiKey))
                request.Headers.TryAddWithoutValidation("Authorization", "Bearer " + _apiKey);
            return request;
        }

        private string ResolveModelName(IReadOnlyList<string> available)
        {
            if (available == null || available.Count == 0)
                return string.Empty;

            if (!string.IsNullOrWhiteSpace(_model))
            {
                foreach (var name in available)
                {
                    if (string.Equals(name, _model, StringComparison.OrdinalIgnoreCase))
                        return name;

                    if (name.StartsWith(_model, StringComparison.OrdinalIgnoreCase))
                        return name;
                }

                return string.Empty;
            }

            foreach (var name in available)
            {
                if (LooksLikeVisionModel(name))
                    return name;
            }

            return string.Empty;
        }

        public async Task<string> ChatWithImageAsync(
            string systemPrompt,
            string userPrompt,
            string imageDataUrl,
            bool jsonFormat,
            CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(userPrompt)
                || string.IsNullOrWhiteSpace(imageDataUrl)
                || !LocalLlmUrlHelper.IsTrustedLocalUrl(_baseUrl))
                return string.Empty;

            if (string.IsNullOrWhiteSpace(_resolvedModel))
            {
                if (!await CheckAvailabilityAsync(cancellationToken).ConfigureAwait(false))
                    return string.Empty;
            }

            var result = await SendVisionAsync(
                systemPrompt,
                userPrompt,
                imageDataUrl,
                jsonFormat,
                cancellationToken).ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(result) || !jsonFormat)
                return result;

            return await SendVisionAsync(
                systemPrompt,
                userPrompt,
                imageDataUrl,
                false,
                cancellationToken).ConfigureAwait(false);
        }

        private async Task<string> SendVisionAsync(
            string systemPrompt,
            string userPrompt,
            string imageDataUrl,
            bool jsonFormat,
            CancellationToken cancellationToken)
        {
            try
            {
                var body = BuildVisionChatRequest(_resolvedModel, systemPrompt, userPrompt, imageDataUrl, jsonFormat);
                using (var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
                {
                    cts.CancelAfter(TimeSpan.FromSeconds(90));
                    var content = new StringContent(body, Encoding.UTF8, "application/json");
                    var request = CreateRequest(HttpMethod.Post, _baseUrl + "/chat/completions");
                    request.Content = content;
                    var response = await SharedClient.SendAsync(request, cts.Token).ConfigureAwait(false);
                    var raw = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                    if (!response.IsSuccessStatusCode)
                    {
                        _lastError = BuildHttpError("Vision", response.StatusCode, raw);
                        return string.Empty;
                    }

                    var text = ExtractChatContent(raw);
                    return string.IsNullOrWhiteSpace(text) ? string.Empty : text.Trim();
                }
            }
            catch (OperationCanceledException)
            {
                _lastError = "Vision-Timeout – Modell antwortet zu langsam.";
                return string.Empty;
            }
            catch (Exception ex)
            {
                _lastError = ex.Message;
                return string.Empty;
            }
        }

        internal static bool LooksLikeVisionModel(string modelName)
        {
            if (string.IsNullOrWhiteSpace(modelName))
                return false;

            var name = modelName.Trim().ToLowerInvariant();
            if (name.IndexOf("tinyllama", StringComparison.Ordinal) >= 0
                || name.IndexOf("revision", StringComparison.Ordinal) >= 0)
                return false;

            return name.IndexOf("llava", StringComparison.Ordinal) >= 0
                   || name.IndexOf("bakllava", StringComparison.Ordinal) >= 0
                   || name.IndexOf("moondream", StringComparison.Ordinal) >= 0
                   || name.IndexOf("minicpm", StringComparison.Ordinal) >= 0
                   || name.IndexOf("qwen2-vl", StringComparison.Ordinal) >= 0
                   || name.IndexOf("qwen2.5-vl", StringComparison.Ordinal) >= 0
                   || name.IndexOf("qwen2.5vl", StringComparison.Ordinal) >= 0
                   || name.IndexOf("qwen-vl", StringComparison.Ordinal) >= 0
                   || name.IndexOf("internvl", StringComparison.Ordinal) >= 0
                   || name.IndexOf("pixtral", StringComparison.Ordinal) >= 0
                   || name.IndexOf("llama3.2-vision", StringComparison.Ordinal) >= 0
                   || name.IndexOf("llama-3.2-vision", StringComparison.Ordinal) >= 0
                   || name.IndexOf("phi-3-vision", StringComparison.Ordinal) >= 0
                   || name.IndexOf("phi-4-multimodal", StringComparison.Ordinal) >= 0
                   || name.Contains("-vl")
                   || name.Contains("_vl")
                   || name.Contains("-vision")
                   || name.Contains("_vision")
                   || name.EndsWith("vl", StringComparison.Ordinal);
        }

        private static string BuildVisionChatRequest(
            string model,
            string systemPrompt,
            string userPrompt,
            string imageDataUrl,
            bool jsonFormat)
        {
            var sb = new StringBuilder();
            sb.Append("{\"model\":\"").Append(SimpleJsonHelper.Escape(model)).Append("\"");
            sb.Append(",\"stream\":false,\"temperature\":0,\"max_tokens\":400");
            if (jsonFormat)
                sb.Append(",\"response_format\":{\"type\":\"json_object\"}");
            sb.Append(",\"messages\":[");
            if (!string.IsNullOrWhiteSpace(systemPrompt))
            {
                sb.Append("{\"role\":\"system\",\"content\":\"");
                sb.Append(SimpleJsonHelper.Escape(systemPrompt));
                sb.Append("\"},");
            }

            sb.Append("{\"role\":\"user\",\"content\":[");
            sb.Append("{\"type\":\"text\",\"text\":\"");
            sb.Append(SimpleJsonHelper.Escape(userPrompt));
            sb.Append("\"},");
            sb.Append("{\"type\":\"image_url\",\"image_url\":{\"url\":\"");
            sb.Append(imageDataUrl ?? string.Empty);
            sb.Append("\"}}]}]}");
            return sb.ToString();
        }

        private static List<string> ExtractModelIds(string json)
        {
            var names = new List<string>();
            if (string.IsNullOrWhiteSpace(json))
                return names;

            var matches = System.Text.RegularExpressions.Regex.Matches(
                json,
                "\"id\"\\s*:\\s*\"([^\"]+)\"",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            foreach (System.Text.RegularExpressions.Match match in matches)
            {
                var value = match.Groups[1].Value?.Trim();
                if (!string.IsNullOrWhiteSpace(value) && !names.Contains(value))
                    names.Add(value);
            }

            return names;
        }

        private static string BuildChatRequest(
            string model,
            string systemPrompt,
            IReadOnlyList<(string role, string content)> messages,
            bool jsonFormat)
        {
            var sb = new StringBuilder();
            sb.Append("{\"model\":\"").Append(SimpleJsonHelper.Escape(model)).Append("\",\"stream\":false");
            if (jsonFormat)
                sb.Append(",\"response_format\":{\"type\":\"json_object\"}");
            sb.Append(",\"messages\":[");
            var first = true;
            if (!string.IsNullOrWhiteSpace(systemPrompt))
            {
                sb.Append("{\"role\":\"system\",\"content\":\"").Append(SimpleJsonHelper.Escape(systemPrompt)).Append("\"}");
                first = false;
            }

            foreach (var message in messages)
            {
                if (string.IsNullOrWhiteSpace(message.content))
                    continue;

                if (!first)
                    sb.Append(',');
                sb.Append("{\"role\":\"").Append(SimpleJsonHelper.Escape(message.role)).Append("\",");
                sb.Append("\"content\":\"").Append(SimpleJsonHelper.Escape(message.content)).Append("\"}");
                first = false;
            }

            sb.Append("]}");
            return sb.ToString();
        }

        private static string ExtractChatContent(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
                return string.Empty;

            var match = System.Text.RegularExpressions.Regex.Match(
                json,
                "\"message\"\\s*:\\s*\\{[\\s\\S]*?\"content\"\\s*:\\s*\"((?:\\\\.|[^\"\\\\])*)\"",
                System.Text.RegularExpressions.RegexOptions.Singleline);
            if (match.Success)
                return SimpleJsonHelper.Unescape(match.Groups[1].Value);

            return SimpleJsonHelper.ExtractResponseField(json);
        }

        private static string BuildHttpError(string prefix, System.Net.HttpStatusCode statusCode, string rawBody)
        {
            var message = prefix + ": HTTP " + (int)statusCode;
            var detail = ExtractErrorMessage(rawBody);
            if (!string.IsNullOrWhiteSpace(detail))
                message += " – " + detail;
            return message;
        }

        private static string ExtractErrorMessage(string rawBody)
        {
            if (string.IsNullOrWhiteSpace(rawBody))
                return string.Empty;

            var errorMatch = System.Text.RegularExpressions.Regex.Match(
                rawBody,
                "\"message\"\\s*:\\s*\"((?:\\\\.|[^\"\\\\])*)\"",
                System.Text.RegularExpressions.RegexOptions.Singleline);
            if (errorMatch.Success)
                return SimpleJsonHelper.Unescape(errorMatch.Groups[1].Value);

            var trimmed = rawBody.Trim();
            return trimmed.Length > 220 ? trimmed.Substring(0, 220) + "…" : trimmed;
        }

        private static HttpClient CreateClient()
        {
            var handler = new HttpClientHandler { UseProxy = false };
            return new HttpClient(handler) { Timeout = TimeSpan.FromMinutes(3) };
        }
    }
}
