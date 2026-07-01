using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using PDF_umbenenen.Models;
using PDF_umbenenen.Services;
using PDF_umbenenen.Services.Ai;

namespace PDF_umbenenen
{
    public partial class NewCustomerWindow : Window
    {
        private readonly CustomerProfileLearningService _learningService;
        private readonly List<string> _sampleFiles = new List<string>();
        private CustomerProfileDraft _draft;

        public NewCustomerWindow()
        {
            InitializeComponent();
            ThemeWindowService.Attach(this);
            _learningService = new CustomerProfileLearningService(new PdfTextExtractionService());
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

            AddFiles(files);
            e.Handled = true;
        }

        private void AddFiles(IEnumerable<string> paths)
        {
            foreach (var path in paths)
            {
                if (Directory.Exists(path))
                {
                    foreach (var file in Directory.GetFiles(path, "*.*", SearchOption.TopDirectoryOnly))
                        TryAddFile(file);
                    continue;
                }

                TryAddFile(path);
            }

            RefreshFilesSummary();
        }

        private void TryAddFile(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                return;

            var ext = Path.GetExtension(path)?.ToLowerInvariant() ?? string.Empty;
            if (ext != ".pdf" && ext != ".xlsx" && ext != ".xls")
                return;

            if (_sampleFiles.Any(f => string.Equals(f, path, StringComparison.OrdinalIgnoreCase)))
                return;

            _sampleFiles.Add(path);
        }

        private void RefreshFilesSummary()
        {
            if (_sampleFiles.Count == 0)
            {
                FilesSummaryTextBlock.Text = "Keine Dateien.";
                return;
            }

            FilesSummaryTextBlock.Text = string.Join(Environment.NewLine,
                _sampleFiles.Select(Path.GetFileName));
        }

        private void Analyze_Click(object sender, RoutedEventArgs e)
        {
            RunAnalysis();
        }

        private void RunAnalysis()
        {
            if (_sampleFiles.Count == 0)
            {
                MessageBox.Show(this, "Bitte zuerst Beispiel-Dateien einfügen.", "Analyse",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            _draft = _learningService.AnalyzeFiles(CustomerNameTextBox.Text, _sampleFiles);
            AnalysisTextBlock.Text = string.Join(Environment.NewLine, _draft.AnalysisLog);
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            var name = CustomerNameTextBox.Text?.Trim();
            if (string.IsNullOrWhiteSpace(name))
            {
                MessageBox.Show(this, "Bitte einen Kundennamen eingeben.", "Speichern",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (_draft == null)
                RunAnalysis();

            if (_draft == null || _draft.SampleDrawingNumbers == null || _draft.SampleDrawingNumbers.Count == 0)
            {
                MessageBox.Show(this,
                    "Es konnten keine Zeichnungsnummern erkannt werden. Bitte weitere Beispiel-PDFs oder Excel hinzufügen.",
                    "Speichern",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            var profile = _draft.ToProfile(name);
            var existing = CustomerProfileStore.GetById(profile.Id);
            if (existing != null && !existing.IsBuiltIn)
            {
                var overwrite = MessageBox.Show(this,
                    "Ein Kunde mit diesem Namen existiert bereits. Überschreiben?",
                    "Speichern",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);
                if (overwrite != MessageBoxResult.Yes)
                    return;
            }

            try
            {
                CustomerProfileStore.Save(profile);
                MessageBox.Show(this,
                    "Kunde '" + profile.DisplayName + "' wurde gespeichert.\r\n\r\nBeim Sortieren erkennt die App diesen Kunden automatisch anhand der Zeichnungsnummern.",
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
