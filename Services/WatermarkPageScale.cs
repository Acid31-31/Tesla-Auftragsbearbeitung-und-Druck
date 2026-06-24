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

        /// <summary>
        /// Baseline-Y im oberen Randstreifen: knapp unter der Blattkante, lesbar ohne Abschnitt.
        /// </summary>
        public static float GetTopBaselineY(float pageWidth, float pageHeight, float fontSize)
        {
            var scale = Get(pageWidth, pageHeight);
            var edgeMargin = Math.Max(8f, 10f * scale);
            var baselineFromTop = edgeMargin + (fontSize * 0.82f);
            return pageHeight - baselineFromTop;
        }

        /// <summary>
        /// Untere Grenze des oberen Randstreifens – Wasserzeichen nicht tiefer platzieren.
        /// </summary>
        public static float GetTopBandFloorY(float pageWidth, float pageHeight, float fontSize)
        {
            var scale = Get(pageWidth, pageHeight);
            var bandDepth = Math.Max(fontSize * 1.5f, pageHeight * 0.05f);
            bandDepth = Math.Min(bandDepth, pageHeight * 0.07f);
            return pageHeight - bandDepth;
        }

        public static float GetSideMargin(float pageWidth, float pageHeight)
        {
            var scale = Get(pageWidth, pageHeight);
            return Math.Max(42f, 50f * scale);
        }
    }
}
