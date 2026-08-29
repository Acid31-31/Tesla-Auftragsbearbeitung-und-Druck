using PDF_umbenenen.Platform.Contracts;
using PDF_umbenenen.Services.Ai;

namespace PDF_umbenenen.Platform.Bridge
{
    internal sealed class PlatformAiFacade : IPlatformFacade
    {
        private readonly IConfigurationManager _configuration;

        public PlatformAiFacade(IAiOrchestrator orchestrator, IConfigurationManager configuration)
        {
            Orchestrator = orchestrator;
            _configuration = configuration;
            LegacyAi = new OfflineAiOrchestrator();
        }

        public IAiOrchestrator Orchestrator { get; }

        public OfflineAiOrchestrator LegacyAi { get; }

        public void SyncSettings()
        {
            var settings = LegacyAi.Settings;
            _configuration.SetSetting("ai.enabled", settings.AiEnabled);
            _configuration.SetSetting("ocr.enabled", settings.LocalOcrEnabled);
            _configuration.SetSetting("ocr.tesseractPath", settings.TesseractPath);
            _configuration.SetSetting("ocr.languages", settings.OcrLanguages);
            _configuration.SetSetting("vision.local", settings.LocalVisionEnabled);
        }
    }
}
