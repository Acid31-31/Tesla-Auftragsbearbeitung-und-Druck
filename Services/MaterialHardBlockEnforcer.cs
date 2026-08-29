using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace PDF_umbenenen.Services
{
    internal sealed class MaterialPlacementPageLog
    {
        public string PdfFileName { get; set; }
        public int PageNumber { get; set; }
        public string DrawingHint { get; set; }
        public string Action { get; set; }
        public MaterialBlockBounds FinalBounds { get; set; }
        public float Darkness { get; set; } = -1f;
        public MaterialHardBlockReason FinalRejectReason { get; set; }
        public string ChosenLabel { get; set; } = string.Empty;
        public string ChosenRel { get; set; } = string.Empty;
        public float ChosenScore { get; set; } = float.NaN;
        public float ChosenPeak { get; set; } = float.NaN;
        public float ChosenMean { get; set; } = float.NaN;
        public MaterialHardBlockReason ChosenRejectReason { get; set; } = MaterialHardBlockReason.None;
        public long PlacementMs { get; set; } = -1;
        public long OcrMs { get; set; } = -1;
        public long RasterMs { get; set; } = -1;
        public int CandidateCount { get; set; } = -1;
        public string SelectedBy { get; set; } = string.Empty;
        public readonly List<string> CandidateLines = new List<string>();
    }

    /// <summary>
    /// Hard-Block: Material nie in DrawingCore/TitleBlock. Letzter Schritt vor PDF-Write.
    /// </summary>
    internal static class MaterialHardBlockEnforcer
    {
        private const float ForbiddenZonePaddingPt = 42f;
        private static readonly object LogLock = new object();

        public static void EnforceBeforePdfWrite(
            List<WatermarkTextPlacement> placements,
            string layoutPdfPath,
            int pageNumber,
            float pageWidth,
            float pageHeight,
            string drawingHint = null)
        {
            if (placements == null || placements.Count == 0 || string.IsNullOrWhiteSpace(layoutPdfPath))
                return;

            if (WatermarkProcessingContext.BatchMode)
                return;

            var materialPlacements = placements
                .Where(p => p != null && string.Equals(p.Category, "Material", StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (materialPlacements.Count == 0)
                return;

            var texts = materialPlacements
                .Select(p => p.Text)
                .Where(t => !string.IsNullOrWhiteSpace(t))
                .ToList();

            if (texts.Count == 0)
                return;

            var pageLog = new MaterialPlacementPageLog
            {
                PdfFileName = Path.GetFileName(layoutPdfPath),
                PageNumber = pageNumber,
                DrawingHint = drawingHint ?? string.Empty
            };

            MaterialPlacementPerfStats.ApplySnapshot(pageLog);

            var forbiddenZones = new List<MaterialBlockBounds>();
            var currentBounds = MaterialBlockBounds.FromMaterialStack(materialPlacements);
            var currentDark = WatermarkProcessingContext.BatchMode
                ? -1f
                : MeasureDarkness(layoutPdfPath, pageNumber, currentBounds, pageWidth, pageHeight);
            var currentReason = MaterialPlacementGeometry.EvaluateHardBlock(
                currentBounds, pageWidth, pageHeight, null, layoutPdfPath, pageNumber);

            if (currentReason != MaterialHardBlockReason.None)
            {
                WatermarkLearningStore.ClearOnPlacementError(
                    "material-hardblock:" + MaterialPlacementGeometry.HardBlockReasonToGerman(currentReason));

                pageLog.CandidateLines.Add(FormatCandidateLine(
                    "current",
                    currentBounds,
                    currentReason,
                    pageWidth,
                    pageHeight,
                    currentDark,
                    float.NaN));

                placements.RemoveAll(p =>
                    p != null && string.Equals(p.Category, "Material", StringComparison.OrdinalIgnoreCase));

                if (!WatermarkProcessingContext.BatchMode)
                {
                    var repositionAttempts = 0;
                    var validRepositioned = new List<WatermarkTextPlacement>();

                    while (repositionAttempts < 2
                           && validRepositioned.Count == 0
                           && !MaterialPlacementPerfStats.IsBudgetExceeded())
                    {
                        repositionAttempts++;
                        MaterialPlacementPerfStats.BeginPlacementBudget();
                        forbiddenZones.Add(MaterialPlacementGeometry.InflateBounds(currentBounds, ForbiddenZonePaddingPt));

                        var repositioned = PlaceWithHardBlock(
                            texts,
                            layoutPdfPath,
                            pageNumber,
                            pageWidth,
                            pageHeight,
                            pageLog,
                            forbiddenZones);

                        validRepositioned = FilterValidMaterialPlacements(
                            repositioned, layoutPdfPath, pageNumber, pageWidth, pageHeight);

                        if (validRepositioned.Count == 0 && repositioned.Count > 0)
                        {
                            var failedBounds = MaterialBlockBounds.FromMaterialStack(repositioned);
                            forbiddenZones.Add(MaterialPlacementGeometry.InflateBounds(failedBounds, ForbiddenZonePaddingPt));
                        }
                    }

                    foreach (var placement in validRepositioned)
                    {
                        placement.Category = "Material";
                        placements.Add(placement);
                    }

                    pageLog.Action = validRepositioned.Count > 0 ? "HardBlock-Reposition" : "HardBlock-Omitted";
                }
                else
                {
                    forbiddenZones.Add(MaterialPlacementGeometry.InflateBounds(currentBounds, ForbiddenZonePaddingPt));
                    var repositioned = PlaceWithHardBlock(
                        texts,
                        layoutPdfPath,
                        pageNumber,
                        pageWidth,
                        pageHeight,
                        pageLog,
                        forbiddenZones);
                    var validRepositioned = FilterValidMaterialPlacements(
                        repositioned, layoutPdfPath, pageNumber, pageWidth, pageHeight);

                    foreach (var placement in validRepositioned)
                    {
                        placement.Category = "Material";
                        placements.Add(placement);
                    }

                    pageLog.Action = validRepositioned.Count > 0 ? "HardBlock-Reposition" : "HardBlock-Omitted";
                }
            }
            else
            {
                pageLog.Action = "HardBlock-OK";
            }

            var finalMaterial = placements
                .Where(p => p != null && string.Equals(p.Category, "Material", StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (finalMaterial.Count > 0)
            {
                pageLog.FinalBounds = MaterialBlockBounds.FromMaterialStack(finalMaterial);
                pageLog.Darkness = WatermarkProcessingContext.BatchMode
                    ? -1f
                    : MeasureDarkness(
                        layoutPdfPath,
                        pageNumber,
                        pageLog.FinalBounds,
                        pageWidth,
                        pageHeight);
                pageLog.FinalRejectReason = MaterialPlacementGeometry.EvaluateHardBlock(
                    pageLog.FinalBounds,
                    pageWidth,
                    pageHeight,
                    null,
                    layoutPdfPath,
                    pageNumber);

                if (pageLog.OcrMs < 0)
                    pageLog.OcrMs = MaterialPlacementPerfStats.OcrMs;
                if (pageLog.RasterMs < 0)
                    pageLog.RasterMs = MaterialPlacementPerfStats.RasterMs;

                if (pageLog.FinalRejectReason != MaterialHardBlockReason.None)
                {
                    WatermarkLearningStore.ClearOnPlacementError(
                        "material-final:" + MaterialPlacementGeometry.HardBlockReasonToGerman(pageLog.FinalRejectReason));

                    placements.RemoveAll(p =>
                        p != null && string.Equals(p.Category, "Material", StringComparison.OrdinalIgnoreCase));
                    pageLog.Action = "HardBlock-Omitted";
                    pageLog.FinalBounds = null;
                    pageLog.Darkness = -1f;
                }
            }

            WritePageLog(pageLog, pageWidth, pageHeight);
            MaterialPlacementPerfStats.Reset();
            MaterialPlacementPerfStats.ClearSnapshot();
        }

        public static List<WatermarkTextPlacement> PlaceWithHardBlock(
            IList<string> texts,
            string pdfPath,
            int pageNumber,
            float pageWidth,
            float pageHeight,
            MaterialPlacementPageLog pageLog = null,
            IList<MaterialBlockBounds> forbiddenZones = null)
        {
            return ReliableMaterialPlacementService.Place(
                texts,
                pdfPath,
                pageNumber,
                pageWidth,
                pageHeight,
                Array.Empty<PdfTextRect>(),
                pageLog,
                forbiddenZones);
        }

        internal static void LogCandidateAttempt(
            MaterialPlacementPageLog pageLog,
            string label,
            MaterialBlockBounds bounds,
            MaterialHardBlockReason reason,
            float pageWidth,
            float pageHeight,
            float darkness,
            float score = float.NaN)
        {
            if (pageLog == null)
                return;

            pageLog.CandidateLines.Add(FormatCandidateLine(label, bounds, reason, pageWidth, pageHeight, darkness, score));
        }

        private static List<WatermarkTextPlacement> FilterValidMaterialPlacements(
            IList<WatermarkTextPlacement> placements,
            string pdfPath,
            int pageNumber,
            float pageWidth,
            float pageHeight)
        {
            if (placements == null || placements.Count == 0)
                return new List<WatermarkTextPlacement>();

            var bounds = MaterialBlockBounds.FromMaterialStack(placements);
            if (bounds.BlockWidth <= 0f)
                return new List<WatermarkTextPlacement>();

            var reason = MaterialPlacementGeometry.EvaluateHardBlock(
                bounds, pageWidth, pageHeight, null, pdfPath, pageNumber);

            return reason == MaterialHardBlockReason.None
                ? placements.ToList()
                : new List<WatermarkTextPlacement>();
        }

        private static string FormatCandidateLine(
            string label,
            MaterialBlockBounds bounds,
            MaterialHardBlockReason reason,
            float pageWidth,
            float pageHeight,
            float darkness,
            float score)
        {
            var scoreText = float.IsNaN(score)
                ? "-"
                : score.ToString("0.0", CultureInfo.InvariantCulture);

            return string.Format(
                CultureInfo.InvariantCulture,
                "candidate={0} rel={1} reject={2} rect={3} dark={4:0.000} score={5}",
                label,
                bounds.ToRelString(pageWidth, pageHeight),
                MaterialPlacementGeometry.HardBlockReasonToGerman(reason),
                bounds.ToRectString(),
                darkness,
                scoreText);
        }

        private static float MeasureDarkness(
            string pdfPath,
            int pageNumber,
            MaterialBlockBounds bounds,
            float pageWidth,
            float pageHeight)
        {
            if (!PdfRasterLayoutService.IsAvailable)
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

        private static void WritePageLog(MaterialPlacementPageLog pageLog, float pageWidth, float pageHeight)
        {
            try
            {
                var dir = Path.Combine(AppInfo.UserDataDirectory, "Diagnostics");
                Directory.CreateDirectory(dir);

                var sb = new StringBuilder()
                    .Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"))
                    .Append(" | ")
                    .Append(pageLog.PdfFileName ?? "-")
                    .Append(" | page=")
                    .Append(pageLog.PageNumber)
                    .Append(" | drawing=")
                    .Append(string.IsNullOrWhiteSpace(pageLog.DrawingHint) ? "-" : pageLog.DrawingHint)
                    .Append(" | action=")
                    .Append(pageLog.Action ?? "-");

                if (pageLog.FinalBounds != null && pageLog.FinalBounds.BlockWidth > 0f)
                {
                    sb.Append(" | finalRel=")
                        .Append(pageLog.FinalBounds.ToRelString(pageWidth, pageHeight))
                        .Append(" | finalRect=")
                        .Append(pageLog.FinalBounds.ToRectString())
                        .Append(" | finalReject=")
                        .Append(MaterialPlacementGeometry.HardBlockReasonToGerman(pageLog.FinalRejectReason))
                        .Append(" | dark=")
                        .Append(pageLog.Darkness.ToString("0.000", CultureInfo.InvariantCulture));
                }

                if (!string.IsNullOrWhiteSpace(pageLog.ChosenLabel))
                {
                    sb.Append(" | chosen=")
                        .Append(pageLog.ChosenLabel)
                        .Append(" | selected_by=")
                        .Append(string.IsNullOrWhiteSpace(pageLog.SelectedBy) ? pageLog.ChosenLabel : pageLog.SelectedBy)
                        .Append(" | chosenRel=")
                        .Append(string.IsNullOrWhiteSpace(pageLog.ChosenRel) ? "-" : pageLog.ChosenRel)
                        .Append(" | chosenScore=")
                        .Append(float.IsNaN(pageLog.ChosenScore)
                            ? "-"
                            : pageLog.ChosenScore.ToString("0.0", CultureInfo.InvariantCulture))
                        .Append(" | rejectReason=")
                        .Append(MaterialPlacementGeometry.HardBlockReasonToGerman(pageLog.ChosenRejectReason))
                        .Append(" | peak=")
                        .Append(float.IsNaN(pageLog.ChosenPeak)
                            ? "-"
                            : pageLog.ChosenPeak.ToString("0.000", CultureInfo.InvariantCulture))
                        .Append(" | mean=")
                        .Append(float.IsNaN(pageLog.ChosenMean)
                            ? "-"
                            : pageLog.ChosenMean.ToString("0.000", CultureInfo.InvariantCulture));
                }
                else if (!string.IsNullOrWhiteSpace(pageLog.SelectedBy))
                {
                    sb.Append(" | selected_by=").Append(pageLog.SelectedBy);
                }

                sb.Append(" | placement_ms=")
                    .Append(pageLog.PlacementMs >= 0 ? pageLog.PlacementMs.ToString(CultureInfo.InvariantCulture) : "-")
                    .Append(" | ocr_ms=")
                    .Append(pageLog.OcrMs >= 0 ? pageLog.OcrMs.ToString(CultureInfo.InvariantCulture) : "-")
                    .Append(" | raster_ms=")
                    .Append(pageLog.RasterMs >= 0 ? pageLog.RasterMs.ToString(CultureInfo.InvariantCulture) : "-")
                    .Append(" | candidate_count=")
                    .Append(pageLog.CandidateCount >= 0 ? pageLog.CandidateCount.ToString(CultureInfo.InvariantCulture) : "-");

                sb.AppendLine();

                foreach (var candidate in pageLog.CandidateLines)
                    sb.Append("    ").Append(candidate).AppendLine();

                lock (LogLock)
                {
                    File.AppendAllText(Path.Combine(dir, "material-placement.log"), sb.ToString(), Encoding.UTF8);
                }
            }
            catch
            {
            }
        }
    }
}
