using System;

namespace PDF_umbenenen.Services
{
    /// <summary>
    /// Skaliert Wasserzeichen-Schriftgrößen relativ zur Blattgröße (Referenz: A4 = 595×842 pt).
    /// </summary>
    internal static class WatermarkPageScale
    {
        private const float RefWidth = 595f;
        private const float RefHeight = 842f;

        public static float Get(float pageWidth, float pageHeight)
        {
            if (pageWidth <= 0f || pageHeight <= 0f)
                return 1f;

            var scale = (float)Math.Sqrt((pageWidth / RefWidth) * (pageHeight / RefHeight));
            return Math.Max(0.85f, Math.Min(scale, 3.5f));
        }

        public static float Scale(float basePointSize, float pageWidth, float pageHeight)
        {
            return basePointSize * Get(pageWidth, pageHeight);
        }

        public static float GetPageNumberFontSize(float pageWidth, float pageHeight)
        {
            var scale = Get(pageWidth, pageHeight);
            return Math.Max(14f, 16f * scale);
        }

        /// <summary>
        /// Unten: exakt dieselbe Kantenformel wie GetMaxTopBaselineY (nur gespiegelt).
        /// Keine Rahmenmessung – die hat Seiten unterschiedlich verfälscht.
        /// </summary>
        public static float GetSharedBottomBandBaselineY(
            float pageWidth,
            float pageHeight,
            float referenceFontSize,
            string pdfPath = null,
            int pageNumber = 1)
        {
            var font = referenceFontSize > 0f
                ? referenceFontSize
                : GetPageNumberFontSize(pageWidth, pageHeight);
            var scale = Get(pageWidth, pageHeight);
            var edgeMargin = Math.Max(1.5f, 2f * scale);
            var descent = Math.Max(font * 0.18f, 2.5f * scale);
            // Oben: Baseline = H - edge - ascent  → Glyph-Oberkante an der Kante.
            // Unten: Baseline = edge + descent     → Glyph-Unterkante an der Kante.
            return edgeMargin + descent;
        }

        public static float GetBottomBandBaselineY(float pageWidth, float pageHeight, float fontSize)
        {
            return GetSharedBottomBandBaselineY(pageWidth, pageHeight, fontSize);
        }

        /// <summary>
        /// Unterer Freiraum = oberer Freiraum dieser Seite (für Expansion).
        /// </summary>
        public static float GetRequiredBottomClearancePt(float pageWidth, float pageHeight)
        {
            return GetRequiredTopClearancePt(pageWidth, pageHeight);
        }

        public static float GetTopBaselineY(float pageWidth, float pageHeight, float fontSize)
        {
            return GetMaxTopBaselineY(pageWidth, pageHeight, fontSize);
        }

        public static float GetMaxTopBaselineY(float pageWidth, float pageHeight, float fontSize)
        {
            var scale = Get(pageWidth, pageHeight);
            var edgeMargin = Math.Max(1.5f, 2f * scale);
            var ascent = Math.Max(fontSize * 0.70f, 9f * scale);
            return pageHeight - edgeMargin - ascent;
        }

        public static float GetSharedTopBandBaselineY(
            float pageWidth,
            float pageHeight,
            float referenceFontSize,
            string pdfPath = null,
            int pageNumber = 1,
            int stackLines = 1)
        {
            var font = referenceFontSize > 0f ? referenceFontSize : 22f;
            var scale = Get(pageWidth, pageHeight);
            var gapAboveFrame = Math.Max(2f, 2.5f * scale);
            var lineHeight = font + 8f;
            var stackExtra = Math.Max(0, stackLines - 1) * lineHeight;
            var nearTop = GetMaxTopBaselineY(pageWidth, pageHeight, font) + stackExtra;

            if (!string.IsNullOrWhiteSpace(pdfPath)
                && PdfRasterLayoutService.TryGetDrawingFrameTopY(
                    pdfPath,
                    pageNumber,
                    pageWidth,
                    pageHeight,
                    out var frameTopY))
            {
                var minAboveFrame = frameTopY + gapAboveFrame + stackExtra;
                return Math.Max(nearTop, minAboveFrame);
            }

            return nearTop;
        }

        public static float GetRequiredTopClearancePt(
            float pageWidth,
            float pageHeight,
            int stackLines = 1)
        {
            var scale = WatermarkPageScale.Get(pageWidth, pageHeight);
            var topBandFont = GetPageNumberFontSize(pageWidth, pageHeight);
            var ascent = Math.Max(topBandFont * 0.72f, 9f * scale);
            var gapAboveFrame = Math.Max(2f, 2.5f * scale);
            var edgeMargin = Math.Max(1.5f, 2f * scale);
            var lineHeight = topBandFont + 8f;
            var stackExtra = Math.Max(0, stackLines - 1) * lineHeight;
            return ascent + gapAboveFrame + edgeMargin + stackExtra;
        }

        public static float GetTopLeftMargin(float pageWidth, float pageHeight)
        {
            var scale = Get(pageWidth, pageHeight);
            return Math.Max(6f, 8f * scale);
        }

        public static float GetTopBandFloorY(float pageWidth, float pageHeight, float fontSize)
        {
            var baseline = GetMaxTopBaselineY(pageWidth, pageHeight, fontSize);
            return baseline - Math.Max(6f, fontSize * 0.35f);
        }

        public static float GetSideMargin(float pageWidth, float pageHeight)
        {
            var scale = Get(pageWidth, pageHeight);
            return Math.Max(42f, 50f * scale);
        }
    }
}
