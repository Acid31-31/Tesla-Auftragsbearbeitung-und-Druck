using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace PDF_umbenenen.Services
{
    /// <summary>
    /// Material-Platzierung: Vektor-Constraint-Solver primär, Raster nur sekundär.
    /// </summary>
    internal static class MaterialFreeAreaPlacementService
    {
        public static List<WatermarkTextPlacement> Place(
            IList<string> lines,
            string pdfPath,
            int pageNumber,
            float pageWidth,
            float pageHeight,
            IList<PdfTextRect> occupiedRects,
            MaterialPlacementPageLog pageLog = null,
            IList<MaterialBlockBounds> forbiddenZones = null)
        {
            var placementSw = Stopwatch.StartNew();
            MaterialPlacementPerfStats.BeginPlacementBudget();

            try
            {
                var cleanLines = (lines ?? Array.Empty<string>())
                    .Where(l => !string.IsNullOrWhiteSpace(l))
                    .Select(l => l.Trim())
                    .ToList();

                if (cleanLines.Count == 0)
                    return new List<WatermarkTextPlacement>();

                if (WatermarkProcessingContext.BatchMode)
                {
                    if (pageWidth <= 0f || pageHeight <= 0f)
                    {
                        var batchLayout = PdfLayoutAnalysisService.AnalyzePage(pdfPath, pageNumber);
                        if (pageWidth <= 0f && batchLayout.PageWidth > 0f)
                            pageWidth = batchLayout.PageWidth;
                        if (pageHeight <= 0f && batchLayout.PageHeight > 0f)
                            pageHeight = batchLayout.PageHeight;
                    }

                    var batchResult = TryPlaceBatchFallback(
                        cleanLines,
                        pageWidth,
                        pageHeight,
                        occupiedRects ?? Array.Empty<PdfTextRect>(),
                        pageLog,
                        forbiddenZones);
                    placementSw.Stop();
                    if (pageLog != null)
                        pageLog.PlacementMs = placementSw.ElapsedMilliseconds;
                    return batchResult;
                }

                if (pageWidth <= 0f || pageHeight <= 0f)
                {
                    var layout = PdfLayoutAnalysisService.AnalyzePage(pdfPath, pageNumber);
                    if (pageWidth <= 0f && layout.PageWidth > 0f)
                        pageWidth = layout.PageWidth;
                    if (pageHeight <= 0f && layout.PageHeight > 0f)
                        pageHeight = layout.PageHeight;
                }
                else
                {
                    PdfLayoutAnalysisService.AnalyzePage(pdfPath, pageNumber);
                }

                PdfVectorOccupancyService.GetPageOccupancy(pdfPath, pageNumber);
                if (PdfRasterLayoutService.IsAvailable
                    && !WatermarkProcessingContext.BatchMode
                    && !string.IsNullOrWhiteSpace(pdfPath))
                    PdfRasterLayoutService.EnsurePageGrid(pdfPath, pageNumber, pageWidth, pageHeight);

                var longestLine = cleanLines.OrderByDescending(l => l.Length).First();
                var fontSizes = new[] { WatermarkPageScale.GetPageNumberFontSize(pageWidth, pageHeight) };

                foreach (var maxFont in fontSizes)
                {
                    if (MaterialPlacementPerfStats.IsBudgetExceeded())
                        break;

                    var fontSize = SmartWatermarkLayoutService.ResolveDrawingAnnotationFontSize(
                        longestLine,
                        pageWidth,
                        pageHeight);
                    fontSize = Math.Min(fontSize, maxFont);
                    var blockWidth = cleanLines.Max(l => SmartWatermarkLayoutService.EstimateTextWidth(l, fontSize));
                    var blockHeight = PdfRasterLayoutService.GetMaterialBlockHeight(fontSize, cleanLines.Count);

                    if (!MaterialConstraintSolver.TrySolve(
                            pdfPath,
                            pageNumber,
                            blockWidth,
                            blockHeight,
                            pageWidth,
                            pageHeight,
                            occupiedRects,
                            forbiddenZones,
                            out var pickX,
                            out var pickY,
                            out var clearance,
                            out var meanDark,
                            out var peakDark,
                            out var methodLabel,
                            out var candidateCount))
                    {
                        continue;
                    }

                    var bounds = MaterialBlockBounds.FromAnchor(pickX, pickY, blockWidth, blockHeight);
                    var finalReject = WatermarkProcessingContext.BatchMode
                        ? MaterialPlacementGeometry.EvaluateGeometryBlock(
                            bounds, pageWidth, pageHeight, occupiedRects)
                        : MaterialPlacementGeometry.EvaluateHardBlock(
                            bounds, pageWidth, pageHeight, occupiedRects, pdfPath, pageNumber);

                    if (finalReject != MaterialHardBlockReason.None)
                        continue;

                    if (pageLog != null)
                    {
                        pageLog.ChosenLabel = methodLabel;
                        pageLog.SelectedBy = methodLabel;
                        pageLog.ChosenScore = clearance;
                        pageLog.ChosenRel = bounds.ToRelString(pageWidth, pageHeight);
                        pageLog.ChosenPeak = peakDark;
                        pageLog.ChosenMean = meanDark;
                        pageLog.ChosenRejectReason = finalReject;
                        pageLog.CandidateCount = candidateCount;
                        pageLog.OcrMs = MaterialPlacementPerfStats.OcrMs;
                        pageLog.RasterMs = MaterialPlacementPerfStats.RasterMs;
                    }

                    var result = BuildStack(cleanLines, pickX, pickY, fontSize);
                    if (pageLog != null)
                    {
                        placementSw.Stop();
                        pageLog.PlacementMs = placementSw.ElapsedMilliseconds;
                    }

                    return result;
                }

                if (pageLog != null)
                {
                    placementSw.Stop();
                    pageLog.PlacementMs = placementSw.ElapsedMilliseconds;
                    pageLog.OcrMs = MaterialPlacementPerfStats.OcrMs;
                    pageLog.RasterMs = MaterialPlacementPerfStats.RasterMs;
                    pageLog.SelectedBy = MaterialPlacementPerfStats.IsBudgetExceeded()
                        ? "budget-timeout"
                        : "no-candidate";
                }

                if (WatermarkProcessingContext.BatchMode)
                {
                    var fallback = TryPlaceBatchFallback(
                        cleanLines,
                        pageWidth,
                        pageHeight,
                        occupiedRects,
                        pageLog,
                        forbiddenZones);
                    if (fallback.Count > 0)
                        return fallback;
                }

                return new List<WatermarkTextPlacement>();
            }
            finally
            {
                MaterialPlacementPerfStats.CaptureSnapshot(
                    pageLog?.PlacementMs ?? -1,
                    pageLog?.CandidateCount ?? -1,
                    pageLog?.SelectedBy,
                    pageLog?.ChosenLabel,
                    pageLog?.ChosenPeak ?? float.NaN,
                    pageLog?.ChosenMean ?? float.NaN);
            }
        }

        private static List<WatermarkTextPlacement> TryPlaceBatchFallback(
            IList<string> cleanLines,
            float pageWidth,
            float pageHeight,
            IList<PdfTextRect> occupiedRects,
            MaterialPlacementPageLog pageLog,
            IList<MaterialBlockBounds> forbiddenZones = null)
        {
            if (cleanLines == null || cleanLines.Count == 0 || pageWidth <= 0f || pageHeight <= 0f)
                return new List<WatermarkTextPlacement>();

            var longestLine = cleanLines.OrderByDescending(l => l.Length).First();
            var fontSize = SmartWatermarkLayoutService.ResolveDrawingAnnotationFontSize(
                longestLine,
                pageWidth,
                pageHeight);
            var blockWidth = cleanLines.Max(l => SmartWatermarkLayoutService.EstimateTextWidth(l, fontSize));
            var blockHeight = PdfRasterLayoutService.GetMaterialBlockHeight(fontSize, cleanLines.Count);
            var margin = MaterialPlacementGeometry.MinMarginPt;
            var titleTop = pageHeight * MaterialPlacementGeometry.TitleBlockTopRatio;
            var titleBlockLeft = pageWidth * MaterialPlacementGeometry.TitleBlockLeftRatio;
            var titleStripX = Math.Max(margin + (blockWidth / 2f), titleBlockLeft - (blockWidth / 2f) - 36f);

            var candidates = new[]
            {
                (titleStripX, titleTop + 90f),
                (titleStripX, titleTop + 72f),
                (titleStripX, titleTop + 52f),
                (margin + (blockWidth / 2f) + 48f, titleTop + 42f)
            };

            foreach (var (candidateX, topY) in candidates)
            {
                var bounds = MaterialBlockBounds.FromAnchor(candidateX, topY, blockWidth, blockHeight);
                if (!IsBatchMaterialGeometryOk(bounds, pageWidth, pageHeight))
                {
                    continue;
                }

                if (pageLog != null)
                {
                    pageLog.ChosenLabel = "batch-fallback";
                    pageLog.SelectedBy = "batch-fallback";
                    pageLog.ChosenRel = bounds.ToRelString(pageWidth, pageHeight);
                    pageLog.ChosenRejectReason = MaterialHardBlockReason.None;
                }

                return BuildStack(cleanLines, candidateX, topY, fontSize);
            }

            var forcedX = titleBlockLeft - (blockWidth / 2f) - 40f;
            var forcedY = titleTop + 90f;
            if (pageLog != null)
            {
                pageLog.ChosenLabel = "batch-forced";
                pageLog.SelectedBy = "batch-forced";
                pageLog.ChosenRel = MaterialBlockBounds.FromAnchor(forcedX, forcedY, blockWidth, blockHeight)
                    .ToRelString(pageWidth, pageHeight);
                pageLog.ChosenRejectReason = MaterialHardBlockReason.None;
            }

            return BuildStack(cleanLines, forcedX, forcedY, fontSize);
        }

        private static bool IsBatchMaterialGeometryOk(
            MaterialBlockBounds bounds,
            float pageWidth,
            float pageHeight)
        {
            if (pageWidth <= 0f || pageHeight <= 0f || bounds.BlockWidth <= 0f || bounds.BlockHeight <= 0f)
                return false;

            if (bounds.Left < MaterialPlacementGeometry.MinMarginPt
                || bounds.Right > pageWidth - MaterialPlacementGeometry.MinMarginPt
                || bounds.Bottom < MaterialPlacementGeometry.MinMarginPt
                || bounds.Top > pageHeight - MaterialPlacementGeometry.MinMarginPt)
            {
                return false;
            }

            if (MaterialPlacementGeometry.IntersectsDrawingCore(bounds, pageWidth, pageHeight))
                return false;

            if (MaterialPlacementGeometry.IntersectsTitleBlock(bounds, pageWidth, pageHeight))
                return false;

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
                return false;
            }

            return true;
        }

        public static bool TryFindBestFreePosition(
            string pdfPath,
            int pageNumber,
            float blockWidth,
            float blockHeight,
            float pageWidth,
            float pageHeight,
            IList<PdfTextRect> occupiedRects,
            IList<MaterialBlockBounds> forbiddenZones,
            out float centerX,
            out float topBaselineY,
            out float meanDarkness,
            out float peakDarkness,
            out float score)
        {
            MaterialPlacementPerfStats.BeginPlacementBudget();
            try
            {
                if (MaterialConstraintSolver.TrySolve(
                        pdfPath,
                        pageNumber,
                        blockWidth,
                        blockHeight,
                        pageWidth,
                        pageHeight,
                        occupiedRects,
                        forbiddenZones,
                        out centerX,
                        out topBaselineY,
                        out var clearance,
                        out meanDarkness,
                        out peakDarkness,
                        out _,
                        out _))
                {
                    score = clearance;
                    return true;
                }
            }
            finally
            {
                MaterialPlacementPerfStats.Reset();
            }

            centerX = 0f;
            topBaselineY = 0f;
            meanDarkness = 1f;
            peakDarkness = 1f;
            score = float.MaxValue;
            return false;
        }

        private static List<WatermarkTextPlacement> BuildStack(
            IList<string> cleanLines,
            float centerX,
            float topBaselineY,
            float fontSize)
        {
            var lineHeight = PdfRasterLayoutService.GetMaterialLineHeight(fontSize);

            var result = new List<WatermarkTextPlacement>();
            for (var i = 0; i < cleanLines.Count; i++)
            {
                result.Add(new WatermarkTextPlacement
                {
                    Text = cleanLines[i],
                    X = centerX,
                    Y = topBaselineY - (i * lineHeight),
                    Alignment = MaterialWatermarkPlacementService.MaterialAlignment,
                    FontSize = fontSize
                });
            }

            return result;
        }
    }
}
