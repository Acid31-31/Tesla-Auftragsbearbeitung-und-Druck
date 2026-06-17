using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using Docnet.Core;
using Docnet.Core.Models;

namespace PDF_umbenenen.Services
{
    /// <summary>
    /// Rendert PDF-Seiten als Bild und findet freie (helle) Bereiche für Wasserzeichen.
    /// Benötigt Docnet.Core.dll + pdfium.dll neben der EXE (siehe install-docnet.ps1).
    /// </summary>
    internal static class PdfRasterLayoutService
    {
        private const int RenderDpi = 120;
        private const int ScanStepPx = 6;
        private const int WhiteThreshold = 242;
        private const float MaxAcceptableDarkness = 0.08f;

        private static bool _initialized;
        private static bool _available;
        private static string _loadError = string.Empty;
        private static readonly Dictionary<string, Bitmap> PageCache = new Dictionary<string, Bitmap>(StringComparer.OrdinalIgnoreCase);
        private static readonly object CacheLock = new object();

        public static bool IsAvailable
        {
            get
            {
                EnsureLoaded();
                return _available;
            }
        }

        public static string GetStatus()
        {
            EnsureLoaded();
            return _available
                ? "PDF-Bildanalyse verfügbar"
                : "PDF-Bildanalyse nicht verfügbar" + (string.IsNullOrWhiteSpace(_loadError) ? string.Empty : ": " + _loadError);
        }

        public static void ClearCache()
        {
            lock (CacheLock)
            {
                foreach (var bitmap in PageCache.Values)
                    bitmap?.Dispose();
                PageCache.Clear();
            }
        }

        public static bool TryFindFreeAnchor(
            string pdfPath,
            int pageNumber,
            float blockWidthPt,
            float blockHeightPt,
            float pageWidthPt,
            float pageHeightPt,
            float zoneLeftPt,
            float zoneRightPt,
            float zoneBottomPt,
            float zoneTopPt,
            float preferredXPt,
            float preferredYPt,
            out float anchorX,
            out float anchorY)
        {
            anchorX = 0f;
            anchorY = 0f;

            if (!_available || string.IsNullOrWhiteSpace(pdfPath) || !File.Exists(pdfPath))
                return false;

            try
            {
                using (var bitmap = GetOrRenderPage(pdfPath, pageNumber))
                {
                    if (bitmap == null)
                        return false;

                    var scaleX = bitmap.Width / pageWidthPt;
                    var scaleY = bitmap.Height / pageHeightPt;
                    var blockWidthPx = Math.Max(12, (int)Math.Ceiling(blockWidthPt * scaleX));
                    var blockHeightPx = Math.Max(12, (int)Math.Ceiling(blockHeightPt * scaleY));

                    var scanLeft = (int)Math.Max(0, zoneLeftPt * scaleX);
                    var scanRight = (int)Math.Min(bitmap.Width - blockWidthPx, zoneRightPt * scaleX);
                    var scanTop = (int)Math.Max(0, bitmap.Height - (zoneTopPt * scaleY));
                    var scanBottom = (int)Math.Min(bitmap.Height - blockHeightPx, bitmap.Height - (zoneBottomPt * scaleY));

                    if (scanRight <= scanLeft || scanBottom <= scanTop)
                        return false;

                    var bestScore = float.MaxValue;
                    var bestX = scanLeft;
                    var bestY = scanTop;

                    for (var y = scanTop; y <= scanBottom; y += ScanStepPx)
                    {
                        for (var x = scanLeft; x <= scanRight; x += ScanStepPx)
                        {
                            var darkness = MeasureDarkness(bitmap, x, y, blockWidthPx, blockHeightPx);
                            if (darkness > MaxAcceptableDarkness)
                                continue;

                            var candidateX = x / scaleX;
                            var candidateY = pageHeightPt - ((y + blockHeightPx) / scaleY);
                            var score = (darkness * 25f) + DistancePenalty(candidateX, candidateY, preferredXPt, preferredYPt, pageWidthPt, pageHeightPt);

                            if (score < bestScore)
                            {
                                bestScore = score;
                                bestX = x;
                                bestY = y;
                            }
                        }
                    }

                    if (bestScore == float.MaxValue)
                        return false;

                    anchorX = bestX / scaleX;
                    anchorY = pageHeightPt - ((bestY + blockHeightPx) / scaleY);
                    return true;
                }
            }
            catch
            {
                return false;
            }
        }

        private static float DistancePenalty(float anchorX, float anchorY, float targetX, float targetY, float pageWidth, float pageHeight)
        {
            var dx = (anchorX - targetX) / Math.Max(120f, pageWidth * 0.35f);
            var dy = (anchorY - targetY) / Math.Max(80f, pageHeight * 0.18f);
            return (dx * dx) + (dy * dy * 2.5f);
        }

        private static float MeasureDarkness(Bitmap bitmap, int left, int top, int width, int height)
        {
            long dark = 0;
            long total = 0;
            var rect = new Rectangle(left, top, width, height);
            var clipped = Rectangle.Intersect(rect, new Rectangle(0, 0, bitmap.Width, bitmap.Height));
            if (clipped.Width <= 0 || clipped.Height <= 0)
                return 1f;

            var bytesPerPixel = Image.GetPixelFormatSize(bitmap.PixelFormat) / 8;
            if (bytesPerPixel < 3)
                return 1f;

            var data = bitmap.LockBits(clipped, ImageLockMode.ReadOnly, bitmap.PixelFormat);
            try
            {
                var stride = data.Stride;
                var bytes = new byte[stride * clipped.Height];
                Marshal.Copy(data.Scan0, bytes, 0, bytes.Length);

                for (var row = 0; row < clipped.Height; row++)
                {
                    var rowOffset = row * stride;
                    for (var col = 0; col < clipped.Width; col++)
                    {
                        var idx = rowOffset + (col * bytesPerPixel);
                        if (idx + 2 >= bytes.Length)
                            continue;

                        var b = bytes[idx];
                        var g = bytes[idx + 1];
                        var r = bytes[idx + 2];
                        var luminance = (r + g + b) / 3;
                        total++;
                        if (luminance < WhiteThreshold)
                            dark++;
                    }
                }
            }
            finally
            {
                bitmap.UnlockBits(data);
            }

            return total == 0 ? 1f : dark / (float)total;
        }

        private static Bitmap GetOrRenderPage(string pdfPath, int pageNumber)
        {
            var key = pdfPath + "|" + pageNumber + "|" + File.GetLastWriteTimeUtc(pdfPath).Ticks;
            lock (CacheLock)
            {
                Bitmap cached;
                if (PageCache.TryGetValue(key, out cached))
                    return (Bitmap)cached.Clone();

                var rendered = RenderPage(pdfPath, pageNumber);
                if (rendered == null)
                    return null;

                PageCache[key] = (Bitmap)rendered.Clone();
                return rendered;
            }
        }

        private static Bitmap RenderPage(string pdfPath, int pageNumber)
        {
            if (!_available)
                return null;

            var pageIndex = Math.Max(0, pageNumber - 1);
            var scalingFactor = RenderDpi / 72.0;

            using (var docReader = DocLib.Instance.GetDocReader(pdfPath, new PageDimensions(scalingFactor)))
            {
                if (pageIndex >= docReader.GetPageCount())
                    return null;

                using (var pageReader = docReader.GetPageReader(pageIndex))
                {
                    var width = pageReader.GetPageWidth();
                    var height = pageReader.GetPageHeight();
                    var bytes = pageReader.GetImage();
                    if (bytes == null || bytes.Length == 0 || width <= 0 || height <= 0)
                        return null;

                    var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
                    var rect = new Rectangle(0, 0, width, height);
                    var data = bitmap.LockBits(rect, ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
                    try
                    {
                        var length = Math.Min(bytes.Length, data.Stride * height);
                        Marshal.Copy(bytes, 0, data.Scan0, length);
                    }
                    finally
                    {
                        bitmap.UnlockBits(data);
                    }

                    return bitmap;
                }
            }
        }

        public static bool TryMeasureBlockDarkness(
            string pdfPath,
            int pageNumber,
            float anchorX,
            float anchorY,
            float blockWidthPt,
            float blockHeightPt,
            float pageWidthPt,
            float pageHeightPt,
            out float darkness)
        {
            darkness = 1f;
            if (!_available || string.IsNullOrWhiteSpace(pdfPath) || !File.Exists(pdfPath))
                return false;

            try
            {
                using (var bitmap = GetOrRenderPage(pdfPath, pageNumber))
                {
                    if (bitmap == null)
                        return false;

                    var scaleX = bitmap.Width / pageWidthPt;
                    var scaleY = bitmap.Height / pageHeightPt;
                    var blockWidthPx = Math.Max(12, (int)Math.Ceiling(blockWidthPt * scaleX));
                    var blockHeightPx = Math.Max(12, (int)Math.Ceiling(blockHeightPt * scaleY));
                    var topPx = (int)Math.Max(0, bitmap.Height - ((anchorY + blockHeightPt) * scaleY));
                    var leftPx = (int)Math.Max(0, anchorX * scaleX);
                    topPx = (int)Math.Min(topPx, bitmap.Height - blockHeightPx);
                    leftPx = (int)Math.Min(leftPx, bitmap.Width - blockWidthPx);

                    darkness = MeasureDarkness(bitmap, leftPx, topPx, blockWidthPx, blockHeightPx);
                    return true;
                }
            }
            catch
            {
                return false;
            }
        }

        private static void EnsureLoaded()
        {
            if (_initialized)
                return;

            _initialized = true;

            try
            {
                var baseDir = AppDomain.CurrentDomain.BaseDirectory;
                var pdfiumPath = Path.Combine(baseDir, "pdfium.dll");
                if (!File.Exists(pdfiumPath))
                {
                    _loadError = "pdfium.dll fehlt im App-Ordner. Projekt neu bauen oder install-docnet.ps1 ausführen.";
                    return;
                }

                _available = true;
            }
            catch (Exception ex)
            {
                _loadError = ex.Message;
                _available = false;
            }
        }
    }
}
