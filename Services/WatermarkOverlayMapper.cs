using System;
using System.Windows;

namespace PDF_umbenenen.Services
{
    internal static class WatermarkOverlayMapper
    {
        public static Point PdfToCanvasTopLeft(WatermarkTextPlacement placement, float pageHeightPt, double scale)
        {
            if (placement == null)
                return new Point(0, 0);

            var fontSize = placement.FontSize > 0 ? placement.FontSize : 22f;
            var textWidth = EstimateTextWidth(placement.Text, fontSize);
            var anchorX = placement.X;

            if (placement.Alignment == 1)
                anchorX -= textWidth / 2f;
            else if (placement.Alignment == 2)
                anchorX -= textWidth;

            var ascent = fontSize * 0.85f;
            var x = anchorX * scale;
            var yTop = (pageHeightPt - placement.Y - ascent) * scale;
            return new Point(x, yTop);
        }

        public static void UpdatePlacementFromCanvasTopLeft(
            WatermarkTextPlacement placement,
            double canvasLeft,
            double canvasTop,
            float pageHeightPt,
            double scale)
        {
            if (placement == null || scale <= 0)
                return;

            var fontSize = placement.FontSize > 0 ? placement.FontSize : 22f;
            var ascent = fontSize * 0.85f;

            placement.Alignment = 0;
            placement.X = (float)(canvasLeft / scale);
            placement.Y = (float)(pageHeightPt - (canvasTop / scale) - ascent);
        }

        public static double GetScreenFontSize(WatermarkTextPlacement placement, double scale)
        {
            var fontSize = placement?.FontSize ?? 22f;
            return Math.Max(10, fontSize * scale * 0.92);
        }

        private static float EstimateTextWidth(string text, float fontSize)
        {
            if (string.IsNullOrEmpty(text))
                return 10f;

            return Math.Max(20f, text.Length * fontSize * 0.55f);
        }
    }
}
