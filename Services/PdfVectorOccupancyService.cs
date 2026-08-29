using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace PDF_umbenenen.Services
{
    internal sealed class MaterialPageOccupancy
    {
        public float PageWidth { get; set; }
        public float PageHeight { get; set; }
        public List<PdfTextRect> Obstacles { get; set; } = new List<PdfTextRect>();
        public int TextObjectCount { get; set; }
        public int StrokeObjectCount { get; set; }
        public bool HasRichVectorLayer { get; set; }
    }

    /// <summary>
    /// Belegte Flächen aus PDF-Textobjekten und Vektorgraphik — einmal pro Seite gecacht.
    /// </summary>
    internal static class PdfVectorOccupancyService
    {
        private const float TextPaddingPt = 3f;
        private const float StrokePaddingPt = 2.5f;
        private const int RichTextObjectThreshold = 6;

        private static readonly Dictionary<string, MaterialPageOccupancy> Cache =
            new Dictionary<string, MaterialPageOccupancy>(StringComparer.OrdinalIgnoreCase);
        private static readonly object CacheLock = new object();

        public static void ClearCache()
        {
            lock (CacheLock)
                Cache.Clear();
        }

        public static MaterialPageOccupancy GetPageOccupancy(string pdfPath, int pageNumber)
        {
            if (string.IsNullOrWhiteSpace(pdfPath) || !File.Exists(pdfPath))
                return new MaterialPageOccupancy { PageWidth = 595f, PageHeight = 842f };

            var cacheKey = pdfPath + "|" + pageNumber + "|" + File.GetLastWriteTimeUtc(pdfPath).Ticks;
            lock (CacheLock)
            {
                if (Cache.TryGetValue(cacheKey, out var cached))
                    return cached;
            }

            var layout = PdfLayoutAnalysisService.AnalyzePage(pdfPath, pageNumber);
            var pageWidth = layout.PageWidth > 0f ? layout.PageWidth : 595f;
            var pageHeight = layout.PageHeight > 0f ? layout.PageHeight : 842f;

            var occupancy = new MaterialPageOccupancy
            {
                PageWidth = pageWidth,
                PageHeight = pageHeight,
                TextObjectCount = layout.TextRects?.Count ?? 0
            };

            if (layout.TextRects != null)
            {
                foreach (var rect in layout.TextRects)
                {
                    if (rect == null || string.IsNullOrWhiteSpace(rect.Text))
                        continue;

                    occupancy.Obstacles.Add(Inflate(rect, TextPaddingPt, "__pdf_text__"));
                }
            }

            var strokes = WatermarkProcessingContext.BatchMode
                ? new List<PdfTextRect>()
                : PdfContentStrokeExtractor.ExtractStrokeRects(pdfPath, pageNumber, pageWidth, pageHeight);
            occupancy.StrokeObjectCount = strokes.Count;
            foreach (var stroke in strokes)
                occupancy.Obstacles.Add(Inflate(stroke, StrokePaddingPt, stroke.Text));

            AddZoneObstacle(
                occupancy.Obstacles,
                pageWidth,
                pageHeight,
                0f,
                pageWidth,
                0f,
                pageHeight * MaterialPlacementGeometry.TitleBlockTopRatio,
                "__title_block__");

            MaterialPlacementGeometry.GetDrawingCoreRect(
                pageWidth,
                pageHeight,
                out var coreLeft,
                out var coreRight,
                out var coreBottom,
                out var coreTop);
            AddZoneObstacle(
                occupancy.Obstacles,
                pageWidth,
                pageHeight,
                coreLeft,
                coreRight,
                coreBottom,
                coreTop,
                "__drawing_core__");

            AddZoneObstacle(
                occupancy.Obstacles,
                pageWidth,
                pageHeight,
                0f,
                pageWidth,
                pageHeight * MaterialPlacementGeometry.MaterialForbiddenBottomRatio,
                pageHeight,
                "__bottom_form__");

            occupancy.HasRichVectorLayer =
                occupancy.TextObjectCount >= RichTextObjectThreshold
                || occupancy.StrokeObjectCount >= 40;

            lock (CacheLock)
                Cache[cacheKey] = occupancy;

            return occupancy;
        }

        private static void AddZoneObstacle(
            List<PdfTextRect> obstacles,
            float pageWidth,
            float pageHeight,
            float left,
            float right,
            float bottom,
            float top,
            string label)
        {
            if (right <= left || top <= bottom)
                return;

            obstacles.Add(new PdfTextRect
            {
                Text = label,
                Left = left,
                Right = right,
                Bottom = bottom,
                Top = top
            });
        }

        private static PdfTextRect Inflate(PdfTextRect rect, float padding, string label)
        {
            return new PdfTextRect
            {
                Text = label ?? rect.Text,
                Left = rect.Left - padding,
                Right = rect.Right + padding,
                Bottom = rect.Bottom - padding,
                Top = rect.Top + padding
            };
        }
    }
}
