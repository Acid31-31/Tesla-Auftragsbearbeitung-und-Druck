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
using PDF_umbenenen.Platform.Core;
using PDF_umbenenen.Platform.UI;
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
        private System.Windows.Controls.TextBox OrderVerificationPdfPathBox => FindName("OrderVerificationPdfPathTextBox") as System.Windows.Controls.TextBox;
        private System.Windows.Controls.CheckBox WatermarkEnabledCheckBoxBox => FindName("WatermarkEnabledCheckBox") as System.Windows.Controls.CheckBox;
        private string _orderVerificationPdfPath = string.Empty;
        private IList<OrderVerificationLine> _cachedOrderVerificationLines;
        private OrderVerificationReport _cachedOrderVerificationReport;
        private IList<OrderExcelRow> _cachedOrderVerificationExcelRows;
        private string _cachedOrderVerificationExcelPath = string.Empty;
        private string _cachedOrderVerificationOrderPdfPath = string.Empty;
        private const string OrderVerificationDropHint = "Noch keine Prüf-PDF geladen";
        private System.Windows.Controls.CheckBox IncludeSubfoldersBox => FindName("IncludeSubfoldersCheckBox") as System.Windows.Controls.CheckBox;
        private System.Windows.Controls.TextBlock DroppedExtensionsBlock => FindName("DroppedExtensionsTextBlock") as System.Windows.Controls.TextBlock;
        private System.Windows.Controls.TextBlock WorkflowStatusBlock => FindName("WorkflowStatusTextBlock") as System.Windows.Controls.TextBlock;
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

        private OfflineAiOrchestrator _aiOrchestrator;
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
        private List<ExcelRowCandidate> _cachedVerificationExcelRows;
        private string _cachedVerificationExcelPath;
        private readonly Dictionary<string, string> _manualDeliveryDateOverrides =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public MainWindow()
        {
            LoadXaml();
            Icon = ThemeWindowService.GetAppIcon();
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
                versionBlock.Text = "Version " + AppInfo.ApplicationVersion;
            }

            SyncWatermarkToggleFromSettings();
            UpdateOrderVerificationPdfDisplay();

            Loaded += MainWindow_Loaded;
            EnsureOrderNumberTextBoxEditable();

            _stopwatchTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(1)
            };
            _stopwatchTimer.Tick += StopwatchTimer_Tick;
        }

        private void EnsureOrderNumberTextBoxEditable()
        {
            var box = OrderNumberBox;
            if (box == null)
                return;

            box.IsReadOnly = false;
            box.IsEnabled = true;
            box.Focusable = true;
            box.IsHitTestVisible = true;
            box.IsTabStop = true;
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

                GitHubUpdateService.EnsureUpdateTokenFromInstallFolder();

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
                    var installed = AppInfo.DisplayVersion + " (Build " + AppInfo.ApplicationVersion.Build + ")";
                    var remote = string.IsNullOrWhiteSpace(update.ReleaseTag)
                        ? "unbekannt"
                        : update.ReleaseTag;
                    var tokenHint = AppInfo.GitHubUpdatesArePublic || GitHubUpdateService.HasGitHubToken()
                        ? string.Empty
                        : "\n\nHinweis: Kein github.token gefunden – bei privatem GitHub-Repository werden Updates blockiert.";

                    var message = string.Format(
                        "Kein neueres Update gefunden.\n\nInstalliert: {0}\nNeuestes GitHub-Release: {1}\n\n"
                        + "Wenn Sie eine neuere Entwicklungsversion erwarten, wurde diese noch nicht auf GitHub veröffentlicht.{2}",
                        installed,
                        remote,
                        tokenHint);

                    AppendStatus("✓ Update-Prüfung: Installiert " + AppInfo.DisplayVersion + ", GitHub " + remote);
                    MessageBox.Show(this, message, "Update-Prüfung", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else if (!update.UpdateAvailable && !string.IsNullOrWhiteSpace(update.ReleaseTag))
                {
                    AppendStatus("✓ Update-Prüfung: " + AppInfo.DisplayVersion + " ist aktuell (GitHub: " + update.ReleaseTag + ").");
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
            await CheckForUpdatesFromUiAsync();
        }

        public async void CheckForUpdatesFromUi()
        {
            await CheckForUpdatesFromUiAsync();
        }

        private async Task CheckForUpdatesFromUiAsync()
        {
            AppendStatus("Prüfe GitHub auf Updates…");
            GitHubUpdateService.EnsureUpdateTokenFromInstallFolder();
            await CheckForUpdatesAsync(showIfCurrent: true);
        }

        private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            EnsureOrderNumberTextBoxEditable();
            ThemeWindowService.Attach(this);
            ThemeWindowService.MaximizeToWorkArea(this);
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
            _aiOrchestrator = PlatformHost.Facade?.LegacyAi ?? new OfflineAiOrchestrator();
            PlatformHost.Facade?.SyncSettings();
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

        private void OpenPlatformDashboard_Click(object sender, RoutedEventArgs e)
        {
            var dashboard = new PlatformDashboardWindow
            {
                Owner = this,
                WindowStartupLocation = WindowStartupLocation.CenterOwner
            };
            ThemeWindowService.Attach(dashboard);
            dashboard.Show();
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
            _aiOrchestrator.Settings.LocalOcrEnabled = dlg.Settings.LocalOcrEnabled;
            _aiOrchestrator.Settings.TesseractPath = dlg.Settings.TesseractPath;
            _aiOrchestrator.Settings.OcrLanguages = dlg.Settings.OcrLanguages;
            _aiOrchestrator.Settings.LocalVisionEnabled = dlg.Settings.LocalVisionEnabled;
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
            _watermarkSettings.MaterialThicknessPlacement = MaterialThicknessPlacement.BottomLeftFixed;
            _watermarkSettings.BgWatermarkEnabled = dlg.Settings.BgWatermarkEnabled;
            _watermarkSettings.BgPlacement = BgPlacement.RightVerticalFixed;
            _watermarkSettings.ExtractPowderCoating = dlg.Settings.ExtractPowderCoating;
            _watermarkSettings.PowderCoatingShowRalNumber = dlg.Settings.PowderCoatingShowRalNumber;
            _watermarkSettings.ExtractAnodizing = dlg.Settings.ExtractAnodizing;
            _watermarkSettings.DeliveryDatePosition = dlg.Settings.DeliveryDatePosition;
            _watermarkSettings.MirrorBottomToTopBand = dlg.Settings.MirrorBottomToTopBand;
            WatermarkSettingsStore.Save(_watermarkSettings);
            SyncWatermarkToggleFromSettings();
            UpdateAiStatusUi();
            RefreshDroppedFilesDisplay();
            AppendStatus(string.Format(CultureInfo.InvariantCulture,
                "Wasserzeichen-Einstellungen gespeichert: {0}, intelligent {1}, Werkstoff {2}, Stärke {3}, BG {4}, Pulver/RAL {5}, Elox {6}, Liefertermin {7}, unten wie oben {8}.",
                _watermarkSettings.WatermarksEnabled ? "An" : "Aus",
                _watermarkSettings.UseSmartPlacement ? "An" : "Aus",
                _watermarkSettings.ExtractMaterialType ? "An" : "Aus",
                _watermarkSettings.ExtractMaterialThickness ? "An" : "Aus",
                _watermarkSettings.BgWatermarkEnabled ? "An" : "Aus",
                _watermarkSettings.ExtractPowderCoating ? "An" : "Aus",
                _watermarkSettings.ExtractAnodizing ? "An" : "Aus",
                WatermarkSettings.GetDeliveryDatePositionLabel(_watermarkSettings.DeliveryDatePosition),
                _watermarkSettings.MirrorBottomToTopBand ? "An" : "Aus"));
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

            OpenMergedPdfPreviewForCorrection(orderedFiles, pdfFolder);
        }

        private void OpenMergedPdfPreviewForCorrection(IList<string> orderedFiles, string pdfFolder)
        {
            string mergedPath = _lastMergedPdfPath;
            List<MergedPdfPageEntry> pageMap = null;

            if (string.IsNullOrWhiteSpace(mergedPath) || !File.Exists(mergedPath))
            {
                if (!PdfWatermarkService.IsAvailable())
                {
                    AppendStatus("❌ Vorschau nicht möglich: iTextSharp fehlt im App-Ordner.");
                    return;
                }

                try
                {
                    mergedPath = Path.Combine(
                        pdfFolder,
                        "_GESAMT_" + DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture) + ".pdf");
                    PdfWatermarkService.MergePdfFiles(orderedFiles.ToList(), mergedPath);
                    pageMap = MergedPdfPageMapService.Build(orderedFiles);
                    MergedPdfPageMapService.Save(mergedPath, pageMap);
                    _lastMergedPdfPath = mergedPath;
                    AppendStatus("✓ Gesamt-PDF erstellt: " + Path.GetFileName(mergedPath));
                }
                catch (Exception ex)
                {
                    AppendStatus("Fehler beim Erstellen der Vorschau: " + ex.Message);
                    return;
                }
            }

            OpenMergedPdfPreview(mergedPath, orderedFiles, pdfFolder, pageMap);
        }

        private void OpenWatermarkEditor(IList<string> orderedFiles, string pdfFolder, IEnumerable<string> editorPaths = null)
        {
            OpenMergedPdfPreviewForCorrection(orderedFiles, pdfFolder);
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

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 2)
            {
                ThemeWindowService.ToggleWorkAreaMaximize(this);
                return;
            }

            try
            {
                DragMove();
            }
            catch
            {
                // DragMove kann fehlschlagen, wenn Maustaste nicht mehr gedrückt ist.
            }
        }

        private void TitleBarMinimize_Click(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState.Minimized;
        }

        private void TitleBarMaximize_Click(object sender, RoutedEventArgs e)
        {
            ThemeWindowService.ToggleWorkAreaMaximize(this);
        }

        private void TitleBarClose_Click(object sender, RoutedEventArgs e)
        {
            Close();
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
            BrowsePdfFolder_Click(sender, e);
        }

        private void BrowsePdfFolder_Click(object sender, RoutedEventArgs e)
        {
            using (var dialog = new Forms.FolderBrowserDialog())
            {
                dialog.Description = "PDF-Ablage-Ordner wählen";
                dialog.ShowNewFolderButton = true;

                var current = PdfFolderBox?.Text?.Trim();
                if (!string.IsNullOrWhiteSpace(current) && Directory.Exists(current))
                    dialog.SelectedPath = current;
                else
                    dialog.SelectedPath = PathStore.GetOrCreateDefaultWorkspaceFolder();

                if (dialog.ShowDialog() != Forms.DialogResult.OK)
                    return;

                PdfFolderBox.Text = dialog.SelectedPath;
                PathStore.SaveFolder(dialog.SelectedPath);
                RefreshDroppedFilesDisplay();
            }
        }

        private void BrowseOrderPdf_Click(object sender, RoutedEventArgs e)
        {
            var initialDirectory = PathStore.GetOrCreateDefaultWorkspaceFolder();
            if (!string.IsNullOrWhiteSpace(_orderVerificationPdfPath) && File.Exists(_orderVerificationPdfPath))
                initialDirectory = Path.GetDirectoryName(_orderVerificationPdfPath);

            var dialog = new OpenFileDialog
            {
                Filter = "PDF-Dateien (*.pdf)|*.pdf|Alle Dateien (*.*)|*.*",
                Title = "Bestell-PDF auswählen",
                InitialDirectory = initialDirectory
            };

            if (dialog.ShowDialog() != true)
                return;

            _orderVerificationPdfPath = dialog.FileName;
            UpdateOrderVerificationPdfDisplay();
            PathStore.SaveOrderVerificationPdf(dialog.FileName);
        }

        private void OpenOrderPdf_Click(object sender, RoutedEventArgs e)
        {
            if (!HasOrderVerificationPdfLoaded())
            {
                MessageBox.Show(this, "Keine Bestell-PDF geladen.", "DOK-V01", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            try
            {
                Process.Start(_orderVerificationPdfPath);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "PDF konnte nicht geöffnet werden: " + ex.Message, "DOK-V01", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void WatermarkEnabled_Changed(object sender, RoutedEventArgs e)
        {
            if (_watermarkSettings == null)
                _watermarkSettings = WatermarkSettingsStore.Load();

            if (WatermarkEnabledCheckBoxBox == null)
                return;

            _watermarkSettings.WatermarksEnabled = WatermarkEnabledCheckBoxBox.IsChecked == true;
            WatermarkSettingsStore.Save(_watermarkSettings);
            RefreshDroppedFilesDisplay();
        }

        private void SyncWatermarkToggleFromSettings()
        {
            if (_watermarkSettings == null)
                _watermarkSettings = WatermarkSettingsStore.Load();

            if (WatermarkEnabledCheckBoxBox != null)
                WatermarkEnabledCheckBoxBox.IsChecked = _watermarkSettings.WatermarksEnabled;
        }

        private void HeaderSettings_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new AppSettingsWindow(this)
            {
                Owner = this,
                WindowStartupLocation = WindowStartupLocation.CenterOwner
            };
            ThemeWindowService.Attach(dlg);
            dlg.ShowDialog();
        }

        internal bool IsDarkThemeActive => _isDarkTheme;

        internal void OpenAiSettingsDialog()
        {
            AiSettingsMenu_Click(this, new RoutedEventArgs());
        }

        internal void OpenWatermarkSettingsDialog()
        {
            WatermarkSettingsMenu_Click(this, new RoutedEventArgs());
        }

        internal void OpenCorrectWatermarkDialog()
        {
            CorrectWatermarkMenu_Click(this, new RoutedEventArgs());
        }

        internal void OpenManageCustomersDialog()
        {
            ManageCustomersMenu_Click(this, new RoutedEventArgs());
        }

        internal void ToggleThemeFromSettings()
        {
            _isDarkTheme = !_isDarkTheme;
            ApplyTheme(_isDarkTheme);
        }

        private void NavOverview_Click(object sender, RoutedEventArgs e)
        {
            SetActiveSidebarNav(FindName("NavOverviewButton") as System.Windows.Controls.Button);
        }

        private void NavLog_Click(object sender, RoutedEventArgs e)
        {
            SetActiveSidebarNav(FindName("NavLogButton") as System.Windows.Controls.Button);
            OpenProtocolDialog();
        }

        internal void OpenProtocolDialog()
        {
            var excelPath = ExcelPathBox.Text.Trim();
            var pdfFolder = PdfFolderBox.Text.Trim();

            if (!TryResolveExcelPath(ref excelPath, AppendStatus))
            {
                MessageBox.Show(this,
                    "Für das Protokoll wird eine Excel-Datei in der Ablage benötigt.\r\n\r\n"
                    + "Bitte Excel und Bestell-PDF laden und einmal sortieren – danach steht hier der Abgleich.",
                    "Protokoll",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                SetActiveSidebarNav(FindName("NavOverviewButton") as System.Windows.Controls.Button);
                return;
            }

            if (string.IsNullOrWhiteSpace(pdfFolder) || !Directory.Exists(pdfFolder))
            {
                MessageBox.Show(this,
                    "Die Ablage ist leer oder nicht verfügbar.\r\n\r\n"
                    + "Bitte zuerst Excel, Bestell-PDF und Zeichnungen laden.",
                    "Protokoll",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                SetActiveSidebarNav(FindName("NavOverviewButton") as System.Windows.Controls.Button);
                return;
            }

            var orderPdfPath = ResolveOrderVerificationPdfPath(pdfFolder);
            if (string.IsNullOrWhiteSpace(orderPdfPath) || !File.Exists(orderPdfPath))
            {
                MessageBox.Show(this,
                    "Für das Protokoll wird eine Bestell-PDF benötigt.\r\n\r\n"
                    + "Bitte die Bestell-PDF bei „Prüf-PDF“ ablegen oder erneut sortieren.",
                    "Protokoll",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                SetActiveSidebarNav(FindName("NavOverviewButton") as System.Windows.Controls.Button);
                return;
            }

            var excelRows = LoadExcelRowsForVerification(excelPath);
            ApplyManualDeliveryDateOverrides(excelRows);
            if (excelRows.Count == 0)
            {
                MessageBox.Show(this,
                    "In der Excel-Datei wurden keine Zeichnungszeilen gefunden.",
                    "Protokoll",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                SetActiveSidebarNav(FindName("NavOverviewButton") as System.Windows.Controls.Button);
                return;
            }

            var searchOption = IncludeSubfoldersBox.IsChecked == true
                ? SearchOption.AllDirectories
                : SearchOption.TopDirectoryOnly;

            var report = RunOrderVerificationReport(excelPath, orderPdfPath, excelRows, pdfFolder, searchOption);
            var orderRows = excelRows.Select(ToOrderExcelRow).ToList();

            Action<int, string, string> persistDeliveryDate = (rowIndex, drawingNumber, date) =>
            {
                PersistDeliveryDateOverride(excelRows, rowIndex, drawingNumber, date);
            };

            Action<string> openDrawingPdf = drawingNumber =>
                OpenDrawingPdfFromProtocol(drawingNumber, pdfFolder, searchOption, orderPdfPath);

            var dialogResult = OrderVerificationReportWindow.ShowDialog(
                this,
                report,
                orderRows,
                Path.GetFileName(excelPath),
                Path.GetFileName(orderPdfPath),
                excelPath,
                orderPdfPath,
                offerContinueOnIssues: false,
                persistDeliveryDate: persistDeliveryDate,
                openDrawingPdf: openDrawingPdf);

            SyncDeliveryDatesAfterVerificationDialog(excelRows, dialogResult);
        }

        private void SetActiveSidebarNav(System.Windows.Controls.Button activeButton)
        {
            var overview = FindName("NavOverviewButton") as System.Windows.Controls.Button;
            var log = FindName("NavLogButton") as System.Windows.Controls.Button;
            if (overview != null)
                overview.Style = (Style)FindResource(activeButton == overview ? "SidebarBtnActive" : "SidebarBtn");
            if (log != null)
                log.Style = (Style)FindResource(activeButton == log ? "SidebarBtnActive" : "SidebarBtn");
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
            HandleFileDrop(e);
        }

        private void Window_Drop(object sender, DragEventArgs e)
        {
            HandleFileDrop(e);
        }

        private bool HasOrderVerificationPdfLoaded()
        {
            return !string.IsNullOrWhiteSpace(_orderVerificationPdfPath) && File.Exists(_orderVerificationPdfPath);
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
            RefreshDroppedFilesDisplay();
        }

        private void UpdateOrderVerificationPdfDisplay()
        {
            var pathBox = OrderVerificationPdfPathBox;
            var block = OrderVerificationPdfStatusBlock;

            if (!HasOrderVerificationPdfLoaded())
            {
                if (pathBox != null)
                {
                    pathBox.Text = string.Empty;
                    pathBox.Foreground = (System.Windows.Media.Brush)FindResource("TextSecondary");
                }

                if (block != null)
                {
                    block.Text = OrderVerificationDropHint;
                    block.Foreground = (System.Windows.Media.Brush)FindResource("TextSecondary");
                }

                return;
            }

            var displayName = Path.GetFileName(_orderVerificationPdfPath);
            if (string.IsNullOrWhiteSpace(displayName))
                displayName = _orderVerificationPdfPath;

            if (pathBox != null)
            {
                pathBox.Text = _orderVerificationPdfPath;
                pathBox.Foreground = (System.Windows.Media.Brush)FindResource("TextPrimary");
            }

            if (block != null)
            {
                block.Text = "✓ " + displayName;
                block.Foreground = (System.Windows.Media.Brush)FindResource("AccentBrush");
                block.ToolTip = _orderVerificationPdfPath;
            }
        }

        private void ResetOrderVerificationPdfDisplay()
        {
            _orderVerificationPdfPath = string.Empty;
            _cachedOrderVerificationLines = null;
            _cachedOrderVerificationReport = null;
            _cachedOrderVerificationExcelRows = null;
            _cachedOrderVerificationExcelPath = string.Empty;
            _cachedOrderVerificationOrderPdfPath = string.Empty;
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
            var orderPdfCopied = false;
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
                            copiedCount += CopySupportedFileToWorkspace(file, targetFolder, ref excelCopied, ref orderPdfCopied, ref ignoredCount);
                        }

                        continue;
                    }

                    copiedCount += CopySupportedFileToWorkspace(sourcePath, targetFolder, ref excelCopied, ref orderPdfCopied, ref ignoredCount);
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

            if (!orderPdfCopied)
                orderPdfCopied = TryAutoDetectOrderVerificationPdf(targetFolder);

            if (orderPdfCopied)
            {
                var orderPath = OrderVerificationFileHelper.GetCanonicalPath(targetFolder);
                if (File.Exists(orderPath))
                {
                    _orderVerificationPdfPath = orderPath;
                    PathStore.SaveOrderVerificationPdf(orderPath);
                    UpdateOrderVerificationPdfDisplay();
                    AppendStatus("✓ Prüf-PDF automatisch erkannt: " + Path.GetFileName(orderPath));
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

            var pdfCount = Directory.GetFiles(targetFolder, "*.pdf", SearchOption.TopDirectoryOnly)
                .Count(f => !Path.GetFileName(f).StartsWith("_", StringComparison.OrdinalIgnoreCase)
                            && !OrderVerificationFileHelper.IsOrderVerificationFileName(f));
            AppendStatus($"ℹ Ablage-Ordner enthält jetzt {pdfCount} PDF-Datei(en).");

            var resolvedExcel = ExcelPathBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(resolvedExcel) || !File.Exists(resolvedExcel))
                resolvedExcel = ExcelDeliveryDateMergeService.ResolvePrimaryExcelPath(targetFolder);

            if (!string.IsNullOrWhiteSpace(resolvedExcel) && File.Exists(resolvedExcel))
            {
                ExcelPathBox.Text = resolvedExcel;
                PathStore.SaveExcel(resolvedExcel);
            }

            if (!string.IsNullOrWhiteSpace(resolvedExcel) && File.Exists(resolvedExcel) && pdfCount > 0)
                AppendStatus("▶ Bereit: Klicken Sie auf „Sortieren & umbenennen“ (Excel-Reihenfolge + alle Wasserzeichen).");

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
                UpdateWorkflowStatus(string.Empty, 0);
                UpdateFooterStatus(string.Empty, 0, 0);
                RefreshLoadedDataOverview(string.Empty, string.Empty);
                return;
            }

            var searchOption = SearchOption.AllDirectories;

            var files = Directory.GetFiles(folder, "*.*", searchOption);
            block.Text = FormatWorkspaceFileSummary(files, folder, "— noch keine Dateien —");

            var excelPath = ExcelPathBox?.Text?.Trim();
            if (string.IsNullOrWhiteSpace(excelPath) || !File.Exists(excelPath))
                excelPath = ExcelDeliveryDateMergeService.ResolvePrimaryExcelPath(folder);

            if (!string.IsNullOrWhiteSpace(excelPath) && File.Exists(excelPath))
            {
                ExcelPathBox.Text = excelPath;
                PathStore.SaveExcel(excelPath);
            }

            var pdfCount = Directory.GetFiles(folder, "*.pdf", SearchOption.TopDirectoryOnly)
                .Count(f => !Path.GetFileName(f).StartsWith("_", StringComparison.OrdinalIgnoreCase)
                            && !OrderVerificationFileHelper.IsOrderVerificationFileName(f)
                            && !string.Equals(Path.GetFileName(f), OrderVerificationFileHelper.CanonicalFileName, StringComparison.OrdinalIgnoreCase));

            UpdateWorkflowStatus(excelPath, pdfCount);
            UpdateFooterStatus(excelPath, pdfCount, files.Length);
            RefreshLoadedDataOverview(folder, excelPath);
        }

        private void UpdateFooterStatus(string excelPath, int pdfCount, int totalFiles)
        {
            var countBlock = FindName("LoadedFilesCountTextBlock") as System.Windows.Controls.TextBlock;
            if (countBlock != null)
                countBlock.Text = totalFiles + " Dateien geladen";

            var readyBlock = FindName("ReadyStatusTextBlock") as System.Windows.Controls.TextBlock;
            if (readyBlock == null)
                return;

            var hasExcel = !string.IsNullOrWhiteSpace(excelPath) && File.Exists(excelPath);
            if (hasExcel && pdfCount > 0)
            {
                readyBlock.Text = "Bereit zum Sortieren";
                readyBlock.Foreground = (System.Windows.Media.Brush)FindResource("AccentBrush");
            }
            else
            {
                readyBlock.Text = "Bereit";
                readyBlock.Foreground = (System.Windows.Media.Brush)FindResource("TextSecondary");
            }
        }

        private void RefreshLoadedDataOverview(string folder, string excelPath)
        {
            try
            {
                var countBlock = FindName("OverviewPdfCountTextBlock") as System.Windows.Controls.TextBlock;
                var ring = FindName("OverviewPdfCountRing") as System.Windows.Shapes.Ellipse;
                var excelDot = FindName("OverviewExcelStatusDot") as System.Windows.Shapes.Ellipse;
                var excelStatus = FindName("OverviewExcelStatusTextBlock") as System.Windows.Controls.TextBlock;
                var excelName = FindName("OverviewExcelFileNameTextBlock") as System.Windows.Controls.TextBlock;
                var orderDot = FindName("OverviewOrderPdfStatusDot") as System.Windows.Shapes.Ellipse;
                var orderStatus = FindName("OverviewOrderPdfStatusTextBlock") as System.Windows.Controls.TextBlock;
                var orderName = FindName("OverviewOrderPdfFileNameTextBlock") as System.Windows.Controls.TextBlock;
                var drawingsHeader = FindName("OverviewDrawingsHeaderTextBlock") as System.Windows.Controls.TextBlock;
                var tiles = FindName("OverviewPdfTilesItemsControl") as System.Windows.Controls.ItemsControl;
                if (countBlock == null || tiles == null)
                    return;

                var orderPdfPath = _orderVerificationPdfPath;
                if ((string.IsNullOrWhiteSpace(orderPdfPath) || !File.Exists(orderPdfPath))
                    && !string.IsNullOrWhiteSpace(folder) && Directory.Exists(folder))
                {
                    var canonical = OrderVerificationFileHelper.GetCanonicalPath(folder);
                    if (File.Exists(canonical))
                        orderPdfPath = canonical;
                }

                var drawingPdfs = (!string.IsNullOrWhiteSpace(folder) && Directory.Exists(folder))
                    ? Directory.GetFiles(folder, "*.pdf", SearchOption.TopDirectoryOnly)
                        .Where(f => !Path.GetFileName(f).StartsWith("_", StringComparison.OrdinalIgnoreCase)
                                    && !OrderVerificationFileHelper.IsOrderVerificationFileName(f))
                        .OrderBy(f => Path.GetFileName(f), StringComparer.OrdinalIgnoreCase)
                        .ToList()
                    : new List<string>();

                countBlock.Text = drawingPdfs.Count.ToString();
                if (drawingsHeader != null)
                    drawingsHeader.Text = "Zeichnungen (" + drawingPdfs.Count + ")";

                if (ring != null)
                {
                    var ratio = drawingPdfs.Count <= 0 ? 0.0 : Math.Min(1.0, drawingPdfs.Count / 40.0);
                    const double radius = (100.0 - 9.0) / 2.0;
                    var circumference = 2.0 * Math.PI * radius;
                    var dash = Math.Max(0.5, circumference * ratio);
                    var gap = Math.Max(0.5, circumference - dash);
                    ring.StrokeDashArray = new System.Windows.Media.DoubleCollection
                    {
                        dash / Math.Max(1.0, ring.StrokeThickness),
                        gap / Math.Max(1.0, ring.StrokeThickness)
                    };
                    ring.Visibility = drawingPdfs.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
                }

                var hasExcel = !string.IsNullOrWhiteSpace(excelPath) && File.Exists(excelPath);
                var accent = (System.Windows.Media.Brush)FindResource("AccentBrush");
                var secondary = (System.Windows.Media.Brush)FindResource("TextSecondary");
                if (excelDot != null)
                    excelDot.Fill = hasExcel ? accent : secondary;
                if (excelStatus != null)
                    excelStatus.Text = hasExcel ? "Excel geladen" : "Excel nicht geladen";
                if (excelName != null)
                {
                    excelName.Text = hasExcel ? Path.GetFileName(excelPath) : "—";
                    excelName.ToolTip = hasExcel ? excelPath : null;
                }

                var hasOrderPdf = !string.IsNullOrWhiteSpace(orderPdfPath) && File.Exists(orderPdfPath);
                if (orderDot != null)
                    orderDot.Fill = hasOrderPdf ? accent : secondary;
                if (orderStatus != null)
                    orderStatus.Text = hasOrderPdf ? "Prüf-PDF geladen" : "Prüf-PDF nicht geladen";
                if (orderName != null)
                {
                    orderName.Text = hasOrderPdf ? Path.GetFileName(orderPdfPath) : "—";
                    orderName.ToolTip = hasOrderPdf ? orderPdfPath : null;
                }

                tiles.ItemsSource = drawingPdfs.Select(p =>
                {
                    var name = Path.GetFileNameWithoutExtension(p) ?? Path.GetFileName(p) ?? p;
                    var shortLabel = name.Length > 16 ? name.Substring(0, 14) + "…" : name;
                    return new OverviewPdfTile { FullPath = p, ShortLabel = shortLabel };
                }).ToList();
            }
            catch (Exception ex)
            {
                AppendStatus("⚠ Auftragsübersicht: " + ex.Message);
            }
        }

        private sealed class OverviewPdfTile
        {
            public string FullPath { get; set; }
            public string ShortLabel { get; set; }
        }

        private void UpdateWorkflowStatus(string excelPath, int pdfCount)
        {
            var workflow = WorkflowStatusBlock;
            if (workflow == null)
                return;

            var hasExcel = !string.IsNullOrWhiteSpace(excelPath) && File.Exists(excelPath);
            var wm = _watermarkSettings?.WatermarksEnabled ?? WatermarkSettingsStore.Load().WatermarksEnabled;

            if (!hasExcel && pdfCount == 0)
            {
                workflow.Text = "Excel + PDF-Zeichnungen hier ablegen, dann ▶ Sortieren & umbenennen";
                workflow.Foreground = (System.Windows.Media.Brush)FindResource("TextSecondary");
                return;
            }

            var excelLabel = hasExcel
                ? "Excel: " + Path.GetFileName(excelPath) + " ✓"
                : "Excel: fehlt ✗";
            var pdfLabel = "PDFs: " + pdfCount;
            var wmLabel = wm ? "Wasserzeichen: an" : "Wasserzeichen: aus";

            if (hasExcel && pdfCount > 0)
            {
                workflow.Text = excelLabel + "  ·  " + pdfLabel + "  ·  " + wmLabel
                    + Environment.NewLine + "▶ Jetzt „Sortieren & umbenennen“ klicken";
                workflow.Foreground = (System.Windows.Media.Brush)FindResource("AccentBrush");
            }
            else
            {
                workflow.Text = excelLabel + "  ·  " + pdfLabel + "  ·  " + wmLabel;
                workflow.Foreground = (System.Windows.Media.Brush)FindResource("TextSecondary");
            }
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

        private int CopySupportedFileToWorkspace(string sourceFile, string targetFolder, ref bool excelCopied, ref bool orderPdfCopied, ref int ignoredCount)
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
                if (OrderVerificationFileHelper.IsOrderVerificationFileName(sourceFile))
                {
                    var orderTarget = OrderVerificationFileHelper.GetCanonicalPath(targetFolder);
                    File.Copy(sourceFile, orderTarget, true);
                    orderPdfCopied = true;
                    return 1;
                }

                var safeName = AppSecurityService.SanitizeFileName(Path.GetFileName(sourceFile));
                var targetPath = AppSecurityService.ResolveSafeTargetPath(targetFolder, safeName);
                targetPath = GetAvailableFilePath(targetPath);
                File.Copy(sourceFile, targetPath, false);
                return 1;
            }

            if (ext == ".zip")
            {
                return ExtractSupportedFilesFromZipToWorkspace(sourceFile, targetFolder, ref excelCopied, ref orderPdfCopied, ref ignoredCount);
            }

            ignoredCount++;
            return 0;
        }

        private bool TryAutoDetectOrderVerificationPdf(string targetFolder)
        {
            if (string.IsNullOrWhiteSpace(targetFolder) || !Directory.Exists(targetFolder))
                return false;

            var canonical = OrderVerificationFileHelper.GetCanonicalPath(targetFolder);
            if (File.Exists(canonical))
                return true;

            var candidate = Directory.GetFiles(targetFolder, "*.pdf", SearchOption.TopDirectoryOnly)
                .FirstOrDefault(f => OrderVerificationFileHelper.IsOrderVerificationFileName(f));
            if (string.IsNullOrWhiteSpace(candidate))
                return false;

            File.Copy(candidate, canonical, true);
            try
            {
                if (!string.Equals(Path.GetFullPath(candidate), Path.GetFullPath(canonical), StringComparison.OrdinalIgnoreCase))
                    File.Delete(candidate);
            }
            catch
            {
                // Original kann bleiben, kanonische Bestellung.pdf ist gesetzt.
            }

            return File.Exists(canonical);
        }

        private int ExtractSupportedFilesFromZipToWorkspace(string zipFile, string targetFolder, ref bool excelCopied, ref bool orderPdfCopied, ref int ignoredCount)
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
                        if (OrderVerificationFileHelper.IsOrderVerificationFileName(entry.Name))
                        {
                            var orderTarget = OrderVerificationFileHelper.GetCanonicalPath(targetFolder);
                            entry.ExtractToFile(orderTarget, true);
                            orderPdfCopied = true;
                            copiedCount++;
                            continue;
                        }

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
                SetProcessingState(true, trackProcessingTime: false);
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
                SetProcessingState(false, trackProcessingTime: false);
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

            if (string.IsNullOrWhiteSpace(orderNumber))
            {
                AppendStatus("Bitte zuerst eine Auftragsnummer eingeben – Sortierung ohne Auftragsnummer ist nicht möglich.");
                MessageBox.Show(
                    this,
                    "Bitte geben Sie eine Auftragsnummer ein.\r\n\r\nOhne Auftragsnummer kann die Sortierung nicht starten.",
                    "Auftragsnummer fehlt",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                OrderNumberBox?.Focus();
                return;
            }

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

            var profiles = CustomerProfileStore.LoadAll();
            if (profiles.Count == 0)
            {
                var createCustomer = MessageBox.Show(this,
                    "Es ist noch kein Kundenprofil angelegt.\r\n\r\n"
                    + "Unter Einstellungen → Kundenprofile → «Neu Kunden anlegen» den Kundennamen eingeben "
                    + "und Beispiel-Excel sowie Zeichnungs-PDFs einfügen. Die App lernt daraus automatisch "
                    + "Zeichnungsnummern, Excel-Spalten und den Workflow.\r\n\r\n"
                    + "Jetzt Kundenprofil anlegen?",
                    "Kundenprofil fehlt",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Information);
                if (createCustomer == MessageBoxResult.Yes)
                    OpenManageCustomersDialog();
                return;
            }

            var detection = CustomerProfileDetectionService.Detect(pdfKeys, profiles);
            var activeCustomer = detection.Profile;
            if (activeCustomer == null)
            {
                var selectDlg = new CustomerSelectWindow(profiles)
                {
                    Owner = this,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner
                };
                ThemeWindowService.Attach(selectDlg);
                if (selectDlg.ShowDialog() != true || selectDlg.SelectedProfile == null)
                {
                    AppendStatus("ℹ Sortierung abgebrochen – kein Kundenprofil gewählt.");
                    return;
                }

                activeCustomer = selectDlg.SelectedProfile;
                AppendStatus("ℹ Kunde manuell gewählt: " + activeCustomer.DisplayName);
            }
            else
            {
                AppendStatus(string.Format(CultureInfo.InvariantCulture,
                    "ℹ Kunde erkannt: {0} ({1}% Übereinstimmung)",
                    activeCustomer.DisplayName,
                    detection.ConfidencePercent));
            }

            if (CustomerProfileSettingsStore.TryApplyToGlobal(activeCustomer.Id))
            {
                AppendStatus("ℹ Gespeicherte Einstellungen für " + activeCustomer.DisplayName + " geladen.");
            }

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

                excelRows = LoadExcelRowsForVerification(excelPath);
                ApplyAngebotDeliveryDates(excelPath, excelRows, AppendStatus);
                ApplyManualDeliveryDateOverrides(excelRows);
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

            // WICHTIG: manuelle Terminänderungen NACH Prüf-Dialog nochmals anwenden
            ApplyManualDeliveryDateOverrides(excelRows);
            LogManualDeliveryDatesForSort(excelRows);

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
                "Soll die Ablage komplett geleert werden?\n\n" +
                "Es werden unwiderruflich gelöscht:\n" +
                "• Alle PDFs (sortiert, unsortiert, Gesamt-PDF)\n" +
                "• Alle Excel-Dateien (Daten.xlsx, Angebot, …)\n" +
                "• Hilfsdateien (.prewm.pdf, .dok-meta.xml, .map.xml)\n" +
                "• ZIP und alle Unterordner\n\n" +
                "Excel-Pfad und Prüf-PDF werden zurückgesetzt.\n" +
                "Hinweis: Offene PDF-Vorschauen bitte vorher schließen.",
                "Ablage leeren für nächsten Auftrag",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (confirm != MessageBoxResult.Yes)
                return;

            TryCloseAppPdfWindows();
            TryCloseDedicatedPdfViewers();
            ReleaseWorkspaceFileLocks();

            var foldersToClean = GetWorkspaceFoldersToClean(pdfFolder);
            var result = CleanupWorkspaceFolders(foldersToClean);

            _lastMergedPdfPath = null;
            ClearCachedVerificationExcelRows();
            ExcelPathBox.Text = string.Empty;
            PathStore.ClearProcessingPaths();
            var defaultFolder = PathStore.GetOrCreateDefaultWorkspaceFolder();
            PdfFolderBox.Text = defaultFolder;

            ReleaseWorkspaceFileLocks();

            ResetOrderVerificationPdfDisplay();
            RefreshDroppedFilesDisplay();
            UpdateFooterStatus(string.Empty, 0, 0);

            if (result.RemainingFiles.Count > 0)
            {
                var failedList = string.Join("\n", result.RemainingFiles.Take(5));
                MessageBox.Show(
                    this,
                    "Die Ablage konnte nicht vollständig geleert werden.\n\n" +
                    failedList +
                    (result.RemainingFiles.Count > 5
                        ? "\n… und " + (result.RemainingFiles.Count - 5) + " weitere"
                        : string.Empty) +
                    "\n\nBitte alle PDF-Vorschauen schließen und erneut versuchen.",
                    "Ablage leeren",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
            else
            {
                MessageBox.Show(
                    this,
                    string.Format(
                        CultureInfo.InvariantCulture,
                        "Ablage ist leer.\n\n{0} Datei(en) und {1} Ordner entfernt.\nSie können jetzt den nächsten Auftrag laden.",
                        result.DeletedFiles,
                        result.RemovedDirectories),
                    "Ablage leeren",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
        }

        private void OpenWorkspaceFolder_Click(object sender, RoutedEventArgs e)
        {
            var folder = PdfFolderBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
                folder = PathStore.GetOrCreateDefaultWorkspaceFolder();

            Directory.CreateDirectory(folder);

            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = folder,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                AppendStatus("Ablage konnte nicht geöffnet werden: " + ex.Message);
            }
        }

        private void TryCloseAppPdfWindows()
        {
            foreach (var window in Application.Current.Windows.Cast<Window>().ToList())
            {
                if (window == null || window == this)
                    continue;

                if (window is PdfMergedPreviewWindow
                    || window is PdfWatermarkEditorWindow
                    || window is PdfPrintPreviewWindow)
                {
                    try
                    {
                        window.Close();
                    }
                    catch
                    {
                    }
                }
            }
        }

        private static IEnumerable<string> GetWorkspaceFoldersToClean(string pdfFolder)
        {
            var folders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var known in PathStore.GetAllKnownWorkspaceFolders())
                folders.Add(known);

            if (!string.IsNullOrWhiteSpace(pdfFolder) && Directory.Exists(pdfFolder))
                folders.Add(Path.GetFullPath(pdfFolder));

            return folders;
        }

        private static WorkspaceCleanupResult CleanupWorkspaceFolders(IEnumerable<string> folders)
        {
            var result = new WorkspaceCleanupResult();
            var uniqueFolders = (folders ?? Enumerable.Empty<string>())
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Select(Path.GetFullPath)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            foreach (var folder in uniqueFolders)
            {
                if (!Directory.Exists(folder))
                    continue;

                if (TryWipeWorkspaceDirectory(folder, result))
                    continue;

                WipeWorkspaceDirectoryFileByFile(folder, result);
            }

            foreach (var folder in uniqueFolders.Where(Directory.Exists))
            {
                foreach (var remaining in Directory.GetFiles(folder, "*.*", SearchOption.AllDirectories))
                {
                    var fullPath = Path.GetFullPath(remaining);
                    if (!result.RemainingFiles.Contains(fullPath, StringComparer.OrdinalIgnoreCase))
                        result.RemainingFiles.Add(fullPath);
                }
            }

            return result;
        }

        private static bool TryWipeWorkspaceDirectory(string folder, WorkspaceCleanupResult result)
        {
            try
            {
                var files = Directory.GetFiles(folder, "*.*", SearchOption.AllDirectories);
                result.DeletedFiles += files.Length;
                result.RemovedDirectories += Directory.GetDirectories(folder, "*", SearchOption.AllDirectories).Length;

                Directory.Delete(folder, recursive: true);
                Directory.CreateDirectory(folder);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static void ReleaseWorkspaceFileLocks()
        {
            PdfRasterLayoutService.ClearCache();
            PdfLayoutAnalysisService.ClearCache();
            PdfMaterialExtractionService.ClearCache();
            PdfVectorOccupancyService.ClearCache();

            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            Thread.Sleep(200);
        }

        private static IEnumerable<string> GetPdfCompanionPaths(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath))
                yield break;

            if (!filePath.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
                yield break;

            yield return WatermarkEditorService.GetPreWatermarkPath(filePath);
            yield return WatermarkMetadataStore.GetMetaPath(filePath);
            yield return MergedPdfPageMapService.GetMapPath(filePath);
        }

        private static void WipeWorkspaceDirectoryFileByFile(string folder, WorkspaceCleanupResult result)
        {
            IEnumerable<string> files;
            try
            {
                files = Directory.GetFiles(folder, "*.*", SearchOption.AllDirectories)
                    .OrderByDescending(path => path.Length)
                    .ToList();
            }
            catch
            {
                result.FailedFiles++;
                return;
            }

            var mergedFiles = files.Where(IsMergedOverviewFilePath).ToList();
            var otherFiles = files.Where(path => !IsMergedOverviewFilePath(path)).ToList();

            var companionFiles = otherFiles
                .Where(path => path.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
                .SelectMany(GetPdfCompanionPaths)
                .Where(File.Exists)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Except(otherFiles, StringComparer.OrdinalIgnoreCase)
                .Except(mergedFiles, StringComparer.OrdinalIgnoreCase)
                .ToList();

            foreach (var file in companionFiles
                .Concat(mergedFiles)
                .Concat(otherFiles))
            {
                if (TryDeleteFileWithRetry(file, IsMergedOverviewFilePath(file) ? 20 : 12))
                {
                    result.DeletedFiles++;
                    continue;
                }

                result.FailedFiles++;
                if (File.Exists(file))
                    result.RemainingFiles.Add(file);
            }

            try
            {
                foreach (var directory in Directory.GetDirectories(folder, "*", SearchOption.AllDirectories)
                    .OrderByDescending(path => path.Length))
                {
                    if (!Directory.EnumerateFileSystemEntries(directory).Any())
                    {
                        Directory.Delete(directory);
                        result.RemovedDirectories++;
                    }
                }
            }
            catch
            {
                result.FailedFiles++;
            }
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

            attempts = Math.Max(1, attempts);

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
                    Thread.Sleep(250);
                }
                catch (UnauthorizedAccessException)
                {
                    Thread.Sleep(250);
                }
            }

            if (!File.Exists(filePath))
                return true;

            try
            {
                var stagedPath = filePath + ".delete-" + Guid.NewGuid().ToString("N");
                File.Move(filePath, stagedPath);
                File.Delete(stagedPath);
                return !File.Exists(filePath);
            }
            catch
            {
                return !File.Exists(filePath);
            }
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

            // Falsch mitsortierte Prüf-PDFs (z. B. 01_5101426055.pdf) zurück als Bestellung.pdf
            if (TryAutoDetectOrderVerificationPdf(pdfFolder))
            {
                var recovered = OrderVerificationFileHelper.GetCanonicalPath(pdfFolder);
                if (File.Exists(recovered))
                {
                    _orderVerificationPdfPath = recovered;
                    PathStore.SaveOrderVerificationPdf(recovered);
                    SafeAppendStatus("ℹ Prüf-PDF aus Ablage wiederhergestellt: " + Path.GetFileName(recovered));
                }
            }

            var pdfCandidates = LoadPdfCandidates(pdfFolder, searchOption, orderVerificationPdfPath).ToList();
            var duplicates = PlatformDocumentService.FindDuplicateFiles(pdfCandidates.Select(p => p.FullPath));
            if (duplicates.Count > 0)
                SafeAppendStatus("ℹ Dubletten erkannt: " + duplicates.Count);
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
            ApplyManualDeliveryDateOverrides(excelRows);
            if (excelRows.Count == 0)
            {
                SafeAppendStatus("Keine Zeichnungsnummern in der Excel-Datei gefunden.");
                return;
            }

            var assemblyDrawingKeys = OrderVerificationDisplayBuilder.BuildAssemblyDrawingKeys(
                excelRows.Select(ToOrderExcelRow).ToList());
            if (assemblyDrawingKeys.Count > 0)
                SafeAppendStatus("Baugruppen erkannt: " + assemblyDrawingKeys.Count + " (Wasserzeichen LT-).");

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

                // Prüf-/Bestell-PDF nie zuordnen oder umbenennen
                if (OrderVerificationFileHelper.IsOrderVerificationFileName(pdf.DisplayName)
                    || OrderVerificationFileHelper.IsOrderVerificationFileName(pdf.OriginalName)
                    || OrderVerificationFileHelper.IsOrderVerificationFileName(pdf.FullPath))
                {
                    SafeAppendStatus("ℹ Prüf-PDF übersprungen (nicht sortieren): " + pdf.DisplayName);
                    continue;
                }

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
                        // Manuell geänderte Excel-Termine haben Vorrang – Baugruppe/freies ET = LT, BG-Unterteil = FT
                        var deliveryDate = ResolveSortDeliveryDate(r, orderLine, assemblyDrawingKeys);

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
                var primaryDeliveryDate = ResolveSortDeliveryDate(primaryRow, primaryOrderLine, assemblyDrawingKeys);

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

            SafeAppendStatus(string.Format(
                CultureInfo.InvariantCulture,
                "PDF-Zuordnung: {0} von {1} Dateien aus Excel übernommen.",
                matches.Count,
                pdfCandidates.Count));

            foreach (var sample in matches.Take(8))
            {
                SafeAppendStatus(string.Format(
                    CultureInfo.InvariantCulture,
                    "  Termin → {0}: {1}",
                    Path.GetFileNameWithoutExtension(sample.DrawingNumber),
                    string.IsNullOrWhiteSpace(sample.RequestedDeliveryDate) ? "(leer)" : sample.RequestedDeliveryDate));
            }

            var sourceFilesToDeleteAfterSuccess = new List<string> { excelPath };

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
                WatermarkEditorService.MoveSidecars(item.SourcePath, item.TempPath);
                moveIndex++;
                SafeSetProgressFraction(0.07 + 0.02 * moveIndex / fileCount, "Sortieren: Dateien verschieben …");
            }

            var renameIndex = 0;
            foreach (var item in renamePlan)
            {
                cancellationToken.ThrowIfCancellationRequested();
                File.Move(item.TempPath, item.FinalPath);
                WatermarkEditorService.MoveSidecars(item.TempPath, item.FinalPath);
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
                PdfSurfaceTreatmentExtractionService.ClearCache();
                TitleBlockOcrService.ClearCache();
                PdfVectorOccupancyService.ClearCache();
                PdfRasterLayoutService.ClearCache();
                WatermarkLearningStore.ResetBatchLearningState();
                WatermarkProcessingContext.BatchMode = true;
                if (_watermarkSettings == null)
                    _watermarkSettings = WatermarkSettingsStore.Load();
                WatermarkProcessingContext.DeliveryDatePosition = _watermarkSettings.DeliveryDatePosition;
                WatermarkProcessingContext.SetAssemblyDrawingKeys(BuildAssemblyDrawingKeysFromExcel(excelRows));
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
                        WatermarkProcessingContext.CurrentDrawingKey = OrderVerificationDrawingKey.Normalize(item.DrawingNumber);
                        var encodedWatermarks = EncodeWatermarkEntries(item.WatermarkEntries);
                        WatermarkEditorService.EnsurePreWatermarkBackup(item.FinalPath);
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
                        PdfRasterLayoutService.TrimCache();
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
                    if (WatermarkLearningStore.WasClearedThisBatch)
                    {
                        SafeAppendStatus("⚠ Lernspeicher für diesen Lauf pausiert (Material-Prüfung) – gespeicherte Korrekturen bleiben erhalten.");
                    }

                    WatermarkProcessingContext.BatchMode = false;
                    WatermarkProcessingContext.ClearDeliveryDatePositionOverride();
                    WatermarkProcessingContext.ClearAssemblyDrawingKeys();
                    WatermarkProcessingContext.CurrentDrawingKey = string.Empty;
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

            var indexPaths = renamePlan.Select(item => item.FinalPath).ToList();
            System.Threading.Tasks.Task.Run(() => PlatformDocumentService.IndexBatch(indexPaths));

            if (unmatchedPdfs.Count > 0)
            {
                SafeAppendStatus(string.Format(
                    CultureInfo.InvariantCulture,
                    "⚠ {0} PDF(s) NICHT in Excel gefunden — bleiben unverändert in der Ablage:",
                    unmatchedPdfs.Count));
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
                SafeAppendStatus("ℹ Hinweis: Die Excel-Quelldatei wurde entfernt. Nicht zugeordnete PDFs bleiben in der Ablage.");
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

        private void SetProcessingState(bool isProcessing, bool trackProcessingTime = true)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.Invoke(() => SetProcessingState(isProcessing, trackProcessingTime));
                return;
            }

            ProcessButtonBox.IsEnabled = !isProcessing;
            MergePrintButtonBox.IsEnabled = !isProcessing;
            DeleteProcessedButtonBox.IsEnabled = !isProcessing;
            CancelButtonBox.IsEnabled = isProcessing;
            if (CancelButtonBox != null)
                CancelButtonBox.Visibility = isProcessing ? Visibility.Visible : Visibility.Collapsed;
            ProgressPanelBox.Visibility = isProcessing ? Visibility.Visible : Visibility.Collapsed;
            BusyProgressBarBox.Value = 0;
            ProgressLabelBox.Text = "0 %";

            if (isProcessing)
            {
                if (!trackProcessingTime)
                    return;

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
                if (!trackProcessingTime)
                    return;

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
            string excludeFull = null;
            if (!string.IsNullOrWhiteSpace(excludeOrderPdfPath) && File.Exists(excludeOrderPdfPath))
            {
                try { excludeFull = Path.GetFullPath(excludeOrderPdfPath); }
                catch { excludeFull = excludeOrderPdfPath; }
            }

            string canonicalFull = null;
            if (!string.IsNullOrWhiteSpace(pdfFolder))
            {
                var canonical = OrderVerificationFileHelper.GetCanonicalPath(pdfFolder);
                if (File.Exists(canonical))
                {
                    try { canonicalFull = Path.GetFullPath(canonical); }
                    catch { canonicalFull = canonical; }
                }
            }

            return Directory.GetFiles(pdfFolder, "*.pdf", searchOption)
                .Where(path => !Path.GetFileName(path).StartsWith("_GESAMT_", StringComparison.OrdinalIgnoreCase))
                .Where(path => !Path.GetFileName(path).EndsWith(".prewm.pdf", StringComparison.OrdinalIgnoreCase))
                .Where(path => !IsExcludedOrderVerificationPdf(path, excludeFull, canonicalFull))
                .Select((path, index) => new PdfCandidate(path, index))
                .Where(candidate => !string.IsNullOrWhiteSpace(candidate.SearchKey))
                .Where(candidate => !OrderVerificationFileHelper.IsOrderVerificationFileName(candidate.DisplayName)
                                    && !OrderVerificationFileHelper.IsOrderVerificationFileName(candidate.OriginalName));
        }

        private static bool IsExcludedOrderVerificationPdf(string path, string excludeFullPath, string canonicalFullPath)
        {
            if (string.IsNullOrWhiteSpace(path))
                return false;

            var fileName = Path.GetFileName(path);
            if (string.Equals(fileName, OrderVerificationFileHelper.CanonicalFileName, StringComparison.OrdinalIgnoreCase))
                return true;

            if (OrderVerificationFileHelper.IsOrderVerificationFileName(fileName))
                return true;

            string full = null;
            try { full = Path.GetFullPath(path); }
            catch { full = path; }

            if (!string.IsNullOrWhiteSpace(excludeFullPath)
                && string.Equals(full, excludeFullPath, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (!string.IsNullOrWhiteSpace(canonicalFullPath)
                && string.Equals(full, canonicalFullPath, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return false;
        }

        private void OpenDrawingPdfFromProtocol(
            string drawingNumber,
            string pdfFolder,
            SearchOption searchOption,
            string orderPdfPath)
        {
            if (string.IsNullOrWhiteSpace(drawingNumber) || string.IsNullOrWhiteSpace(pdfFolder) || !Directory.Exists(pdfFolder))
            {
                MessageBox.Show(this,
                    "Zeichnungs-PDF konnte nicht geöffnet werden.",
                    "Zeichnung öffnen",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            // Immer rekursiv suchen – Zeichnungen liegen oft in Unterordnern oder noch in ZIP-Dateien
            var path = FindDrawingPdfPath(drawingNumber, pdfFolder, SearchOption.AllDirectories, orderPdfPath);
            if (string.IsNullOrWhiteSpace(path))
                path = TryExtractDrawingPdfFromZip(drawingNumber, pdfFolder, SearchOption.AllDirectories);
            if (string.IsNullOrWhiteSpace(path))
                path = FindDrawingPdfByLooseNameMatch(drawingNumber, pdfFolder);

            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                var zipCount = 0;
                var pdfCount = 0;
                try
                {
                    zipCount = Directory.GetFiles(pdfFolder, "*.zip", SearchOption.AllDirectories).Length;
                    pdfCount = Directory.GetFiles(pdfFolder, "*.pdf", SearchOption.AllDirectories).Length;
                }
                catch
                {
                }

                MessageBox.Show(this,
                    "Kein Zeichnungs-PDF gefunden für:\r\n" + drawingNumber + "\r\n\r\n"
                    + "Ablage: " + pdfFolder + "\r\n"
                    + "PDF-Dateien: " + pdfCount + " · ZIP-Dateien: " + zipCount + "\r\n\r\n"
                    + "Tipp: ZIP mit den Zeichnungen in die Ablage legen (Drag & Drop) und erneut doppelklicken.",
                    "Zeichnung öffnen",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            try
            {
                Process.Start(path);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this,
                    "PDF konnte nicht geöffnet werden:\r\n" + ex.Message,
                    "Zeichnung öffnen",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }

        private string FindDrawingPdfPath(
            string drawingNumber,
            string pdfFolder,
            SearchOption searchOption,
            string orderPdfPath)
        {
            var wanted = OrderVerificationDrawingKey.Normalize(drawingNumber);
            if (string.IsNullOrWhiteSpace(wanted))
                return null;

            foreach (var candidate in LoadPdfCandidates(pdfFolder, searchOption, orderPdfPath))
            {
                if (DrawingPdfMatches(candidate.SearchKey, candidate.OriginalName, drawingNumber, wanted))
                    return candidate.FullPath;
            }

            return null;
        }

        private static bool DrawingPdfMatches(string searchKey, string originalName, string drawingNumber, string wantedNormalized)
        {
            var wanted = NormalizeKey(drawingNumber);
            if (string.IsNullOrWhiteSpace(wanted))
                wanted = NormalizeKey(wantedNormalized);
            if (string.IsNullOrWhiteSpace(wanted))
                return false;

            var wantedCore = StripLeadingDrawingPrefix(wanted);

            foreach (var raw in new[]
            {
                searchKey,
                originalName,
                StripRevSuffix(originalName ?? string.Empty),
                StripSequencePrefixes(originalName ?? string.Empty)
            })
            {
                if (string.IsNullOrWhiteSpace(raw))
                    continue;

                var key = NormalizeKey(raw);
                if (string.IsNullOrWhiteSpace(key))
                    continue;

                if (string.Equals(key, wanted, StringComparison.OrdinalIgnoreCase))
                    return true;

                var keyCore = StripLeadingDrawingPrefix(key);
                if (!string.IsNullOrWhiteSpace(wantedCore)
                    && string.Equals(keyCore, wantedCore, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                // Dateiname enthält Zeichnungsnummer (z. B. Ordner/TSL-…-Rev.pdf)
                if (key.IndexOf(wanted, StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
                if (wanted.Length >= 8 && key.IndexOf(wantedCore, StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            }

            if (OrderVerificationDrawingKey.Equal(originalName, drawingNumber)
                || OrderVerificationDrawingKey.Equal(searchKey, drawingNumber))
            {
                return true;
            }

            return false;
        }

        private static string StripLeadingDrawingPrefix(string normalizedKey)
        {
            if (string.IsNullOrWhiteSpace(normalizedKey) || normalizedKey.Length < 5)
                return normalizedKey ?? string.Empty;

            // TSL11365393 → 11365393 (Präfix 2–6 Buchstaben vor Ziffern)
            var i = 0;
            while (i < normalizedKey.Length && char.IsLetter(normalizedKey[i]))
                i++;
            if (i >= 2 && i <= 6 && i < normalizedKey.Length && char.IsDigit(normalizedKey[i]))
                return normalizedKey.Substring(i);

            return normalizedKey;
        }

        /// <summary>
        /// Sucht die Zeichnung in ZIP-Dateien der Ablage, extrahiert sie einmalig und gibt den Pfad zurück.
        /// </summary>
        private string TryExtractDrawingPdfFromZip(string drawingNumber, string pdfFolder, SearchOption searchOption)
        {
            var wanted = OrderVerificationDrawingKey.Normalize(drawingNumber);
            if (string.IsNullOrWhiteSpace(wanted) || !Directory.Exists(pdfFolder))
                return null;

            string[] zipFiles;
            try
            {
                zipFiles = Directory.GetFiles(pdfFolder, "*.zip", searchOption);
            }
            catch
            {
                return null;
            }

            foreach (var zipPath in zipFiles)
            {
                try
                {
                    using (var archive = ZipFile.OpenRead(zipPath))
                    {
                        foreach (var entry in archive.Entries)
                        {
                            if (string.IsNullOrWhiteSpace(entry.Name))
                                continue;

                            if (!entry.Name.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
                                continue;

                            var fileName = Path.GetFileName(entry.Name);
                            var originalName = StripSequencePrefixes(Path.GetFileNameWithoutExtension(fileName));
                            var searchKey = NormalizeKey(StripRevSuffix(originalName));
                            var fullPathName = (entry.FullName ?? string.Empty).Replace('/', Path.DirectorySeparatorChar);

                            var matches = DrawingPdfMatches(searchKey, originalName, drawingNumber, wanted)
                                          || DrawingPdfMatches(
                                              NormalizeKey(fullPathName),
                                              Path.GetFileNameWithoutExtension(fullPathName),
                                              drawingNumber,
                                              wanted);

                            if (!matches)
                            {
                                var extracted = OrderVerificationDrawingKey.ExtractPrimaryFromTexts(
                                    fileName,
                                    fullPathName,
                                    originalName);
                                matches = !string.IsNullOrWhiteSpace(extracted)
                                          && OrderVerificationDrawingKey.Equal(extracted, drawingNumber);
                            }

                            if (!matches)
                                continue;

                            var extractDir = Path.Combine(pdfFolder, ".dok-zip-preview");
                            Directory.CreateDirectory(extractDir);
                            var safeName = AppSecurityService.SanitizeFileName(fileName);
                            if (string.IsNullOrWhiteSpace(safeName))
                                safeName = AppSecurityService.SanitizeFileName(drawingNumber) + ".pdf";
                            var targetPath = AppSecurityService.ResolveSafeTargetPath(extractDir, safeName);

                            if (!AppSecurityService.IsPathInsideDirectory(targetPath, extractDir))
                                continue;

                            if (File.Exists(targetPath) && new FileInfo(targetPath).Length == entry.Length)
                                return targetPath;

                            entry.ExtractToFile(targetPath, true);
                            return targetPath;
                        }
                    }
                }
                catch
                {
                    // nächste ZIP
                }
            }

            return null;
        }

        /// <summary>Fallback: Dateiname enthält die Zeichnungsnummer (auch mit Rev/Präfix).</summary>
        private string FindDrawingPdfByLooseNameMatch(string drawingNumber, string pdfFolder)
        {
            var wanted = NormalizeKey(drawingNumber);
            var wantedCore = StripLeadingDrawingPrefix(wanted);
            if (string.IsNullOrWhiteSpace(wanted) || !Directory.Exists(pdfFolder))
                return null;

            string[] files;
            try
            {
                files = Directory.GetFiles(pdfFolder, "*.pdf", SearchOption.AllDirectories);
            }
            catch
            {
                return null;
            }

            foreach (var file in files)
            {
                var name = Path.GetFileNameWithoutExtension(file);
                if (string.IsNullOrWhiteSpace(name))
                    continue;

                if (Path.GetFileName(file).StartsWith("_GESAMT_", StringComparison.OrdinalIgnoreCase))
                    continue;

                var key = NormalizeKey(StripSequencePrefixes(StripRevSuffix(name)));
                if (string.IsNullOrWhiteSpace(key))
                    continue;

                if (string.Equals(key, wanted, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(StripLeadingDrawingPrefix(key), wantedCore, StringComparison.OrdinalIgnoreCase)
                    || key.IndexOf(wanted, StringComparison.OrdinalIgnoreCase) >= 0
                    || (!string.IsNullOrWhiteSpace(wantedCore) && key.IndexOf(wantedCore, StringComparison.OrdinalIgnoreCase) >= 0))
                {
                    return file;
                }
            }

            return null;
        }

        private string ResolveOrderVerificationPdfPath(string pdfFolder)
        {
            if (!string.IsNullOrWhiteSpace(_orderVerificationPdfPath) && File.Exists(_orderVerificationPdfPath))
                return _orderVerificationPdfPath;

            if (!string.IsNullOrWhiteSpace(pdfFolder) && Directory.Exists(pdfFolder))
            {
                var canonical = OrderVerificationFileHelper.GetCanonicalPath(pdfFolder);
                if (File.Exists(canonical))
                {
                    _orderVerificationPdfPath = canonical;
                    return canonical;
                }
            }

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
            var orderRows = excelRows.Select(ToOrderExcelRow).ToList();
            var report = RunOrderVerificationReport(excelPath, orderPdfPath, excelRows, pdfFolder, searchOption);
            AppendStatus(report.ToDisplayText());

            Action<int, string, string> persistDeliveryDate = (rowIndex, drawingNumber, date) =>
            {
                PersistDeliveryDateOverride(excelRows, rowIndex, drawingNumber, date);
            };

            Action<string> openDrawingPdf = drawingNumber =>
                OpenDrawingPdfFromProtocol(drawingNumber, pdfFolder, searchOption, orderPdfPath);

            var dialogResult = OrderVerificationReportWindow.ShowDialog(
                this,
                report,
                orderRows,
                Path.GetFileName(excelPath),
                Path.GetFileName(orderPdfPath),
                excelPath,
                orderPdfPath,
                offerContinueOnIssues: blockOnIssues,
                persistDeliveryDate: persistDeliveryDate,
                openDrawingPdf: openDrawingPdf);

            SyncDeliveryDatesAfterVerificationDialog(excelRows, dialogResult);

            // Kein erneutes ApplyOrderPdfDates mit dem alten Prüfbericht –
            // der Dialog hat Excel-Zeilen + Overrides bereits geschrieben.
            if (dialogResult.DatesModified || dialogResult.AdoptOrderPdfDates)
            {
                report = RunOrderVerificationReport(excelPath, orderPdfPath, excelRows, pdfFolder, searchOption);
                if (dialogResult.AdoptOrderPdfDates)
                    AppendStatus("✓ Abweichende PDF-Termine in Excel-Spalte übernommen.");
                AppendStatus("✓ Liefertermine für Sortierung: Excel-Spalte ("
                             + CountManualDeliveryDateOverrideRows() + " Position(en)).");
                AppendStatus(report.ToDisplayText());
            }
            else if (_manualDeliveryDateOverrides.Count > 0)
            {
                AppendStatus("✓ Liefertermine für Sortierung gespeichert ("
                             + CountManualDeliveryDateOverrideRows() + " Position(en)).");
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

        private void PersistDeliveryDateOverride(
            IList<ExcelRowCandidate> candidates,
            int rowIndex,
            string drawingNumber,
            string date)
        {
            if (string.IsNullOrWhiteSpace(date))
                return;

            PersistDeliveryDateToCandidates(candidates, rowIndex, drawingNumber, date);
            RememberManualDeliveryDate(rowIndex, drawingNumber, date);
        }

        private void PersistDeliveryDateToCandidates(
            IList<ExcelRowCandidate> candidates,
            int rowIndex,
            string drawingNumber,
            string date)
        {
            if (candidates == null || string.IsNullOrWhiteSpace(date))
                return;

            ExcelRowCandidate candidate = null;
            if (rowIndex > 0)
                candidate = candidates.FirstOrDefault(c => c.RowIndex == rowIndex);

            if (candidate == null && !string.IsNullOrWhiteSpace(drawingNumber))
            {
                candidate = candidates.FirstOrDefault(c =>
                    OrderVerificationDrawingKey.Equal(c.RawDrawingNumber, drawingNumber));
            }

            if (candidate != null)
                candidate.RequestedDeliveryDate = date;
        }

        private void RememberManualDeliveryDate(int rowIndex, string drawingNumber, string date)
        {
            if (string.IsNullOrWhiteSpace(date))
                return;

            if (rowIndex > 0)
                _manualDeliveryDateOverrides[BuildRowOverrideKey(rowIndex)] = date;

            var drawingKey = OrderVerificationDrawingKey.Normalize(drawingNumber);
            if (!string.IsNullOrWhiteSpace(drawingKey))
                _manualDeliveryDateOverrides[BuildDrawingOverrideKey(drawingKey)] = date;
        }

        private static string BuildRowOverrideKey(int rowIndex)
        {
            return "r:" + rowIndex.ToString(CultureInfo.InvariantCulture);
        }

        private static string BuildDrawingOverrideKey(string normalizedDrawingKey)
        {
            return "d:" + normalizedDrawingKey;
        }

        private int CountManualDeliveryDateOverrideRows()
        {
            return _manualDeliveryDateOverrides.Keys.Count(k =>
                k != null && k.StartsWith("r:", StringComparison.OrdinalIgnoreCase));
        }

        private void SyncDeliveryDatesAfterVerificationDialog(
            IList<ExcelRowCandidate> excelRows,
            OrderVerificationDialogResult dialogResult)
        {
            if (excelRows == null || dialogResult == null)
                return;

            var updated = dialogResult.UpdatedExcelRows;
            if (updated != null)
                ApplyOrderExcelRowDates(excelRows, updated);

            // Nur aus den Excel-Kandidaten speichern – verhindert das Zurückschreiben veralteter Dialog-Kopien
            StoreManualDeliveryDateOverridesFromCandidates(excelRows);
            ApplyManualDeliveryDateOverrides(excelRows);
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
            _cachedOrderVerificationReport = report;
            _cachedOrderVerificationExcelRows = orderRows;
            _cachedOrderVerificationExcelPath = excelPath ?? string.Empty;
            _cachedOrderVerificationOrderPdfPath = orderPdfPath ?? string.Empty;
            return report;
        }

        private List<ExcelRowCandidate> LoadExcelRowsForVerification(string excelPath)
        {
            if (_cachedVerificationExcelRows != null
                && string.Equals(_cachedVerificationExcelPath, excelPath, StringComparison.OrdinalIgnoreCase))
            {
                return _cachedVerificationExcelRows;
            }

            _cachedVerificationExcelRows = ReadExcelRows(excelPath).ToList();
            _cachedVerificationExcelPath = excelPath;
            return _cachedVerificationExcelRows;
        }

        private void ClearCachedVerificationExcelRows()
        {
            _cachedVerificationExcelRows = null;
            _cachedVerificationExcelPath = null;
            _manualDeliveryDateOverrides.Clear();
        }

        private void LogManualDeliveryDatesForSort(IList<ExcelRowCandidate> excelRows)
        {
            if (excelRows == null || excelRows.Count == 0)
                return;

            var withDate = excelRows
                .Where(r => r != null && !string.IsNullOrWhiteSpace(r.RequestedDeliveryDate))
                .Take(8)
                .ToList();

            if (withDate.Count == 0)
                return;

            AppendStatus("Liefertermine für Sortierung (Auszug):");
            foreach (var row in withDate)
            {
                var marker = HasManualDeliveryDateOverride(row) ? " [manuell]" : string.Empty;
                AppendStatus(string.Format(
                    CultureInfo.InvariantCulture,
                    "  Zeile {0} {1}: {2}{3}",
                    row.RowIndex,
                    row.RawDrawingNumber,
                    row.RequestedDeliveryDate,
                    marker));
            }
        }

        private void StoreManualDeliveryDateOverrides(IList<OrderExcelRow> orderRows)
        {
            if (orderRows == null)
                return;

            foreach (var orderRow in orderRows)
            {
                if (orderRow == null || string.IsNullOrWhiteSpace(orderRow.DeliveryDate))
                    continue;

                RememberManualDeliveryDate(orderRow.RowIndex, orderRow.DrawingNumber, orderRow.DeliveryDate);

                if (!string.IsNullOrWhiteSpace(orderRow.SearchKey))
                {
                    var searchKey = OrderVerificationDrawingKey.Normalize(orderRow.SearchKey);
                    if (!string.IsNullOrWhiteSpace(searchKey))
                        _manualDeliveryDateOverrides[BuildDrawingOverrideKey(searchKey)] = orderRow.DeliveryDate;
                }
            }
        }

        private void StoreManualDeliveryDateOverridesFromCandidates(IList<ExcelRowCandidate> candidates)
        {
            if (candidates == null)
                return;

            foreach (var row in candidates)
            {
                if (row == null || string.IsNullOrWhiteSpace(row.RequestedDeliveryDate))
                    continue;

                RememberManualDeliveryDate(row.RowIndex, row.RawDrawingNumber, row.RequestedDeliveryDate);
            }
        }

        private void ApplyManualDeliveryDateOverrides(IList<ExcelRowCandidate> candidates)
        {
            if (candidates == null || _manualDeliveryDateOverrides.Count == 0)
                return;

            foreach (var row in candidates)
            {
                if (row == null)
                    continue;

                string date;
                if (!TryGetManualDeliveryDateOverride(row.RowIndex, row.RawDrawingNumber, out date))
                    continue;

                row.RequestedDeliveryDate = date;
            }
        }

        private bool TryGetManualDeliveryDateOverride(int rowIndex, string drawingNumber, out string date)
        {
            date = null;

            if (rowIndex > 0
                && _manualDeliveryDateOverrides.TryGetValue(BuildRowOverrideKey(rowIndex), out date)
                && !string.IsNullOrWhiteSpace(date))
            {
                return true;
            }

            var drawingKey = OrderVerificationDrawingKey.Normalize(drawingNumber);
            if (!string.IsNullOrWhiteSpace(drawingKey)
                && _manualDeliveryDateOverrides.TryGetValue(BuildDrawingOverrideKey(drawingKey), out date)
                && !string.IsNullOrWhiteSpace(date))
            {
                return true;
            }

            // Altformat (nur Zeichnungsschlüssel ohne Präfix) – Kompatibilität zu älteren Sitzungen
            if (!string.IsNullOrWhiteSpace(drawingKey)
                && _manualDeliveryDateOverrides.TryGetValue(drawingKey, out date)
                && !string.IsNullOrWhiteSpace(date))
            {
                return true;
            }

            date = null;
            return false;
        }

        private bool HasManualDeliveryDateOverride(ExcelRowCandidate row)
        {
            if (row == null)
                return false;

            string date;
            return TryGetManualDeliveryDateOverride(row.RowIndex, row.RawDrawingNumber, out date);
        }

        private static void ApplyOrderExcelRowDates(IList<ExcelRowCandidate> candidates, IList<OrderExcelRow> orderRows)
        {
            if (candidates == null || orderRows == null)
                return;

            foreach (var orderRow in orderRows)
            {
                if (orderRow == null || string.IsNullOrWhiteSpace(orderRow.DeliveryDate))
                    continue;

                ExcelRowCandidate candidate = null;
                if (orderRow.RowIndex > 0)
                    candidate = candidates.FirstOrDefault(c => c.RowIndex == orderRow.RowIndex);

                if (candidate == null && !string.IsNullOrWhiteSpace(orderRow.DrawingNumber))
                {
                    candidate = candidates.FirstOrDefault(c =>
                        OrderVerificationDrawingKey.Equal(c.RawDrawingNumber, orderRow.DrawingNumber));
                }

                if (candidate == null && !string.IsNullOrWhiteSpace(orderRow.SearchKey))
                {
                    candidate = candidates.FirstOrDefault(c =>
                        c.NormalizedCells != null
                        && c.NormalizedCells.Any(cell => OrderVerificationDrawingKey.Equal(cell, orderRow.SearchKey)));
                }

                if (candidate != null)
                    candidate.RequestedDeliveryDate = orderRow.DeliveryDate;
            }
        }

        private static void ApplyOrderPdfDates(IList<ExcelRowCandidate> excelRows, OrderVerificationReport report)
        {
            if (excelRows == null || report?.DateMismatches == null)
                return;

            foreach (var mismatch in report.DateMismatches)
            {
                if (string.IsNullOrWhiteSpace(mismatch.PdfDate)
                    || mismatch.PdfDate.StartsWith("(", StringComparison.Ordinal))
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

        private string ResolveSortDeliveryDate(
            ExcelRowCandidate row,
            OrderVerificationLine orderLine,
            ISet<string> assemblyDrawingKeys = null)
        {
            if (row == null)
                return string.Empty;

            // Nur BG-Unterzeichnungen („verbaut in …“) → FT; Baugruppe und freie Einzelteile → LT
            var useFt = OrderVerificationLookup.IsBgSubDrawing(row.ParentArticleNumber)
                        && !IsAssemblyExcelRow(row, assemblyDrawingKeys);

            // 1) Manuelle Override-Liste (aus Prüfprotokoll) – Zeilenindex hat Vorrang
            string overrideDate;
            if (TryGetManualDeliveryDateOverride(row.RowIndex, row.RawDrawingNumber, out overrideDate))
            {
                return OrderVerificationLookup.FormatWatermarkTermin(overrideDate, useFt);
            }

            // 2) Excel-Wert (ggf. nach Prüfprotokoll aktualisiert)
            if (!string.IsNullOrWhiteSpace(row.RequestedDeliveryDate))
            {
                return OrderVerificationLookup.FormatWatermarkTermin(row.RequestedDeliveryDate, useFt);
            }

            // 3) Fallback Bestell-PDF
            if (useFt)
            {
                return OrderVerificationLookup.ResolveWatermarkDeliveryDate(
                    row.ParentArticleNumber,
                    orderLine,
                    null);
            }

            return OrderVerificationLookup.ResolveWatermarkDeliveryDate(
                null,
                orderLine,
                null);
        }

        private static bool IsAssemblyExcelRow(ExcelRowCandidate row, ISet<string> assemblyDrawingKeys)
        {
            if (row == null)
                return false;

            // „verbaut in …“ = Einzelteil, nicht die Baugruppe selbst
            if (OrderVerificationLookup.IsBgSubDrawing(row.ParentArticleNumber))
                return false;

            if (assemblyDrawingKeys == null || assemblyDrawingKeys.Count == 0)
                return false;

            var key = OrderVerificationDrawingKey.Normalize(row.RawDrawingNumber);
            return !string.IsNullOrWhiteSpace(key) && assemblyDrawingKeys.Contains(key);
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

            var excelRows = LoadExcelRowsForVerification(excelPath);
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
                (row, date) =>
                {
                    // Manuelle Prüfprotokoll-Termine nie durch Angebot überschreiben
                    if (HasManualDeliveryDateOverride(row))
                        return;
                    row.RequestedDeliveryDate = date;
                });

            log?.Invoke("ℹ Angebot-Excel: " + Path.GetFileName(angebotPath) + " – Termine mit Vorrang übernommen.");
            ApplyManualDeliveryDateOverrides(targetRows);
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
            // Statusmeldungen in der Oberfläche nicht mehr anzeigen.
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
            // Statusmeldungen in der Oberfläche nicht mehr anzeigen.
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

        private static HashSet<string> BuildAssemblyDrawingKeysFromExcel(IList<ExcelRowCandidate> excelRows)
        {
            var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (excelRows == null || excelRows.Count == 0)
                return keys;

            var articleToDrawing = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var row in excelRows)
            {
                if (string.IsNullOrWhiteSpace(row.RawDrawingNumber) || !IsLikelyDrawingNumber(row.RawDrawingNumber))
                    continue;

                if (row.DetectedArticleNumbers == null || row.DetectedArticleNumbers.Count == 0)
                    continue;

                var drawingKey = OrderVerificationDrawingKey.Normalize(row.RawDrawingNumber);
                if (string.IsNullOrWhiteSpace(drawingKey))
                    continue;

                foreach (var article in row.DetectedArticleNumbers)
                {
                    if (string.IsNullOrWhiteSpace(article))
                        continue;

                    articleToDrawing[article.Trim()] = drawingKey;
                }
            }

            foreach (var row in excelRows)
            {
                if (string.IsNullOrWhiteSpace(row.ParentArticleNumber))
                    continue;

                if (articleToDrawing.TryGetValue(row.ParentArticleNumber.Trim(), out var parentDrawing)
                    && !string.IsNullOrWhiteSpace(parentDrawing))
                {
                    keys.Add(parentDrawing);
                }
            }

            return keys;
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
