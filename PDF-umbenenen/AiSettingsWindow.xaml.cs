using System.Windows;
using PDF_umbenenen.Services.Ai;

namespace PDF_umbenenen
{
    public partial class AiSettingsWindow : Window
    {
        public AiSettingsWindow(AiSettings settings)
        {
            InitializeComponent();
            Settings = new AiSettings
            {
                AiEnabled = settings?.AiEnabled ?? true,
                OllamaBaseUrl = settings?.OllamaBaseUrl ?? "http://localhost:11434",
                ModelName = settings?.ModelName ?? "llama3.2:3b",
                VisionModelName = settings?.VisionModelName ?? "llava:7b",
                MaterialExtractionViaOllama = true
            };

            AiEnabledCheckBox.IsChecked = Settings.AiEnabled;
            OllamaUrlTextBox.Text = Settings.OllamaBaseUrl;
            AiModelNameTextBox.Text = Settings.ModelName;
        }

        public AiSettings Settings { get; private set; }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            Settings.AiEnabled = AiEnabledCheckBox.IsChecked == true;
            Settings.OllamaBaseUrl = OllamaUrlTextBox.Text.Trim();
            Settings.ModelName = AiModelNameTextBox.Text.Trim();
            Settings.VisionModelName = string.IsNullOrWhiteSpace(Settings.VisionModelName)
                ? "llava:7b"
                : Settings.VisionModelName;
            Settings.MaterialExtractionViaOllama = true;
            DialogResult = true;
            Close();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
