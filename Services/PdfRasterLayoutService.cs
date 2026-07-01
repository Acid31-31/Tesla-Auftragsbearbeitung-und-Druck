using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
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
        private const int VisionRenderDpi = 72;
        private const int MaterialPlacementRenderDpi = 72;
        private const int ScanStepPx = 5;
        private const int MaterialCellSizePx = 8;
        private const int MaterialCoarseScanStepCells = 3;
        private const int WhiteThreshold = 242;
        private const float MaxAcceptableDarkness = 0.035f;
        private const float MaterialMaxAcceptableDarkness = 0.025f;
        private const float MaterialFallbackMaxDarkness = 0.055f;
        private const int MaxPageCacheEntries = 24;
        private const int MaxGridCacheEntries = 48;

        [ThreadStatic]
        private static Action<double> _scanProgress;

        /// <summary>0–1 Fortschritt innerhalb eines Raster-Scans (nur Worker-Thread).</summary>
        internal static Action<double> ScanProgress
        {
            get => _scanProgress;
            set => _scanProgress = value;
        }

        private static bool _initialized;
        private static bool _available;
        private static string _loadError = string.Empty;
        private static readonly Dictionary<string, Bitmap> PageCache = new Dictionary<string, Bitmap>(StringComparer.OrdinalIgnoreCase);
        private static readonly List<string> PageCacheOrder = new List<string>();
        private static readonly Dictionary<string, PageDarknessGrid> GridCache = new Dictionary<string, PageDarknessGrid>(StringComparer.OrdinalIgnoreCase);
        private static readonly List<string> GridCacheOrder = new List<string>();
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
                PageCacheOrder.Clear();
                GridCache.Clear();
                GridCacheOrder.Clear();
            }
        }

        /// <summary>Eine Rasterisierung + Integralbild pro Seite (wird gecacht).</summary>
        public static void EnsurePageGrid(string pdfPath, int pageNumber, float pageWidthPt, float pageHeightPt)
        {
            if (!_available)
                return;

            GetOrCreateDarknessGrid(pdfPath, pageNumber, pageWidthPt, pageHeightPt);
        }

        private static void RegisterPageCacheEntry(string key, Bitmap bitmap)
        {
            if (PageCache.TryGetValue(key, out var existing))
            {
                existing?.Dispose();
                PageCache.Remove(key);
            }

            PageCacheOrder.Remove(key);
            PageCache[key] = bitmap;
            PageCacheOrder.Add(key);

            while (PageCache.Count > MaxPageCacheEntries && PageCacheOrder.Count > 0)
            {
                var oldest = PageCacheOrder[0];
                PageCacheOrder.RemoveAt(0);
                if (!PageCache.TryGetValue(oldest, out var oldBitmap))
                    continue;

                PageCache.Remove(oldest);
                oldBitmap?.Dispose();
            }
        }

        private static void RegisterGridCacheEntry(string key, PageDarknessGrid grid)
        {
            GridCacheOrder.Remove(key);
            GridCache[key] = grid;
            GridCacheOrder.Add(key);

            while (GridCache.Count > MaxGridCacheEntries && GridCacheOrder.Count > 0)
            {
                var oldest = GridCacheOrder[0];
                GridCacheOrder.RemoveAt(0);
                GridCache.Remove(oldest);
            }
        }

        private sealed class PageDarknessGrid
        {
            public int CellSize;
            public int Cols;
            public int Rows;
            public int[] Sat;
            public float ScaleX;
            public float ScaleY;
            public float PageWidthPt;
            public float PageHeightPt;
            public int BitmapHeightPx;

            public float MeasureCenterBlockDarkness(float centerXPt, float anchorYPt, float blockWidthPt, float blockHeightPt)
            {
                var leftPt = centerXPt - (blockWidthPt / 2f);
                return MeasureRectDarkness(leftPt, anchorYPt, blockWidthPt, blockHeightPt);
            }

            public float MeasureAnchorBlockDarkness(float anchorX, float anchorY, float blockWidthPt, float blockHeightPt, int alignment)
            {
                var leftPt = alignment == 2
                    ? anchorX - blockWidthPt
                    : alignment == 1
                        ? anchorX - (blockWidthPt / 2f)
                        : anchorX;
                return MeasureRectDarkness(leftPt, anchorY, blockWidthPt, blockHeightPt);
            }

            private float MeasureRectDarkness(float leftPt, float bottomPt, float blockWidthPt, float blockHeightPt)
            {
                var topPt = bottomPt + blockHeightPt;
                var leftPx = leftPt * ScaleX;
                var rightPx = leftPx + (blockWidthPt * ScaleX);
                var topPx = BitmapHeightPx - (topPt * ScaleY);
                var bottomPx = BitmapHeightPx - (bottomPt * ScaleY);

                var col0 = Math.Max(0, (int)(leftPx / CellSize));
                var col1 = Math.Min(Cols, (int)Math.Ceiling(rightPx / CellSize));
                var row0 = Math.Max(0, (int)(topPx / CellSize));
                var row1 = Math.Min(Rows, (int)Math.Ceiling(bottomPx / CellSize));
                if (col1 <= col0 || row1 <= row0)
                    return 1f;

                var darkCells = SumRect(col0, row0, col1, row1);
                var totalCells = (col1 - col0) * (row1 - row0);
                return totalCells == 0 ? 1f : darkCells / (float)totalCells;
            }

            private int SumRect(int col0, int row0, int col1, int row1)
            {
                return Sat[(row1 * (Cols + 1)) + col1]
                       - Sat[(row0 * (Cols + 1)) + col1]
                       - Sat[(row1 * (Cols + 1)) + col0]
                       + Sat[(row0 * (Cols + 1)) + col0];
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
                var grid = GetOrCreateDarknessGrid(pdfPath, pageNumber, pageWidthPt, pageHeightPt);
                if (grid == null)
                    return false;

                var maxAnchorX = Math.Max(zoneLeftPt, zoneRightPt - blockWidthPt);
                if (maxAnchorX < zoneLeftPt)
                    return false;

                var maxAnchorY = Math.Max(zoneBottomPt, zoneTopPt - blockHeightPt);
                if (maxAnchorY < zoneBottomPt)
                    return false;

                var quickCandidates = new[]
                {
                    (preferredXPt, preferredYPt),
                    (zoneLeftPt, preferredYPt),
                    (maxAnchorX, preferredYPt),
                    (preferredXPt, zoneBottomPt),
                    (preferredXPt, maxAnchorY),
                    ((zoneLeftPt + maxAnchorX) / 2f, preferredYPt)
                };

                var stepPt = Math.Max(16f, blockWidthPt / 2f);
                var bestScore = float.MaxValue;
                var found = false;

                foreach (var candidate in quickCandidates)
                {
                    var x = Math.Max(zoneLeftPt, Math.Min(maxAnchorX, candidate.Item1));
                    var y = Math.Max(zoneBottomPt, Math.Min(maxAnchorY, candidate.Item2));
                    if (!IsGridAnchorAcceptable(grid, x, y, blockWidthPt, blockHeightPt, preferredXPt, preferredYPt, pageWidthPt, pageHeightPt, out var quickScore))
                        continue;

                    if (quickScore >= bestScore)
                        continue;

                    bestScore = quickScore;
                    anchorX = x;
                    anchorY = y;
                    found = true;
                }

                if (found)
                    return true;

                bestScore = float.MaxValue;
                found = false;
                var scanHeight = Math.Max(1f, maxAnchorY - zoneBottomPt);
                var rowIndex = 0;

                for (var y = zoneBottomPt; y <= maxAnchorY; y += stepPt)
                {
                    if (rowIndex % 3 == 0)
                        _scanProgress?.Invoke((y - zoneBottomPt) / scanHeight);
                    rowIndex++;

                    for (var x = zoneLeftPt; x <= maxAnchorX; x += stepPt)
                    {
                        if (!IsGridAnchorAcceptable(grid, x, y, blockWidthPt, blockHeightPt, preferredXPt, preferredYPt, pageWidthPt, pageHeightPt, out var score))
                            continue;

                        if (score >= bestScore)
                            continue;

                        bestScore = score;
                        anchorX = x;
                        anchorY = y;
                        found = true;
                    }
                }

                return found;
            }
            catch
            {
                return false;
            }
        }

        private static bool IsGridAnchorAcceptable(
            PageDarknessGrid grid,
            float anchorX,
            float anchorY,
            float blockWidthPt,
            float blockHeightPt,
            float preferredXPt,
            float preferredYPt,
            float pageWidthPt,
            float pageHeightPt,
            out float score)
        {
            var darkness = grid.MeasureAnchorBlockDarkness(anchorX, anchorY, blockWidthPt, blockHeightPt, 0);
            if (darkness > MaxAcceptableDarkness)
            {
                score = float.MaxValue;
                return false;
            }

            score = (darkness * 25f) + DistancePenalty(anchorX, anchorY, preferredXPt, preferredYPt, pageWidthPt, pageHeightPt);
            return true;
        }

        /// <summary>
        /// Schnelle Material-Platzierung: eine Rasterisierung pro Seite, Integralbild, früher Abbruch.
        /// </summary>
        public static bool TryFindMaterialCenterAnchor(
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
            IList<(float CenterX, float AnchorY)> preferredPoints,
            out float centerX,
            out float anchorY)
        {
            centerX = 0f;
            anchorY = 0f;

            if (!_available || string.IsNullOrWhiteSpace(pdfPath) || !File.Exists(pdfPath))
                return false;

            try
            {
                var grid = GetOrCreateDarknessGrid(pdfPath, pageNumber, pageWidthPt, pageHeightPt);
                if (grid == null)
                    return false;

                var minCenterPt = zoneLeftPt + (blockWidthPt / 2f) + 6f;
                var maxCenterPt = zoneRightPt - (blockWidthPt / 2f) - 6f;
                if (maxCenterPt <= minCenterPt)
                    return false;

                var defaultCenterX = (zoneLeftPt + zoneRightPt) / 2f;
                var defaultAnchorY = ((zoneBottomPt + zoneTopPt) / 2f) - (blockHeightPt / 2f);
                var bestCenterX = defaultCenterX;
                var bestAnchorY = defaultAnchorY;

                var bestScore = float.MaxValue;
                var bestDarkness = 1f;
                var foundAcceptable = false;
                var prefX = preferredPoints != null && preferredPoints.Count > 0
                    ? preferredPoints[0].CenterX
                    : defaultCenterX;
                var prefY = preferredPoints != null && preferredPoints.Count > 0
                    ? preferredPoints[0].AnchorY
                    : defaultAnchorY;

                for (var pass = 0; pass < 2; pass++)
                {
                    var quickExit = pass == 0;
                    if (quickExit)
                    {
                        var quick = new List<(float X, float Y)>();
                        if (preferredPoints != null)
                        {
                            foreach (var point in preferredPoints)
                                quick.Add((point.CenterX, point.AnchorY));
                        }

                        quick.Add((defaultCenterX, defaultAnchorY));

                        foreach (var candidate in quick)
                        {
                            TryScoreMaterialCandidate(
                                candidate.X,
                                candidate.Y,
                                grid,
                                blockWidthPt,
                                blockHeightPt,
                                minCenterPt,
                                maxCenterPt,
                                zoneBottomPt,
                                zoneTopPt,
                                prefX,
                                prefY,
                                pageWidthPt,
                                pageHeightPt,
                                true,
                                ref bestScore,
                                ref bestDarkness,
                                ref foundAcceptable,
                                ref bestCenterX,
                                ref bestAnchorY,
                                out var earlyExit);

                            if (earlyExit)
                            {
                                centerX = bestCenterX;
                                anchorY = bestAnchorY;
                                return true;
                            }
                        }
                    }
                    else
                    {
                        var blockWidthPx = Math.Max(16f, blockWidthPt * grid.ScaleX);
                        var blockHeightPx = Math.Max(16f, blockHeightPt * grid.ScaleY);
                        var scanTop = grid.BitmapHeightPx - (zoneTopPt * grid.ScaleY);
                        var scanBottom = grid.BitmapHeightPx - (zoneBottomPt * grid.ScaleY);
                        var scanLeft = minCenterPt * grid.ScaleX;
                        var scanRight = maxCenterPt * grid.ScaleX;
                        var stepPx = Math.Max(MaterialCellSizePx * MaterialCoarseScanStepCells, blockWidthPx / 4f);

                        for (var yPx = scanTop; yPx <= scanBottom - blockHeightPx; yPx += stepPx)
                        {
                            for (var centerPx = scanLeft; centerPx <= scanRight; centerPx += stepPx)
                            {
                                TryScoreMaterialCandidate(
                                    centerPx / grid.ScaleX,
                                    pageHeightPt - ((yPx + blockHeightPx) / grid.ScaleY),
                                    grid,
                                    blockWidthPt,
                                    blockHeightPt,
                                    minCenterPt,
                                    maxCenterPt,
                                    zoneBottomPt,
                                    zoneTopPt,
                                    prefX,
                                    prefY,
                                    pageWidthPt,
                                    pageHeightPt,
                                    false,
                                    ref bestScore,
                                    ref bestDarkness,
                                    ref foundAcceptable,
                                    ref bestCenterX,
                                    ref bestAnchorY,
                                    out _);
                            }
                        }
                    }
                }

                if (bestScore == float.MaxValue)
                    return false;

                centerX = bestCenterX;
                anchorY = bestAnchorY;

                if (foundAcceptable)
                    return true;

                return bestDarkness <= MaterialFallbackMaxDarkness;
            }
            catch
            {
                return false;
            }
        }

        private static bool TryScoreMaterialCandidate(
            float candidateX,
            float candidateY,
            PageDarknessGrid grid,
            float blockWidthPt,
            float blockHeightPt,
            float minCenterPt,
            float maxCenterPt,
            float zoneBottomPt,
            float zoneTopPt,
            float prefX,
            float prefY,
            float pageWidthPt,
            float pageHeightPt,
            bool quickExit,
            ref float bestScore,
            ref float bestDarkness,
            ref bool foundAcceptable,
            ref float bestCenterX,
            ref float bestAnchorY,
            out bool earlyExit)
        {
            earlyExit = false;

            if (candidateX < minCenterPt || candidateX > maxCenterPt)
                return false;
            if (candidateY < zoneBottomPt || candidateY + blockHeightPt > zoneTopPt)
                return false;

            var darkness = grid.MeasureCenterBlockDarkness(
                candidateX,
                candidateY,
                blockWidthPt,
                blockHeightPt);

            var score = (darkness * 60f)
                        + DistancePenalty(
                            candidateX,
                            candidateY,
                            prefX,
                            prefY,
                            pageWidthPt,
                            pageHeightPt);

            if (darkness <= MaterialMaxAcceptableDarkness)
            {
                foundAcceptable = true;
                if (quickExit && score < 0.35f)
                {
                    bestCenterX = candidateX;
                    bestAnchorY = candidateY;
                    earlyExit = true;
                    return true;
                }
            }

            if (score < bestScore)
            {
                bestScore = score;
                bestDarkness = darkness;
                bestCenterX = candidateX;
                bestAnchorY = candidateY;
            }

            return true;
        }

        /// <summary>
        /// Findet freie Fläche für zentriertes Material-Wasserzeichen anhand der gerenderten Seite.
        /// </summary>
        public static bool TryFindFreeCenterAnchor(
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
            float preferredCenterXPt,
            float preferredAnchorYPt,
            out float centerX,
            out float anchorY)
        {
            return TryFindMaterialCenterAnchor(
                pdfPath,
                pageNumber,
                blockWidthPt,
                blockHeightPt,
                pageWidthPt,
                pageHeightPt,
                zoneLeftPt,
                zoneRightPt,
                zoneBottomPt,
                zoneTopPt,
                new List<(float, float)> { (preferredCenterXPt, preferredAnchorYPt) },
                out centerX,
                out anchorY);
        }

        private static PageDarknessGrid GetOrCreateDarknessGrid(
            string pdfPath,
            int pageNumber,
            float pageWidthPt,
            float pageHeightPt)
        {
            var key = pdfPath + "|" + pageNumber + "|" + MaterialPlacementRenderDpi + "|grid|"
                      + File.GetLastWriteTimeUtc(pdfPath).Ticks;

            lock (CacheLock)
            {
                PageDarknessGrid cached;
                if (GridCache.TryGetValue(key, out cached))
                    return cached;
            }

            using (var bitmap = GetOrRenderPage(pdfPath, pageNumber, MaterialPlacementRenderDpi))
            {
                if (bitmap == null)
                    return null;

                var grid = BuildDarknessGrid(bitmap, pageWidthPt, pageHeightPt);
                if (grid == null)
                    return null;

                lock (CacheLock)
                    RegisterGridCacheEntry(key, grid);

                return grid;
            }
        }

        private static PageDarknessGrid BuildDarknessGrid(Bitmap bitmap, float pageWidthPt, float pageHeightPt)
        {
            var cellSize = MaterialCellSizePx;
            var cols = Math.Max(1, (bitmap.Width + cellSize - 1) / cellSize);
            var rows = Math.Max(1, (bitmap.Height + cellSize - 1) / cellSize);
            var dark = new int[cols * rows];
            var rect = new Rectangle(0, 0, bitmap.Width, bitmap.Height);
            var data = bitmap.LockBits(rect, ImageLockMode.ReadOnly, bitmap.PixelFormat);

            try
            {
                var stride = data.Stride;
                var bytes = new byte[stride * bitmap.Height];
                Marshal.Copy(data.Scan0, bytes, 0, bytes.Length);
                var bytesPerPixel = Image.GetPixelFormatSize(bitmap.PixelFormat) / 8;
                if (bytesPerPixel < 3)
                    return null;

                for (var row = 0; row < rows; row++)
                {
                    var sampleY = Math.Min(bitmap.Height - 1, (row * cellSize) + (cellSize / 2));
                    var rowOffset = sampleY * stride;

                    for (var col = 0; col < cols; col++)
                    {
                        var sampleX = Math.Min(bitmap.Width - 1, (col * cellSize) + (cellSize / 2));
                        var idx = rowOffset + (sampleX * bytesPerPixel);
                        if (idx + 2 >= bytes.Length)
                            continue;

                        var luminance = (bytes[idx + 2] + bytes[idx + 1] + bytes[idx]) / 3;
                        if (luminance < WhiteThreshold)
                            dark[(row * cols) + col] = 1;
                    }
                }
            }
            finally
            {
                bitmap.UnlockBits(data);
            }

            var sat = new int[(cols + 1) * (rows + 1)];
            for (var row = 0; row < rows; row++)
            {
                var rowSum = 0;
                for (var col = 0; col < cols; col++)
                {
                    rowSum += dark[(row * cols) + col];
                    var satIdx = ((row + 1) * (cols + 1)) + (col + 1);
                    sat[satIdx] = sat[(row * (cols + 1)) + (col + 1)] + rowSum;
                }
            }

            return new PageDarknessGrid
            {
                CellSize = cellSize,
                Cols = cols,
                Rows = rows,
                Sat = sat,
                ScaleX = bitmap.Width / pageWidthPt,
                ScaleY = bitmap.Height / pageHeightPt,
                PageWidthPt = pageWidthPt,
                PageHeightPt = pageHeightPt,
                BitmapHeightPx = bitmap.Height
            };
        }

        public static float MeasurePlacementDarkness(
            string pdfPath,
            int pageNumber,
            float anchorX,
            float anchorY,
            float blockWidthPt,
            float blockHeightPt,
            float pageWidthPt,
            float pageHeightPt,
            int alignment)
        {
            EnsureLoaded();
            if (!_available || string.IsNullOrWhiteSpace(pdfPath) || !File.Exists(pdfPath))
                return 1f;

            var left = alignment == 2
                ? anchorX - blockWidthPt
                : alignment == 1
                    ? anchorX - (blockWidthPt / 2f)
                    : anchorX;

            try
            {
                var grid = GetOrCreateDarknessGrid(pdfPath, pageNumber, pageWidthPt, pageHeightPt);
                if (grid != null)
                    return grid.MeasureAnchorBlockDarkness(anchorX, anchorY, blockWidthPt, blockHeightPt, alignment);

                using (var bitmap = GetOrRenderPage(pdfPath, pageNumber))
                {
                    if (bitmap == null)
                        return 1f;

                    var scaleX = bitmap.Width / pageWidthPt;
                    var scaleY = bitmap.Height / pageHeightPt;
                    var blockWidthPx = Math.Max(12, (int)Math.Ceiling(blockWidthPt * scaleX));
                    var blockHeightPx = Math.Max(12, (int)Math.Ceiling(blockHeightPt * scaleY));
                    var leftPx = (int)(left * scaleX);
                    var topPx = (int)(bitmap.Height - ((anchorY + blockHeightPt) * scaleY));
                    return MeasureDarkness(bitmap, leftPx, topPx, blockWidthPx, blockHeightPx);
                }
            }
            catch
            {
                return 1f;
            }
        }

        public static bool IsPlacementAcceptable(
            string pdfPath,
            int pageNumber,
            float anchorX,
            float anchorY,
            float blockWidthPt,
            float blockHeightPt,
            float pageWidthPt,
            float pageHeightPt,
            int alignment,
            bool isMaterial = false)
        {
            var maxDark = isMaterial ? MaterialMaxAcceptableDarkness : MaxAcceptableDarkness;
            return MeasurePlacementDarkness(
                       pdfPath,
                       pageNumber,
                       anchorX,
                       anchorY,
                       blockWidthPt,
                       blockHeightPt,
                       pageWidthPt,
                       pageHeightPt,
                       alignment) <= maxDark;
        }

        private static float DistancePenalty(float anchorX, float anchorY, float targetX, float targetY, float pageWidth, float pageHeight)
        {
            var dx = (anchorX - targetX) / Math.Max(120f, pageWidth * 0.35f);
            var dy = (anchorY - targetY) / Math.Max(80f, pageHeight * 0.18f);
            return (dx * dx) + (dy * dy * 2.5f);
        }

        private static float MeasureDarkness(Bitmap bitmap, int left, int top, int width, int height, int paddingPx = 0)
        {
            long dark = 0;
            long total = 0;
            var rect = new Rectangle(left - paddingPx, top - paddingPx, width + (paddingPx * 2), height + (paddingPx * 2));
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
            return GetOrRenderPage(pdfPath, pageNumber, RenderDpi);
        }

        private static Bitmap GetOrRenderPageForThickness(string pdfPath, int pageNumber, int scaleDenominator)
        {
            return GetOrRenderPage(pdfPath, pageNumber, GetThicknessRenderDpi(scaleDenominator));
        }

        internal static int GetThicknessRenderDpi(int scaleDenominator)
        {
            var scale = Math.Max(1, scaleDenominator);
            return Math.Min(240, 110 + (scale * 12));
        }

        private static Bitmap GetOrRenderPage(string pdfPath, int pageNumber, int renderDpi)
        {
            var key = pdfPath + "|" + pageNumber + "|" + renderDpi + "|" + File.GetLastWriteTimeUtc(pdfPath).Ticks;
            lock (CacheLock)
            {
                Bitmap cached;
                if (PageCache.TryGetValue(key, out cached))
                    return (Bitmap)cached.Clone();

                var rendered = RenderPage(pdfPath, pageNumber, renderDpi);
                if (rendered == null)
                    return null;

                var stored = (Bitmap)rendered.Clone();
                RegisterPageCacheEntry(key, stored);
                return rendered;
            }
        }

        private static Bitmap RenderPage(string pdfPath, int pageNumber)
        {
            return RenderPage(pdfPath, pageNumber, RenderDpi);
        }

        private static Bitmap RenderPage(string pdfPath, int pageNumber, int renderDpi)
        {
            if (!_available)
                return null;

            var pageIndex = Math.Max(0, pageNumber - 1);
            var scalingFactor = renderDpi / 72.0;

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
            out float darkness,
            int paddingPx = 0)
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

                    darkness = MeasureDarkness(bitmap, leftPx, topPx, blockWidthPx, blockHeightPx, paddingPx);
                    return true;
                }
            }
            catch
            {
                return false;
            }
        }

        public static Bitmap RenderPageForThickness(string pdfPath, int pageNumber, int scaleDenominator)
        {
            EnsureLoaded();
            if (!_available)
                return null;

            return GetOrRenderPageForThickness(pdfPath, pageNumber, scaleDenominator);
        }

        public static Bitmap RenderPageForVision(string pdfPath, int pageNumber)
        {
            EnsureLoaded();
            if (!_available)
                return null;

            return GetOrRenderPage(pdfPath, pageNumber, VisionRenderDpi);
        }

        public static Bitmap RenderPageForPreview(string pdfPath, int pageNumber)
        {
            EnsureLoaded();
            if (!_available)
                return null;

            return GetOrRenderPage(pdfPath, pageNumber, 110);
        }

        public static Bitmap RenderPageForPrint(string pdfPath, int pageNumber)
        {
            EnsureLoaded();
            if (!_available)
                return null;

            return GetOrRenderPage(pdfPath, pageNumber, 150);
        }

        public static bool TryDetectSideProfileThicknessMm(
            string pdfPath,
            int pageNumber,
            float pageWidthPt,
            float pageHeightPt,
            int scaleDenominator,
            out float thicknessMm)
        {
            thicknessMm = 0f;
            if (!_available || string.IsNullOrWhiteSpace(pdfPath) || !File.Exists(pdfPath))
                return false;

            try
            {
                using (var bitmap = GetOrRenderPageForThickness(pdfPath, pageNumber, scaleDenominator))
                {
                    if (bitmap == null)
                        return false;

                    var regions = new[]
                    {
                        new ScanRegion(0.52f, 0.84f, 0.28f, 0.66f),
                        new ScanRegion(0.32f, 0.95f, 0.18f, 0.78f),
                        new ScanRegion(0.40f, 0.92f, 0.22f, 0.72f),
                        new ScanRegion(0.28f, 0.88f, 0.15f, 0.82f)
                    };

                    var scales = new[] { scaleDenominator, 1 };
                    float bestThickness = 0f;
                    var found = false;

                    foreach (var region in regions)
                    {
                        if (!TryMeasureSideProfileInRegion(bitmap, pageWidthPt, region, scaleDenominator, out var widthPx))
                            continue;

                        foreach (var scale in scales)
                        {
                            var candidate = ConvertProfileWidthToThicknessMm(widthPx, bitmap.Width, pageWidthPt, scale);
                            if (candidate < 0.4f || candidate > 40f)
                                continue;

                            if (!found || IsBetterThicknessCandidate(candidate, bestThickness))
                            {
                                bestThickness = candidate;
                                found = true;
                            }
                        }

                        if (found)
                            break;
                    }

                    if (!found)
                        return false;

                    thicknessMm = bestThickness;
                    return true;
                }
            }
            catch
            {
                return false;
            }
        }

        private sealed class ScanRegion
        {
            public ScanRegion(float x0Rel, float x1Rel, float y0Rel, float y1Rel)
            {
                X0Rel = x0Rel;
                X1Rel = x1Rel;
                Y0Rel = y0Rel;
                Y1Rel = y1Rel;
            }

            public float X0Rel { get; }
            public float X1Rel { get; }
            public float Y0Rel { get; }
            public float Y1Rel { get; }
        }

        private static bool TryMeasureSideProfileInRegion(Bitmap bitmap, float pageWidthPt, ScanRegion region, int scaleDenominator, out int bestWidthPx)
        {
            bestWidthPx = 0;
            var x0 = (int)(bitmap.Width * region.X0Rel);
            var x1 = (int)(bitmap.Width * region.X1Rel);
            var y0 = (int)(bitmap.Height * region.Y0Rel);
            var y1 = (int)(bitmap.Height * region.Y1Rel);
            if (x1 <= x0 || y1 <= y0)
                return false;

            var regionHeight = y1 - y0;
            var columnCount = x1 - x0;
            var columnDark = new int[columnCount];

            for (var xi = 0; xi < columnCount; xi++)
            {
                var x = x0 + xi;
                var count = 0;
                for (var y = y0; y < y1; y++)
                {
                    if (IsDarkPixel(bitmap, x, y))
                        count++;
                }

                columnDark[xi] = count;
            }

            var threshold = Math.Max(4, regionHeight / 16);
            var scaleFactor = Math.Max(1, scaleDenominator);
            var minWidthPx = Math.Max(2, (int)(bitmap.Width * 0.0025f / scaleFactor));
            var maxWidthPx = Math.Max(90, (int)(bitmap.Width * 0.14f));
            var bestScore = float.MaxValue;

            for (var start = 0; start < columnCount; start++)
            {
                if (columnDark[start] < threshold)
                    continue;

                var end = start;
                while (end + 1 < columnCount && columnDark[end + 1] >= threshold / 2)
                    end++;

                var widthPx = end - start + 1;
                if (widthPx < minWidthPx || widthPx > maxWidthPx)
                {
                    start = end;
                    continue;
                }

                var avgDark = 0;
                for (var i = start; i <= end; i++)
                    avgDark += columnDark[i];
                avgDark /= Math.Max(1, widthPx);

                var score = (widthPx * 12f) - (avgDark * 1.5f);
                if (score < bestScore)
                {
                    bestScore = score;
                    bestWidthPx = widthPx;
                }

                start = end;
            }

            return bestWidthPx > 0;
        }

        private static float ConvertProfileWidthToThicknessMm(int widthPx, int bitmapWidthPx, float pageWidthPt, int scaleDenominator)
        {
            var widthPt = (widthPx / (float)bitmapWidthPx) * pageWidthPt;
            return (float)(widthPt * (25.4 / 72.0) * Math.Max(1, scaleDenominator));
        }

        private static bool IsBetterThicknessCandidate(float candidate, float current)
        {
            if (current <= 0f)
                return true;

            var preferredBand = candidate >= 0.8f && candidate <= 12f;
            var currentInBand = current >= 0.8f && current <= 12f;
            if (preferredBand && !currentInBand)
                return true;
            if (!preferredBand && currentInBand)
                return false;

            var commonThicknesses = new[] { 1f, 1.5f, 2f, 2.5f, 3f, 4f, 5f, 6f, 8f, 10f };
            var candidateDist = commonThicknesses.Min(t => Math.Abs(candidate - t));
            var currentDist = commonThicknesses.Min(t => Math.Abs(current - t));
            return candidateDist < currentDist;
        }

        private static bool IsDarkPixel(Bitmap bitmap, int x, int y)
        {
            if (x < 0 || y < 0 || x >= bitmap.Width || y >= bitmap.Height)
                return false;

            var pixel = bitmap.GetPixel(x, y);
            var luminance = (pixel.R + pixel.G + pixel.B) / 3;
            return luminance < 200;
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
