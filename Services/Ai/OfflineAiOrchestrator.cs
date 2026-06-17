using System;
using System.Threading;
using System.Threading.Tasks;

namespace PDF_umbenenen.Services.Ai
{
    internal enum AiBackendMode
    {
        Disabled,
        Ollama,
        RulesOnly
    }

    internal sealed class OfflineAiOrchestrator : IDisposable
    {
        private readonly AiSettings _settings = new AiSettings();
        private OllamaAiBackend _ollama;
        private readonly RuleBasedFallback _fallback = new RuleBasedFallback();
        private AiBackendMode _mode = AiBackendMode.RulesOnly;
        private DateTime _lastAvailabilityCheck = DateTime.MinValue;
        private bool _ollamaAvailable;

        public OfflineAiOrchestrator()
        {
            ReloadSettings();
        }

        public AiSettings Settings => _settings;

        public AiBackendMode Mode => _mode;

        public RuleBasedFallback Fallback => _fallback;

        public void ReloadSettings()
        {
            var loaded = AiSettingsStore.Load();
            _settings.AiEnabled = loaded.AiEnabled;
            _settings.OllamaBaseUrl = loaded.OllamaBaseUrl;
            _settings.ModelName = loaded.ModelName;

            _ollama?.Dispose();
            _ollama = new OllamaAiBackend(_settings);
            _mode = _settings.AiEnabled ? AiBackendMode.RulesOnly : AiBackendMode.Disabled;
            _lastAvailabilityCheck = DateTime.MinValue;
        }

        public void SaveSettings()
        {
            AiSettingsStore.Save(_settings);
            ApplyCurrentSettings();
        }

        public void ApplyCurrentSettings()
        {
            _ollama?.Dispose();
            _ollama = new OllamaAiBackend(_settings);
            _mode = _settings.AiEnabled ? AiBackendMode.RulesOnly : AiBackendMode.Disabled;
            _lastAvailabilityCheck = DateTime.MinValue;
        }

        public async Task RefreshAvailabilityAsync(CancellationToken cancellationToken)
        {
            if (!_settings.AiEnabled)
            {
                _mode = AiBackendMode.Disabled;
                _ollamaAvailable = false;
                return;
            }

            _ollamaAvailable = await _ollama.CheckAvailabilityAsync(cancellationToken).ConfigureAwait(false);
            _mode = _ollamaAvailable ? AiBackendMode.Ollama : AiBackendMode.RulesOnly;
            _lastAvailabilityCheck = DateTime.UtcNow;
        }

        public async Task EnsureAvailabilityAsync(CancellationToken cancellationToken)
        {
            if ((DateTime.UtcNow - _lastAvailabilityCheck).TotalSeconds > 30)
                await RefreshAvailabilityAsync(cancellationToken).ConfigureAwait(false);
        }

        public string GetStatusText()
        {
            if (!_settings.AiEnabled)
                return "KI deaktiviert";

            return _mode == AiBackendMode.Ollama
                ? "Ollama verbunden"
                : "Offline-Modus (Regeln)";
        }

        public async Task<string> CompleteAsync(string prompt, bool jsonFormat, CancellationToken cancellationToken)
        {
            await EnsureAvailabilityAsync(cancellationToken).ConfigureAwait(false);
            if (_mode == AiBackendMode.Ollama)
            {
                try
                {
                    return await _ollama.CompleteAsync(prompt, jsonFormat, cancellationToken).ConfigureAwait(false);
                }
                catch
                {
                    _mode = AiBackendMode.RulesOnly;
                }
            }

            return string.Empty;
        }

        public async Task<string> ChatAsync(string systemPrompt, string userPrompt, CancellationToken cancellationToken)
        {
            await EnsureAvailabilityAsync(cancellationToken).ConfigureAwait(false);
            if (_mode == AiBackendMode.Ollama)
            {
                try
                {
                    return await _ollama.ChatAsync(systemPrompt, userPrompt, cancellationToken).ConfigureAwait(false);
                }
                catch
                {
                    _mode = AiBackendMode.RulesOnly;
                }
            }

            return await _fallback.ChatAsync(systemPrompt, userPrompt, cancellationToken).ConfigureAwait(false);
        }

        public void Dispose()
        {
            _ollama?.Dispose();
        }
    }
}
