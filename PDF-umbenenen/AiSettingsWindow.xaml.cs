using System;
using System.IO;
using System.Windows;
using PDF_umbenenen.Services;
using PDF_umbenenen.Services.Ai;
using PDF_umbenenen.Services.Cli;

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
                LocalOcrEnabled = settings?.LocalOcrEnabled ?? true,
                TesseractPath = settings?.TesseractPath ?? string.Empty,
                OcrLanguages = settings?.OcrLanguages ?? "deu+eng",
                LocalVisionEnabled = settings?.LocalVisionEnabled ?? true
            };

            LocalVisionCheckBox.IsChecked = Settings.LocalVisionEnabled;
            LocalOcrCheckBox.IsChecked = Settings.LocalOcrEnabled;
            AiEnabledCheckBox.IsChecked = Settings.AiEnabled;
            TesseractPathTextBox.Text = Settings.TesseractPath;
            OcrLanguagesTextBox.Text = Settings.OcrLanguages;
            TryAutoFillTesseractPath();
            UpdateStatusHint();
        }

        public AiSettings Settings { get; private set; }

        private void TryAutoFillTesseractPath()
        {
            if (!string.IsNullOrWhiteSpace(TesseractPathTextBox.Text)
                && File.Exists(TesseractPathTextBox.Text))
            {
                return;
            }

            var detected = TesseractCliService.ResolveExecutable();
            if (!string.IsNullOrWhiteSpace(detected))
                TesseractPathTextBox.Text = detected;
        }

        private void UpdateStatusHint()
        {
            if (TesseractCliService.IsAvailable())
            {
                StatusHintTextBlock.Text = "Tesseract: gefunden — OCR offline bereit (" + TesseractCliService.ResolveExecutable() + ").";
                StatusHintTextBlock.Foreground = (System.Windows.Media.Brush)FindResource("TextSecondary");
                return;
            }

            StatusHintTextBlock.Text =
                "Tesseract: nicht gefunden. Wird beim Start automatisch eingerichtet (~48 MB, einmalig, kein Admin). "
                + "Alternativ „Installieren…“ — Ziel: "
                + TesseractInstallService.TesseractInstallDirectory + ".";
        }

        private void InstallTesseract_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                InstallTesseractButton.IsEnabled = false;

                if (TesseractCliService.IsAvailable())
                {
                    TryAutoFillTesseractPath();
                    UpdateStatusHint();
                    MessageBox.Show(
                        this,
                        "Tesseract ist bereits installiert:\n" + TesseractCliService.ResolveExecutable(),
                        "Tesseract",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                    return;
                }

                MessageBox.Show(
                    this,
                    "Tesseract wird heruntergeladen und installiert (~48 MB).\n\n"
                    + "Zielordner (Benutzer, kein Admin):\n"
                    + TesseractInstallService.TesseractInstallDirectory
                    + "\n\nBitte den Installer-Assistenten ggf. bestätigen.",
                    "Tesseract installieren",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);

                if (TesseractInstallService.TryInstall(out var resultMessage))
                {
                    TryAutoFillTesseractPath();
                    TesseractInstallService.PersistDetectedPath();
                    Settings.TesseractPath = TesseractPathTextBox.Text?.Trim() ?? string.Empty;
                    UpdateStatusHint();
                    MessageBox.Show(
                        this,
                        "Tesseract wurde installiert:\n" + resultMessage,
                        "Tesseract installieren",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                    return;
                }

                MessageBox.Show(
                    this,
                    "Tesseract konnte noch nicht automatisch eingerichtet werden.\n\n"
                    + resultMessage
                    + "\n\nAlternativ in PowerShell:\nwinget install UB-Mannheim.TesseractOCR",
                    "Hinweis",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    this,
                    "Installation konnte nicht gestartet werden:\n" + ex.Message
                    + "\n\nAlternativ: winget install UB-Mannheim.TesseractOCR",
                    "Fehler",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
            finally
            {
                InstallTesseractButton.IsEnabled = true;
                UpdateStatusHint();
            }
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            Settings.LocalVisionEnabled = LocalVisionCheckBox.IsChecked == true;
            Settings.LocalOcrEnabled = LocalOcrCheckBox.IsChecked == true;
            Settings.AiEnabled = AiEnabledCheckBox.IsChecked == true;
            Settings.TesseractPath = TesseractPathTextBox.Text?.Trim() ?? string.Empty;
            Settings.OcrLanguages = string.IsNullOrWhiteSpace(OcrLanguagesTextBox.Text)
                ? "deu+eng"
                : OcrLanguagesTextBox.Text.Trim();
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
