using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using PDF_umbenenen.Models;
using PDF_umbenenen.Services;
using MessageBox = System.Windows.MessageBox;

namespace PDF_umbenenen
{
    public partial class GeoCollectWindow : Window
    {
        private readonly List<GeoCollectResult> _allResults = new List<GeoCollectResult>();
        private readonly IList<OrderExcelRow> _excelRows;
        private readonly string _orderNumber;
        private readonly string _pdfFolder;
        private string _targetFolder = string.Empty;
        private Point _dragStart;
        private bool _dragPending;
        private bool _suppressSelectionChangeOnDrag;
        private bool _searchStarted;

        public GeoCollectWindow(
            IList<OrderExcelRow> excelRows,
            string orderNumber,
            string pdfFolder = null)
        {
            InitializeComponent();
            _excelRows = excelRows ?? new List<OrderExcelRow>();
            _orderNumber = string.IsNullOrWhiteSpace(orderNumber) ? "Auftrag" : orderNumber.Trim();
            _pdfFolder = pdfFolder ?? string.Empty;

            Title = "GEO-Dateien · " + _orderNumber;
            SummaryTextBlock.Text = "Suche wird vorbereitet…";
            TargetFolderTextBlock.Text = string.Empty;

            Loaded += GeoCollectWindow_Loaded;
        }

        private async void GeoCollectWindow_Loaded(object sender, RoutedEventArgs e)
        {
            if (_searchStarted)
                return;
            _searchStarted = true;

            Activate();
            Focus();

            try
            {
                _targetFolder = GeoSettingsStore.ResolveTargetFolder(_orderNumber);
                TargetFolderTextBlock.Text = "Kopien: " + _targetFolder;
            }
            catch (Exception ex)
            {
                TargetFolderTextBlock.Text = "Zielordner: " + ex.Message;
            }

            await RunCollectAsync().ConfigureAwait(true);
        }

        private async Task RunCollectAsync()
        {
            var settings = GeoSettingsStore.Load();
            if (settings.SearchFolders == null || settings.SearchFolders.Count == 0)
            {
                SummaryTextBlock.Text = "Keine Suchordner konfiguriert. Bitte unter GEO-Einstellungen Ordner hinzufügen.";
                _allResults.Clear();
                ApplyFilter();
                return;
            }

            var queries = GeoFileSearchService.BuildQueriesFromExcelRows(_excelRows);
            if (queries.Count == 0)
            {
                SummaryTextBlock.Text = "Keine Excel-Positionen zum Abgleich.";
                _allResults.Clear();
                ApplyFilter();
                return;
            }

            GeoFileSearchService.EnrichThicknessFromSortedPdfs(queries, _pdfFolder);

            SummaryTextBlock.Text = "Suche in " + settings.SearchFolders.Count
                + " Ordner(n) · " + queries.Count + " Position(en)…";
            Mouse.OverrideCursor = Cursors.Wait;

            List<GeoCollectResult> results;
            string error = null;
            var orderNumber = _orderNumber;
            var folders = settings.SearchFolders.ToList();
            Action<string> progress = msg =>
            {
                try
                {
                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        if (!string.IsNullOrWhiteSpace(msg))
                            SummaryTextBlock.Text = msg;
                    }));
                }
                catch
                {
                }
            };

            try
            {
                results = await Task.Run(() =>
                {
                    try
                    {
                        return GeoFileSearchService.CollectForOrder(queries, orderNumber, folders, progress);
                    }
                    catch (Exception ex)
                    {
                        error = ex.Message;
                        return new List<GeoCollectResult>();
                    }
                }).ConfigureAwait(true);
            }
            finally
            {
                Mouse.OverrideCursor = null;
            }

            if (!string.IsNullOrWhiteSpace(error))
            {
                SummaryTextBlock.Text = "GEO-Suche fehlgeschlagen: " + error;
                MessageBox.Show(this, error, "GEO-Suche", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            _allResults.Clear();
            _allResults.AddRange(results ?? new List<GeoCollectResult>());

            var found = _allResults.Count(r => r.Found);
            var missing = _allResults.Count(r => !r.Found);
            SummaryTextBlock.Text = found + " gefunden, " + missing
                + " müssen neu erstellt werden · Auftrag " + _orderNumber;

            BuildThicknessFilter();
            ApplyFilter();
            Activate();
        }

        private void BuildThicknessFilter()
        {
            var thicknesses = _allResults
                .Where(r => !string.IsNullOrWhiteSpace(r.ThicknessMm))
                .Select(r => r.ThicknessMm)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(t => t, StringComparer.OrdinalIgnoreCase)
                .ToList();

            ThicknessFilterCombo.SelectionChanged -= ThicknessFilter_Changed;
            ThicknessFilterCombo.Items.Clear();
            ThicknessFilterCombo.Items.Add("(alle)");
            foreach (var t in thicknesses)
                ThicknessFilterCombo.Items.Add(t);
            ThicknessFilterCombo.SelectedIndex = 0;
            ThicknessFilterCombo.SelectionChanged += ThicknessFilter_Changed;
        }

        private void ApplyFilter()
        {
            IEnumerable<GeoCollectResult> filtered = _allResults;
            var selected = ThicknessFilterCombo.SelectedItem as string;
            if (!string.IsNullOrWhiteSpace(selected)
                && !string.Equals(selected, "(alle)", StringComparison.OrdinalIgnoreCase))
            {
                filtered = filtered.Where(r =>
                    string.Equals(r.ThicknessMm, selected, StringComparison.OrdinalIgnoreCase));
            }

            GeoListView.ItemsSource = filtered
                .OrderBy(r => r.ThicknessSortKey, StringComparer.OrdinalIgnoreCase)
                .ThenBy(r => r.DrawingNumber, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private void ThicknessFilter_Changed(object sender, SelectionChangedEventArgs e)
        {
            ApplyFilter();
        }

        private void SelectAllVisible_Click(object sender, RoutedEventArgs e)
        {
            GeoListView.SelectAll();
            GeoListView.Focus();
        }

        private void SelectAll_Executed(object sender, ExecutedRoutedEventArgs e)
        {
            GeoListView.SelectAll();
            e.Handled = true;
        }

        private void OpenTargetFolder_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var folder = string.IsNullOrWhiteSpace(_targetFolder)
                    ? GeoSettingsStore.ResolveTargetFolder(_orderNumber)
                    : _targetFolder;
                if (!Directory.Exists(folder))
                    Directory.CreateDirectory(folder);
                Process.Start(new ProcessStartInfo
                {
                    FileName = folder,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "GEO", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void OpenSettings_Click(object sender, RoutedEventArgs e)
        {
            var win = new GeoSettingsWindow { Owner = this };
            ThemeWindowService.Attach(win);
            if (win.ShowDialog() == true)
            {
                _searchStarted = false;
                SummaryTextBlock.Text = "Einstellungen geändert – Suche wird neu gestartet…";
                _ = RunCollectAsync();
            }
        }

        private void GeoListView_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            var item = GeoListView.SelectedItem as GeoCollectResult;
            if (item == null || !item.Found || string.IsNullOrWhiteSpace(item.CopyPath))
                return;
            if (!File.Exists(item.CopyPath))
                return;

            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = item.CopyPath,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "GEO öffnen", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void GeoListView_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _dragStart = e.GetPosition(null);
            _dragPending = true;
            _suppressSelectionChangeOnDrag = false;

            // Klick auf bereits ausgewählte Zeile: Auswahl (Mehrfach) für Drag behalten
            var row = FindAncestor<ListViewItem>(e.OriginalSource as DependencyObject);
            if (row != null && row.IsSelected
                && (Keyboard.Modifiers & (ModifierKeys.Control | ModifierKeys.Shift)) == ModifierKeys.None)
            {
                _suppressSelectionChangeOnDrag = true;
                e.Handled = true;
            }
        }

        private void GeoListView_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (!_suppressSelectionChangeOnDrag)
                return;

            _suppressSelectionChangeOnDrag = false;
            // Kurzer Klick ohne Drag → normale Einzelauswahl
            if (!_dragPending)
                return;

            _dragPending = false;
            var row = FindAncestor<ListViewItem>(e.OriginalSource as DependencyObject);
            if (row == null)
                return;

            GeoListView.SelectedItems.Clear();
            row.IsSelected = true;
            row.Focus();
        }

        private void GeoListView_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (!_dragPending || e.LeftButton != MouseButtonState.Pressed)
                return;

            var pos = e.GetPosition(null);
            if (Math.Abs(pos.X - _dragStart.X) < SystemParameters.MinimumHorizontalDragDistance
                && Math.Abs(pos.Y - _dragStart.Y) < SystemParameters.MinimumVerticalDragDistance)
                return;

            _dragPending = false;
            _suppressSelectionChangeOnDrag = false;
            StartFileDrag();
        }

        private void StartFileDrag()
        {
            var files = GeoListView.SelectedItems
                .Cast<object>()
                .OfType<GeoCollectResult>()
                .Where(r => r.Found && !string.IsNullOrWhiteSpace(r.CopyPath) && File.Exists(r.CopyPath))
                .Select(r => r.CopyPath)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            if (files.Length == 0)
                return;

            var data = new DataObject(DataFormats.FileDrop, files);
            DragDrop.DoDragDrop(GeoListView, data, DragDropEffects.Copy);
        }

        private static T FindAncestor<T>(DependencyObject current) where T : DependencyObject
        {
            while (current != null)
            {
                if (current is T match)
                    return match;
                current = System.Windows.Media.VisualTreeHelper.GetParent(current);
            }

            return null;
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
