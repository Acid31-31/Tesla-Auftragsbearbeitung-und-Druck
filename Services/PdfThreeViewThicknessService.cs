using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace PDF_umbenenen.Services
{
    /// <summary>
    /// Ermittelt Blechstärke aus der typischen 3-Ansichten-Anordnung (Hauptansicht + Seitenansicht + Isometrie).
    /// Die Seitenansicht ist das schmale Profil rechts (oder oberhalb) der Hauptansicht.
    /// </summary>
    internal static class PdfThreeViewThicknessService
    {
        private static readonly Regex IsolatedThicknessRegex = new Regex(
            @"^(\d{1,2}([.,]\d{1,2})?)\s*(?:mm|MM)?$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

        private sealed class ViewCluster
        {
            public int LeftPx { get; set; }
            public int RightPx { get; set; }
            public int TopPx { get; set; }
            public int BottomPx { get; set; }
            public int InkCount { get; set; }

            public int WidthPx => Math.Max(1, RightPx - LeftPx + 1);
            public int HeightPx => Math.Max(1, BottomPx - TopPx + 1);
            public int AreaPx => WidthPx * HeightPx;
            public int MinDimPx => Math.Min(WidthPx, HeightPx);
            public float CenterXPx => (LeftPx + RightPx) / 2f;
            public float CenterYPx => (TopPx + BottomPx) / 2f;
        }

        public static bool TryCollectSideViewTextCandidates(
            string pdfPath,
            int pageNumber,
            float pageWidthPt,
            float pageHeightPt,
            IList<PdfTextRect> textRects,
            ICollection<ThicknessCandidate> candidates,
            string materialRaw = null)
        {
            if (candidates == null || !PdfRasterLayoutService.IsAvailable)
                return false;

            Bitmap bitmap = null;
            try
            {
                bitmap = PdfRasterLayoutService.RenderPageForThickness(pdfPath, pageNumber, 1);
                if (bitmap == null)
                    return false;

                var drawTop = (int)(bitmap.Height * 0.06f);
                var drawBottom = (int)(bitmap.Height * 0.76f);
                var drawLeft = (int)(bitmap.Width * 0.02f);
                var drawRight = (int)(bitmap.Width * 0.98f);

                var clusters = FindViewClusters(bitmap, drawLeft, drawTop, drawRight, drawBottom);
                if (clusters.Count < 2)
                    return TryAddSideViewTextNearRightEdge(textRects, pageWidthPt, pageHeightPt, bitmap, candidates, materialRaw);

                var frontView = SelectFrontViewCluster(clusters, bitmap.Height, textRects, bitmap, pageWidthPt, pageHeightPt);
                var sideViews = IdentifySideViewClusters(clusters, frontView);
                if (sideViews.Count == 0)
                    return TryAddSideViewTextNearRightEdge(textRects, pageWidthPt, pageHeightPt, bitmap, candidates, materialRaw);

                var added = false;
                foreach (var sideView in sideViews)
                {
                    var textThickness = TryReadThicknessTextNearView(textRects, pageWidthPt, pageHeightPt, bitmap, sideView, materialRaw);
                    if (textThickness <= 0f)
                        continue;

                    ThicknessCandidateResolver.AddCandidate(
                        candidates,
                        textThickness,
                        ThicknessCandidateResolver.PriorityThreeViewText,
                        "3-ansichten-bemassung");
                    added = true;
                }

                return added || TryAddSideViewTextNearRightEdge(textRects, pageWidthPt, pageHeightPt, bitmap, candidates, materialRaw);
            }
            catch
            {
                return false;
            }
            finally
            {
                bitmap?.Dispose();
            }
        }

        private static bool TryAddSideViewTextNearRightEdge(
            IList<PdfTextRect> textRects,
            float pageWidthPt,
            float pageHeightPt,
            Bitmap bitmap,
            ICollection<ThicknessCandidate> candidates,
            string materialRaw = null)
        {
            if (textRects == null || textRects.Count == 0)
                return false;

            var zoneLeftPt = pageWidthPt * 0.35f;
            var zoneRightPt = pageWidthPt * 0.95f;
            var zoneBottomPt = pageHeightPt * 0.12f;
            var zoneTopPt = pageHeightPt * 0.88f;

            var scored = new List<(double Value, float Score)>();

            foreach (var rect in textRects)
            {
                if (!OverlapsPdfZone(rect, zoneLeftPt, zoneRightPt, zoneBottomPt, zoneTopPt))
                    continue;

                var text = Normalize(rect.Text);
                if (!IsolatedThicknessRegex.IsMatch(text))
                    continue;

                if (!TryParseValue(text, out var value) || value < 0.3d || value > ThicknessCandidateResolver.MaxSideViewThicknessMm)
                    continue;

                if (ThicknessCandidateResolver.IsLikelyMaterialNumberFragment(rect.Text, value)
                    || ThicknessCandidateResolver.IsMaterialNumberThicknessFalsePositive(value, materialRaw, rect.Text))
                    continue;

                if (ThicknessCandidateResolver.IsLikelyHoleOrFeatureDimension(rect.Text, value)
                    || ThicknessCandidateResolver.IsLikelyHoleDiameterMm(value))
                    continue;

                var centerX = (rect.Left + rect.Right) / 2f;
                var relX = centerX / Math.Max(1f, pageWidthPt);
                var score = relX >= 0.40f ? 10f : 40f;
                scored.Add((value, score));
            }

            var resolved = ThicknessCandidateResolver.ResolveSideViewThicknessMm(scored);
            if (!resolved.HasValue)
                return false;

            ThicknessCandidateResolver.AddCandidate(
                candidates,
                resolved.Value,
                ThicknessCandidateResolver.PriorityThreeViewText,
                "seitenansicht-zone");
            return true;
        }

        public static bool TryCollectCandidates(
            string pdfPath,
            int pageNumber,
            float pageWidthPt,
            float pageHeightPt,
            int scaleDenominator,
            IList<PdfTextRect> textRects,
            ICollection<ThicknessCandidate> candidates)
        {
            return TryCollectSideViewTextCandidates(
                pdfPath, pageNumber, pageWidthPt, pageHeightPt, textRects, candidates);
        }

        public static bool TryDetectThickness(
            string pdfPath,
            int pageNumber,
            float pageWidthPt,
            float pageHeightPt,
            int scaleDenominator,
            IList<PdfTextRect> textRects,
            out float thicknessMm)
        {
            thicknessMm = 0f;
            var candidates = new List<ThicknessCandidate>();
            if (!TryCollectSideViewTextCandidates(pdfPath, pageNumber, pageWidthPt, pageHeightPt, textRects, candidates))
                return false;

            var best = ThicknessCandidateResolver.ResolveBestMm(candidates, null);
            if (!best.HasValue)
                return false;

            thicknessMm = (float)best.Value;
            return true;
        }

        private static List<ViewCluster> FindViewClusters(Bitmap bitmap, int left, int top, int right, int bottom)
        {
            var width = right - left + 1;
            if (width <= 0)
                return new List<ViewCluster>();

            var colInk = new int[width];
            for (var xi = 0; xi < width; xi++)
            {
                var x = left + xi;
                var count = 0;
                for (var y = top; y <= bottom; y += 2)
                {
                    if (IsGeometryPixel(bitmap, x, y))
                        count++;
                }

                colInk[xi] = count;
            }

            Smooth(colInk, 7);
            var drawHeight = Math.Max(1, bottom - top + 1);
            var segmentThreshold = Math.Max(4, drawHeight / 60);
            var gapThreshold = Math.Max(2, segmentThreshold / 4);
            var minGapWidth = Math.Max(6, width / 120);

            var segments = ExtractSegments(colInk, segmentThreshold, gapThreshold, minGapWidth);
            var clusters = new List<ViewCluster>();

            foreach (var segment in segments)
            {
                var segLeft = left + segment.Item1;
                var segRight = left + segment.Item2;
                var cluster = BuildCluster(bitmap, segLeft, segRight, top, bottom);
                if (cluster == null)
                    continue;

                if (cluster.InkCount < Math.Max(120, drawHeight / 2))
                    continue;

                if (cluster.WidthPx < 4 || cluster.HeightPx < 12)
                    continue;

                clusters.Add(cluster);
            }

            return MergeOverlappingClusters(clusters);
        }

        private static ViewCluster BuildCluster(Bitmap bitmap, int left, int right, int top, int bottom)
        {
            var ink = 0;
            var minY = bottom;
            var maxY = top;

            for (var x = left; x <= right; x += 2)
            {
                for (var y = top; y <= bottom; y += 2)
                {
                    if (!IsGeometryPixel(bitmap, x, y))
                        continue;

                    ink++;
                    if (y < minY)
                        minY = y;
                    if (y > maxY)
                        maxY = y;
                }
            }

            if (ink == 0 || maxY <= minY)
                return null;

            return new ViewCluster
            {
                LeftPx = left,
                RightPx = right,
                TopPx = minY,
                BottomPx = maxY,
                InkCount = ink
            };
        }

        private static List<ViewCluster> MergeOverlappingClusters(List<ViewCluster> clusters)
        {
            if (clusters.Count <= 1)
                return clusters;

            var ordered = clusters.OrderBy(c => c.LeftPx).ToList();
            var merged = new List<ViewCluster> { ordered[0] };

            for (var i = 1; i < ordered.Count; i++)
            {
                var prev = merged[merged.Count - 1];
                var cur = ordered[i];
                var gap = cur.LeftPx - prev.RightPx;
                var overlap = prev.RightPx >= cur.LeftPx - 8;

                if (overlap || gap < Math.Max(12, prev.WidthPx * 0.08f))
                {
                    prev.LeftPx = Math.Min(prev.LeftPx, cur.LeftPx);
                    prev.RightPx = Math.Max(prev.RightPx, cur.RightPx);
                    prev.TopPx = Math.Min(prev.TopPx, cur.TopPx);
                    prev.BottomPx = Math.Max(prev.BottomPx, cur.BottomPx);
                    prev.InkCount += cur.InkCount;
                }
                else
                {
                    merged.Add(cur);
                }
            }

            return merged;
        }

        private static ViewCluster SelectFrontViewCluster(
            IList<ViewCluster> clusters,
            int bitmapHeight,
            IList<PdfTextRect> textRects,
            Bitmap bitmap,
            float pageWidthPt,
            float pageHeightPt)
        {
            var upperLimit = bitmapHeight * 0.46f;
            var upperClusters = clusters
                .Where(c => c.CenterYPx <= upperLimit)
                .Where(c => !IsAbwicklungCluster(c, textRects, bitmap, pageWidthPt, pageHeightPt))
                .ToList();

            var pool = upperClusters.Count >= 2 ? upperClusters : clusters.Where(c => !IsAbwicklungCluster(c, textRects, bitmap, pageWidthPt, pageHeightPt)).ToList();
            if (pool.Count == 0)
                pool = clusters.ToList();

            return pool.OrderByDescending(c => c.InkCount).ThenByDescending(c => c.AreaPx).First();
        }

        private static bool IsAbwicklungCluster(
            ViewCluster cluster,
            IList<PdfTextRect> textRects,
            Bitmap bitmap,
            float pageWidthPt,
            float pageHeightPt)
        {
            if (textRects == null || textRects.Count == 0)
                return cluster.WidthPx > cluster.HeightPx * 1.6f && cluster.AreaPx > bitmap.Height * bitmap.Width * 0.04f;

            var zoneLeftPt = PixelXToPt(cluster.LeftPx - 20, bitmap.Width, pageWidthPt);
            var zoneRightPt = PixelXToPt(cluster.RightPx + 20, bitmap.Width, pageWidthPt);
            var zoneTopPt = PixelYToPt(cluster.TopPx - 20, bitmap.Height, pageHeightPt);
            var zoneBottomPt = PixelYToPt(cluster.BottomPx + 20, bitmap.Height, pageHeightPt);

            foreach (var rect in textRects)
            {
                if (!OverlapsPdfZone(rect, zoneLeftPt, zoneRightPt, zoneBottomPt, zoneTopPt))
                    continue;

                var text = Normalize(rect.Text);
                if (text.IndexOf("ABWICKLUNG", StringComparison.OrdinalIgnoreCase) >= 0
                    || text.IndexOf("FLAT PATTERN", StringComparison.OrdinalIgnoreCase) >= 0
                    || text.IndexOf("UNFOLD", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
            }

            return false;
        }

        private static List<ViewCluster> IdentifySideViewClusters(IList<ViewCluster> clusters, ViewCluster frontView)
        {
            var result = new List<ViewCluster>();

            foreach (var cluster in clusters)
            {
                if (ReferenceEquals(cluster, frontView))
                    continue;

                if (cluster.AreaPx >= frontView.AreaPx * 0.55f)
                    continue;

                var verticalOverlap = VerticalOverlapRatio(cluster, frontView);
                var rightOfFront = cluster.LeftPx >= frontView.LeftPx + (frontView.WidthPx * 0.35f);
                var aboveFront = cluster.BottomPx < frontView.TopPx + (frontView.HeightPx * 0.25f);
                var verticalStrip = cluster.WidthPx < cluster.HeightPx * 0.42f
                                    && cluster.WidthPx <= frontView.WidthPx * 0.28f;
                var horizontalStrip = cluster.HeightPx < cluster.WidthPx * 0.42f
                                      && cluster.HeightPx <= frontView.HeightPx * 0.28f;

                var alignedWithFront = verticalOverlap >= 0.18f || aboveFront;
                if (!alignedWithFront)
                    continue;

                if ((rightOfFront && verticalStrip) || (aboveFront && horizontalStrip) || cluster.MinDimPx <= frontView.MinDimPx * 0.22f)
                    result.Add(cluster);
            }

            if (result.Count == 0)
            {
                result = clusters
                    .Where(c => !ReferenceEquals(c, frontView))
                    .Where(c => c.MinDimPx < frontView.MinDimPx * 0.35f)
                    .OrderBy(c => c.MinDimPx)
                    .Take(2)
                    .ToList();
            }

            return result
                .OrderBy(c => c.MinDimPx)
                .ThenByDescending(c => VerticalOverlapRatio(c, frontView))
                .ToList();
        }

        private static float VerticalOverlapRatio(ViewCluster a, ViewCluster b)
        {
            var overlap = Math.Min(a.BottomPx, b.BottomPx) - Math.Max(a.TopPx, b.TopPx);
            if (overlap <= 0)
                return 0f;

            var minHeight = Math.Min(a.HeightPx, b.HeightPx);
            return overlap / Math.Max(1f, minHeight);
        }

        private static bool TryMeasureRightEdgeSideProfile(Bitmap bitmap, float pageWidthPt, int scaleDenominator, out int profilePx)
        {
            profilePx = 0;
            var regions = new[]
            {
                new ScanRegion(0.62f, 0.94f, 0.08f, 0.58f),
                new ScanRegion(0.68f, 0.92f, 0.12f, 0.52f),
                new ScanRegion(0.55f, 0.88f, 0.18f, 0.65f)
            };

            var minWidth = Math.Max(2, 4 / Math.Max(1, scaleDenominator / 2));
            var best = 0;
            var bestScore = float.MaxValue;

            foreach (var region in regions)
            {
                var x0 = (int)(bitmap.Width * region.X0Rel);
                var x1 = (int)(bitmap.Width * region.X1Rel);
                var y0 = (int)(bitmap.Height * region.Y0Rel);
                var y1 = (int)(bitmap.Height * region.Y1Rel);
                if (x1 <= x0 || y1 <= y0)
                    continue;

                var width = FindNarrowestRun(BuildColumnDark(bitmap, x0, x1, y0, y1), Math.Max(3, (y1 - y0) / 18), minWidth);
                if (width <= 0)
                    continue;

                var score = width * 12f;
                if (score < bestScore)
                {
                    bestScore = score;
                    best = width;
                }
            }

            profilePx = best;
            return profilePx > 0;
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

        private static int[] BuildColumnDark(Bitmap bitmap, int x0, int x1, int y0, int y1)
        {
            var columnCount = x1 - x0 + 1;
            var columnDark = new int[columnCount];
            for (var xi = 0; xi < columnCount; xi++)
            {
                var x = x0 + xi;
                var count = 0;
                for (var y = y0; y <= y1; y++)
                {
                    if (IsGeometryPixel(bitmap, x, y))
                        count++;
                }

                columnDark[xi] = count;
            }

            return columnDark;
        }

        private static int MeasureProfileThicknessPx(Bitmap bitmap, ViewCluster sideView, int scaleDenominator)
        {
            if (sideView.WidthPx <= sideView.HeightPx)
                return MeasureNarrowestVerticalStrip(bitmap, sideView, scaleDenominator);

            return MeasureNarrowestHorizontalStrip(bitmap, sideView, scaleDenominator);
        }

        private static int MeasureNarrowestVerticalStrip(Bitmap bitmap, ViewCluster region, int scaleDenominator)
        {
            var columnCount = region.WidthPx;
            var columnDark = new int[columnCount];
            var height = region.HeightPx;

            for (var xi = 0; xi < columnCount; xi++)
            {
                var x = region.LeftPx + xi;
                var count = 0;
                for (var y = region.TopPx; y <= region.BottomPx; y++)
                {
                    if (IsGeometryPixel(bitmap, x, y))
                        count++;
                }

                columnDark[xi] = count;
            }

            var minWidth = Math.Max(2, 3 / Math.Max(1, scaleDenominator / 2));
            return FindNarrowestRun(columnDark, Math.Max(3, height / 18), minWidth);
        }

        private static int MeasureNarrowestHorizontalStrip(Bitmap bitmap, ViewCluster region, int scaleDenominator)
        {
            var rowCount = region.HeightPx;
            var rowDark = new int[rowCount];
            var width = region.WidthPx;

            for (var yi = 0; yi < rowCount; yi++)
            {
                var y = region.TopPx + yi;
                var count = 0;
                for (var x = region.LeftPx; x <= region.RightPx; x++)
                {
                    if (IsGeometryPixel(bitmap, x, y))
                        count++;
                }

                rowDark[yi] = count;
            }

            var minWidth = Math.Max(2, 3 / Math.Max(1, scaleDenominator / 2));
            return FindNarrowestRun(rowDark, Math.Max(3, width / 18), minWidth);
        }

        private static int FindNarrowestRun(int[] counts, int threshold, int minWidth)
        {
            var bestWidth = 0;
            var bestScore = float.MaxValue;

            for (var start = 0; start < counts.Length; start++)
            {
                if (counts[start] < threshold)
                    continue;

                var end = start;
                while (end + 1 < counts.Length && counts[end + 1] >= threshold / 2)
                    end++;

                var width = end - start + 1;
                if (width < minWidth)
                {
                    start = end;
                    continue;
                }

                var avg = 0;
                for (var i = start; i <= end; i++)
                    avg += counts[i];
                avg /= Math.Max(1, width);

                var score = (width * 14f) - (avg * 1.6f);
                if (score < bestScore)
                {
                    bestScore = score;
                    bestWidth = width;
                }

                start = end;
            }

            return bestWidth;
        }

        private static float TryReadThicknessTextNearView(
            IList<PdfTextRect> textRects,
            float pageWidthPt,
            float pageHeightPt,
            Bitmap bitmap,
            ViewCluster sideView,
            string materialRaw = null)
        {
            if (textRects == null || textRects.Count == 0 || pageWidthPt <= 0f || pageHeightPt <= 0f)
                return 0f;

            var padX = Math.Max(8, sideView.WidthPx * 2);
            var padY = Math.Max(8, sideView.HeightPx * 0.15f);
            var zoneLeftPt = PixelXToPt(sideView.LeftPx - padX, bitmap.Width, pageWidthPt);
            var zoneRightPt = PixelXToPt(sideView.RightPx + padX, bitmap.Width, pageWidthPt);
            var zoneTopPt = PixelYToPt(sideView.TopPx - padY, bitmap.Height, pageHeightPt);
            var zoneBottomPt = PixelYToPt(sideView.BottomPx + padY, bitmap.Height, pageHeightPt);

            var scored = new List<(double Value, float Score)>();

            foreach (var rect in textRects)
            {
                if (!OverlapsPdfZone(rect, zoneLeftPt, zoneRightPt, zoneBottomPt, zoneTopPt))
                    continue;

                var text = Normalize(rect.Text);
                if (!IsolatedThicknessRegex.IsMatch(text))
                    continue;

                if (!TryParseValue(text, out var value) || value < 0.3d || value > ThicknessCandidateResolver.MaxSideViewThicknessMm)
                    continue;

                if (ThicknessCandidateResolver.IsLikelyMaterialNumberFragment(rect.Text, value)
                    || ThicknessCandidateResolver.IsMaterialNumberThicknessFalsePositive(value, materialRaw, rect.Text))
                    continue;

                if (ThicknessCandidateResolver.IsLikelyHoleOrFeatureDimension(rect.Text, value)
                    || ThicknessCandidateResolver.IsLikelyHoleDiameterMm(value))
                    continue;

                var centerX = (rect.Left + rect.Right) / 2f;
                var centerY = (rect.Bottom + rect.Top) / 2f;
                var sideCenterX = (zoneLeftPt + zoneRightPt) / 2f;
                var sideCenterY = (zoneBottomPt + zoneTopPt) / 2f;
                var relX = centerX / Math.Max(1f, pageWidthPt);
                var dx = Math.Abs(centerX - sideCenterX) / Math.Max(40f, pageWidthPt * 0.12f);
                var dy = Math.Abs(centerY - sideCenterY) / Math.Max(40f, pageHeightPt * 0.12f);
                var score = dx + dy + (rect.Width * 0.02f);
                if (ThicknessCandidateResolver.IsStandardSheetGauge(value, 0.5d)
                    && value <= ThicknessCandidateResolver.MaxAutoDetectedSheetMm)
                    score -= 10f;
                else if (ThicknessCandidateResolver.IsStandardSheetGauge(value, 0.5d)
                    && value >= 5d
                    && value <= 15d
                    && relX >= 0.35d)
                {
                    score -= 8f;
                }
                else if (ThicknessCandidateResolver.IsWholeNumber(value) && value >= 4d && relX < 0.45f)
                    score += 5f;
                if (ThicknessCandidateResolver.HasFractionalPart(value) && value >= 0.5d && value <= 6d && relX >= 0.40f)
                    score -= 3f;
                if (value >= 0.5d && value <= 3d)
                    score -= 4f;
                if (value > ThicknessCandidateResolver.MaxAutoDetectedSheetMm && relX >= 0.40f)
                    score -= 6f;

                scored.Add((value, score));
            }

            var resolved = ThicknessCandidateResolver.ResolveSideViewThicknessMm(scored);
            return resolved.HasValue ? (float)resolved.Value : 0f;
        }

        private static bool OverlapsPdfZone(PdfTextRect rect, float left, float right, float bottom, float top)
        {
            return rect.Right >= left && rect.Left <= right && rect.Top >= bottom && rect.Bottom <= top;
        }

        private static float PixelXToPt(float xPx, int bitmapWidth, float pageWidthPt)
        {
            return (xPx / Math.Max(1, bitmapWidth)) * pageWidthPt;
        }

        private static float PixelYToPt(float yPx, int bitmapHeight, float pageHeightPt)
        {
            return pageHeightPt - ((yPx / Math.Max(1, bitmapHeight)) * pageHeightPt);
        }

        private static List<Tuple<int, int>> ExtractSegments(int[] values, int threshold, int gapThreshold, int minGapWidth)
        {
            var segments = new List<Tuple<int, int>>();
            var start = -1;
            var gap = 0;

            for (var i = 0; i < values.Length; i++)
            {
                if (values[i] >= threshold)
                {
                    if (start < 0)
                        start = i;
                    gap = 0;
                    continue;
                }

                if (start < 0)
                    continue;

                gap++;
                if (gap < minGapWidth)
                    continue;

                var end = i - gap;
                if (end >= start)
                    segments.Add(Tuple.Create(start, end));

                start = -1;
                gap = 0;
            }

            if (start >= 0)
                segments.Add(Tuple.Create(start, values.Length - 1));

            return segments;
        }

        private static void Smooth(int[] values, int window)
        {
            if (values.Length == 0 || window <= 1)
                return;

            var copy = (int[])values.Clone();
            var radius = window / 2;
            for (var i = 0; i < values.Length; i++)
            {
                var sum = 0;
                var count = 0;
                for (var j = i - radius; j <= i + radius; j++)
                {
                    if (j < 0 || j >= copy.Length)
                        continue;
                    sum += copy[j];
                    count++;
                }

                values[i] = count == 0 ? 0 : sum / count;
            }
        }

        private static bool IsGeometryPixel(Bitmap bitmap, int x, int y)
        {
            if (x < 0 || y < 0 || x >= bitmap.Width || y >= bitmap.Height)
                return false;

            var pixel = bitmap.GetPixel(x, y);
            if (pixel.A < 16)
                return false;

            var luminance = (pixel.R + pixel.G + pixel.B) / 3;
            if (pixel.G > 170 && pixel.R < 120 && pixel.B < 120)
                return false;

            return luminance < 155;
        }

        private static float ConvertPxToThicknessMm(int profilePx, int bitmapWidthPx, float pageWidthPt, int scaleDenominator)
        {
            var widthPt = (profilePx / (float)Math.Max(1, bitmapWidthPx)) * pageWidthPt;
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

            var common = new[] { 1f, 1.5f, 2f, 2.5f, 3f, 4f, 5f, 6f, 8f, 10f };
            return common.Min(t => Math.Abs(candidate - t)) < common.Min(t => Math.Abs(current - t));
        }

        private static string Normalize(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;

            return Regex.Replace(value.Trim(), @"\s+", " ");
        }

        private static bool TryParseValue(string text, out double value)
        {
            value = 0d;
            if (string.IsNullOrWhiteSpace(text))
                return false;

            var cleaned = Regex.Replace(text, @"mm", string.Empty, RegexOptions.IgnoreCase).Trim();
            return ThicknessCandidateResolver.TryParseThicknessMm(cleaned, out value);
        }
    }
}
