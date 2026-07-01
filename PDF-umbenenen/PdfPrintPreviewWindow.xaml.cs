using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Documents;
using PDF_umbenenen.Services;

namespace PDF_umbenenen
{
    public partial class PdfPrintPreviewWindow : Window
    {
        private readonly string _pdfPath;
        private FixedDocument _document;

        public PdfPrintPreviewWindow(string pdfPath)
        {
            InitializeComponent();
            ThemeWindowService.Attach(this);

            _pdfPath = pdfPath;
            Loaded += async (_, __) => await LoadPreviewAsync();
        }

        private async Task LoadPreviewAsync()
        {
            PrintButton.IsEnabled = false;
            StatusTextBlock.Text = "Lade Druckvorschau…";

            try
            {
                if (!PdfRasterLayoutService.IsAvailable)
                    throw new InvalidOperationException("PDF-Druck nicht verfügbar (Docnet/pdfium fehlt).");

                if (string.IsNullOrWhiteSpace(_pdfPath) || !System.IO.File.Exists(_pdfPath))
                    throw new System.IO.FileNotFoundException("PDF nicht gefunden.", _pdfPath);

                _document = new FixedDocument();
                var pageCount = PdfPrintService.GetPageCount(_pdfPath);

                for (var pageNumber = 1; pageNumber <= pageCount; pageNumber++)
                {
                    StatusTextBlock.Text = string.Format("Lade Seite {0} / {1}…", pageNumber, pageCount);

                    var page = pageNumber;
                    System.Drawing.Bitmap bitmap = null;
                    await Task.Run(() =>
                    {
                        bitmap = PdfPrintService.RenderPrintPageBitmap(_pdfPath, page);
                    }).ConfigureAwait(true);

                    if (bitmap == null)
                        continue;

                    try
                    {
                        PdfPrintService.AddRenderedPage(_document, bitmap);
                    }
                    finally
                    {
                        bitmap.Dispose();
                    }

                    await Task.Yield();
                }

                if (_document.Pages.Count == 0)
                    throw new InvalidOperationException("Keine druckbaren Seiten gefunden.");

                PrintDocumentViewer.Document = _document;
                StatusTextBlock.Text = string.Format(
                    "Druckvorschau bereit — {0} Seite(n).",
                    _document.Pages.Count);
                PrintButton.IsEnabled = true;
            }
            catch (Exception ex)
            {
                StatusTextBlock.Text = "Druckvorschau konnte nicht geladen werden.";
                MessageBox.Show(this, ex.Message, "Druckvorschau", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void PrintButton_Click(object sender, RoutedEventArgs e)
        {
            if (_document == null)
                return;

            try
            {
                if (PdfPrintService.TryPrint(_document, this, "DOK-V01 Gesamt-PDF"))
                    StatusTextBlock.Text = "Druckauftrag gesendet.";
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "Drucken", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
