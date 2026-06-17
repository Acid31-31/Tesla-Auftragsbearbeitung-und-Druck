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
                    return new AiSettings();

                var doc = XDocument.Load(StorePath);
                var root = doc.Root;
                if (root == null)
                    return new AiSettings();

                return new AiSettings
                {
                    AiEnabled = !string.Equals((string)root.Element("AiEnabled"), "false", StringComparison.OrdinalIgnoreCase),
                    OllamaBaseUrl = (string)root.Element("OllamaBaseUrl") ?? "http://localhost:11434",
                    ModelName = (string)root.Element("ModelName") ?? "llama3.2:3b"
                };
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
                        new XElement("ModelName", settings.ModelName ?? "llama3.2:3b")
                    )
                ).Save(StorePath);
            }
            catch
            {
            }
        }
    }
}
