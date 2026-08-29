using System;
using System.Collections.Generic;
using System.Linq;

namespace PDF_umbenenen.Services
{
    /// <summary>
    /// Material-Wasserzeichen: zuverlässige Regel-Platzierung (kein Raster-KI-Raten).
    /// </summary>
    internal static class MaterialWatermarkPlacementService
    {
        public const int MaterialAlignment = 1;
        public const float MinPageMarginPt = 28f;

        public static List<WatermarkTextPlacement> PlaceDynamic(
            IList<string> lines,
            string pdfPath,
            int pageNumber,
            float pageWidth,
            float pageHeight,
            IList<PdfTextRect> occupied)
        {
            return ReliableMaterialPlacementService.Place(
                lines,
                pdfPath,
                pageNumber,
                pageWidth,
                pageHeight,
                occupied);
        }

        public static List<WatermarkTextPlacement> PlaceDynamicGuaranteed(
            IList<string> lines,
            string pdfPath,
            int pageNumber,
            float pageWidth,
            float pageHeight)
        {
            return ReliableMaterialPlacementService.Place(
                lines,
                pdfPath,
                pageNumber,
                pageWidth,
                pageHeight,
                Array.Empty<PdfTextRect>());
        }

        public static void EnforceAuthoritative(
            List<WatermarkTextPlacement> placements,
            string pdfPath,
            int pageNumber,
            float pageWidth,
            float pageHeight,
            IList<PdfTextRect> occupied)
        {
            if (placements == null || placements.Count == 0 || string.IsNullOrWhiteSpace(pdfPath))
                return;

            var materialLines = placements
                .Where(p => p != null && string.Equals(p.Category, "Material", StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (materialLines.Count == 0)
                return;

            var texts = materialLines.Select(p => p.Text).Where(t => !string.IsNullOrWhiteSpace(t)).ToList();
            if (texts.Count == 0)
                return;

            var currentBounds = MaterialBlockBounds.FromMaterialStack(materialLines);
            var occupiedForCheck = occupied != null ? occupied.ToList() : new List<PdfTextRect>();
            MaterialPlacementGeometry.AppendReservedWatermarkRects(occupiedForCheck, pageWidth, pageHeight);

            var currentReason = MaterialPlacementGeometry.EvaluateHardBlock(
                currentBounds, pageWidth, pageHeight, occupiedForCheck, pdfPath, pageNumber);

            if (currentReason == MaterialHardBlockReason.None)
                return;

            WatermarkLearningStore.ClearOnPlacementError(
                "material-authoritative:" + MaterialPlacementGeometry.HardBlockReasonToGerman(currentReason));

            placements.RemoveAll(p =>
                p != null && string.Equals(p.Category, "Material", StringComparison.OrdinalIgnoreCase));

            var refreshed = PlaceDynamic(texts, pdfPath, pageNumber, pageWidth, pageHeight, occupied);
            foreach (var placement in refreshed)
            {
                placement.Category = "Material";
                placements.Add(placement);
            }
        }

        public static void GetMaterialBlockMetrics(
            IList<WatermarkTextPlacement> materialPlacements,
            out float blockWidth,
            out float blockHeight,
            out float fontSize)
        {
            blockWidth = 20f;
            blockHeight = 22f;
            fontSize = 22f;

            if (materialPlacements == null || materialPlacements.Count == 0)
                return;

            fontSize = materialPlacements
                .Select(p => p.FontSize)
                .FirstOrDefault(s => s > 0f);
            if (fontSize <= 0f)
                fontSize = 22f;

            var resolvedFontSize = fontSize;
            blockWidth = materialPlacements.Max(p =>
                SmartWatermarkLayoutService.EstimateTextWidth(p.Text, p.FontSize > 0f ? p.FontSize : resolvedFontSize));
            blockHeight = PdfRasterLayoutService.GetMaterialBlockHeight(fontSize, materialPlacements.Count);
        }

        public static float MeasureDarkness(
            string pdfPath,
            int pageNumber,
            float centerX,
            float topBaselineY,
            float blockWidth,
            float blockHeight,
            float pageWidth,
            float pageHeight)
        {
            if (!PdfRasterLayoutService.IsAvailable)
                return -1f;

            return PdfRasterLayoutService.MeasureMaterialPlacementDarkness(
                pdfPath,
                pageNumber,
                centerX,
                topBaselineY,
                blockWidth,
                blockHeight,
                pageWidth,
                pageHeight);
        }

        public static float MeasureInkDarknessRobust(
            string pdfPath,
            int pageNumber,
            float centerX,
            float topBaselineY,
            float blockWidth,
            float blockHeight,
            float pageWidth,
            float pageHeight)
        {
            if (!PdfRasterLayoutService.IsAvailable)
                return -1f;

            return PdfRasterLayoutService.MeasureMaterialPlacementRobustDarkness(
                pdfPath,
                pageNumber,
                centerX,
                topBaselineY,
                blockWidth,
                blockHeight,
                pageWidth,
                pageHeight);
        }

        public static bool IsPlacementValid(
            string pdfPath,
            int pageNumber,
            float centerX,
            float topBaselineY,
            float blockWidth,
            float blockHeight,
            float pageWidth,
            float pageHeight)
        {
            var bounds = MaterialBlockBounds.FromAnchor(centerX, topBaselineY, blockWidth, blockHeight);
            return MaterialPlacementGeometry.EvaluateHardBlock(
                       bounds, pageWidth, pageHeight, null, pdfPath, pageNumber)
                   == MaterialHardBlockReason.None;
        }
    }
}
