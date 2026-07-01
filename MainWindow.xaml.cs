using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Diagnostics;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using System.Xml.Linq;
using System.Text.RegularExpressions;
using Forms = System.Windows.Forms;
using PDF_umbenenen.Models;
using PDF_umbenenen.Services;
using PDF_umbenenen.Services.Ai;

namespace PDF_umbenenen
{
    public partial class MainWindow : Window
    {
        private bool _isDarkTheme = true;
        private System.Windows.Controls.TextBox ExcelPathBox => FindName("ExcelPathTextBox") as System.Windows.Controls.TextBox;
        private System.Windows.Controls.TextBox PdfFolderBox => FindName("PdfFolderTextBox") as System.Windows.Controls.TextBox;
        private System.Windows.Controls.TextBox OrderNumberBox => FindName("OrderNumberTextBox") as System.Windows.Controls.TextBox;
        private System.Windows.Controls.TextBlock OrderVerificationPdfStatusBlock => FindName("OrderVerificationPdfStatusTextBlock") as System.Windows.Controls.TextBlock;
        private string _orderVerificationPdfPath = string.Empty;
        private IList<OrderVerificationLine> _cachedOrderVerificationLines;
        private const string OrderVerificationDropHint = "Bestell-PDF hier ablegen …";
        private System.Windows.Controls.CheckBox IncludeSubfoldersBox => FindName("IncludeSubfoldersCheckBox") as System.Windows.Controls.CheckBox;
        private System.Windows.Controls.TextBlock DroppedExtensionsBlock => FindName("DroppedExtensionsTextBlock") as System.Windows.Controls.TextBlock;
        private System.Windows.Controls.Button ProcessButtonBox => FindName("ProcessButton") as System.Windows.Controls.Button;
        private System.Windows.Controls.Button CancelButtonBox => FindName("CancelButton") as System.Windows.Controls.Button;
        private System.Windows.Controls.Button MergePrintButtonBox => FindName("MergePrintButton") as System.Windows.Controls.Button;
        private System.Windows.Controls.Button DeleteProcessedButtonBox => FindName("DeleteProcessedButton") as System.Windows.Controls.Button;
        private System.Windows.Controls.StackPanel ProgressPanelBox => FindName("ProgressPanel") as System.Windows.Controls.StackPanel;
        private System.Windows.Controls.ProgressBar BusyProgressBarBox => FindName("BusyProgressBar") as System.Windows.Controls.ProgressBar;
        private System.Windows.Controls.TextBlock ProgressLabelBox => FindName("ProgressLabel") as System.Windows.Controls.TextBlock;
        private System.Windows.Controls.TextBox StatusTextBoxBox => FindName("StatusTextBox") as System.Windows.Controls.TextBox;
        private System.Windows.Controls.TextBlock ThemeIconBox => FindName("ThemeIcon") as System.Windows.Controls.TextBlock;
        private System.Windows.Controls.TextBlock ThemeLabelBox => FindName("ThemeLabel") as System.Windows.Controls.TextBlock;
        private System.Windows.Controls.MenuItem AiEnabledMenuItemBox => FindName("AiEnabledMenuItem") as System.Windows.Controls.MenuItem;

        private readonly OfflineAiOrchestrator _aiOrchestrator = new OfflineAiOrchestrator();
        private readonly PdfTextExtractionService _pdfTextService = new PdfTextExtractionService();
        private readonly OrderExcelVerificationService _orderExcelVerificationService = new OrderExcelVerificationService();
        private AiMatchingService _aiMatchingService;
        private AiFieldExtractionService _aiFieldExtractionService;
        private AiQualityCheckService _aiQualityCheckService;
        private AiChatService _aiChatService;
        private AiQualityReport _lastQualityReport;
        private AiChatWindow _aiChatWindow;
        private WatermarkSettings _watermarkSettings;
        private bool _menuSync;
        private string _lastMergedPdfPath;

        public MainWindow()
        {
            LoadXaml();
            Icon = ThemeWindowService.GetAppIcon();
            StatusTextBoxBox.Text = "Bereit.";
            ApplyTheme(true);

            LoadSavedPaths();
            _watermarkSettings = WatermarkSettingsStore.Load();
            InitializeAiServices();
            RefreshAiMenuToggle();
            var demoStatus = DemoLicenseService.Evaluate();
            Title = AppInfo.ProductName + AppInfo.WindowTitleSuffix + demoStatus.TitleSuffix;
            var versionBlock = FindName("AppVersionTextBlock") as System.Windows.Controls.TextBlock;
            if (versionBlock != null)
            {
                versionBlock.Text = "Version " + AppInfo.DisplayVersion + " · " + demoStatus.VersionLine + " · Build " + AppInfo.BuildDate;
            }

            Loaded += MainWindow_Loaded;

            _stopwatchTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(1)
            };
            _stopwatchTimer.Tick += StopwatchTimer_Tick;
        }

        private void StopwatchTimer_Tick(object sender, EventArgs e)
        {
            UpdateStopwatchDisplay();
        }

        private async Task CheckForUpdatesAsync(bool showIfCurrent)
        {
            try
            {
                if (showIfCurrent)
                    Mouse.OverrideCursor = Cursors.Wait;

                var update = await GitHubUpdateService.CheckForUpdateAsync().ConfigureAwait(true);

                if (!string.IsNullOrWhiteSpace(update.ErrorMessage))
                {
                    if (showIfCurrent)
                    {
                        AppendStatus("ℹ Update-Prüfung: " + update.ErrorMessage);
                        MessageBox.Show(
                            this,
                            update.ErrorMessage,
                            "Update-Prüfung",
                            MessageBoxButton.OK,
                            MessageBoxImage.Warning);
                    }
                    return;
                }

                if (update.UpdateAvailable)
                {
                    AppendStatus("⬆ Update verfügbar: " + update.ReleaseTag);
                    var dialog = new UpdateAvailableWindow(update)
                    {
                        Owner = this,
                        WindowStartupLocation = WindowStartupLocation.CenterOwner
                    };
                    ThemeWindowService.Attach(dialog);
                    dialog.ShowDialog();
                    return;
                }

                if (showIfCurrent)
                {
                    var message = "Sie verwenden bereits die neueste Version (" + AppInfo.DisplayVersion + ").";
                    AppendStatus("✓ " + message);
                    MessageBox.Show(this, message, "Update-Prüfung", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                if (showIfCurrent)
                {
                    AppendStatus("ℹ Update-Prüfung fehlgeschlagen: " + ex.Message);
                    MessageBox.Show(
                        this,
                        ex.Message,
                        "Update-Prüfung",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                }
            }
            finally
            {
                if (showIfCurrent)
                    Mouse.OverrideCursor = null;
            }
        }

        private async void CheckForUpdatesMenu_Click(object sender, RoutedEventArgs e)
        {
            AppendStatus("Prüfe GitHub auf Updates…");
            await CheckForUpdatesAsync(showIfCurrent: true);
        }

        private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            ThemeWindowService.Attach(this);
            EnsureDesktopShortcut();

            var legacyCleanup = LegacyBrandingCleanupService.Cleanup();
            if (legacyCleanup.RemovedLegacyInstall || legacyCleanup.RemovedDesktopShortcut)
            {
                AppendStatus("Alte Installation/Verknüpfung entfernt.");
                AppendStatus("Hinweis: Falls in der Taskleiste noch ein altes Symbol angeheftet ist: Rechtsklick → Von Taskleiste lösen.");
            }

            RefreshDroppedFilesDisplay();

            if (AppInfo.IsDemoEdition)
            {
                var demoStatus = DemoLicenseService.Evaluate();
                AppendStatus("ℹ " + demoStatus.SummaryText);
            }

            await Task.Delay(TimeSpan.FromSeconds(12)).ConfigureAwait(true);
            if (IsLoaded)
                await CheckForUpdatesAsync(showIfCurrent: false);
        }

        private void InitializeAiServices()
        {
            _aiMatchingService = new AiMatchingService(_aiOrchestrator, _pdfTextService);
            _aiFieldExtractionService = new AiFieldExtractionService(_aiOrchestrator, _pdfTextService);
            _aiQualityCheckService = new AiQualityCheckService(_aiOrchestrator, _aiMatchingService, _aiFieldExtractionService);
            _aiChatService = new AiChatService(_aiOrchestrator);
            _ = RefreshAiStatusAsync();
        }

        private async Task RefreshAiStatusAsync()
        {
            try
            {
                await _aiOrchestrator.RefreshAvailabilityAsync(CancellationToken.None).ConfigureAwait(true);
                UpdateAiStatusUi();
            }
            catch (Exception ex)
            {
                AppendStatus("KI-Statusfehler: " + ex.Message);
            }
        }

        private void UpdateAiStatusUi()
        {
            _aiChatService?.SetAppContext(BuildAiAppContext());
        }

        private string BuildAiAppContext()
        {
            if (_watermarkSettings == null)
                _watermarkSettings = WatermarkSettingsStore.Load();

            return string.Format(
                CultureInfo.InvariantCulture,
                "Excel: {0}\nPDF-Ordner: {1}\nAuftragsnummer: {2}\nWasserzeichen: {3}\nIntelligente Platzierung: {4}\nWerkstoff-Erkennung: {5}\nMaterialstärke: {6}",
                ExcelPathBox.Text,
                PdfFolderBox.Text,
                OrderNumberBox.Text,
                _watermarkSettings.WatermarksEnabled ? "An" : "Aus",
                _watermarkSettings.UseSmartPlacement ? "Ja" : "Nein",
                _watermarkSettings.ExtractMaterialType ? "An" : "Aus",
                _watermarkSettings.ExtractMaterialThickness ? "An" : "Aus");
        }

        private async void AiRefreshStatus_Click(object sender, RoutedEventArgs e)
        {
            await RefreshAiStatusAsync();
            AppendStatus("KI-Status: " + _aiOrchestrator.GetStatusText());
        }

        private async void AiSettingsMenu_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new AiSettingsWindow(_aiOrchestrator.Settings)
            {
                Owner = this,
                WindowStartupLocation = WindowStartupLocation.CenterOwner
            };
            ThemeWindowService.Attach(dlg);

            if (dlg.ShowDialog() != true)
                return;

            _aiOrchestrator.Settings.AiEnabled = dlg.Settings.AiEnabled;
            _aiOrchestrator.Settings.OllamaBaseUrl = dlg.Settings.OllamaBaseUrl;
            _aiOrchestrator.Settings.ModelName = dlg.Settings.ModelName;
            _aiOrchestrator.Settings.VisionModelName = dlg.Settings.VisionModelName;
            _aiOrchestrator.Settings.MaterialExtractionViaOllama = true;
            _aiOrchestrator.SaveSettings();
            RefreshAiMenuToggle();
            await RefreshAiStatusAsync();
            AppendStatus("KI-Einstellungen gespeichert.");
        }

        private void WatermarkSettingsMenu_Click(object sender, RoutedEventArgs e)
        {
            if (_watermarkSettings == null)
                _watermarkSettings = WatermarkSettingsStore.Load();

            var dlg = new WatermarkSettingsWindow(_watermarkSettings)
            {
                Owner = this,
                WindowStartupLocation = WindowStartupLocation.CenterOwner
            };
            ThemeWindowService.Attach(dlg);

            if (dlg.ShowDialog() != true)
                return;

            _watermarkSettings.WatermarksEnabled = dlg.Settings.WatermarksEnabled;
            _watermarkSettings.UseSmartPlacement = dlg.Settings.UseSmartPlacement;
            _watermarkSettings.ExtractMaterialType = dlg.Settings.ExtractMaterialType;
            _watermarkSettings.ExtractMaterialThickness = dlg.Settings.ExtractMaterialThickness;
            WatermarkSettingsStore.Save(_watermarkSettings);
            UpdateAiStatusUi();
            AppendStatus(string.Format(CultureInfo.InvariantCulture,
                "Wasserzeichen-Einstellungen gespeichert: {0}, intelligent {1}, Werkstoff {2}, Stärke {3}.",
                _watermarkSettings.WatermarksEnabled ? "An" : "Aus",
                _watermarkSettings.UseSmartPlacement ? "An" : "Aus",
                _watermarkSettings.ExtractMaterialType ? "An" : "Aus",
                _watermarkSettings.ExtractMaterialThickness ? "An" : "Aus"));
        }

        private void CorrectWatermarkMenu_Click(object sender, RoutedEventArgs e)
        {
            var pdfFolder = PdfFolderBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(pdfFolder) || !Directory.Exists(pdfFolder))
            {
                AppendStatus("Bitte zuerst einen gültigen PDF-Ordner wählen.");
                return;
            }

            var searchOption = IncludeSubfoldersBox.IsChecked == true
                ? SearchOption.AllDirectories
                : SearchOption.TopDirectoryOnly;

            var orderedFiles = GetMergeCandidateFiles(pdfFolder, searchOption);
            if (orderedFiles.Count == 0)
            {
                MessageBox.Show(
                    this,
                    "Keine bearbeiteten PDF-Dateien gefunden (z. B. 01_..., 02_...).",
                    "Wasserzeichen korrigieren",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            OpenWatermarkEditor(orderedFiles, pdfFolder);
        }

        private void OpenWatermarkEditor(IList<string> orderedFiles, string pdfFolder, IEnumerable<string> editorPaths = null)
        {
            var paths = (editorPaths ?? orderedFiles)
                .Where(File.Exists)
                .ToList();
            if (paths.Count == 0)
                return;

            var editor = new PdfWatermarkEditorWindow(
                paths,
                pdfFolder,
                _lastMergedPdfPath,
                orderedFiles)
            {
                Owner = this,
                WindowStartupLocation = WindowStartupLocation.CenterOwner
            };
            ThemeWindowService.Attach(editor);

            if (editor.ShowDialog() == true && !string.IsNullOrWhiteSpace(editor.MergedPdfPath))
            {
                _lastMergedPdfPath = editor.MergedPdfPath;
                AppendStatus("✓ Gesamt-PDF gespeichert und geöffnet: " + Path.GetFileName(editor.MergedPdfPath));
            }
        }

        private void NewCustomerMenu_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new NewCustomerWindow
            {
                Owner = this,
                WindowStartupLocation = WindowStartupLocation.CenterOwner
            };
            ThemeWindowService.Attach(dlg);
            if (dlg.ShowDialog() == true)
                AppendStatus("ℹ Neues Kundenprofil gespeichert. Beim Sortieren wird der Kunde automatisch erkannt.");
        }

        private void ManageCustomersMenu_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new ManageCustomersWindow
            {
                Owner = this,
                WindowStartupLocation = WindowStartupLocation.CenterOwner
            };
            ThemeWindowService.Attach(dlg);
            dlg.ShowDialog();
        }

        private static List<ExcelRowCandidate> BuildExcelRowsFromOrderLines(IList<OrderVerificationLine> lines)
        {
            var rows = new List<ExcelRowCandidate>();
            if (lines == null || lines.Count == 0)
                return rows;

            for (var i = 0; i < lines.Count; i++)
            {
                var line = lines[i];
                if (line == null || string.IsNullOrWhiteSpace(line.DrawingNumber))
                    continue;

                var drawing = line.DrawingNumber.Trim();
                var key = NormalizeKey(StripRevSuffix(drawing));
                if (string.IsNullOrWhiteSpace(key))
                    continue;

                rows.Add(new ExcelRowCandidate
                {
                    RowIndex = rows.Count + 1,
                    NormalizedCells = new List<string> { key },
                    RawDrawingNumber = drawing,
                    RequestedAmount = line.Amount,
                    RequestedDeliveryDate = line.DeliveryDate,
                    DetectedArticleNumbers = string.IsNullOrWhiteSpace(line.ArticleNumber)
                        ? new List<string>()
                        : new List<string> { line.ArticleNumber }
                });
            }

            return rows;
        }

        private void RefreshAiMenuToggle()
        {
            _menuSync = true;
            try
            {
                if (AiEnabledMenuItemBox != null)
                    AiEnabledMenuItemBox.IsChecked = _aiOrchestrator.Settings.AiEnabled;
            }
            finally
            {
                _menuSync = false;
            }
        }

        private void AiEnabledMenu_Changed(object sender, RoutedEventArgs e)
        {
            if (_menuSync)
                return;

            _aiOrchestrator.Settings.AiEnabled = AiEnabledMenuItemBox?.IsChecked == true;
            _aiOrchestrator.SaveSettings();
            UpdateAiStatusUi();
            AppendStatus(_aiOrchestrator.Settings.AiEnabled ? "KI aktiviert." : "KI deaktiviert (Regel-Fallback).");
        }

        private bool TryResolveExcelPath(ref string excelPath, Action<string> log = null)
        {
            if (!string.IsNullOrWhiteSpace(excelPath) && File.Exists(excelPath))
                return true;

            var workspaceFolder = PathStore.GetOrCreateDefaultWorkspaceFolder();
            var resolved = ExcelDeliveryDateMergeService.ResolvePrimaryExcelPath(workspaceFolder);
            if (string.IsNullOrWhiteSpace(resolved))
                return false;

            excelPath = resolved;
            ExcelPathBox.Text = resolved;
            PathStore.SaveExcel(resolved);
            log?.Invoke("ℹ Excel automatisch geladen: " + Path.GetFileName(resolved));
            return true;
        }

        private async void AiQualityCheck_Click(object sender, RoutedEventArgs e)
        {
            var excelPath = ExcelPathBox.Text.Trim();
            var pdfFolder = PdfFolderBox.Text.Trim();
            if (!TryResolveExcelPath(ref excelPath, AppendStatus))
            {
                AppendStatus("Bitte zuerst eine gültige Excel-Datei laden.");
                return;
            }

            if (string.IsNullOrWhiteSpace(pdfFolder) || !Directory.Exists(pdfFolder))
            {
                AppendStatus("Bitte zuerst PDFs in den Ablage-Ordner laden.");
                return;
            }

            try
            {
                AppendStatus("KI-Qualitätsprüfung gestartet...");
                await _aiOrchestrator.RefreshAvailabilityAsync(CancellationToken.None);

                var searchOption = IncludeSubfoldersBox.IsChecked == true ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
                var orderPdfForCheck = ResolveOrderVerificationPdfPath(pdfFolder);
                var pdfs = LoadPdfCandidates(pdfFolder, searchOption, orderPdfForCheck).Select(ToAiPdfInfo).ToList();
                var excelRows = ReadExcelRows(excelPath).Select(ToAiExcelRowInfo).ToList();

                _lastQualityReport = await _aiQualityCheckService.AnalyzeAsync(pdfs, excelRows, CancellationToken.None);
                _aiChatService.SetLastQualityReport(_lastQualityReport);
                _aiChatService.SetAppContext(BuildAiAppContext());

                var reportText = _lastQualityReport.ToDisplayText();
                AppendStatus(reportText);
                UpdateAiStatusUi();
            }
            catch (Exception ex)
            {
                AppendStatus("KI-Qualitätsprüfung fehlgeschlagen: " + ex.Message);
            }
        }

        private void AiOpenChat_Click(object sender, RoutedEventArgs e)
        {
            OpenAiChatWindow(null);
        }

        private void Window_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == System.Windows.Input.Key.K
                && (System.Windows.Input.Keyboard.Modifiers & System.Windows.Input.ModifierKeys.Control) == System.Windows.Input.ModifierKeys.Control
                && !e.Handled)
            {
                e.Handled = true;
                OpenAiChatWindow(null);
            }
        }

        private void OpenAiChatWindow(string initialQuestion)
        {
            if (_aiChatWindow != null)
            {
                _aiChatWindow.Activate();
                if (!string.IsNullOrWhiteSpace(initialQuestion))
                    _aiChatWindow.AskQuestion(initialQuestion);
                return;
            }

            _aiChatWindow = new AiChatWindow(
                async (message, cancellationToken) =>
                {
                    if (_watermarkSettings == null)
                        _watermarkSettings = WatermarkSettingsStore.Load();

                    var command = AiAppCommandService.TryExecute(message, _watermarkSettings);
                    if (command.Executed)
                    {
                        WatermarkSettingsStore.Save(_watermarkSettings);
                        AppendStatus("KI-Befehl ausgeführt: " + command.Message.Replace("\r\n", " | "));
                        return "✅ " + command.Message;
                    }

                    _aiChatService.SetAppContext(BuildAiAppContext());
                    if (_lastQualityReport != null)
                        _aiChatService.SetLastQualityReport(_lastQualityReport);

                    var response = await _aiChatService.SendAsync(message, cancellationToken);
                    return response.Content;
                },
                () => _aiChatService.History,
                () => _aiOrchestrator.GetStatusText())
                {
                    Owner = this,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner
                };
                ThemeWindowService.Attach(_aiChatWindow);
                _aiChatWindow.Closed += (s, e) => _aiChatWindow = null;
                _aiChatWindow.Show();

            if (!string.IsNullOrWhiteSpace(initialQuestion))
                _aiChatWindow.AskQuestion(initialQuestion);
        }

        private static AiPdfInfo ToAiPdfInfo(PdfCandidate candidate)
        {
            return new AiPdfInfo
            {
                FullPath = candidate.FullPath,
                DisplayName = candidate.DisplayName,
                OriginalName = candidate.OriginalName,
                SearchKey = candidate.SearchKey,
                Order = candidate.Order
            };
        }

        private static AiExcelRowInfo ToAiExcelRowInfo(ExcelRowCandidate row)
        {
            return new AiExcelRowInfo
            {
                RowIndex = row.RowIndex,
                NormalizedCells = row.NormalizedCells ?? new List<string>(),
                RawDrawingNumber = row.RawDrawingNumber,
                DetectedArticleNumbers = row.DetectedArticleNumbers ?? new List<string>(),
                RequestedAmount = row.RequestedAmount,
                RequestedDeliveryDate = row.RequestedDeliveryDate,
                RequestedDeliveryLocation = row.RequestedDeliveryLocation
            };
        }

        private void ThemeToggle_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            _isDarkTheme = !_isDarkTheme;
            ApplyTheme(_isDarkTheme);
        }

        private void LoadXaml()
        {
            var assemblyName = Assembly.GetExecutingAssembly().GetName().Name;
            Application.LoadComponent(this, new Uri("/" + assemblyName + ";component/MainWindow.xaml", UriKind.Relative));
        }

        private void ApplyTheme(bool dark)
        {
            var res = Application.Current.Resources;

            var themeUri = new Uri(
                dark ? "PDF-umbenenen/Themes/DarkTheme.xaml" : "PDF-umbenenen/Themes/LightTheme.xaml",
                UriKind.Relative);

            var source = new ResourceDictionary { Source = themeUri };

            // Alle Brush-Schlüssel aus dem Quell-Dictionary in die App-Resources kopieren
            foreach (var k in source.Keys)
            {
                res[k] = source[k];
            }

            ThemeIconBox.Text = dark ? "🌙" : "☀";
            ThemeLabelBox.Text = dark ? "Dunkel" : "Hell";
            ThemeWindowService.RefreshAllWindows();
        }


        private void LoadSavedPaths()
        {
            var saved = PathStore.Load();

            var workspaceFolder = PathStore.GetOrCreateDefaultWorkspaceFolder();

            if (!string.IsNullOrWhiteSpace(saved.ExcelPath) && File.Exists(saved.ExcelPath))
            {
                ExcelPathBox.Text = saved.ExcelPath;
            }
            else
            {
                var resolvedExcelPath = ExcelDeliveryDateMergeService.ResolvePrimaryExcelPath(workspaceFolder);
                if (!string.IsNullOrWhiteSpace(resolvedExcelPath))
                {
                    ExcelPathBox.Text = resolvedExcelPath;
                    PathStore.SaveExcel(resolvedExcelPath);
                }
            }

            if (!string.IsNullOrWhiteSpace(saved.OrderVerificationPdfPath) && File.Exists(saved.OrderVerificationPdfPath))
            {
                _orderVerificationPdfPath = saved.OrderVerificationPdfPath;
            }
            else
            {
                _orderVerificationPdfPath = string.Empty;
                _cachedOrderVerificationLines = null;
            }

            UpdateOrderVerificationPdfDisplay();

            // PDF-Ordner ist fest auf den Ablage-Ordner gesetzt
            if (!Directory.Exists(workspaceFolder))
            {
                Directory.CreateDirectory(workspaceFolder);
            }

            PdfFolderBox.Text = workspaceFolder;
            PathStore.SaveFolder(workspaceFolder);
        }

        private void EnsureDesktopShortcut()
        {
            try
            {
                var exePath = Assembly.GetExecutingAssembly().Location;
                if (string.IsNullOrWhiteSpace(exePath) || !File.Exists(exePath))
                {
                    AppendStatus("⚠ Desktop-Verknüpfung: Programmdatei nicht gefunden.");
                    return;
                }

                var targetExe = AppInfo.GetInstalledExePath();
                var baseDir = Path.GetDirectoryName(targetExe);

                if (DesktopShortcutService.TryCreate(targetExe, out var message))
                {
                    AppendStatus("✓ Desktop-Verknüpfung erstellt: " + AppInfo.ShortcutFileName);
                    return;
                }

                AppendStatus("⚠ Desktop-Verknüpfung konnte nicht erstellt werden: " + message);
            }
            catch (Exception ex)
            {
                AppendStatus("⚠ Desktop-Verknüpfung konnte nicht erstellt werden: " + ex.Message);
            }
        }

        private static string ResolveDesktopPath()
        {
            return DesktopShortcutService.ResolveDesktopPath();
        }

        private void BrowseExcel_Click(object sender, RoutedEventArgs e)
        {
            var initialDirectory = PathStore.GetOrCreateDefaultWorkspaceFolder();
            if (!string.IsNullOrWhiteSpace(ExcelPathBox.Text) && File.Exists(ExcelPathBox.Text))
            {
                initialDirectory = Path.GetDirectoryName(ExcelPathBox.Text);
            }

            var dialog = new OpenFileDialog
            {
                Filter = "Excel-Dateien (*.xlsx;*.xls)|*.xlsx;*.xls|Alle Dateien (*.*)|*.*",
                Title = "Excel-Datei auswählen",
                InitialDirectory = initialDirectory
            };

            if (dialog.ShowDialog() == true)
            {
                ExcelPathBox.Text = dialog.FileName;
                PathStore.SaveExcel(dialog.FileName);
            }
        }

        private void BrowseFolder_Click(object sender, RoutedEventArgs e)
        {
            var fixedFolder = PathStore.GetOrCreateDefaultWorkspaceFolder();
            if (!Directory.Exists(fixedFolder))
            {
                Directory.CreateDirectory(fixedFolder);
            }

            PdfFolderBox.Text = fixedFolder;
            PathStore.SaveFolder(fixedFolder);
            AppendStatus("ℹ Fester Ablage-Ordner: " + fixedFolder);
        }

        private void Window_PreviewDragEnter(object sender, DragEventArgs e)
        {
            SetFileDropEffects(e);
            UpdateDragDropPreview(e);
        }

        private void Window_PreviewDragOver(object sender, DragEventArgs e)
        {
            SetFileDropEffects(e);
            UpdateDragDropPreview(e);
        }

        private void Window_DragEnter(object sender, DragEventArgs e)
        {
            SetFileDropEffects(e);
            UpdateDragDropPreview(e);
        }

        private void Window_DragOver(object sender, DragEventArgs e)
        {
            SetFileDropEffects(e);
            UpdateDragDropPreview(e);
        }

        private void Window_PreviewDragLeave(object sender, DragEventArgs e)
        {
            var pos = e.GetPosition(this);
            if (pos.X < 0 || pos.Y < 0 || pos.X > ActualWidth || pos.Y > ActualHeight)
                RefreshDroppedFilesDisplay();
        }

        private static void SetFileDropEffects(DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                e.Effects = DragDropEffects.Copy;
            }
            else
            {
                e.Effects = DragDropEffects.None;
            }

            e.Handled = true;
        }

        private void Window_PreviewDrop(object sender, DragEventArgs e)
        {
            if (IsOverOrderVerificationDropZone(e))
                return;

            HandleFileDrop(e);
        }

        private void Window_Drop(object sender, DragEventArgs e)
        {
            if (IsOverOrderVerificationDropZone(e))
                return;

            HandleFileDrop(e);
        }

        private bool IsOverOrderVerificationDropZone(DragEventArgs e)
        {
            var zone = OrderVerificationDropZone;
            if (zone == null || zone.ActualWidth <= 0 || zone.ActualHeight <= 0)
                return false;

            var pos = e.GetPosition(zone);
            return pos.X >= 0 && pos.Y >= 0 && pos.X <= zone.ActualWidth && pos.Y <= zone.ActualHeight;
        }

        private bool HasOrderVerificationPdfLoaded()
        {
            return !string.IsNullOrWhiteSpace(_orderVerificationPdfPath) && File.Exists(_orderVerificationPdfPath);
        }

        private void OrderVerificationDropZone_PreviewDragEnter(object sender, DragEventArgs e)
        {
            SetOrderVerificationDropEffects(e);
            HighlightOrderVerificationDropZone(true);
        }

        private void OrderVerificationDropZone_PreviewDragOver(object sender, DragEventArgs e)
        {
            SetOrderVerificationDropEffects(e);
        }

        private void OrderVerificationDropZone_PreviewDragLeave(object sender, DragEventArgs e)
        {
            HighlightOrderVerificationDropZone(false);
        }

        private void OrderVerificationDropZone_PreviewDrop(object sender, DragEventArgs e)
        {
            HighlightOrderVerificationDropZone(false);

            if (!e.Data.GetDataPresent(DataFormats.FileDrop))
                return;

            var files = e.Data.GetData(DataFormats.FileDrop) as string[];
            var pdfPath = FindFirstPdfPath(files, IncludeSubfoldersBox.IsChecked == true);
            if (string.IsNullOrWhiteSpace(pdfPath))
            {
                AppendStatus("⚠ Für Prüf-PDF bitte genau eine PDF-Datei hier ablegen.");
                e.Handled = true;
                return;
            }

            LoadOrderVerificationPdfFromSource(pdfPath);
            e.Handled = true;
        }

        private void SetOrderVerificationDropEffects(DragEventArgs e)
        {
            var files = e.Data.GetDataPresent(DataFormats.FileDrop)
                ? e.Data.GetData(DataFormats.FileDrop) as string[]
                : null;

            e.Effects = FindFirstPdfPath(files, false) != null
                ? DragDropEffects.Copy
                : DragDropEffects.None;
            e.Handled = true;
        }

        private void HighlightOrderVerificationDropZone(bool active)
        {
            var zone = OrderVerificationDropZone;
            if (zone == null)
                return;

            zone.BorderBrush = active
                ? (System.Windows.Media.Brush)FindResource("AccentBrush")
                : (System.Windows.Media.Brush)FindResource("GroupBorder");
            zone.Background = active
                ? (System.Windows.Media.Brush)FindResource("InputBackground")
                : System.Windows.Media.Brushes.Transparent;
        }

        private static string FindFirstPdfPath(string[] paths, bool includeSubfolders)
        {
            if (paths == null || paths.Length == 0)
                return null;

            var searchOption = includeSubfolders ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;

            foreach (var path in paths)
            {
                if (string.IsNullOrWhiteSpace(path))
                    continue;

                if (File.Exists(path)
                    && string.Equals(Path.GetExtension(path), ".pdf", StringComparison.OrdinalIgnoreCase))
                {
                    return path;
                }

                if (Directory.Exists(path))
                {
                    var pdf = Directory.GetFiles(path, "*.pdf", searchOption).FirstOrDefault();
                    if (!string.IsNullOrWhiteSpace(pdf))
                        return pdf;
                }
            }

            return null;
        }

        private void LoadOrderVerificationPdfFromSource(string sourceFile)
        {
            if (string.IsNullOrWhiteSpace(sourceFile) || !File.Exists(sourceFile))
            {
                AppendStatus("⚠ Prüf-PDF-Datei nicht gefunden.");
                return;
            }

            if (!string.Equals(Path.GetExtension(sourceFile), ".pdf", StringComparison.OrdinalIgnoreCase))
            {
                AppendStatus("⚠ Für Prüf-PDF wird eine PDF-Datei benötigt.");
                return;
            }

            var workspaceFolder = PathStore.GetOrCreateDefaultWorkspaceFolder();
            Directory.CreateDirectory(workspaceFolder);
            var targetPath = OrderVerificationFileHelper.GetCanonicalPath(workspaceFolder);
            File.Copy(sourceFile, targetPath, true);

            _orderVerificationPdfPath = targetPath;
            UpdateOrderVerificationPdfDisplay();
            PathStore.SaveOrderVerificationPdf(targetPath);
            PdfFolderBox.Text = workspaceFolder;
            PathStore.SaveFolder(workspaceFolder);
            AppendStatus("✓ Bestell-PDF geladen: " + Path.GetFileName(targetPath));
        }

        private void UpdateOrderVerificationPdfDisplay()
        {
            var block = OrderVerificationPdfStatusBlock;
            if (block == null)
                return;

            if (!HasOrderVerificationPdfLoaded())
            {
                block.Text = OrderVerificationDropHint;
                block.Foreground = (System.Windows.Media.Brush)FindResource("TextSecondary");
                return;
            }

            block.Text = "✓ " + Path.GetFileName(_orderVerificationPdfPath) + " geladen";
            block.Foreground = (System.Windows.Media.Brush)FindResource("TextPrimary");
        }

        private void ResetOrderVerificationPdfDisplay()
        {
            _orderVerificationPdfPath = string.Empty;
            _cachedOrderVerificationLines = null;
            PathStore.SaveOrderVerificationPdf(string.Empty);
            UpdateOrderVerificationPdfDisplay();
        }

        private void HandleFileDrop(DragEventArgs e)
        {
            if (e.Handled)
                return;

            if (!e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                AppendStatus("⚠ Drop erkannt, aber keine Dateien enthalten.");
                return;
            }

            var files = e.Data.GetData(DataFormats.FileDrop) as string[];
            if (files == null || files.Length == 0)
            {
                AppendStatus("⚠ Keine Dateipfade im Drop erhalten.");
                return;
            }

            e.Handled = true;

            var targetFolder = PathStore.GetOrCreateDefaultWorkspaceFolder();
            Directory.CreateDirectory(targetFolder);

            var copiedCount = 0;
            var ignoredCount = 0;
            var excelCopied = false;
            var searchOption = IncludeSubfoldersBox.IsChecked == true
                ? SearchOption.AllDirectories
                : SearchOption.TopDirectoryOnly;

            foreach (var sourcePath in files)
            {
                try
                {
                    if (Directory.Exists(sourcePath))
                    {
                        foreach (var file in Directory.GetFiles(sourcePath, "*.*", searchOption))
                        {
                            copiedCount += CopySupportedFileToWorkspace(file, targetFolder, ref excelCopied, ref ignoredCount);
                        }

                        continue;
                    }

                    copiedCount += CopySupportedFileToWorkspace(sourcePath, targetFolder, ref excelCopied, ref ignoredCount);
                }
                catch (Exception ex)
                {
                    AppendStatus("⚠ Fehler beim Kopieren: " + ex.Message);
                }
            }

            PdfFolderBox.Text = targetFolder;
            PathStore.SaveFolder(targetFolder);

            if (excelCopied)
            {
                var autoExcel = ExcelDeliveryDateMergeService.ResolvePrimaryExcelPath(targetFolder);
                if (!string.IsNullOrWhiteSpace(autoExcel))
                {
                    ExcelPathBox.Text = autoExcel;
                    PathStore.SaveExcel(autoExcel);
                }
            }

            if (copiedCount == 0)
            {
                AppendStatus("⚠ Keine unterstützten Dateien erkannt. Erlaubt: PDF, Excel (.xlsx/.xls), ZIP, Ordner mit diesen Dateien.");
                if (ignoredCount > 0)
                {
                    AppendStatus($"ℹ {ignoredCount} nicht unterstützte Datei(en) übersprungen.");
                }

                RefreshDroppedFilesDisplay();
                return;
            }

            AppendStatus($"📥 Drag & Drop: {copiedCount} Datei(en) kopiert, {ignoredCount} übersprungen.");
            if (excelCopied)
            {
                var loaded = ExcelPathBox.Text.Trim();
                if (!string.IsNullOrWhiteSpace(loaded))
                {
                    var folder = Path.GetDirectoryName(loaded);
                    if (ExcelDeliveryDateMergeService.HasSeparateAngebotExcel(folder, loaded))
                        AppendStatus("ℹ Zwei Excel-Dateien: Standard + Angebot (Liefertermine mit Vorrang).");
                    else
                        AppendStatus("ℹ Excel geladen: " + Path.GetFileName(loaded));
                }
            }

            var pdfCount = Directory.GetFiles(targetFolder, "*.pdf", SearchOption.TopDirectoryOnly).Length;
            AppendStatus($"ℹ Ablage-Ordner enthält jetzt {pdfCount} PDF-Datei(en).");
            RefreshDroppedFilesDisplay();
        }

        private void RefreshDroppedFilesDisplay()
        {
            var block = DroppedExtensionsBlock;
            if (block == null)
                return;

            var folder = PdfFolderBox?.Text?.Trim();
            if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
            {
                block.Text = "— noch keine Dateien —";
                return;
            }

            var searchOption = IncludeSubfoldersBox?.IsChecked == true
                ? SearchOption.AllDirectories
                : SearchOption.TopDirectoryOnly;

            var files = Directory.GetFiles(folder, "*.*", searchOption);
            block.Text = FormatWorkspaceFileSummary(files, folder, "— noch keine Dateien —");
        }

        private static string FormatWorkspaceFileSummary(
            IEnumerable<string> files,
            string workspaceFolder,
            string emptyText)
        {
            var parts = new List<string>();
            AppendExcelWorkspaceEntry(parts, workspaceFolder, "Daten.xlsx", "Daten.xls", "Excel Standard");
            AppendExcelWorkspaceEntry(parts, workspaceFolder, "Daten_Angebot.xlsx", "Daten_Angebot.xls", "Excel Angebot");

            var reservedExcelNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "Daten.xlsx", "Daten.xls", "Daten_Angebot.xlsx", "Daten_Angebot.xls"
            };

            var otherExtensions = files
                .Where(file => !reservedExcelNames.Contains(Path.GetFileName(file) ?? string.Empty))
                .Select(file => Path.GetExtension(file)?.ToLowerInvariant() ?? string.Empty)
                .Where(ext => !string.IsNullOrWhiteSpace(ext));

            var extensionSummary = FormatExtensionSummary(otherExtensions, string.Empty);
            if (!string.IsNullOrWhiteSpace(extensionSummary))
                parts.Add(extensionSummary);

            return parts.Count == 0 ? emptyText : string.Join("    ", parts);
        }

        private static void AppendExcelWorkspaceEntry(
            ICollection<string> parts,
            string folder,
            string xlsxName,
            string xlsName,
            string label)
        {
            var xlsxPath = Path.Combine(folder, xlsxName);
            if (File.Exists(xlsxPath))
            {
                parts.Add(label + " (" + xlsxName + ")");
                return;
            }

            var xlsPath = Path.Combine(folder, xlsName);
            if (File.Exists(xlsPath))
                parts.Add(label + " (" + xlsName + ")");
        }

        private void UpdateDragDropPreview(DragEventArgs e)
        {
            var block = DroppedExtensionsBlock;
            if (block == null || !e.Data.GetDataPresent(DataFormats.FileDrop))
                return;

            var files = e.Data.GetData(DataFormats.FileDrop) as string[];
            if (files == null || files.Length == 0)
                return;

            var includeSubfolders = IncludeSubfoldersBox?.IsChecked == true;
            var extensions = CollectExtensionsFromPaths(files, includeSubfolders);
            var excelHints = CollectExcelDropHints(files);
            var summary = FormatExtensionSummary(extensions, string.Empty);
            if (!string.IsNullOrWhiteSpace(excelHints))
                summary = string.IsNullOrWhiteSpace(summary) ? excelHints : excelHints + "    " + summary;

            block.Text = string.IsNullOrWhiteSpace(summary)
                ? "Keine unterstützten Dateiendungen erkannt"
                : "Wird eingefügt: " + summary;
        }

        private static string CollectExcelDropHints(IEnumerable<string> paths)
        {
            var hints = new List<string>();
            var hasStandard = false;
            var hasAngebot = false;

            foreach (var path in paths)
            {
                if (!File.Exists(path))
                    continue;

                var ext = Path.GetExtension(path)?.ToLowerInvariant() ?? string.Empty;
                if (ext != ".xlsx" && ext != ".xls")
                    continue;

                if (ExcelDeliveryDateMergeService.IsAngebotExcelFile(path))
                    hasAngebot = true;
                else
                    hasStandard = true;
            }

            if (hasStandard)
                hints.Add("Excel Standard → Daten.xlsx");
            if (hasAngebot)
                hints.Add("Excel Angebot → Daten_Angebot.xlsx");

            return string.Join("    ", hints);
        }

        private static IEnumerable<string> CollectExtensionsFromPaths(IEnumerable<string> paths, bool includeSubfolders)
        {
            var searchOption = includeSubfolders ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
            var extensions = new List<string>();

            foreach (var path in paths)
            {
                if (Directory.Exists(path))
                {
                    foreach (var file in Directory.GetFiles(path, "*.*", searchOption))
                    {
                        var ext = Path.GetExtension(file)?.ToLowerInvariant();
                        if (!string.IsNullOrWhiteSpace(ext))
                            extensions.Add(ext);
                    }

                    continue;
                }

                if (!File.Exists(path))
                    continue;

                var fileExt = Path.GetExtension(path)?.ToLowerInvariant();
                if (!string.IsNullOrWhiteSpace(fileExt))
                    extensions.Add(fileExt);
            }

            return extensions;
        }

        private static string FormatExtensionSummary(IEnumerable<string> extensions, string emptyText)
        {
            var groups = extensions
                .Where(ext => !string.IsNullOrWhiteSpace(ext))
                .GroupBy(ext => ext, StringComparer.OrdinalIgnoreCase)
                .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.Key + " ×" + group.Count())
                .ToList();

            return groups.Count == 0 ? emptyText : string.Join("    ", groups);
        }

        private int CopySupportedFileToWorkspace(string sourceFile, string targetFolder, ref bool excelCopied, ref int ignoredCount)
        {
            if (!File.Exists(sourceFile))
            {
                return 0;
            }

            var ext = Path.GetExtension(sourceFile)?.ToLowerInvariant() ?? string.Empty;
            if (ext == ".xlsx" || ext == ".xls")
            {
                var baseName = Path.GetFileNameWithoutExtension(sourceFile) ?? string.Empty;
                var targetName = ExcelDeliveryDateMergeService.IsAngebotExcelFile(sourceFile)
                    ? (ext == ".xls" ? "Daten_Angebot.xls" : "Daten_Angebot.xlsx")
                    : (ext == ".xls" ? "Daten.xls" : "Daten.xlsx");
                var excelTarget = AppSecurityService.ResolveSafeTargetPath(targetFolder, targetName);
                File.Copy(sourceFile, excelTarget, true);
                excelCopied = true;
                return 1;
            }

            if (ext == ".pdf")
            {
                var safeName = AppSecurityService.SanitizeFileName(Path.GetFileName(sourceFile));
                var targetPath = AppSecurityService.ResolveSafeTargetPath(targetFolder, safeName);
                targetPath = GetAvailableFilePath(targetPath);
                File.Copy(sourceFile, targetPath, false);
                return 1;
            }

            if (ext == ".zip")
            {
                return ExtractSupportedFilesFromZipToWorkspace(sourceFile, targetFolder, ref excelCopied, ref ignoredCount);
            }

            ignoredCount++;
            return 0;
        }

        private int ExtractSupportedFilesFromZipToWorkspace(string zipFile, string targetFolder, ref bool excelCopied, ref int ignoredCount)
        {
            var copiedCount = 0;

            using (var archive = ZipFile.OpenRead(zipFile))
            {
                foreach (var entry in archive.Entries)
                {
                    if (string.IsNullOrWhiteSpace(entry.Name))
                    {
                        continue;
                    }

                    var ext = Path.GetExtension(entry.Name)?.ToLowerInvariant() ?? string.Empty;
                    if (ext == ".xlsx" || ext == ".xls")
                    {
                        var targetName = ExcelDeliveryDateMergeService.IsAngebotExcelFile(entry.Name)
                            ? (ext == ".xls" ? "Daten_Angebot.xls" : "Daten_Angebot.xlsx")
                            : (ext == ".xls" ? "Daten.xls" : "Daten.xlsx");
                        var excelTarget = AppSecurityService.ResolveSafeTargetPath(targetFolder, targetName);
                        entry.ExtractToFile(excelTarget, true);
                        excelCopied = true;
                        copiedCount++;
                        continue;
                    }

                    if (ext == ".pdf")
                    {
                        var safeName = AppSecurityService.SanitizeFileName(Path.GetFileName(entry.Name));
                        var targetPath = AppSecurityService.ResolveSafeTargetPath(targetFolder, safeName);
                        targetPath = GetAvailableFilePath(targetPath);
                        entry.ExtractToFile(targetPath, false);
                        copiedCount++;
                        continue;
                    }

                    ignoredCount++;
                }
            }

            return copiedCount;
        }

        private CancellationTokenSource _processingCancellationSource;
        private Stopwatch _processingStopwatch;
        private readonly DispatcherTimer _stopwatchTimer;
        private TimeSpan? _lastRunElapsed;

        private async void MergeAndPrint_Click(object sender, RoutedEventArgs e)
        {
            var pdfFolder = PdfFolderBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(pdfFolder) || !Directory.Exists(pdfFolder))
            {
                AppendStatus("Bitte einen gültigen PDF-Ordner auswählen.");
                return;
            }

            if (!PdfWatermarkService.IsAvailable())
            {
                AppendStatus("❌ Zusammenführen nicht möglich: iTextSharp fehlt im App-Ordner.");
                AppendStatus("ℹ Benötigt: itextsharp.dll und BouncyCastle.Cryptography.dll neben der EXE.");
                return;
            }

            var searchOption = IncludeSubfoldersBox.IsChecked == true ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;

            try
            {
                SetProcessingState(true);
                AppendStatus("Zusammenführen gestartet...");

                var mergeResult = await Task.Run(() =>
                {
                    var orderedFiles = GetMergeCandidateFiles(pdfFolder, searchOption);
                    if (orderedFiles.Count == 0)
                    {
                        throw new InvalidOperationException(
                            "Keine bearbeiteten PDF-Dateien gefunden (z. B. 01_..., 02_...). " +
                            "Bitte zuerst 'Sortieren & umbenennen' ausführen.");
                    }

                    var outputPath = Path.Combine(
                        pdfFolder,
                        "_GESAMT_" + DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture) + ".pdf");

                    PdfWatermarkService.MergePdfFiles(orderedFiles, outputPath);
                    var pageMap = MergedPdfPageMapService.Build(orderedFiles);
                    MergedPdfPageMapService.Save(outputPath, pageMap);
                    return new { OutputPath = outputPath, OrderedFiles = orderedFiles, PageMap = pageMap };
                }).ConfigureAwait(true);

                _lastMergedPdfPath = mergeResult.OutputPath;
                AppendStatus("✓ Zusammengeführt: " + Path.GetFileName(mergeResult.OutputPath));
                AppendStatus("👁 Gesamt-PDF Vorschau — „Gesamt-PDF öffnen“ zum Drucken im PDF-Reader.");
                OpenMergedPdfPreview(mergeResult.OutputPath, mergeResult.OrderedFiles, pdfFolder, mergeResult.PageMap);
            }
            catch (Exception ex)
            {
                AppendStatus("Fehler beim Zusammenführen/Vorschau: " + ex.Message);
            }
            finally
            {
                SetProcessingState(false);
            }
        }

        private static List<string> GetMergeCandidateFiles(string pdfFolder, SearchOption searchOption)
        {
            var numbered = Directory.GetFiles(pdfFolder, "*.pdf", searchOption)
                .Where(IsMergeCandidateFile)
                .Where(path => Regex.IsMatch(Path.GetFileName(path), @"^\d+_.+\.pdf$", RegexOptions.IgnoreCase))
                .OrderBy(path => GetLeadingNumber(Path.GetFileNameWithoutExtension(path)))
                .ThenBy(path => Path.GetFileName(path), StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (numbered.Count > 0)
                return numbered;

            return Directory.GetFiles(pdfFolder, "*.pdf", searchOption)
                .Where(IsMergeCandidateFile)
                .OrderBy(path => GetLeadingNumber(Path.GetFileNameWithoutExtension(path)))
                .ThenBy(path => Path.GetFileName(path), StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static bool IsMergeCandidateFile(string path)
        {
            var name = Path.GetFileName(path);
            if (string.IsNullOrWhiteSpace(name))
                return false;

            if (name.StartsWith("_GESAMT_", StringComparison.OrdinalIgnoreCase))
                return false;

            if (name.EndsWith(WatermarkEditorService.PreWatermarkSuffix, StringComparison.OrdinalIgnoreCase))
                return false;

            return true;
        }

        private static int GetLeadingNumber(string fileNameWithoutExtension)
        {
            if (string.IsNullOrWhiteSpace(fileNameWithoutExtension))
                return int.MaxValue;

            var i = 0;
            while (i < fileNameWithoutExtension.Length && char.IsDigit(fileNameWithoutExtension[i])) i++;
            if (i == 0) return int.MaxValue;

            var token = fileNameWithoutExtension.Substring(0, i);
            return int.TryParse(token, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : int.MaxValue;
        }

        private void OpenMergedPdfPreview(
            string mergedPdfPath,
            IList<string> orderedFiles,
            string pdfFolder,
            List<MergedPdfPageEntry> pageMap = null)
        {
            if (string.IsNullOrWhiteSpace(mergedPdfPath) || !File.Exists(mergedPdfPath))
            {
                AppendStatus("⚠ Vorschau fehlgeschlagen: PDF-Datei nicht gefunden.");
                return;
            }

            var preview = new PdfMergedPreviewWindow(mergedPdfPath, orderedFiles, pdfFolder, pageMap)
            {
                WindowStartupLocation = WindowStartupLocation.CenterScreen
            };
            ThemeWindowService.Attach(preview);
            preview.Closed += (_, __) =>
            {
                if (!string.IsNullOrWhiteSpace(preview.MergedPdfPath))
                    _lastMergedPdfPath = preview.MergedPdfPath;
            };
            preview.Show();
        }

        private void OpenPdfPreview(string pdfPath)
        {
            if (string.IsNullOrWhiteSpace(pdfPath) || !File.Exists(pdfPath))
            {
                AppendStatus("⚠ Vorschau fehlgeschlagen: PDF-Datei nicht gefunden.");
                return;
            }

            Exception lastError = null;

            try
            {
                var defaultStart = new ProcessStartInfo
                {
                    FileName = pdfPath,
                    UseShellExecute = true
                };

                if (Process.Start(defaultStart) != null)
                {
                    AppendStatus("👁 Vorschau geöffnet. Bitte im PDF-Viewer drucken (Strg+P) und Drucker auswählen.");
                    return;
                }
            }
            catch (Exception ex)
            {
                lastError = ex;
            }

            var fallbackApps = new[]
            {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Adobe", "Acrobat DC", "Acrobat", "Acrobat.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Adobe", "Acrobat Reader DC", "Reader", "AcroRd32.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Microsoft", "Edge", "Application", "msedge.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Microsoft", "Edge", "Application", "msedge.exe")
            };

            foreach (var appPath in fallbackApps)
            {
                if (!File.Exists(appPath))
                    continue;

                try
                {
                    var psi = new ProcessStartInfo
                    {
                        FileName = appPath,
                        Arguments = "\"" + pdfPath + "\"",
                        UseShellExecute = false
                    };

                    if (Process.Start(psi) != null)
                    {
                        AppendStatus("👁 Vorschau geöffnet mit: " + Path.GetFileName(appPath));
                        return;
                    }
                }
                catch (Exception ex)
                {
                    lastError = ex;
                }
            }

            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = "/select,\"" + pdfPath + "\"",
                    UseShellExecute = true
                });
                AppendStatus("⚠ Kein PDF-Viewer gefunden. Datei im Explorer markiert:");
                AppendStatus("ℹ " + pdfPath);
                if (lastError != null)
                    AppendStatus("ℹ Details: " + lastError.Message);
            }
            catch (Exception ex)
            {
                AppendStatus("⚠ Vorschau konnte nicht geöffnet werden: " + ex.Message);
                AppendStatus("ℹ Datei manuell öffnen: " + pdfPath);
            }
        }

        private async void Process_Click(object sender, RoutedEventArgs e)
        {
            var excelPath = ExcelPathBox.Text.Trim();
            var pdfFolder = PdfFolderBox.Text.Trim();
            var orderNumber = OrderNumberBox.Text.Trim();

            if (string.IsNullOrWhiteSpace(pdfFolder) || !Directory.Exists(pdfFolder))
            {
                AppendStatus("Bitte einen gültigen PDF-Ordner auswählen.");
                return;
            }

            var searchOption = IncludeSubfoldersBox.IsChecked == true ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
            var orderPdfPath = ResolveOrderVerificationPdfPath(pdfFolder);
            var hasOrderPdf = !string.IsNullOrWhiteSpace(orderPdfPath) && File.Exists(orderPdfPath);

            var pdfKeys = LoadPdfCandidates(pdfFolder, searchOption, orderPdfPath)
                .Select(p => p.SearchKey)
                .ToList();
            var detection = CustomerProfileDetectionService.Detect(pdfKeys, CustomerProfileStore.LoadAll());
            var activeCustomer = detection.Profile ?? CustomerProfileStore.GetBuiltInTesla();
            AppendStatus(string.Format(CultureInfo.InvariantCulture,
                "ℹ Kunde erkannt: {0} ({1}% Übereinstimmung)",
                activeCustomer.DisplayName,
                detection.ConfidencePercent));

            IList<OrderVerificationLine> orderVerificationLines = Array.Empty<OrderVerificationLine>();
            List<ExcelRowCandidate> excelRows;

            if (activeCustomer.RequiresExcel)
            {
                if (!TryResolveExcelPath(ref excelPath, AppendStatus))
                {
                    AppendStatus("Für " + activeCustomer.DisplayName + " wird eine Excel-Datei benötigt.");
                    AppendStatus("Tipp: Eine Excel (.xlsx/.xls) in den Ablage-Ordner ziehen — optional zweite mit *Angebot* im Namen.");
                    return;
                }

                excelRows = ReadExcelRows(excelPath).ToList();
                ApplyAngebotDeliveryDates(excelPath, excelRows, AppendStatus);
                if (excelRows.Count == 0)
                {
                    AppendStatus("Keine Zeichnungsnummern in der Excel-Datei gefunden.");
                    return;
                }

                if (hasOrderPdf)
                {
                    if (!TryRunOrderVerification(excelPath, orderPdfPath, excelRows, pdfFolder, searchOption, blockOnIssues: true))
                        return;

                    orderVerificationLines = _cachedOrderVerificationLines ?? Array.Empty<OrderVerificationLine>();
                }
                else
                {
                    orderPdfPath = string.Empty;
                    AppendStatus("ℹ Keine Prüf-PDF – Sortierung ohne Bestell-PDF-Abgleich.");
                }
            }
            else
            {
                excelPath = string.Empty;
                if (!hasOrderPdf)
                {
                    AppendStatus("Für " + activeCustomer.DisplayName + " wird die Bestell-PDF im Bereich „Prüf-PDF“ benötigt.");
                    return;
                }

                orderVerificationLines = CustomerOrderListParser.Parse(orderPdfPath, activeCustomer, _pdfTextService);
                if (orderVerificationLines.Count == 0)
                {
                    AppendStatus("In der Bestell-PDF wurden keine Zeichnungszeilen für " + activeCustomer.DisplayName + " gefunden.");
                    return;
                }

                excelRows = BuildExcelRowsFromOrderLines(orderVerificationLines);
                AppendStatus("ℹ Auftrag aus Bestell-PDF: " + excelRows.Count + " Position(en).");
            }

            // WICHTIG: UI-Wert auf dem UI-Thread speichern, BEVOR wir in den Background-Thread gehen!
            if (_watermarkSettings == null)
                _watermarkSettings = WatermarkSettingsStore.Load();
            var addWatermarks = _watermarkSettings.WatermarksEnabled;
            var excelRowsForThread = excelRows;
            var customerForThread = activeCustomer;

            try
            {
                SetProcessingState(true);
                _processingCancellationSource = new CancellationTokenSource();
                AppendStatus("Verarbeitung gestartet...");
                await RunOnStaThreadAsync(() => ProcessDocuments(
                    excelPath,
                    pdfFolder,
                    searchOption,
                    orderPdfPath,
                    orderVerificationLines,
                    _processingCancellationSource.Token,
                    SetProgress,
                    addWatermarks,
                    orderNumber,
                    excelRowsForThread,
                    customerForThread), _processingCancellationSource.Token);
            }
            catch (OperationCanceledException)
            {
                AppendStatus("Verarbeitung abgebrochen.");
            }
            catch (Exception ex)
            {
                AppendStatus("Fehler: " + ex.Message);
            }
            finally
            {
                _processingCancellationSource?.Dispose();
                _processingCancellationSource = null;
                SetProcessingState(false);
            }
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            _processingCancellationSource?.Cancel();
            AppendStatus("Abbruch angefordert...");
        }

        private void DeleteProcessedOrders_Click(object sender, RoutedEventArgs e)
        {
            var pdfFolder = PdfFolderBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(pdfFolder) || !Directory.Exists(pdfFolder))
            {
                AppendStatus("Bitte einen gültigen PDF-Ordner auswählen.");
                return;
            }

            var confirm = MessageBox.Show(
                "Soll der Ablage-Ordner komplett geleert werden?\n\n" +
                "Es werden gelöscht:\n" +
                "• Alle PDFs (bearbeitet, Ursprungs- und entpackte Dateien)\n" +
                "• Gesamt-PDF / Gesamtansicht (_GESAMT_...)\n" +
                "• Excel-Dateien (Daten.xlsx / Daten.xls)\n" +
                "• ZIP-Dateien\n" +
                "• Hilfsdateien (.dok-meta.xml, .map.xml, temporäre Dateien)\n" +
                "• Leere Unterordner\n\n" +
                "Hinweis: Offene PDF-Vorschau bitte vorher schließen.",
                "Bearbeitete Aufträge löschen",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (confirm != MessageBoxResult.Yes)
            {
                AppendStatus("Löschen abgebrochen.");
                return;
            }

            TryCloseDedicatedPdfViewers();
            var foldersToClean = GetWorkspaceFoldersToClean(pdfFolder);
            var result = CleanupWorkspaceFolders(foldersToClean);
            if (!string.IsNullOrWhiteSpace(ExcelPathBox.Text)
                && foldersToClean.Any(folder => IsPathInsideDirectory(ExcelPathBox.Text.Trim(), folder))
                && !File.Exists(ExcelPathBox.Text.Trim()))
            {
                ExcelPathBox.Text = string.Empty;
                PathStore.SaveExcel(string.Empty);
            }

            if (result.RemainingFiles.Count > 0)
            {
                foreach (var remaining in result.RemainingFiles.Take(5))
                    AppendStatus("⚠ Konnte nicht gelöscht werden: " + remaining);

                if (result.RemainingFiles.Count > 5)
                    AppendStatus("⚠ ... und " + (result.RemainingFiles.Count - 5) + " weitere Datei(en).");

                AppendStatus("ℹ Bitte PDF-Vorschau (Gesamtansicht) schließen und erneut löschen.");
            }
            else
            {
                _lastMergedPdfPath = null;
            }

            AppendStatus(string.Format(CultureInfo.InvariantCulture,
                "🧹 Ablage bereinigt: {0} Datei(en), {1} Ordner entfernt. Fehler: {2}.",
                result.DeletedFiles,
                result.RemovedDirectories,
                result.FailedFiles));
            ResetOrderVerificationPdfDisplay();
            RefreshDroppedFilesDisplay();
        }

        private static IEnumerable<string> GetWorkspaceFoldersToClean(string pdfFolder)
        {
            var folders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (!string.IsNullOrWhiteSpace(pdfFolder) && Directory.Exists(pdfFolder))
                folders.Add(Path.GetFullPath(pdfFolder));

            var defaultFolder = PathStore.GetOrCreateDefaultWorkspaceFolder();
            if (Directory.Exists(defaultFolder))
                folders.Add(Path.GetFullPath(defaultFolder));

            return folders;
        }

        private static WorkspaceCleanupResult CleanupWorkspaceFolders(IEnumerable<string> folders)
        {
            var result = new WorkspaceCleanupResult();
            var filesToDelete = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var folder in folders ?? Enumerable.Empty<string>())
            {
                if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
                    continue;

                foreach (var file in Directory.GetFiles(folder, "*.*", SearchOption.AllDirectories))
                {
                    if (ShouldDeleteWorkspaceFile(file))
                        filesToDelete.Add(Path.GetFullPath(file));
                }
            }

            var mergedFiles = filesToDelete
                .Where(IsMergedOverviewFilePath)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToList();
            var otherFiles = filesToDelete
                .Where(path => !IsMergedOverviewFilePath(path))
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToList();

            foreach (var file in mergedFiles.Concat(otherFiles))
            {
                if (TryDeleteFileWithRetry(file, IsMergedOverviewFilePath(file) ? 12 : 4))
                {
                    result.DeletedFiles++;
                    continue;
                }

                result.FailedFiles++;
                if (File.Exists(file))
                    result.RemainingFiles.Add(file);
            }

            foreach (var folder in (folders ?? Enumerable.Empty<string>())
                .Where(path => !string.IsNullOrWhiteSpace(path) && Directory.Exists(path)))
            {
                foreach (var directory in Directory.GetDirectories(folder, "*", SearchOption.AllDirectories)
                    .OrderByDescending(path => path.Length))
                {
                    try
                    {
                        if (!Directory.EnumerateFileSystemEntries(directory).Any())
                        {
                            Directory.Delete(directory);
                            result.RemovedDirectories++;
                        }
                    }
                    catch (Exception)
                    {
                        result.FailedFiles++;
                    }
                }
            }

            return result;
        }

        private static bool ShouldDeleteWorkspaceFile(string filePath)
        {
            var fileName = Path.GetFileName(filePath) ?? string.Empty;
            if (fileName.EndsWith(".dok-meta.xml", StringComparison.OrdinalIgnoreCase)
                || fileName.EndsWith(".map.xml", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            var extension = Path.GetExtension(filePath);
            if (string.IsNullOrWhiteSpace(extension))
                return false;

            return extension.Equals(".pdf", StringComparison.OrdinalIgnoreCase)
                || extension.Equals(".xlsx", StringComparison.OrdinalIgnoreCase)
                || extension.Equals(".xls", StringComparison.OrdinalIgnoreCase)
                || extension.Equals(".zip", StringComparison.OrdinalIgnoreCase)
                || extension.Equals(".tmp", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsMergedOverviewFilePath(string filePath)
        {
            var fileName = Path.GetFileName(filePath);
            if (string.IsNullOrWhiteSpace(fileName))
                return false;

            return fileName.StartsWith("_GESAMT_", StringComparison.OrdinalIgnoreCase)
                || fileName.IndexOf("gesamt", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool TryDeleteFileWithRetry(string filePath, int attempts)
        {
            if (string.IsNullOrWhiteSpace(filePath))
                return true;

            for (var attempt = 0; attempt < attempts; attempt++)
            {
                try
                {
                    if (!File.Exists(filePath))
                        return true;

                    var attributes = File.GetAttributes(filePath);
                    if ((attributes & FileAttributes.ReadOnly) != 0)
                        File.SetAttributes(filePath, attributes & ~FileAttributes.ReadOnly);

                    File.Delete(filePath);
                    if (!File.Exists(filePath))
                        return true;
                }
                catch (IOException)
                {
                    Thread.Sleep(350);
                }
                catch (UnauthorizedAccessException)
                {
                    Thread.Sleep(350);
                }
            }

            return !File.Exists(filePath);
        }

        private static void TryCloseDedicatedPdfViewers()
        {
            foreach (var processName in new[] { "AcroRd32", "Acrobat", "FoxitReader", "SumatraPDF" })
            {
                foreach (var process in Process.GetProcessesByName(processName))
                {
                    try
                    {
                        if (process.HasExited)
                            continue;

                        if (process.MainWindowHandle != IntPtr.Zero)
                            process.CloseMainWindow();

                        if (!process.WaitForExit(1500))
                            process.Kill();
                    }
                    catch
                    {
                        // Viewer konnte nicht geschlossen werden – Löschen versucht trotzdem mit Retry.
                    }
                    finally
                    {
                        process.Dispose();
                    }
                }
            }
        }

        private static bool IsPathInsideDirectory(string filePath, string directoryPath)
        {
            try
            {
                var fullFilePath = Path.GetFullPath(filePath);
                var fullDirectoryPath = Path.GetFullPath(directoryPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                    + Path.DirectorySeparatorChar);
                return fullFilePath.StartsWith(fullDirectoryPath, StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        private sealed class WorkspaceCleanupResult
        {
            public int DeletedFiles { get; set; }
            public int RemovedDirectories { get; set; }
            public int FailedFiles { get; set; }
            public List<string> RemainingFiles { get; } = new List<string>();
        }

        private void ProcessDocuments(
            string excelPath,
            string pdfFolder,
            SearchOption searchOption,
            string orderVerificationPdfPath,
            IList<OrderVerificationLine> orderVerificationLines,
            CancellationToken cancellationToken,
            Action<int> reportProgress,
            bool addWatermarks,
            string orderNumber,
            IList<ExcelRowCandidate> excelRowsPreloaded = null,
            CustomerProfile activeCustomerProfile = null)
        {
            // WICHTIG: Diese ganze Methode läuft auf einem separaten STA-Thread!
            // Daher nutzen wir SafeAppendStatus und SafeSetProgress!
            // Der addWatermarks Parameter wird NICHT vom UI-Thread abgerufen, sondern als Parameter übergeben!

            // Überprüfe iTextSharp-Status
            SafeAppendStatus("──────────────────────────────────");
            SafeAppendStatus("Wasserzeichen-Status: " + (PdfWatermarkService.IsAvailable() ? "✅ AKTIV" : "❌ iTextSharp nicht installiert"));
            SafeAppendStatus("──────────────────────────────────");

            SafeSetProgress(0);
            cancellationToken.ThrowIfCancellationRequested();

            var aiSettings = AiSettingsStore.Load();
            var watermarkSettings = WatermarkSettingsStore.Load();
            if (addWatermarks && watermarkSettings.ExtractMaterialThickness)
                SafeAppendStatus("Materialstärke: schnelle Regel-Erkennung aktiv (Raster nur bei Bedarf).");

            var pdfCandidates = LoadPdfCandidates(pdfFolder, searchOption, orderVerificationPdfPath).ToList();
            if (pdfCandidates.Count == 0)
            {
                SafeAppendStatus("Keine PDF-Dateien im ausgewählten Ordner gefunden.");
                return;
            }

            if (orderVerificationLines == null || orderVerificationLines.Count == 0)
            {
                SafeAppendStatus("ℹ Keine Prüf-PDF – Liefertermine aus Excel (falls vorhanden).");
                orderVerificationLines = Array.Empty<OrderVerificationLine>();
            }

            if (activeCustomerProfile != null)
                SafeAppendStatus("Aktives Kundenprofil: " + activeCustomerProfile.DisplayName);

            cancellationToken.ThrowIfCancellationRequested();
            var excelRows = excelRowsPreloaded?.ToList() ?? ReadExcelRows(excelPath).ToList();
            if (excelRowsPreloaded == null)
                ApplyAngebotDeliveryDates(excelPath, excelRows, SafeAppendStatus);
            if (excelRows.Count == 0)
            {
                SafeAppendStatus("Keine Zeichnungsnummern in der Excel-Datei gefunden.");
                return;
            }

            // --- Diagnose: erste 5 PDF-Schlüssel und erste 5 Excel-Zellen anzeigen ---
            // Sammle die Diagnose-Nachrichten OHNE auf UI zuzugreifen
            var diagnosticMessages = new List<string> { "── Diagnose ──", "PDF-Schlüssel (erste 5):" };
            foreach (var p in pdfCandidates.Take(5))
                diagnosticMessages.Add("  PDF  roh=«" + p.OriginalName + "»  key=«" + p.SearchKey + "»");

            diagnosticMessages.Add("Excel-Zellen (erste 5 Zeilen, bis 3 Zellen je Zeile):");
            foreach (var r in excelRows.Take(5))
                diagnosticMessages.Add("  Zeile " + r.RowIndex + ": " + string.Join(" | ", r.NormalizedCells.Take(3).Select(c => "«" + c + "»")));
            diagnosticMessages.Add("──────────────");

            // Gebe alle Nachrichten auf UI-Thread aus
            foreach (var msg in diagnosticMessages)
                SafeAppendStatus(msg);

            var articleToBgInfo = excelRows
                .Where(r => !string.IsNullOrWhiteSpace(r.RawDrawingNumber)
                            && IsLikelyDrawingNumber(r.RawDrawingNumber)
                            && r.DetectedArticleNumbers != null
                            && r.DetectedArticleNumbers.Count > 0)
                .SelectMany(r => r.DetectedArticleNumbers.Select(a => new
                {
                    Article = a,
                    Drawing = r.RawDrawingNumber,
                    Revision = r.RevisionNumber,
                    HasParent = !string.IsNullOrWhiteSpace(r.ParentArticleNumber)
                }))
                .GroupBy(x => x.Article)
                .ToDictionary(
                    g => g.Key,
                    g =>
                    {
                        var pick = g.OrderBy(x => x.HasParent ? 1 : 0).First();
                        return new BgExcelInfo
                        {
                            DrawingNumber = pick.Drawing,
                            RevisionNumber = pick.Revision
                        };
                    });

            var matches = new List<RenamingItem>();
            var unmatchedPdfs = new List<string>();
            var aiExcelRows = excelRows.Select(ToAiExcelRowInfo).ToList();
            var aiEnabled = _aiOrchestrator.Settings.AiEnabled;
            var pdfCandidateCount = Math.Max(1, pdfCandidates.Count);
            var pdfMatchIndex = 0;

            foreach (var pdf in pdfCandidates)
            {
                cancellationToken.ThrowIfCancellationRequested();
                pdfMatchIndex++;
                SafeSetProgressFraction(0.07 * pdfMatchIndex / pdfCandidateCount, "PDFs zuordnen …");
                var matchingRows = excelRows
                    .Where(row => row.NormalizedCells.Any(cell => cell == pdf.SearchKey))
                    .OrderBy(row => row.RowIndex)
                    .ToList();

                if (matchingRows.Count == 0 && aiEnabled)
                {
                    var aiPdf = ToAiPdfInfo(pdf);
                    var suggestion = _aiMatchingService
                        .SuggestMatchAsync(aiPdf, aiExcelRows, cancellationToken)
                        .GetAwaiter()
                        .GetResult();

                    if (suggestion != null && suggestion.IsAutoAccepted)
                    {
                        var aiRow = excelRows.FirstOrDefault(r => r.RowIndex == suggestion.ExcelRowIndex);
                        if (aiRow != null)
                        {
                            matchingRows = new List<ExcelRowCandidate> { aiRow };
                            SafeAppendStatus(string.Format(CultureInfo.InvariantCulture,
                                "🤖 KI-Zuordnung: {0} -> Zeile {1} ({2:P0})",
                                pdf.DisplayName, suggestion.ExcelRowIndex, suggestion.Confidence));
                        }
                    }
                    else if (suggestion != null && suggestion.IsWarning)
                    {
                        SafeAppendStatus(string.Format(CultureInfo.InvariantCulture,
                            "⚠ Unsichere KI-Zuordnung übersprungen: {0} ({1:P0})",
                            pdf.DisplayName, suggestion.Confidence));
                    }
                }

                if (matchingRows.Count == 0)
                {
                    unmatchedPdfs.Add(pdf.DisplayName);
                    continue;
                }

                var primaryRow = matchingRows[0];
                var orderRowForPdf = ToOrderExcelRow(primaryRow);
                var watermarkEntries = matchingRows
                    .Select(r =>
                    {
                        BgExcelInfo bgInfo = null;
                        if (!string.IsNullOrWhiteSpace(r.ParentArticleNumber))
                            articleToBgInfo.TryGetValue(r.ParentArticleNumber, out bgInfo);

                        var bgDrawing = bgInfo?.DrawingNumber ?? string.Empty;
                        if (!IsLikelyDrawingNumber(bgDrawing))
                            bgDrawing = string.Empty;

                        var bgRevision = bgInfo?.RevisionNumber;
                        if (string.IsNullOrWhiteSpace(bgRevision) && !string.IsNullOrWhiteSpace(bgDrawing))
                        {
                            var bgRow = excelRows.FirstOrDefault(row =>
                                string.Equals(row.RawDrawingNumber, bgDrawing, StringComparison.OrdinalIgnoreCase));
                            if (bgRow != null)
                                bgRevision = bgRow.RevisionNumber;
                        }

                        var orderLine = OrderVerificationLookup.FindBestMatchingLine(
                            ToOrderExcelRow(r),
                            orderVerificationLines,
                            pdf.SearchKey);
                        var deliveryDate = OrderVerificationLookup.ResolveWatermarkDeliveryDate(ToOrderExcelRow(r), orderLine);

                        return new WatermarkEntry
                        {
                            RequestedAmount = r.RequestedAmount,
                            RequestedDeliveryDate = deliveryDate,
                            RequestedDeliveryLocation = r.RequestedDeliveryLocation,
                            BgDrawingNumber = FormatBgWatermarkText(bgDrawing, bgRevision),
                            OrderNumber = orderNumber
                        };
                    })
                    .ToList();

                var primaryOrderLine = OrderVerificationLookup.FindBestMatchingLine(
                    orderRowForPdf,
                    orderVerificationLines,
                    pdf.SearchKey);
                var primaryDeliveryDate = OrderVerificationLookup.ResolveWatermarkDeliveryDate(orderRowForPdf, primaryOrderLine);

                matches.Add(new RenamingItem
                {
                    SourcePath = pdf.FullPath,
                    DrawingNumber = pdf.OriginalName,
                    ExcelRowIndex = primaryRow.RowIndex,
                    PdfOrder = pdf.Order,
                    RequestedAmount = primaryRow.RequestedAmount,
                    RequestedDeliveryDate = primaryDeliveryDate,
                    RequestedDeliveryLocation = primaryRow.RequestedDeliveryLocation,
                    WatermarkEntries = watermarkEntries
                });
            }

            if (matches.Count == 0)
            {
                SafeAppendStatus("Keine passenden PDFs zu den Zeichnungsnummern in der Excel-Datei gefunden.");
                SafeAppendStatus("Tipp: Vergleichen Sie die PDF-Schlüssel mit den Excel-Zellen oben.");
                return;
            }

            var sourceFilesToDeleteAfterSuccess = new List<string>();
            sourceFilesToDeleteAfterSuccess.AddRange(pdfCandidates.Select(p => p.FullPath));
            sourceFilesToDeleteAfterSuccess.Add(excelPath);

            matches = matches
                .OrderBy(item => item.ExcelRowIndex)
                .ThenBy(item => item.PdfOrder)
                .ToList();

            var width = Math.Max(2, matches.Count.ToString(CultureInfo.InvariantCulture).Length);
            var renamePlan = matches
                .Select((item, index) => new
                {
                    item.SourcePath,
                    item.DrawingNumber,
                    item.RequestedAmount,
                    item.RequestedDeliveryDate,
                    item.RequestedDeliveryLocation,
                    item.WatermarkEntries,
                    TempPath = Path.Combine(Path.GetDirectoryName(item.SourcePath), Guid.NewGuid().ToString("N") + Path.GetExtension(item.SourcePath)),
                    FinalPath = GetAvailableFilePath(Path.Combine(Path.GetDirectoryName(item.SourcePath), string.Format(CultureInfo.InvariantCulture, "{0:D" + width + "}_{1}.pdf", index + 1, SanitizeFileName(StripSequencePrefixes(Path.GetFileNameWithoutExtension(item.SourcePath))))))
                })
                .ToList();

            var fileCount = Math.Max(1, renamePlan.Count);
            var moveIndex = 0;
            var watermarkCount = renamePlan.Count;
            var watermarkIndex = 0;
            var watermarkPhaseEnd = addWatermarks ? 0.94 : 0.11;
            var pageNumberPhaseStart = watermarkPhaseEnd;

            foreach (var item in renamePlan)
            {
                cancellationToken.ThrowIfCancellationRequested();
                File.Move(item.SourcePath, item.TempPath);
                moveIndex++;
                SafeSetProgressFraction(0.07 + 0.02 * moveIndex / fileCount, "Sortieren: Dateien verschieben …");
            }

            var renameIndex = 0;
            foreach (var item in renamePlan)
            {
                cancellationToken.ThrowIfCancellationRequested();
                File.Move(item.TempPath, item.FinalPath);
                SafeAppendStatus(string.Format(CultureInfo.InvariantCulture, "{0} -> {1}", Path.GetFileName(item.SourcePath), Path.GetFileName(item.FinalPath)));
                renameIndex++;
                SafeSetProgressFraction(0.09 + 0.02 * renameIndex / fileCount, "Sortieren: Dateien umbenennen …");
            }

            // Wasserzeichen auf alle PDFs anwenden (NUR wenn der Checkbox aktiviert ist)
            if (addWatermarks)
            {
                SafeAppendStatus("──────────────────────────────────────────");
                SafeAppendStatus("Wasserzeichen werden hinzugefügt...");
                PdfLayoutAnalysisService.ClearCache();
                PdfMaterialExtractionService.ClearCache();
                PdfRasterLayoutService.ClearCache();
                WatermarkProcessingContext.BatchMode = true;
                SafeAppendStatus("──────────────────────────────────────────");

                try
                {
                foreach (var item in renamePlan)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    watermarkIndex++;
                    var fileName = Path.GetFileName(item.FinalPath);
                    var phase = string.Format(
                        CultureInfo.CurrentCulture,
                        "Wasserzeichen {0}/{1}: {2}",
                        watermarkIndex,
                        watermarkCount,
                        fileName);
                    var currentWatermarkIndex = watermarkIndex;

                    void ReportWatermarkFileProgress(double fileSubFraction)
                    {
                        var clamped = Math.Max(0d, Math.Min(1d, fileSubFraction));
                        var fileFraction = ((currentWatermarkIndex - 1) + clamped) / fileCount;
                        var fraction = 0.11 + (watermarkPhaseEnd - 0.11) * fileFraction;
                        var overallPercent = (int)Math.Round(fraction * 100d, MidpointRounding.AwayFromZero);
                        var phaseText = string.Format(
                            CultureInfo.CurrentCulture,
                            "Wasserzeichen {0}/{1}: {2}  ·  Gesamt {3} %",
                            currentWatermarkIndex,
                            watermarkCount,
                            fileName,
                            overallPercent);
                        SafeSetProgressFraction(fraction, phaseText);
                    }

                    try
                    {
                        var encodedWatermarks = EncodeWatermarkEntries(item.WatermarkEntries);
                        WatermarkEditorService.EnsurePreWatermarkBackup(item.FinalPath);
                        WatermarkMetadataStore.Save(item.FinalPath, item.DrawingNumber, encodedWatermarks);
                        PdfWatermarkService.AddWatermark(
                            item.FinalPath,
                            encodedWatermarks,
                            string.Empty,
                            string.Empty,
                            item.DrawingNumber,
                            _watermarkSettings,
                            ReportWatermarkFileProgress);
                        SafeAppendStatus(string.Format(CultureInfo.InvariantCulture, "✓ Wasserzeichen hinzugefügt zu: {0}", fileName));
                        SafeSetProgressFraction(0.11 + (watermarkPhaseEnd - 0.11) * currentWatermarkIndex / fileCount, phase + " ✓");
                    }
                    catch (InvalidOperationException ioex)
                    {
                        SafeAppendStatus(string.Format(CultureInfo.InvariantCulture, "❌ FEHLER - Wasserzeichen-Funktion nicht verfügbar: {0}", ioex.Message));
                        SafeAppendStatus("");
                        if (!PdfWatermarkService.IsAvailable())
                        {
                            SafeAppendStatus("⚠️ BITTE BEACHTEN:");
                            SafeAppendStatus("Wasserzeichen brauchen die Dateien itextsharp.dll und BouncyCastle.Cryptography.dll im selben Ordner wie die EXE.");
                            SafeAppendStatus("Tipp: immer den kompletten bin\\Debug- oder bin\\Release-Ordner auf den Ziel-PC kopieren.");
                            SafeAppendStatus("");
                        }
                    }
                    catch (Exception ex)
                    {
                        SafeAppendStatus(string.Format(CultureInfo.InvariantCulture, "❌ Fehler bei {0}: {1}", fileName, ex.Message));
                    }
                }
                }
                finally
                {
                    WatermarkProcessingContext.BatchMode = false;
                }

            }
            else
            {
                SafeAppendStatus("ℹ️  Wasserzeichen deaktiviert (kein Fehler)");
            }

            SafeAppendStatus("──────────────────────────────────────────");
            SafeAppendStatus("Seitenzahlen werden eingefügt (fortlaufend wie in einem Buch)...");

            var nextPageNumber = 1;
            var pageNumberIndex = 0;
            foreach (var item in renamePlan)
            {
                cancellationToken.ThrowIfCancellationRequested();
                pageNumberIndex++;
                try
                {
                    nextPageNumber = PdfWatermarkService.AddPageNumbers(item.FinalPath, nextPageNumber);
                    SafeAppendStatus(string.Format(CultureInfo.InvariantCulture, "✓ Seitenzahlen eingefügt: {0}", Path.GetFileName(item.FinalPath)));
                }
                catch (InvalidOperationException ioex)
                {
                    SafeAppendStatus(string.Format(CultureInfo.InvariantCulture, "❌ FEHLER - Seitenzahlen nicht verfügbar: {0}", ioex.Message));
                    if (!PdfWatermarkService.IsAvailable())
                    {
                        SafeAppendStatus("⚠️ BITTE BEACHTEN:");
                        SafeAppendStatus("Seitenzahlen brauchen itextsharp.dll und BouncyCastle.Cryptography.dll im App-Ordner.");
                    }
                }
                catch (Exception ex)
                {
                    SafeAppendStatus(string.Format(CultureInfo.InvariantCulture, "❌ Fehler bei {0}: {1}", Path.GetFileName(item.FinalPath), ex.Message));
                }

                SafeSetProgressFraction(pageNumberPhaseStart + (1.0 - pageNumberPhaseStart) * pageNumberIndex / fileCount, "Seitenzahlen einfügen …");
            }

            SafeSetProgress(100, "Fertig");
            SafeAppendStatus(string.Format(CultureInfo.InvariantCulture, "Fertig. {0} PDF-Dateien wurden umbenannt.", renamePlan.Count));

            if (unmatchedPdfs.Count > 0)
            {
                SafeAppendStatus("Nicht gefunden in Excel: " + string.Join(", ", unmatchedPdfs));
            }

            // Nach erfolgreicher Verarbeitung: Ursprungs-Excel und nicht mehr vorhandene Quell-PDFs entfernen
            var deletedCount = 0;
            var deletedNames = new List<string>();
            foreach (var fileToDelete in sourceFilesToDeleteAfterSuccess.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                try
                {
                    if (File.Exists(fileToDelete))
                    {
                        var name = Path.GetFileName(fileToDelete);
                        File.Delete(fileToDelete);
                        deletedCount++;
                        deletedNames.Add(name);
                    }
                }
                catch (Exception ex)
                {
                    SafeAppendStatus("⚠ Konnte nicht gelöscht werden: " + Path.GetFileName(fileToDelete) + " - " + ex.Message);
                }
            }

            if (deletedCount == 0)
            {
                SafeAppendStatus("ℹ Ursprungsdaten: keine Dateien zum Löschen (PDFs bereits umbenannt).");
            }
            else
            {
                SafeAppendStatus(string.Format(
                    CultureInfo.InvariantCulture,
                    "🧹 Ursprungsdaten bereinigt: {0} Datei(en) gelöscht ({1}).",
                    deletedCount,
                    string.Join(", ", deletedNames)));
                SafeAppendStatus("ℹ Hinweis: Nach dem Umbenennen bleiben nur die sortierten PDFs in der Ablage; die Excel-Quelldatei wird entfernt.");
            }
        }

        private Task RunOnStaThreadAsync(Action action, CancellationToken cancellationToken)
        {
            var tcs = new TaskCompletionSource<object>();
            var thread = new Thread(() =>
            {
                try
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    action();
                    tcs.TrySetResult(null);
                }
                catch (OperationCanceledException)
                {
                    tcs.TrySetCanceled();
                }
                catch (Exception ex)
                {
                    tcs.TrySetException(ex);
                }
            });

            thread.IsBackground = true;
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            return tcs.Task;
        }

        private void SetProcessingState(bool isProcessing)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.Invoke(() => SetProcessingState(isProcessing));
                return;
            }

            ProcessButtonBox.IsEnabled = !isProcessing;
            MergePrintButtonBox.IsEnabled = !isProcessing;
            DeleteProcessedButtonBox.IsEnabled = !isProcessing;
            CancelButtonBox.IsEnabled = isProcessing;
            ProgressPanelBox.Visibility = isProcessing ? Visibility.Visible : Visibility.Collapsed;
            BusyProgressBarBox.Value = 0;
            ProgressLabelBox.Text = "0 %";

            if (isProcessing)
            {
                _lastRunElapsed = null;
                StopwatchPanel.Visibility = Visibility.Visible;
                StopwatchTextBlock.Text = "00:00:00";
                StopwatchHintTextBlock.Text = "Stopuhr läuft …";
                _processingStopwatch = Stopwatch.StartNew();
                _stopwatchTimer.Start();
                UpdateStopwatchDisplay();
            }
            else
            {
                _stopwatchTimer.Stop();

                if (BusyProgressBarBox != null)
                    BusyProgressBarBox.IsIndeterminate = false;

                if (_processingStopwatch != null)
                {
                    _processingStopwatch.Stop();
                    _lastRunElapsed = _processingStopwatch.Elapsed;
                    StopwatchTextBlock.Text = FormatStopwatchTime(_lastRunElapsed.Value);
                    StopwatchHintTextBlock.Text = "Gesamtdauer";
                    AppendStatus("⏱ Auftragsdauer: " + FormatDuration(_processingStopwatch.Elapsed));
                    _processingStopwatch = null;
                }

                StopwatchPanel.Visibility = Visibility.Visible;
            }
        }

        private void UpdateStopwatchDisplay()
        {
            if (StopwatchTextBlock == null)
                return;

            if (_processingStopwatch != null && _processingStopwatch.IsRunning)
                StopwatchTextBlock.Text = FormatStopwatchTime(_processingStopwatch.Elapsed);
            else if (_lastRunElapsed.HasValue)
                StopwatchTextBlock.Text = FormatStopwatchTime(_lastRunElapsed.Value);
        }

        private static string FormatStopwatchTime(TimeSpan duration)
        {
            if (duration.TotalHours >= 1d)
            {
                return string.Format(
                    CultureInfo.InvariantCulture,
                    "{0:00}:{1:00}:{2:00}",
                    (int)duration.TotalHours,
                    duration.Minutes,
                    duration.Seconds);
            }

            return string.Format(
                CultureInfo.InvariantCulture,
                "00:{0:00}:{1:00}",
                duration.Minutes,
                duration.Seconds);
        }

        private void SetProgress(int percent)
        {
            SetProgress(percent, null);
        }

        private void SetProgress(int percent, string phaseText)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.Invoke(() => SetProgress(percent, phaseText));
                return;
            }

            UpdateProgressUi(percent, phaseText);
        }

        private void UpdateProgressUi(int percent, string phaseText = null)
        {
            if (percent < 0)
                percent = 0;
            if (percent > 100)
                percent = 100;

            if (BusyProgressBarBox != null)
            {
                BusyProgressBarBox.IsIndeterminate = false;
                BusyProgressBarBox.Value = percent;
            }

            if (ProgressLabelBox != null)
                ProgressLabelBox.Text = percent.ToString(CultureInfo.InvariantCulture) + " %";

            if (!string.IsNullOrWhiteSpace(phaseText) && StopwatchHintTextBlock != null)
                StopwatchHintTextBlock.Text = phaseText;
        }

        private static string FormatDuration(TimeSpan duration)
        {
            if (duration.TotalHours >= 1d)
                return string.Format(CultureInfo.CurrentCulture, "{0:0} h {1:00} min", (int)duration.TotalHours, duration.Minutes);

            if (duration.TotalMinutes >= 1d)
                return string.Format(CultureInfo.CurrentCulture, "{0:0} min {1:00} s", (int)duration.TotalMinutes, duration.Seconds);

            return string.Format(CultureInfo.CurrentCulture, "{0:0} s", Math.Max(1, (int)Math.Round(duration.TotalSeconds)));
        }

        private IEnumerable<string> ReadDrawingNumbersFromExcel(string excelPath)
        {
            var extension = Path.GetExtension(excelPath).ToLowerInvariant();
            if (extension == ".xls")
            {
                return ReadDrawingNumbersFromXls(excelPath);
            }

            if (extension == ".xlsx")
            {
                return ReadDrawingNumbersFromXlsx(excelPath);
            }

            throw new NotSupportedException("Nur .xls und .xlsx werden unterstützt.");
        }

        private IEnumerable<string> ReadDrawingNumbersFromXlsx(string excelPath)
        {
            using (var archive = ZipFile.OpenRead(excelPath))
            {
                var workbook = LoadXmlFromArchive(archive, "xl/workbook.xml");
                var relationships = LoadXmlFromArchive(archive, "xl/_rels/workbook.xml.rels");

                var sheet = workbook.Descendants(WorkbookNamespace + "sheet").FirstOrDefault();
                if (sheet == null)
                {
                    return Enumerable.Empty<string>();
                }

                var relId = sheet.Attribute(RelationshipNamespace + "id")?.Value;
                if (string.IsNullOrWhiteSpace(relId))
                {
                    return Enumerable.Empty<string>();
                }

                var target = relationships.Descendants(RelationshipNamespace + "Relationship")
                    .FirstOrDefault(x => string.Equals((string)x.Attribute("Id"), relId, StringComparison.OrdinalIgnoreCase))
                    ?.Attribute("Target")?.Value;

                if (string.IsNullOrWhiteSpace(target))
                {
                    return Enumerable.Empty<string>();
                }

                var sheetPath = NormalizeZipPath("xl/" + target);
                var sheetXml = LoadXmlFromArchive(archive, sheetPath);
                var sharedStrings = LoadSharedStrings(archive);
                var values = new List<string>();

                foreach (var row in sheetXml.Descendants(SpreadsheetNamespace + "row"))
                {
                    var cells = row.Elements(SpreadsheetNamespace + "c").ToList();
                    if (cells.Count == 0)
                    {
                        continue;
                    }

                    var cell = cells[0];
                    var text = GetCellText(cell, sharedStrings);
                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        values.Add(text.Trim());
                    }
                }

                return values;
            }
        }

        private IEnumerable<string> ReadDrawingNumbersFromXls(string excelPath)
        {
            var excelType = Type.GetTypeFromProgID("Excel.Application");
            if (excelType == null)
            {
                throw new InvalidOperationException("Microsoft Excel ist für das Lesen von .xls-Dateien nicht installiert.");
            }

            dynamic excel = null;
            dynamic workbook = null;
            dynamic worksheet = null;

            try
            {
                excel = Activator.CreateInstance(excelType);
                excel.Visible = false;
                excel.DisplayAlerts = false;

                workbook = excel.Workbooks.Open(excelPath, ReadOnly: true);
                worksheet = workbook.Worksheets[1];
                var usedRange = worksheet.UsedRange;
                var rows = usedRange.Rows.Count;
                var values = new List<string>();

                for (var row = 1; row <= rows; row++)
                {
                    var value = Convert.ToString(usedRange.Cells[row, 1].Value2, CultureInfo.InvariantCulture)?.Trim();
                    if (!string.IsNullOrWhiteSpace(value))
                    {
                        values.Add(value);
                    }
                }

                return values;
            }
            finally
            {
                if (workbook != null)
                {
                    workbook.Close(false);
                }

                if (excel != null)
                {
                    excel.Quit();
                }
            }
        }

        private static XDocument LoadXmlFromArchive(ZipArchive archive, string entryPath)
        {
            var entry = archive.GetEntry(entryPath.Replace('/', '\\')) ?? archive.GetEntry(entryPath);
            if (entry == null)
            {
                throw new FileNotFoundException("Excel-Datei ist unvollständig: " + entryPath);
            }

            using (var stream = entry.Open())
            {
                return XDocument.Load(stream);
            }
        }

        private static List<string> LoadSharedStrings(ZipArchive archive)
        {
            var entry = archive.GetEntry("xl/sharedStrings.xml");
            if (entry == null)
            {
                return new List<string>();
            }

            using (var stream = entry.Open())
            {
                var document = XDocument.Load(stream);
                return document.Descendants(SpreadsheetNamespace + "si")
                    .Select(GetSharedStringText)
                    .ToList();
            }
        }

        private static string GetSharedStringText(XElement sharedString)
        {
            return string.Concat(sharedString.Descendants(SpreadsheetNamespace + "t").Select(x => x.Value));
        }

        private static string GetCellText(XElement cell, IList<string> sharedStrings)
        {
            var valueElement = cell.Element(SpreadsheetNamespace + "v");
            var inlineString = cell.Element(SpreadsheetNamespace + "is");
            var cellType = (string)cell.Attribute("t");

            if (cellType == "s" && valueElement != null)
            {
                if (int.TryParse(valueElement.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var sharedIndex) && sharedIndex >= 0 && sharedIndex < sharedStrings.Count)
                {
                    return sharedStrings[sharedIndex];
                }
            }

            if (cellType == "inlineStr" && inlineString != null)
            {
                return string.Concat(inlineString.Descendants(SpreadsheetNamespace + "t").Select(x => x.Value));
            }

            return valueElement?.Value ?? string.Empty;
        }

        private static string NormalizeZipPath(string path)
        {
            return path.Replace('\\', '/').TrimStart('/');
        }

        private static readonly XNamespace SpreadsheetNamespace = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        private static readonly XNamespace WorkbookNamespace = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        private static readonly XNamespace RelationshipNamespace = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";

        private IEnumerable<PdfCandidate> LoadPdfCandidates(string pdfFolder, SearchOption searchOption, string excludeOrderPdfPath = null)
        {
            var excluded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (!string.IsNullOrWhiteSpace(excludeOrderPdfPath) && File.Exists(excludeOrderPdfPath))
                excluded.Add(excludeOrderPdfPath);

            return Directory.GetFiles(pdfFolder, "*.pdf", searchOption)
                .Where(path => !Path.GetFileName(path).StartsWith("_GESAMT_", StringComparison.OrdinalIgnoreCase))
                .Where(path => !excluded.Contains(path))
                .Select((path, index) => new PdfCandidate(path, index))
                .Where(candidate => !string.IsNullOrWhiteSpace(candidate.SearchKey));
        }

        private string ResolveOrderVerificationPdfPath(string pdfFolder)
        {
            if (!string.IsNullOrWhiteSpace(_orderVerificationPdfPath) && File.Exists(_orderVerificationPdfPath))
                return _orderVerificationPdfPath;

            return string.Empty;
        }

        private bool TryRunOrderVerification(
            string excelPath,
            string orderPdfPath,
            IList<ExcelRowCandidate> excelRows,
            string pdfFolder,
            SearchOption searchOption,
            bool blockOnIssues)
        {
            var report = RunOrderVerificationReport(excelPath, orderPdfPath, excelRows, pdfFolder, searchOption);
            AppendStatus(report.ToDisplayText());

            var dialogResult = OrderVerificationReportWindow.ShowDialog(
                this,
                report,
                Path.GetFileName(excelPath),
                Path.GetFileName(orderPdfPath),
                offerContinueOnIssues: blockOnIssues);

            if (dialogResult.AdoptOrderPdfDates)
            {
                ApplyOrderPdfDates(excelRows, report);
                report = RunOrderVerificationReport(excelPath, orderPdfPath, excelRows, pdfFolder, searchOption);
                AppendStatus("✓ Liefertermine aus Bestell-PDF übernommen.");
                AppendStatus(report.ToDisplayText());
            }

            if (!report.HasBlockingIssues)
            {
                AppendStatus("✓ Auftragsprüfung bestanden – Sortierung kann starten.");
                return true;
            }

            if (!blockOnIssues)
                return true;

            if (!dialogResult.Proceed)
            {
                AppendStatus("Sortierung abgebrochen – bitte Excel und Bestell-PDF abgleichen.");
                return false;
            }

            AppendStatus("⚠ Sortierung trotz Abweichungen fortgesetzt (Benutzerbestätigung).");
            return true;
        }

        private OrderVerificationReport RunOrderVerificationReport(
            string excelPath,
            string orderPdfPath,
            IList<ExcelRowCandidate> excelRows,
            string pdfFolder,
            SearchOption searchOption)
        {
            var orderRows = excelRows.Select(ToOrderExcelRow).ToList();
            var pdfDrawingKeys = LoadPdfCandidates(pdfFolder, searchOption, orderPdfPath)
                .Select(p => p.SearchKey)
                .ToList();
            var report = _orderExcelVerificationService.Verify(
                orderPdfPath,
                orderRows,
                _pdfTextService,
                pdfDrawingKeys);
            _cachedOrderVerificationLines = report.ParsedOrderLines;
            return report;
        }

        private static void ApplyOrderPdfDates(IList<ExcelRowCandidate> excelRows, OrderVerificationReport report)
        {
            if (excelRows == null || report?.DateMismatches == null)
                return;

            foreach (var mismatch in report.DateMismatches)
            {
                if (string.IsNullOrWhiteSpace(mismatch.PdfDate)
                    || mismatch.PdfDate.StartsWith("(", System.StringComparison.Ordinal))
                {
                    continue;
                }

                var normalizedPdfDate = OrderVerificationDateHelper.NormalizeToEu(mismatch.PdfDate);
                if (string.IsNullOrWhiteSpace(normalizedPdfDate))
                    continue;

                foreach (var row in excelRows)
                {
                    if (OrderVerificationDrawingKey.Equal(row.RawDrawingNumber, mismatch.DrawingKey)
                        || (row.NormalizedCells != null
                            && row.NormalizedCells.Any(cell => OrderVerificationDrawingKey.Equal(cell, mismatch.DrawingKey))))
                    {
                        row.RequestedDeliveryDate = normalizedPdfDate;
                    }
                }
            }
        }

        private static OrderExcelRow ToOrderExcelRow(ExcelRowCandidate row)
        {
            var searchKey = row.NormalizedCells != null
                ? row.NormalizedCells.FirstOrDefault(c => !string.IsNullOrWhiteSpace(c))
                : null;

            return new OrderExcelRow
            {
                RowIndex = row.RowIndex,
                DrawingNumber = row.RawDrawingNumber,
                SearchKey = searchKey ?? string.Empty,
                ArticleNumbers = row.DetectedArticleNumbers ?? new List<string>(),
                ParentArticleNumber = row.ParentArticleNumber,
                Amount = row.RequestedAmount,
                DeliveryDate = row.RequestedDeliveryDate,
                DeliveryLocation = row.RequestedDeliveryLocation
            };
        }

        private void OrderVerificationCheck_Click(object sender, RoutedEventArgs e)
        {
            var excelPath = ExcelPathBox.Text.Trim();
            var pdfFolder = PdfFolderBox.Text.Trim();

            if (!TryResolveExcelPath(ref excelPath, AppendStatus))
            {
                AppendStatus("Bitte zuerst eine gültige Excel-Datei laden.");
                return;
            }

            if (string.IsNullOrWhiteSpace(pdfFolder) || !Directory.Exists(pdfFolder))
            {
                AppendStatus("Bitte zuerst den Ablage-Ordner verwenden.");
                return;
            }

            var orderPdfPath = ResolveOrderVerificationPdfPath(pdfFolder);
            if (string.IsNullOrWhiteSpace(orderPdfPath) || !File.Exists(orderPdfPath))
            {
                AppendStatus("Keine Bestell-PDF geladen. Bitte per Maus in „Prüf-PDF“ ablegen.");
                return;
            }

            var excelRows = ReadExcelRows(excelPath).ToList();
            if (excelRows.Count == 0)
            {
                AppendStatus("Keine Excel-Zeilen gefunden.");
                return;
            }

            TryRunOrderVerification(
                excelPath,
                orderPdfPath,
                excelRows,
                pdfFolder,
                IncludeSubfoldersBox.IsChecked == true ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly,
                blockOnIssues: false);
        }

        private IEnumerable<ExcelRowCandidate> ReadExcelRows(string excelPath)
        {
            var extension = Path.GetExtension(excelPath).ToLowerInvariant();
            if (extension == ".xlsx")
            {
                return ReadExcelRowsFromXlsx(excelPath);
            }

            if (extension == ".xls")
            {
                return ReadExcelRowsWithExcelInterop(excelPath);
            }

            throw new NotSupportedException("Nur .xls und .xlsx werden unterstützt.");
        }

        private void ApplyAngebotDeliveryDates(string primaryExcelPath, IList<ExcelRowCandidate> targetRows, Action<string> log = null)
        {
            if (targetRows == null || targetRows.Count == 0 || string.IsNullOrWhiteSpace(primaryExcelPath))
                return;

            var folder = Path.GetDirectoryName(primaryExcelPath);
            var angebotPath = ExcelDeliveryDateMergeService.FindAngebotExcelPath(folder, primaryExcelPath);
            if (string.IsNullOrWhiteSpace(angebotPath) || !File.Exists(angebotPath))
                return;

            var quoteRows = ReadExcelRows(angebotPath).ToList();
            if (quoteRows.Count == 0)
            {
                log?.Invoke("⚠ Angebot-Excel ohne Zeichnungszeilen: " + Path.GetFileName(angebotPath));
                return;
            }

            ExcelDeliveryDateMergeService.ApplyQuoteDates(
                targetRows,
                quoteRows,
                row =>
                {
                    var keys = new List<string>();
                    if (!string.IsNullOrWhiteSpace(row.RawDrawingNumber))
                        keys.Add(NormalizeKey(row.RawDrawingNumber));
                    if (row.NormalizedCells != null)
                        keys.AddRange(row.NormalizedCells.Where(k => !string.IsNullOrWhiteSpace(k)));
                    return keys;
                },
                row => row.RequestedDeliveryDate,
                row => row.QuoteDeliveryDate,
                (row, date) => row.RequestedDeliveryDate = date);

            log?.Invoke("ℹ Angebot-Excel: " + Path.GetFileName(angebotPath) + " – Termine mit Vorrang übernommen.");
        }

        private IEnumerable<ExcelRowCandidate> ReadExcelRowsFromXlsx(string excelPath)
        {
            using (var archive = ZipFile.OpenRead(excelPath))
            {
                var workbook = LoadXmlFromArchive(archive, "xl/workbook.xml");
                var relsXml = LoadXmlFromArchive(archive, "xl/_rels/workbook.xml.rels");
                var sharedStrings = LoadSharedStrings(archive);
                var result = new List<ExcelRowCandidate>();

                var rels = relsXml.Descendants()
                    .Where(e => e.Name.LocalName == "Relationship")
                    .ToDictionary(
                        e => (string)e.Attribute("Id"),
                        e => (string)e.Attribute("Target"),
                        StringComparer.OrdinalIgnoreCase);

                foreach (var sheetEl in workbook.Descendants(WorkbookNamespace + "sheet"))
                {
                    var relId = sheetEl.Attribute(RelationshipNamespace + "id")?.Value;
                    if (string.IsNullOrWhiteSpace(relId) || !rels.TryGetValue(relId, out var target))
                        continue;

                    string globalDeliveryDate = string.Empty;
                    string globalQuoteDeliveryDate = string.Empty;
                    string globalDeliveryLocation = string.Empty;

                    var sheetPath = NormalizeZipPath("xl/" + target);
                    var sheetXml = LoadXmlFromArchive(archive, sheetPath);
                    var allRows = sheetXml.Descendants(SpreadsheetNamespace + "row").ToList();

                    // Spalten für Drawing, Qty, Requested Delivery Date und Delivery Place finden
                    string drawingColRef = null;
                    string revisionColRef = null;
                    string articleNoColRef = null;
                    string amountColRef = null;
                    string dateColRef = null;
                    string quoteDateColRef = null;
                    string locationColRef = null;
                    int headerRowIndex = -1;

                    foreach (var row in allRows)
                    {
                        foreach (var cell in row.Elements(SpreadsheetNamespace + "c"))
                        {
                            var text = GetCellText(cell, sharedStrings).Trim();
                            var cellRef = (string)cell.Attribute("r") ?? string.Empty;
                            var colRef = new string(cellRef.TakeWhile(char.IsLetter).ToArray());

                            if (drawingColRef == null && IsDrawingHeader(text))
                            {
                                drawingColRef = colRef;
                                headerRowIndex = int.Parse(row.Attribute("r")?.Value ?? "0", CultureInfo.InvariantCulture);
                            }
                        }
                    }

                    // Metadaten-Header nur in/nahe der echten Drawing-Header-Zeile suchen
                    if (headerRowIndex > 0)
                    {
                        var headerRows = allRows.Where(r =>
                        {
                            var rn = GetRowNumber(r);
                            return rn == headerRowIndex || rn == headerRowIndex + 1 || rn == headerRowIndex + 2;
                        });

                        foreach (var headerRow in headerRows)
                        {
                            foreach (var cell in headerRow.Elements(SpreadsheetNamespace + "c"))
                            {
                                var text = GetCellText(cell, sharedStrings).Trim();
                                var cellRef = (string)cell.Attribute("r") ?? string.Empty;
                                var colRef = new string(cellRef.TakeWhile(char.IsLetter).ToArray());

                                if (articleNoColRef == null && IsArticleNumberHeader(text))
                                    articleNoColRef = colRef;
                                if (amountColRef == null && IsAmountHeader(text))
                                    amountColRef = colRef;
                                if (quoteDateColRef == null && IsQuoteDateHeader(text))
                                    quoteDateColRef = colRef;
                                if (dateColRef == null && IsStandardDateHeader(text))
                                    dateColRef = colRef;
                                if (locationColRef == null && IsLocationHeader(text))
                                    locationColRef = colRef;
                                if (revisionColRef == null && IsRevisionHeader(text))
                                    revisionColRef = colRef;
                            }
                        }
                    }

                    if (drawingColRef != null && revisionColRef == null)
                        revisionColRef = GetNextColumnRef(drawingColRef);

                    if (drawingColRef == null)
                    {
                        // Kein "Drawing"-Header gefunden – Fallback
                        foreach (var row in allRows)
                        {
                            var cellValues = row.Elements(SpreadsheetNamespace + "c")
                                .Select(c => GetCellText(c, sharedStrings).Trim())
                                .Where(t => !string.IsNullOrWhiteSpace(t))
                                .ToList();
                            if (cellValues.Count > 0)
                                result.Add(new ExcelRowCandidate { RowIndex = result.Count + 1, NormalizedCells = cellValues.Select(NormalizeKey).Where(v => !string.IsNullOrWhiteSpace(v)).ToList() });
                        }
                        continue;
                    }

                    // Globalwerte (z.B. "Requested Del : Neuwied") suchen
                    foreach (var row in allRows)
                    {
                        var cells = row.Elements(SpreadsheetNamespace + "c").ToList();
                        for (var i = 0; i < cells.Count; i++)
                        {
                            var txt = GetCellText(cells[i], sharedStrings).Trim();
                            if (string.IsNullOrWhiteSpace(txt))
                                continue;

                            var n = NormalizeHeader(txt);

                            if (string.IsNullOrWhiteSpace(globalDeliveryLocation)
                                && ((n.StartsWith("requested del") && !n.Contains("date")) || n.Contains("delivery location") || n.Contains("delivery place") || n.Contains("lieferort")))
                            {
                                var extracted = ExtractValueAfterSeparator(txt);
                                if (string.IsNullOrWhiteSpace(extracted))
                                {
                                    for (var j = i + 1; j < cells.Count; j++)
                                    {
                                        extracted = GetCellText(cells[j], sharedStrings).Trim();
                                        if (!string.IsNullOrWhiteSpace(extracted))
                                            break;
                                    }
                                }

                                if (!string.IsNullOrWhiteSpace(extracted) && !IsLikelyExcelDateSerial(extracted))
                                    globalDeliveryLocation = extracted;
                            }

                            if (string.IsNullOrWhiteSpace(globalDeliveryDate)
                                && IsStandardDateHeader(n))
                            {
                                var extracted = ExtractValueAfterSeparator(txt);
                                if (string.IsNullOrWhiteSpace(extracted))
                                {
                                    for (var j = i + 1; j < cells.Count; j++)
                                    {
                                        extracted = GetCellText(cells[j], sharedStrings).Trim();
                                        if (!string.IsNullOrWhiteSpace(extracted))
                                            break;
                                    }
                                }

                                if (!string.IsNullOrWhiteSpace(extracted))
                                    globalDeliveryDate = NormalizeDateValue(extracted);
                            }

                            if (string.IsNullOrWhiteSpace(globalQuoteDeliveryDate)
                                && IsQuoteDateHeader(n))
                            {
                                var extracted = ExtractValueAfterSeparator(txt);
                                if (string.IsNullOrWhiteSpace(extracted))
                                {
                                    for (var j = i + 1; j < cells.Count; j++)
                                    {
                                        extracted = GetCellText(cells[j], sharedStrings).Trim();
                                        if (!string.IsNullOrWhiteSpace(extracted))
                                            break;
                                    }
                                }

                                if (!string.IsNullOrWhiteSpace(extracted))
                                    globalQuoteDeliveryDate = NormalizeDateValue(extracted);
                            }
                        }
                    }

                    // Nur Zeilen nach der Kopfzeile
                    foreach (var row in allRows)
                    {
                        var rowNum = int.Parse(row.Attribute("r")?.Value ?? "0", CultureInfo.InvariantCulture);
                        if (rowNum <= headerRowIndex) continue;

                        var rowTexts = row.Elements(SpreadsheetNamespace + "c")
                            .Select(c => GetCellText(c, sharedStrings).Trim())
                            .Where(t => !string.IsNullOrWhiteSpace(t))
                            .ToList();

                        var articleNoValue = articleNoColRef != null ? GetCellValueByColRef(row, articleNoColRef, sharedStrings) : string.Empty;

                        // Zelle in der Drawing-Spalte suchen
                        var drawingCell = row.Elements(SpreadsheetNamespace + "c")
                            .FirstOrDefault(c => new string(((string)c.Attribute("r") ?? string.Empty).TakeWhile(char.IsLetter).ToArray())
                                .Equals(drawingColRef, StringComparison.OrdinalIgnoreCase));

                        if (drawingCell == null) continue;

                        var drawingText = GetCellText(drawingCell, sharedStrings).Trim();
                        if (!string.IsNullOrWhiteSpace(drawingText))
                        {
                            var key = NormalizeKey(drawingText);
                            if (!string.IsNullOrWhiteSpace(key))
                            {
                                var amount = amountColRef != null ? GetCellValueByColRef(row, amountColRef, sharedStrings) : string.Empty;
                                var standardDateRaw = dateColRef != null ? GetCellValueByColRef(row, dateColRef, sharedStrings) : string.Empty;
                                var standardDate = NormalizeDateValue(standardDateRaw);
                                if (string.IsNullOrWhiteSpace(standardDate))
                                    standardDate = globalDeliveryDate;

                                var quoteDateRaw = quoteDateColRef != null ? GetCellValueByColRef(row, quoteDateColRef, sharedStrings) : string.Empty;
                                var quoteDate = NormalizeDateValue(quoteDateRaw);
                                if (string.IsNullOrWhiteSpace(quoteDate))
                                    quoteDate = globalQuoteDeliveryDate;

                                var date = ExcelDeliveryDateMergeService.ResolveEffectiveDeliveryDate(standardDate, quoteDate);

                                var location = locationColRef != null ? GetCellValueByColRef(row, locationColRef, sharedStrings) : string.Empty;
                                if (string.IsNullOrWhiteSpace(location) || IsLikelyExcelDateSerial(location))
                                    location = globalDeliveryLocation;

                                var revision = revisionColRef != null ? GetCellValueByColRef(row, revisionColRef, sharedStrings) : string.Empty;

                                result.Add(new ExcelRowCandidate
                                {
                                    RowIndex = result.Count + 1,
                                    NormalizedCells = new List<string> { key },
                                    RawDrawingNumber = drawingText,
                                    RevisionNumber = revision,
                                    DetectedArticleNumbers = ExtractArticleNumbers(new[] { articleNoValue }),
                                    ParentArticleNumber = ExtractParentArticleNumber(new[] { articleNoValue }),
                                    RequestedAmount = amount,
                                    RequestedDeliveryDate = date,
                                    QuoteDeliveryDate = quoteDate,
                                    RequestedDeliveryLocation = location
                                });
                            }
                        }
                    }
                }

                return result;
            }
        }

        private string GetCellValueByColRef(XElement row, string colRef, IList<string> sharedStrings)
        {
            var cell = row.Elements(SpreadsheetNamespace + "c")
                .FirstOrDefault(c => new string(((string)c.Attribute("r") ?? string.Empty).TakeWhile(char.IsLetter).ToArray())
                    .Equals(colRef, StringComparison.OrdinalIgnoreCase));

            return cell != null ? GetCellText(cell, sharedStrings).Trim() : string.Empty;
        }

        private static int GetRowNumber(XElement row)
        {
            return int.TryParse(row.Attribute("r")?.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var rn) ? rn : 0;
        }

        private static string NormalizeDateValue(string raw)
        {
            return OrderVerificationDateHelper.TryNormalizeExcelDate(raw);
        }

        private static bool IsLikelyExcelDateSerial(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return false;

            var text = value.Trim();
            if (double.TryParse(text, NumberStyles.Any, CultureInfo.InvariantCulture, out var oa)
                || double.TryParse(text, NumberStyles.Any, CultureInfo.GetCultureInfo("de-DE"), out oa))
            {
                return oa > 20000 && oa < 90000;
            }

            return false;
        }

        private static string ExtractValueAfterSeparator(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return string.Empty;

            var idx = text.IndexOf(':');
            if (idx >= 0 && idx + 1 < text.Length)
                return text.Substring(idx + 1).Trim();

            idx = text.IndexOf('-');
            if (idx >= 0 && idx + 1 < text.Length)
                return text.Substring(idx + 1).Trim();

            return string.Empty;
        }

        private IEnumerable<ExcelRowCandidate> ReadExcelRowsWithExcelInterop(string excelPath)
        {
            var excelType = Type.GetTypeFromProgID("Excel.Application");
            if (excelType == null)
            {
                throw new InvalidOperationException("Microsoft Excel ist für das Lesen der Excel-Datei nicht installiert.");
            }

            dynamic excel = null;
            dynamic workbook = null;

            try
            {
                excel = Activator.CreateInstance(excelType);
                excel.Visible = false;
                excel.DisplayAlerts = false;

                workbook = excel.Workbooks.Open(excelPath, ReadOnly: true);
                var result = new List<ExcelRowCandidate>();
                var sheetCount = workbook.Worksheets.Count;

                for (var sheetIndex = 1; sheetIndex <= sheetCount; sheetIndex++)
                {
                    string globalDeliveryDate = string.Empty;
                    string globalQuoteDeliveryDate = string.Empty;
                    string globalDeliveryLocation = string.Empty;

                    dynamic worksheet = workbook.Worksheets[sheetIndex];
                    dynamic usedRange = worksheet.UsedRange;
                    var rows = usedRange.Rows.Count;
                    var columns = usedRange.Columns.Count;

                    object[,] allValues;
                    try { allValues = (object[,])usedRange.Value2; }
                    catch { continue; }

                    // Spalten für Drawing, Qty, Requested Delivery Date und Delivery Place suchen
                    int drawingCol = -1;
                    int revisionCol = -1;
                    int articleNoCol = -1;
                    int amountCol = -1;
                    int dateCol = -1;
                    int quoteDateCol = -1;
                    int locationCol = -1;
                    int headerRow = -1;

                    for (var r = 1; r <= rows; r++)
                        for (var c = 1; c <= columns; c++)
                        {
                            var v = Convert.ToString(allValues[r, c], CultureInfo.InvariantCulture)?.Trim();
                            if (drawingCol < 0 && IsDrawingHeader(v))
                            { drawingCol = c; headerRow = r; }
                        }

                    if (headerRow > 0)
                    {
                        for (var c = 1; c <= columns; c++)
                        {
                            var h0 = Convert.ToString(allValues[headerRow, c], CultureInfo.InvariantCulture)?.Trim();
                            var h1 = headerRow + 1 <= rows ? Convert.ToString(allValues[headerRow + 1, c], CultureInfo.InvariantCulture)?.Trim() : string.Empty;
                            var h2 = headerRow + 2 <= rows ? Convert.ToString(allValues[headerRow + 2, c], CultureInfo.InvariantCulture)?.Trim() : string.Empty;

                            if (articleNoCol < 0 && (IsArticleNumberHeader(h0) || IsArticleNumberHeader(h1) || IsArticleNumberHeader(h2))) articleNoCol = c;
                            if (amountCol < 0 && (IsAmountHeader(h0) || IsAmountHeader(h1) || IsAmountHeader(h2))) amountCol = c;
                            if (quoteDateCol < 0 && (IsQuoteDateHeader(h0) || IsQuoteDateHeader(h1) || IsQuoteDateHeader(h2))) quoteDateCol = c;
                            if (dateCol < 0 && (IsStandardDateHeader(h0) || IsStandardDateHeader(h1) || IsStandardDateHeader(h2))) dateCol = c;
                            if (locationCol < 0 && (IsLocationHeader(h0) || IsLocationHeader(h1) || IsLocationHeader(h2))) locationCol = c;
                            if (revisionCol < 0 && (IsRevisionHeader(h0) || IsRevisionHeader(h1) || IsRevisionHeader(h2))) revisionCol = c;
                        }
                    }

                    if (drawingCol > 0 && revisionCol < 0)
                        revisionCol = drawingCol + 1;

                    var scanLimit = headerRow > 0 ? Math.Min(rows, headerRow + 6) : Math.Min(rows, 25);
                    for (var r = 1; r <= scanLimit; r++)
                    {
                        for (var c = 1; c <= columns; c++)
                        {
                            var txt = Convert.ToString(allValues[r, c], CultureInfo.InvariantCulture)?.Trim();
                            if (string.IsNullOrWhiteSpace(txt))
                                continue;

                            var n = NormalizeHeader(txt);

                            if (string.IsNullOrWhiteSpace(globalDeliveryLocation)
                                && ((n.StartsWith("requested del") && !n.Contains("date")) || n.Contains("delivery location") || n.Contains("delivery place") || n.Contains("lieferort")))
                            {
                                var extracted = ExtractValueAfterSeparator(txt);
                                if (string.IsNullOrWhiteSpace(extracted))
                                {
                                    for (var nc = c + 1; nc <= columns; nc++)
                                    {
                                        extracted = Convert.ToString(allValues[r, nc], CultureInfo.InvariantCulture)?.Trim();
                                        if (!string.IsNullOrWhiteSpace(extracted))
                                            break;
                                    }
                                }

                                if (!string.IsNullOrWhiteSpace(extracted) && !IsLikelyExcelDateSerial(extracted))
                                    globalDeliveryLocation = extracted;
                            }

                            if (string.IsNullOrWhiteSpace(globalDeliveryDate)
                                && IsStandardDateHeader(n))
                            {
                                var extracted = ExtractValueAfterSeparator(txt);
                                if (string.IsNullOrWhiteSpace(extracted))
                                {
                                    for (var nc = c + 1; nc <= columns; nc++)
                                    {
                                        extracted = Convert.ToString(allValues[r, nc], CultureInfo.InvariantCulture)?.Trim();
                                        if (!string.IsNullOrWhiteSpace(extracted))
                                            break;
                                    }
                                }

                                if (!string.IsNullOrWhiteSpace(extracted))
                                    globalDeliveryDate = NormalizeDateValue(extracted);
                            }

                            if (string.IsNullOrWhiteSpace(globalQuoteDeliveryDate)
                                && IsQuoteDateHeader(n))
                            {
                                var extracted = ExtractValueAfterSeparator(txt);
                                if (string.IsNullOrWhiteSpace(extracted))
                                {
                                    for (var nc = c + 1; nc <= columns; nc++)
                                    {
                                        extracted = Convert.ToString(allValues[r, nc], CultureInfo.InvariantCulture)?.Trim();
                                        if (!string.IsNullOrWhiteSpace(extracted))
                                            break;
                                    }
                                }

                                if (!string.IsNullOrWhiteSpace(extracted))
                                    globalQuoteDeliveryDate = NormalizeDateValue(extracted);
                            }
                        }
                    }

                    if (drawingCol < 0)
                    {
                        // Fallback: alle Zellen
                        for (var row = 1; row <= rows; row++)
                        {
                            var cellValues = new List<string>();
                            for (var column = 1; column <= columns; column++)
                            {
                                var value = Convert.ToString(allValues[row, column], CultureInfo.InvariantCulture)?.Trim();
                                if (!string.IsNullOrWhiteSpace(value)) cellValues.Add(value);
                            }
                            if (cellValues.Count > 0)
                                result.Add(new ExcelRowCandidate { RowIndex = result.Count + 1, NormalizedCells = cellValues.Select(NormalizeKey).Where(v => !string.IsNullOrWhiteSpace(v)).ToList() });
                        }
                        continue;
                    }

                    for (var row = headerRow + 1; row <= rows; row++)
                    {
                        var rowTexts = new List<string>();
                        for (var c = 1; c <= columns; c++)
                        {
                            var v = Convert.ToString(allValues[row, c], CultureInfo.InvariantCulture)?.Trim();
                            if (!string.IsNullOrWhiteSpace(v))
                                rowTexts.Add(v);
                        }

                        var articleNoValue = articleNoCol > 0 ? Convert.ToString(allValues[row, articleNoCol], CultureInfo.InvariantCulture)?.Trim() ?? string.Empty : string.Empty;

                        var text = Convert.ToString(allValues[row, drawingCol], CultureInfo.InvariantCulture)?.Trim();
                        if (!string.IsNullOrWhiteSpace(text))
                        {
                            var key = NormalizeKey(text);
                            if (!string.IsNullOrWhiteSpace(key))
                            {
                                var amount = amountCol > 0 ? Convert.ToString(allValues[row, amountCol], CultureInfo.InvariantCulture)?.Trim() ?? string.Empty : string.Empty;
                                var standardDateRaw = dateCol > 0 ? Convert.ToString(allValues[row, dateCol], CultureInfo.InvariantCulture)?.Trim() ?? string.Empty : string.Empty;
                                var standardDate = NormalizeDateValue(standardDateRaw);
                                if (string.IsNullOrWhiteSpace(standardDate))
                                    standardDate = globalDeliveryDate;

                                var quoteDateRaw = quoteDateCol > 0 ? Convert.ToString(allValues[row, quoteDateCol], CultureInfo.InvariantCulture)?.Trim() ?? string.Empty : string.Empty;
                                var quoteDate = NormalizeDateValue(quoteDateRaw);
                                if (string.IsNullOrWhiteSpace(quoteDate))
                                    quoteDate = globalQuoteDeliveryDate;

                                var date = ExcelDeliveryDateMergeService.ResolveEffectiveDeliveryDate(standardDate, quoteDate);
                                var location = locationCol > 0 ? Convert.ToString(allValues[row, locationCol], CultureInfo.InvariantCulture)?.Trim() ?? string.Empty : string.Empty;
                                if (string.IsNullOrWhiteSpace(location) || IsLikelyExcelDateSerial(location))
                                    location = globalDeliveryLocation;

                                var revision = revisionCol > 0
                                    ? Convert.ToString(allValues[row, revisionCol], CultureInfo.InvariantCulture)?.Trim() ?? string.Empty
                                    : string.Empty;

                                result.Add(new ExcelRowCandidate
                                {
                                    RowIndex = result.Count + 1,
                                    NormalizedCells = new List<string> { key },
                                    RawDrawingNumber = text,
                                    RevisionNumber = revision,
                                    DetectedArticleNumbers = ExtractArticleNumbers(new[] { articleNoValue }),
                                    ParentArticleNumber = ExtractParentArticleNumber(new[] { articleNoValue }),
                                    RequestedAmount = amount,
                                    RequestedDeliveryDate = date,
                                    QuoteDeliveryDate = quoteDate,
                                    RequestedDeliveryLocation = location
                                });
                            }
                        }
                    }
                }

                return result;
            }
            finally
            {
                if (workbook != null)
                {
                    workbook.Close(false);
                }

                if (excel != null)
                {
                    excel.Quit();
                }
            }
        }

        private IEnumerable<ExcelRowCandidate> ReadExcelRowsFromXls(string excelPath)
        {
            return ReadExcelRowsWithExcelInterop(excelPath);
        }

        private static string NormalizeHeader(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;

            var builder = new StringBuilder(value.Length);
            foreach (var ch in value.Trim().ToLowerInvariant())
            {
                if (char.IsLetterOrDigit(ch))
                    builder.Append(ch);
                else
                    builder.Append(' ');
            }

            var normalized = builder.ToString();
            while (normalized.Contains("  "))
                normalized = normalized.Replace("  ", " ");

            return normalized.Trim();
        }

        private static bool IsDrawingHeader(string value)
        {
            var n = NormalizeHeader(value);
            return n == "drawing" || n == "zeichnung";
        }

        private static bool IsArticleNumberHeader(string value)
        {
            var n = NormalizeHeader(value);
            return n == "articleno" || n == "article no" || n == "article number" || n == "artikelnummer";
        }

        private static bool IsAmountHeader(string value)
        {
            var n = NormalizeHeader(value);
            return n == "qty" || n == "quantity" || n == "menge" || n == "requested amount" || n.Contains("amount");
        }

        private static bool IsQuoteDateHeader(string value)
        {
            var n = NormalizeHeader(value);
            return n.Contains("according to quote")
                || n.Contains("ltg acc quote")
                || n.Contains("delivery date according to quote")
                || (n.Contains("angebot") && n.Contains("date"))
                || (n.Contains("quote") && n.Contains("delivery") && n.Contains("date"));
        }

        private static bool IsStandardDateHeader(string value)
        {
            if (IsQuoteDateHeader(value))
                return false;

            var n = NormalizeHeader(value);
            return n == "lieferdatum"
                || n == "leiferdatum"
                || n == "delivery date"
                || n == "requested delivery date"
                || (n.Contains("requested") && n.Contains("delivery") && n.Contains("date"))
                || (n.Contains("delivery") && n.Contains("date") && !n.Contains("quote"))
                || (n.Contains("liefer") && n.Contains("datum"))
                || (n.Contains("leifer") && n.Contains("datum"));
        }

        private static bool IsDateHeader(string value)
        {
            return IsStandardDateHeader(value) || IsQuoteDateHeader(value);
        }

        private static bool IsLocationHeader(string value)
        {
            var n = NormalizeHeader(value);
            return n == "lieferort"
                || n == "leiferort"
                || n == "ort"
                || n == "delivery place"
                || n == "requested delivery location"
                || n == "requested del location"
                || n == "requested del place"
                || (n.Contains("delivery") && n.Contains("location"))
                || (n.Contains("delivery") && n.Contains("place"))
                || (n.Contains("liefer") && n.Contains("ort"))
                || (n.Contains("leifer") && n.Contains("ort"));
        }

        private static bool IsRevisionHeader(string value)
        {
            var n = NormalizeHeader(value);
            return n == "rev"
                || n == "rev."
                || n == "rev no"
                || n == "revno"
                || n == "revision"
                || n == "revisions"
                || n == "revisionsnummer"
                || n == "index"
                || n == "drawing rev"
                || n == "drawing revision"
                || n == "zeichnungsindex"
                || n.Contains("revision")
                || (n.StartsWith("rev") && n.Length <= 8);
        }

        private static string GetNextColumnRef(string colRef)
        {
            if (string.IsNullOrWhiteSpace(colRef))
                return colRef;

            var chars = colRef.Trim().ToUpperInvariant().ToCharArray();
            var index = chars.Length - 1;
            while (index >= 0)
            {
                if (chars[index] < 'Z')
                {
                    chars[index]++;
                    return new string(chars);
                }

                chars[index] = 'A';
                index--;
            }

            return "A" + new string(chars);
        }

        private static string FormatBgWatermarkText(string drawingNumber, string revisionNumber)
        {
            if (string.IsNullOrWhiteSpace(drawingNumber))
                return string.Empty;

            var text = "BG: " + drawingNumber.Trim();
            var revision = NormalizeRevisionText(revisionNumber);
            if (!string.IsNullOrWhiteSpace(revision))
                text += " " + revision;

            return text;
        }

        private static string NormalizeRevisionText(string revisionNumber)
        {
            if (string.IsNullOrWhiteSpace(revisionNumber))
                return string.Empty;

            var rev = revisionNumber.Trim().ToUpperInvariant();
            rev = rev.Replace(" ", string.Empty);

            if (rev.StartsWith("REV", StringComparison.Ordinal))
            {
                var suffix = rev.Substring(3).TrimStart();
                if (suffix.Length > 0 && suffix.All(char.IsDigit))
                    return "R" + suffix.PadLeft(2, '0');

                return string.IsNullOrWhiteSpace(suffix) ? string.Empty : "R" + suffix;
            }

            if (rev.StartsWith("R", StringComparison.Ordinal) && rev.Length > 1)
                return rev;

            if (rev.All(char.IsDigit))
                return "R" + rev.PadLeft(2, '0');

            return rev;
        }

        private static string StripSequencePrefixes(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            var result = value.Trim();
            while (result.Length >= 3 && char.IsDigit(result[0]) && char.IsDigit(result[1]) && result[2] == '_')
            {
                result = result.Substring(3).Trim();
            }

            return result;
        }

        private static string StripRevSuffix(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return value;
            // Entfernt _REV00, _REV01, _R00, _R01, _V00 usw. am Ende
            var idx = value.LastIndexOf('_');
            if (idx > 0)
            {
                var suffix = value.Substring(idx + 1).ToUpperInvariant();
                if (suffix.StartsWith("REV") || suffix.StartsWith("R0") || suffix.StartsWith("R1") || suffix.StartsWith("V"))
                    return value.Substring(0, idx);
            }
            return value;
        }

        private static string NormalizeKey(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            var builder = new StringBuilder(value.Length);
            foreach (var ch in value.ToUpperInvariant())
            {
                if (char.IsLetterOrDigit(ch))
                {
                    builder.Append(ch);
                }
            }

            return builder.ToString();
        }

        private static bool IsLikelyDrawingNumber(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return false;

            var text = value.Trim().ToUpperInvariant();
            if (!text.Contains("-"))
                return false;

            return text.Any(char.IsLetter) && text.Any(char.IsDigit);
        }

        private static string SanitizeFileName(string value)
        {
            var invalidChars = Path.GetInvalidFileNameChars();
            var builder = new StringBuilder(value.Length);
            foreach (var ch in value)
            {
                builder.Append(invalidChars.Contains(ch) ? '_' : ch);
            }

            return builder.ToString().Trim();
        }

        private static string GetAvailableFilePath(string filePath)
        {
            if (!File.Exists(filePath))
            {
                return filePath;
            }

            var directory = Path.GetDirectoryName(filePath);
            var fileName = Path.GetFileNameWithoutExtension(filePath);
            var extension = Path.GetExtension(filePath);
            var index = 1;

            while (true)
            {
                var candidate = Path.Combine(directory, string.Format(CultureInfo.InvariantCulture, "{0} ({1}){2}", fileName, index, extension));
                if (!File.Exists(candidate))
                {
                    return candidate;
                }

                index++;
            }
        }

        private const string WatermarkEntrySeparator = "§§§";

        private static string EncodeWatermarkEntries(List<WatermarkEntry> entries)
        {
            if (entries == null || entries.Count == 0)
                return string.Empty;

            return string.Join(WatermarkEntrySeparator, entries.Select(e => string.Join("|",
                EscapeWatermarkValue(e.RequestedAmount),
                EscapeWatermarkValue(e.RequestedDeliveryDate),
                EscapeWatermarkValue(e.RequestedDeliveryLocation),
                EscapeWatermarkValue(e.BgDrawingNumber),
                EscapeWatermarkValue(e.OrderNumber))));
        }

        private static string EscapeWatermarkValue(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;

            return value.Replace("\\", "\\\\").Replace("|", "\\|").Trim();
        }

        private static readonly Regex ArticleRegex = new Regex(@"\bTGA\d+\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex TeslaPartArticleRegex = new Regex(
            @"\b(?:[A-Z]{2,6}-)?\d{4,6}-\d{2,6}(?:-[A-Z0-9]+)?\b",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex ParentArticleRegex = new Regex(@"verbaut\s*in\s*(TGA\d+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static List<string> ExtractArticleNumbers(IEnumerable<string> rowTexts)
        {
            var result = new List<string>();
            if (rowTexts == null)
                return result;

            foreach (var text in rowTexts)
            {
                if (string.IsNullOrWhiteSpace(text))
                    continue;

                // "verbaut in ..." enthält Parent-Artikel und darf NICHT als eigene Artikelnummer gewertet werden
                if (NormalizeHeader(text).Contains("verbaut in"))
                    continue;

                foreach (Match m in ArticleRegex.Matches(text))
                {
                    var v = m.Value.ToUpperInvariant();
                    if (!result.Contains(v))
                        result.Add(v);
                }

                foreach (Match m in TeslaPartArticleRegex.Matches(text))
                {
                    var v = m.Value.ToUpperInvariant();
                    if (!result.Contains(v))
                        result.Add(v);
                }
            }

            return result;
        }

        private static string ExtractParentArticleNumber(IEnumerable<string> rowTexts)
        {
            if (rowTexts == null)
                return string.Empty;

            foreach (var text in rowTexts)
            {
                if (string.IsNullOrWhiteSpace(text))
                    continue;

                var normalized = NormalizeHeader(text);
                if (!normalized.Contains("verbaut in"))
                    continue;

                // Wichtig: Parent ist die Artikelnummer NACH "verbaut in", nicht die erste in der Zelle
                var parentMatch = ParentArticleRegex.Match(text);
                if (parentMatch.Success && parentMatch.Groups.Count > 1)
                    return parentMatch.Groups[1].Value.ToUpperInvariant();

                // Fallback, falls Format ungewöhnlich ist
                var idx = normalized.IndexOf("verbaut in", StringComparison.Ordinal);
                var tail = idx >= 0 ? text.Substring(Math.Min(text.Length, idx + "verbaut in".Length)) : text;
                var articleInTail = ArticleRegex.Match(tail);
                if (articleInTail.Success)
                    return articleInTail.Value.ToUpperInvariant();
            }

            return string.Empty;
        }

        /// <summary>
        /// Thread-sichere Methode um Status-Text auszugeben.
        /// Kann von jedem Thread aufgerufen werden!
        /// </summary>
        private void SafeAppendStatus(string message)
        {
            // Immer zum UI-Thread delegieren, egal wo wir sind
            Dispatcher.Invoke(() =>
            {
                if (!string.IsNullOrWhiteSpace(StatusTextBoxBox.Text))
                    StatusTextBoxBox.AppendText(Environment.NewLine);
                StatusTextBoxBox.AppendText(message);
                StatusTextBoxBox.ScrollToEnd();
            });
        }

        /// <summary>
        /// Thread-sichere Methode um Fortschritt zu melden.
        /// Kann von jedem Thread aufgerufen werden!
        /// </summary>
        private void SafeSetProgress(int percent, string phaseText = null)
        {
            Dispatcher.Invoke(() => UpdateProgressUi(percent, phaseText));
        }

        private void SafeSetProgressFraction(double fraction, string phaseText = null)
        {
            var percent = (int)Math.Round(Math.Max(0d, Math.Min(1d, fraction)) * 100d, MidpointRounding.AwayFromZero);
            Dispatcher.Invoke(() => UpdateProgressUi(percent, phaseText));
        }

        // Die alte AppendStatus bleibt für Aufrufe vom UI-Thread
        private void AppendStatus(string message)
        {
            if (!Dispatcher.CheckAccess())
            {
                // Delegiere auf UI-Thread
                Dispatcher.Invoke(() => AppendStatus(message));
                return;
            }

            // Jetzt sind wir auf dem UI-Thread
            if (StatusTextBoxBox == null)
                return;

            if (!string.IsNullOrWhiteSpace(StatusTextBoxBox.Text))
            {
                StatusTextBoxBox.AppendText(Environment.NewLine);
            }

            StatusTextBoxBox.AppendText(message);
            StatusTextBoxBox.ScrollToEnd();
        }

        private sealed class PdfCandidate
        {
            public PdfCandidate(string fullPath, int order)
            {
                FullPath = fullPath;
                Order = order;
                OriginalName = StripSequencePrefixes(Path.GetFileNameWithoutExtension(fullPath));
                SearchKey = NormalizeKey(StripRevSuffix(OriginalName));
                DisplayName = Path.GetFileName(fullPath);
            }

            public string FullPath { get; }

            public string OriginalName { get; }

            public string SearchKey { get; }

            public string DisplayName { get; }

            public int Order { get; }
        }

        private sealed class ExcelRowCandidate
        {
            public int RowIndex { get; set; }

            public List<string> NormalizedCells { get; set; }

            public string RawDrawingNumber { get; set; }

            public string RevisionNumber { get; set; }

            public List<string> DetectedArticleNumbers { get; set; }

            public string ParentArticleNumber { get; set; }

            public string RequestedAmount { get; set; }

            public string RequestedDeliveryDate { get; set; }

            public string QuoteDeliveryDate { get; set; }

            public string RequestedDeliveryLocation { get; set; }
        }

        private sealed class BgExcelInfo
        {
            public string DrawingNumber { get; set; }

            public string RevisionNumber { get; set; }
        }

        private sealed class WatermarkEntry
        {
            public string RequestedAmount { get; set; }

            public string RequestedDeliveryDate { get; set; }

            public string RequestedDeliveryLocation { get; set; }

            public string BgDrawingNumber { get; set; }

            public string OrderNumber { get; set; }
        }

        private sealed class RenamingItem
        {
            public string SourcePath { get; set; }

            public string DrawingNumber { get; set; }

            public int ExcelRowIndex { get; set; }

            public int PdfOrder { get; set; }

            public string RequestedAmount { get; set; }

            public string RequestedDeliveryDate { get; set; }

            public string RequestedDeliveryLocation { get; set; }

            public List<WatermarkEntry> WatermarkEntries { get; set; }
        }

        private void CreateDataBackup_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                using (var dialog = new Forms.FolderBrowserDialog())
                {
                    dialog.Description = "Zielordner für die komplette Datensicherung wählen";
                    dialog.ShowNewFolderButton = true;

                    var defaultRoot = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                        AppInfo.ProductName + " Datensicherung");
                    if (Directory.Exists(defaultRoot))
                        dialog.SelectedPath = defaultRoot;

                    if (dialog.ShowDialog() != Forms.DialogResult.OK)
                        return;

                    AppendStatus("Datensicherung gestartet...");
                    ProcessButtonBox.IsEnabled = false;
                    MergePrintButtonBox.IsEnabled = false;
                    DeleteProcessedButtonBox.IsEnabled = false;

                    var request = new DataBackupRequest
                    {
                        DestinationRoot = dialog.SelectedPath,
                        ExcelPath = ExcelPathBox?.Text?.Trim() ?? string.Empty,
                        PdfFolder = PdfFolderBox?.Text?.Trim() ?? string.Empty,
                        OrderVerificationPdfPath = _orderVerificationPdfPath,
                        CreateZip = true
                    };

                    var result = DataBackupService.CreateBackup(request);

                    if (result.Success)
                    {
                        AppendStatus("✓ " + result.SummaryText);
                        AppendStatus("Ordner: " + result.BackupFolder);
                        foreach (var section in result.IncludedSections)
                            AppendStatus("  · " + section);
                        foreach (var warning in result.Warnings)
                            AppendStatus("  ⚠ " + warning);

                        MessageBox.Show(
                            result.SummaryText + Environment.NewLine + Environment.NewLine +
                            "Ordner:" + Environment.NewLine + result.BackupFolder +
                            (string.IsNullOrWhiteSpace(result.ZipPath)
                                ? string.Empty
                                : Environment.NewLine + Environment.NewLine + "ZIP:" + Environment.NewLine + result.ZipPath),
                            AppInfo.ProductName + " – Datensicherung",
                            MessageBoxButton.OK,
                            MessageBoxImage.Information);
                    }
                    else
                    {
                        AppendStatus("❌ Datensicherung fehlgeschlagen: " + result.ErrorMessage);
                        MessageBox.Show(
                            result.ErrorMessage,
                            AppInfo.ProductName + " – Datensicherung",
                            MessageBoxButton.OK,
                            MessageBoxImage.Error);
                    }
                }
            }
            catch (Exception ex)
            {
                AppendStatus("❌ Datensicherung fehlgeschlagen: " + ex.Message);
                MessageBox.Show(
                    ex.Message,
                    AppInfo.ProductName + " – Datensicherung",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
            finally
            {
                ProcessButtonBox.IsEnabled = true;
                MergePrintButtonBox.IsEnabled = true;
                DeleteProcessedButtonBox.IsEnabled = true;
            }
        }

        private void About_Click(object sender, RoutedEventArgs e)
        {
            AboutWindow aboutWindow = new AboutWindow
            {
                Owner = this,
                WindowStartupLocation = WindowStartupLocation.CenterOwner
            };
            ThemeWindowService.Attach(aboutWindow);
            aboutWindow.ShowDialog();
        }

        private void OpenManual_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var baseDir = AppDomain.CurrentDomain.BaseDirectory;
                var candidates = new[]
                {
                    Path.Combine(baseDir, "BEDIENUNGSANLEITUNG.md"),
                    Path.Combine(baseDir, "BEDIENUNGSANLEITUNG.txt"),
                    Path.Combine(baseDir, "..", "BEDIENUNGSANLEITUNG.md"),
                    Path.Combine(baseDir, "..", "BEDIENUNGSANLEITUNG.txt"),
                    Path.Combine(baseDir, "..", "..", "BEDIENUNGSANLEITUNG.md"),
                    Path.Combine(baseDir, "..", "..", "BEDIENUNGSANLEITUNG.txt"),
                    Path.Combine(baseDir, "..", "..", "..", "BEDIENUNGSANLEITUNG.md"),
                    Path.Combine(baseDir, "..", "..", "..", "BEDIENUNGSANLEITUNG.txt"),
                    Path.Combine(Environment.CurrentDirectory, "BEDIENUNGSANLEITUNG.md"),
                    Path.Combine(Environment.CurrentDirectory, "BEDIENUNGSANLEITUNG.txt")
                };

                var manualPath = candidates
                    .Select(Path.GetFullPath)
                    .FirstOrDefault(File.Exists);

                var manualText = !string.IsNullOrWhiteSpace(manualPath)
                    ? File.ReadAllText(manualPath, Encoding.UTF8)
                    : "BEDIENUNGSANLEITUNG - " + AppInfo.ProductName + Environment.NewLine
                      + "=====================================================" + Environment.NewLine + Environment.NewLine
                      + "1) Dateien laden:" + Environment.NewLine
                      + "   - PDFs + Excel oder ZIP per Drag & Drop in die App ziehen." + Environment.NewLine
                      + "2) Verarbeitung:" + Environment.NewLine
                      + "   - 'Sortieren & umbenennen' starten." + Environment.NewLine
                      + "3) Optional:" + Environment.NewLine
                      + "   - Wasserzeichen An/Aus schalten." + Environment.NewLine
                      + "   - 'Zusammenfügen & Vorschau' für Gesamt-PDF nutzen." + Environment.NewLine
                      + "4) Aufräumen:" + Environment.NewLine
                      + "   - 'Bearbeitete Aufträge löschen' für den nächsten Auftrag.";

                var manualWindow = new Window
                {
                    Title = "Bedienungsanleitung",
                    Owner = this,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner,
                    Width = 980,
                    Height = 700,
                    MinWidth = 760,
                    MinHeight = 520,
                    Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(16, 22, 36))
                };

                var textBox = new System.Windows.Controls.TextBox
                {
                    Text = manualText,
                    IsReadOnly = true,
                    AcceptsReturn = true,
                    TextWrapping = TextWrapping.Wrap,
                    VerticalScrollBarVisibility = System.Windows.Controls.ScrollBarVisibility.Auto,
                    HorizontalScrollBarVisibility = System.Windows.Controls.ScrollBarVisibility.Auto,
                    BorderThickness = new Thickness(0),
                    Background = System.Windows.Media.Brushes.Transparent,
                    Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(234, 242, 255)),
                    FontFamily = new System.Windows.Media.FontFamily("Consolas"),
                    FontSize = 13,
                    Padding = new Thickness(10)
                };

                var border = new System.Windows.Controls.Border
                {
                    Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(14, 21, 35)),
                    BorderBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(45, 58, 88)),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(8),
                    Margin = new Thickness(18),
                    Child = textBox
                };

                manualWindow.Content = border;
                manualWindow.ShowDialog();
            }
            catch (Exception ex)
            {
                AppendStatus("⚠ Bedienungsanleitung konnte nicht geöffnet werden: " + ex.Message);
            }
        }
    }
}
