using System;

namespace PDF_umbenenen.Services
{
    internal sealed class MaterialPlacementPerfSnapshot
    {
        public long PlacementMs { get; set; } = -1;
        public long OcrMs { get; set; } = -1;
        public long RasterMs { get; set; } = -1;
        public int CandidateCount { get; set; } = -1;
        public string SelectedBy { get; set; } = string.Empty;
        public string ChosenLabel { get; set; } = string.Empty;
        public float ChosenPeak { get; set; } = float.NaN;
        public float ChosenMean { get; set; } = float.NaN;
    }

    internal static class MaterialPlacementPerfStats
    {
        public const int PlacementBudgetMs = 700;

        [ThreadStatic]
        private static long _ocrMs;

        [ThreadStatic]
        private static long _rasterMs;

        [ThreadStatic]
        private static DateTime? _deadlineUtc;

        [ThreadStatic]
        private static MaterialPlacementPerfSnapshot _lastSnapshot;

        public static void BeginPage()
        {
            _ocrMs = 0;
            _rasterMs = 0;
            _deadlineUtc = null;
            _lastSnapshot = null;
        }

        public static void BeginPlacementBudget()
        {
            if (!_deadlineUtc.HasValue)
                _deadlineUtc = DateTime.UtcNow.AddMilliseconds(PlacementBudgetMs);
        }

        public static void Reset()
        {
            _ocrMs = 0;
            _rasterMs = 0;
            _deadlineUtc = null;
        }

        public static void CaptureSnapshot(
            long placementMs,
            int candidateCount,
            string selectedBy,
            string chosenLabel,
            float chosenPeak,
            float chosenMean)
        {
            _lastSnapshot = new MaterialPlacementPerfSnapshot
            {
                PlacementMs = placementMs,
                OcrMs = _ocrMs,
                RasterMs = _rasterMs,
                CandidateCount = candidateCount,
                SelectedBy = selectedBy ?? string.Empty,
                ChosenLabel = chosenLabel ?? string.Empty,
                ChosenPeak = chosenPeak,
                ChosenMean = chosenMean
            };
        }

        public static void ApplySnapshot(MaterialPlacementPageLog pageLog)
        {
            if (pageLog == null || _lastSnapshot == null)
                return;

            if (pageLog.PlacementMs < 0)
                pageLog.PlacementMs = _lastSnapshot.PlacementMs;
            if (pageLog.OcrMs < 0)
                pageLog.OcrMs = _lastSnapshot.OcrMs;
            if (pageLog.RasterMs < 0)
                pageLog.RasterMs = _lastSnapshot.RasterMs;
            if (pageLog.CandidateCount < 0)
                pageLog.CandidateCount = _lastSnapshot.CandidateCount;
            if (string.IsNullOrWhiteSpace(pageLog.SelectedBy))
                pageLog.SelectedBy = _lastSnapshot.SelectedBy;
            if (string.IsNullOrWhiteSpace(pageLog.ChosenLabel))
                pageLog.ChosenLabel = _lastSnapshot.ChosenLabel;
            if (float.IsNaN(pageLog.ChosenPeak))
                pageLog.ChosenPeak = _lastSnapshot.ChosenPeak;
            if (float.IsNaN(pageLog.ChosenMean))
                pageLog.ChosenMean = _lastSnapshot.ChosenMean;
        }

        public static void ClearSnapshot()
        {
            _lastSnapshot = null;
        }

        public static void AddOcrMs(long ms)
        {
            if (ms > 0)
                _ocrMs += ms;
        }

        public static void AddRasterMs(long ms)
        {
            if (ms > 0)
                _rasterMs += ms;
        }

        public static long OcrMs => _ocrMs;

        public static long RasterMs => _rasterMs;

        public static bool IsBudgetExceeded()
        {
            return _deadlineUtc.HasValue && DateTime.UtcNow >= _deadlineUtc.Value;
        }
    }
}
