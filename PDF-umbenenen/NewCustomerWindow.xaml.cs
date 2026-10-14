using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Media;
using PDF_umbenenen.Models;
using PDF_umbenenen.Services;
using PDF_umbenenen.Services.Ai;

namespace PDF_umbenenen
{
    public partial class NewCustomerWindow : Window
    {
        private readonly CustomerProfileLearningService _learningService;
        private readonly CustomerOnboardingStagingService _staging = new CustomerOnboardingStagingService();
        private readonly List<AiChatMessage> _chatHistory = new List<AiChatMessage>();
        private CustomerProfileDraft _draft;
        private readonly StringBuilder _chatLog = new StringBuilder();
        private bool _chatBusy;
        private int _step = 1;

        public MainWindow HostMainWindow { get; set; }

        public NewCustomerWindow()
        {
            InitializeComponent();
            ThemeWindowService.Attach(this);
            _learningService = new CustomerProfileLearningService(new PdfTextExtractionService());
            HasDrawingsCheckBox.Checked += DocumentSelection_Changed;
            HasDrawingsCheckBox.Unchecked += DocumentSelection_Changed;
            HasExcelCheckBox.Checked += DocumentSelection_Changed;
            HasExcelCheckBox.Unchecked += DocumentSelection_Changed;
            HasOrderPdfCheckBox.Checked += DocumentSelection_Changed;
            HasOrderPdfCheckBox.Unchecked += DocumentSelection_Changed;
            HasOrderTextCheckBox.Checked += DocumentSelection_Changed;
            HasOrderTextCheckBox.Unchecked += DocumentSelection_Changed;
            RefreshFilesSummary();
            ShowStep(1);
            AppendChatSystem("Schritt 1: Name und Unterlagen wählen. Bei Bedarf hier nachjustieren, z. B. «Kunde heißt …».");
        }

        private void Window_Closed(object sender, EventArgs e)
        {
            _staging.DisposeFolder();
        }

        private void Window_PreviewDragOver(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                e.Effects = DragDropEffects.Copy;
                e.Handled = true;
            }
        }

        private void Window_PreviewDrop(object sender, DragEventArgs e)
        {
            if (!e.Data.GetDataPresent(DataFormats.FileDrop))
                return;

            var files = e.Data.GetData(DataFormats.FileDrop) as string[];
            if (files == null || files.Length == 0)
                return;

            var added = _staging.AddPaths(files);
            RefreshFilesSummary();
            SyncDocumentCheckboxesFromStaging();
            if (added > 0)
            {
                if (_step < 2)
                    ShowStep(2);
                AppendChatSystem(added + " Datei(en) übernommen. «Weiter» für Analyse und Sortier-Prüfung.");
            }

            e.Handled = true;
        }

        private void ShowStep(int step)
        {
            _step = Math.Max(1, Math.Min(3, step));

            Step1Panel.Visibility = _step == 1 ? Visibility.Visible : Visibility.Collapsed;
            Step2Panel.Visibility = _step == 2 ? Visibility.Visible : Visibility.Collapsed;
            Step3Panel.Visibility = _step == 3 ? Visibility.Visible : Visibility.Collapsed;

            BackButton.IsEnabled = _step > 1;
            NextButton.Visibility = _step < 3 ? Visibility.Visible : Visibility.Collapsed;
            FinishButton.Visibility = _step == 3 ? Visibility.Visible : Visibility.Collapsed;

            StyleStepBadge(Step1Badge, _step == 1);
            StyleStepBadge(Step2Badge, _step == 2);
            StyleStepBadge(Step3Badge, _step == 3);

            if (_step == 1)
            {
                StepTitleTextBlock.Text = "Schritt 1 von 3 – Name & Unterlagen";
                StepHintTextBlock.Text = "Name eingeben und ankreuzen, was der Kunde schickt.";
                ChatPanelTitleTextBlock.Text = "KI – Hilfe zu Schritt 1";
                ChatPanelHintTextBlock.Text = "Optional. z. B. «Kunde heißt Müller» oder «nur Excel, keine Prüf-PDF»";
                NextButton.Content = "Weiter";
            }
            else if (_step == 2)
            {
                StepTitleTextBlock.Text = "Schritt 2 von 3 – Dateien";
                StepHintTextBlock.Text = "Beispiel-Dateien ablegen (Zeichnungen + Bestellung). Die App erkennt den Rest.";
                ChatPanelTitleTextBlock.Text = "KI – Hilfe zu Schritt 2";
                ChatPanelHintTextBlock.Text = "Optional. z. B. «diese PDF ist die Bestellung» oder «Zeichnungen beginnen mit MK-»";
                NextButton.Content = "Analysieren & Weiter";
            }
            else
            {
                StepTitleTextBlock.Text = "Schritt 3 von 3 – Prüfen & Speichern";
                StepHintTextBlock.Text = "Ergebnis prüfen. Bei Bedarf KI nutzen, Sortier-Vorschau öffnen, dann «Kunde anlegen».";
                ChatPanelTitleTextBlock.Text = "KI – Hilfe zu Schritt 3";
                ChatPanelHintTextBlock.Text = "Optional. z. B. «Stückzahl nach links» oder «Zeichnungsspalte heißt Zeichnung»";
            }
        }

        private void StyleStepBadge(System.Windows.Controls.Border badge, bool active)
        {
            if (badge == null)
                return;

            if (active)
            {
                badge.Background = (Brush)FindResource("AccentBrush");
                badge.BorderThickness = new Thickness(0);
                if (badge.Child is System.Windows.Controls.TextBlock tb)
                {
                    tb.Foreground = Brushes.White;
                    tb.FontWeight = FontWeights.SemiBold;
                }
            }
            else
            {
                badge.Background = (Brush)FindResource("CardBackground");
                badge.BorderBrush = (Brush)FindResource("GroupBorder");
                badge.BorderThickness = new Thickness(1);
                if (badge.Child is System.Windows.Controls.TextBlock tb)
                {
                    tb.Foreground = (Brush)FindResource("TextSecondary");
                    tb.FontWeight = FontWeights.Normal;
                }
            }
        }

        private void Back_Click(object sender, RoutedEventArgs e)
        {
            ShowStep(_step - 1);
        }

        private void Next_Click(object sender, RoutedEventArgs e)
        {
            if (_step == 1)
            {
                if (!ValidateStep1())
                    return;
                ShowStep(2);
                AppendChatSystem("Schritt 2: Dateien ablegen. Ohne Chat – einfach reinziehen.");
                return;
            }

            if (_step == 2)
            {
                if (_staging.Files.Count == 0)
                {
                    MessageBox.Show(this,
                        "Bitte zuerst Beispiel-Dateien ablegen (Zeichnungen + Bestellung).",
                        "Schritt 2",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                    return;
                }

                if (!RunAnalysis())
                    return;

                ShowStep(3);
                AppendChatSystem("Schritt 3: Analyse fertig. Sortier-Vorschau prüfen oder direkt «Kunde anlegen».");
                if (DeclaresExcel() || DeclaresOrderList())
                    OpenSortPreview();
            }
        }

        private bool ValidateStep1()
        {
            var name = CustomerNameTextBox.Text?.Trim();
            if (string.IsNullOrWhiteSpace(name))
            {
                MessageBox.Show(this, "Bitte einen Kundennamen eingeben.", "Schritt 1",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                CustomerNameTextBox.Focus();
                return false;
            }

            if (CustomerProfile.IsBuiltInCustomerName(name)
                || CustomerProfile.IsBuiltInCustomerId(CustomerProfileStore.CustomerProfileDraftSlug(name)))
            {
                MessageBox.Show(this,
                    "Tesla und EcoMark sind feste Kunden.\r\nBitte einen anderen Namen wählen.",
                    "Schritt 1",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return false;
            }

            if (HasDrawingsCheckBox.IsChecked != true)
            {
                MessageBox.Show(this, "Bitte «Zeichnungen (PDF)» ankreuzen – ohne Zeichnungen kann nicht sortiert werden.",
                    "Schritt 1", MessageBoxButton.OK, MessageBoxImage.Information);
                return false;
            }

            if (!DeclaresExcel() && !DeclaresOrderList())
            {
                MessageBox.Show(this,
                    "Bitte die Bestellung ankreuzen:\r\n• Excel-Bestellung\r\n• Bestell-PDF\r\n• Text-Bestellung (.txt/.csv)",
                    "Schritt 1",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return false;
            }

            return true;
        }

        private void RefreshFilesSummary()
        {
            if (FilesSummaryTextBlock == null)
                return;

            if (_staging.Files.Count == 0)
            {
                FilesSummaryTextBlock.Text = "Noch keine Dateien.";
                return;
            }

            FilesSummaryTextBlock.Text = string.Join(Environment.NewLine,
                _staging.Files.Select(Path.GetFileName));
        }

        private void Analyze_Click(object sender, RoutedEventArgs e)
        {
            if (!RunAnalysis())
                return;

            if (_step < 3)
                ShowStep(3);

            AppendChatSystem("Analyse aktualisiert.");
            if (DeclaresExcel() || DeclaresOrderList())
                OpenSortPreview();
        }

        private bool RunAnalysis()
        {
            if (_staging.Files.Count == 0)
            {
                MessageBox.Show(this, "Bitte zuerst Beispiel-Dateien einfügen (PDF, Excel, Text, ZIP …).", "Analyse",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return false;
            }

            var analyzable = _staging.Files
                .Where(f =>
                {
                    var ext = Path.GetExtension(f)?.ToLowerInvariant();
                    return ext == ".pdf" || ext == ".xlsx" || ext == ".xls"
                           || ext == ".txt" || ext == ".csv";
                })
                .ToList();

            if (analyzable.Count == 0)
            {
                MessageBox.Show(this, "Keine PDF-/Excel-/Text-Dateien zum Analysieren gefunden.", "Analyse",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return false;
            }

            var previousNotes = _draft?.AnalysisLog?
                .Where(l => l != null && l.StartsWith("Korrektur:", StringComparison.OrdinalIgnoreCase))
                .ToList() ?? new List<string>();

            _draft = _learningService.AnalyzeFiles(CustomerNameTextBox.Text, analyzable);
            ApplyDocumentSelectionToDraft();
            foreach (var note in previousNotes)
                _draft.AnalysisLog.Add(note);

            if (AnalysisTextBlock != null)
                AnalysisTextBlock.Text = string.Join(Environment.NewLine, _draft.AnalysisLog);
            PostAnalysisSummaryToChat();
            return true;
        }

        private void PostAnalysisSummaryToChat()
        {
            var summary = BuildAnalysisChatSummary(_draft, _staging.Files.Count);
            AppendChatSystem(summary);
            _chatHistory.Add(new AiChatMessage("assistant", summary));
        }

        private static string BuildAnalysisChatSummary(CustomerProfileDraft draft, int fileCount)
        {
            var sb = new StringBuilder();
            sb.AppendLine("Analyse-Ergebnis:");

            if (draft == null)
            {
                sb.Append("Keine Analyse vorhanden.");
                return sb.ToString();
            }

            var drawings = draft.SampleDrawingNumbers?.Count ?? 0;
            var excelOk = draft.RequiresExcel
                && (draft.ExcelDrawingHeaders?.Count > 0 || draft.ExcelAmountHeaders?.Count > 0);

            if (drawings > 0)
                sb.AppendLine("• Zeichnungsnummern: " + drawings + " gefunden (" + string.Join(", ", draft.SampleDrawingNumbers.Take(5)) + (drawings > 5 ? ", …" : "") + ")");
            else
                sb.AppendLine("• Zeichnungsnummern: NICHT gefunden – Dateien prüfen oder hier beschreiben.");

            if (draft.RequiresExcel)
            {
                if (excelOk)
                {
                    sb.AppendLine("• Excel: erkannt"
                                  + (draft.ExcelDrawingHeaders?.Count > 0 ? ", Zeichnungsspalte: " + string.Join("/", draft.ExcelDrawingHeaders.Take(3)) : "")
                                  + (draft.ExcelAmountHeaders?.Count > 0 ? ", Menge: " + string.Join("/", draft.ExcelAmountHeaders.Take(3)) : ""));
                }
                else
                    sb.AppendLine("• Excel: Spalten unklar – hier z. B. «Spalte für Zeichnung heißt …»");
            }
            else if (draft.RequiresOrderPdf)
                sb.AppendLine("• Workflow: Bestellung aus PDF/Text (ohne Excel)");

            sb.AppendLine("• Dateien: " + fileCount);
            if (drawings > 0)
                sb.AppendLine("Einschätzung: OK – Sortier-Vorschau prüfen oder «Kunde anlegen».");
            else
                sb.AppendLine("Einschätzung: Unvollständig – ohne Zeichnungsnummern nicht speichern.");

            return sb.ToString().TrimEnd();
        }

        private void ChatInput_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key != System.Windows.Input.Key.Enter)
                return;
            e.Handled = true;
            _ = ApplyChatMessageAsync(ChatInputTextBox.Text);
        }

        private async void ChatSend_Click(object sender, RoutedEventArgs e)
        {
            await ApplyChatMessageAsync(ChatInputTextBox.Text).ConfigureAwait(true);
        }

        private OfflineAiOrchestrator ResolveOrchestrator()
        {
            return HostMainWindow?.AiOrchestrator
                   ?? (Owner as MainWindow)?.AiOrchestrator
                   ?? (Application.Current?.MainWindow as MainWindow)?.AiOrchestrator;
        }

        private async System.Threading.Tasks.Task ApplyChatMessageAsync(string message)
        {
            var text = (message ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(text) || _chatBusy)
                return;

            ChatInputTextBox.Text = string.Empty;
            AppendChatUser(text);
            _chatHistory.Add(new AiChatMessage("user", text));
            _chatBusy = true;

            var waitingShown = false;
            try
            {
                if (_draft == null && _staging.Files.Count > 0)
                    RunAnalysis();
                if (_draft == null)
                    _draft = new CustomerProfileDraft();

                var orchestrator = ResolveOrchestrator();
                var context = new CustomerProfileEditorContext
                {
                    CustomerName = CustomerNameTextBox.Text?.Trim() ?? string.Empty,
                    Draft = _draft,
                    SampleFileNames = _staging.Files.Select(Path.GetFileName).ToList(),
                    StagingFiles = _staging.Files.ToList(),
                    DeclaresDrawings = HasDrawingsCheckBox.IsChecked == true,
                    DeclaresExcel = HasExcelCheckBox.IsChecked == true,
                    DeclaresOrderPdf = HasOrderPdfCheckBox.IsChecked == true,
                    DeclaresOrderText = HasOrderTextCheckBox.IsChecked == true
                };

                string reply;
                var didChange = false;
                if (orchestrator != null)
                {
                    if (!CustomerAiAgentService.LooksLikeInstantAction(text)
                        && !CustomerAiAgentService.IsPureGreetingPublic(text))
                    {
                        AppendChatSystem("KI denkt und setzt um … (max. ~45 Sekunden)");
                        waitingShown = true;
                    }

                    var agent = await CustomerAiAgentService.HandleAsync(
                            text,
                            context,
                            orchestrator,
                            _chatHistory,
                            System.Threading.CancellationToken.None)
                        .ConfigureAwait(true);

                    reply = agent?.Reply;
                    didChange = agent?.DidChange == true;

                    if (didChange && !string.IsNullOrWhiteSpace(context.CustomerName)
                        && context.CustomerName.Trim().Length < 80)
                    {
                        CustomerNameTextBox.Text = context.CustomerName.Trim();
                    }

                    ApplyDocumentSelectionToDraft();
                    if (_draft != null && AnalysisTextBlock != null)
                        AnalysisTextBlock.Text = string.Join(Environment.NewLine, _draft.AnalysisLog);

                    if (didChange)
                    {
                        HostMainWindow?.ReloadWatermarkSettingsFromStore();
                        (Owner as MainWindow)?.ReloadWatermarkSettingsFromStore();
                    }

                    if (agent?.OpenSortPreview == true)
                    {
                        if (waitingShown)
                            RemoveLastWaitingLine();
                        waitingShown = false;
                        OpenSortPreview();
                    }
                }
                else
                {
                    var parsed = CustomerProfileDescriptionParser.Parse(text);
                    ApplyParsedToDraft(parsed);
                    ApplyDocumentSelectionToDraft();
                    reply = "Hauptfenster-KI nicht verbunden. Bitte Dialog über das Hauptfenster öffnen.";
                }

                if (waitingShown)
                    RemoveLastWaitingLine();

                if (string.IsNullOrWhiteSpace(reply))
                    reply = "Keine Antwort von der KI. Prüfe KI-Einstellungen und Ordner AI_DOK-V01.";

                AppendChatSystem(reply);
                _chatHistory.Add(new AiChatMessage("assistant", reply));
            }
            catch (Exception ex)
            {
                if (waitingShown)
                    RemoveLastWaitingLine();
                var err = "Fehler im Chat: " + ex.Message;
                AppendChatSystem(err);
                _chatHistory.Add(new AiChatMessage("assistant", err));
            }
            finally
            {
                _chatBusy = false;
            }
        }

        private void RemoveLastWaitingLine()
        {
            var text = _chatLog.ToString();
            const string marker = "KI: KI denkt";
            var idx = text.LastIndexOf(marker, StringComparison.Ordinal);
            if (idx < 0)
                return;
            _chatLog.Clear();
            _chatLog.Append(text.Substring(0, idx).TrimEnd());
            if (_chatLog.Length > 0)
                _chatLog.AppendLine().AppendLine();
            ChatHistoryTextBlock.Text = string.IsNullOrWhiteSpace(_chatLog.ToString())
                ? string.Empty
                : _chatLog.ToString().TrimEnd();
        }

        private void AppendChatUser(string text)
        {
            _chatLog.AppendLine("Du: " + text);
            _chatLog.AppendLine();
            ChatHistoryTextBlock.Text = _chatLog.ToString().TrimEnd();
            ScrollChatToEnd();
        }

        private void AppendChatSystem(string text)
        {
            _chatLog.AppendLine("KI: " + text);
            _chatLog.AppendLine();
            ChatHistoryTextBlock.Text = _chatLog.ToString().TrimEnd();
            ScrollChatToEnd();
        }

        private void ScrollChatToEnd()
        {
            try
            {
                ChatScrollViewer?.ScrollToEnd();
            }
            catch
            {
            }
        }

        private void DocumentSelection_Changed(object sender, RoutedEventArgs e)
        {
            ApplyDocumentSelectionToDraft();
            if (_draft != null && AnalysisTextBlock != null)
                AnalysisTextBlock.Text = string.Join(Environment.NewLine, _draft.AnalysisLog);
        }

        private bool DeclaresExcel()
        {
            return HasExcelCheckBox.IsChecked == true;
        }

        private bool DeclaresOrderPdf()
        {
            return HasOrderPdfCheckBox.IsChecked == true;
        }

        private bool DeclaresOrderText()
        {
            return HasOrderTextCheckBox.IsChecked == true;
        }

        private bool DeclaresOrderList()
        {
            return DeclaresOrderPdf() || DeclaresOrderText();
        }

        private void SyncDocumentCheckboxesFromStaging()
        {
            if (!string.IsNullOrWhiteSpace(_staging.ExcelPath) && File.Exists(_staging.ExcelPath))
                HasExcelCheckBox.IsChecked = true;

            if (!string.IsNullOrWhiteSpace(_staging.OrderPdfPath) && File.Exists(_staging.OrderPdfPath))
                HasOrderPdfCheckBox.IsChecked = true;

            if (!string.IsNullOrWhiteSpace(_staging.OrderTextPath) && File.Exists(_staging.OrderTextPath))
                HasOrderTextCheckBox.IsChecked = true;

            var hasDrawingPdf = _staging.Files.Any(f =>
            {
                var ext = Path.GetExtension(f)?.ToLowerInvariant();
                if (ext != ".pdf")
                    return false;
                return !OrderVerificationFileHelper.IsOrderVerificationFileName(Path.GetFileName(f));
            });
            if (hasDrawingPdf)
                HasDrawingsCheckBox.IsChecked = true;
        }

        private void ApplyDocumentSelectionToDraft()
        {
            if (_draft == null)
                return;

            var hasExcel = DeclaresExcel();
            var hasOrderPdf = DeclaresOrderPdf();
            var hasOrderText = DeclaresOrderText();
            string note;
            if (hasExcel)
            {
                _draft.EnsureExcelWorkflow();
                if (hasOrderPdf || hasOrderText)
                    note = "Vorgabe: Excel-Bestellung führt, Bestell-PDF/Text nur optional zum Abgleich.";
                else
                    note = "Vorgabe: Excel + Zeichnungen – keine Prüf-PDF.";
            }
            else if (hasOrderText && !hasOrderPdf)
            {
                _draft.EnsureOrderPdfWorkflow();
                note = "Vorgabe: Text-Bestellung + Zeichnungen (ohne Excel).";
            }
            else if (hasOrderPdf || hasOrderText)
            {
                _draft.EnsureOrderPdfWorkflow();
                note = "Vorgabe: Bestell-PDF + Zeichnungen (ohne Excel).";
            }
            else
            {
                _draft.WorkflowKind = CustomerWorkflowKind.ExcelPrimary;
                _draft.RequiresExcel = false;
                _draft.RequiresOrderPdf = false;
                note = "Vorgabe: nur Zeichnungen – zum Sortieren Excel, Bestell-PDF oder Text ankreuzen.";
            }

            _draft.AnalysisLog.RemoveAll(l =>
                l != null && l.StartsWith("Vorgabe:", StringComparison.OrdinalIgnoreCase));
            _draft.AnalysisLog.Add(note);
        }

        private void SortPreview_Click(object sender, RoutedEventArgs e)
        {
            OpenSortPreview();
        }

        private void OpenSortPreview()
        {
            if (_draft == null && !RunAnalysis())
                return;

            if (_draft == null)
                return;

            if (!DeclaresExcel() && !DeclaresOrderList())
            {
                MessageBox.Show(this,
                    "Bitte in Schritt 1 ankreuzen, welche Bestellung vorliegt.",
                    "Sortier-Vorschau",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            var name = CustomerNameTextBox.Text?.Trim();
            if (string.IsNullOrWhiteSpace(name))
                name = "Vorschau-Kunde";

            ApplyDocumentSelectionToDraft();
            var profile = _draft.ToProfile(name);
            var main = HostMainWindow ?? Owner as MainWindow ?? Application.Current?.MainWindow as MainWindow;
            if (main == null)
            {
                MessageBox.Show(this,
                    "Sortier-Vorschau benötigt das Hauptfenster. Bitte über Einstellungen → Kundenprofile öffnen.",
                    "Sortier-Vorschau",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            var excel = DeclaresExcel() ? _staging.ExcelPath : string.Empty;
            string orderListPath = string.Empty;
            if (!DeclaresExcel())
            {
                if (DeclaresOrderText() && !string.IsNullOrWhiteSpace(_staging.OrderTextPath))
                    orderListPath = _staging.OrderTextPath;
                else if (DeclaresOrderPdf())
                    orderListPath = _staging.OrderPdfPath;
                else
                    orderListPath = _staging.OrderTextPath ?? _staging.OrderPdfPath;
            }

            var dlg = new CustomerSortPreviewWindow(
                main,
                profile,
                _staging.StagingFolder,
                excel,
                orderListPath)
            {
                Owner = this,
                WindowStartupLocation = WindowStartupLocation.CenterOwner
            };
            ThemeWindowService.Attach(dlg);
            dlg.ShowDialog();
        }

        private bool ApplyParsedToDraft(CustomerProfileDescriptionParseResult parsed)
        {
            if (parsed == null || _draft == null)
                return false;

            var changed = false;

            if (parsed.SampleDrawingNumbers != null && parsed.SampleDrawingNumbers.Count > 0)
            {
                foreach (var sample in parsed.SampleDrawingNumbers)
                {
                    if (string.IsNullOrWhiteSpace(sample))
                        continue;
                    if (_draft.SampleDrawingNumbers.Any(s => string.Equals(s, sample, StringComparison.OrdinalIgnoreCase)))
                        continue;
                    _draft.SampleDrawingNumbers.Add(sample.Trim().ToUpperInvariant());
                    changed = true;
                }

                _draft.DrawingNumberPattern = CustomerProfilePatternBuilder.BuildDrawingPattern(_draft.SampleDrawingNumbers);
            }

            if (!string.IsNullOrWhiteSpace(parsed.DrawingNumberPattern)
                && !string.Equals(parsed.DrawingNumberPattern, CustomerProfile.DefaultDrawingPattern, StringComparison.Ordinal)
                && !string.Equals(_draft.DrawingNumberPattern, parsed.DrawingNumberPattern, StringComparison.Ordinal))
            {
                _draft.DrawingNumberPattern = parsed.DrawingNumberPattern.Trim();
                changed = true;
            }

            if (parsed.ExcelDrawingHeaders != null)
            {
                foreach (var header in parsed.ExcelDrawingHeaders)
                {
                    if (string.IsNullOrWhiteSpace(header))
                        continue;
                    if (_draft.ExcelDrawingHeaders.Any(h => string.Equals(h, header, StringComparison.OrdinalIgnoreCase)))
                        continue;
                    _draft.ExcelDrawingHeaders.Add(header.Trim());
                    changed = true;
                }
            }

            if (parsed.ExcelAmountHeaders != null)
            {
                foreach (var header in parsed.ExcelAmountHeaders)
                {
                    if (string.IsNullOrWhiteSpace(header))
                        continue;
                    if (_draft.ExcelAmountHeaders.Any(h => string.Equals(h, header, StringComparison.OrdinalIgnoreCase)))
                        continue;
                    _draft.ExcelAmountHeaders.Add(header.Trim());
                    changed = true;
                }
            }

            if (parsed.WorkflowKind.HasValue)
            {
                _draft.WorkflowKind = parsed.WorkflowKind.Value;
                changed = true;
            }

            if (parsed.RequiresExcel.HasValue)
            {
                _draft.RequiresExcel = parsed.RequiresExcel.Value;
                changed = true;
            }

            if (parsed.RequiresOrderPdf.HasValue)
            {
                _draft.RequiresOrderPdf = parsed.RequiresOrderPdf.Value;
                changed = true;
            }

            return changed;
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            if (!ValidateStep1())
            {
                ShowStep(1);
                return;
            }

            var name = CustomerNameTextBox.Text.Trim();

            if (_draft == null && !RunAnalysis())
            {
                ShowStep(2);
                return;
            }

            ApplyDocumentSelectionToDraft();

            if (_draft == null || _draft.SampleDrawingNumbers == null || _draft.SampleDrawingNumbers.Count == 0)
            {
                MessageBox.Show(this,
                    "Es konnten keine Zeichnungsnummern erkannt werden. Bitte in Schritt 2 weitere Dateien hinzufügen oder rechts der KI beschreiben.",
                    "Kunde anlegen",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                ShowStep(2);
                return;
            }

            var profile = _draft.ToProfile(name);
            var existing = CustomerProfileStore.GetById(profile.Id);
            if (existing != null)
            {
                var overwrite = MessageBox.Show(this,
                    "Ein Kunde mit diesem Namen existiert bereits. Überschreiben?",
                    "Kunde anlegen",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);
                if (overwrite != MessageBoxResult.Yes)
                    return;
            }

            try
            {
                CustomerProfileStore.Save(profile);
                CustomerProfileSettingsStore.SaveSnapshotFromGlobal(profile.Id);
                MessageBox.Show(this,
                    "Kunde '" + profile.DisplayName + "' wurde gespeichert.\r\n\r\n"
                    + "Beim Sortieren erkennt die App diesen Kunden anhand der Zeichnungsnummern.",
                    "Gespeichert",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                DialogResult = true;
                Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "Fehler", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}
