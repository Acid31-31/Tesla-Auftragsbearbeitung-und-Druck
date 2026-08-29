using System;
using PDF_umbenenen.Platform.Models;
using PDF_umbenenen.Services.Ai;

namespace PDF_umbenenen.Platform.Core
{
    internal sealed class ConfigurationManager : Contracts.IConfigurationManager
    {
        private readonly System.Collections.Concurrent.ConcurrentDictionary<string, object> _settings =
            new System.Collections.Concurrent.ConcurrentDictionary<string, object>(StringComparer.OrdinalIgnoreCase);

        public ConfigurationManager()
        {
            LoadFromAiSettings();
        }

        public T GetSetting<T>(string key, T defaultValue = default)
        {
            if (string.IsNullOrWhiteSpace(key) || !_settings.TryGetValue(key, out var value))
                return defaultValue;

            if (value is T typed)
                return typed;

            try
            {
                return (T)Convert.ChangeType(value, typeof(T));
            }
            catch
            {
                return defaultValue;
            }
        }

        public void SetSetting<T>(string key, T value)
        {
            if (string.IsNullOrWhiteSpace(key))
                return;

            _settings[key] = value;
        }

        public void Save()
        {
            var settings = AiSettingsStore.Load();
            settings.AiEnabled = GetSetting("ai.enabled", settings.AiEnabled);
            settings.LocalOcrEnabled = GetSetting("ocr.enabled", settings.LocalOcrEnabled);
            settings.TesseractPath = GetSetting("ocr.tesseractPath", settings.TesseractPath);
            settings.OcrLanguages = GetSetting("ocr.languages", settings.OcrLanguages);
            settings.LocalVisionEnabled = GetSetting("vision.local", settings.LocalVisionEnabled);
            AiSettingsStore.Save(settings);
        }

        private void LoadFromAiSettings()
        {
            var settings = AiSettingsStore.Load();
            SetSetting("ai.enabled", settings.AiEnabled);
            SetSetting("ocr.enabled", settings.LocalOcrEnabled);
            SetSetting("ocr.tesseractPath", settings.TesseractPath);
            SetSetting("ocr.languages", settings.OcrLanguages);
            SetSetting("vision.local", settings.LocalVisionEnabled);
            SetSetting("database.provider", "sqlite");
            SetSetting("api.port", 8765);
            SetSetting("api.enabled", true);
        }
    }
}
