using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
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
        private readonly Func<string, string> _resolveDrawingPdf;
        private readonly bool _offerContinueOnIssues;
        private readonly bool _pdfOrderListMode;
        private List<OrderVerificationRowDisplay> _displayRows;
        private bool _syncingPaneScroll;
        private OrderVerificationRowDisplay _dateEditRow;
        private bool _suppressCalendarChange;
        private readonly DispatcherTimer _hoverHideTimer;
        private readonly Dictionary<string, BitmapSource> _hoverPreviewCache = new Dictionary<string, BitmapSource>(StringComparer.OrdinalIgnoreCase);
        private int _hoverGeneration;

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
            Action<string> openDrawingPdf,
            Func<string, string> resolveDrawingPdf = null,
            bool pdfOrderListMode = false)
        {
            InitializeComponent();
            _report = report;
            _excelPath = excelPath;
            _orderPdfPath = orderPdfPath;
            _excelRows = excelRows ?? new List<OrderExcelRow>();
            _persistDeliveryDate = persistDeliveryDate;
            _openDrawingPdf = openDrawingPdf;
            _resolveDrawingPdf = resolveDrawingPdf;
            _offerContinueOnIssues = offerContinueOnIssues;
            _pdfOrderListMode = pdfOrderListMode;
            _hoverHideTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(160) };
            _hoverHideTimer.Tick += HoverHideTimer_Tick;

            ThemeWindowService.MaximizeToWorkArea(this);

            ExcelFileTextBlock.Text = "Excel: " + (excelFileName ?? "—");
            if (_pdfOrderListMode && LoadExcelButton != null)
                LoadExcelButton.Visibility = Visibility.Collapsed;
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
            HideDrawingHover();
            if (_hoverHideTimer != null)
            {
                _hoverHideTimer.Stop();
                _hoverHideTimer.Tick -= HoverHideTimer_Tick;
            }
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
            Action<string> openDrawingPdf = null,
            Func<string, string> resolveDrawingPdf = null,
            bool pdfOrderListMode = false)
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
                openDrawingPdf,
                resolveDrawingPdf,
                pdfOrderListMode)
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

            Keyboard.ClearFocus();

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

        private void ExcelDateCell_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            var element = sender as FrameworkElement;
            var row = element != null ? element.DataContext as OrderVerificationRowDisplay : null;
            if (row == null || row.SourceRow == null || ExcelDateCalendarPopup == null)
                return;

            _dateEditRow = row;
            _suppressCalendarChange = true;
            try
            {
                ExcelDateCalendar.SelectedDate = row.ExcelSelectedDate;
            }
            finally
            {
                _suppressCalendarChange = false;
            }

            ExcelDateCalendarPopup.PlacementTarget = element;
            ExcelDateCalendarPopup.IsOpen = true;
            e.Handled = true;
        }

        private void ExcelDateCalendar_SelectedDatesChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_suppressCalendarChange || _dateEditRow == null || ExcelDateCalendar.SelectedDate == null)
                return;

            var eu = ExcelDateCalendar.SelectedDate.Value.ToString(
                "dd.MM.yyyy",
                System.Globalization.CultureInfo.GetCultureInfo("de-DE"));
            ApplyDeliveryDateToRow(_dateEditRow, eu);
            ExcelDateCalendarPopup.IsOpen = false;
            _dateEditRow = null;
        }

        private void PaneScroll_ScrollChanged(object sender, ScrollChangedEventArgs e)
        {
            if (e.VerticalChange != 0)
                HideDrawingHover();

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

            // Abweichungen aufschlüsseln
            var dateOnly = _displayRows.Count(r => r.Status == OrderVerificationMatchStatus.Deviation
                && r.HighlightPdfDate && !r.HighlightPdfAmount);
            var amountOnly = _displayRows.Count(r => r.Status == OrderVerificationMatchStatus.Deviation
                && !r.HighlightPdfDate && r.HighlightPdfAmount);
            var both = _displayRows.Count(r => r.Status == OrderVerificationMatchStatus.Deviation
                && r.HighlightPdfDate && r.HighlightPdfAmount);

            // Fehler aufschlüsseln
            var missingInPdf = _displayRows.Count(r => r.Status == OrderVerificationMatchStatus.MissingInPdf);
            var missingInExcel = _displayRows.Count(r => r.Status == OrderVerificationMatchStatus.MissingInExcel);

            MatchCountTextBlock.Text = matchCount + " Übereinstimmung" + (matchCount == 1 ? string.Empty : "en");

            // Abweichungen mit Details
            var devParts = new List<string>();
            if (dateOnly > 0) devParts.Add(dateOnly + "× Liefertermin");
            if (amountOnly > 0) devParts.Add(amountOnly + "× Menge");
            if (both > 0) devParts.Add(both + "× Termin+Menge");
            DeviationCountTextBlock.Text = deviationCount + " Abweichung" + (deviationCount == 1 ? string.Empty : "en")
                + (devParts.Count > 0 ? " (" + string.Join(", ", devParts) + ")" : string.Empty);

            // Fehler mit Details
            var errParts = new List<string>();
            if (missingInPdf > 0) errParts.Add(missingInPdf + "× fehlt in der Bestellliste (Auftragsbestätigung)");
            if (missingInExcel > 0)
            {
                errParts.Add(missingInExcel + "× nur in PDF, nicht in Excel");
            }
            ErrorCountTextBlock.Text = errorCount + " Fehler"
                + (errParts.Count > 0 ? " (" + string.Join(", ", errParts) + ")" : string.Empty);
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

            // Hauptbutton – klare Beschriftung je nach Zustand
            CompleteButton.IsEnabled = true;
            if (DatesModified && !hasDateMismatch)
            {
                CompleteButton.Content = "✓  Geänderte Termine übernehmen";
                CompleteButton.ToolTip = "Ihre manuell geänderten Liefertermine werden gespeichert und die Sortierung startet.";
                CompleteButton.Style = (Style)FindResource("ProtocolPrimaryBtn");
            }
            else if (hasDateMismatch)
            {
                CompleteButton.Content = "Termine aus Excel übernehmen";
                CompleteButton.ToolTip = "Die Termine aus der Excel-Spalte (links) behalten und die Sortierung starten.";
                CompleteButton.Style = (Style)FindResource("ProtocolOutlineBtn");
            }
            else if (hasIssues)
            {
                CompleteButton.Content = "Ignorieren und übernehmen";
                CompleteButton.ToolTip = "Abweichungen ignorieren und die Sortierung mit den aktuellen Excel-Daten starten.";
                CompleteButton.Style = (Style)FindResource("ProtocolOutlineBtn");
            }
            else
            {
                CompleteButton.Content = "✓  Alles stimmt – sortieren";
                CompleteButton.ToolTip = "Excel und Bestell-PDF stimmen überein. Sortierung starten.";
                CompleteButton.Style = (Style)FindResource("ProtocolPrimaryBtn");
            }

            // PDF-Termine übernehmen (bei Termin-Abweichungen)
            AdoptDatesButton.Visibility = hasDateMismatch ? Visibility.Visible : Visibility.Collapsed;
            AdoptDatesButton.Content = "Termine aus PDF übernehmen";
            AdoptDatesButton.ToolTip = "Abweichende Liefertermine aus der Bestell-PDF in Excel übernehmen und sortieren.";

            // Dritte Option: Abweichungen ignorieren (sichtbar bei Problemen, wenn nicht schon der Hauptbutton)
            if (hasIssues && hasDateMismatch)
            {
                ContinueButton.Visibility = Visibility.Visible;
                ContinueButton.Content = "Ignorieren und übernehmen";
                ContinueButton.ToolTip = "Abweichungen ignorieren und mit den aktuellen Excel-Terminen sortieren.";
            }
            else
            {
                ContinueButton.Visibility = Visibility.Collapsed;
            }

            if (_offerContinueOnIssues && hasIssues)
            {
                CancelButton.Visibility = Visibility.Visible;
                CancelButton.Content = "Abbrechen – nicht sortieren";
                CancelButton.ToolTip = "Sortierung abbrechen. Sie können die Excel-Datei\nkorrigieren und es erneut versuchen.";
                CloseButton.Visibility = Visibility.Collapsed;
            }
            else
            {
                CancelButton.Visibility = Visibility.Collapsed;
                CloseButton.Visibility = Visibility.Visible;
                CloseButton.Content = "Schließen";
                CloseButton.ToolTip = "Prüfprotokoll schließen ohne zu sortieren. Geänderte Termine bleiben gespeichert.";
            }
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

        private async void DrawingNumber_MouseEnter(object sender, MouseEventArgs e)
        {
            _hoverHideTimer.Stop();
            var element = sender as FrameworkElement;
            var drawing = GetDrawingNumber(element);
            if (string.IsNullOrWhiteSpace(drawing) || drawing == "—")
                return;

            var generation = ++_hoverGeneration;
            await ShowDrawingHoverAsync(element, drawing, generation).ConfigureAwait(true);
        }

        private void DrawingNumber_MouseLeave(object sender, MouseEventArgs e)
        {
            _hoverHideTimer.Stop();
            _hoverHideTimer.Start();
        }

        private void HoverHideTimer_Tick(object sender, EventArgs e)
        {
            _hoverHideTimer.Stop();
            HideDrawingHover();
        }

        private void DrawingNumber_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount < 2)
                return;

            e.Handled = true;
            HideDrawingHover();
            var element = sender as FrameworkElement;
            var drawing = GetDrawingNumber(element);
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

        private static string GetDrawingNumber(FrameworkElement element)
        {
            var row = element != null ? element.DataContext as OrderVerificationRowDisplay : null;
            return row != null ? row.DrawingNumber : null;
        }

        private async Task ShowDrawingHoverAsync(FrameworkElement target, string drawing, int generation)
        {
            if (DrawingHoverPopup == null || generation != _hoverGeneration)
                return;

            PositionDrawingHoverPopup(target);
            DrawingHoverTitle.Text = drawing;

            BitmapSource cached;
            if (_hoverPreviewCache.TryGetValue(drawing, out cached) && cached != null)
            {
                DrawingHoverImage.Source = cached;
                DrawingHoverImage.Visibility = Visibility.Visible;
                DrawingHoverStatus.Visibility = Visibility.Collapsed;
                DrawingHoverPopup.IsOpen = true;
                return;
            }

            DrawingHoverImage.Source = null;
            DrawingHoverImage.Visibility = Visibility.Collapsed;
            DrawingHoverStatus.Text = "Lade Vorschau…";
            DrawingHoverStatus.Visibility = Visibility.Visible;
            DrawingHoverPopup.IsOpen = true;

            string error = null;
            System.Drawing.Bitmap rendered = null;

            await Task.Run(() =>
            {
                try
                {
                    var pdfPath = _resolveDrawingPdf != null ? _resolveDrawingPdf(drawing) : null;
                    if (string.IsNullOrWhiteSpace(pdfPath) || !File.Exists(pdfPath))
                    {
                        error = "Keine Zeichnung in der Ablage gefunden.";
                        return;
                    }

                    if (!PdfRasterLayoutService.IsAvailable)
                    {
                        error = PdfRasterLayoutService.GetUnavailableUserMessage();
                        return;
                    }

                    rendered = PdfRasterLayoutService.RenderPageForPreview(pdfPath, 1);
                    if (rendered == null)
                        error = "Vorschau nicht verfügbar.";
                }
                catch (Exception ex)
                {
                    error = "Vorschau fehlgeschlagen: " + ex.Message;
                }
            }).ConfigureAwait(true);

            if (generation != _hoverGeneration)
            {
                if (rendered != null)
                    rendered.Dispose();
                return;
            }

            BitmapSource preview = null;
            try
            {
                preview = ConvertBitmap(rendered);
            }
            finally
            {
                if (rendered != null)
                    rendered.Dispose();
            }

            if (preview != null)
            {
                _hoverPreviewCache[drawing] = preview;
                DrawingHoverImage.Source = preview;
                DrawingHoverImage.Visibility = Visibility.Visible;
                DrawingHoverStatus.Visibility = Visibility.Collapsed;
            }
            else
            {
                DrawingHoverImage.Source = null;
                DrawingHoverImage.Visibility = Visibility.Collapsed;
                DrawingHoverStatus.Text = error ?? "Vorschau nicht verfügbar.";
                DrawingHoverStatus.Visibility = Visibility.Visible;
            }

            DrawingHoverPopup.IsOpen = true;
        }

        private void PositionDrawingHoverPopup(FrameworkElement target)
        {
            DrawingHoverPopup.PlacementTarget = target;
            var fromPdfPane = target != null && string.Equals(Convert.ToString(target.Tag), "pdf", StringComparison.OrdinalIgnoreCase);
            DrawingHoverPopup.Placement = fromPdfPane ? PlacementMode.Left : PlacementMode.Right;
            DrawingHoverPopup.HorizontalOffset = fromPdfPane ? -10 : 10;
            DrawingHoverPopup.VerticalOffset = -8;
        }

        private void HideDrawingHover()
        {
            _hoverGeneration++;
            if (DrawingHoverPopup != null)
                DrawingHoverPopup.IsOpen = false;
            if (DrawingHoverImage != null)
                DrawingHoverImage.Source = null;
        }

        private static BitmapSource ConvertBitmap(System.Drawing.Bitmap bitmap)
        {
            if (bitmap == null)
                return null;

            var handle = bitmap.GetHbitmap();
            try
            {
                var source = Imaging.CreateBitmapSourceFromHBitmap(
                    handle,
                    IntPtr.Zero,
                    Int32Rect.Empty,
                    BitmapSizeOptions.FromEmptyOptions());
                if (source.CanFreeze)
                    source.Freeze();
                return source;
            }
            finally
            {
                DeleteObject(handle);
            }
        }

        [DllImport("gdi32.dll")]
        private static extern bool DeleteObject(IntPtr hObject);

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

        private void GeoSearch_Click(object sender, RoutedEventArgs e)
        {
            var settings = GeoSettingsStore.Load();
            if (settings.SearchFolders == null || settings.SearchFolders.Count == 0)
            {
                var ask = MessageBox.Show(
                    this,
                    "Noch keine GEO-Suchordner eingestellt.\n\nJetzt Ordner konfigurieren?",
                    "GEO suchen",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);
                if (ask != MessageBoxResult.Yes)
                    return;

                var geoSettings = new GeoSettingsWindow { Owner = this };
                ThemeWindowService.Attach(geoSettings);
                if (geoSettings.ShowDialog() != true)
                    return;

                settings = GeoSettingsStore.Load();
                if (settings.SearchFolders == null || settings.SearchFolders.Count == 0)
                    return;
            }

            var orderNumber = ResolveOrderNumberForGeo();
            var pdfFolder = string.Empty;
            try
            {
                var owner = Owner as MainWindow;
                var box = owner?.FindName("PdfFolderTextBox") as System.Windows.Controls.TextBox;
                pdfFolder = box?.Text?.Trim() ?? string.Empty;
            }
            catch
            {
            }

            var win = new GeoCollectWindow(_excelRows, orderNumber, pdfFolder) { Owner = this };
            ThemeWindowService.Attach(win);
            win.Show();
        }

        private string ResolveOrderNumberForGeo()
        {
            try
            {
                var owner = Owner as MainWindow;
                if (owner != null)
                {
                    var box = owner.FindName("OrderNumberTextBox") as System.Windows.Controls.TextBox;
                    var text = box?.Text?.Trim();
                    if (!string.IsNullOrWhiteSpace(text))
                        return text;
                }
            }
            catch
            {
            }

            if (!string.IsNullOrWhiteSpace(_excelPath))
            {
                var name = Path.GetFileNameWithoutExtension(_excelPath);
                if (!string.IsNullOrWhiteSpace(name))
                    return name;
            }

            if (!string.IsNullOrWhiteSpace(_orderPdfPath))
            {
                var name = Path.GetFileNameWithoutExtension(_orderPdfPath);
                if (!string.IsNullOrWhiteSpace(name))
                    return name;
            }

            return "Auftrag";
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
            }

            DatesModified = true;
            AdoptOrderPdfDates = true;
            ContinueDespiteIssues = true;
            DialogResult = true;
            Close();
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
            DialogResult = false;
            Close();
        }
    }
}
