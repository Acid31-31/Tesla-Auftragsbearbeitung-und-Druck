using System;
using System.Threading;
using System.Threading.Tasks;

namespace PDF_umbenenen.Services.Ai
{
    internal enum AiBackendMode
    {
        Disabled,
        RulesOnly
    }

    /// <summary>
    /// Offline-KI ohne Ollama: ausschließlich lokale Regeln und Plattform-Module.
    /// </summary>
    internal sealed class OfflineAiOrchestrator : IDisposable
    {
        private readonly AiSettings _settings = new AiSettings();
        private readonly RuleBasedFallback _fallback = new RuleBasedFallback();
        private AiBackendMode _mode = AiBackendMode.RulesOnly;

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
            _settings.LocalOcrEnabled = loaded.LocalOcrEnabled;
            _settings.TesseractPath = loaded.TesseractPath;
            _settings.OcrLanguages = loaded.OcrLanguages;
            _settings.LocalVisionEnabled = loaded.LocalVisionEnabled;
            _mode = _settings.AiEnabled ? AiBackendMode.RulesOnly : AiBackendMode.Disabled;
        }

        public void SaveSettings()
        {
            AiSettingsStore.Save(_settings);
            ApplyCurrentSettings();
        }

        public void ApplyCurrentSettings()
        {
            _mode = _settings.AiEnabled ? AiBackendMode.RulesOnly : AiBackendMode.Disabled;
        }

        public Task RefreshAvailabilityAsync(CancellationToken cancellationToken)
        {
            _mode = _settings.AiEnabled ? AiBackendMode.RulesOnly : AiBackendMode.Disabled;
            return Task.CompletedTask;
        }

        public Task EnsureAvailabilityAsync(CancellationToken cancellationToken)
        {
            if (!_settings.LocalOcrEnabled)
                return Task.CompletedTask;

            return TesseractBootstrapService.EnsureAvailableAsync(cancellationToken);
        }

        public string GetStatusText()
        {
            if (!_settings.AiEnabled)
                return "KI deaktiviert";

            var ocr = _settings.LocalOcrEnabled
                ? (Cli.TesseractCliService.IsAvailable() ? "OCR: Tesseract" : "OCR: Tesseract fehlt")
                : "OCR: aus";

            return "KI-Vision aktiv (Wasserzeichen-Platzierung) · " + ocr;
        }

        public Task<string> CompleteAsync(string prompt, bool jsonFormat, CancellationToken cancellationToken)
            => Task.FromResult(string.Empty);

        public Task<string> ChatAsync(string systemPrompt, string userPrompt, CancellationToken cancellationToken)
            => _fallback.ChatAsync(systemPrompt, userPrompt, cancellationToken);

        public void Dispose()
        {
        }
    }
}
