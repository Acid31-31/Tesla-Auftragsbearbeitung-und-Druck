using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using Microsoft.Win32;
using PDF_umbenenen.Platform.Core;
using PDF_umbenenen.Platform.Models;

namespace PDF_umbenenen.Platform.UI
{
    public partial class PlatformDashboardWindow : Window
    {
        private DocumentAnalysisResult _lastAnalysis;
        private AiDecisionRecord _lastDecision;

        public PlatformDashboardWindow()
        {
            InitializeComponent();
            Loaded += (_, __) => RefreshDashboard();
        }

        private void BrowseFile_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog
            {
                Filter = "Dokumente|*.pdf;*.xlsx;*.xls;*.docx;*.zip;*.png;*.jpg|Alle Dateien|*.*"
            };

            if (dlg.ShowDialog() == true)
                PreviewFileBox.Text = dlg.FileName;
        }

        private async void Analyze_Click(object sender, RoutedEventArgs e)
        {
            var path = PreviewFileBox.Text?.Trim();
            if (!File.Exists(path))
            {
                MessageBox.Show(this, "Bitte eine gültige Datei wählen.", "KI-Plattform", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            try
            {
                _lastAnalysis = await PlatformHost.Orchestrator.AnalyzeDocumentAsync(path).ConfigureAwait(true);
                var rules = await PlatformHost.Orchestrator.EvaluateRulesAsync(_lastAnalysis).ConfigureAwait(true);
                _lastDecision = await PlatformHost.Orchestrator.DecideAsync(_lastAnalysis, rules).ConfigureAwait(true);

                PreviewTextBox.Text = _lastAnalysis.PlainText ?? string.Empty;
                AnalysisBox.Text = BuildAnalysisText(_lastAnalysis);
                DecisionBox.Text = BuildDecisionText(_lastDecision, rules);
                ConfidenceBar.Value = (_lastAnalysis.Fields?.Confidence ?? 0d) * 100d;
                ConfidenceLabel.Text = "Confidence: " + (_lastAnalysis.Fields?.Confidence ?? 0d).ToString("0.0%");
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "Analyse fehlgeschlagen", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void SaveCorrection_Click(object sender, RoutedEventArgs e)
        {
            PlatformHost.Learning.RecordCorrection(new UserCorrection
            {
                DocumentKey = LearnDocKeyBox.Text?.Trim(),
                FieldName = LearnFieldBox.Text?.Trim(),
                NewValue = LearnValueBox.Text?.Trim()
            });

            MessageBox.Show(this, "Korrektur in lokaler Wissensbasis gespeichert.", "Lernmodus", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void Refresh_Click(object sender, RoutedEventArgs e) => RefreshDashboard();

        private void RefreshDashboard()
        {
            var knowledge = PlatformHost.Knowledge.Search(null, null);
            StatsBox.Text = "Wissenseinträge: " + knowledge.Count + Environment.NewLine
                + "Vektor-Index: aktiv" + Environment.NewLine
                + "API: " + (PlatformHost.Api?.IsRunning == true ? "läuft" : "gestoppt");

            var logDir = Path.Combine(AppInfo.UserDataDirectory, "Logs", "Platform");
            var audit = Path.Combine(logDir, "audit.log");
            var history = Path.Combine(logDir, "user-activity.log");
            HistoryBox.Text = File.Exists(history) ? ReadTail(history, 80) : "(kein Verlauf)";
            if (File.Exists(audit))
                HistoryBox.Text += Environment.NewLine + Environment.NewLine + "— Audit —" + Environment.NewLine + ReadTail(audit, 40);

            var snap = PlatformHost.Resources.GetSnapshot();
            MonitorBox.Text = "RAM: " + snap.WorkingSetMb + " MB" + Environment.NewLine
                + "CPU: " + snap.CpuUsagePercent + " %" + Environment.NewLine
                + "Kerne: " + snap.ProcessorCount + Environment.NewLine
                + "GPU: " + (snap.GpuAvailable ? snap.GpuName : "nicht erkannt") + Environment.NewLine
                + "Worker: " + PlatformHost.Resources.RecommendedWorkerCount;
        }

        private void Close_Click(object sender, RoutedEventArgs e) => Close();

        private static string BuildAnalysisText(DocumentAnalysisResult analysis)
        {
            if (analysis == null)
                return string.Empty;

            var sb = new StringBuilder();
            sb.AppendLine("Format: " + analysis.Document?.Format);
            sb.AppendLine("Klassifikation: " + analysis.Classification?.Kind + " (" + (analysis.Classification?.Confidence ?? 0d).ToString("0.0%") + ")");
            sb.AppendLine("Schritte: " + string.Join(", ", analysis.ProcessingSteps ?? Enumerable.Empty<string>()));
            if (analysis.Fields != null)
            {
                sb.AppendLine();
                sb.AppendLine("Zeichnungsnr: " + analysis.Fields.DrawingNumber);
                sb.AppendLine("Material: " + analysis.Fields.Material);
                sb.AppendLine("Dicke: " + analysis.Fields.MaterialThickness);
                sb.AppendLine("Kunde: " + analysis.Fields.Customer);
                sb.AppendLine("Baugruppe: " + analysis.Fields.IsAssembly);
            }

            return sb.ToString();
        }

        private static string BuildDecisionText(AiDecisionRecord decision, RuleEvaluationResult rules)
        {
            var sb = new StringBuilder();
            if (rules != null)
            {
                sb.AppendLine("Regel: " + rules.RuleId + " → " + rules.Action);
                sb.AppendLine("Regel-Treffer: " + rules.Matched);
                sb.AppendLine();
            }

            if (decision != null)
            {
                sb.AppendLine("Entscheidung: " + decision.Decision);
                sb.AppendLine("Confidence: " + decision.Confidence.ToString("0.0%"));
                sb.AppendLine("Begründung: " + decision.Rationale);
                foreach (var pair in decision.Evidence)
                    sb.AppendLine(pair.Key + ": " + pair.Value);
            }

            return sb.ToString();
        }

        private static string ReadTail(string path, int maxLines)
        {
            try
            {
                var lines = File.ReadAllLines(path);
                return string.Join(Environment.NewLine, lines.Skip(Math.Max(0, lines.Length - maxLines)));
            }
            catch
            {
                return string.Empty;
            }
        }
    }
}
