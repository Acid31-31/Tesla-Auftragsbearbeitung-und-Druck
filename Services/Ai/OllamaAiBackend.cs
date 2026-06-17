using System;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace PDF_umbenenen.Services.Ai
{
    internal sealed class OllamaAiBackend : IAiBackend, IDisposable
    {
        private readonly HttpClient _httpClient;
        private readonly AiSettings _settings;

        public OllamaAiBackend(AiSettings settings)
        {
            _settings = settings ?? new AiSettings();
            _httpClient = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(120)
            };
        }

        public string Name => "Ollama";

        public async Task<bool> CheckAvailabilityAsync(CancellationToken cancellationToken)
        {
            try
            {
                var url = NormalizeBaseUrl(_settings.OllamaBaseUrl) + "/api/tags";
                using (var response = await _httpClient.GetAsync(url, cancellationToken).ConfigureAwait(false))
                {
                    return response.IsSuccessStatusCode;
                }
            }
            catch
            {
                return false;
            }
        }

        public async Task<string> CompleteAsync(string prompt, bool jsonFormat, CancellationToken cancellationToken)
        {
            var url = NormalizeBaseUrl(_settings.OllamaBaseUrl) + "/api/generate";
            var body = SimpleJsonHelper.BuildGenerateRequest(_settings.ModelName, prompt, jsonFormat);
            using (var content = new StringContent(body, Encoding.UTF8, "application/json"))
            using (var response = await _httpClient.PostAsync(url, content, cancellationToken).ConfigureAwait(false))
            {
                response.EnsureSuccessStatusCode();
                var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                return SimpleJsonHelper.ExtractResponseField(json);
            }
        }

        public async Task<string> ChatAsync(string systemPrompt, string userPrompt, CancellationToken cancellationToken)
        {
            var url = NormalizeBaseUrl(_settings.OllamaBaseUrl) + "/api/chat";
            var messages = new System.Collections.Generic.List<(string role, string content)>();
            if (!string.IsNullOrWhiteSpace(systemPrompt))
                messages.Add(("system", systemPrompt));
            messages.Add(("user", userPrompt));

            var body = SimpleJsonHelper.BuildChatRequest(_settings.ModelName, messages);
            using (var content = new StringContent(body, Encoding.UTF8, "application/json"))
            using (var response = await _httpClient.PostAsync(url, content, cancellationToken).ConfigureAwait(false))
            {
                response.EnsureSuccessStatusCode();
                var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                return SimpleJsonHelper.ExtractResponseField(json);
            }
        }

        private static string NormalizeBaseUrl(string baseUrl)
        {
            if (string.IsNullOrWhiteSpace(baseUrl))
                return "http://localhost:11434";

            return baseUrl.Trim().TrimEnd('/');
        }

        public void Dispose()
        {
            _httpClient?.Dispose();
        }
    }
}
