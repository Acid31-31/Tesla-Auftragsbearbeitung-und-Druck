using System;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace PDF_umbenenen.Services.Ai
{
    public sealed class AiSettings
    {
        public bool AiEnabled { get; set; } = true;

        public string OllamaBaseUrl { get; set; } = "http://localhost:11434";

        public string ModelName { get; set; } = "llama3.2:3b";

        /// <summary>Vision-Modell für Zeichnungsanalyse (z. B. llava:7b, moondream).</summary>
        public string VisionModelName { get; set; } = "llava:7b";

        /// <summary>Blechdicke per Ollama-Vision auf localhost bei Unsicherheit oder Abweichung prüfen.</summary>
        public bool MaterialExtractionViaOllama { get; set; } = true;
    }

    internal static class AiSettingsStore
    {
        private static readonly string StorePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PDF-umbenenen",
            "ai-settings.xml");

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
                {
                    var defaults = new AiSettings();
                    Save(defaults);
                    return defaults;
                }

                var settings = new AiSettings
                {
                    AiEnabled = !string.Equals((string)root.Element("AiEnabled"), "false", StringComparison.OrdinalIgnoreCase),
                    OllamaBaseUrl = (string)root.Element("OllamaBaseUrl") ?? "http://localhost:11434",
                    ModelName = (string)root.Element("ModelName") ?? "llama3.2:3b",
                    VisionModelName = (string)root.Element("VisionModelName") ?? "llava:7b",
                    MaterialExtractionViaOllama = true
                };

                if (root.Element("VisionModelName") == null
                    || root.Element("MaterialExtractionViaOllama") == null
                    || string.Equals((string)root.Element("MaterialExtractionViaOllama"), "false", StringComparison.OrdinalIgnoreCase))
                {
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
                        new XElement("OllamaBaseUrl", settings.OllamaBaseUrl ?? "http://localhost:11434"),
                        new XElement("ModelName", settings.ModelName ?? "llama3.2:3b"),
                        new XElement("VisionModelName", settings.VisionModelName ?? "llava:7b"),
                        new XElement("MaterialExtractionViaOllama", settings.MaterialExtractionViaOllama ? "true" : "false")
                    )
                ).Save(StorePath);
            }
            catch
            {
            }
        }
    }
}
