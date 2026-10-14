using System;
using System.IO;
using System.Printing;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Interop;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace PDF_umbenenen.Services
{
    internal static class PdfPrintService
    {
        public static int GetPageCount(string pdfPath)
        {
            return Math.Max(1, PdfWatermarkService.GetPageCount(pdfPath));
        }

        public static System.Drawing.Bitmap RenderPrintPageBitmap(string pdfPath, int pageNumber)
        {
            if (!PdfRasterLayoutService.IsAvailable)
                return null;

            return PdfRasterLayoutService.RenderPageForPrint(pdfPath, pageNumber);
        }

        /// <summary>
        /// WPF-Elemente — nur vom UI-Thread (STA) aufrufen.
        /// </summary>
        public static void AddRenderedPage(FixedDocument document, System.Drawing.Bitmap bitmap)
        {
            if (document == null)
                throw new ArgumentNullException(nameof(document));

            if (bitmap == null)
                return;

            var source = ConvertBitmap(bitmap);
            if (source.CanFreeze)
                source.Freeze();

            var width = source.Width;
            var height = source.Height;

            var page = new FixedPage
            {
                Width = width,
                Height = height
            };

            var image = new Image
            {
                Source = source,
                Width = width,
                Height = height,
                Stretch = Stretch.Fill
            };

            FixedPage.SetLeft(image, 0);
            FixedPage.SetTop(image, 0);
            page.Children.Add(image);

            var pageContent = new PageContent();
            ((IAddChild)pageContent).AddChild(page);
            document.Pages.Add(pageContent);
        }

        public static FixedDocument CreateFixedDocument(string pdfPath, IProgress<int> progress = null)
        {
            if (string.IsNullOrWhiteSpace(pdfPath) || !File.Exists(pdfPath))
                throw new FileNotFoundException("PDF nicht gefunden.", pdfPath);

            if (!PdfRasterLayoutService.IsAvailable)
                throw new InvalidOperationException("PDF-Druck nicht verfügbar (Docnet/pdfium fehlt).");

            var document = new FixedDocument();
            var pageCount = GetPageCount(pdfPath);

            for (var pageNumber = 1; pageNumber <= pageCount; pageNumber++)
            {
                progress?.Report(pageNumber);

                using (var bitmap = RenderPrintPageBitmap(pdfPath, pageNumber))
                {
                    if (bitmap == null)
                        continue;

                    AddRenderedPage(document, bitmap);
                }
            }

            if (document.Pages.Count == 0)
                throw new InvalidOperationException("Keine druckbaren Seiten gefunden.");

            return document;
        }

        public static bool TryPrint(FixedDocument document, Window owner, string jobName = "DOK-V01")
        {
            if (document == null)
                return false;

            var dialog = new PrintDialog();
            if (dialog.ShowDialog() != true)
                return false;

            dialog.PrintDocument(document.DocumentPaginator, jobName);
            return true;
        }

        /// <summary>
        /// Ein Drucker-Dialog, dann Auftrag/Excel beidseitig und Zeichnungen einseitig.
        /// </summary>
        public static bool TryPrintAuftragDuplexAndDrawingsSimplex(
            string auftragPdfPath,
            string drawingsPdfPath,
            Window owner)
        {
            var hasAuftrag = !string.IsNullOrWhiteSpace(auftragPdfPath) && File.Exists(auftragPdfPath);
            var hasDrawings = !string.IsNullOrWhiteSpace(drawingsPdfPath) && File.Exists(drawingsPdfPath);
            if (!hasAuftrag && !hasDrawings)
                return false;

            if (!PdfRasterLayoutService.IsAvailable)
            {
                // Fallback: Dateien öffnen, Nutzer stellt Duplex am Drucker ein
                if (hasAuftrag)
                    TryOpenPdfExternally(auftragPdfPath);
                if (hasDrawings)
                    TryOpenPdfExternally(drawingsPdfPath);
                if (owner != null)
                {
                    MessageBox.Show(
                        owner,
                        "Bitte drucken:\n\n"
                        + "1) _DRUCK_AUFTRAG.pdf → beidseitig\n"
                        + "2) _DRUCK_ZEICHNUNGEN.pdf → einseitig",
                        "Drucken",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                }

                return true;
            }

            var dialog = new PrintDialog();
            if (dialog.ShowDialog() != true)
                return false;

            try
            {
                if (hasAuftrag)
                {
                    var ticket = dialog.PrintTicket ?? new PrintTicket();
                    ticket.Duplexing = Duplexing.TwoSidedLongEdge;
                    dialog.PrintTicket = ticket;
                    var doc = CreateFixedDocument(auftragPdfPath);
                    dialog.PrintDocument(doc.DocumentPaginator, "DOK-V01 Auftrag/Excel (beidseitig)");
                }

                if (hasDrawings)
                {
                    var ticket = dialog.PrintTicket ?? new PrintTicket();
                    ticket.Duplexing = Duplexing.OneSided;
                    dialog.PrintTicket = ticket;
                    var doc = CreateFixedDocument(drawingsPdfPath);
                    dialog.PrintDocument(doc.DocumentPaginator, "DOK-V01 Zeichnungen (einseitig)");
                }

                return true;
            }
            catch (Exception ex)
            {
                if (owner != null)
                {
                    MessageBox.Show(
                        owner,
                        "Druck fehlgeschlagen: " + ex.Message
                        + "\n\nBitte _DRUCK_AUFTRAG.pdf (beidseitig) und _DRUCK_ZEICHNUNGEN.pdf (einseitig) manuell drucken.",
                        "Drucken",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                }

                return false;
            }
        }

        /// <summary>Druckt die PDF-Datei über den Standard-PDF-Handler (korrektes Seitenformat).</summary>
        public static bool TryPrintPdfFile(string pdfPath, Window owner = null)
        {
            if (string.IsNullOrWhiteSpace(pdfPath) || !File.Exists(pdfPath))
            {
                if (owner != null)
                {
                    MessageBox.Show(owner, "Gesamt-PDF nicht gefunden.", "Drucken", MessageBoxButton.OK, MessageBoxImage.Warning);
                }

                return false;
            }

            try
            {
                var printInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = pdfPath,
                    Verb = "print",
                    UseShellExecute = true
                };
                System.Diagnostics.Process.Start(printInfo);
                return true;
            }
            catch
            {
                try
                {
                    if (TryOpenPdfExternally(pdfPath))
                    {
                        if (owner != null)
                        {
                            MessageBox.Show(
                                owner,
                                "Der Direktdruck ist fehlgeschlagen.\n\n" +
                                "Die Gesamt-PDF wurde im Standard-Reader geöffnet — bitte dort mit Strg+P drucken.",
                                "Drucken",
                                MessageBoxButton.OK,
                                MessageBoxImage.Information);
                        }

                        return true;
                    }
                }
                catch
                {
                }
            }

            if (owner != null)
            {
                MessageBox.Show(
                    owner,
                    "Drucken fehlgeschlagen. Bitte die Gesamt-PDF manuell im PDF-Reader öffnen und drucken.",
                    "Drucken",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }

            return false;
        }

        public static bool TryOpenPdfExternally(string pdfPath)
        {
            if (string.IsNullOrWhiteSpace(pdfPath) || !File.Exists(pdfPath))
                return false;

            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = pdfPath,
                UseShellExecute = true
            });
            return true;
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

        [DllImport("gdi32.dll")]
        private static extern bool DeleteObject(IntPtr hObject);
    }
}
