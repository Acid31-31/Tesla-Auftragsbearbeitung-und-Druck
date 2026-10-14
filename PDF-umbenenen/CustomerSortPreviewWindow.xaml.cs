using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DrawingBitmap = System.Drawing.Bitmap;
using MediaBrushes = System.Windows.Media.Brushes;
using MediaColor = System.Windows.Media.Color;
using MediaImage = System.Windows.Controls.Image;
using Microsoft.Win32;
using PDF_umbenenen.Models;
using PDF_umbenenen.Services;

namespace PDF_umbenenen
{
    public partial class CustomerSortPreviewWindow : Window
    {
        private readonly MainWindow _mainWindow;
        private readonly CustomerProfile _profile;
        private string _pdfFolder;
        private string _excelPath;
        private string _orderPdfPath;
        private string _watermarkTempFolder;
        private IReadOnlyList<CustomerSortPreviewLine> _lines = Array.Empty<CustomerSortPreviewLine>();
        private bool _loadingPreview;

        public CustomerSortPreviewWindow(MainWindow mainWindow, CustomerProfile profile, string pdfFolder, string excelPath)
            : this(mainWindow, profile, pdfFolder, excelPath, null)
        {
        }

        public CustomerSortPreviewWindow(
            MainWindow mainWindow,
            CustomerProfile profile,
            string pdfFolder,
            string excelPath,
            string orderPdfPath)
        {
            InitializeComponent();
            ThemeWindowService.Attach(this);
            _mainWindow = mainWindow;
            _profile = profile;
            _pdfFolder = pdfFolder ?? string.Empty;
            _excelPath = excelPath ?? string.Empty;
            _orderPdfPath = orderPdfPath ?? string.Empty;
            HeaderTextBlock.Text = "Sortier-Vorschau: " + (profile?.DisplayName ?? "Kunde");
            Loaded += async (_, __) => await RefreshPreviewAsync();
            Closed += (_, __) => DisposeWatermarkTempFolder();
        }

        private async Task RefreshPreviewAsync()
        {
            TryResolveExcelFromFolder();
            TryResolveOrderPdfFromFolder();

            var excelReady = !string.IsNullOrWhiteSpace(_excelPath) && File.Exists(_excelPath);
            var useExcel = _profile != null && _profile.ShouldSortFromExcel(excelReady);

            var pathsText = "PDF: " + (string.IsNullOrWhiteSpace(_pdfFolder) ? "(leer)" : _pdfFolder);
            if (_profile != null && !useExcel)
            {
                var orderLabel = OrderVerificationFileHelper.IsOrderTextExtension(_orderPdfPath)
                    ? "Text-Bestellung: "
                    : "Bestellung: ";
                pathsText += Environment.NewLine
                               + orderLabel
                               + (string.IsNullOrWhiteSpace(_orderPdfPath)
                                   ? "(leer – bitte wählen oder im Hauptfenster laden)"
                                   : _orderPdfPath);
            }
            else
            {
                pathsText += Environment.NewLine
                               + "Excel (nur für Zuordnung): "
                               + (string.IsNullOrWhiteSpace(_excelPath)
                                   ? "(leer – bitte wählen oder Auftrag laden)"
                                   : _excelPath);
            }

            PathsTextBlock.Text = pathsText;

            if (_mainWindow == null || _profile == null)
            {
                _lines = Array.Empty<CustomerSortPreviewLine>();
                BindList();
                PagesStack.Children.Clear();
                HintTextBlock.Text = "Keine Daten.";
                HintTextBlock.Visibility = Visibility.Visible;
                return;
            }

            _lines = _mainWindow.BuildCustomerSortPreview(_profile, _pdfFolder, _excelPath, _orderPdfPath);
            BindList();
            await LoadPdfPreviewAsync();
        }

        private void BindList()
        {
            var errorLine = _lines.FirstOrDefault(l => l.SortIndex == 0 && string.IsNullOrWhiteSpace(l.PdfFullPath));
            StatusTextBlock.Text = errorLine != null
                ? errorLine.Status
                : BuildSummaryText();

            var items = _lines
                .Where(l => !string.IsNullOrWhiteSpace(l.PdfFileName) || l.SortIndex > 0)
                .Select(l => new SortPreviewListItem(l))
                .ToList();

            SortListBox.ItemsSource = items;
            if (items.Count > 0)
                SortListBox.SelectedIndex = 0;
        }

        private string BuildSummaryText()
        {
            var sorted = _lines.Count(l => l.SortIndex > 0);
            var unmatched = _lines.Count(l => l.SortIndex == 0 && !string.IsNullOrWhiteSpace(l.PdfFullPath));
            if (sorted == 0 && unmatched == 0)
                return "Keine PDFs gefunden.";

            var wm = WatermarkSettingsStore.Load().WatermarksEnabled
                ? "mit Wasserzeichen"
                : "ohne Wasserzeichen (in den Einstellungen aus)";
            return sorted + " PDF(s) in Excel-Reihenfolge · "
                   + unmatched + " ohne Excel-Treffer · Vorschau " + wm + ".";
        }

        private async Task LoadPdfPreviewAsync()
        {
            if (_loadingPreview)
                return;

            _loadingPreview = true;
            PagesStack.Children.Clear();
            HintTextBlock.Text = "PDF-Vorschau mit Wasserzeichen wird geladen…";
            HintTextBlock.Visibility = Visibility.Visible;

            var previewLines = _lines
                .Where(l => l.SortIndex > 0 && !string.IsNullOrWhiteSpace(l.PdfFullPath) && File.Exists(l.PdfFullPath))
                .OrderBy(l => l.SortIndex)
                .ToList();

            // Wenn noch nichts gematcht wurde: trotzdem vorhandene PDFs zeigen (Analyse/Zuordnung prüfen).
            if (previewLines.Count == 0)
            {
                previewLines = _lines
                    .Where(l => !string.IsNullOrWhiteSpace(l.PdfFullPath) && File.Exists(l.PdfFullPath))
                    .OrderBy(l => l.SortIndex == 0 ? int.MaxValue : l.SortIndex)
                    .ThenBy(l => l.PdfFileName, StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }

            if (previewLines.Count == 0)
            {
                HintTextBlock.Text = string.IsNullOrWhiteSpace(StatusTextBlock.Text)
                    ? "Keine PDF-Vorschau – Excel und Zeichnungs-PDFs laden, dann «Aktualisieren»."
                    : StatusTextBlock.Text;
                _loadingPreview = false;
                return;
            }

            if (!PdfRasterLayoutService.IsAvailable)
                NativeDependencyService.TryRepairAndReload(out _);

            if (!PdfRasterLayoutService.IsAvailable)
            {
                HintTextBlock.Text = PdfRasterLayoutService.GetUnavailableUserMessage();
                _loadingPreview = false;
                return;
            }

            try
            {
                DisposeWatermarkTempFolder();
                EnsureWatermarkTempFolder();
                var index = 0;
                foreach (var line in previewLines)
                {
                    index++;
                    DrawingBitmap bitmap = null;
                    var sourcePath = line.PdfFullPath;
                    await Task.Run(() =>
                    {
                        try
                        {
                            var renderPath = _mainWindow != null
                                ? _mainWindow.TryCreateWatermarkedPreviewPdf(line, _profile, _watermarkTempFolder)
                                : sourcePath;
                            bitmap = PdfRasterLayoutService.RenderPageForPreview(renderPath, 1);
                        }
                        catch
                        {
                        }
                    }).ConfigureAwait(true);

                    PagesStack.Children.Add(bitmap == null
                        ? CreateErrorBlock(line)
                        : CreatePdfBlock(line, bitmap));

                    if (bitmap != null)
                        bitmap.Dispose();

                    HintTextBlock.Text = "Lade " + index + " / " + previewLines.Count + "…";
                }

                HintTextBlock.Visibility = Visibility.Collapsed;
                UpdatePageWidths();
            }
            catch (Exception ex)
            {
                HintTextBlock.Text = "Vorschau fehlgeschlagen: " + ex.Message;
                HintTextBlock.Visibility = Visibility.Visible;
            }
            finally
            {
                _loadingPreview = false;
            }
        }

        private FrameworkElement CreatePdfBlock(CustomerSortPreviewLine line, DrawingBitmap bitmap)
        {
            var source = ConvertBitmap(bitmap);
            var image = new MediaImage
            {
                Source = source,
                Stretch = Stretch.None,
                SnapsToDevicePixels = true
            };

            var caption = new TextBlock
            {
                Text = line.SortIndex + ". " + (string.IsNullOrWhiteSpace(line.ProposedFileName) ? line.PdfFileName : line.ProposedFileName),
                Foreground = MediaBrushes.Black,
                FontWeight = FontWeights.SemiBold,
                FontSize = 13,
                Margin = new Thickness(0, 0, 0, 6)
            };

            var sub = new TextBlock
            {
                Text = line.PdfSearchKey + " · " + line.Status,
                Foreground = MediaBrushes.Gray,
                FontSize = 11,
                Margin = new Thickness(0, 0, 0, 8)
            };

            var viewbox = new Viewbox
            {
                Stretch = Stretch.Uniform,
                StretchDirection = StretchDirection.DownOnly,
                HorizontalAlignment = HorizontalAlignment.Center,
                Child = image,
                Tag = line.SortIndex
            };

            var panel = new StackPanel { Margin = new Thickness(12, 16, 12, 8) };
            panel.Children.Add(caption);
            panel.Children.Add(sub);
            panel.Children.Add(viewbox);

            return new Border
            {
                Tag = line.SortIndex,
                Background = MediaBrushes.White,
                BorderBrush = new SolidColorBrush(MediaColor.FromRgb(220, 220, 220)),
                BorderThickness = new Thickness(0, 0, 0, 1),
                Child = panel
            };
        }

        private static FrameworkElement CreateErrorBlock(CustomerSortPreviewLine line)
        {
            return new Border
            {
                Tag = line.SortIndex,
                Background = MediaBrushes.White,
                MinHeight = 80,
                Padding = new Thickness(16),
                Child = new TextBlock
                {
                    Text = line.ProposedFileName + " – Seite konnte nicht geladen werden.",
                    Foreground = MediaBrushes.Gray,
                    TextWrapping = TextWrapping.Wrap
                }
            };
        }

        private void UpdatePageWidths()
        {
            var width = Math.Max(320, PagesScrollViewer.ActualWidth - 48);
            foreach (var child in PagesStack.Children)
            {
                if (child is Border border && border.Child is StackPanel sp)
                {
                    foreach (var item in sp.Children)
                    {
                        if (item is Viewbox vb)
                            vb.MaxWidth = width;
                    }
                }
            }
        }

        private void SortListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!(SortListBox.SelectedItem is SortPreviewListItem item) || item.Line.SortIndex <= 0)
                return;

            var target = PagesStack.Children
                .OfType<FrameworkElement>()
                .FirstOrDefault(c => c.Tag is int tag && tag == item.Line.SortIndex);

            target?.BringIntoView();
        }

        private void EnsureWatermarkTempFolder()
        {
            if (!string.IsNullOrWhiteSpace(_watermarkTempFolder) && Directory.Exists(_watermarkTempFolder))
                return;

            _watermarkTempFolder = Path.Combine(
                Path.GetTempPath(),
                "DOK-V01",
                "SortPreviewWm",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_watermarkTempFolder);
        }

        private void DisposeWatermarkTempFolder()
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(_watermarkTempFolder) && Directory.Exists(_watermarkTempFolder))
                    Directory.Delete(_watermarkTempFolder, true);
            }
            catch
            {
            }

            _watermarkTempFolder = null;
        }

        private void TryResolveExcelFromFolder()
        {
            if (!string.IsNullOrWhiteSpace(_excelPath) && File.Exists(_excelPath))
                return;

            if (string.IsNullOrWhiteSpace(_pdfFolder) || !Directory.Exists(_pdfFolder))
                return;

            var resolved = ExcelDeliveryDateMergeService.ResolvePrimaryExcelPath(_pdfFolder);
            if (!string.IsNullOrWhiteSpace(resolved) && File.Exists(resolved))
                _excelPath = resolved;
        }

        private void TryResolveOrderPdfFromFolder()
        {
            if (!string.IsNullOrWhiteSpace(_orderPdfPath) && File.Exists(_orderPdfPath))
                return;

            if (_mainWindow != null)
            {
                var fromMain = _mainWindow.GetCurrentOrderPdfPathForPreview(_pdfFolder);
                if (!string.IsNullOrWhiteSpace(fromMain) && File.Exists(fromMain))
                {
                    _orderPdfPath = fromMain;
                    return;
                }
            }

            if (string.IsNullOrWhiteSpace(_pdfFolder) || !Directory.Exists(_pdfFolder))
                return;

            var storePath = PathStore.Load().OrderVerificationPdfPath;
            if (!string.IsNullOrWhiteSpace(storePath) && File.Exists(storePath))
            {
                _orderPdfPath = storePath;
                return;
            }

            var found = OrderVerificationFileHelper.FindOrderListInFolder(_pdfFolder);
            if (!string.IsNullOrWhiteSpace(found))
                _orderPdfPath = found;
        }

        private async void Refresh_Click(object sender, RoutedEventArgs e)
        {
            if (_mainWindow != null)
            {
                var paths = PathStore.Load();
                if (!string.IsNullOrWhiteSpace(paths.PdfFolder))
                    _pdfFolder = paths.PdfFolder;

                var fromMain = _mainWindow.GetCurrentExcelPathForPreview();
                if (!string.IsNullOrWhiteSpace(fromMain))
                    _excelPath = fromMain;

                var orderFromMain = _mainWindow.GetCurrentOrderPdfPathForPreview(_pdfFolder);
                if (!string.IsNullOrWhiteSpace(orderFromMain))
                    _orderPdfPath = orderFromMain;
            }

            await RefreshPreviewAsync();
        }

        private async void UseWorkspace_Click(object sender, RoutedEventArgs e)
        {
            var paths = PathStore.Load();
            if (!string.IsNullOrWhiteSpace(paths.PdfFolder))
                _pdfFolder = paths.PdfFolder;

            if (_mainWindow != null)
            {
                var fromMain = _mainWindow.GetCurrentExcelPathForPreview();
                if (!string.IsNullOrWhiteSpace(fromMain))
                    _excelPath = fromMain;

                var orderFromMain = _mainWindow.GetCurrentOrderPdfPathForPreview(_pdfFolder);
                if (!string.IsNullOrWhiteSpace(orderFromMain))
                    _orderPdfPath = orderFromMain;
            }

            TryResolveExcelFromFolder();
            TryResolveOrderPdfFromFolder();
            await RefreshPreviewAsync();
        }

        private async void PickPdfFolder_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new System.Windows.Forms.FolderBrowserDialog
            {
                Description = "PDF-Ablage für Sortier-Vorschau",
                SelectedPath = Directory.Exists(_pdfFolder) ? _pdfFolder : PathStore.GetOrCreateDefaultWorkspaceFolder()
            };

            if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                _pdfFolder = dlg.SelectedPath;
                TryResolveExcelFromFolder();
                await RefreshPreviewAsync();
            }
        }

        private async void PickExcel_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog
            {
                Filter = "Excel|*.xlsx;*.xls|Alle|*.*",
                InitialDirectory = Directory.Exists(_pdfFolder)
                    ? _pdfFolder
                    : PathStore.GetOrCreateDefaultWorkspaceFolder()
            };

            if (dlg.ShowDialog() == true)
            {
                _excelPath = dlg.FileName;
                await RefreshPreviewAsync();
            }
        }

        private async void PickOrderPdf_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog
            {
                Filter = "PDF|*.pdf|Alle|*.*",
                InitialDirectory = Directory.Exists(_pdfFolder)
                    ? _pdfFolder
                    : PathStore.GetOrCreateDefaultWorkspaceFolder()
            };

            if (dlg.ShowDialog() == true)
            {
                _orderPdfPath = dlg.FileName;
                await RefreshPreviewAsync();
            }
        }

        private static BitmapSource ConvertBitmap(DrawingBitmap bitmap)
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

        private sealed class SortPreviewListItem
        {
            public SortPreviewListItem(CustomerSortPreviewLine line)
            {
                Line = line;
            }

            public CustomerSortPreviewLine Line { get; }

            public string ListTitle =>
                (Line.SortIndex > 0 ? Line.SortIndex + ". " : "– ")
                + (string.IsNullOrWhiteSpace(Line.ProposedFileName) ? Line.PdfFileName : Line.ProposedFileName);

            public string ListSubtitle =>
                string.IsNullOrWhiteSpace(Line.PdfSearchKey)
                    ? Line.Status
                    : Line.PdfSearchKey + " · " + Line.Status;
        }
    }
}
