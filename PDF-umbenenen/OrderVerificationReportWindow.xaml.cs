using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;
using PDF_umbenenen.Models;
using PDF_umbenenen.Services;

namespace PDF_umbenenen
{
    public partial class OrderVerificationReportWindow : Window
    {
        private readonly OrderVerificationReport _report;
        private readonly string _excelPath;
        private readonly string _orderPdfPath;
        private readonly IList<OrderExcelRow> _excelRows;
        private readonly Action<int, string, string> _persistDeliveryDate;
        private readonly Action<string> _openDrawingPdf;
        private readonly bool _offerContinueOnIssues;
        private List<OrderVerificationRowDisplay> _displayRows;
        private bool _syncingPaneScroll;

        public bool ContinueDespiteIssues { get; private set; }

        public bool AdoptOrderPdfDates { get; private set; }

        public bool DatesModified { get; private set; }

        public IList<OrderExcelRow> UpdatedExcelRows
        {
            get { return _excelRows; }
        }

        private OrderVerificationReportWindow(
            OrderVerificationReport report,
            IList<OrderExcelRow> excelRows,
            string excelFileName,
            string orderPdfFileName,
            string excelPath,
            string orderPdfPath,
            bool offerContinueOnIssues,
            Action<int, string, string> persistDeliveryDate,
            Action<string> openDrawingPdf)
        {
            InitializeComponent();
            _report = report;
            _excelPath = excelPath;
            _orderPdfPath = orderPdfPath;
            _excelRows = excelRows ?? new List<OrderExcelRow>();
            _persistDeliveryDate = persistDeliveryDate;
            _openDrawingPdf = openDrawingPdf;
            _offerContinueOnIssues = offerContinueOnIssues;

            ThemeWindowService.MaximizeToWorkArea(this);

            ExcelFileTextBlock.Text = "Excel: " + (excelFileName ?? "—");
            PdfFileTextBlock.Text = "PDF: " + (orderPdfFileName ?? "—");
            PdfMetaTextBlock.Text = BuildPdfMetaText(orderPdfPath);
            FooterVersionTextBlock.Text = "DOK-V01 | Prüf-PDF | Version " + AppInfo.DisplayVersion;

            RefreshDisplay();
            UpdateActionButtons();
            Closing += OrderVerificationReportWindow_Closing;
        }

        private void ProtocolPane_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            var border = sender as Border;
            if (border == null || border.ActualWidth <= 0 || border.ActualHeight <= 0)
                return;

            var radius = border.CornerRadius.TopLeft;
            if (radius <= 0)
                radius = 10;

            border.Clip = new RectangleGeometry(
                new Rect(0, 0, border.ActualWidth, border.ActualHeight),
                radius,
                radius);
        }

        private void OrderVerificationReportWindow_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            CommitAllVisibleDateBoxes();
        }

        public static OrderVerificationDialogResult ShowDialog(
            Window owner,
            OrderVerificationReport report,
            IList<OrderExcelRow> excelRows,
            string excelFileName,
            string orderPdfFileName,
            string excelPath,
            string orderPdfPath,
            bool offerContinueOnIssues,
            Action<int, string, string> persistDeliveryDate = null,
            Action<string> openDrawingPdf = null)
        {
            var window = new OrderVerificationReportWindow(
                report,
                excelRows,
                excelFileName,
                orderPdfFileName,
                excelPath,
                orderPdfPath,
                offerContinueOnIssues,
                persistDeliveryDate,
                openDrawingPdf)
            {
                Owner = owner
            };
            ThemeWindowService.Attach(window);

            window.ShowDialog();

            var result = new OrderVerificationDialogResult
            {
                DatesModified = window.DatesModified,
                AdoptOrderPdfDates = window.AdoptOrderPdfDates,
                UpdatedExcelRows = window.UpdatedExcelRows
            };

            if (offerContinueOnIssues && report.HasBlockingIssues)
            {
                result.Proceed = window.DialogResult == true
                                 && (window.ContinueDespiteIssues || window.AdoptOrderPdfDates || window.DatesModified);
            }
            else
            {
                result.Proceed = window.DialogResult == true;
            }

            return result;
        }

        private void CommitAllVisibleDateBoxes()
        {
            Keyboard.ClearFocus();
            if (_displayRows == null)
                return;

            // Zuerst alle sichtbaren TextBoxen im VisualTree lesen (Binding ggf. noch nicht geschrieben)
            CommitVisibleTextBoxDates();

            foreach (var row in _displayRows)
            {
                if (row?.SourceRow == null)
                    continue;

                var typed = row.ExcelDate;
                if (string.IsNullOrWhiteSpace(typed) || typed == "—")
                    continue;

                var normalized = NormalizeDate(typed);
                if (string.IsNullOrWhiteSpace(normalized))
                    continue;

                if (!string.Equals(NormalizeDate(row.SourceRow.DeliveryDate), normalized, StringComparison.OrdinalIgnoreCase))
                    ApplyDeliveryDateToRow(row, normalized);
            }
        }

        private void CommitVisibleTextBoxDates()
        {
            if (ExcelRowsItemsControl == null)
                return;

            foreach (var box in FindVisualChildren<TextBox>(ExcelRowsItemsControl))
            {
                if (box == null || box.DataContext == null)
                    continue;

                CommitExcelDateFromBox(box);
            }
        }

        private void PaneScroll_ScrollChanged(object sender, ScrollChangedEventArgs e)
        {
            if (_syncingPaneScroll || e.VerticalChange == 0)
                return;

            _syncingPaneScroll = true;
            try
            {
                var offset = e.VerticalOffset;
                if (!ReferenceEquals(sender, ExcelRowsScroll) && ExcelRowsScroll != null)
                    ExcelRowsScroll.ScrollToVerticalOffset(offset);
                if (!ReferenceEquals(sender, StatusRowsScroll) && StatusRowsScroll != null)
                    StatusRowsScroll.ScrollToVerticalOffset(offset);
                if (!ReferenceEquals(sender, PdfRowsScroll) && PdfRowsScroll != null)
                    PdfRowsScroll.ScrollToVerticalOffset(offset);
            }
            finally
            {
                _syncingPaneScroll = false;
            }
        }

        private static IEnumerable<T> FindVisualChildren<T>(DependencyObject parent) where T : DependencyObject
        {
            if (parent == null)
                yield break;

            for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                var typed = child as T;
                if (typed != null)
                    yield return typed;

                foreach (var nested in FindVisualChildren<T>(child))
                    yield return nested;
            }
        }

        private void RefreshDisplay()
        {
            _displayRows = OrderVerificationDisplayBuilder.Build(_excelRows, _report).ToList();
            ExcelRowsItemsControl.ItemsSource = _displayRows;
            StatusRowsItemsControl.ItemsSource = _displayRows;
            PdfRowsItemsControl.ItemsSource = _displayRows;
            UpdateCounts();
        }

        private void UpdateCounts()
        {
            if (_displayRows == null)
                return;

            var matchCount = _displayRows.Count(r => r.Status == OrderVerificationMatchStatus.Match);
            var deviationCount = _displayRows.Count(r => r.Status == OrderVerificationMatchStatus.Deviation);
            var errorCount = _displayRows.Count(r =>
                r.Status == OrderVerificationMatchStatus.MissingInPdf
                || r.Status == OrderVerificationMatchStatus.MissingInExcel);

            MatchCountTextBlock.Text = matchCount + " Übereinstimmung" + (matchCount == 1 ? string.Empty : "en");
            DeviationCountTextBlock.Text = deviationCount + " Abweichung" + (deviationCount == 1 ? string.Empty : "en");
            ErrorCountTextBlock.Text = errorCount + " Fehler";
        }

        private void UpdateActionButtons()
        {
            if (_displayRows == null)
                return;

            var deviationCount = _displayRows.Count(r => r.Status == OrderVerificationMatchStatus.Deviation);
            var errorCount = _displayRows.Count(r =>
                r.Status == OrderVerificationMatchStatus.MissingInPdf
                || r.Status == OrderVerificationMatchStatus.MissingInExcel);
            var hasDateMismatch = _displayRows.Any(r => r.HighlightPdfDate || r.HighlightExcelDate);
            var hasIssues = deviationCount > 0 || errorCount > 0
                            || (_report.MissingInPdf.Count > 0)
                            || (_report.MissingInExcel.Count > 0)
                            || (_report.MissingDrawingPdfs.Count > 0)
                            || (_report.DrawingsNotInOrderPdf.Count > 0);

            // Ein klarer Haupbutton – nie deaktiviert mit sinnlosem Warntext
            CompleteButton.IsEnabled = true;
            if (DatesModified)
            {
                CompleteButton.Content = "✓  Änderungen übernehmen";
                CompleteButton.Style = (Style)FindResource("ProtocolPrimaryBtn");
            }
            else if (hasIssues)
            {
                CompleteButton.Content = "Trotzdem übernehmen";
                CompleteButton.Style = (Style)FindResource("ProtocolOutlineBtn");
            }
            else
            {
                CompleteButton.Content = "✓  Prüfung abgeschlossen";
                CompleteButton.Style = (Style)FindResource("ProtocolPrimaryBtn");
            }

            // Optional: einzelne PDF-Termine nach links kopieren (nicht der Hauptweg)
            AdoptDatesButton.Visibility = hasDateMismatch ? Visibility.Visible : Visibility.Collapsed;
            AdoptDatesButton.Content = "Nur PDF → Excel kopieren";

            // Weiterführen läuft immer über CompleteButton – kein zweiter „Trotzdem“-Button
            ContinueButton.Visibility = Visibility.Collapsed;

            if (_offerContinueOnIssues && hasIssues)
            {
                CancelButton.Visibility = Visibility.Visible;
                CancelButton.Content = "Abbrechen";
                CloseButton.Visibility = Visibility.Collapsed;
            }
            else
            {
                CancelButton.Visibility = Visibility.Collapsed;
                CloseButton.Visibility = Visibility.Visible;
                CloseButton.Content = "Schließen";
            }
        }

        private void ExcelDateBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Enter && e.Key != Key.Return)
                return;

            var box = sender as TextBox;
            if (box == null)
                return;

            CommitExcelDateFromBox(box);
            e.Handled = true;
            MoveFocus(new TraversalRequest(FocusNavigationDirection.Next));
        }

        private void ExcelDateBox_LostFocus(object sender, RoutedEventArgs e)
        {
            var box = sender as TextBox;
            if (box == null)
                return;

            CommitExcelDateFromBox(box);
        }

        private void CommitExcelDateFromBox(TextBox box)
        {
            var row = box.DataContext as OrderVerificationRowDisplay;
            if (row == null || row.SourceRow == null)
                return;

            var typed = box.Text;
            if (string.IsNullOrWhiteSpace(typed) || typed.Trim() == "—")
                return;

            var typedNormalized = NormalizeDate(typed);
            if (string.IsNullOrWhiteSpace(typedNormalized))
            {
                MessageBox.Show(this,
                    "Datum nicht erkannt: «" + typed + "»\r\n\r\nBitte z. B. 22.06.2026 eingeben.",
                    "Liefertermin",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                box.Text = row.ExcelDate;
                return;
            }

            ApplyDeliveryDateToRow(row, typedNormalized);
            box.Text = typedNormalized;
        }

        private void CopyPdfDate_Click(object sender, RoutedEventArgs e)
        {
            var button = sender as FrameworkElement;
            var row = button != null ? button.DataContext as OrderVerificationRowDisplay : null;
            if (row == null || row.SourceRow == null)
                return;

            if (string.IsNullOrWhiteSpace(row.PdfDate) || row.PdfDate == "—")
            {
                MessageBox.Show(this, "In der PDF gibt es keinen Termin für diese Zeile.", "Liefertermin",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var normalized = NormalizeDate(row.PdfDate);
            if (string.IsNullOrWhiteSpace(normalized))
            {
                MessageBox.Show(this, "PDF-Termin konnte nicht gelesen werden: «" + row.PdfDate + "»", "Liefertermin",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            ApplyDeliveryDateToRow(row, normalized);
        }

        private void DrawingNumber_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount < 2)
                return;

            e.Handled = true;
            var element = sender as FrameworkElement;
            var row = element != null ? element.DataContext as OrderVerificationRowDisplay : null;
            var drawing = row != null ? row.DrawingNumber : null;
            if (string.IsNullOrWhiteSpace(drawing) || drawing == "—")
                return;

            if (_openDrawingPdf != null)
            {
                _openDrawingPdf(drawing);
                return;
            }

            MessageBox.Show(this,
                "Kein Zeichnungs-PDF in der Ablage gefunden für:\r\n" + drawing,
                "Zeichnung öffnen",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }

        private void ApplyDeliveryDateToRow(OrderVerificationRowDisplay row, string normalizedDate)
        {
            if (row == null || row.SourceRow == null || string.IsNullOrWhiteSpace(normalizedDate))
                return;

            // Baugruppe / freies Einzelteil → LT-, nur BG-Unterzeichnung → FT-
            var isBgSub = OrderVerificationLookup.IsBgSubDrawing(row.SourceRow);
            var useFt = isBgSub && !row.IsAssembly;
            var termin = OrderVerificationLookup.FormatWatermarkTermin(normalizedDate, useFt);
            if (string.IsNullOrWhiteSpace(termin))
                termin = normalizedDate.Trim();

            row.SourceRow.DeliveryDate = termin;
            row.ExcelDate = termin;
            row.Description = row.IsAssembly
                ? "Baugruppe · LT"
                : (isBgSub ? "Einzelteil · FT" : "Einzelteil · LT");

            // Sofort in die Sortier-Excel-Zeilen schreiben (nicht erst beim Schließen)
            if (_persistDeliveryDate != null)
                _persistDeliveryDate(row.SourceRow.RowIndex, row.SourceRow.DrawingNumber, termin);

            // BG-Einzelzeichnungen ohne Bestell-PDF-Position bleiben „Skipped“ (kein Fehler)
            if (isBgSub && (string.IsNullOrWhiteSpace(row.PdfDate) || row.PdfDate == "—"))
            {
                row.HighlightExcelDate = false;
                row.HighlightPdfDate = false;
                row.Status = OrderVerificationMatchStatus.Skipped;
                row.StatusIcon = "○";
                row.StatusTitle = "BG-Einzelzeichnung";
                row.StatusDetail = "Nur in Excel (verbaut in Baugruppe)";
                DatesModified = true;
                UpdateCounts();
                UpdateActionButtons();
                return;
            }

            var dateMismatch = !DatesEqual(termin, row.PdfDate);
            row.HighlightExcelDate = dateMismatch;
            row.HighlightPdfDate = dateMismatch;

            if (dateMismatch || row.HighlightPdfAmount || row.HighlightExcelAmount)
            {
                row.Status = OrderVerificationMatchStatus.Deviation;
                row.StatusIcon = "⚠";
                row.StatusTitle = "Abweichung";
                row.StatusDetail = dateMismatch && (row.HighlightPdfAmount || row.HighlightExcelAmount)
                    ? "Liefertermin und Bestellmenge unterscheiden sich"
                    : (dateMismatch ? "Liefertermin unterscheidet sich" : "Bestellmenge unterscheidet sich");
            }
            else
            {
                row.Status = OrderVerificationMatchStatus.Match;
                row.StatusIcon = "✓";
                row.StatusTitle = "Übereinstimmung";
                row.StatusDetail = string.Empty;
            }

            DatesModified = true;
            UpdateCounts();
            UpdateActionButtons();
        }

        private static string NormalizeDate(string raw)
        {
            var normalized = OrderVerificationDateHelper.TryNormalizeExcelDate(raw);
            if (string.IsNullOrWhiteSpace(normalized))
                normalized = OrderVerificationDateHelper.NormalizeToEu(raw);
            return normalized ?? string.Empty;
        }

        private static bool DatesEqual(string excel, string pdf)
        {
            if (string.IsNullOrWhiteSpace(excel) || string.IsNullOrWhiteSpace(pdf) || pdf == "—")
                return true;
            return OrderVerificationDateHelper.AreEqual(excel, pdf);
        }

        private static string BuildPdfMetaText(string orderPdfPath)
        {
            if (string.IsNullOrWhiteSpace(orderPdfPath) || !File.Exists(orderPdfPath))
                return "Bestelldatum: —";

            var date = File.GetLastWriteTime(orderPdfPath);
            return "Bestelldatum: " + date.ToString("dd.MM.yyyy");
        }

        private void ExportReport_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new SaveFileDialog
            {
                Filter = "Textdatei (*.txt)|*.txt|Alle Dateien (*.*)|*.*",
                FileName = "DOK-V01_Pruefbericht.txt",
                Title = "Prüfbericht exportieren"
            };

            if (dialog.ShowDialog(this) != true)
                return;

            File.WriteAllText(dialog.FileName, _report.ToDetailedReportText(includeHeader: true, compactSuccess: false));
        }

        private void LoadExcel_Click(object sender, RoutedEventArgs e)
        {
            if (!string.IsNullOrWhiteSpace(_excelPath) && File.Exists(_excelPath))
            {
                try
                {
                    System.Diagnostics.Process.Start(_excelPath);
                }
                catch
                {
                    MessageBox.Show(this, "Excel-Datei konnte nicht geöffnet werden.", "DOK-V01", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
        }

        private void LoadPdf_Click(object sender, RoutedEventArgs e)
        {
            if (!string.IsNullOrWhiteSpace(_orderPdfPath) && File.Exists(_orderPdfPath))
            {
                try
                {
                    System.Diagnostics.Process.Start(_orderPdfPath);
                }
                catch
                {
                    MessageBox.Show(this, "PDF konnte nicht geöffnet werden.", "DOK-V01", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
        }

        private void Settings_Click(object sender, RoutedEventArgs e)
        {
            if (Owner != null)
            {
                var method = Owner.GetType().GetMethod("AiSettingsMenu_Click", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);
                method?.Invoke(Owner, new object[] { sender, e });
            }
        }

        private void Info_Click(object sender, RoutedEventArgs e)
        {
            var about = new AboutWindow { Owner = this };
            ThemeWindowService.Attach(about);
            about.ShowDialog();
        }

        private void AdoptDates_Click(object sender, RoutedEventArgs e)
        {
            CommitAllVisibleDateBoxes();

            var confirm = MessageBox.Show(this,
                "Die Excel-Spalte (links) wird mit den Terminen aus der Bestell-PDF (rechts) überschrieben.\r\n\r\n"
                + "Manuell geänderte Excel-Termine gehen dabei verloren.\r\n\r\n"
                + "Beim Sortieren zählt immer die Excel-Spalte links.\r\n\r\n"
                + "Wirklich PDF → Excel kopieren?",
                "PDF-Termine nach Excel",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (confirm != MessageBoxResult.Yes)
                return;

            var changed = 0;
            foreach (var displayRow in _displayRows)
            {
                if (displayRow.SourceRow == null)
                    continue;
                if (string.IsNullOrWhiteSpace(displayRow.PdfDate) || displayRow.PdfDate == "—")
                    continue;

                // Nur echte Abweichungen – nicht alle Positionen „blind“ überschreiben
                if (!displayRow.HighlightExcelDate && !displayRow.HighlightPdfDate)
                    continue;

                var normalized = NormalizeDate(displayRow.PdfDate);
                if (string.IsNullOrWhiteSpace(normalized))
                    continue;

                ApplyDeliveryDateToRow(displayRow, normalized);
                changed++;
            }

            if (changed == 0)
            {
                MessageBox.Show(this,
                    "Keine Termin-Abweichungen gefunden – nichts kopiert.\r\n\r\n"
                    + "Tipp: Termin links in der Excel-Spalte ändern. Danach „Prüfung abgeschlossen“ / Sortieren.",
                    "PDF → Excel",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            DatesModified = true;
            AdoptOrderPdfDates = true;

            MessageBox.Show(this,
                changed + " abweichende PDF-Termin(e) nach links (Excel) kopiert.\r\n\r\n"
                + "Diese Excel-Termine werden beim Sortieren als Wasserzeichen verwendet.",
                "PDF → Excel",
                MessageBoxButton.OK,
                MessageBoxImage.Information);

            // Dialog offen lassen – Nutzer soll die Werte noch sehen und bewusst weiterklicken
            UpdateActionButtons();
        }

        private void Continue_Click(object sender, RoutedEventArgs e)
        {
            CommitAllVisibleDateBoxes();
            ContinueDespiteIssues = true;
            DialogResult = true;
            Close();
        }

        private void Complete_Click(object sender, RoutedEventArgs e)
        {
            CommitAllVisibleDateBoxes();
            // Bei Abweichungen ohne Abbrechen = bewusst fortfahren (Sortierung)
            if (_offerContinueOnIssues)
                ContinueDespiteIssues = true;
            DialogResult = true;
            Close();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            ContinueDespiteIssues = false;
            DialogResult = false;
            Close();
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            CommitAllVisibleDateBoxes();
            DialogResult = true;
            Close();
        }
    }
}
