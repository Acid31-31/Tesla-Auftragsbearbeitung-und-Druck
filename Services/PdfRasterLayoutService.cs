using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Security;
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
        private const int VisionRenderDpi = 132;
        private const int MaterialPlacementRenderDpi = 120;
        private const int MaterialVerifyRenderDpi = 120;
        private const int MaterialCellSizePx = 6;
        private const int WhiteThreshold = 248;
        private const float MaxAcceptableDarkness = 0.035f;
        private const float MaterialMaxAcceptableDarkness = 0.003f;
        private const float MaterialGridMaxDarkness = 0.008f;
        public const float MaterialPreferredMaxDarkness = 0.15f;
        public const float MaterialFallbackMaxDarkness = 0.55f;
        private const int MaterialPlacementVisionDpi = 150;
        private const float MaterialWhitespaceStepPt = 10f;
        private const int MaterialWhitespaceSeedCount = 32;
        private const float MaterialCandidateStepPt = 14f;
        private const int MaterialCandidateMaxPerZone = 96;
        private const float MaterialMarginMaxDarkness = 0.035f;
        private const int ScanStepPx = 5;
        private const int MaterialCoarseScanStepCells = 2;
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
        private static readonly Dictionary<string, FrameBoundsCacheEntry> FrameBoundsCache =
            new Dictionary<string, FrameBoundsCacheEntry>(StringComparer.OrdinalIgnoreCase);
        private static readonly object CacheLock = new object();

        private sealed class FrameBoundsCacheEntry
        {
            public bool Ok;
            public float Left;
            public float Right;
            public float Top;
        }

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

        public static string GetUnavailableUserMessage()
        {
            EnsureLoaded();
            if (_available)
                return string.Empty;

            if (!string.IsNullOrWhiteSpace(_loadError)
                && (_loadError.IndexOf("32-Bit", StringComparison.OrdinalIgnoreCase) >= 0
                    || _loadError.IndexOf("64-Bit", StringComparison.OrdinalIgnoreCase) >= 0
                    || _loadError.IndexOf("nicht 64", StringComparison.OrdinalIgnoreCase) >= 0
                    || _loadError.IndexOf("nicht 32", StringComparison.OrdinalIgnoreCase) >= 0))
            {
                return NativeDependencyService.FormatBadImageMessage();
            }

            return NativeDependencyService.FormatMissingPdfiumMessage(_loadError);
        }

        internal static void ResetAvailability()
        {
            _initialized = false;
            _available = false;
            _loadError = string.Empty;
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
                FrameBoundsCache.Clear();
            }
        }

        /// <summary>Speicher nach einzelnen Batch-PDFs freigeben.</summary>
        public static void TrimCache(int keepPageEntries = 2, int keepGridEntries = 4)
        {
            lock (CacheLock)
            {
                while (PageCache.Count > keepPageEntries && PageCacheOrder.Count > 0)
                {
                    var oldest = PageCacheOrder[0];
                    PageCacheOrder.RemoveAt(0);
                    if (!PageCache.TryGetValue(oldest, out var oldBitmap))
                        continue;

                    PageCache.Remove(oldest);
                    oldBitmap?.Dispose();
                }

                while (GridCache.Count > keepGridEntries && GridCacheOrder.Count > 0)
                {
                    var oldest = GridCacheOrder[0];
                    GridCacheOrder.RemoveAt(0);
                    GridCache.Remove(oldest);
                }

                if (FrameBoundsCache.Count > keepGridEntries + 4)
                    FrameBoundsCache.Clear();
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

                var bottomPt = anchorY;
                if (alignment == 1)
                {
                    PdfRasterLayoutService.ResolveBlockGeometry(
                        anchorX,
                        anchorY,
                        blockWidthPt,
                        blockHeightPt,
                        alignment,
                        materialYIsBottomAnchor: false,
                        out _,
                        out bottomPt);
                }

                return MeasureRectDarkness(leftPt, bottomPt, blockWidthPt, blockHeightPt);
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

            public float MeasureCenterBlockDarknessRobust(
                float centerXPt,
                float anchorYPt,
                float blockWidthPt,
                float blockHeightPt)
            {
                var leftPt = centerXPt - (blockWidthPt / 2f);
                return MeasureRectDarknessRobust(leftPt, anchorYPt, blockWidthPt, blockHeightPt);
            }

            public float MeasureCenterBlockPeakDarkness(
                float centerXPt,
                float anchorYPt,
                float blockWidthPt,
                float blockHeightPt)
            {
                var leftPt = centerXPt - (blockWidthPt / 2f);
                return MeasureRectPeakDarkness(leftPt, anchorYPt, blockWidthPt, blockHeightPt);
            }

            private float MeasureRectPeakDarkness(float leftPt, float bottomPt, float blockWidthPt, float blockHeightPt)
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

                var peak = 0f;
                for (var row = row0; row < row1; row++)
                {
                    for (var col = col0; col < col1; col++)
                    {
                        var cell = CellMedianNeighborhood(col, row);
                        if (cell > peak)
                            peak = cell;
                    }
                }

                return peak;
            }

            private float MeasureRectDarknessRobust(float leftPt, float bottomPt, float blockWidthPt, float blockHeightPt)
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

                var scores = new List<float>((col1 - col0) * (row1 - row0));
                for (var row = row0; row < row1; row++)
                {
                    for (var col = col0; col < col1; col++)
                        scores.Add(CellMedianNeighborhood(col, row));
                }

                if (scores.Count == 0)
                    return 1f;

                scores.Sort();
                var idx = (int)Math.Ceiling(scores.Count * 0.75d) - 1;
                if (idx < 0)
                    idx = 0;
                if (idx >= scores.Count)
                    idx = scores.Count - 1;
                return scores[idx];
            }

            private float CellMedianNeighborhood(int col, int row)
            {
                var values = new float[9];
                var index = 0;
                for (var dr = -1; dr <= 1; dr++)
                {
                    for (var dc = -1; dc <= 1; dc++)
                    {
                        var sampleCol = col + dc;
                        var sampleRow = row + dr;
                        values[index++] = IsCellDark(sampleCol, sampleRow) ? 1f : 0f;
                    }
                }

                Array.Sort(values);
                return values[4];
            }

            private bool IsCellDark(int col, int row)
            {
                if (col < 0 || row < 0 || col >= Cols || row >= Rows)
                    return false;

                return SumRect(col, row, col + 1, row + 1) > 0;
            }

            private int SumRect(int col0, int row0, int col1, int row1)
            {
                return Sat[(row1 * (Cols + 1)) + col1]
                       - Sat[(row0 * (Cols + 1)) + col1]
                       - Sat[(row1 * (Cols + 1)) + col0]
                       + Sat[(row0 * (Cols + 1)) + col0];
            }

            public bool TryFindBestFreeCenterSlot(
                float blockWidthPt,
                float blockHeightPt,
                float marginPt,
                float titleBlockLeftPt,
                float titleBlockTopPt,
                out float centerX,
                out float anchorBottomY,
                out float darkness)
            {
                centerX = 0f;
                anchorBottomY = 0f;
                darkness = 1f;

                var zoneLeft = marginPt;
                var zoneRight = PageWidthPt - marginPt;
                var zoneBottom = marginPt;
                var zoneTop = PageHeightPt - marginPt;

                var blockWCols = Math.Max(1, (int)Math.Ceiling((blockWidthPt * ScaleX) / CellSize));
                var blockHRows = Math.Max(1, (int)Math.Ceiling((blockHeightPt * ScaleY) / CellSize));

                var colStart = Math.Max(0, (int)Math.Floor((zoneLeft * ScaleX) / CellSize));
                var colEnd = Math.Min(Cols - blockWCols, (int)Math.Ceiling((zoneRight * ScaleX) / CellSize) - blockWCols);
                var rowStart = Math.Max(0, (int)Math.Floor((BitmapHeightPx - (zoneTop * ScaleY)) / CellSize));
                var rowEnd = Math.Min(Rows - blockHRows, (int)Math.Ceiling((BitmapHeightPx - (zoneBottom * ScaleY)) / CellSize) - blockHRows);

                if (colEnd < colStart || rowEnd < rowStart)
                    return false;

                var bestDark = 1f;
                var bestCol = colStart;
                var bestRow = rowStart;
                var bestBottomPt = float.MaxValue;
                const int step = 2;
                var topBandLimitPt = PageHeightPt * 0.82f;

                for (var row = rowStart; row <= rowEnd; row += step)
                {
                    for (var col = colStart; col <= colEnd; col += step)
                    {
                        ConsiderSlot(
                            col,
                            row,
                            blockWCols,
                            blockHRows,
                            blockWidthPt,
                            blockHeightPt,
                            titleBlockLeftPt,
                            titleBlockTopPt,
                            topBandLimitPt,
                            ref bestDark,
                            ref bestCol,
                            ref bestRow,
                            ref bestBottomPt);
                    }
                }

                RefineBestSlot(
                    bestCol,
                    bestRow,
                    colStart,
                    colEnd,
                    rowStart,
                    rowEnd,
                    blockWCols,
                    blockHRows,
                    blockWidthPt,
                    blockHeightPt,
                    titleBlockLeftPt,
                    titleBlockTopPt,
                    topBandLimitPt,
                    ref bestDark,
                    ref bestCol,
                    ref bestRow,
                    ref bestBottomPt);

                if (bestDark > MaterialPreferredMaxDarkness)
                    return false;

                centerX = ((bestCol + (blockWCols / 2f)) * CellSize) / ScaleX;
                var anchorTopPt = (BitmapHeightPx - (bestRow * CellSize)) / ScaleY;
                anchorBottomY = anchorTopPt - blockHeightPt;
                darkness = bestDark;
                return true;
            }

            private void ConsiderSlot(
                int col,
                int row,
                int blockWCols,
                int blockHRows,
                float blockWidthPt,
                float blockHeightPt,
                float titleBlockLeftPt,
                float titleBlockTopPt,
                float topBandLimitPt,
                ref float bestDark,
                ref int bestCol,
                ref int bestRow,
                ref float bestBottomPt)
            {
                var centerXPt = ((col + (blockWCols / 2f)) * CellSize) / ScaleX;
                var topPt = (BitmapHeightPx - (row * CellSize)) / ScaleY;
                var bottomPt = topPt - blockHeightPt;

                if (topPt > topBandLimitPt)
                    return;

                if (PdfRasterLayoutService.IsInsideTitleBlock(
                        centerXPt,
                        bottomPt,
                        blockWidthPt,
                        blockHeightPt,
                        PageWidthPt,
                        PageHeightPt,
                        titleBlockLeftPt,
                        titleBlockTopPt))
                {
                    return;
                }

                if (PdfRasterLayoutService.IsInsideDrawingCore(
                        centerXPt,
                        bottomPt,
                        blockWidthPt,
                        blockHeightPt,
                        PageWidthPt,
                        PageHeightPt,
                        1,
                        materialYIsBottomAnchor: true))
                {
                    return;
                }

                if (PdfRasterLayoutService.IsInMaterialLeftDimensionStrip(
                        centerXPt,
                        bottomPt,
                        blockWidthPt,
                        blockHeightPt,
                        PageWidthPt,
                        PageHeightPt,
                        1,
                        materialYIsBottomAnchor: true))
                {
                    return;
                }

                var darkCells = SumRect(col, row, col + blockWCols, row + blockHRows);
                var totalCells = blockWCols * blockHRows;
                var dark = totalCells == 0 ? 1f : darkCells / (float)totalCells;

                if (dark < bestDark - 0.002f)
                {
                    bestDark = dark;
                    bestCol = col;
                    bestRow = row;
                    bestBottomPt = bottomPt;
                }
            }

            private void RefineBestSlot(
                int bestCol,
                int bestRow,
                int colStart,
                int colEnd,
                int rowStart,
                int rowEnd,
                int blockWCols,
                int blockHRows,
                float blockWidthPt,
                float blockHeightPt,
                float titleBlockLeftPt,
                float titleBlockTopPt,
                float topBandLimitPt,
                ref float bestDark,
                ref int refinedCol,
                ref int refinedRow,
                ref float bestBottomPt)
            {
                for (var row = Math.Max(rowStart, bestRow - 2); row <= Math.Min(rowEnd, bestRow + 2); row++)
                {
                    for (var col = Math.Max(colStart, bestCol - 2); col <= Math.Min(colEnd, bestCol + 2); col++)
                    {
                        ConsiderSlot(
                            col,
                            row,
                            blockWCols,
                            blockHRows,
                            blockWidthPt,
                            blockHeightPt,
                            titleBlockLeftPt,
                            titleBlockTopPt,
                            topBandLimitPt,
                            ref bestDark,
                            ref refinedCol,
                            ref refinedRow,
                            ref bestBottomPt);
                    }
                }
            }

            public bool TryFindBestMaterialWhitespace(
                string pdfPath,
                int pageNumber,
                float blockWidthPt,
                float blockHeightPt,
                float maxPeakDarkness,
                out float centerX,
                out float topBaselineY,
                out float meanDarkness,
                out float peakDarkness)
            {
                centerX = 0f;
                topBaselineY = 0f;
                meanDarkness = 1f;
                peakDarkness = 1f;

                const float margin = MaterialPlacementGeometry.MinMarginPt;
                var minCenterX = margin + (blockWidthPt / 2f);
                var maxCenterX = PageWidthPt - margin - (blockWidthPt / 2f);
                var minTopY = Math.Max(
                    margin + blockHeightPt,
                    PageHeightPt * MaterialPlacementGeometry.MaterialForbiddenBottomRatio + 8f);
                var maxTopY = PageHeightPt - margin;

                if (maxCenterX <= minCenterX || maxTopY <= minTopY)
                    return false;

                var step = MaterialWhitespaceStepPt;
                var coarse = new List<(float X, float Y, float Dark)>();

                for (var y = minTopY; y <= maxTopY; y += step)
                {
                    for (var x = minCenterX; x <= maxCenterX; x += step)
                    {
                        if (PdfRasterLayoutService.IsMaterialPlacementForbidden(
                                x,
                                y,
                                blockWidthPt,
                                blockHeightPt,
                                PageWidthPt,
                                PageHeightPt,
                                pdfPath: pdfPath,
                                pageNumber: pageNumber,
                                checkPeakInk: false))
                        {
                            continue;
                        }

                        var dark = MeasureAnchorBlockDarkness(x, y, blockWidthPt, blockHeightPt, 1);
                        coarse.Add((x, y, dark));
                    }
                }

                if (coarse.Count == 0)
                    return false;

                var refineStep = Math.Max(6f, step / 2f);
                var seedCount = MaterialWhitespaceSeedCount;
                var seeds = coarse
                    .OrderBy(c => c.Dark)
                    .Take(seedCount)
                    .ToList();

                var bestScore = float.MaxValue;
                var found = false;

                foreach (var seed in seeds)
                {
                    for (var dy = -refineStep; dy <= refineStep; dy += refineStep)
                    {
                        for (var dx = -refineStep; dx <= refineStep; dx += refineStep)
                        {
                            var x = seed.X + dx;
                            var y = seed.Y + dy;
                            if (x < minCenterX || x > maxCenterX || y < minTopY || y > maxTopY)
                                continue;

                            if (PdfRasterLayoutService.IsMaterialPlacementForbidden(
                                    x,
                                    y,
                                    blockWidthPt,
                                    blockHeightPt,
                                    PageWidthPt,
                                    PageHeightPt,
                                    pdfPath: pdfPath,
                                    pageNumber: pageNumber,
                                    checkPeakInk: false))
                            {
                                continue;
                            }

                            var dark = MeasureAnchorBlockDarkness(x, y, blockWidthPt, blockHeightPt, 1);
                            var peak = PdfRasterLayoutService.MeasureMaterialPlacementPeakDarkness(
                                pdfPath,
                                pageNumber,
                                x,
                                y,
                                blockWidthPt,
                                blockHeightPt,
                                PageWidthPt,
                                PageHeightPt);

                            if (peak > maxPeakDarkness)
                                continue;

                            var preciseInk = PdfRasterLayoutService.MeasureMaterialPlacementPreciseDarkness(
                                pdfPath,
                                pageNumber,
                                x,
                                y,
                                blockWidthPt,
                                blockHeightPt,
                                PageWidthPt,
                                PageHeightPt);

                            if (preciseInk > MaterialPlacementGeometry.MaterialPreciseInkRejectDarkness)
                                continue;

                            var relY = y / PageHeightPt;
                            var relX = x / PageWidthPt;
                            var zoneBonus = 0f;
                            if (relY > 0.42f && relY < 0.82f)
                                zoneBonus += 0.10f;
                            if (relX < 0.52f)
                                zoneBonus += 0.06f;

                            var score = (peak * 0.75f) + (dark * 0.15f) + (preciseInk * 0.10f) - zoneBonus;
                            if (score >= bestScore)
                                continue;

                            bestScore = score;
                            centerX = x;
                            topBaselineY = y;
                            meanDarkness = dark;
                            peakDarkness = peak;
                            found = true;
                        }
                    }
                }

                if (found)
                {
                    if (PdfRasterLayoutService.IsMaterialAnchorHardBlockValid(
                            pdfPath, pageNumber, centerX, topBaselineY,
                            blockWidthPt, blockHeightPt, PageWidthPt, PageHeightPt))
                        return true;

                    found = false;
                }

                // Kein strenger Treffer: freieste Stelle nur mit vollständiger HardBlock-Prüfung.
                bestScore = float.MaxValue;
                foreach (var seed in seeds)
                {
                    for (var dy = -refineStep; dy <= refineStep; dy += refineStep)
                    {
                        for (var dx = -refineStep; dx <= refineStep; dx += refineStep)
                        {
                            var x = seed.X + dx;
                            var y = seed.Y + dy;
                            if (x < minCenterX || x > maxCenterX || y < minTopY || y > maxTopY)
                                continue;

                            if (PdfRasterLayoutService.IsMaterialPlacementForbidden(
                                    x,
                                    y,
                                    blockWidthPt,
                                    blockHeightPt,
                                    PageWidthPt,
                                    PageHeightPt,
                                    pdfPath: pdfPath,
                                    pageNumber: pageNumber,
                                    checkPeakInk: false))
                            {
                                continue;
                            }

                            var dark = MeasureAnchorBlockDarkness(x, y, blockWidthPt, blockHeightPt, 1);
                            var peak = PdfRasterLayoutService.MeasureMaterialPlacementPeakDarkness(
                                pdfPath,
                                pageNumber,
                                x,
                                y,
                                blockWidthPt,
                                blockHeightPt,
                                PageWidthPt,
                                PageHeightPt);
                            var preciseInk = PdfRasterLayoutService.MeasureMaterialPlacementPreciseDarkness(
                                pdfPath,
                                pageNumber,
                                x,
                                y,
                                blockWidthPt,
                                blockHeightPt,
                                PageWidthPt,
                                PageHeightPt);
                            var relY = y / PageHeightPt;
                            var relX = x / PageWidthPt;
                            var zoneBonus = 0f;
                            if (relY > 0.42f && relY < 0.82f)
                                zoneBonus += 0.08f;
                            if (relX < 0.52f)
                                zoneBonus += 0.05f;
                            var score = (peak * 0.80f) + (dark * 0.12f) + (preciseInk * 0.08f) - zoneBonus;
                            if (score >= bestScore)
                                continue;

                            bestScore = score;
                            centerX = x;
                            topBaselineY = y;
                            meanDarkness = dark;
                            peakDarkness = peak;
                            found = true;
                        }
                    }
                }

                if (found)
                {
                    if (PdfRasterLayoutService.IsMaterialAnchorHardBlockValid(
                            pdfPath, pageNumber, centerX, topBaselineY,
                            blockWidthPt, blockHeightPt, PageWidthPt, PageHeightPt))
                        return true;

                    found = false;
                }

                foreach (var candidate in coarse
                    .OrderBy(c => c.Dark)
                    .ThenByDescending(c => c.Y / PageHeightPt)
                    .ThenBy(c => c.X / PageWidthPt))
                {
                    if (!PdfRasterLayoutService.IsMaterialAnchorHardBlockValid(
                            pdfPath,
                            pageNumber,
                            candidate.X,
                            candidate.Y,
                            blockWidthPt,
                            blockHeightPt,
                            PageWidthPt,
                            PageHeightPt))
                    {
                        continue;
                    }

                    var peak = PdfRasterLayoutService.MeasureMaterialPlacementPeakDarkness(
                        pdfPath,
                        pageNumber,
                        candidate.X,
                        candidate.Y,
                        blockWidthPt,
                        blockHeightPt,
                        PageWidthPt,
                        PageHeightPt);
                    if (peak > MaterialPlacementGeometry.MaterialInkPeakHardBlockDarkness)
                        continue;

                    centerX = candidate.X;
                    topBaselineY = candidate.Y;
                    meanDarkness = candidate.Dark;
                    peakDarkness = peak;
                    return true;
                }

                return false;
            }
        }

        private static int GetMaterialPlacementDpi()
        {
            if (AiWatermarkPlacementService.IsEnabled)
                return MaterialPlacementVisionDpi;

            return MaterialPlacementRenderDpi;
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
                    var quickExit = false;
                    if (pass == 0)
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

                return false;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Sucht auf der gesamten Seite (nur Schriftfeld ausgeschlossen) die hellste freie Fläche.
        /// Keine feste Position — rein bildbasiert.
        /// </summary>
        public static bool TryFindGlobalFreeMaterialAnchor(
            string pdfPath,
            int pageNumber,
            float blockWidthPt,
            float blockHeightPt,
            float pageWidthPt,
            float pageHeightPt,
            float titleBlockLeftPt,
            float titleBlockTopPt,
            out float centerX,
            out float anchorY,
            out float bestDarkness)
        {
            centerX = 0f;
            anchorY = 0f;
            bestDarkness = 1f;

            if (!_available || string.IsNullOrWhiteSpace(pdfPath) || !File.Exists(pdfPath))
                return false;

            try
            {
                var grid = GetOrCreateDarknessGrid(pdfPath, pageNumber, pageWidthPt, pageHeightPt);
                if (grid == null)
                    return false;

                const float margin = 24f;
                if (!grid.TryFindBestFreeCenterSlot(
                        blockWidthPt,
                        blockHeightPt,
                        margin,
                        titleBlockLeftPt,
                        titleBlockTopPt,
                        out centerX,
                        out anchorY,
                        out bestDarkness))
                {
                    return false;
                }

                ScanProgress?.Invoke(1d);
                return bestDarkness <= MaterialPreferredMaxDarkness;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Sucht nur in den Seitenrändern (oben, links, rechts über Schriftfeld) — nie in der Zeichnungsmitte.
        /// </summary>
        public static bool TryFindMarginFreeMaterialAnchor(
            string pdfPath,
            int pageNumber,
            float blockWidthPt,
            float blockHeightPt,
            float pageWidthPt,
            float pageHeightPt,
            float titleBlockLeftPt,
            float titleBlockTopPt,
            out float centerX,
            out float anchorY,
            out float bestDarkness)
        {
            centerX = 0f;
            anchorY = 0f;
            bestDarkness = 1f;

            if (!_available || string.IsNullOrWhiteSpace(pdfPath) || !File.Exists(pdfPath))
                return false;

            try
            {
                var grid = GetOrCreateDarknessGrid(pdfPath, pageNumber, pageWidthPt, pageHeightPt);
                if (grid == null)
                    return false;

                const float margin = 28f;
                var zones = new[]
                {
                    new MarginZone(margin, titleBlockLeftPt - 22f, margin, titleBlockTopPt - 10f),
                    new MarginZone(margin, pageWidthPt - margin, margin, Math.Min(titleBlockTopPt * 0.55f, pageHeightPt * 0.42f)),
                    new MarginZone(margin, margin + blockWidthPt + 48f, titleBlockTopPt * 0.22f, pageHeightPt * 0.62f)
                };

                var found = false;
                foreach (var zone in zones)
                {
                    if (!TryFindFreeCenterInZone(
                            grid,
                            blockWidthPt,
                            blockHeightPt,
                            zone.Left,
                            zone.Right,
                            zone.Bottom,
                            zone.Top,
                            titleBlockLeftPt,
                            titleBlockTopPt,
                            MaterialMarginMaxDarkness,
                            out var cx,
                            out var ay,
                            out var dark))
                    {
                        continue;
                    }

                    if (dark < bestDarkness)
                    {
                        bestDarkness = dark;
                        centerX = cx;
                        anchorY = ay;
                        found = true;
                    }
                }

                return found && bestDarkness <= MaterialMarginMaxDarkness;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>Unterer Zeichnungsbereich links vom Schriftfeld — bevorzugter Material-Platz.</summary>
        public static bool TryFindBottomFreeMaterialAnchor(
            string pdfPath,
            int pageNumber,
            float blockWidthPt,
            float blockHeightPt,
            float pageWidthPt,
            float pageHeightPt,
            float titleBlockLeftPt,
            float titleBlockTopPt,
            out float centerX,
            out float anchorY,
            out float bestDarkness)
        {
            centerX = 0f;
            anchorY = 0f;
            bestDarkness = 1f;

            if (!_available || string.IsNullOrWhiteSpace(pdfPath) || !File.Exists(pdfPath))
                return false;

            try
            {
                var grid = GetOrCreateDarknessGrid(pdfPath, pageNumber, pageWidthPt, pageHeightPt);
                if (grid == null)
                    return false;

                const float margin = 28f;
                var zoneRight = Math.Max(120f, titleBlockLeftPt - 18f);
                var zoneTop = Math.Max(titleBlockTopPt - 8f, pageHeightPt * 0.48f);

                if (!TryFindFreeCenterInZone(
                        grid,
                        blockWidthPt,
                        blockHeightPt,
                        margin,
                        zoneRight,
                        margin,
                        zoneTop,
                        titleBlockLeftPt,
                        titleBlockTopPt,
                        MaterialMarginMaxDarkness,
                        out centerX,
                        out anchorY,
                        out bestDarkness))
                {
                    return false;
                }

                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>Streifen links neben dem Schriftfeld — typischer Weißraum bei Tesla-Zeichnungen.</summary>
        public static bool TryFindTitleBlockStripMaterialAnchor(
            string pdfPath,
            int pageNumber,
            float blockWidthPt,
            float blockHeightPt,
            float pageWidthPt,
            float pageHeightPt,
            float titleBlockLeftPt,
            float titleBlockTopPt,
            out float centerX,
            out float anchorY,
            out float bestDarkness)
        {
            centerX = 0f;
            anchorY = 0f;
            bestDarkness = 1f;

            if (!_available || string.IsNullOrWhiteSpace(pdfPath) || !File.Exists(pdfPath))
                return false;

            try
            {
                var grid = GetOrCreateDarknessGrid(pdfPath, pageNumber, pageWidthPt, pageHeightPt);
                if (grid == null)
                    return false;

                var stripRight = Math.Max(100f, titleBlockLeftPt - 22f);
                if (!TryFindFreeCenterInZone(
                        grid,
                        blockWidthPt,
                        blockHeightPt,
                        32f,
                        stripRight,
                        titleBlockTopPt + 6f,
                        titleBlockTopPt + 150f,
                        titleBlockLeftPt,
                        titleBlockTopPt,
                        MaterialMarginMaxDarkness,
                        out centerX,
                        out anchorY,
                        out bestDarkness))
                {
                    return false;
                }

                return true;
            }
            catch
            {
                return false;
            }
        }

        private readonly struct MarginZone
        {
            public MarginZone(float left, float right, float bottom, float top)
            {
                Left = left;
                Right = right;
                Bottom = bottom;
                Top = top;
            }

            public float Left { get; }
            public float Right { get; }
            public float Bottom { get; }
            public float Top { get; }
        }

        private static bool TryFindFreeCenterInZone(
            PageDarknessGrid grid,
            float blockWidthPt,
            float blockHeightPt,
            float zoneLeftPt,
            float zoneRightPt,
            float zoneBottomPt,
            float zoneTopPt,
            float titleBlockLeftPt,
            float titleBlockTopPt,
            float maxDarkness,
            out float centerX,
            out float anchorY,
            out float darkness)
        {
            centerX = 0f;
            anchorY = 0f;
            darkness = 1f;

            var blockWCols = Math.Max(1, (int)Math.Ceiling((blockWidthPt * grid.ScaleX) / grid.CellSize));
            var blockHRows = Math.Max(1, (int)Math.Ceiling((blockHeightPt * grid.ScaleY) / grid.CellSize));

            var colStart = Math.Max(0, (int)Math.Floor((zoneLeftPt * grid.ScaleX) / grid.CellSize));
            var colEnd = Math.Min(grid.Cols - blockWCols, (int)Math.Ceiling((zoneRightPt * grid.ScaleX) / grid.CellSize) - blockWCols);
            var rowStart = Math.Max(0, (int)Math.Floor((grid.BitmapHeightPx - (zoneTopPt * grid.ScaleY)) / grid.CellSize));
            var rowEnd = Math.Min(grid.Rows - blockHRows, (int)Math.Ceiling((grid.BitmapHeightPx - (zoneBottomPt * grid.ScaleY)) / grid.CellSize) - blockHRows);

            if (colEnd < colStart || rowEnd < rowStart)
                return false;

            var bestDark = 1f;
            var bestCol = colStart;
            var bestRow = rowStart;
            var topBandLimitPt = grid.PageHeightPt * 0.82f;

            for (var row = rowStart; row <= rowEnd; row++)
            {
                for (var col = colStart; col <= colEnd; col++)
                {
                    var centerXPt = ((col + (blockWCols / 2f)) * grid.CellSize) / grid.ScaleX;
                    var topPt = (grid.BitmapHeightPx - (row * grid.CellSize)) / grid.ScaleY;
                    var bottomPt = topPt - blockHeightPt;

                    if (topPt > topBandLimitPt)
                        continue;

                    if (IsInsideTitleBlock(
                            centerXPt,
                            bottomPt,
                            blockWidthPt,
                            blockHeightPt,
                            grid.PageWidthPt,
                            grid.PageHeightPt,
                            titleBlockLeftPt,
                            titleBlockTopPt))
                    {
                        continue;
                    }

                    var dark = grid.MeasureCenterBlockDarkness(centerXPt, bottomPt, blockWidthPt, blockHeightPt);

                    if (IsInsideDrawingCore(centerXPt, bottomPt, blockWidthPt, blockHeightPt, grid.PageWidthPt, grid.PageHeightPt, 1))
                        continue;

                    if (dark < bestDark - 0.002f)
                    {
                        bestDark = dark;
                        bestCol = col;
                        bestRow = row;
                    }
                }
            }

            if (bestDark > maxDarkness)
                return false;

            centerX = ((bestCol + (blockWCols / 2f)) * grid.CellSize) / grid.ScaleX;
            var anchorTopPt = (grid.BitmapHeightPx - (bestRow * grid.CellSize)) / grid.ScaleY;
            anchorY = anchorTopPt - blockHeightPt;
            darkness = bestDark;
            return true;
        }

        public static float GetMaterialLineHeight(float fontSize) => fontSize + 10f;

        public static float GetMaterialBlockHeight(float fontSize, int lineCount = 1)
        {
            var lines = Math.Max(1, lineCount);
            return (lines * GetMaterialLineHeight(fontSize)) + 6f;
        }

        public static float GetMaterialAnchorBottomFromTopBaseline(float topBaselineY, float fontSize, int lineCount = 1)
        {
            var blockHeight = GetMaterialBlockHeight(fontSize, lineCount);
            return topBaselineY - blockHeight + GetMaterialLineHeight(fontSize);
        }

        public static float AnchorBottomToMaterialTopBaseline(float anchorBottomY, float blockHeightPt, int lineCount = 1)
        {
            var estFont = Math.Max(14f, (blockHeightPt - 6f) / Math.Max(1, lineCount));
            var lineHeight = GetMaterialLineHeight(estFont);
            return anchorBottomY + blockHeightPt - lineHeight;
        }

        public static bool IsMaterialPlacementForbidden(
            float centerX,
            float topBaselineY,
            float blockWidthPt,
            float blockHeightPt,
            float pageWidthPt,
            float pageHeightPt,
            float minMarginPt = 28f,
            string pdfPath = null,
            int pageNumber = 1,
            bool checkPeakInk = true)
        {
            ResolveBlockGeometry(
                centerX,
                topBaselineY,
                blockWidthPt,
                blockHeightPt,
                1,
                materialYIsBottomAnchor: false,
                out _,
                out var anchorBottomY);

            var left = centerX - (blockWidthPt / 2f);
            var right = centerX + (blockWidthPt / 2f);
            var top = anchorBottomY + blockHeightPt;

            if (left < minMarginPt || right > pageWidthPt - minMarginPt)
                return true;
            if (anchorBottomY < minMarginPt || top > pageHeightPt - minMarginPt)
                return true;

            if (IsOverlappingTitleBlock(
                    centerX,
                    topBaselineY,
                    blockWidthPt,
                    blockHeightPt,
                    pageWidthPt,
                    pageHeightPt,
                    1,
                    materialYIsBottomAnchor: false))
            {
                return true;
            }

            var bounds = MaterialBlockBounds.FromAnchor(centerX, topBaselineY, blockWidthPt, blockHeightPt);
            if (MaterialPlacementGeometry.IntersectsPageNumberStrip(bounds, pageWidthPt, pageHeightPt))
                return true;

            if (MaterialPlacementGeometry.IntersectsBgReservedStrip(bounds, pageWidthPt, pageHeightPt))
                return true;

            if (MaterialPlacementGeometry.IntersectsMaterialForbiddenBottomStrip(bounds, pageWidthPt, pageHeightPt))
                return true;

            if (_available && !string.IsNullOrWhiteSpace(pdfPath) && checkPeakInk)
            {
                var peak = MeasureMaterialPlacementPeakDarkness(
                    pdfPath, pageNumber, centerX, topBaselineY,
                    blockWidthPt, blockHeightPt, pageWidthPt, pageHeightPt);
                if (peak >= MaterialPlacementGeometry.MaterialInkPeakSearchBlockDarkness)
                    return true;
            }

            return false;
        }

        /// <summary>Linker Maßlinien-Streifen (TSL) – hier nie Material platzieren.</summary>
        public static bool IsInMaterialLeftDimensionStrip(
            float anchorX,
            float anchorY,
            float blockWidthPt,
            float blockHeightPt,
            float pageWidthPt,
            float pageHeightPt,
            int alignment = 1,
            bool materialYIsBottomAnchor = false)
        {
            ResolveBlockGeometry(
                anchorX,
                anchorY,
                blockWidthPt,
                blockHeightPt,
                alignment,
                materialYIsBottomAnchor,
                out var centerX,
                out var anchorBottomY);

            var left = centerX - (blockWidthPt / 2f);
            var right = centerX + (blockWidthPt / 2f);
            var top = anchorBottomY + blockHeightPt;

            var zoneLeft = 24f;
            var zoneRight = pageWidthPt * 0.32f;
            var zoneBottom = pageHeightPt * 0.28f;
            var zoneTop = pageHeightPt * 0.88f;

            return right > zoneLeft
                && left < zoneRight
                && top > zoneBottom
                && anchorBottomY < zoneTop;
        }

        /// <summary>Nur Geometrie – Dunkelheit fließt in Scoring ein, nicht als hartes Verbot.</summary>
        public static bool IsMaterialGeometryForbidden(
            float centerX,
            float topBaselineY,
            float blockWidthPt,
            float blockHeightPt,
            float pageWidthPt,
            float pageHeightPt,
            float minMarginPt = 28f)
        {
            return IsMaterialPlacementForbidden(
                centerX,
                topBaselineY,
                blockWidthPt,
                blockHeightPt,
                pageWidthPt,
                pageHeightPt,
                minMarginPt,
                pdfPath: null,
                pageNumber: 1);
        }

        public static float MeasureMaterialPlacementDarkness(
            string pdfPath,
            int pageNumber,
            float centerX,
            float topBaselineY,
            float blockWidthPt,
            float blockHeightPt,
            float pageWidthPt,
            float pageHeightPt)
        {
            ResolveBlockGeometry(
                centerX,
                topBaselineY,
                blockWidthPt,
                blockHeightPt,
                1,
                materialYIsBottomAnchor: false,
                out var resolvedCenterX,
                out var anchorBottomY);

            var grid = GetOrCreateDarknessGrid(pdfPath, pageNumber, pageWidthPt, pageHeightPt);
            if (grid != null)
            {
                return grid.MeasureCenterBlockDarkness(
                    resolvedCenterX,
                    anchorBottomY,
                    blockWidthPt,
                    blockHeightPt);
            }

            return MeasureMaterialBlockDarknessPrecise(
                pdfPath,
                pageNumber,
                resolvedCenterX,
                anchorBottomY,
                blockWidthPt,
                blockHeightPt,
                pageWidthPt,
                pageHeightPt);
        }

        public static float MeasureMaterialPlacementRobustDarkness(
            string pdfPath,
            int pageNumber,
            float centerX,
            float topBaselineY,
            float blockWidthPt,
            float blockHeightPt,
            float pageWidthPt,
            float pageHeightPt)
        {
            ResolveBlockGeometry(
                centerX,
                topBaselineY,
                blockWidthPt,
                blockHeightPt,
                1,
                materialYIsBottomAnchor: false,
                out var resolvedCenterX,
                out var anchorBottomY);

            var grid = GetOrCreateDarknessGrid(pdfPath, pageNumber, pageWidthPt, pageHeightPt);
            if (grid != null)
            {
                return grid.MeasureCenterBlockDarknessRobust(
                    resolvedCenterX,
                    anchorBottomY,
                    blockWidthPt,
                    blockHeightPt);
            }

            return MeasureMaterialPlacementDarkness(
                pdfPath,
                pageNumber,
                centerX,
                topBaselineY,
                blockWidthPt,
                blockHeightPt,
                pageWidthPt,
                pageHeightPt);
        }

        public static float MeasureMaterialPlacementPeakDarkness(
            string pdfPath,
            int pageNumber,
            float centerX,
            float topBaselineY,
            float blockWidthPt,
            float blockHeightPt,
            float pageWidthPt,
            float pageHeightPt)
        {
            ResolveBlockGeometry(
                centerX,
                topBaselineY,
                blockWidthPt,
                blockHeightPt,
                1,
                materialYIsBottomAnchor: false,
                out var resolvedCenterX,
                out var anchorBottomY);

            var grid = GetOrCreateDarknessGrid(pdfPath, pageNumber, pageWidthPt, pageHeightPt);
            if (grid != null)
            {
                return grid.MeasureCenterBlockPeakDarkness(
                    resolvedCenterX,
                    anchorBottomY,
                    blockWidthPt,
                    blockHeightPt);
            }

            return MeasureMaterialPlacementDarkness(
                pdfPath,
                pageNumber,
                centerX,
                topBaselineY,
                blockWidthPt,
                blockHeightPt,
                pageWidthPt,
                pageHeightPt);
        }

        public static bool TryFindBestMaterialPlacement(
            string pdfPath,
            int pageNumber,
            float blockWidthPt,
            float blockHeightPt,
            float pageWidthPt,
            float pageHeightPt,
            out float centerX,
            out float topBaselineY,
            out float darkness,
            int materialLineCount = 1)
        {
            centerX = 0f;
            topBaselineY = 0f;
            darkness = 1f;

            try
            {
                if (TryFindWhitespaceMaterialPlacement(
                        pdfPath,
                        pageNumber,
                        blockWidthPt,
                        blockHeightPt,
                        pageWidthPt,
                        pageHeightPt,
                        out centerX,
                        out topBaselineY,
                        out darkness,
                        out _))
                {
                    return true;
                }

                return false;
            }
            catch
            {
                return false;
            }
        }

        private sealed class MaterialPlacementCandidate
        {
            public float CenterX;
            public float TopBaselineY;
            public float Darkness;
            public float PeakDarkness;
            public float Score;
            public float ZoneBias;
        }

        public static bool TryFindWhitespaceMaterialPlacement(
            string pdfPath,
            int pageNumber,
            float blockWidthPt,
            float blockHeightPt,
            float pageWidthPt,
            float pageHeightPt,
            out float centerX,
            out float topBaselineY,
            out float darkness,
            out float peakDarkness)
        {
            centerX = 0f;
            topBaselineY = 0f;
            darkness = 1f;
            peakDarkness = 1f;

            if (!MaterialFreeAreaPlacementService.TryFindBestFreePosition(
                    pdfPath,
                    pageNumber,
                    blockWidthPt,
                    blockHeightPt,
                    pageWidthPt,
                    pageHeightPt,
                    Array.Empty<PdfTextRect>(),
                    null,
                    out centerX,
                    out topBaselineY,
                    out darkness,
                    out peakDarkness,
                    out _))
            {
                return false;
            }

            return true;
        }

        internal static bool IsMaterialAnchorHardBlockValid(
            string pdfPath,
            int pageNumber,
            float centerX,
            float topBaselineY,
            float blockWidthPt,
            float blockHeightPt,
            float pageWidthPt,
            float pageHeightPt)
        {
            var bounds = MaterialBlockBounds.FromAnchor(centerX, topBaselineY, blockWidthPt, blockHeightPt);
            return MaterialPlacementGeometry.EvaluateHardBlock(
                       bounds,
                       pageWidthPt,
                       pageHeightPt,
                       null,
                       pdfPath,
                       pageNumber) == MaterialHardBlockReason.None;
        }

        private static void CollectMaterialCandidates(
            string pdfPath,
            int pageNumber,
            float blockWidthPt,
            float blockHeightPt,
            float pageWidthPt,
            float pageHeightPt,
            float zoneLeft,
            float zoneRight,
            float zoneBottom,
            float zoneTop,
            float stepPt,
            float zoneBias,
            List<MaterialPlacementCandidate> target)
        {
            var minCenterX = zoneLeft + (blockWidthPt / 2f);
            var maxCenterX = zoneRight - (blockWidthPt / 2f);
            var minTopY = zoneBottom + blockHeightPt;
            var maxTopY = zoneTop;

            if (maxCenterX <= minCenterX || maxTopY <= minTopY)
                return;

            var step = stepPt;
            var added = 0;
            var maxPerZone = MaterialCandidateMaxPerZone;

            for (var y = minTopY; y <= maxTopY; y += step)
            {
                for (var x = minCenterX; x <= maxCenterX; x += step)
                {
                    if (added >= maxPerZone)
                        return;

                    if (IsMaterialPlacementForbidden(
                            x, y, blockWidthPt, blockHeightPt, pageWidthPt, pageHeightPt,
                            pdfPath: pdfPath, pageNumber: pageNumber))
                        continue;

                    var dark = MeasureMaterialPlacementDarkness(
                        pdfPath, pageNumber, x, y, blockWidthPt, blockHeightPt, pageWidthPt, pageHeightPt);

                    var peak = MeasureMaterialPlacementPeakDarkness(
                        pdfPath, pageNumber, x, y, blockWidthPt, blockHeightPt, pageWidthPt, pageHeightPt);

                    var score = (1f - dark) + (zoneBias * 0.5f) - (peak * 0.55f);

                    target.Add(new MaterialPlacementCandidate
                    {
                        CenterX = x,
                        TopBaselineY = y,
                        Darkness = dark,
                        PeakDarkness = peak,
                        Score = score,
                        ZoneBias = zoneBias
                    });
                    added++;
                }
            }
        }

        private static void RefineMaterialCandidate(
            string pdfPath,
            int pageNumber,
            float blockWidthPt,
            float blockHeightPt,
            float pageWidthPt,
            float pageHeightPt,
            float seedCenterX,
            float seedTopY,
            float zoneBias,
            List<MaterialPlacementCandidate> target)
        {
            for (var dy = -16f; dy <= 16f; dy += 8f)
            {
                for (var dx = -16f; dx <= 16f; dx += 8f)
                {
                    var x = seedCenterX + dx;
                    var y = seedTopY + dy;
                    if (IsMaterialPlacementForbidden(
                            x, y, blockWidthPt, blockHeightPt, pageWidthPt, pageHeightPt,
                            pdfPath: pdfPath, pageNumber: pageNumber))
                        continue;

                    var dark = MeasureMaterialPlacementDarkness(
                        pdfPath, pageNumber, x, y, blockWidthPt, blockHeightPt, pageWidthPt, pageHeightPt);

                    var peak = MeasureMaterialPlacementPeakDarkness(
                        pdfPath, pageNumber, x, y, blockWidthPt, blockHeightPt, pageWidthPt, pageHeightPt);

                    var score = (1f - dark) + (zoneBias * 0.5f) - (peak * 0.55f);

                    target.Add(new MaterialPlacementCandidate
                    {
                        CenterX = x,
                        TopBaselineY = y,
                        Darkness = dark,
                        PeakDarkness = peak,
                        Score = score,
                        ZoneBias = zoneBias
                    });
                }
            }
        }

        private static bool TryFindBottomCenterMaterialAnchor(
            string pdfPath,
            int pageNumber,
            float blockWidthPt,
            float blockHeightPt,
            float pageWidthPt,
            float pageHeightPt,
            float titleBlockLeftPt,
            float titleBlockTopPt,
            out float centerX,
            out float topBaselineY,
            out float darkness,
            int materialLineCount = 1)
        {
            centerX = 0f;
            topBaselineY = 0f;
            darkness = 1f;

            var zoneLeft = pageWidthPt * 0.20f;
            var zoneRight = pageWidthPt * 0.80f;
            var zoneBottom = pageHeightPt * 0.55f;
            var zoneTop = Math.Min(pageHeightPt * 0.86f, titleBlockTopPt + 160f);

            var candidates = new List<MaterialPlacementCandidate>();
            CollectMaterialCandidates(
                pdfPath,
                pageNumber,
                blockWidthPt,
                blockHeightPt,
                pageWidthPt,
                pageHeightPt,
                zoneLeft,
                zoneRight,
                zoneBottom,
                zoneTop,
                stepPt: MaterialCandidateStepPt,
                zoneBias: 0.16f,
                candidates);

            var best = candidates
                .Where(c => IsMaterialAnchorHardBlockValid(
                    pdfPath, pageNumber, c.CenterX, c.TopBaselineY,
                    blockWidthPt, blockHeightPt, pageWidthPt, pageHeightPt))
                .Where(c => !IsMaterialPlacementForbidden(
                    c.CenterX, c.TopBaselineY, blockWidthPt, blockHeightPt, pageWidthPt, pageHeightPt,
                    pdfPath: pdfPath, pageNumber: pageNumber))
                .OrderByDescending(c => c.Score)
                .FirstOrDefault();

            if (best == null)
                return false;

            centerX = best.CenterX;
            topBaselineY = best.TopBaselineY;
            darkness = best.Darkness;
            return true;
        }

        public static bool TryFindSafeMaterialPlacement(
            string pdfPath,
            int pageNumber,
            float blockWidthPt,
            float blockHeightPt,
            float pageWidthPt,
            float pageHeightPt,
            out float centerX,
            out float topBaselineY,
            out float darkness)
        {
            centerX = 0f;
            topBaselineY = 0f;
            darkness = 1f;

            return TryFindWhitespaceMaterialPlacement(
                pdfPath,
                pageNumber,
                blockWidthPt,
                blockHeightPt,
                pageWidthPt,
                pageHeightPt,
                out centerX,
                out topBaselineY,
                out darkness,
                out _);
        }

        public static void ResolveEmergencyMaterialAnchor(
            string pdfPath,
            int pageNumber,
            float blockWidthPt,
            float blockHeightPt,
            float pageWidthPt,
            float pageHeightPt,
            out float centerX,
            out float topBaselineY)
        {
            if (TryFindWhitespaceMaterialPlacement(
                    pdfPath,
                    pageNumber,
                    blockWidthPt,
                    blockHeightPt,
                    pageWidthPt,
                    pageHeightPt,
                    out centerX,
                    out topBaselineY,
                    out _,
                    out _))
            {
                return;
            }

            centerX = 0f;
            topBaselineY = 0f;
        }

        private static bool TryMaterialSafeFallback(
            string pdfPath,
            int pageNumber,
            float blockWidthPt,
            float blockHeightPt,
            float pageWidthPt,
            float pageHeightPt,
            out float centerX,
            out float topBaselineY,
            out float darkness)
        {
            return TryFindSafeMaterialPlacement(
                pdfPath,
                pageNumber,
                blockWidthPt,
                blockHeightPt,
                pageWidthPt,
                pageHeightPt,
                out centerX,
                out topBaselineY,
                out darkness);
        }

        public static void ResolveBlockGeometry(
            float anchorX,
            float anchorY,
            float blockWidthPt,
            float blockHeightPt,
            int alignment,
            bool materialYIsBottomAnchor,
            out float centerX,
            out float anchorBottomY)
        {
            if (alignment == 1)
            {
                centerX = anchorX;
                anchorBottomY = materialYIsBottomAnchor
                    ? anchorY
                    : GetMaterialAnchorBottomFromTopBaseline(
                        anchorY,
                        Math.Max(12f, blockHeightPt - 8f),
                        Math.Max(1, (int)Math.Round((blockHeightPt - 6f) / GetMaterialLineHeight(Math.Max(12f, blockHeightPt - 8f)))));
            }
            else if (alignment == 2)
            {
                centerX = anchorX - (blockWidthPt / 2f);
                anchorBottomY = anchorY;
            }
            else
            {
                centerX = anchorX + (blockWidthPt / 2f);
                anchorBottomY = anchorY;
            }
        }

        public static bool IsOverlappingTitleBlock(
            float anchorX,
            float anchorY,
            float blockWidthPt,
            float blockHeightPt,
            float pageWidthPt,
            float pageHeightPt,
            int alignment,
            bool materialYIsBottomAnchor = false)
        {
            ResolveBlockGeometry(
                anchorX,
                anchorY,
                blockWidthPt,
                blockHeightPt,
                alignment,
                materialYIsBottomAnchor,
                out var centerX,
                out var anchorBottomY);

            var titleLeft = pageWidthPt * MaterialPlacementGeometry.TitleBlockLeftRatio;
            var titleTop = pageHeightPt * MaterialPlacementGeometry.TitleBlockTopRatio;

            var bounds = new MaterialBlockBounds
            {
                CenterX = centerX,
                TopBaselineY = anchorY,
                BlockWidth = blockWidthPt,
                BlockHeight = blockHeightPt,
                Left = centerX - (blockWidthPt / 2f),
                Right = centerX + (blockWidthPt / 2f),
                Bottom = anchorBottomY,
                Top = anchorBottomY + blockHeightPt
            };

            return MaterialPlacementGeometry.IntersectsTitleBlock(bounds, pageWidthPt, pageHeightPt);
        }

        public static bool IsInsideDrawingCore(
            float anchorX,
            float anchorY,
            float blockWidthPt,
            float blockHeightPt,
            float pageWidthPt,
            float pageHeightPt,
            int alignment,
            bool materialYIsBottomAnchor = false)
        {
            ResolveBlockGeometry(
                anchorX,
                anchorY,
                blockWidthPt,
                blockHeightPt,
                alignment,
                materialYIsBottomAnchor,
                out var centerX,
                out var anchorBottomY);

            var left = centerX - (blockWidthPt / 2f);
            var right = centerX + (blockWidthPt / 2f);
            var top = anchorBottomY + blockHeightPt;

            var bounds = new MaterialBlockBounds
            {
                CenterX = centerX,
                TopBaselineY = materialYIsBottomAnchor ? anchorBottomY + blockHeightPt - GetMaterialLineHeight(Math.Max(12f, blockHeightPt - 8f)) : anchorY,
                BlockWidth = blockWidthPt,
                BlockHeight = blockHeightPt,
                Left = left,
                Right = right,
                Bottom = anchorBottomY,
                Top = top
            };

            return MaterialPlacementGeometry.IntersectsDrawingCore(bounds, pageWidthPt, pageHeightPt);
        }

        /// <summary>Nur Randkorridore — nie die Zeichnungsmitte.</summary>
        public static bool TryFindVisionMarginMaterialAnchor(
            string pdfPath,
            int pageNumber,
            float blockWidthPt,
            float blockHeightPt,
            float pageWidthPt,
            float pageHeightPt,
            out float centerX,
            out float anchorY)
        {
            centerX = 0f;
            anchorY = 0f;

            if (!_available || string.IsNullOrWhiteSpace(pdfPath) || !File.Exists(pdfPath))
                return false;

            var titleLeft = pageWidthPt * 0.58f;
            var titleTop = pageHeightPt * 0.22f;
            const float margin = 28f;

            var corridors = new[]
            {
                new { L = pageWidthPt * 0.22f, R = pageWidthPt * 0.78f, B = pageHeightPt * 0.55f, T = pageHeightPt * 0.84f },
                new { L = margin, R = titleLeft - 28f, B = margin, T = Math.Min(titleTop - 12f, pageHeightPt * 0.40f) },
                new { L = titleLeft - 22f, R = pageWidthPt - margin, B = titleTop + 8f, T = titleTop + 140f }
            };

            var bestDark = 1f;
            var found = false;

            foreach (var zone in corridors)
            {
                if (zone.R <= zone.L + 24f || zone.T <= zone.B + 12f)
                    continue;

                var grid = GetOrCreateDarknessGrid(pdfPath, pageNumber, pageWidthPt, pageHeightPt);
                if (grid == null)
                    continue;

                if (!TryFindFreeCenterInZone(
                        grid,
                        blockWidthPt,
                        blockHeightPt,
                        zone.L,
                        zone.R,
                        zone.B,
                        zone.T,
                        titleLeft,
                        titleTop,
                        MaterialGridMaxDarkness,
                        out var cx,
                        out var ay,
                        out var dark))
                {
                    continue;
                }

                if (IsInsideDrawingCore(cx, ay, blockWidthPt, blockHeightPt, pageWidthPt, pageHeightPt, 1, materialYIsBottomAnchor: true))
                    continue;

                if (!IsPlacementAcceptable(
                        pdfPath,
                        pageNumber,
                        cx,
                        ay,
                        blockWidthPt,
                        blockHeightPt,
                        pageWidthPt,
                        pageHeightPt,
                        alignment: 1,
                        isMaterial: true,
                        materialYIsBottomAnchor: true))
                {
                    continue;
                }

                if (dark < bestDark)
                {
                    bestDark = dark;
                    centerX = cx;
                    anchorY = ay;
                    found = true;
                }
            }

            return found;
        }

        internal static bool IsInsideTitleBlock(
            float centerX,
            float anchorY,
            float blockWidthPt,
            float blockHeightPt,
            float pageWidthPt,
            float pageHeightPt,
            float titleBlockLeftPt,
            float titleBlockTopPt)
        {
            var top = anchorY + blockHeightPt;

            // Schriftfeld = unterer Streifen über die volle Blattbreite (Toleranztabellen links inkl.).
            if (top > titleBlockTopPt + 32f)
                return false;

            return anchorY < titleBlockTopPt + 36f;
        }

        /// <summary>Pixelgenaue Prüfung in hoher Auflösung — erkennt auch dünne Zeichnungslinien.</summary>
        public static float MeasureMaterialPlacementPreciseDarkness(
            string pdfPath,
            int pageNumber,
            float centerX,
            float topBaselineY,
            float blockWidthPt,
            float blockHeightPt,
            float pageWidthPt,
            float pageHeightPt)
        {
            ResolveBlockGeometry(
                centerX,
                topBaselineY,
                blockWidthPt,
                blockHeightPt,
                1,
                materialYIsBottomAnchor: false,
                out var resolvedCenterX,
                out var anchorBottomY);

            return MeasureMaterialBlockDarknessPrecise(
                pdfPath,
                pageNumber,
                resolvedCenterX,
                anchorBottomY,
                blockWidthPt,
                blockHeightPt,
                pageWidthPt,
                pageHeightPt);
        }

        /// <summary>Pixelgenaue Prüfung in hoher Auflösung — erkennt auch dünne Zeichnungslinien.</summary>
        public static float MeasureMaterialBlockDarknessPrecise(
            string pdfPath,
            int pageNumber,
            float centerX,
            float anchorBottomY,
            float blockWidthPt,
            float blockHeightPt,
            float pageWidthPt,
            float pageHeightPt)
        {
            EnsureLoaded();
            if (!_available || string.IsNullOrWhiteSpace(pdfPath) || !File.Exists(pdfPath))
                return 1f;

            try
            {
                using (var bitmap = GetOrRenderPage(pdfPath, pageNumber, MaterialVerifyRenderDpi))
                {
                    if (bitmap == null)
                        return 1f;

                    var scaleX = bitmap.Width / pageWidthPt;
                    var scaleY = bitmap.Height / pageHeightPt;
                    var leftPt = centerX - (blockWidthPt / 2f);
                    var topPt = anchorBottomY + blockHeightPt;
                    var leftPx = (int)(leftPt * scaleX);
                    var topPx = (int)(bitmap.Height - (topPt * scaleY));
                    var widthPx = Math.Max(8, (int)Math.Ceiling(blockWidthPt * scaleX));
                    var heightPx = Math.Max(8, (int)Math.Ceiling(blockHeightPt * scaleY));
                    return MeasureDarkness(bitmap, leftPx, topPx, widthPx, heightPx);
                }
            }
            catch
            {
                return 1f;
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
                if (quickExit && darkness <= MaterialMaxAcceptableDarkness * 0.5f)
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
            var key = pdfPath + "|" + pageNumber + "|" + GetMaterialPlacementDpi() + "|grid|"
                      + File.GetLastWriteTimeUtc(pdfPath).Ticks;

            lock (CacheLock)
            {
                PageDarknessGrid cached;
                if (GridCache.TryGetValue(key, out cached))
                    return cached;
            }

            using (var bitmap = GetOrRenderPage(pdfPath, pageNumber, GetMaterialPlacementDpi()))
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
                var strideAbs = Math.Abs(data.Stride);
                if (strideAbs <= 0)
                    return null;

                var bytes = new byte[checked(strideAbs * bitmap.Height)];
                CopyLockedBitmapBytes(data, bytes, strideAbs);
                var bytesPerPixel = Image.GetPixelFormatSize(bitmap.PixelFormat) / 8;
                if (bytesPerPixel < 3)
                    return null;

                for (var row = 0; row < rows; row++)
                {
                    var y0 = row * cellSize;
                    var y1 = Math.Min(bitmap.Height, y0 + cellSize);

                    for (var col = 0; col < cols; col++)
                    {
                        var x0 = col * cellSize;
                        var x1 = Math.Min(bitmap.Width, x0 + cellSize);
                        var darkPixels = 0;
                        var totalPixels = 0;

                        for (var y = y0; y < y1; y++)
                        {
                            var rowOffset = y * strideAbs;
                            for (var x = x0; x < x1; x++)
                            {
                                var idx = rowOffset + (x * bytesPerPixel);
                                if (idx + 2 >= bytes.Length)
                                    continue;

                                totalPixels++;
                                var luminance = (bytes[idx + 2] + bytes[idx + 1] + bytes[idx]) / 3;
                                if (luminance < WhiteThreshold)
                                    darkPixels++;
                            }
                        }

                        if (totalPixels > 0 && (darkPixels / (float)totalPixels) >= 0.028f)
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
            bool isMaterial = false,
            bool materialYIsBottomAnchor = false)
        {
            if (isMaterial)
            {
                ResolveBlockGeometry(
                    anchorX,
                    anchorY,
                    blockWidthPt,
                    blockHeightPt,
                    alignment,
                    materialYIsBottomAnchor,
                    out var centerX,
                    out var anchorBottomY);

                if (IsInsideDrawingCore(
                        anchorX,
                        anchorY,
                        blockWidthPt,
                        blockHeightPt,
                        pageWidthPt,
                        pageHeightPt,
                        alignment,
                        materialYIsBottomAnchor))
                {
                    return false;
                }

                if (IsOverlappingTitleBlock(
                        anchorX,
                        anchorY,
                        blockWidthPt,
                        blockHeightPt,
                        pageWidthPt,
                        pageHeightPt,
                        alignment,
                        materialYIsBottomAnchor))
                {
                    return false;
                }

                if (WatermarkProcessingContext.BatchMode)
                {
                    var grid = GetOrCreateDarknessGrid(pdfPath, pageNumber, pageWidthPt, pageHeightPt);
                    if (grid != null)
                        return grid.MeasureCenterBlockDarkness(centerX, anchorBottomY, blockWidthPt, blockHeightPt)
                               <= MaterialGridMaxDarkness;
                }

                return MeasureMaterialBlockDarknessPrecise(
                           pdfPath,
                           pageNumber,
                           centerX,
                           anchorBottomY,
                           blockWidthPt,
                           blockHeightPt,
                           pageWidthPt,
                           pageHeightPt)
                       <= MaterialPreferredMaxDarkness;
            }

            var maxDark = MaxAcceptableDarkness;
            ResolveBlockGeometry(
                anchorX,
                anchorY,
                blockWidthPt,
                blockHeightPt,
                alignment,
                materialYIsBottomAnchor: false,
                out _,
                out var measureBottomY);

            var leftPt = alignment == 2
                ? anchorX - blockWidthPt
                : alignment == 1
                    ? anchorX - (blockWidthPt / 2f)
                    : anchorX;

            return MeasurePlacementDarkness(
                       pdfPath,
                       pageNumber,
                       leftPt,
                       measureBottomY,
                       blockWidthPt,
                       blockHeightPt,
                       pageWidthPt,
                       pageHeightPt,
                       alignment: 0) <= maxDark;
        }

        private static float DistancePenalty(float anchorX, float anchorY, float targetX, float targetY, float pageWidth, float pageHeight)
        {
            var dx = (anchorX - targetX) / Math.Max(120f, pageWidth * 0.35f);
            var dy = (anchorY - targetY) / Math.Max(80f, pageHeight * 0.18f);
            return (dx * dx) + (dy * dy * 2.5f);
        }

        [HandleProcessCorruptedStateExceptions]
        [SecurityCritical]
        private static float MeasureDarkness(Bitmap bitmap, int left, int top, int width, int height, int paddingPx = 0)
        {
            try
            {
                if (bitmap == null || bitmap.Width <= 0 || bitmap.Height <= 0)
                    return 1f;

                long dark = 0;
                long total = 0;
                var rect = new Rectangle(left - paddingPx, top - paddingPx, width + (paddingPx * 2), height + (paddingPx * 2));
                var clipped = Rectangle.Intersect(rect, new Rectangle(0, 0, bitmap.Width, bitmap.Height));
                if (clipped.Width <= 0 || clipped.Height <= 0)
                    return 1f;

                // Exotische/Indexed-Formate und negative Stride (bottom-up) führen sonst zu AV in Marshal.Copy.
                Bitmap owned = null;
                var source = bitmap;
                try
                {
                    if (bitmap.PixelFormat != PixelFormat.Format32bppArgb
                        && bitmap.PixelFormat != PixelFormat.Format32bppRgb
                        && bitmap.PixelFormat != PixelFormat.Format24bppRgb)
                    {
                        owned = new Bitmap(clipped.Width, clipped.Height, PixelFormat.Format32bppArgb);
                        using (var g = Graphics.FromImage(owned))
                        {
                            g.DrawImage(
                                bitmap,
                                new Rectangle(0, 0, clipped.Width, clipped.Height),
                                clipped,
                                GraphicsUnit.Pixel);
                        }

                        source = owned;
                        clipped = new Rectangle(0, 0, owned.Width, owned.Height);
                    }

                    var bytesPerPixel = Image.GetPixelFormatSize(source.PixelFormat) / 8;
                    if (bytesPerPixel < 3)
                        return 1f;

                    var data = source.LockBits(clipped, ImageLockMode.ReadOnly, source.PixelFormat);
                    try
                    {
                        var strideAbs = Math.Abs(data.Stride);
                        if (strideAbs <= 0 || data.Height <= 0)
                            return 1f;

                        var byteCount = checked(strideAbs * data.Height);
                        var bytes = new byte[byteCount];
                        CopyLockedBitmapBytes(data, bytes, strideAbs);

                        for (var row = 0; row < data.Height; row++)
                        {
                            var rowOffset = row * strideAbs;
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
                        source.UnlockBits(data);
                    }
                }
                finally
                {
                    if (owned != null)
                        owned.Dispose();
                }

                return total == 0 ? 1f : dark / (float)total;
            }
            catch
            {
                return 1f;
            }
        }

        private static void CopyLockedBitmapBytes(BitmapData data, byte[] destination, int strideAbs)
        {
            if (data == null || destination == null || strideAbs <= 0 || data.Height <= 0)
                return;

            if (data.Stride > 0)
            {
                Marshal.Copy(data.Scan0, destination, 0, Math.Min(destination.Length, strideAbs * data.Height));
                return;
            }

            // Bottom-up: Scan0 zeigt auf die unterste Zeile, Stride ist negativ.
            for (var y = 0; y < data.Height; y++)
            {
                var src = IntPtr.Add(data.Scan0, y * data.Stride);
                var destOffset = y * strideAbs;
                if (destOffset + strideAbs > destination.Length)
                    break;
                Marshal.Copy(src, destination, destOffset, strideAbs);
            }
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

            try
            {
                var pageIndex = Math.Max(0, pageNumber - 1);
                var scalingFactor = renderDpi / 72.0;

                // Docnet/pdfium kann Dateien gelegentlich länger locken (z. B. _GESAMT_*.pdf in Vorschau).
                // Wenn ein byte[]-Overload verfügbar ist, verwenden wir ihn, um den Dateihandle sofort freizugeben.
                object docReaderObj = null;
                var docLib = DocLib.Instance;
                try
                {
                    var docLibType = docLib.GetType();
                    var pageDim = new PageDimensions(scalingFactor);
                    var byteOverload = docLibType.GetMethods()
                        .FirstOrDefault(m =>
                            string.Equals(m.Name, "GetDocReader", StringComparison.Ordinal)
                            && m.GetParameters().Length == 2
                            && m.GetParameters()[0].ParameterType == typeof(byte[])
                            && m.GetParameters()[1].ParameterType == typeof(PageDimensions));

                    if (byteOverload != null)
                    {
                        var bytes = File.ReadAllBytes(pdfPath);
                        docReaderObj = byteOverload.Invoke(docLib, new object[] { bytes, pageDim });
                    }
                    else
                    {
                        docReaderObj = docLibType.GetMethod("GetDocReader", new[] { typeof(string), typeof(PageDimensions) })
                            ?.Invoke(docLib, new object[] { pdfPath, pageDim });
                    }
                }
                catch
                {
                    docReaderObj = null;
                }

                if (docReaderObj == null)
                {
                    docReaderObj = docLib.GetType()
                        .GetMethod("GetDocReader", new[] { typeof(string), typeof(PageDimensions) })
                        ?.Invoke(docLib, new object[] { pdfPath, new PageDimensions(scalingFactor) });
                }

                using (var docReader = docReaderObj as IDisposable)
                {
                    if (docReader == null)
                        return null;

                    var docReaderLive = docReaderObj;
                    var docReaderType = docReaderLive.GetType();
                    var getPageCount = docReaderType.GetMethod("GetPageCount", Type.EmptyTypes);
                    var getPageReader = docReaderType.GetMethod("GetPageReader", new[] { typeof(int) });
                    if (getPageCount == null || getPageReader == null)
                        return null;

                    var pageCount = (int)getPageCount.Invoke(docReaderLive, null);
                    if (pageIndex >= pageCount)
                        return null;

                    var pageReaderLive = getPageReader.Invoke(docReaderLive, new object[] { pageIndex });
                    using (var pageReader = pageReaderLive as IDisposable)
                    {
                        if (pageReaderLive == null)
                            return null;

                        var pageReaderType = pageReaderLive.GetType();
                        var getW = pageReaderType.GetMethod("GetPageWidth", Type.EmptyTypes);
                        var getH = pageReaderType.GetMethod("GetPageHeight", Type.EmptyTypes);
                        var getImg = pageReaderType.GetMethod("GetImage", Type.EmptyTypes);
                        if (getW == null || getH == null || getImg == null)
                            return null;

                        var width = (int)getW.Invoke(pageReaderLive, null);
                        var height = (int)getH.Invoke(pageReaderLive, null);
                        var bytes = getImg.Invoke(pageReaderLive, null) as byte[];
                        if (bytes == null || bytes.Length == 0 || width <= 0 || height <= 0)
                            return null;

                        var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
                        using (var g = Graphics.FromImage(bitmap))
                        {
                            // pdfium liefert oft transparente Pixel (RGBA=0) statt Weiß –
                            // ohne Weiß-Untergrund war MeasureDarkness überall 1.0.
                            g.Clear(Color.White);
                        }

                        var rect = new Rectangle(0, 0, width, height);
                        var data = bitmap.LockBits(rect, ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);
                        try
                        {
                            var strideAbs = Math.Abs(data.Stride);
                            if (strideAbs <= 0)
                                return null;

                            var destLength = checked(strideAbs * height);
                            var length = Math.Min(bytes.Length, destLength);
                            var raw = new byte[length];
                            Buffer.BlockCopy(bytes, 0, raw, 0, length);

                            var dest = new byte[destLength];
                            CopyLockedBitmapBytes(data, dest, strideAbs);

                            var bpp = 4;
                            for (var i = 0; i + 3 < length && i + 3 < dest.Length; i += bpp)
                            {
                                var a = raw[i + 3];
                                if (a >= 250)
                                {
                                    dest[i] = raw[i];
                                    dest[i + 1] = raw[i + 1];
                                    dest[i + 2] = raw[i + 2];
                                    dest[i + 3] = 255;
                                    continue;
                                }

                                if (a <= 2)
                                    continue; // Weiß aus Clear bleibt

                                // Auf Weiß aufblendieren.
                                var inv = 255 - a;
                                dest[i] = (byte)(((raw[i] * a) + (255 * inv)) / 255);
                                dest[i + 1] = (byte)(((raw[i + 1] * a) + (255 * inv)) / 255);
                                dest[i + 2] = (byte)(((raw[i + 2] * a) + (255 * inv)) / 255);
                                dest[i + 3] = 255;
                            }

                            if (data.Stride > 0)
                            {
                                Marshal.Copy(dest, 0, data.Scan0, dest.Length);
                            }
                            else
                            {
                                for (var y = 0; y < height; y++)
                                {
                                    var dstPtr = IntPtr.Add(data.Scan0, y * data.Stride);
                                    Marshal.Copy(dest, y * strideAbs, dstPtr, strideAbs);
                                }
                            }
                        }
                        finally
                        {
                            bitmap.UnlockBits(data);
                        }

                        return bitmap;
                    }
                }
            }
            catch
            {
                return null;
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

        public static Bitmap RenderPageForOcr(string pdfPath, int pageNumber, int renderDpi)
        {
            EnsureLoaded();
            if (!_available)
                return null;

            return GetOrRenderPage(pdfPath, pageNumber, Math.Max(VisionRenderDpi, renderDpi));
        }

        public static Bitmap RenderPageForPreview(string pdfPath, int pageNumber)
        {
            EnsureLoaded();
            if (!_available)
                return null;

            return GetOrRenderPage(pdfPath, pageNumber, 110);
        }

        public static float MeasureMarginStripDarkness(
            string pdfPath,
            int pageNumber,
            float pageWidthPt,
            float pageHeightPt,
            PdfMarginStripKind strip)
        {
            EnsureLoaded();
            if (!_available
                || string.IsNullOrWhiteSpace(pdfPath)
                || !File.Exists(pdfPath)
                || pageWidthPt <= 0f
                || pageHeightPt <= 0f)
            {
                return 0f;
            }

            try
            {
                using (var bitmap = GetOrRenderPage(pdfPath, pageNumber, MaterialVerifyRenderDpi))
                {
                    if (bitmap == null)
                        return 0f;

                    var scaleX = bitmap.Width / pageWidthPt;
                    var scaleY = bitmap.Height / pageHeightPt;

                    float leftPt;
                    float bottomPt;
                    float widthPt;
                    float heightPt;

                    switch (strip)
                    {
                        case PdfMarginStripKind.Top:
                            leftPt = 0f;
                            widthPt = pageWidthPt;
                            heightPt = pageHeightPt * 0.075f;
                            bottomPt = pageHeightPt - heightPt;
                            break;
                        case PdfMarginStripKind.TopLeft:
                            leftPt = 0f;
                            widthPt = pageWidthPt * 0.28f;
                            heightPt = pageHeightPt * 0.12f;
                            bottomPt = pageHeightPt - heightPt;
                            break;
                        default:
                            leftPt = 0f;
                            widthPt = pageWidthPt * 0.07f;
                            heightPt = pageHeightPt * 0.82f;
                            bottomPt = pageHeightPt * 0.12f;
                            break;
                    }

                    var leftPx = (int)(leftPt * scaleX);
                    var topPx = (int)(bitmap.Height - ((bottomPt + heightPt) * scaleY));
                    var widthPx = Math.Max(8, (int)Math.Ceiling(widthPt * scaleX));
                    var heightPx = Math.Max(8, (int)Math.Ceiling(heightPt * scaleY));
                    return MeasureDarkness(bitmap, leftPx, topPx, widthPx, heightPx);
                }
            }
            catch
            {
                return 0f;
            }
        }

        /// <summary>
        /// Freier Rand von der Blattkante bis zur äußeren oberen Rahmenlinie, in pt.
        /// Negativ = nicht messbar.
        /// </summary>
        public static float MeasureFreeTopMarginPt(
            string pdfPath,
            int pageNumber,
            float pageWidthPt,
            float pageHeightPt)
        {
            EnsureLoaded();
            if (!_available
                || string.IsNullOrWhiteSpace(pdfPath)
                || !File.Exists(pdfPath)
                || pageWidthPt <= 0f
                || pageHeightPt <= 0f)
            {
                return -1f;
            }

            try
            {
                using (var bitmap = GetOrRenderPage(pdfPath, pageNumber, MaterialVerifyRenderDpi))
                {
                    if (bitmap == null || bitmap.Width < 16 || bitmap.Height < 16)
                        return -1f;

                    if (!TryFindTopFrameRowPx(bitmap, pageWidthPt, pageHeightPt, out var bestY, out _))
                        return -1f;

                    var scaleY = bitmap.Height / pageHeightPt;
                    return bestY / scaleY;
                }
            }
            catch
            {
                return -1f;
            }
        }

        /// <summary>
        /// Freier Weißraum unter dem äußeren Zeichnungsrahmen (pt, von Blattunterkante).
        /// </summary>
        public static float MeasureFreeBottomMarginPt(
            string pdfPath,
            int pageNumber,
            float pageWidthPt,
            float pageHeightPt)
        {
            EnsureLoaded();
            if (!_available
                || string.IsNullOrWhiteSpace(pdfPath)
                || !File.Exists(pdfPath)
                || pageWidthPt <= 0f
                || pageHeightPt <= 0f)
            {
                return -1f;
            }

            try
            {
                using (var bitmap = GetOrRenderPage(pdfPath, pageNumber, MaterialVerifyRenderDpi))
                {
                    if (bitmap == null || bitmap.Width < 16 || bitmap.Height < 16)
                        return -1f;

                    if (!TryFindBottomFrameRowPx(bitmap, pageWidthPt, pageHeightPt, out var bestYFromBottom, out _))
                        return -1f;

                    var scaleY = bitmap.Height / pageHeightPt;
                    return bestYFromBottom / scaleY;
                }
            }
            catch
            {
                return -1f;
            }
        }

        /// <summary>Debug: Dunkelheitsprofil der oberen Zeilen.</summary>
        public static void DumpTopMarginProfile(
            string pdfPath,
            int pageNumber,
            float pageWidthPt,
            float pageHeightPt)
        {
            EnsureLoaded();
            if (!_available || string.IsNullOrWhiteSpace(pdfPath) || !File.Exists(pdfPath))
                return;

            try
            {
                using (var bitmap = GetOrRenderPage(pdfPath, pageNumber, MaterialVerifyRenderDpi))
                {
                    if (bitmap == null)
                        return;

                    var scaleY = bitmap.Height / pageHeightPt;
                    var insetX = Math.Max(2, bitmap.Width / 20);
                    var widthPx = Math.Max(8, bitmap.Width - (insetX * 2));
                    var rowH = Math.Max(2, (int)Math.Ceiling(1.2f * scaleY));
                    var scanHeightPx = Math.Min(bitmap.Height, Math.Max(12, (int)Math.Ceiling(pageHeightPt * 0.22f * scaleY)));
                    Console.WriteLine($"Profile bitmap={bitmap.Width}x{bitmap.Height} scan={scanHeightPx}px");
                    for (var y = 0; y < Math.Min(scanHeightPx, 80); y += Math.Max(1, rowH))
                    {
                        var d = MeasureDarkness(bitmap, insetX, y, widthPx, rowH);
                        if (d >= 0.008f || y < 10)
                            Console.WriteLine($"  yPx={y,3} pt={y / scaleY,6:0.0} dark={d:0.0000}");
                    }

                    if (TryFindTopFrameRowPx(bitmap, pageWidthPt, pageHeightPt, out var bestY, out var bestDark))
                        Console.WriteLine($"  BEST yPx={bestY} pt={bestY / scaleY:0.0} dark={bestDark:0.0000}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("Profile error: " + ex.Message);
            }
        }

        private static bool TryFindTopFrameRowPx(
            Bitmap bitmap,
            float pageWidthPt,
            float pageHeightPt,
            out int bestY,
            out float bestDark)
        {
            bestY = -1;
            bestDark = 0f;
            if (bitmap == null || pageHeightPt <= 0f)
                return false;

            var scaleY = bitmap.Height / pageHeightPt;
            var scanHeightPx = Math.Max(12, (int)Math.Ceiling(pageHeightPt * 0.22f * scaleY));
            scanHeightPx = Math.Min(scanHeightPx, bitmap.Height);
            var insetX = Math.Max(2, bitmap.Width / 20);
            var widthPx = Math.Max(8, bitmap.Width - (insetX * 2));
            var rowH = Math.Max(2, (int)Math.Ceiling(1.2f * scaleY));

            // Erste kräftige Linie NACH einem hellen Streifen (äußerer Rahmen, nicht y=0-Rauschen).
            var sawBright = false;
            var peakY = -1;
            var peakDark = 0f;
            for (var y = 0; y < scanHeightPx - rowH; y++)
            {
                var darkness = MeasureDarkness(bitmap, insetX, y, widthPx, rowH);
                if (darkness < 0.008f)
                    sawBright = true;

                if (!sawBright && y < (int)(2f * scaleY))
                    continue;

                if (darkness > peakDark)
                {
                    peakDark = darkness;
                    peakY = y;
                }
            }

            if (peakY < 0 || peakDark < 0.012f)
            {
                peakY = -1;
                peakDark = 0f;
                for (var y = 0; y < scanHeightPx - rowH; y++)
                {
                    var darkness = MeasureDarkness(bitmap, insetX, y, widthPx, rowH);
                    if (darkness > peakDark)
                    {
                        peakDark = darkness;
                        peakY = y;
                    }
                }
            }

            if (peakY < 0 || peakDark < 0.012f)
                return false;

            // Oberkante der dunklen Bandbreite (Richtung Blattrand) – nicht die Linienmitte.
            // Sonst liegt die Baseline in der Rahmenlinie und der Strich geht durch den Text.
            var bandThreshold = Math.Max(0.08f, peakDark * 0.35f);
            var topY = peakY;
            for (var y = peakY; y >= 0; y--)
            {
                if (MeasureDarkness(bitmap, insetX, y, widthPx, rowH) >= bandThreshold)
                    topY = y;
                else
                    break;
            }

            bestY = topY;
            bestDark = peakDark;
            return true;
        }

        /// <summary>
        /// Untere Rahmenlinie: Abstand in Pixeln von der Bitmap-Unterkante.
        /// </summary>
        private static bool TryFindBottomFrameRowPx(
            Bitmap bitmap,
            float pageWidthPt,
            float pageHeightPt,
            out int bestYFromBottom,
            out float bestDark)
        {
            bestYFromBottom = -1;
            bestDark = 0f;
            if (bitmap == null || pageHeightPt <= 0f)
                return false;

            var scaleY = bitmap.Height / pageHeightPt;
            // Bis 40 % der Seite – äußerer Rahmen darf tiefer liegen als der Titelblock-Scan.
            var scanHeightPx = Math.Max(12, (int)Math.Ceiling(pageHeightPt * 0.40f * scaleY));
            scanHeightPx = Math.Min(scanHeightPx, bitmap.Height);
            var insetX = Math.Max(2, bitmap.Width / 20);
            var widthPx = Math.Max(8, bitmap.Width - (insetX * 2));
            var rowH = Math.Max(2, (int)Math.Ceiling(1.2f * scaleY));

            var sawBright = false;
            var peakY = -1;
            var peakDark = 0f;
            for (var dist = 0; dist < scanHeightPx - rowH; dist++)
            {
                var y = bitmap.Height - rowH - dist;
                if (y < 0)
                    break;

                var darkness = MeasureDarkness(bitmap, insetX, y, widthPx, rowH);
                if (darkness < 0.008f)
                    sawBright = true;

                if (!sawBright && dist < (int)(2f * scaleY))
                    continue;

                if (darkness > peakDark)
                {
                    peakDark = darkness;
                    peakY = y;
                }
            }

            if (peakY < 0 || peakDark < 0.012f)
            {
                peakY = -1;
                peakDark = 0f;
                for (var dist = 0; dist < scanHeightPx - rowH; dist++)
                {
                    var y = bitmap.Height - rowH - dist;
                    if (y < 0)
                        break;

                    var darkness = MeasureDarkness(bitmap, insetX, y, widthPx, rowH);
                    if (darkness > peakDark)
                    {
                        peakDark = darkness;
                        peakY = y;
                    }
                }
            }

            if (peakY < 0 || peakDark < 0.012f)
                return false;

            var bandThreshold = Math.Max(0.08f, peakDark * 0.35f);
            var bottomEdgeY = peakY;
            for (var y = peakY; y + rowH < bitmap.Height; y++)
            {
                if (MeasureDarkness(bitmap, insetX, y, widthPx, rowH) >= bandThreshold)
                    bottomEdgeY = y;
                else
                    break;
            }

            bestYFromBottom = Math.Max(0, bitmap.Height - (bottomEdgeY + rowH));
            bestDark = peakDark;
            return true;
        }

        /// <summary>
        /// Untere Kante des äußeren Zeichnungsrahmens in PDF-Koordinaten (Y nach oben).
        /// </summary>
        public static bool TryGetDrawingFrameBottomY(
            string pdfPath,
            int pageNumber,
            float pageWidthPt,
            float pageHeightPt,
            out float frameBottomY)
        {
            frameBottomY = 0f;
            var freeBelow = MeasureFreeBottomMarginPt(pdfPath, pageNumber, pageWidthPt, pageHeightPt);
            if (freeBelow < 0f)
                return false;

            frameBottomY = freeBelow;
            return true;
        }

        /// <summary>
        /// Obere Kante des äußeren Zeichnungsrahmens in PDF-Koordinaten (Y nach oben).
        /// </summary>
        public static bool TryGetDrawingFrameTopY(
            string pdfPath,
            int pageNumber,
            float pageWidthPt,
            float pageHeightPt,
            out float frameTopY)
        {
            frameTopY = 0f;
            return TryGetDrawingFrameBounds(
                pdfPath,
                pageNumber,
                pageWidthPt,
                pageHeightPt,
                out _,
                out _,
                out frameTopY);
        }

        /// <summary>
        /// Äußerer Zeichnungsrahmen: links, rechts, oben (PDF-Koordinaten, Y nach oben).
        /// Raster zuerst (schnell, gecacht); Stroke-Parse nur außerhalb des Batch.
        /// </summary>
        public static bool TryGetDrawingFrameBounds(
            string pdfPath,
            int pageNumber,
            float pageWidthPt,
            float pageHeightPt,
            out float frameLeftX,
            out float frameRightX,
            out float frameTopY)
        {
            frameLeftX = 0f;
            frameRightX = pageWidthPt;
            frameTopY = 0f;

            if (string.IsNullOrWhiteSpace(pdfPath)
                || !File.Exists(pdfPath)
                || pageWidthPt <= 0f
                || pageHeightPt <= 0f)
            {
                return false;
            }

            var cacheKey = pdfPath + "|" + pageNumber.ToString(System.Globalization.CultureInfo.InvariantCulture)
                           + "|" + ((int)pageWidthPt) + "x" + ((int)pageHeightPt);
            lock (CacheLock)
            {
                if (FrameBoundsCache.TryGetValue(cacheKey, out var cached))
                {
                    if (!cached.Ok)
                        return false;
                    frameLeftX = cached.Left;
                    frameRightX = cached.Right;
                    frameTopY = cached.Top;
                    return true;
                }
            }

            var ok = TryGetOuterFrameFromRaster(
                pdfPath,
                pageNumber,
                pageWidthPt,
                pageHeightPt,
                out frameLeftX,
                out frameRightX,
                out frameTopY);

            // Kein Stroke-Fallback: Content-Stream-Parse hängt bei CAD-PDFs und blockiert die Sortierung.

            lock (CacheLock)
            {
                FrameBoundsCache[cacheKey] = new FrameBoundsCacheEntry
                {
                    Ok = ok,
                    Left = frameLeftX,
                    Right = frameRightX,
                    Top = frameTopY
                };
            }

            return ok;
        }

        private static bool TryGetOuterFrameFromRaster(
            string pdfPath,
            int pageNumber,
            float pageWidthPt,
            float pageHeightPt,
            out float frameLeftX,
            out float frameRightX,
            out float frameTopY)
        {
            frameLeftX = 0f;
            frameRightX = pageWidthPt;
            frameTopY = 0f;

            EnsureLoaded();
            if (!_available)
                return false;

            try
            {
                using (var bitmap = GetOrRenderPage(pdfPath, pageNumber, MaterialVerifyRenderDpi))
                {
                    if (bitmap == null || bitmap.Width < 16 || bitmap.Height < 16)
                        return false;

                    var scaleX = bitmap.Width / pageWidthPt;
                    var scaleY = bitmap.Height / pageHeightPt;

                    var freeAbove = MeasureFreeTopMarginPt(pdfPath, pageNumber, pageWidthPt, pageHeightPt);
                    if (freeAbove < 0f)
                        return false;

                    frameTopY = pageHeightPt - freeAbove;
                    // Rahmen darf am Blattrand liegen (freeAbove≈0); nur zu tief ablehnen.
                    if (frameTopY < pageHeightPt * 0.70f || frameTopY > pageHeightPt + 0.5f)
                        return false;

                    // Endpunkte der oberen Rahmenlinie = äußere linke/rechte Ecke.
                    // (Vertikal-Scan unterhalb trifft oft die innere Gitterkante nach den Buchstaben F/E/D.)
                    var rowH = Math.Max(2, (int)Math.Ceiling(1.5f * scaleY));
                    var colW = Math.Max(2, (int)Math.Ceiling(1.0f * scaleX));
                    var baseRowPx = (int)Math.Round(freeAbove * scaleY);
                    if (baseRowPx < 0)
                        baseRowPx = 0;
                    if (baseRowPx > bitmap.Height - rowH - 1)
                        baseRowPx = Math.Max(0, bitmap.Height - rowH - 1);

                    const float lineThreshold = 0.010f;
                    var maxInsetPx = Math.Max(8, (int)(pageWidthPt * 0.35f * scaleX));

                    var leftPx = -1;
                    var rightPx = -1;
                    for (var dy = -1; dy <= 2; dy++)
                    {
                        var rowPx = baseRowPx + dy;
                        if (rowPx < 0 || rowPx + rowH >= bitmap.Height)
                            continue;

                        var localLeft = -1;
                        for (var x = 0; x < maxInsetPx - colW; x++)
                        {
                            if (MeasureDarkness(bitmap, x, rowPx, colW, rowH) >= lineThreshold)
                            {
                                localLeft = x;
                                break;
                            }
                        }

                        var localRight = -1;
                        for (var x = bitmap.Width - colW; x > bitmap.Width - maxInsetPx; x--)
                        {
                            if (MeasureDarkness(bitmap, x, rowPx, colW, rowH) >= lineThreshold)
                            {
                                localRight = x + colW;
                                break;
                            }
                        }

                        if (localLeft < 0 || localRight < 0 || localRight <= localLeft + 20)
                            continue;

                        // Äußerste Endpunkte über benachbarte Zeilen behalten.
                        if (leftPx < 0 || localLeft < leftPx)
                            leftPx = localLeft;
                        if (rightPx < 0 || localRight > rightPx)
                            rightPx = localRight;
                    }

                    if (leftPx < 0 || rightPx < 0 || rightPx <= leftPx + 20)
                        return false;

                    frameLeftX = leftPx / scaleX;
                    frameRightX = rightPx / scaleX;

                    var width = frameRightX - frameLeftX;
                    if (width < pageWidthPt * 0.40f || width > pageWidthPt * 0.998f)
                        return false;

                    return true;
                }
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Äußerer Rahmen aus PDF-Vektorrechtecken / langen Linien (zuverlässiger als Raster).
        /// </summary>
        private static bool TryGetOuterFrameFromStrokes(
            string pdfPath,
            int pageNumber,
            float pageWidthPt,
            float pageHeightPt,
            out float frameLeftX,
            out float frameRightX,
            out float frameTopY)
        {
            frameLeftX = 0f;
            frameRightX = pageWidthPt;
            frameTopY = 0f;

            try
            {
                var strokes = PdfContentStrokeExtractor.ExtractStrokeRects(
                    pdfPath, pageNumber, pageWidthPt, pageHeightPt);
                if (strokes == null || strokes.Count == 0)
                    return false;

                PdfTextRect bestRect = null;
                var bestArea = 0f;
                foreach (var rect in strokes)
                {
                    if (rect == null)
                        continue;

                    var w = rect.Right - rect.Left;
                    var h = rect.Top - rect.Bottom;
                    if (w < pageWidthPt * 0.55f || h < pageHeightPt * 0.55f)
                        continue;
                    if (rect.Top < pageHeightPt * 0.72f)
                        continue;
                    if (rect.Left < -2f || rect.Right > pageWidthPt + 2f)
                        continue;
                    // Blatt-Rand-Rechteck (fast 0,0) überspringen – wir wollen den Zeichnungsrahmen.
                    if (rect.Left <= 1.5f && rect.Right >= pageWidthPt - 1.5f
                        && rect.Bottom <= 1.5f && rect.Top >= pageHeightPt - 1.5f)
                        continue;

                    var area = w * h;
                    if (area > bestArea)
                    {
                        bestArea = area;
                        bestRect = rect;
                    }
                }

                if (bestRect != null)
                {
                    frameLeftX = bestRect.Left;
                    frameRightX = bestRect.Right;
                    frameTopY = bestRect.Top;
                    return true;
                }

                // Fallback: Rahmen als Linienzug (kein "re") – lange Vertikalen/Horizontalen.
                float? left = null;
                float? right = null;
                float? top = null;
                foreach (var rect in strokes)
                {
                    if (rect == null)
                        continue;

                    var w = rect.Right - rect.Left;
                    var h = rect.Top - rect.Bottom;
                    var cx = (rect.Left + rect.Right) * 0.5f;
                    var cy = (rect.Bottom + rect.Top) * 0.5f;

                    // Lange Vertikale nahe dem linken/rechten Rand.
                    if (h >= pageHeightPt * 0.45f && w <= 8f)
                    {
                        if (cx <= pageWidthPt * 0.22f)
                            left = left.HasValue ? Math.Min(left.Value, cx) : cx;
                        if (cx >= pageWidthPt * 0.78f)
                            right = right.HasValue ? Math.Max(right.Value, cx) : cx;
                    }

                    // Lange Horizontale nahe der oberen Kante.
                    if (w >= pageWidthPt * 0.45f && h <= 8f && cy >= pageHeightPt * 0.72f)
                        top = top.HasValue ? Math.Max(top.Value, cy) : cy;
                }

                if (!left.HasValue || !right.HasValue || !top.HasValue)
                    return false;
                if (right.Value - left.Value < pageWidthPt * 0.40f)
                    return false;

                frameLeftX = left.Value;
                frameRightX = right.Value;
                frameTopY = top.Value;
                return true;
            }
            catch
            {
                return false;
            }
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
                if (!NativeDependencyService.TryEnsurePdfium(out var dependencyError))
                {
                    NativeDependencyService.TryRepairPdfium(AppDomain.CurrentDomain.BaseDirectory);
                }

                if (!NativeDependencyService.TryEnsurePdfium(out dependencyError))
                {
                    _loadError = dependencyError;
                    _available = false;
                    return;
                }

                var baseDir = AppDomain.CurrentDomain.BaseDirectory;
                var docnetPath = Path.Combine(baseDir, "Docnet.Core.dll");
                if (!File.Exists(docnetPath))
                {
                    _loadError = "Docnet.Core.dll fehlt im App-Ordner.";
                    _available = false;
                    return;
                }

                _available = true;
            }
            catch (BadImageFormatException)
            {
                _loadError = NativeDependencyService.FormatBadImageMessage();
                _available = false;
            }
            catch (Exception ex)
            {
                _loadError = NativeDependencyService.IsBadImageFormatError(ex)
                    ? NativeDependencyService.FormatBadImageMessage()
                    : ex.Message;
                _available = false;
            }
        }
    }
}
