using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace PDF_umbenenen.Services
{
    /// <summary>
    /// Deterministischer Constraint-Solver: Vektor-Belegung primär, Raster nur sekundär.
    /// </summary>
    internal static class MaterialConstraintSolver
    {
        private const int MaxCandidateEvaluations = 24;
        private const int BatchMaxCandidateEvaluations = 12;
        private const float CoarseStepPt = 28f;
        private const float BatchCoarseStepPt = 42f;
        private const float RefineStepPt = 8f;
        private const float MinLineClearancePt = 4f;
        private const float ExcellentClearancePt = 22f;
        private const float EarlyExitPeakDarkness = 0.10f;
        private const float EarlyExitMeanDarkness = 0.12f;
        private const float RasterVerifyPeakLimit = 0.20f;
        private const float RasterVerifyMeanLimit = 0.24f;

        public static bool TrySolve(
            string pdfPath,
            int pageNumber,
            float blockWidth,
            float blockHeight,
            float pageWidth,
            float pageHeight,
            IList<PdfTextRect> extraOccupied,
            IList<MaterialBlockBounds> forbiddenZones,
            out float centerX,
            out float topBaselineY,
            out float clearance,
            out float meanDarkness,
            out float peakDarkness,
            out string methodLabel,
            out int candidateCount)
        {
            centerX = 0f;
            topBaselineY = 0f;
            clearance = 0f;
            meanDarkness = 1f;
            peakDarkness = 1f;
            methodLabel = "vector-constraint";
            candidateCount = 0;

            if (pageWidth <= 0f || pageHeight <= 0f || blockWidth <= 0f || blockHeight <= 0f)
                return false;

            if (MaterialPlacementPerfStats.IsBudgetExceeded())
                return false;

            PdfLayoutAnalysisService.AnalyzePage(pdfPath, pageNumber);
            var occupancy = PdfVectorOccupancyService.GetPageOccupancy(pdfPath, pageNumber);
            if (PdfRasterLayoutService.IsAvailable
                && !WatermarkProcessingContext.BatchMode
                && !string.IsNullOrWhiteSpace(pdfPath))
                PdfRasterLayoutService.EnsurePageGrid(pdfPath, pageNumber, pageWidth, pageHeight);

            var obstacles = new List<PdfTextRect>(occupancy.Obstacles);
            AppendExtraOccupied(obstacles, extraOccupied, pageWidth, pageHeight);

            var margin = MaterialPlacementGeometry.MinMarginPt;
            var minCenterX = margin + (blockWidth / 2f);
            var maxCenterX = pageWidth - margin - (blockWidth / 2f);
            var minTopY = margin + blockHeight;
            var maxTopY = pageHeight - margin;

            if (maxCenterX <= minCenterX || maxTopY <= minTopY)
                return false;

            var coarseStep = WatermarkProcessingContext.BatchMode ? BatchCoarseStepPt : CoarseStepPt;
            var maxCandidates = WatermarkProcessingContext.BatchMode
                ? BatchMaxCandidateEvaluations
                : MaxCandidateEvaluations;

            var coarse = new List<(float X, float Y, float Clearance)>();
            for (var y = minTopY; y <= maxTopY; y += coarseStep)
            {
                if (MaterialPlacementPerfStats.IsBudgetExceeded())
                    return false;

                for (var x = minCenterX; x <= maxCenterX; x += coarseStep)
                {
                    var bounds = MaterialBlockBounds.FromAnchor(x, y, blockWidth, blockHeight);
                    if (!PassesHardConstraints(bounds, pageWidth, pageHeight, obstacles, forbiddenZones))
                        continue;

                    var candidateClearance = MeasureClearance(bounds, obstacles, MinLineClearancePt);
                    if (candidateClearance < MinLineClearancePt)
                        continue;

                    coarse.Add((x, y, candidateClearance));
                }
            }

            if (coarse.Count == 0)
                return false;

            var seeds = coarse
                .OrderByDescending(c => c.Clearance)
                .ThenBy(c => c.Y)
                .ThenBy(c => c.X)
                .Take(maxCandidates)
                .ToList();

            var bestClearance = float.MinValue;
            var found = false;
            var evaluated = 0;
            float bestX = 0f;
            float bestY = 0f;
            float bestMean = 1f;
            float bestPeak = 1f;

            foreach (var seed in seeds)
            {
                if (MaterialPlacementPerfStats.IsBudgetExceeded())
                    goto PickBest;

                for (var dy = -RefineStepPt; dy <= RefineStepPt; dy += RefineStepPt)
                {
                    for (var dx = -RefineStepPt; dx <= RefineStepPt; dx += RefineStepPt)
                    {
                        if (MaterialPlacementPerfStats.IsBudgetExceeded())
                            goto PickBest;

                        var x = seed.X + dx;
                        var y = seed.Y + dy;
                        if (x < minCenterX || x > maxCenterX || y < minTopY || y > maxTopY)
                            continue;

                        evaluated++;
                        candidateCount = evaluated;
                        if (evaluated > maxCandidates)
                            goto PickBest;

                        var bounds = MaterialBlockBounds.FromAnchor(x, y, blockWidth, blockHeight);
                        if (!PassesHardConstraints(bounds, pageWidth, pageHeight, obstacles, forbiddenZones))
                            continue;

                        var candidateClearance = MeasureClearance(bounds, obstacles, MinLineClearancePt);
                        if (candidateClearance < MinLineClearancePt)
                            continue;

                        if (candidateClearance <= bestClearance)
                            continue;

                        float mean;
                        float peak;
                        if (WatermarkProcessingContext.BatchMode)
                        {
                            mean = 0f;
                            peak = 0f;
                        }
                        else if (!TryMeasureRaster(
                                pdfPath,
                                pageNumber,
                                bounds,
                                pageWidth,
                                pageHeight,
                                occupancy.HasRichVectorLayer,
                                out mean,
                                out peak))
                        {
                            mean = 0f;
                            peak = 0f;
                        }

                        if (mean >= MaterialPlacementGeometry.MaterialInkHardBlockDarkness
                            || peak >= MaterialPlacementGeometry.MaterialInkPeakHardBlockDarkness)
                        {
                            continue;
                        }

                        bestClearance = candidateClearance;
                        bestX = x;
                        bestY = y;
                        bestMean = mean;
                        bestPeak = peak;
                        found = true;

                        if (candidateClearance >= ExcellentClearancePt
                            || (peak < EarlyExitPeakDarkness && mean < EarlyExitMeanDarkness))
                        {
                            goto PickBest;
                        }
                    }
                }
            }

        PickBest:
            if (!found)
                return false;

            centerX = bestX;
            topBaselineY = bestY;
            clearance = bestClearance;
            meanDarkness = bestMean;
            peakDarkness = bestPeak;

            var finalBounds = MaterialBlockBounds.FromAnchor(centerX, topBaselineY, blockWidth, blockHeight);
            if (!PassesHardConstraints(finalBounds, pageWidth, pageHeight, obstacles, forbiddenZones))
                return false;

            if (WatermarkProcessingContext.BatchMode)
            {
                methodLabel = "vector-batch";
                return MaterialPlacementGeometry.EvaluateGeometryBlock(
                           finalBounds, pageWidth, pageHeight, obstacles) == MaterialHardBlockReason.None;
            }

            if (occupancy.HasRichVectorLayer
                && peakDarkness < EarlyExitPeakDarkness
                && meanDarkness < EarlyExitMeanDarkness)
            {
                methodLabel = "vector-only";
                return MaterialPlacementGeometry.EvaluateGeometryBlock(
                           finalBounds, pageWidth, pageHeight, obstacles) == MaterialHardBlockReason.None;
            }

            if (!TryMeasureRaster(
                    pdfPath,
                    pageNumber,
                    finalBounds,
                    pageWidth,
                    pageHeight,
                    forceRaster: true,
                    out meanDarkness,
                    out peakDarkness))
            {
                if (occupancy.HasRichVectorLayer)
                {
                    methodLabel = "vector-only";
                    return MaterialPlacementGeometry.EvaluateGeometryBlock(
                               finalBounds, pageWidth, pageHeight, obstacles) == MaterialHardBlockReason.None;
                }

                return false;
            }

            if (meanDarkness >= RasterVerifyMeanLimit || peakDarkness >= RasterVerifyPeakLimit)
                return false;

            methodLabel = occupancy.HasRichVectorLayer ? "vector+verify" : "vector+raster";
            return MaterialPlacementGeometry.EvaluateHardBlock(
                       finalBounds,
                       pageWidth,
                       pageHeight,
                       obstacles,
                       pdfPath,
                       pageNumber) == MaterialHardBlockReason.None;
        }

        private static bool TryMeasureRaster(
            string pdfPath,
            int pageNumber,
            MaterialBlockBounds bounds,
            float pageWidth,
            float pageHeight,
            bool forceRaster,
            out float meanDarkness,
            out float peakDarkness)
        {
            meanDarkness = 1f;
            peakDarkness = 1f;

            if (!forceRaster || !PdfRasterLayoutService.IsAvailable || string.IsNullOrWhiteSpace(pdfPath))
                return false;

            var sw = Stopwatch.StartNew();
            try
            {
                meanDarkness = PdfRasterLayoutService.MeasureMaterialPlacementDarkness(
                    pdfPath,
                    pageNumber,
                    bounds.CenterX,
                    bounds.TopBaselineY,
                    bounds.BlockWidth,
                    bounds.BlockHeight,
                    pageWidth,
                    pageHeight);
                peakDarkness = PdfRasterLayoutService.MeasureMaterialPlacementPeakDarkness(
                    pdfPath,
                    pageNumber,
                    bounds.CenterX,
                    bounds.TopBaselineY,
                    bounds.BlockWidth,
                    bounds.BlockHeight,
                    pageWidth,
                    pageHeight);
                return true;
            }
            finally
            {
                sw.Stop();
                MaterialPlacementPerfStats.AddRasterMs(sw.ElapsedMilliseconds);
            }
        }

        private static bool PassesHardConstraints(
            MaterialBlockBounds bounds,
            float pageWidth,
            float pageHeight,
            IList<PdfTextRect> obstacles,
            IList<MaterialBlockBounds> forbiddenZones)
        {
            if (MaterialPlacementGeometry.EvaluateGeometryBlock(
                    bounds, pageWidth, pageHeight, obstacles) != MaterialHardBlockReason.None)
            {
                return false;
            }

            if (forbiddenZones == null || !forbiddenZones.Any())
                return true;

            return MaterialPlacementGeometry.EvaluatePlacementHardBlock(
                       bounds,
                       pageWidth,
                       pageHeight,
                       null,
                       1,
                       obstacles,
                       forbiddenZones) == MaterialHardBlockReason.None;
        }

        private static float MeasureClearance(
            MaterialBlockBounds bounds,
            IList<PdfTextRect> obstacles,
            float minRequired)
        {
            if (obstacles == null || obstacles.Count == 0)
                return float.MaxValue;

            var minDistance = float.MaxValue;
            foreach (var obstacle in obstacles)
            {
                if (obstacle == null)
                    continue;

                var distance = RectClearanceDistance(bounds, obstacle);
                if (distance <= 0f)
                    return 0f;

                if (distance < minDistance)
                    minDistance = distance;
            }

            return minDistance < minRequired ? 0f : minDistance;
        }

        private static float RectClearanceDistance(MaterialBlockBounds bounds, PdfTextRect obstacle)
        {
            if (bounds.Left < obstacle.Right
                && bounds.Right > obstacle.Left
                && bounds.Bottom < obstacle.Top
                && bounds.Top > obstacle.Bottom)
            {
                return 0f;
            }

            var dx = 0f;
            if (bounds.Right < obstacle.Left)
                dx = obstacle.Left - bounds.Right;
            else if (bounds.Left > obstacle.Right)
                dx = bounds.Left - obstacle.Right;

            var dy = 0f;
            if (bounds.Top < obstacle.Bottom)
                dy = obstacle.Bottom - bounds.Top;
            else if (bounds.Bottom > obstacle.Top)
                dy = bounds.Bottom - obstacle.Top;

            if (dx <= 0f)
                return dy;
            if (dy <= 0f)
                return dx;

            return (float)Math.Sqrt((dx * dx) + (dy * dy));
        }

        private static void AppendExtraOccupied(
            List<PdfTextRect> obstacles,
            IList<PdfTextRect> extraOccupied,
            float pageWidth,
            float pageHeight)
        {
            if (extraOccupied == null)
            {
                MaterialPlacementGeometry.AppendReservedWatermarkRects(obstacles, pageWidth, pageHeight);
                return;
            }

            foreach (var rect in extraOccupied)
            {
                if (rect == null)
                    continue;

                obstacles.Add(new PdfTextRect
                {
                    Text = rect.Text,
                    Left = rect.Left - 8f,
                    Right = rect.Right + 8f,
                    Bottom = rect.Bottom - 6f,
                    Top = rect.Top + 6f
                });
            }

            MaterialPlacementGeometry.AppendReservedWatermarkRects(obstacles, pageWidth, pageHeight);
        }
    }
}
