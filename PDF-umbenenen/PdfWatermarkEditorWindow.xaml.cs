using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
    public partial class PdfWatermarkEditorWindow : Window
    {
        private readonly List<WatermarkEditorItem> _items;
        private readonly List<string> _allOrderedFiles;
        private readonly string _pdfFolder;
        private readonly WatermarkSettings _settings;
        private readonly Dictionary<int, List<WatermarkTextPlacement>> _editedPlacements =
            new Dictionary<int, List<WatermarkTextPlacement>>();

        private int _currentIndex;
        private List<WatermarkTextPlacement> _currentPlacements = new List<WatermarkTextPlacement>();
        private readonly Dictionary<Border, WatermarkTextPlacement> _overlayMap =
            new Dictionary<Border, WatermarkTextPlacement>();

        private WatermarkTextPlacement _selectedPlacement;
        private double _scale = 1.0;
        private bool _suppressMaterialTextChange;
        private bool _hasUnsavedChanges;

        private Point _dragStart;
        private bool _isDragging;
        private Border _dragBorder;

        public string MergedPdfPath { get; private set; }

        public PdfWatermarkEditorWindow(
            IEnumerable<string> pdfPaths,
            string pdfFolder,
            string mergedPdfPath = null,
            IEnumerable<string> allOrderedFilesForRemerge = null)
        {
            InitializeComponent();
            ThemeWindowService.Attach(this);

            _items = WatermarkEditorService.LoadItems(pdfPaths);
            _pdfFolder = pdfFolder ?? string.Empty;
            _allOrderedFiles = (allOrderedFilesForRemerge ?? pdfPaths ?? Enumerable.Empty<string>())
                .Where(File.Exists)
                .ToList();
            _settings = WatermarkSettingsStore.Load();
            MergedPdfPath = mergedPdfPath;

            if (_items.Count == 0)
            {
                MessageBox.Show(
                    this,
                    "Keine bearbeitbaren PDF-Dateien gefunden.",
                    "Wasserzeichen korrigieren",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                Loaded += (_, __) => Close();
                return;
            }

            if (_items.Count <= 1)
                NavigationPanel.Visibility = Visibility.Collapsed;

            _currentIndex = 0;
            Loaded += (_, __) => LoadCurrentItem();
        }

        private void LoadCurrentItem()
        {
            if (_items.Count == 0 || _currentIndex < 0 || _currentIndex >= _items.Count)
                return;

            var item = _items[_currentIndex];
            FileInfoTextBlock.Text = string.Format(
                "Datei {0}/{1}: {2}   |   Zeichnung: {3}",
                _currentIndex + 1,
                _items.Count,
                Path.GetFileName(item.PdfPath),
                string.IsNullOrWhiteSpace(item.DrawingNumber) ? "—" : item.DrawingNumber);

            _currentPlacements = GetPlacementsForIndex(_currentIndex);
            RenderPreview(item);
            _hasUnsavedChanges = false;
            UpdateHint();
        }

        private List<WatermarkTextPlacement> GetPlacementsForIndex(int index)
        {
            if (_editedPlacements.TryGetValue(index, out var edited))
                return ClonePlacements(edited);

            return ClonePlacements(WatermarkEditorService.BuildPlacements(_items[index], _settings));
        }

        private void RenderPreview(WatermarkEditorItem item)
        {
            PreviewCanvas.Children.Clear();
            _overlayMap.Clear();
            _selectedPlacement = null;
            _suppressMaterialTextChange = true;
            MaterialTextBox.Text = string.Empty;
            _suppressMaterialTextChange = false;

            using (var bitmap = PdfRasterLayoutService.RenderPageForPreview(item.PreWatermarkPath, 1))
            {
                if (bitmap == null)
                {
                    HintTextBlock.Text = "Vorschau konnte nicht gerendert werden.";
                    return;
                }

                _scale = bitmap.Width / item.PageWidth;
                PreviewCanvas.Width = bitmap.Width;
                PreviewCanvas.Height = bitmap.Height;
                PreviewImage.Width = bitmap.Width;
                PreviewImage.Height = bitmap.Height;
                PreviewImage.Source = ConvertBitmap(bitmap);
            }

            PreviewCanvas.Children.Add(PreviewImage);
            Panel.SetZIndex(PreviewImage, 0);

            foreach (var placement in _currentPlacements)
            {
                if (string.IsNullOrWhiteSpace(placement?.Text))
                    continue;

                var border = CreateOverlay(placement, item.PageHeight);
                PreviewCanvas.Children.Add(border);
            }
        }

        private Border CreateOverlay(WatermarkTextPlacement placement, float pageHeight)
        {
            var fontSize = WatermarkOverlayMapper.GetScreenFontSize(placement, _scale);
            var textBlock = new TextBlock
            {
                Text = placement.Text,
                FontSize = fontSize,
                FontWeight = placement.Category == "Material" ? FontWeights.Bold : FontWeights.SemiBold,
                Foreground = placement.UseRedText
                    ? new SolidColorBrush(System.Windows.Media.Color.FromRgb(200, 40, 40))
                    : new SolidColorBrush(System.Windows.Media.Color.FromRgb(55, 55, 55)),
                TextWrapping = TextWrapping.Wrap,
                MaxWidth = Math.Max(180, PreviewCanvas.Width * 0.35)
            };

            var border = new Border
            {
                Child = textBlock,
                Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(48, 255, 255, 120)),
                BorderBrush = new SolidColorBrush(System.Windows.Media.Color.FromArgb(180, 255, 210, 0)),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(4, 2, 4, 2),
                Cursor = Cursors.SizeAll,
                Tag = placement.Category
            };

            var topLeft = WatermarkOverlayMapper.PdfToCanvasTopLeft(placement, pageHeight, _scale);
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
            SelectPlacement(_overlayMap[_dragBorder]);
            e.Handled = true;
        }

        private void Overlay_MouseMove(object sender, MouseEventArgs e)
        {
            if (!_isDragging || _dragBorder == null)
                return;

            var current = e.GetPosition(PreviewCanvas);
            var deltaX = current.X - _dragStart.X;
            var deltaY = current.Y - _dragStart.Y;
            _dragStart = current;

            var left = Canvas.GetLeft(_dragBorder) + deltaX;
            var top = Canvas.GetTop(_dragBorder) + deltaY;
            left = Math.Max(0, Math.Min(left, PreviewCanvas.Width - 40));
            top = Math.Max(0, Math.Min(top, PreviewCanvas.Height - 20));
            Canvas.SetLeft(_dragBorder, left);
            Canvas.SetTop(_dragBorder, top);

            if (_overlayMap.TryGetValue(_dragBorder, out var placement))
            {
                WatermarkOverlayMapper.UpdatePlacementFromCanvasTopLeft(
                    placement,
                    left,
                    top,
                    _items[_currentIndex].PageHeight,
                    _scale);
                _hasUnsavedChanges = true;
            }
        }

        private void Overlay_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (_dragBorder != null)
                _dragBorder.ReleaseMouseCapture();

            _isDragging = false;
            _dragBorder = null;
        }

        private void SelectPlacement(WatermarkTextPlacement placement)
        {
            _selectedPlacement = placement;

            foreach (var pair in _overlayMap)
            {
                pair.Key.BorderThickness = pair.Value == placement
                    ? new Thickness(2)
                    : new Thickness(1);
            }

            _suppressMaterialTextChange = true;
            if (placement != null && placement.Category == "Material")
                MaterialTextBox.Text = placement.Text ?? string.Empty;
            else if (_currentPlacements.Any(p => p.Category == "Material"))
                MaterialTextBox.Text = _currentPlacements.First(p => p.Category == "Material").Text ?? string.Empty;
            else
                MaterialTextBox.Text = string.Empty;
            _suppressMaterialTextChange = false;
        }

        private void MaterialTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_suppressMaterialTextChange)
                return;

            var newText = MaterialTextBox.Text ?? string.Empty;
            var materialPlacements = _currentPlacements
                .Where(p => string.Equals(p.Category, "Material", StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (materialPlacements.Count == 0 && !string.IsNullOrWhiteSpace(newText))
            {
                var item = _items[_currentIndex];
                var placement = new WatermarkTextPlacement
                {
                    Category = "Material",
                    Text = newText.Trim(),
                    X = (float)(item.PageWidth * 0.5f),
                    Y = (float)(item.PageHeight * 0.5f),
                    Alignment = 1,
                    FontSize = 22f
                };
                _currentPlacements.Add(placement);
                var border = CreateOverlay(placement, item.PageHeight);
                PreviewCanvas.Children.Add(border);
                SelectPlacement(placement);
            }
            else
            {
                foreach (var placement in materialPlacements)
                {
                    placement.Text = newText.Trim();
                    var border = _overlayMap.FirstOrDefault(p => p.Value == placement).Key;
                    if (border?.Child is TextBlock textBlock)
                        textBlock.Text = placement.Text;
                }
            }

            _hasUnsavedChanges = true;
        }

        private void CommitOverlayPositions()
        {
            if (_items.Count == 0 || _currentIndex < 0 || _currentIndex >= _items.Count)
                return;

            var pageHeight = _items[_currentIndex].PageHeight;
            foreach (var pair in _overlayMap)
            {
                var left = Canvas.GetLeft(pair.Key);
                var top = Canvas.GetTop(pair.Key);
                WatermarkOverlayMapper.UpdatePlacementFromCanvasTopLeft(
                    pair.Value,
                    left,
                    top,
                    pageHeight,
                    _scale);
            }
        }

        private void SaveCurrent_Click(object sender, RoutedEventArgs e)
        {
            SaveCurrentItem(showMessage: true);
        }

        private bool SaveCurrentItem(bool showMessage)
        {
            if (_items.Count == 0)
                return false;

            try
            {
                CommitOverlayPositions();
                var item = _items[_currentIndex];
                _editedPlacements[_currentIndex] = ClonePlacements(_currentPlacements);
                WatermarkEditorService.SaveItem(item, _currentPlacements, _settings);
                PdfRasterLayoutService.ClearCache();
                _hasUnsavedChanges = false;
                UpdateHint();

                if (showMessage)
                {
                    MessageBox.Show(
                        this,
                        "Gespeichert. Position und Material werden für ähnliche Zeichnungen gemerkt.",
                        "Wasserzeichen",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                }

                return true;
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "Speichern fehlgeschlagen", MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }
        }

        private void SaveAllAndRemerge_Click(object sender, RoutedEventArgs e)
        {
            if (!SaveCurrentItem(showMessage: false))
                return;

            for (var i = 0; i < _items.Count; i++)
            {
                if (i == _currentIndex)
                    continue;

                try
                {
                    var placements = GetPlacementsForIndex(i);
                    WatermarkEditorService.SaveItem(_items[i], placements, _settings);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(
                        this,
                        "Fehler bei " + Path.GetFileName(_items[i].PdfPath) + ": " + ex.Message,
                        "Speichern",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                }
            }

            try
            {
                var orderedPaths = _allOrderedFiles.Count > 0
                    ? _allOrderedFiles
                    : _items.Select(item => item.PdfPath).ToList();
                WatermarkEditorService.RefreshPageNumbers(orderedPaths);

                if (!string.IsNullOrWhiteSpace(_pdfFolder) && Directory.Exists(_pdfFolder))
                    MergedPdfPath = WatermarkEditorService.RemergeFolder(_pdfFolder, orderedPaths);

                _hasUnsavedChanges = false;
                if (!string.IsNullOrWhiteSpace(MergedPdfPath))
                    PdfPrintService.TryOpenPdfExternally(MergedPdfPath);

                DialogResult = true;
                Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "Gesamt-PDF", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void PreviousItem_Click(object sender, RoutedEventArgs e)
        {
            Navigate(-1);
        }

        private void NextItem_Click(object sender, RoutedEventArgs e)
        {
            Navigate(1);
        }

        private void Navigate(int delta)
        {
            if (_items.Count == 0)
                return;

            if (_hasUnsavedChanges)
            {
                var result = MessageBox.Show(
                    this,
                    "Änderungen an dieser Datei speichern?",
                    "Ungespeicherte Änderungen",
                    MessageBoxButton.YesNoCancel,
                    MessageBoxImage.Question);

                if (result == MessageBoxResult.Cancel)
                    return;

                if (result == MessageBoxResult.Yes && !SaveCurrentItem(showMessage: false))
                    return;
            }

            _currentIndex = (_currentIndex + delta + _items.Count) % _items.Count;
            LoadCurrentItem();
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            if (_hasUnsavedChanges)
            {
                var result = MessageBox.Show(
                    this,
                    "Änderungen speichern bevor Sie schließen?",
                    "Ungespeicherte Änderungen",
                    MessageBoxButton.YesNoCancel,
                    MessageBoxImage.Question);

                if (result == MessageBoxResult.Cancel)
                    return;

                if (result == MessageBoxResult.Yes && !SaveCurrentItem(showMessage: false))
                    return;
            }

            DialogResult = false;
            Close();
        }

        private void UpdateHint()
        {
            HintTextBlock.Text = _hasUnsavedChanges
                ? "Ungespeicherte Änderungen – bitte Speichern klicken."
                : "Tipp: Wasserzeichen mit der Maus ziehen. Material-Feld bearbeiten und speichern.";
        }

        private static List<WatermarkTextPlacement> ClonePlacements(IEnumerable<WatermarkTextPlacement> source)
        {
            return (source ?? Enumerable.Empty<WatermarkTextPlacement>())
                .Select(p => new WatermarkTextPlacement
                {
                    Text = p.Text,
                    X = p.X,
                    Y = p.Y,
                    Alignment = p.Alignment,
                    Angle = p.Angle,
                    FontSize = p.FontSize,
                    UseRedText = p.UseRedText,
                    Category = p.Category
                })
                .ToList();
        }

        private static BitmapSource ConvertBitmap(System.Drawing.Bitmap bitmap)
        {
            var handle = bitmap.GetHbitmap();
            try
            {
                return Imaging.CreateBitmapSourceFromHBitmap(
                    handle,
                    IntPtr.Zero,
                    Int32Rect.Empty,
                    BitmapSizeOptions.FromEmptyOptions());
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
