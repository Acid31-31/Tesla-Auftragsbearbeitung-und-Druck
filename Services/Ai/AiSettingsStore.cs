using System;
using System.IO;
using System.Xml.Linq;

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
                    var defaults = new AiSettings();
                    Save(defaults);
                    return defaults;
                }

                var doc = XDocument.Load(StorePath);
                var root = doc.Root;
                if (root == null)
                    return new AiSettings();

                var settings = new AiSettings
                {
                    AiEnabled = !string.Equals((string)root.Element("AiEnabled"), "false", StringComparison.OrdinalIgnoreCase),
                    LocalOcrEnabled = !string.Equals((string)root.Element("LocalOcrEnabled"), "false", StringComparison.OrdinalIgnoreCase),
                    TesseractPath = (string)root.Element("TesseractPath") ?? string.Empty,
                    OcrLanguages = (string)root.Element("OcrLanguages") ?? "deu+eng",
                    LocalVisionEnabled = !string.Equals((string)root.Element("LocalVisionEnabled"), "false", StringComparison.OrdinalIgnoreCase)
                };

                // Migration alter Ollama-Einstellungen: OCR/Vision lokal aktiv lassen
                if (root.Element("LocalOcrEnabled") == null)
                {
                    settings.LocalOcrEnabled = true;
                    settings.LocalVisionEnabled = true;
                    Save(settings);
                }

                return settings;
            }
            catch
            {
                return new AiSettings();
            }
        }

        public static void Save(AiSettings settings)
        {
            if (settings == null)
                return;

            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(StorePath));
                new XDocument(
                    new XElement("AiSettings",
                        new XElement("AiEnabled", settings.AiEnabled ? "true" : "false"),
                        new XElement("LocalOcrEnabled", settings.LocalOcrEnabled ? "true" : "false"),
                        new XElement("TesseractPath", settings.TesseractPath ?? string.Empty),
                        new XElement("OcrLanguages", settings.OcrLanguages ?? "deu+eng"),
                        new XElement("LocalVisionEnabled", settings.LocalVisionEnabled ? "true" : "false")
                    )
                ).Save(StorePath);
            }
            catch
            {
            }
        }
    }
}
