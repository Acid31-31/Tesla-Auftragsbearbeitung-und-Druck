using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;

namespace PDF_umbenenen.Services
{
    /// <summary>
    /// Schriftfeld-OCR ausschließlich über lokales Tesseract-CLI (offline).
    /// </summary>
    internal static class TitleBlockOcrService
    {
        private static readonly Dictionary<string, string> PageOcrCache =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private static readonly object CacheLock = new object();

        internal static string LastError { get; private set; }

        public static void ClearCache()
        {
            lock (CacheLock)
                PageOcrCache.Clear();
        }

        public static string TryReadTitleBlock(string pdfPath, int pageNumber = 1)
        {
            var dpi = WatermarkProcessingContext.BatchMode ? OcrBatchRenderDpi : OcrStandardRenderDpi;
            return TryReadTitleBlock(pdfPath, pageNumber, dpi);
        }

        public static string TryReadTitleBlockThorough(string pdfPath, int pageNumber = 1)
        {
            var primary = TryReadTitleBlock(pdfPath, pageNumber, OcrThoroughRenderDpi);
            if (!string.IsNullOrWhiteSpace(primary))
                return primary;

            return TryReadTitleBlock(pdfPath, pageNumber, OcrStandardRenderDpi);
        }

        private const int OcrStandardRenderDpi = 132;
        private const int OcrBatchRenderDpi = 108;
        private const int OcrThoroughRenderDpi = 192;

        private static string TryReadTitleBlock(string pdfPath, int pageNumber, int renderDpi)
        {
            LastError = string.Empty;

            if (!string.IsNullOrWhiteSpace(pdfPath) && File.Exists(pdfPath))
            {
                var cacheKey = pdfPath + "|" + pageNumber + "|" + File.GetLastWriteTimeUtc(pdfPath).Ticks;
                lock (CacheLock)
                {
                    if (PageOcrCache.TryGetValue(cacheKey, out var cached))
                        return cached ?? string.Empty;
                }
            }

            var settings = Ai.AiSettingsStore.Load();
            if (!settings.LocalOcrEnabled)
            {
                LastError = "ocr-deaktiviert";
                return string.Empty;
            }

            if (!PdfRasterLayoutService.IsAvailable)
            {
                LastError = "pdfium fehlt";
                return string.Empty;
            }

            string tempPath = null;
            try
            {
                using (var page = PdfRasterLayoutService.RenderPageForOcr(pdfPath, pageNumber, renderDpi))
                {
                    if (page == null)
                    {
                        LastError = "render-null";
                        return string.Empty;
                    }

                    using (var crop = CropTitleBlock(page))
                    {
                        tempPath = Path.Combine(Path.GetTempPath(), "dok-titleblock-" + Guid.NewGuid().ToString("N") + ".png");
                        crop.Save(tempPath, ImageFormat.Png);

                        var langs = string.IsNullOrWhiteSpace(settings.OcrLanguages) ? "deu+eng" : settings.OcrLanguages;
                        var text = Cli.TesseractCliService.RecognizeImage(tempPath, langs);
                        LastError = Cli.TesseractCliService.LastError;

                        if (string.IsNullOrWhiteSpace(text) && string.IsNullOrWhiteSpace(LastError))
                            LastError = "ocr-empty";

                        var result = text ?? string.Empty;
                        if (!string.IsNullOrWhiteSpace(pdfPath) && File.Exists(pdfPath))
                        {
                            var cacheKey = pdfPath + "|" + pageNumber + "|" + File.GetLastWriteTimeUtc(pdfPath).Ticks;
                            lock (CacheLock)
                                PageOcrCache[cacheKey] = result;
                        }

                        return result;
                    }
                }
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
                return string.Empty;
            }
            finally
            {
                if (tempPath != null)
                {
                    try { File.Delete(tempPath); } catch { }
                }
            }
        }

        internal static Bitmap CropTitleBlock(Bitmap source)
        {
            var x = (int)Math.Round(source.Width * 0.38f);
            var y = (int)Math.Round(source.Height * 0.44f);
            var w = Math.Max(1, source.Width - x);
            var h = Math.Max(1, source.Height - y);

            var crop = new Bitmap(w, h);
            using (var graphics = Graphics.FromImage(crop))
            {
                graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                graphics.DrawImage(source, new Rectangle(0, 0, w, h), new Rectangle(x, y, w, h), GraphicsUnit.Pixel);
            }

            return crop;
        }
    }
}
