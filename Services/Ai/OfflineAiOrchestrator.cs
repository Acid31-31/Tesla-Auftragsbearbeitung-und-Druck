using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using PDF_umbenenen.Services;

namespace PDF_umbenenen.Services.Ai
{
    internal enum AiBackendMode
    {
        Disabled,
        RulesOnly,
        Ollama
    }

    /// <summary>
    /// Offline-KI: Regeln + optionales portable Ollama aus externem Ordner AI_DOK-V01.
    /// </summary>
    internal sealed class OfflineAiOrchestrator : IDisposable
    {
        private readonly AiSettings _settings = new AiSettings();
        private readonly RuleBasedFallback _fallback = new RuleBasedFallback();
        private readonly OllamaAiBackend _ollama = new OllamaAiBackend();
        private AiBackendMode _mode = AiBackendMode.RulesOnly;
        private string _ollamaLastError = string.Empty;

        public OfflineAiOrchestrator()
        {
            ReloadSettings();
        }

        public AiSettings Settings => _settings;

        public AiBackendMode Mode => _mode;

        public RuleBasedFallback Fallback => _fallback;

        public string OllamaLastError =>
            !string.IsNullOrWhiteSpace(_ollamaLastError)
                ? _ollamaLastError
                : (_ollama.LastError ?? string.Empty);

        public void ReloadSettings()
        {
            var loaded = AiSettingsStore.Load();
            _settings.AiEnabled = loaded.AiEnabled;
            _settings.LocalOcrEnabled = loaded.LocalOcrEnabled;
            _settings.TesseractPath = loaded.TesseractPath;
            _settings.OcrLanguages = loaded.OcrLanguages;
            _settings.LocalVisionEnabled = loaded.LocalVisionEnabled;
            _settings.DrawingVisionEnabled = loaded.DrawingVisionEnabled;
            _settings.LocalLlmBaseUrl = loaded.LocalLlmBaseUrl;
            _settings.LocalLlmModel = loaded.LocalLlmModel;
            _settings.LocalLlmApiKey = loaded.LocalLlmApiKey;
            _settings.OllamaEnabled = loaded.OllamaEnabled;
            _settings.OllamaBaseUrl = loaded.OllamaBaseUrl;
            _settings.OllamaModel = loaded.OllamaModel;
            _settings.OllamaUsePortableFolder = loaded.OllamaUsePortableFolder;
            _settings.OllamaPortableDirectory = loaded.OllamaPortableDirectory;
            _ollama.Configure(_settings);
            ApplyCurrentSettings();
        }

        public void SaveSettings()
        {
            AiSettingsStore.Save(_settings);
            _ollama.Configure(_settings);
            ApplyCurrentSettings();
        }

        public void ApplyCurrentSettings()
        {
            if (!_settings.AiEnabled)
            {
                _mode = AiBackendMode.Disabled;
                return;
            }

            _mode = _settings.OllamaEnabled ? AiBackendMode.Ollama : AiBackendMode.RulesOnly;
        }

        public async Task RefreshAvailabilityAsync(CancellationToken cancellationToken)
        {
            ApplyCurrentSettings();
            _ollamaLastError = string.Empty;

            if (!_settings.AiEnabled || !_settings.OllamaEnabled)
            {
                if (_settings.AiEnabled)
                    _mode = AiBackendMode.RulesOnly;
                return;
            }

            _ollama.Configure(_settings);
            var ok = await _ollama.CheckAvailabilityAsync(cancellationToken).ConfigureAwait(false);
            if (ok)
            {
                _mode = AiBackendMode.Ollama;
                return;
            }

            _ollamaLastError = _ollama.LastError;
            _mode = AiBackendMode.RulesOnly;
        }

        public async Task EnsureAvailabilityAsync(CancellationToken cancellationToken)
        {
            if (_settings.LocalOcrEnabled)
                await TesseractBootstrapService.EnsureAvailableAsync(cancellationToken).ConfigureAwait(false);

            await RefreshAvailabilityAsync(cancellationToken).ConfigureAwait(false);
        }

        public bool CanUseOllama()
        {
            return _settings.AiEnabled
                   && _settings.OllamaEnabled
                   && _mode == AiBackendMode.Ollama;
        }

        public string GetStatusText()
        {
            if (!_settings.AiEnabled)
                return "KI deaktiviert";

            var ocr = _settings.LocalOcrEnabled
                ? (Cli.TesseractCliService.IsAvailable() ? "OCR: Tesseract" : "OCR: Tesseract fehlt")
                : "OCR: aus";

            var kiDir = OllamaPortableService.ResolveKiDirectory(_settings);
            if (_settings.OllamaEnabled && _mode == AiBackendMode.Ollama)
            {
                return "Ollama aktiv (" + (_settings.OllamaModel ?? "?") + ") · "
                       + ocr + " · KI-Ordner: " + kiDir;
            }

            if (_settings.OllamaEnabled)
            {
                var err = string.IsNullOrWhiteSpace(OllamaLastError) ? "nicht erreichbar" : OllamaLastError;
                return "Ollama aus/fehlt (" + err + ") · Regeln aktiv · " + ocr;
            }

            return "KI-Vision/Regeln aktiv · " + ocr
                   + (_settings.DrawingVisionEnabled ? " · Zeichnungs-Vision: an" : string.Empty);
        }

        public async Task<string> CompleteAsync(string prompt, bool jsonFormat, CancellationToken cancellationToken)
        {
            if (!CanUseOllama() && _settings.OllamaEnabled)
                await RefreshAvailabilityAsync(cancellationToken).ConfigureAwait(false);

            if (!CanUseOllama())
                return string.Empty;

            var result = await _ollama.CompleteAsync(prompt, jsonFormat, cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(result))
                _ollamaLastError = _ollama.LastError;
            return result ?? string.Empty;
        }

        public async Task<string> ChatAsync(string systemPrompt, string userPrompt, CancellationToken cancellationToken)
        {
            if (!CanUseOllama() && _settings.OllamaEnabled)
                await RefreshAvailabilityAsync(cancellationToken).ConfigureAwait(false);

            if (CanUseOllama())
            {
                var ollama = await _ollama.ChatAsync(systemPrompt, userPrompt, cancellationToken).ConfigureAwait(false);
                if (!string.IsNullOrWhiteSpace(ollama))
                    return ollama;
                _ollamaLastError = _ollama.LastError;
            }

            return await _fallback.ChatAsync(systemPrompt, userPrompt, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>Nur Ollama – kein Regel-Fallback (für Kunden-Assistent).</summary>
        public async Task<string> ChatOllamaOnlyAsync(
            string systemPrompt,
            string userPrompt,
            IReadOnlyList<(string role, string content)> priorMessages,
            CancellationToken cancellationToken)
        {
            if (!_settings.AiEnabled || !_settings.OllamaEnabled)
                return string.Empty;

            await RefreshAvailabilityAsync(cancellationToken).ConfigureAwait(false);
            if (!CanUseOllama())
                return string.Empty;

            var messages = new List<(string role, string content)>();
            if (priorMessages != null)
                messages.AddRange(priorMessages);
            messages.Add(("user", userPrompt ?? string.Empty));

            var result = await _ollama.ChatAsync(systemPrompt, messages, cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(result))
                _ollamaLastError = _ollama.LastError;
            return result ?? string.Empty;
        }

        public void Dispose()
        {
        }
    }
}
