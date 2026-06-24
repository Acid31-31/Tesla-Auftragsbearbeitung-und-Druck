using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using PDF_umbenenen.Services.Ai;

namespace PDF_umbenenen.Services
{
    /// <summary>
    /// Schnelle Regel-Erkennung mit optionalem Raster; Ollama nur als letzter Ausweg.
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
            string pdfPath,
            int pageNumber,
            string materialRaw)
        {
            if (candidates == null)
                candidates = new List<ThicknessCandidate>();

            var ruleMm = ThicknessCandidateResolver.ResolveBestMm(candidates, materialRaw);
            var rasterMm = FindRasterCandidateMm(candidates);

            if (ruleMm.HasValue
                && ThicknessCandidateResolver.HasExplicitSourceForValue(candidates, ruleMm.Value))
            {
                return ruleMm;
            }

            var merged = PreferRasterWhenRulesSuspicious(ruleMm, rasterMm, candidates, materialRaw);
            if (merged.HasValue)
                return merged;

            var settings = AiSettingsStore.Load();
            if (!settings.MaterialExtractionViaOllama)
                return null;

            var ollama = OllamaMaterialThicknessService.TryExtract(pdfPath, pageNumber);
            if (ollama == null || ollama.IsAssembly)
                return null;

            var ollamaMm = ParseThicknessMm(ollama.ThicknessText);
            if (!ollamaMm.HasValue || !IsPlausibleThicknessMm(ollamaMm.Value))
                return null;

            if (ollama.Confidence > 0d && ollama.Confidence < 0.5d)
                return null;

            return ThicknessCandidateResolver.StandardGaugeDistance(ollamaMm.Value) <= 0.35d
                ? ThicknessCandidateResolver.SnapToStandardGauge(ollamaMm.Value)
                : ollamaMm;
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

        private static double? ParseThicknessMm(string thicknessText)
        {
            if (string.IsNullOrWhiteSpace(thicknessText))
                return null;

            var text = System.Text.RegularExpressions.Regex.Replace(
                thicknessText,
                @"mm",
                string.Empty,
                System.Text.RegularExpressions.RegexOptions.IgnoreCase).Trim();

            if (double.TryParse(text, NumberStyles.Any, CultureInfo.GetCultureInfo("de-DE"), out var mm))
                return mm;

            if (double.TryParse(text, NumberStyles.Any, CultureInfo.InvariantCulture, out mm))
                return mm;

            return null;
        }

        private static bool IsPlausibleThicknessMm(double mm)
        {
            return mm >= 0.3d && mm <= ThicknessCandidateResolver.MaxSideViewThicknessMm;
        }
    }
}
