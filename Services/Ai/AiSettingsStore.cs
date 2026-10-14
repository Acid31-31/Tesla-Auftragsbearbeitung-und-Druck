using System;
using System.IO;
using System.Xml.Linq;
using PDF_umbenenen;

namespace PDF_umbenenen.Services.Ai
{
    public sealed class AiSettings
    {
        /// <summary>KI-Chat und Regel-Assistent aktiv.</summary>
        public bool AiEnabled { get; set; } = true;

        /// <summary>Lokales OCR (Tesseract CLI) für Schriftfelder ohne Textlayer.</summary>
        public bool LocalOcrEnabled { get; set; } = true;

        /// <summary>Optionale Pfadangabe zu tesseract.exe.</summary>
        public string TesseractPath { get; set; } = string.Empty;

        /// <summary>Tesseract-Sprachen, z. B. deu+eng.</summary>
        public string OcrLanguages { get; set; } = "deu+eng";

        /// <summary>Lokale Layout-/Zeichnungsanalyse (PDF-Text + OCR + Regeln).</summary>
        public bool LocalVisionEnabled { get; set; } = true;

        /// <summary>Lokales Bildmodell (LM Studio / OpenAI-kompatibel) liest Blechdicke und RAL von der Zeichnung.</summary>
        public bool DrawingVisionEnabled { get; set; } = true;

        public string LocalLlmBaseUrl { get; set; } = LocalLlmUrlHelper.DefaultBaseUrl;

        /// <summary>Leer = erstes geladenes Vision-Modell am Server.</summary>
        public string LocalLlmModel { get; set; } = string.Empty;

        public string LocalLlmApiKey { get; set; } = string.Empty;

        /// <summary>Portable Ollama (externer Ordner AI_DOK-V01) für echten Text-Chat.</summary>
        public bool OllamaEnabled { get; set; } = false;

        public string OllamaBaseUrl { get; set; } = OllamaUrlHelper.DefaultBaseUrl;

        /// <summary>Bevorzugtes Textmodell im KI-Ordner (tinyllama = schnell auf CPU).</summary>
        public string OllamaModel { get; set; } = "tinyllama";

        /// <summary>Immer true: KI liegt extern neben DOK, nicht im Update.</summary>
        public bool OllamaUsePortableFolder { get; set; } = true;

        /// <summary>Optionaler Override; leer = automatisch AI_DOK-V01 neben DOK suchen.</summary>
        public string OllamaPortableDirectory { get; set; } = string.Empty;
    }

    internal static class AiSettingsStore
    {
        private static string StorePath => UserSettingsPath.GetFilePath("ai-settings.xml");

        public static AiSettings Load()
        {
            try
            {
                if (!File.Exists(StorePath))
                {
                    var defaults = CreateDefaults();
                    Save(defaults);
                    return defaults;
                }

                var doc = XDocument.Load(StorePath);
                var root = doc.Root;
                if (root == null)
                    return CreateDefaults();

                var settings = new AiSettings
                {
                    AiEnabled = !string.Equals((string)root.Element("AiEnabled"), "false", StringComparison.OrdinalIgnoreCase),
                    LocalOcrEnabled = !string.Equals((string)root.Element("LocalOcrEnabled"), "false", StringComparison.OrdinalIgnoreCase),
                    TesseractPath = (string)root.Element("TesseractPath") ?? string.Empty,
                    OcrLanguages = (string)root.Element("OcrLanguages") ?? "deu+eng",
                    LocalVisionEnabled = !string.Equals((string)root.Element("LocalVisionEnabled"), "false", StringComparison.OrdinalIgnoreCase),
                    DrawingVisionEnabled = !string.Equals((string)root.Element("DrawingVisionEnabled"), "false", StringComparison.OrdinalIgnoreCase),
                    LocalLlmBaseUrl = (string)root.Element("LocalLlmBaseUrl") ?? LocalLlmUrlHelper.DefaultBaseUrl,
                    LocalLlmModel = (string)root.Element("LocalLlmModel") ?? string.Empty,
                    LocalLlmApiKey = (string)root.Element("LocalLlmApiKey") ?? string.Empty,
                    OllamaEnabled = !string.Equals((string)root.Element("OllamaEnabled"), "false", StringComparison.OrdinalIgnoreCase),
                    OllamaBaseUrl = (string)root.Element("OllamaBaseUrl") ?? OllamaUrlHelper.DefaultBaseUrl,
                    OllamaModel = (string)root.Element("OllamaModel") ?? "tinyllama",
                    OllamaUsePortableFolder = !string.Equals((string)root.Element("OllamaUsePortableFolder"), "false", StringComparison.OrdinalIgnoreCase),
                    OllamaPortableDirectory = (string)root.Element("OllamaPortableDirectory") ?? string.Empty
                };

                if (root.Element("OllamaEnabled") == null)
                {
                    // Neu: große KI standardmäßig aus. Alte Dateien ohne Schalter bleiben aus,
                    // bis der Nutzer sie unter Einstellungen einschaltet.
                    settings.OllamaEnabled = false;
                    settings.OllamaUsePortableFolder = true;
                    if (string.IsNullOrWhiteSpace(settings.OllamaModel))
                        settings.OllamaModel = "tinyllama";
                    Save(settings);
                }

                // llama3.2:3b ist auf CPU oft Minuten langsam – für Chat auf tinyllama wechseln
                if (!string.IsNullOrWhiteSpace(settings.OllamaModel)
                    && settings.OllamaModel.IndexOf("llama3.2", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    settings.OllamaModel = "tinyllama";
                    Save(settings);
                }

                if (root.Element("LocalOcrEnabled") == null)
                {
                    settings.LocalOcrEnabled = true;
                    settings.LocalVisionEnabled = true;
                    Save(settings);
                }

                if (root.Element("DrawingVisionEnabled") == null)
                {
                    settings.DrawingVisionEnabled = true;
                    if (string.IsNullOrWhiteSpace(settings.LocalLlmBaseUrl))
                        settings.LocalLlmBaseUrl = LocalLlmUrlHelper.DefaultBaseUrl;
                    Save(settings);
                }

                return settings;
            }
            catch
            {
                return CreateDefaults();
            }
        }

        public static void Save(AiSettings settings)
        {
            WriteTo(StorePath, settings);
        }

        internal static AiSettings ReadFrom(string path)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                    return CreateDefaults();

                var doc = XDocument.Load(path);
                var root = doc.Root;
                if (root == null)
                    return CreateDefaults();

                return new AiSettings
                {
                    AiEnabled = !string.Equals((string)root.Element("AiEnabled"), "false", StringComparison.OrdinalIgnoreCase),
                    LocalOcrEnabled = !string.Equals((string)root.Element("LocalOcrEnabled"), "false", StringComparison.OrdinalIgnoreCase),
                    TesseractPath = (string)root.Element("TesseractPath") ?? string.Empty,
                    OcrLanguages = (string)root.Element("OcrLanguages") ?? "deu+eng",
                    LocalVisionEnabled = !string.Equals((string)root.Element("LocalVisionEnabled"), "false", StringComparison.OrdinalIgnoreCase),
                    DrawingVisionEnabled = !string.Equals((string)root.Element("DrawingVisionEnabled"), "false", StringComparison.OrdinalIgnoreCase),
                    LocalLlmBaseUrl = (string)root.Element("LocalLlmBaseUrl") ?? LocalLlmUrlHelper.DefaultBaseUrl,
                    LocalLlmModel = (string)root.Element("LocalLlmModel") ?? string.Empty,
                    LocalLlmApiKey = (string)root.Element("LocalLlmApiKey") ?? string.Empty,
                    OllamaEnabled = !string.Equals((string)root.Element("OllamaEnabled"), "false", StringComparison.OrdinalIgnoreCase),
                    OllamaBaseUrl = (string)root.Element("OllamaBaseUrl") ?? OllamaUrlHelper.DefaultBaseUrl,
                    OllamaModel = (string)root.Element("OllamaModel") ?? "tinyllama",
                    OllamaUsePortableFolder = !string.Equals((string)root.Element("OllamaUsePortableFolder"), "false", StringComparison.OrdinalIgnoreCase),
                    OllamaPortableDirectory = (string)root.Element("OllamaPortableDirectory") ?? string.Empty
                };
            }
            catch
            {
                return CreateDefaults();
            }
        }

        internal static void WriteTo(string path, AiSettings settings)
        {
            if (settings == null || string.IsNullOrWhiteSpace(path))
                return;

            try
            {
                var dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrWhiteSpace(dir))
                    Directory.CreateDirectory(dir);

                new XDocument(
                    new XElement("AiSettings",
                        new XElement("AiEnabled", settings.AiEnabled ? "true" : "false"),
                        new XElement("LocalOcrEnabled", settings.LocalOcrEnabled ? "true" : "false"),
                        new XElement("TesseractPath", settings.TesseractPath ?? string.Empty),
                        new XElement("OcrLanguages", settings.OcrLanguages ?? "deu+eng"),
                        new XElement("LocalVisionEnabled", settings.LocalVisionEnabled ? "true" : "false"),
                        new XElement("DrawingVisionEnabled", settings.DrawingVisionEnabled ? "true" : "false"),
                        new XElement("LocalLlmBaseUrl", settings.LocalLlmBaseUrl ?? LocalLlmUrlHelper.DefaultBaseUrl),
                        new XElement("LocalLlmModel", settings.LocalLlmModel ?? string.Empty),
                        new XElement("LocalLlmApiKey", settings.LocalLlmApiKey ?? string.Empty),
                        new XElement("OllamaEnabled", settings.OllamaEnabled ? "true" : "false"),
                        new XElement("OllamaBaseUrl", settings.OllamaBaseUrl ?? OllamaUrlHelper.DefaultBaseUrl),
                        new XElement("OllamaModel", settings.OllamaModel ?? "tinyllama"),
                        new XElement("OllamaUsePortableFolder", settings.OllamaUsePortableFolder ? "true" : "false"),
                        new XElement("OllamaPortableDirectory", settings.OllamaPortableDirectory ?? string.Empty)
                    )
                ).Save(path);
            }
            catch
            {
            }
        }

        private static AiSettings CreateDefaults()
        {
            return new AiSettings
            {
                OllamaEnabled = false
            };
        }
    }
}
