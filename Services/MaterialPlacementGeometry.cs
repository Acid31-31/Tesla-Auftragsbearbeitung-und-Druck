using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace PDF_umbenenen.Services
{
    public enum MaterialHardBlockReason
    {
        None = 0,
        DrawingCore,
        TitleBlock,
        LeftDimensionStrip,
        OutOfMargin,
        TextOverlap,
        DrawingInk,
        ForbiddenZone
    }

    /// <summary>
    /// Einheitliche Material-Geometrie für Render, Guard, Diagnose und Hard-Block.
    /// Y = Baseline oberste Zeile (zentriert, Alignment=1).
    /// </summary>
    public sealed class MaterialBlockBounds
    {
        public float CenterX { get; set; }
        public float TopBaselineY { get; set; }
        public float BlockWidth { get; set; }
        public float BlockHeight { get; set; }
        public float Left { get; set; }
        public float Right { get; set; }
        public float Bottom { get; set; }
        public float Top { get; set; }

        public static MaterialBlockBounds FromAnchor(
            float centerX,
            float topBaselineY,
            float blockWidth,
            float blockHeight)
        {
            PdfRasterLayoutService.ResolveBlockGeometry(
                centerX,
                topBaselineY,
                blockWidth,
                blockHeight,
                MaterialWatermarkPlacementService.MaterialAlignment,
                materialYIsBottomAnchor: false,
                out var resolvedCenterX,
                out var anchorBottomY);

            return new MaterialBlockBounds
            {
                CenterX = resolvedCenterX,
                TopBaselineY = topBaselineY,
                BlockWidth = blockWidth,
                BlockHeight = blockHeight,
                Left = resolvedCenterX - (blockWidth / 2f),
                Right = resolvedCenterX + (blockWidth / 2f),
                Bottom = anchorBottomY,
                Top = anchorBottomY + blockHeight
            };
        }

        internal static MaterialBlockBounds FromMaterialStack(IList<WatermarkTextPlacement> materialPlacements)
        {
            MaterialWatermarkPlacementService.GetMaterialBlockMetrics(
                materialPlacements, out var blockWidth, out var blockHeight, out _);

            var topLine = materialPlacements?
                .OrderByDescending(p => p.Y)
                .FirstOrDefault();

            if (topLine == null)
                return new MaterialBlockBounds();

            return FromAnchor(topLine.X, topLine.Y, blockWidth, blockHeight);
        }

        public string ToRectString()
        {
            return string.Format(
                CultureInfo.InvariantCulture,
                "L:{0:0} R:{1:0} B:{2:0} T:{3:0}",
                Left,
                Right,
                Bottom,
                Top);
        }

        public string ToRelString(float pageWidth, float pageHeight)
        {
            if (pageWidth <= 0f || pageHeight <= 0f)
                return "-";

            return string.Format(
                CultureInfo.InvariantCulture,
                "({0:0.00},{1:0.00})",
                CenterX / pageWidth,
                TopBaselineY / pageHeight);
        }
    }

    internal static class MaterialPlacementGeometry
    {
        public const float MinMarginPt = 28f;
        public const float TitleBlockLeftRatio = 0.58f;
        public const float TitleBlockTopRatio = 0.22f;
        /// <summary>Material nie unterhalb dieser Höhe (volle Breite: Schriftfeld, Stückliste, Revisionsblock).</summary>
        public const float MaterialForbiddenBottomRatio = 0.40f;
        public const float DrawingCoreLeftRatio = 0.06f;
        public const float DrawingCoreRightMarginPt = 24f;
        public const float DrawingCoreTopMarginPt = 24f;
        public const float DrawingCoreBottomAboveTitlePt = 110f;
        /// <summary>Final-Check: Mittelwert zu dunkel.</summary>
        public const float MaterialInkHardBlockDarkness = 0.28f;
        /// <summary>Final-Check: Linie im Block (Isometrie-Schutz).</summary>
        public const float MaterialInkPeakHardBlockDarkness = 0.22f;
        /// <summary>Suche: Spitzen-Tinte (Linie im Block) — blockiert Isometrie-Weißraum-Falle.</summary>
        public const float MaterialInkPeakSearchBlockDarkness = 0.16f;

        /// <summary>Suche: Pixel-Tinte über diesem Anteil = Zeichnung.</summary>
        public const float MaterialPreciseInkRejectDarkness = 0.018f;

        /// <summary>Suche: bevorzugt unter diesem Mittelwert.</summary>
        public const float MaterialInkPreferredDarkness = 0.18f;

        public static void AppendReservedWatermarkRects(List<PdfTextRect> occupied, float pageWidth, float pageHeight)
        {
            if (occupied == null || pageWidth <= 0f || pageHeight <= 0f)
                return;

            var titleTop = pageHeight * TitleBlockTopRatio;
            occupied.Add(new PdfTextRect
            {
                Text = "__reserved_page_number__",
                Left = pageWidth * 0.30f,
                Right = pageWidth * 0.70f,
                Bottom = 0f,
                Top = Math.Max(52f, pageHeight * 0.06f)
            });

            occupied.Add(new PdfTextRect
            {
                Text = "__reserved_bg_strip__",
                Left = MinMarginPt,
                Right = pageWidth * TitleBlockLeftRatio - 20f,
                Bottom = titleTop,
                Top = titleTop + 160f
            });

            occupied.Add(new PdfTextRect
            {
                Text = "__reserved_full_bottom_form__",
                Left = 0f,
                Right = pageWidth,
                Bottom = 0f,
                Top = pageHeight * MaterialForbiddenBottomRatio
            });
        }

        public static bool IntersectsMaterialForbiddenBottomStrip(MaterialBlockBounds bounds, float pageWidth, float pageHeight)
        {
            if (bounds == null || pageWidth <= 0f || pageHeight <= 0f)
                return false;

            var stripTop = pageHeight * MaterialForbiddenBottomRatio;
            return bounds.Left < pageWidth
                && bounds.Right > 0f
                && bounds.Top > 0f
                && bounds.Bottom < stripTop;
        }

        public static bool IntersectsPageNumberStrip(MaterialBlockBounds bounds, float pageWidth, float pageHeight)
        {
            if (bounds == null || pageWidth <= 0f || pageHeight <= 0f)
                return false;

            return bounds.Left < pageWidth * 0.70f
                && bounds.Right > pageWidth * 0.30f
                && bounds.Top > 0f
                && bounds.Bottom < Math.Max(52f, pageHeight * 0.06f);
        }

        public static bool IntersectsBgReservedStrip(MaterialBlockBounds bounds, float pageWidth, float pageHeight)
        {
            if (bounds == null || pageWidth <= 0f || pageHeight <= 0f)
                return false;

            var titleTop = pageHeight * TitleBlockTopRatio;
            return bounds.Left < pageWidth * TitleBlockLeftRatio - 20f
                && bounds.Right > MinMarginPt
                && bounds.Top > titleTop
                && bounds.Bottom < titleTop + 160f;
        }

        public static bool HasBlockingInk(
            MaterialBlockBounds bounds,
            string pdfPath,
            int pageNumber,
            float pageWidth,
            float pageHeight)
        {
            if (!PdfRasterLayoutService.IsAvailable || string.IsNullOrWhiteSpace(pdfPath))
                return false;

            var peak = PdfRasterLayoutService.MeasureMaterialPlacementPeakDarkness(
                pdfPath,
                pageNumber,
                bounds.CenterX,
                bounds.TopBaselineY,
                bounds.BlockWidth,
                bounds.BlockHeight,
                pageWidth,
                pageHeight);

            return peak >= MaterialInkPeakSearchBlockDarkness;
        }

        public static void GetDrawingCoreRect(
            float pageWidth,
            float pageHeight,
            out float left,
            out float right,
            out float bottom,
            out float top)
        {
            var titleTop = pageHeight * TitleBlockTopRatio;
            left = pageWidth * DrawingCoreLeftRatio;
            right = pageWidth - DrawingCoreRightMarginPt;
            bottom = titleTop + DrawingCoreBottomAboveTitlePt;
            top = pageHeight - DrawingCoreTopMarginPt;
        }

        public static void GetTitleBlockRect(
            float pageWidth,
            float pageHeight,
            out float left,
            out float right,
            out float bottom,
            out float top)
        {
            left = 0f;
            right = pageWidth;
            bottom = 0f;
            top = pageHeight * TitleBlockTopRatio;
        }

        public static MaterialHardBlockReason EvaluateHardBlock(
            MaterialBlockBounds bounds,
            float pageWidth,
            float pageHeight,
            IEnumerable<PdfTextRect> occupiedRects = null,
            string pdfPath = null,
            int pageNumber = 1)
        {
            if (WatermarkProcessingContext.BatchMode)
                return EvaluateGeometryBlock(bounds, pageWidth, pageHeight, occupiedRects, pdfPath, pageNumber);

            var geometryReason = EvaluateGeometryBlock(bounds, pageWidth, pageHeight, occupiedRects, pdfPath, pageNumber);
            if (geometryReason != MaterialHardBlockReason.None)
                return geometryReason;

            var inkReason = EvaluateDrawingInk(bounds, pdfPath, pageNumber, pageWidth, pageHeight);
            return inkReason != MaterialHardBlockReason.None ? inkReason : MaterialHardBlockReason.None;
        }

        /// <summary>Geometrie, Schriftfeld, Overlap — ohne Tinten-Score (schneller Kandidaten-Vorfilter).</summary>
        public static MaterialHardBlockReason EvaluateGeometryBlock(
            MaterialBlockBounds bounds,
            float pageWidth,
            float pageHeight,
            IEnumerable<PdfTextRect> occupiedRects = null,
            string pdfPath = null,
            int pageNumber = 1)
        {
            if (pageWidth <= 0f || pageHeight <= 0f || bounds.BlockWidth <= 0f || bounds.BlockHeight <= 0f)
                return MaterialHardBlockReason.OutOfMargin;

            if (bounds.Left < MinMarginPt
                || bounds.Right > pageWidth - MinMarginPt
                || bounds.Bottom < MinMarginPt
                || bounds.Top > pageHeight - MinMarginPt)
            {
                return MaterialHardBlockReason.OutOfMargin;
            }

            if (IntersectsDrawingCore(bounds, pageWidth, pageHeight))
                return MaterialHardBlockReason.DrawingCore;

            if (IntersectsTitleBlock(bounds, pageWidth, pageHeight))
                return MaterialHardBlockReason.TitleBlock;

            if (IntersectsMaterialForbiddenBottomStrip(bounds, pageWidth, pageHeight))
                return MaterialHardBlockReason.ForbiddenZone;

            if (PdfRasterLayoutService.IsInMaterialLeftDimensionStrip(
                    bounds.CenterX,
                    bounds.TopBaselineY,
                    bounds.BlockWidth,
                    bounds.BlockHeight,
                    pageWidth,
                    pageHeight,
                    MaterialWatermarkPlacementService.MaterialAlignment,
                    materialYIsBottomAnchor: false))
            {
                return MaterialHardBlockReason.LeftDimensionStrip;
            }

            if (occupiedRects != null)
            {
                foreach (var occupied in occupiedRects)
                {
                    if (occupied == null)
                        continue;

                    if (Intersects(bounds, occupied))
                        return MaterialHardBlockReason.TextOverlap;
                }
            }

            return MaterialHardBlockReason.None;
        }

        /// <summary>Platzierungssuche: Geometrie + Overlap — Ink nur als Score (Freifläche), nicht Hard-Reject.</summary>
        public static MaterialHardBlockReason EvaluatePlacementHardBlock(
            MaterialBlockBounds bounds,
            float pageWidth,
            float pageHeight,
            string pdfPath,
            int pageNumber,
            IEnumerable<PdfTextRect> occupiedRects = null,
            IEnumerable<MaterialBlockBounds> forbiddenZones = null)
        {
            if (pageWidth <= 0f || pageHeight <= 0f || bounds.BlockWidth <= 0f || bounds.BlockHeight <= 0f)
                return MaterialHardBlockReason.OutOfMargin;

            if (bounds.Left < MinMarginPt
                || bounds.Right > pageWidth - MinMarginPt
                || bounds.Bottom < MinMarginPt
                || bounds.Top > pageHeight - MinMarginPt)
            {
                return MaterialHardBlockReason.OutOfMargin;
            }

            if (IntersectsForbiddenZone(bounds, forbiddenZones))
                return MaterialHardBlockReason.ForbiddenZone;

            if (IntersectsDrawingCore(bounds, pageWidth, pageHeight))
                return MaterialHardBlockReason.DrawingCore;

            if (IntersectsTitleBlock(bounds, pageWidth, pageHeight))
                return MaterialHardBlockReason.TitleBlock;

            if (IntersectsMaterialForbiddenBottomStrip(bounds, pageWidth, pageHeight))
                return MaterialHardBlockReason.ForbiddenZone;

            if (PdfRasterLayoutService.IsInMaterialLeftDimensionStrip(
                    bounds.CenterX,
                    bounds.TopBaselineY,
                    bounds.BlockWidth,
                    bounds.BlockHeight,
                    pageWidth,
                    pageHeight,
                    MaterialWatermarkPlacementService.MaterialAlignment,
                    materialYIsBottomAnchor: false))
            {
                return MaterialHardBlockReason.LeftDimensionStrip;
            }

            if (occupiedRects != null)
            {
                foreach (var occupied in occupiedRects)
                {
                    if (occupied == null)
                        continue;

                    if (Intersects(bounds, occupied))
                        return MaterialHardBlockReason.TextOverlap;
                }
            }

            if (IntersectsPageNumberStrip(bounds, pageWidth, pageHeight))
                return MaterialHardBlockReason.ForbiddenZone;

            if (IntersectsBgReservedStrip(bounds, pageWidth, pageHeight))
                return MaterialHardBlockReason.ForbiddenZone;

            if (IntersectsMaterialForbiddenBottomStrip(bounds, pageWidth, pageHeight))
                return MaterialHardBlockReason.ForbiddenZone;

            return MaterialHardBlockReason.None;
        }

        public static bool IntersectsBottomRightIsometricZone(
            MaterialBlockBounds bounds,
            float pageWidth,
            float pageHeight)
        {
            if (bounds == null || pageWidth <= 0f || pageHeight <= 0f)
                return false;

            var titleTop = pageHeight * TitleBlockTopRatio;
            var isoLeft = pageWidth * 0.50f;
            var isoRight = pageWidth - MinMarginPt;
            var isoBottom = titleTop + 8f;
            var isoTop = pageHeight * 0.60f;

            return bounds.Left < isoRight
                && bounds.Right > isoLeft
                && bounds.Top > isoBottom
                && bounds.Bottom < isoTop;
        }

        public static MaterialHardBlockReason EvaluateDrawingInk(
            MaterialBlockBounds bounds,
            string pdfPath,
            int pageNumber,
            float pageWidth,
            float pageHeight)
        {
            if (!PdfRasterLayoutService.IsAvailable || string.IsNullOrWhiteSpace(pdfPath))
                return MaterialHardBlockReason.None;

            var meanDark = MaterialWatermarkPlacementService.MeasureInkDarknessRobust(
                pdfPath,
                pageNumber,
                bounds.CenterX,
                bounds.TopBaselineY,
                bounds.BlockWidth,
                bounds.BlockHeight,
                pageWidth,
                pageHeight);

            if (meanDark >= MaterialInkHardBlockDarkness)
                return MaterialHardBlockReason.DrawingInk;

            var peakDark = PdfRasterLayoutService.MeasureMaterialPlacementPeakDarkness(
                pdfPath,
                pageNumber,
                bounds.CenterX,
                bounds.TopBaselineY,
                bounds.BlockWidth,
                bounds.BlockHeight,
                pageWidth,
                pageHeight);

            return peakDark >= MaterialInkPeakHardBlockDarkness
                ? MaterialHardBlockReason.DrawingInk
                : MaterialHardBlockReason.None;
        }

        public static MaterialBlockBounds InflateBounds(MaterialBlockBounds bounds, float paddingPt)
        {
            if (bounds == null || bounds.BlockWidth <= 0f || bounds.BlockHeight <= 0f)
                return bounds;

            return new MaterialBlockBounds
            {
                CenterX = bounds.CenterX,
                TopBaselineY = bounds.TopBaselineY,
                BlockWidth = bounds.BlockWidth + (paddingPt * 2f),
                BlockHeight = bounds.BlockHeight + (paddingPt * 2f),
                Left = bounds.Left - paddingPt,
                Right = bounds.Right + paddingPt,
                Bottom = bounds.Bottom - paddingPt,
                Top = bounds.Top + paddingPt
            };
        }

        public static bool IntersectsForbiddenZone(
            MaterialBlockBounds bounds,
            IEnumerable<MaterialBlockBounds> forbiddenZones)
        {
            if (bounds == null || forbiddenZones == null)
                return false;

            foreach (var forbidden in forbiddenZones)
            {
                if (forbidden == null || forbidden.BlockWidth <= 0f)
                    continue;

                if (bounds.Left < forbidden.Right
                    && bounds.Right > forbidden.Left
                    && bounds.Top > forbidden.Bottom
                    && bounds.Bottom < forbidden.Top)
                {
                    return true;
                }
            }

            return false;
        }

        public static float MeasureInkDarkness(
            MaterialBlockBounds bounds,
            string pdfPath,
            int pageNumber,
            float pageWidth,
            float pageHeight)
        {
            if (!PdfRasterLayoutService.IsAvailable || string.IsNullOrWhiteSpace(pdfPath))
                return -1f;

            return MaterialWatermarkPlacementService.MeasureDarkness(
                pdfPath,
                pageNumber,
                bounds.CenterX,
                bounds.TopBaselineY,
                bounds.BlockWidth,
                bounds.BlockHeight,
                pageWidth,
                pageHeight);
        }

        public static bool IsHardBlocked(
            MaterialBlockBounds bounds,
            float pageWidth,
            float pageHeight,
            IEnumerable<PdfTextRect> occupiedRects = null,
            string pdfPath = null,
            int pageNumber = 1)
        {
            return EvaluateHardBlock(bounds, pageWidth, pageHeight, occupiedRects, pdfPath, pageNumber)
                   != MaterialHardBlockReason.None;
        }

        public static bool IntersectsDrawingCore(MaterialBlockBounds bounds, float pageWidth, float pageHeight)
        {
            GetDrawingCoreRect(pageWidth, pageHeight, out var left, out var right, out var bottom, out var top);
            return bounds.Left < right
                && bounds.Right > left
                && bounds.Top > bottom
                && bounds.Bottom < top;
        }

        public static bool IntersectsTitleBlock(MaterialBlockBounds bounds, float pageWidth, float pageHeight)
        {
            GetTitleBlockRect(pageWidth, pageHeight, out var left, out var right, out var bottom, out var top);
            return bounds.Left < right
                && bounds.Right > left
                && bounds.Top > bottom
                && bounds.Bottom < top;
        }

        public static string HardBlockReasonToGerman(MaterialHardBlockReason reason)
        {
            switch (reason)
            {
                case MaterialHardBlockReason.DrawingCore:
                    return "Core";
                case MaterialHardBlockReason.TitleBlock:
                    return "TitleBlock";
                case MaterialHardBlockReason.LeftDimensionStrip:
                    return "LeftStrip";
                case MaterialHardBlockReason.OutOfMargin:
                    return "Margin";
                case MaterialHardBlockReason.TextOverlap:
                    return "Overlap";
                case MaterialHardBlockReason.DrawingInk:
                    return "Ink";
                case MaterialHardBlockReason.ForbiddenZone:
                    return "Forbidden";
                default:
                    return "None";
            }
        }

        private static bool Intersects(MaterialBlockBounds bounds, PdfTextRect rect)
        {
            return bounds.Left < rect.Right
                && bounds.Right > rect.Left
                && bounds.Bottom < rect.Top
                && bounds.Top > rect.Bottom;
        }
    }
}
