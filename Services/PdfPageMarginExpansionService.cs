using System;
using System.IO;

namespace PDF_umbenenen.Services
{
    public enum PdfMarginStripKind
    {
        Top,
        TopLeft,
        Left
    }

    public sealed class PdfMarginExpansionPlan
    {
        public bool ShouldExpand { get; set; }

        public float MarginTop { get; set; }

        public float MarginLeft { get; set; }

        public float MarginRight { get; set; }

        public float MarginBottom { get; set; }

        public float Scale { get; set; } = 1f;
    }

    /// <summary>
    /// Erzwingt: freier Rand unten = freier Rand oben (Spiegel),
    /// und Zeichnungsrahmen horizontal zentriert (links = rechts).
    /// </summary>
    internal static class PdfPageMarginExpansionService
    {
        private const float MinScale = 0.85f;
        private const float MaxScale = 1.25f;
        private const float HorizontalCenterTolerancePt = 1.0f;

        public static bool TryBuildPlan(
            string pdfPath,
            int pageNumber,
            float pageWidth,
            float pageHeight,
            out PdfMarginExpansionPlan plan)
        {
            plan = null;
            if (string.IsNullOrWhiteSpace(pdfPath)
                || !File.Exists(pdfPath)
                || pageWidth <= 0f
                || pageHeight <= 0f
                || !PdfRasterLayoutService.IsAvailable)
            {
                return false;
            }

            var minBand = WatermarkPageScale.GetRequiredTopClearancePt(pageWidth, pageHeight);
            var freeAbove = PdfRasterLayoutService.MeasureFreeTopMarginPt(
                pdfPath, pageNumber, pageWidth, pageHeight);
            var freeBelow = PdfRasterLayoutService.MeasureFreeBottomMarginPt(
                pdfPath, pageNumber, pageWidth, pageHeight);

            if (freeAbove < 0f)
                freeAbove = 0f;
            if (freeBelow < 0f)
                freeBelow = 0f;

            // Pro Seite: unterer Rand = oberer Rand (Maßstab der Zeichnung).
            var mirror = true;
            try
            {
                var settings = WatermarkSettingsStore.Load();
                if (settings != null)
                    mirror = settings.MirrorBottomToTopBand;
            }
            catch
            {
                /* ignore */
            }

            var textNeed = minBand;
            float targetBand = freeAbove;
            float marginBottom = 0f;
            float scale = 1f;
            var needsVertical = false;

            if (!mirror)
            {
                if (freeAbove < textNeed - 0.75f)
                {
                    targetBand = textNeed;
                    var denom = pageHeight - freeAbove;
                    if (denom < 40f)
                        denom = pageHeight;
                    scale = (pageHeight - targetBand) / denom;
                    if (scale < 0.999f)
                    {
                        scale = Math.Max(MinScale, Math.Min(0.998f, scale));
                        marginBottom = 0f;
                        needsVertical = true;
                    }
                    else
                    {
                        scale = 1f;
                    }
                }
            }
            else
            {
                // Seite-2 war OK mit ~22 pt Rand; Seiten mit nur ~16 pt wirken unten gequetscht.
                // Ziel: oben und unten gleicher Rand, mindestens Text + Luft (wie die gute Seite).
                var pageScale = WatermarkPageScale.Get(pageWidth, pageHeight);
                var comfortExtra = Math.Max(5f, 6f * pageScale);
                var comfortBand = textNeed + comfortExtra;

                targetBand = Math.Max(freeAbove, freeBelow);
                if (targetBand < comfortBand)
                    targetBand = comfortBand;
                if (targetBand < 8f)
                    targetBand = comfortBand;

                var frameHeight = pageHeight - freeAbove - freeBelow;
                if (frameHeight >= 60f)
                {
                    var verticalAlreadyOk = Math.Abs(freeBelow - targetBand) <= 0.5f
                                           && Math.Abs(freeAbove - targetBand) <= 0.5f;
                    if (!verticalAlreadyOk)
                    {
                        var targetHeight = pageHeight - (2f * targetBand);
                        if (targetHeight >= 40f)
                        {
                            scale = targetHeight / frameHeight;
                            if (!float.IsNaN(scale) && !float.IsInfinity(scale) && scale > 0f)
                            {
                                scale = Math.Max(MinScale, Math.Min(MaxScale, scale));
                                marginBottom = targetBand - (freeBelow * scale);
                                needsVertical = true;
                            }
                            else
                            {
                                scale = 1f;
                            }
                        }
                    }
                }
            }

            if (float.IsNaN(scale) || float.IsInfinity(scale) || scale <= 0f)
                return false;

            // Horizontal: Zeichnungsrahmen zentrieren (links = rechts), Hoch- und Querformat.
            float frameLeft = 0f;
            float frameRight = pageWidth;
            var hasFrame = PdfRasterLayoutService.TryGetDrawingFrameBounds(
                pdfPath,
                pageNumber,
                pageWidth,
                pageHeight,
                out frameLeft,
                out frameRight,
                out _);

            float marginLeft;
            float freeLeft;
            float freeRight;
            if (hasFrame && frameRight > frameLeft + 20f)
            {
                freeLeft = Math.Max(0f, frameLeft);
                freeRight = Math.Max(0f, pageWidth - frameRight);
                // Nach Skalierung: tx so wählen, dass Rahmenmitte = Seitenmitte.
                marginLeft = (pageWidth - (scale * (frameLeft + frameRight))) / 2f;
            }
            else
            {
                freeLeft = 0f;
                freeRight = 0f;
                var unusedWidth = pageWidth * (1f - scale);
                marginLeft = unusedWidth * 0.5f;
            }

            var needsHorizontal = hasFrame
                                  && Math.Abs(freeLeft - freeRight) > HorizontalCenterTolerancePt;

            if (!needsVertical && !needsHorizontal)
                return false;

            if (!needsVertical)
            {
                scale = 1f;
                marginBottom = 0f;
                targetBand = freeAbove;
                if (hasFrame && frameRight > frameLeft + 20f)
                    marginLeft = (pageWidth - (frameLeft + frameRight)) / 2f;
                else
                    marginLeft = 0f;
            }

            try
            {
                var logDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "DOK-V01", "Logs");
                Directory.CreateDirectory(logDir);
                File.AppendAllText(
                    Path.Combine(logDir, "margin-mirror.log"),
                    string.Format(
                        System.Globalization.CultureInfo.InvariantCulture,
                        "{0:yyyy-MM-dd HH:mm:ss} EXPAND above={1:0.0} below={2:0.0} target={3:0.0} scale={4:0.000} marginL={5:0.0} freeL={6:0.0} freeR={7:0.0} v={8} h={9} {10}\n",
                        DateTime.Now,
                        freeAbove,
                        freeBelow,
                        targetBand,
                        scale,
                        marginLeft,
                        freeLeft,
                        freeRight,
                        needsVertical ? 1 : 0,
                        needsHorizontal ? 1 : 0,
                        pdfPath));
            }
            catch
            {
                /* ignore */
            }

            var unusedAfter = pageWidth - (scale * pageWidth);
            plan = new PdfMarginExpansionPlan
            {
                ShouldExpand = true,
                MarginTop = targetBand,
                MarginLeft = marginLeft,
                MarginRight = unusedAfter - marginLeft,
                MarginBottom = marginBottom,
                Scale = scale
            };

            return true;
        }

        public static bool TryExpandSourceInPlace(
            string sourcePdfPath,
            int pageNumber,
            float pageWidth,
            float pageHeight)
        {
            if (!TryBuildPlan(sourcePdfPath, pageNumber, pageWidth, pageHeight, out var plan))
                return false;

            var tempPath = sourcePdfPath + ".margin.tmp";
            try
            {
                PdfWatermarkService.RewritePdfWithExpandedMargins(sourcePdfPath, tempPath, plan, pageNumber);
                PdfRasterLayoutService.ClearCache();
                PdfLayoutAnalysisService.ClearCache();
                File.Copy(tempPath, sourcePdfPath, true);
                return true;
            }
            finally
            {
                if (File.Exists(tempPath))
                {
                    try { File.Delete(tempPath); }
                    catch { /* ignore */ }
                }
            }
        }
    }
}
