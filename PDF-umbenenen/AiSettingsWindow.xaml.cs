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
                LocalVisionEnabled = settings?.LocalVisionEnabled ?? true,
                DrawingVisionEnabled = settings?.DrawingVisionEnabled ?? true,
                LocalLlmBaseUrl = string.IsNullOrWhiteSpace(settings?.LocalLlmBaseUrl)
                    ? LocalLlmUrlHelper.DefaultBaseUrl
                    : settings.LocalLlmBaseUrl,
                LocalLlmModel = settings?.LocalLlmModel ?? string.Empty,
                LocalLlmApiKey = settings?.LocalLlmApiKey ?? string.Empty,
                OllamaEnabled = settings?.OllamaEnabled ?? false,
                OllamaBaseUrl = settings?.OllamaBaseUrl ?? OllamaUrlHelper.DefaultBaseUrl,
                OllamaModel = string.IsNullOrWhiteSpace(settings?.OllamaModel) ? "tinyllama" : settings.OllamaModel,
                OllamaUsePortableFolder = settings?.OllamaUsePortableFolder ?? true,
                OllamaPortableDirectory = settings?.OllamaPortableDirectory ?? string.Empty
            };

            LocalVisionCheckBox.IsChecked = Settings.LocalVisionEnabled;
            DrawingVisionCheckBox.IsChecked = Settings.DrawingVisionEnabled;
            DrawingVisionModuleTextBlock.Text = "Eingebautes Modul: "
                + OllamaPortableService.GetVisionModuleDisplayName(Settings)
                + " (Bildmodell für Blechdicke und RAL)";
            LocalOcrCheckBox.IsChecked = Settings.LocalOcrEnabled;
            AiEnabledCheckBox.IsChecked = Settings.AiEnabled;
            OllamaEnabledCheckBox.IsChecked = Settings.OllamaEnabled;
            OllamaModelTextBox.Text = Settings.OllamaModel;
            KiFolderPathTextBox.Text = string.IsNullOrWhiteSpace(Settings.OllamaPortableDirectory)
                ? AppInfo.GetDefaultPortableKiDirectory()
                : Settings.OllamaPortableDirectory;
            TesseractPathTextBox.Text = Settings.TesseractPath;
            OcrLanguagesTextBox.Text = Settings.OcrLanguages;
            TryAutoFillTesseractPath();
            UpdateStatusHint();
            Loaded += AiSettingsWindow_Loaded;
        }

        private async void AiSettingsWindow_Loaded(object sender, RoutedEventArgs e)
        {
            Loaded -= AiSettingsWindow_Loaded;
            await CheckOllamaUpdateAsync().ConfigureAwait(true);
            await CheckTesseractUpdateAsync().ConfigureAwait(true);
            await CheckDrawingVisionUpdateAsync().ConfigureAwait(true);
        }

        public AiSettings Settings { get; private set; }

        private TesseractUpdateInfo _tesseractUpdate;
        private OllamaUpdateInfo _ollamaUpdate;
        private DrawingVisionUpdateInfo _drawingVisionUpdate;

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
            var kiDir = AppInfo.TryFindPortableAiDirectory()
                        ?? AppInfo.GetDefaultPortableKiDirectory();
            var kiOk = AppInfo.HasPortableAiLayout(kiDir);
            var tessOk = TesseractCliService.IsAvailable();
            var tessVersion = TesseractCliService.TryReadVersion();

            if (TesseractInstalledTextBlock != null)
            {
                TesseractInstalledTextBlock.Text = tessOk
                    ? "Installiert: " + (string.IsNullOrWhiteSpace(tessVersion) ? "gefunden (Version unbekannt)" : tessVersion)
                    : "Installiert: nicht gefunden";
            }

            if (TesseractLatestTextBlock != null
                && (TesseractLatestTextBlock.Text == null
                    || TesseractLatestTextBlock.Text.IndexOf("wird geprüft", StringComparison.OrdinalIgnoreCase) >= 0
                    || TesseractLatestTextBlock.Text.IndexOf("Prüfung", StringComparison.OrdinalIgnoreCase) >= 0))
            {
                TesseractLatestTextBlock.Text = "Neueste Version: wird geprüft…";
            }

            var lines = new System.Text.StringBuilder();
            lines.Append(kiOk
                ? "Ollama-Ordner gefunden: " + kiDir
                : "Ollama-Ordner fehlt – bitte AI_DOK-V01 neben DOK legen (erwartet: " + kiDir + ").");
            lines.Append(tessOk ? "  ·  Tesseract: vorhanden" : "  ·  Tesseract: nicht gefunden");

            StatusHintTextBlock.Text = lines.ToString();
            StatusHintTextBlock.Foreground = (System.Windows.Media.Brush)FindResource("TextSecondary");
            KiFolderPathTextBox.Text = kiDir;
        }

        private async System.Threading.Tasks.Task CheckOllamaUpdateAsync()
        {
            try
            {
                if (!OllamaUpdateCheckService.ShouldOfferRuntimeUpdate(Settings))
                {
                    _ollamaUpdate = null;
                    if (OllamaInstalledTextBlock != null)
                    {
                        OllamaInstalledTextBlock.Text = "Installiert: große KI aus – kein Update";
                    }

                    if (OllamaLatestTextBlock != null)
                    {
                        OllamaLatestTextBlock.Text = "Neueste Version: nach dem Einschalten wird geprüft";
                        OllamaLatestTextBlock.Foreground = (System.Windows.Media.Brush)FindResource("TextSecondary");
                    }

                    if (UpdateOllamaButton != null)
                        UpdateOllamaButton.Visibility = Visibility.Collapsed;
                    return;
                }

                var info = await OllamaUpdateCheckService.CheckAsync().ConfigureAwait(true);
                _ollamaUpdate = info;

                if (OllamaInstalledTextBlock != null)
                {
                    if (!info.FolderFound)
                        OllamaInstalledTextBlock.Text = "Installiert: Ordner fehlt";
                    else if (string.IsNullOrWhiteSpace(info.InstalledVersion))
                        OllamaInstalledTextBlock.Text = "Installiert: gefunden (Version unbekannt)";
                    else
                        OllamaInstalledTextBlock.Text = "Installiert: " + info.InstalledVersion;
                }

                if (OllamaLatestTextBlock != null)
                {
                    if (!string.IsNullOrWhiteSpace(info.LatestVersion))
                    {
                        OllamaLatestTextBlock.Text = info.NewerAvailable
                            ? "Neueste Version: " + info.LatestVersion + "  —  Update verfügbar"
                            : "Neueste Version: " + info.LatestVersion + "  —  aktuell";
                        OllamaLatestTextBlock.Foreground = info.NewerAvailable
                            ? (System.Windows.Media.Brush)FindResource("WarningBrush")
                            : (System.Windows.Media.Brush)FindResource("SuccessBrush");
                    }
                    else if (!string.IsNullOrWhiteSpace(info.ErrorMessage))
                    {
                        OllamaLatestTextBlock.Text = "Neueste Version: Prüfung fehlgeschlagen";
                        OllamaLatestTextBlock.Foreground = (System.Windows.Media.Brush)FindResource("WarningBrush");
                    }
                    else
                    {
                        OllamaLatestTextBlock.Text = "Neueste Version: unbekannt";
                        OllamaLatestTextBlock.Foreground = (System.Windows.Media.Brush)FindResource("TextSecondary");
                    }
                }

                if (UpdateOllamaButton != null)
                    UpdateOllamaButton.Visibility = info.NewerAvailable
                        ? Visibility.Visible
                        : Visibility.Collapsed;
            }
            catch
            {
                if (OllamaLatestTextBlock != null)
                    OllamaLatestTextBlock.Text = "Neueste Version: Prüfung fehlgeschlagen";
                if (UpdateOllamaButton != null)
                    UpdateOllamaButton.Visibility = Visibility.Collapsed;
            }
        }

        private async void UpdateOllama_Click(object sender, RoutedEventArgs e)
        {
            var info = _ollamaUpdate;
            if (info == null || !info.NewerAvailable)
                return;

            var dialog = new OllamaUpdateWindow(info)
            {
                Owner = this,
                WindowStartupLocation = WindowStartupLocation.CenterOwner
            };
            ThemeWindowService.Attach(dialog);
            dialog.ShowDialog();
            if (dialog.Updated)
                await CheckOllamaUpdateAsync().ConfigureAwait(true);
        }

        private async System.Threading.Tasks.Task CheckTesseractUpdateAsync()
        {
            try
            {
                var info = await TesseractUpdateCheckService.CheckAsync().ConfigureAwait(true);
                if (TesseractInstalledTextBlock != null)
                {
                    TesseractInstalledTextBlock.Text = string.IsNullOrWhiteSpace(info.InstalledVersion)
                        ? "Installiert: nicht gefunden"
                        : "Installiert: " + info.InstalledVersion;
                }

                if (TesseractLatestTextBlock != null)
                {
                    if (!string.IsNullOrWhiteSpace(info.LatestVersion))
                    {
                        TesseractLatestTextBlock.Text = info.NewerAvailable
                            ? "Neueste Version: " + info.LatestVersion + "  —  Update verfügbar"
                            : "Neueste Version: " + info.LatestVersion + "  —  aktuell";
                        TesseractLatestTextBlock.Foreground = info.NewerAvailable
                            ? (System.Windows.Media.Brush)FindResource("WarningBrush")
                            : (System.Windows.Media.Brush)FindResource("SuccessBrush");
                    }
                    else if (!string.IsNullOrWhiteSpace(info.ErrorMessage))
                    {
                        TesseractLatestTextBlock.Text = "Neueste Version: Prüfung fehlgeschlagen";
                        TesseractLatestTextBlock.Foreground = (System.Windows.Media.Brush)FindResource("WarningBrush");
                    }
                    else
                    {
                        TesseractLatestTextBlock.Text = "Neueste Version: unbekannt";
                        TesseractLatestTextBlock.Foreground = (System.Windows.Media.Brush)FindResource("TextSecondary");
                    }
                }

                _tesseractUpdate = info;
                if (UpdateTesseractButton != null)
                    UpdateTesseractButton.Visibility = info.NewerAvailable
                        ? Visibility.Visible
                        : Visibility.Collapsed;
            }
            catch
            {
                if (TesseractLatestTextBlock != null)
                    TesseractLatestTextBlock.Text = "Neueste Version: Prüfung fehlgeschlagen";
                if (UpdateTesseractButton != null)
                    UpdateTesseractButton.Visibility = Visibility.Collapsed;
            }
        }

        private async void UpdateTesseract_Click(object sender, RoutedEventArgs e)
        {
            var info = _tesseractUpdate;
            if (info == null || !info.NewerAvailable)
                return;

            var dialog = new TesseractUpdateWindow(info)
            {
                Owner = this,
                WindowStartupLocation = WindowStartupLocation.CenterOwner
            };
            ThemeWindowService.Attach(dialog);
            dialog.ShowDialog();
            if (dialog.Updated)
            {
                TryAutoFillTesseractPath();
                TesseractPathTextBox.Text = TesseractInstallService.InstalledExecutablePath;
                Settings.TesseractPath = TesseractPathTextBox.Text;
                UpdateStatusHint();
                await CheckTesseractUpdateAsync().ConfigureAwait(true);
            }
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
                    if (_tesseractUpdate != null && _tesseractUpdate.NewerAvailable)
                    {
                        UpdateTesseract_Click(sender, e);
                        return;
                    }

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
                var unused = CheckTesseractUpdateAsync();
            }
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            Settings.LocalVisionEnabled = LocalVisionCheckBox.IsChecked == true;
            Settings.DrawingVisionEnabled = DrawingVisionCheckBox.IsChecked == true;
            Settings.LocalOcrEnabled = LocalOcrCheckBox.IsChecked == true;
            Settings.AiEnabled = AiEnabledCheckBox.IsChecked == true;
            Settings.OllamaEnabled = OllamaEnabledCheckBox.IsChecked == true;
            Settings.OllamaUsePortableFolder = true;
            Settings.OllamaModel = string.IsNullOrWhiteSpace(OllamaModelTextBox.Text)
                ? "tinyllama"
                : OllamaModelTextBox.Text.Trim();
            Settings.OllamaPortableDirectory = string.Empty;
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

        private async System.Threading.Tasks.Task CheckDrawingVisionUpdateAsync()
        {
            try
            {
                var info = await DrawingVisionUpdateCheckService.CheckAsync().ConfigureAwait(true);
                _drawingVisionUpdate = info;

                if (DrawingVisionModuleTextBlock != null)
                {
                    DrawingVisionModuleTextBlock.Text = "Eingebautes Modul: "
                        + (string.IsNullOrWhiteSpace(info.ModuleDisplayName)
                            ? "Ollama · moondream"
                            : info.ModuleDisplayName)
                        + " (Bildmodell für Blechdicke und RAL)";
                }

                if (DrawingVisionInstalledTextBlock != null)
                {
                    DrawingVisionInstalledTextBlock.Text = info.FolderFound
                        ? "Installiert: " + (string.IsNullOrWhiteSpace(info.InstalledVersion) ? "nicht gefunden" : info.InstalledVersion)
                        : "Installiert: KI-Ordner fehlt";
                }

                if (DrawingVisionLatestTextBlock != null)
                {
                    if (!string.IsNullOrWhiteSpace(info.LatestVersion))
                    {
                        DrawingVisionLatestTextBlock.Text = info.NewerAvailable
                            ? "Neueste Version: " + info.LatestVersion + "  —  Update verfügbar"
                            : "Neueste Version: " + info.LatestVersion + "  —  aktuell";
                        DrawingVisionLatestTextBlock.Foreground = info.NewerAvailable
                            ? (System.Windows.Media.Brush)FindResource("WarningBrush")
                            : (System.Windows.Media.Brush)FindResource("SuccessBrush");
                    }
                    else if (!string.IsNullOrWhiteSpace(info.ErrorMessage))
                    {
                        DrawingVisionLatestTextBlock.Text = "Neueste Version: Prüfung fehlgeschlagen";
                        DrawingVisionLatestTextBlock.Foreground = (System.Windows.Media.Brush)FindResource("WarningBrush");
                    }
                    else
                    {
                        DrawingVisionLatestTextBlock.Text = "Neueste Version: unbekannt";
                        DrawingVisionLatestTextBlock.Foreground = (System.Windows.Media.Brush)FindResource("TextSecondary");
                    }
                }

                if (UpdateDrawingVisionButton != null)
                    UpdateDrawingVisionButton.Visibility = info.NewerAvailable
                        ? Visibility.Visible
                        : Visibility.Collapsed;
            }
            catch
            {
                if (DrawingVisionLatestTextBlock != null)
                    DrawingVisionLatestTextBlock.Text = "Neueste Version: Prüfung fehlgeschlagen";
                if (UpdateDrawingVisionButton != null)
                    UpdateDrawingVisionButton.Visibility = Visibility.Collapsed;
            }
        }

        private async void UpdateDrawingVision_Click(object sender, RoutedEventArgs e)
        {
            var info = _drawingVisionUpdate;
            if (info == null || !info.NewerAvailable)
                return;

            var dialog = new DrawingVisionUpdateWindow(info)
            {
                Owner = this,
                WindowStartupLocation = WindowStartupLocation.CenterOwner
            };
            ThemeWindowService.Attach(dialog);
            dialog.ShowDialog();
            if (dialog.Updated)
                await CheckDrawingVisionUpdateAsync().ConfigureAwait(true);
        }
    }
}
