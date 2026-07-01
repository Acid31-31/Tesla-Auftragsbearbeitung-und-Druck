using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using PDF_umbenenen.Models;
using PDF_umbenenen.Services;

namespace PDF_umbenenen
{
    public partial class PdfMergedPreviewWindow : Window
    {
        private readonly string _pdfFolder;
        private readonly List<string> _orderedFiles;
        private readonly WatermarkSettings _settings;

        private string _mergedPdfPath;
        private List<MergedPdfPageEntry> _pageMap = new List<MergedPdfPageEntry>();
        private int _currentMergedPage = 1;
        private int _totalPages;

        private bool _editMode;
        private bool _loadingPages;
        private WatermarkEditorItem _editItem;
        private List<WatermarkTextPlacement> _editPlacements = new List<WatermarkTextPlacement>();
        private readonly Dictionary<Border, WatermarkTextPlacement> _overlayMap =
            new Dictionary<Border, WatermarkTextPlacement>();

        private double _scale = 1.0;
        private bool _suppressMaterialTextChange;
        private bool _editDirty;

        private Point _dragStart;
        private bool _isDragging;
        private Border _dragBorder;

        public string MergedPdfPath => _mergedPdfPath;

        public PdfMergedPreviewWindow(
            string mergedPdfPath,
            IList<string> orderedSourceFiles,
            string pdfFolder,
            List<MergedPdfPageEntry> pageMap = null)
        {
            InitializeComponent();
            ThemeWindowService.Attach(this);

            _mergedPdfPath = mergedPdfPath;
            _orderedFiles = (orderedSourceFiles ?? Array.Empty<string>()).Where(File.Exists).ToList();
            _pdfFolder = pdfFolder ?? string.Empty;
            _settings = WatermarkSettingsStore.Load();
            _pageMap = pageMap?.Count > 0
                ? pageMap.ToList()
                : MergedPdfPageMapService.Build(_orderedFiles);

            if (string.IsNullOrWhiteSpace(_mergedPdfPath) || !File.Exists(_mergedPdfPath))
            {
                MessageBox.Show(this, "PDF-Vorschau nicht gefunden.", "Vorschau", MessageBoxButton.OK, MessageBoxImage.Warning);
                Loaded += (_, __) => Close();
                return;
            }

            if (_pageMap.Count == 0)
                _pageMap = MergedPdfPageMapService.Build(_orderedFiles);

            _pageMap = NormalizePageMap(_pageMap);

            _totalPages = Math.Max(1, _pageMap.Count > 0
                ? _pageMap.Count
                : PdfWatermarkService.GetPageCount(_mergedPdfPath));
            Closed += (_, __) =>
            {
                PagesStack.Children.Clear();
                PdfRasterLayoutService.ClearCache();
            };
            Loaded += (_, __) => _ = LoadAllPagesAsync();
        }

        private static List<MergedPdfPageEntry> NormalizePageMap(IEnumerable<MergedPdfPageEntry> map)
        {
            return (map ?? Enumerable.Empty<MergedPdfPageEntry>())
                .Select(entry => new MergedPdfPageEntry
                {
                    MergedPageNumber = entry.MergedPageNumber,
                    SourcePageNumber = entry.SourcePageNumber,
                    SourcePdfPath = ResolveWatermarkedPdfPath(entry.SourcePdfPath)
                })
                .ToList();
        }

        private static string ResolveWatermarkedPdfPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return path;

            if (!path.EndsWith(WatermarkEditorService.PreWatermarkSuffix, StringComparison.OrdinalIgnoreCase))
                return path;

            var watermarkedPath = path.Substring(0, path.Length - WatermarkEditorService.PreWatermarkSuffix.Length) + ".pdf";
            return File.Exists(watermarkedPath) ? watermarkedPath : path;
        }

        private MergedPdfPageEntry GetCurrentEntry()
        {
            return _pageMap.FirstOrDefault(entry => entry.MergedPageNumber == _currentMergedPage);
        }

        private async Task LoadAllPagesAsync(int? scrollToPage = null)
        {
            if (_loadingPages || _editMode)
                return;

            _loadingPages = true;
            PdfRasterLayoutService.ClearCache();
            HintTextBlock.Text = "Lade Vorschau…";
            PagesStack.Children.Clear();
            CorrectButton.IsEnabled = false;

            var pdfPath = _mergedPdfPath;
            var entries = GetPreviewEntries();
            var total = entries.Count;
            var renderedCount = 0;

            if (!PdfRasterLayoutService.IsAvailable)
            {
                HintTextBlock.Text = "Interne PDF-Anzeige nicht verfügbar.";
                MessageBox.Show(
                    this,
                    "Die eingebaute Vorschau kann nicht gerendert werden (Docnet/pdfium fehlt).\n\n" +
                    "Bitte install-docnet.ps1 ausführen.",
                    "Vorschau",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                CorrectButton.IsEnabled = true;
                _loadingPages = false;
                return;
            }

            try
            {
                foreach (var entry in entries)
                {
                    System.Drawing.Bitmap bitmap = null;
                    await Task.Run(() =>
                    {
                        bitmap = RenderPreviewPage(entry);
                    }).ConfigureAwait(true);

                    if (_editMode || !string.Equals(pdfPath, _mergedPdfPath, StringComparison.OrdinalIgnoreCase))
                    {
                        bitmap?.Dispose();
                        return;
                    }

                    FrameworkElement container;
                    if (bitmap == null)
                    {
                        container = CreateErrorPageContainer(entry.MergedPageNumber);
                    }
                    else
                    {
                        try
                        {
                            container = CreatePageContainer(entry.MergedPageNumber, bitmap);
                            renderedCount++;
                        }
                        finally
                        {
                            bitmap.Dispose();
                        }
                    }

                    PagesStack.Children.Add(container);
                    HintTextBlock.Text = string.Format(
                        "Lade Seite {0} / {1}…",
                        entry.MergedPageNumber,
                        total);

                    if (entry.MergedPageNumber == 1)
                        PagesStack.UpdateLayout();
                }

                if (renderedCount == 0)
                {
                    HintTextBlock.Text = "Keine Seite konnte gerendert werden.";
                    MessageBox.Show(
                        this,
                        "Die Vorschau konnte keine Seiten anzeigen.",
                        "Vorschau",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                }
                else
                {
                    HintTextBlock.Text = "Durchscrollen wie gewohnt. Korrektur nur bei Bedarf.";
                }

                CorrectButton.IsEnabled = true;
                PagesStack.UpdateLayout();
                UpdatePageFitWidth();
                if (scrollToPage.HasValue)
                    ScrollToPage(scrollToPage.Value);
                else
                {
                    AllPagesScroll.ScrollToVerticalOffset(0);
                    _currentMergedPage = 1;
                    UpdatePageInfo();
                }
            }
            catch (Exception ex)
            {
                HintTextBlock.Text = "Vorschau konnte nicht geladen werden.";
                MessageBox.Show(this, ex.Message, "Vorschau", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            finally
            {
                _loadingPages = false;
            }
        }

        private async Task RefreshPagesAfterSaveAsync(int scrollToPage, string editedSourcePath)
        {
            if (string.IsNullOrWhiteSpace(editedSourcePath))
            {
                await LoadAllPagesAsync(scrollToPage);
                return;
            }

            var normalizedEditedPath = ResolveWatermarkedPdfPath(editedSourcePath);
            var affectedEntries = _pageMap
                .Where(entry => string.Equals(
                    ResolveWatermarkedPdfPath(entry.SourcePdfPath),
                    normalizedEditedPath,
                    StringComparison.OrdinalIgnoreCase))
                .OrderBy(entry => entry.MergedPageNumber)
                .ToList();

            if (affectedEntries.Count == 0 || PagesStack.Children.Count == 0)
            {
                await LoadAllPagesAsync(scrollToPage);
                return;
            }

            HintTextBlock.Text = "Aktualisiere Vorschau…";
            CorrectButton.IsEnabled = false;

            try
            {
                foreach (var entry in affectedEntries)
                    await ReplacePageContainerAsync(entry);

                PagesStack.UpdateLayout();
                ScrollToPage(scrollToPage);
                HintTextBlock.Text = string.Format("✓ Seite {0} gespeichert — KI merkt sich die Position.", scrollToPage);
            }
            finally
            {
                CorrectButton.IsEnabled = true;
            }
        }

        private async Task ReplacePageContainerAsync(MergedPdfPageEntry entry)
        {
            System.Drawing.Bitmap bitmap = null;
            await Task.Run(() =>
            {
                bitmap = RenderPreviewPage(entry);
            }).ConfigureAwait(true);

            FrameworkElement newContainer;
            if (bitmap == null)
            {
                newContainer = CreateErrorPageContainer(entry.MergedPageNumber);
            }
            else
            {
                try
                {
                    newContainer = CreatePageContainer(entry.MergedPageNumber, bitmap);
                }
                finally
                {
                    bitmap.Dispose();
                }
            }

            for (var i = 0; i < PagesStack.Children.Count; i++)
            {
                if (PagesStack.Children[i] is FrameworkElement element &&
                    element.Tag is int pageNumber &&
                    pageNumber == entry.MergedPageNumber)
                {
                    PagesStack.Children.RemoveAt(i);
                    PagesStack.Children.Insert(i, newContainer);
                    return;
                }
            }
        }

        private void ScrollToPage(int pageNumber)
        {
            if (pageNumber < 1)
                return;

            _currentMergedPage = pageNumber;
            UpdatePageInfo();

            FrameworkElement target = null;
            foreach (UIElement child in PagesStack.Children)
            {
                if (child is FrameworkElement element &&
                    element.Tag is int tag &&
                    tag == pageNumber)
                {
                    target = element;
                    break;
                }
            }

            if (target == null)
                return;

            PagesStack.UpdateLayout();
            AllPagesScroll.UpdateLayout();
            try
            {
                target.BringIntoView();
            }
            catch
            {
                var offset = target.TransformToAncestor(PagesStack).Transform(new Point(0, 0)).Y;
                AllPagesScroll.ScrollToVerticalOffset(Math.Max(0, offset - 24));
            }
        }

        private List<MergedPdfPageEntry> GetPreviewEntries()
        {
            if (_pageMap.Count > 0)
                return _pageMap.OrderBy(entry => entry.MergedPageNumber).ToList();

            var entries = new List<MergedPdfPageEntry>();
            for (var page = 1; page <= _totalPages; page++)
            {
                entries.Add(new MergedPdfPageEntry
                {
                    MergedPageNumber = page,
                    SourcePdfPath = _mergedPdfPath,
                    SourcePageNumber = page
                });
            }

            return entries;
        }

        private System.Drawing.Bitmap RenderPreviewPage(MergedPdfPageEntry entry)
        {
            if (entry == null)
                return null;

            if (!string.IsNullOrWhiteSpace(_mergedPdfPath) && File.Exists(_mergedPdfPath))
                return PdfRasterLayoutService.RenderPageForPreview(_mergedPdfPath, entry.MergedPageNumber);

            var sourcePath = ResolveWatermarkedPdfPath(entry.SourcePdfPath);
            var pageNumber = entry.SourcePageNumber;
            if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath))
                return null;

            return PdfRasterLayoutService.RenderPageForPreview(sourcePath, pageNumber);
        }

        private FrameworkElement CreatePageContainer(int pageNumber, System.Drawing.Bitmap bitmap)
        {
            var source = ConvertBitmap(bitmap);
            if (source.CanFreeze)
                source.Freeze();

            var image = new Image
            {
                Source = source,
                Stretch = Stretch.None,
                Width = source.Width,
                Height = source.Height,
                SnapsToDevicePixels = true
            };

            var viewbox = new Viewbox
            {
                Stretch = Stretch.Uniform,
                StretchDirection = StretchDirection.DownOnly,
                HorizontalAlignment = HorizontalAlignment.Center,
                Child = image
            };

            return new Border
            {
                Tag = pageNumber,
                Background = Brushes.White,
                Margin = new Thickness(0, 2, 0, 2),
                Child = viewbox
            };
        }

        private static FrameworkElement CreateErrorPageContainer(int pageNumber)
        {
            return new Border
            {
                Tag = pageNumber,
                Background = Brushes.White,
                MinHeight = 120,
                Padding = new Thickness(16),
                Child = new TextBlock
                {
                    Text = "Seite " + pageNumber + " konnte nicht geladen werden.",
                    Foreground = Brushes.Gray,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                }
            };
        }

        private void AllPagesScroll_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (_editMode || _loadingPages)
                return;

            UpdatePageFitWidth();
        }

        private void EditScrollViewer_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (!_editMode)
                return;

            var width = EditScrollViewer.ViewportWidth;
            if (width <= 0)
                width = EditScrollViewer.ActualWidth;
            if (width > 0)
                EditViewbox.MaxWidth = Math.Max(200, width - 8);
        }

        private void UpdatePageFitWidth()
        {
            var width = AllPagesScroll.ViewportWidth;
            if (width <= 0)
                width = AllPagesScroll.ActualWidth;
            if (width <= 0)
                return;

            var maxWidth = Math.Max(200, width - 8);
            foreach (UIElement child in PagesStack.Children)
            {
                if (child is FrameworkElement element)
                    element.MaxWidth = maxWidth;
            }
        }

        private void AllPagesScroll_ScrollChanged(object sender, ScrollChangedEventArgs e)
        {
            if (_editMode || _loadingPages)
                return;

            UpdateCurrentPageFromScroll();
        }

        private void UpdateCurrentPageFromScroll()
        {
            if (PagesStack.Children.Count == 0)
            {
                UpdatePageInfo();
                return;
            }

            var centerY = AllPagesScroll.VerticalOffset + AllPagesScroll.ViewportHeight / 2;
            var offsetY = 0.0;

            foreach (UIElement child in PagesStack.Children)
            {
                var height = child.RenderSize.Height;
                if (height <= 0)
                    height = child.DesiredSize.Height;

                if (centerY >= offsetY && centerY < offsetY + height)
                {
                    if (child is FrameworkElement element && element.Tag is int pageNumber)
                        _currentMergedPage = pageNumber;
                    break;
                }

                offsetY += height;
            }

            UpdatePageInfo();
        }

        private void ShowViewMode()
        {
            _editMode = false;
            _editDirty = false;
            EditPanel.Visibility = Visibility.Collapsed;
            SaveButton.Visibility = Visibility.Collapsed;
            CancelEditButton.Visibility = Visibility.Collapsed;
            CorrectButton.Visibility = Visibility.Visible;
            EditViewBorder.Visibility = Visibility.Collapsed;
            AllPagesScroll.Visibility = Visibility.Visible;
            HintTextBlock.Text = PagesStack.Children.Count > 0
                ? "Durchscrollen wie gewohnt. Korrektur nur bei Bedarf."
                : "Lade Vorschau…";

            ClearOverlays();
            UpdatePageInfo();
        }

        private void EnterEditMode()
        {
            var entry = GetCurrentEntry();
            if (entry == null || string.IsNullOrWhiteSpace(entry.SourcePdfPath) || !File.Exists(entry.SourcePdfPath))
            {
                MessageBox.Show(this, "Keine Zuordnung zur Quelldatei für diese Seite.", "Korrektur", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            if (entry.SourcePageNumber != 1)
            {
                MessageBox.Show(
                    this,
                    "Wasserzeichen können nur auf der ersten Seite einer Zeichnung korrigiert werden. Bitte zur ersten Seite dieser Zeichnung scrollen.",
                    "Korrektur",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            var items = WatermarkEditorService.LoadItems(new[] { entry.SourcePdfPath });
            _editItem = items.FirstOrDefault();
            if (_editItem == null)
            {
                MessageBox.Show(this, "Datei konnte nicht geladen werden.", "Korrektur", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            _editMode = true;
            _editDirty = false;
            _editingMergedPage = _currentMergedPage;
            _editPlacements = WatermarkEditorService.BuildPlacements(_editItem, _settings);

            AllPagesScroll.Visibility = Visibility.Collapsed;
            EditViewBorder.Visibility = Visibility.Visible;
            EditPanel.Visibility = Visibility.Visible;
            SaveButton.Visibility = Visibility.Visible;
            CancelEditButton.Visibility = Visibility.Visible;
            CorrectButton.Visibility = Visibility.Collapsed;
            HintTextBlock.Text = "Korrekturmodus für: " + Path.GetFileName(_editItem.PdfPath);

            ClearOverlays();
            RenderEditCanvas();
            UpdateMaterialField();
            EditScrollViewer_SizeChanged(EditScrollViewer, null);
        }

        private void RenderEditCanvas()
        {
            if (_editItem == null)
                return;

            using (var bitmap = PdfRasterLayoutService.RenderPageForPreview(_editItem.PreWatermarkPath, 1))
            {
                if (bitmap == null)
                    return;

                _scale = bitmap.Width / _editItem.PageWidth;
                PreviewCanvas.Width = bitmap.Width;
                PreviewCanvas.Height = bitmap.Height;
                PreviewImage.Width = bitmap.Width;
                PreviewImage.Height = bitmap.Height;
                PreviewImage.Source = ConvertBitmap(bitmap);
            }

            PreviewCanvas.Children.Clear();
            PreviewCanvas.Children.Add(PreviewImage);
            Panel.SetZIndex(PreviewImage, 0);

            foreach (var placement in _editPlacements)
            {
                if (string.IsNullOrWhiteSpace(placement?.Text))
                    continue;

                PreviewCanvas.Children.Add(CreateOverlay(placement));
            }
        }

        private Border CreateOverlay(WatermarkTextPlacement placement)
        {
            var fontSize = WatermarkOverlayMapper.GetScreenFontSize(placement, _scale);
            var textBlock = new TextBlock
            {
                Text = placement.Text,
                FontSize = fontSize,
                FontWeight = placement.Category == "Material" ? FontWeights.Bold : FontWeights.SemiBold,
                Foreground = placement.UseRedText
                    ? new SolidColorBrush(Color.FromRgb(200, 40, 40))
                    : new SolidColorBrush(Color.FromRgb(55, 55, 55)),
                TextWrapping = TextWrapping.Wrap,
                MaxWidth = Math.Max(180, PreviewCanvas.Width * 0.35)
            };

            var border = new Border
            {
                Child = textBlock,
                Background = new SolidColorBrush(Color.FromArgb(48, 255, 255, 120)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(180, 255, 210, 0)),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(4, 2, 4, 2),
                Cursor = Cursors.SizeAll
            };

            var topLeft = WatermarkOverlayMapper.PdfToCanvasTopLeft(placement, _editItem.PageHeight, _scale);
            Canvas.SetLeft(border, topLeft.X);
            Canvas.SetTop(border, topLeft.Y);
            Panel.SetZIndex(border, 10);

            border.MouseLeftButtonDown += Overlay_MouseLeftButtonDown;
            border.MouseMove += Overlay_MouseMove;
            border.MouseLeftButtonUp += Overlay_MouseLeftButtonUp;
            _overlayMap[border] = placement;
            return border;
        }

        private void Overlay_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _dragBorder = sender as Border;
            if (_dragBorder == null)
                return;

            _isDragging = true;
            _dragStart = e.GetPosition(PreviewCanvas);
            _dragBorder.CaptureMouse();
            _editDirty = true;
            e.Handled = true;
        }

        private void Overlay_MouseMove(object sender, MouseEventArgs e)
        {
            if (!_isDragging || _dragBorder == null || _editItem == null)
                return;

            var current = e.GetPosition(PreviewCanvas);
            var left = Canvas.GetLeft(_dragBorder) + (current.X - _dragStart.X);
            var top = Canvas.GetTop(_dragBorder) + (current.Y - _dragStart.Y);
            _dragStart = current;

            left = Math.Max(0, Math.Min(left, PreviewCanvas.Width - 40));
            top = Math.Max(0, Math.Min(top, PreviewCanvas.Height - 20));
            Canvas.SetLeft(_dragBorder, left);
            Canvas.SetTop(_dragBorder, top);

            if (_overlayMap.TryGetValue(_dragBorder, out var placement))
            {
                WatermarkOverlayMapper.UpdatePlacementFromCanvasTopLeft(
                    placement, left, top, _editItem.PageHeight, _scale);
                _editDirty = true;
            }
        }

        private void Overlay_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (_dragBorder != null)
                _dragBorder.ReleaseMouseCapture();

            _isDragging = false;
            _dragBorder = null;
        }

        private void MaterialTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_suppressMaterialTextChange || !_editMode)
                return;

            var newText = MaterialTextBox.Text ?? string.Empty;
            var materialPlacements = _editPlacements
                .Where(p => string.Equals(p.Category, "Material", StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (materialPlacements.Count == 0 && !string.IsNullOrWhiteSpace(newText) && _editItem != null)
            {
                var placement = new WatermarkTextPlacement
                {
                    Category = "Material",
                    Text = newText.Trim(),
                    X = _editItem.PageWidth * 0.5f,
                    Y = _editItem.PageHeight * 0.5f,
                    Alignment = 1,
                    FontSize = 22f
                };
                _editPlacements.Add(placement);
                PreviewCanvas.Children.Add(CreateOverlay(placement));
            }
            else
            {
                foreach (var placement in materialPlacements)
                {
                    placement.Text = newText.Trim();
                    var border = _overlayMap.FirstOrDefault(pair => pair.Value == placement).Key;
                    if (border?.Child is TextBlock textBlock)
                        textBlock.Text = placement.Text;
                }
            }

            _editDirty = true;
        }

        private void UpdateMaterialField()
        {
            _suppressMaterialTextChange = true;
            var material = _editPlacements.FirstOrDefault(p => p.Category == "Material");
            MaterialTextBox.Text = material?.Text ?? string.Empty;
            _suppressMaterialTextChange = false;
        }

        private void UpdatePageInfo()
        {
            var entry = GetCurrentEntry();
            var sourceName = entry == null ? string.Empty : Path.GetFileName(entry.SourcePdfPath);
            if (!string.IsNullOrWhiteSpace(sourceName) &&
                sourceName.EndsWith(WatermarkEditorService.PreWatermarkSuffix, StringComparison.OrdinalIgnoreCase))
            {
                sourceName = sourceName.Substring(0, sourceName.Length - WatermarkEditorService.PreWatermarkSuffix.Length) + ".pdf";
            }

            var total = _pageMap.Count > 0 ? _pageMap.Count : _totalPages;
            PageInfoTextBlock.Text = string.Format(
                "Aktuelle Seite {0} / {1}{2}",
                _currentMergedPage,
                total,
                string.IsNullOrWhiteSpace(sourceName) ? string.Empty : "   |   " + sourceName);
        }

        private void ClearOverlays()
        {
            _overlayMap.Clear();
            if (PreviewCanvas != null)
                PreviewCanvas.Children.Clear();
        }

        private int _editingMergedPage;

        private void CommitOverlayPositions()
        {
            if (_editItem == null)
                return;

            foreach (var pair in _overlayMap)
            {
                var left = Canvas.GetLeft(pair.Key);
                var top = Canvas.GetTop(pair.Key);
                WatermarkOverlayMapper.UpdatePlacementFromCanvasTopLeft(
                    pair.Value,
                    left,
                    top,
                    _editItem.PageHeight,
                    _scale);
            }
        }

        private bool SaveCurrentEdit()
        {
            if (!_editMode || _editItem == null)
                return true;

            try
            {
                CommitOverlayPositions();
                WatermarkEditorService.SaveItem(_editItem, _editPlacements, _settings);
                PdfRasterLayoutService.ClearCache();
                _mergedPdfPath = WatermarkEditorService.RemergeFolder(_pdfFolder, _orderedFiles);
                _pageMap = NormalizePageMap(MergedPdfPageMapService.Build(_orderedFiles));
                MergedPdfPageMapService.Save(_mergedPdfPath, _pageMap);
                _totalPages = Math.Max(1, _pageMap.Count);
                _editDirty = false;
                return true;
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "Speichern fehlgeschlagen", MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }
        }

        private void CorrectButton_Click(object sender, RoutedEventArgs e)
        {
            UpdateCurrentPageFromScroll();
            EnterEditMode();
        }

        private async void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            var savedPage = _editingMergedPage > 0 ? _editingMergedPage : _currentMergedPage;

            if (!SaveCurrentEdit())
                return;

            ShowViewMode();
            await LoadAllPagesAsync(savedPage);
        }

        private void CancelEditButton_Click(object sender, RoutedEventArgs e)
        {
            if (_editDirty)
            {
                var result = MessageBox.Show(
                    this,
                    "Korrektur verwerfen?",
                    "Abbrechen",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

                if (result != MessageBoxResult.Yes)
                    return;
            }

            ShowViewMode();
        }

        private void OpenPdfButton_Click(object sender, RoutedEventArgs e)
        {
            if (_editMode && _editDirty)
            {
                var result = MessageBox.Show(
                    this,
                    "Ungespeicherte Korrektur speichern bevor die PDF geöffnet wird?",
                    "Korrektur",
                    MessageBoxButton.YesNoCancel,
                    MessageBoxImage.Question);

                if (result == MessageBoxResult.Cancel)
                    return;

                if (result == MessageBoxResult.Yes && !SaveCurrentEdit())
                    return;

                if (_editMode)
                    ShowViewMode();
            }

            if (string.IsNullOrWhiteSpace(_mergedPdfPath) || !File.Exists(_mergedPdfPath))
            {
                MessageBox.Show(this, "Gesamt-PDF nicht gefunden.", "PDF öffnen", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (!PdfPrintService.TryOpenPdfExternally(_mergedPdfPath))
            {
                MessageBox.Show(
                    this,
                    "Die Gesamt-PDF konnte nicht geöffnet werden.",
                    "PDF öffnen",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            _allowClose = true;
            Close();
        }

        private void EditorButton_Click(object sender, RoutedEventArgs e)
        {
            if (_editMode && _editDirty)
            {
                var result = MessageBox.Show(
                    this,
                    "Ungespeicherte Korrektur speichern bevor der Editor geöffnet wird?",
                    "Korrektur",
                    MessageBoxButton.YesNoCancel,
                    MessageBoxImage.Question);

                if (result == MessageBoxResult.Cancel)
                    return;

                if (result == MessageBoxResult.Yes && !SaveCurrentEdit())
                    return;

                if (_editMode)
                    ShowViewMode();
            }

            if (_orderedFiles.Count == 0)
            {
                MessageBox.Show(this, "Keine Quelldateien für den Editor gefunden.", "Editor", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var editor = new PdfWatermarkEditorWindow(
                _orderedFiles,
                _pdfFolder,
                _mergedPdfPath,
                _orderedFiles)
            {
                Owner = this,
                WindowStartupLocation = WindowStartupLocation.CenterOwner
            };
            ThemeWindowService.Attach(editor);

            if (editor.ShowDialog() != true || string.IsNullOrWhiteSpace(editor.MergedPdfPath))
                return;

            _allowClose = true;
            Close();
        }

        private bool _allowClose;

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            if (_allowClose)
            {
                base.OnClosing(e);
                return;
            }

            if (_editMode && _editDirty)
            {
                e.Cancel = true;

                var result = MessageBox.Show(
                    this,
                    "Korrektur speichern bevor Sie schließen?",
                    "Ungespeicherte Korrektur",
                    MessageBoxButton.YesNoCancel,
                    MessageBoxImage.Question);

                if (result == MessageBoxResult.Cancel)
                    return;

                if (result == MessageBoxResult.Yes && !SaveCurrentEdit())
                    return;

                if (_editMode)
                    ShowViewMode();
            }

            _allowClose = true;
            e.Cancel = false;
            base.OnClosing(e);
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

        [System.Runtime.InteropServices.DllImport("gdi32.dll")]
        private static extern bool DeleteObject(IntPtr hObject);
    }
}
