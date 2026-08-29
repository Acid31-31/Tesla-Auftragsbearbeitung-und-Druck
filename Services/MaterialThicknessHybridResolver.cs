using System;
using System.Collections.Generic;
using System.Linq;

namespace PDF_umbenenen.Services
{
    /// <summary>
    /// Schnelle Regel-Erkennung mit optionalem Raster (nur außerhalb des Stapelmodus).
    /// </summary>
    internal static class MaterialThicknessHybridResolver
    {
        public static bool NeedsRasterVerification(
            IList<ThicknessCandidate> candidates,
            string materialRaw,
            double? textOnlyMm)
        {
            if (ThicknessCandidateResolver.HasConfidentExplicitThickness(candidates, materialRaw))
                return false;

            if (!textOnlyMm.HasValue)
                return true;

            if (ThicknessCandidateResolver.HasExplicitSourceForValue(candidates, textOnlyMm.Value))
                return false;

            if (IsRuleResultSuspicious(textOnlyMm.Value, candidates, materialRaw))
                return true;

            if (textOnlyMm.Value <= ThicknessCandidateResolver.MaxAutoDetectedSheetMm
                && HasPlausiblePlateCandidate(candidates))
            {
                return true;
            }

            return HasTextConflicts(candidates, materialRaw);
        }

        private static bool HasPlausiblePlateCandidate(IList<ThicknessCandidate> candidates)
        {
            if (candidates == null)
                return false;

            return candidates.Any(c =>
                ThicknessCandidateResolver.IsPlausiblePlateThicknessMm(c.ValueMm)
                && !ThicknessCandidateResolver.IsLikelyHoleDiameterMm(c.ValueMm));
        }

        private static bool HasTextConflicts(IList<ThicknessCandidate> candidates, string materialRaw)
        {
            if (candidates == null || candidates.Count < 2)
                return false;

            var distinct = candidates
                .Where(c => !c.SuspectWatermark)
                .Where(c => !ThicknessCandidateResolver.IsMaterialNumberThicknessFalsePositive(
                    c.ValueMm, materialRaw, c.Source, c.Source))
                .Where(c => !ThicknessCandidateResolver.IsLikelyHoleDiameterMm(c.ValueMm))
                .Select(c => ThicknessCandidateResolver.SnapToStandardGauge(c.ValueMm))
                .Distinct()
                .Take(5)
                .ToList();

            return distinct.Count >= 2 && distinct.Max() - distinct.Min() > 2d;
        }

        public static double? ResolveFinalMm(
            IList<ThicknessCandidate> candidates,
            string materialRaw)
        {
            if (candidates == null)
                candidates = new List<ThicknessCandidate>();

            var ruleMm = ThicknessCandidateResolver.ResolveBestMm(candidates, materialRaw);
            var rasterMm = FindRasterCandidateMm(candidates);

            if (ruleMm.HasValue
                && ThicknessCandidateResolver.HasExplicitSourceForValue(candidates, ruleMm.Value))
            {
                return ThicknessCandidateResolver.CorrectOversizedPlateGuess(ruleMm, candidates, materialRaw);
            }

            var merged = PreferRasterWhenRulesSuspicious(ruleMm, rasterMm, candidates, materialRaw);
            var result = merged ?? ruleMm;
            return ThicknessCandidateResolver.CorrectOversizedPlateGuess(result, candidates, materialRaw);
        }

        internal static void AddRasterSideProfileCandidate(
            ICollection<ThicknessCandidate> candidates,
            string pdfPath,
            int pageNumber,
            float pageWidthPt,
            float pageHeightPt,
            int scaleDenominator)
        {
            if (candidates == null
                || !PdfRasterLayoutService.IsAvailable
                || string.IsNullOrWhiteSpace(pdfPath))
            {
                return;
            }

            if (!PdfRasterLayoutService.TryDetectSideProfileThicknessMm(
                    pdfPath,
                    pageNumber,
                    pageWidthPt,
                    pageHeightPt,
                    scaleDenominator,
                    out var rasterMm)
                || rasterMm < 0.35f
                || rasterMm > ThicknessCandidateResolver.MaxSideViewThicknessMm + 2f)
            {
                return;
            }

            var value = (double)rasterMm;
            if (ThicknessCandidateResolver.StandardGaugeDistance(value) <= 0.35d)
                value = ThicknessCandidateResolver.SnapToStandardGauge(value);

            ThicknessCandidateResolver.AddCandidate(
                candidates,
                value,
                ThicknessCandidateResolver.PriorityRasterSideProfile,
                "raster-seitenprofil");
        }

        private static double? FindRasterCandidateMm(IList<ThicknessCandidate> candidates)
        {
            if (candidates == null || candidates.Count == 0)
                return null;

            var raster = candidates
                .Where(c => string.Equals(c.Source, "raster-seitenprofil", StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(c => c.Priority)
                .FirstOrDefault();

            return raster != null ? raster.ValueMm : (double?)null;
        }

        private static double? PreferRasterWhenRulesSuspicious(
            double? ruleMm,
            double? rasterMm,
            IList<ThicknessCandidate> candidates,
            string materialRaw)
        {
            if (!rasterMm.HasValue)
                return ruleMm;

            if (!ruleMm.HasValue)
                return rasterMm;

            if (Math.Abs(ruleMm.Value - rasterMm.Value) < 0.45d)
                return ruleMm;

            if (ThicknessCandidateResolver.HasExplicitSourceForValue(candidates, ruleMm.Value))
                return ruleMm;

            // Explizite Blechdicke/t=… schlägt Raster/Seitenansicht immer.
            if (ThicknessCandidateResolver.HasConfidentExplicitThickness(candidates, materialRaw))
            {
                var explicitBest = ThicknessCandidateResolver.ResolveBestMm(
                    candidates
                        .Where(c => ThicknessCandidateResolver.IsExplicitThicknessSource(c.Source)
                                    || string.Equals(c.Source, "schriftfeld-label", StringComparison.OrdinalIgnoreCase)
                                    || string.Equals(c.Source, "schriftfeld-thk", StringComparison.OrdinalIgnoreCase))
                        .ToList(),
                    materialRaw);
                if (explicitBest.HasValue)
                    return explicitBest;
            }

            if (IsRuleResultSuspicious(ruleMm.Value, candidates, materialRaw)
                && ThicknessCandidateResolver.StandardGaugeDistance(rasterMm.Value) <= 0.35d)
            {
                return rasterMm;
            }

            if (ruleMm.Value <= ThicknessCandidateResolver.MaxAutoDetectedSheetMm
                && rasterMm.Value > ThicknessCandidateResolver.MaxAutoDetectedSheetMm
                && ThicknessCandidateResolver.IsPlausiblePlateThicknessMm(rasterMm.Value)
                && IsRuleResultSuspicious(ruleMm.Value, candidates, materialRaw))
            {
                return rasterMm;
            }

            return ruleMm;
        }

        internal static bool IsRuleResultSuspicious(double ruleMm, IList<ThicknessCandidate> candidates, string materialRaw)
        {
            if (ThicknessCandidateResolver.IsLikelyHoleDiameterMm(ruleMm))
                return true;

            if (ThicknessCandidateResolver.IsWholeNumber(ruleMm)
                && ruleMm >= 4d
                && ruleMm <= 25d
                && candidates != null
                && candidates.Any(c =>
                    c.ValueMm >= 0.8d
                    && c.ValueMm <= 6.5d
                    && (ThicknessCandidateResolver.IsExplicitThicknessSource(c.Source)
                        || string.Equals(c.Source, "schriftfeld-label", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(c.Source, "schriftfeld-thk", StringComparison.OrdinalIgnoreCase)
                        || (ThicknessCandidateResolver.IsSideViewSource(c.Source)
                            && ThicknessCandidateResolver.IsTypicalSheetThickness(c.ValueMm)))))
            {
                return true;
            }

            if (ThicknessCandidateResolver.IsWholeNumber(ruleMm)
                && ruleMm >= 4d
                && ruleMm <= 10d
                && candidates != null
                && candidates.Any(c =>
                    ThicknessCandidateResolver.HasFractionalPart(c.ValueMm)
                    && c.ValueMm >= 0.8d
                    && c.ValueMm <= 3.5d
                    && ThicknessCandidateResolver.IsSideViewSource(c.Source)))
            {
                return true;
            }

            if (ThicknessCandidateResolver.IsSuspiciousThinGauge(ruleMm)
                && !string.IsNullOrWhiteSpace(materialRaw)
                && (materialRaw.IndexOf("1.0038", StringComparison.OrdinalIgnoreCase) >= 0
                    || materialRaw.IndexOf("1.4301", StringComparison.OrdinalIgnoreCase) >= 0
                    || materialRaw.IndexOf("1.4404", StringComparison.OrdinalIgnoreCase) >= 0))
            {
                return true;
            }

            var winner = candidates?
                .FirstOrDefault(c => Math.Abs(c.ValueMm - ruleMm) < 0.08d);

            if (winner != null
                && ThicknessCandidateResolver.IsMaterialNumberThicknessFalsePositive(
                    winner.ValueMm, materialRaw, winner.Source, winner.Source))
            {
                return true;
            }

            return false;
        }
    }
}
