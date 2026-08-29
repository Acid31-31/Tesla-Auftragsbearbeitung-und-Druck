using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace PDF_umbenenen.Services
{
    public enum MaterialPlacementFailureReason
    {
        None = 0,
        OnDrawingCore,
        OnTitleBlock,
        OutOfMargin,
        TooDark,
        RasterUnavailable,
        RepositionFailed,
        MissingCoordinates
    }

    public sealed class MaterialPlacementVerdict
    {
        public bool IsValid { get; set; }
        public bool IsGeometrySafe { get; set; }
        public MaterialPlacementFailureReason Reason { get; set; }
        public MaterialHardBlockReason HardBlockReason { get; set; }
        public float Darkness { get; set; }
        public float CenterX { get; set; }
        public float TopBaselineY { get; set; }
        public float BlockWidth { get; set; }
        public float BlockHeight { get; set; }
        public MaterialBlockBounds Bounds { get; set; }

        public string ToShortGerman()
        {
            if (IsGeometrySafe)
                return "OK";

            switch (HardBlockReason)
            {
                case MaterialHardBlockReason.DrawingCore:
                    return "AUF BAUTEIL";
                case MaterialHardBlockReason.TitleBlock:
                    return "IM SCHRIFTFELD";
                case MaterialHardBlockReason.DrawingInk:
                    return "AUF ZEICHNUNG";
                case MaterialHardBlockReason.LeftDimensionStrip:
                    return "MASSLINIEN LINKS";
                case MaterialHardBlockReason.OutOfMargin:
                    return "AUSSERHALB RAND";
                case MaterialHardBlockReason.TextOverlap:
                    return "TEXT-KOLLISION";
                default:
                    return "UNGÜLTIG";
            }
        }
    }

    internal static class MaterialWatermarkGuardService
    {
        public static MaterialPlacementVerdict ValidateMaterialStack(
            IList<WatermarkTextPlacement> materialPlacements,
            string pdfPath,
            int pageNumber,
            float pageWidth,
            float pageHeight,
            bool strictDarkness = true)
        {
            var verdict = new MaterialPlacementVerdict
            {
                Reason = MaterialPlacementFailureReason.RepositionFailed,
                Darkness = 1f
            };

            if (materialPlacements == null || materialPlacements.Count == 0)
            {
                verdict.IsValid = true;
                verdict.IsGeometrySafe = true;
                verdict.Reason = MaterialPlacementFailureReason.None;
                verdict.HardBlockReason = MaterialHardBlockReason.None;
                return verdict;
            }

            var bounds = MaterialBlockBounds.FromMaterialStack(materialPlacements);
            verdict.Bounds = bounds;
            verdict.CenterX = bounds.CenterX;
            verdict.TopBaselineY = bounds.TopBaselineY;
            verdict.BlockWidth = bounds.BlockWidth;
            verdict.BlockHeight = bounds.BlockHeight;

            var topLine = materialPlacements.OrderByDescending(p => p.Y).FirstOrDefault();
            if (topLine == null || (topLine.X <= 0f && topLine.Y <= 0f))
            {
                verdict.Reason = MaterialPlacementFailureReason.MissingCoordinates;
                return verdict;
            }

            return ValidateBounds(bounds, pdfPath, pageNumber, pageWidth, pageHeight, strictDarkness);
        }

        public static MaterialPlacementVerdict ValidateBlock(
            string pdfPath,
            int pageNumber,
            float centerX,
            float topBaselineY,
            float blockWidth,
            float blockHeight,
            float pageWidth,
            float pageHeight,
            bool strictDarkness = true)
        {
            var bounds = MaterialBlockBounds.FromAnchor(centerX, topBaselineY, blockWidth, blockHeight);
            return ValidateBounds(bounds, pdfPath, pageNumber, pageWidth, pageHeight, strictDarkness);
        }

        private static MaterialPlacementVerdict ValidateBounds(
            MaterialBlockBounds bounds,
            string pdfPath,
            int pageNumber,
            float pageWidth,
            float pageHeight,
            bool strictDarkness)
        {
            var verdict = new MaterialPlacementVerdict
            {
                Bounds = bounds,
                CenterX = bounds.CenterX,
                TopBaselineY = bounds.TopBaselineY,
                BlockWidth = bounds.BlockWidth,
                BlockHeight = bounds.BlockHeight,
                Darkness = 1f,
                IsValid = false,
                IsGeometrySafe = false
            };

            verdict.HardBlockReason = MaterialPlacementGeometry.EvaluateHardBlock(
                bounds, pageWidth, pageHeight, null, pdfPath, pageNumber);
            if (verdict.HardBlockReason != MaterialHardBlockReason.None)
            {
                verdict.Reason = MapHardReason(verdict.HardBlockReason);
                return verdict;
            }

            verdict.IsGeometrySafe = true;

            if (!PdfRasterLayoutService.IsAvailable || string.IsNullOrWhiteSpace(pdfPath))
            {
                verdict.IsValid = true;
                verdict.Reason = MaterialPlacementFailureReason.None;
                verdict.Darkness = -1f;
                return verdict;
            }

            verdict.Darkness = MaterialWatermarkPlacementService.MeasureDarkness(
                pdfPath,
                pageNumber,
                bounds.CenterX,
                bounds.TopBaselineY,
                bounds.BlockWidth,
                bounds.BlockHeight,
                pageWidth,
                pageHeight);

            if (strictDarkness && verdict.Darkness > PdfRasterLayoutService.MaterialPreferredMaxDarkness)
            {
                verdict.Reason = MaterialPlacementFailureReason.TooDark;
                return verdict;
            }

            verdict.IsValid = true;
            verdict.Reason = MaterialPlacementFailureReason.None;
            return verdict;
        }

        public static void EnforceFinalLockBeforeWrite(
            List<WatermarkTextPlacement> placements,
            string layoutPdfPath,
            int pageNumber,
            float pageWidth,
            float pageHeight,
            string drawingHint = null)
        {
            MaterialHardBlockEnforcer.EnforceBeforePdfWrite(
                placements,
                layoutPdfPath,
                pageNumber,
                pageWidth,
                pageHeight,
                drawingHint);
        }

        private static MaterialPlacementFailureReason MapHardReason(MaterialHardBlockReason reason)
        {
            switch (reason)
            {
                case MaterialHardBlockReason.DrawingCore:
                case MaterialHardBlockReason.LeftDimensionStrip:
                case MaterialHardBlockReason.DrawingInk:
                    return MaterialPlacementFailureReason.OnDrawingCore;
                case MaterialHardBlockReason.TitleBlock:
                    return MaterialPlacementFailureReason.OnTitleBlock;
                case MaterialHardBlockReason.OutOfMargin:
                    return MaterialPlacementFailureReason.OutOfMargin;
                default:
                    return MaterialPlacementFailureReason.RepositionFailed;
            }
        }
    }
}
