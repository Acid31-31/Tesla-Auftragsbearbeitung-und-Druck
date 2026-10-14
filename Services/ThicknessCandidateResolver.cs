using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace PDF_umbenenen.Services
{
    internal sealed class ThicknessCandidate
    {
        public double ValueMm { get; set; }
        public int Priority { get; set; }
        public string Source { get; set; }
        public bool SuspectWatermark { get; set; }
    }

    /// <summary>
    /// Wählt die plausibelste Blechstärke aus Beschriftung, Schriftfeld und Seitenansicht.
    /// Gilt für jede Zeichnung – unabhängig von Bestellung oder Zeichnungsnummer.
    /// </summary>
    internal static class ThicknessCandidateResolver
    {
        private static readonly double[] StandardGaugesMm =
        {
            0.5, 0.8, 1, 1.25, 1.5, 2, 2.5, 3, 4, 5, 6, 8, 10, 12, 15, 20
        };

        public const double MaxAutoDetectedSheetMm = 6d;

        public const double MaxSideViewThicknessMm = 25d;

        public const int PriorityExplicitText = 100;
        public const int PriorityThreeViewText = 96;
        public const int PriorityRasterSideProfile = 94;
        public const int PrioritySideViewText = 92;
        public const int PriorityThreeViewRaster = 82;
        public const int PriorityTitleBlock = 78;
        public const int PriorityRasterFallback = 55;

        public static double StandardGaugeDistance(double mm)
        {
            if (mm <= 0)
                return double.MaxValue;

            return StandardGaugesMm.Min(g => Math.Abs(mm - g));
        }

        public static bool IsStandardSheetGauge(double mm, double maxDistance = 0.18d)
        {
            return StandardGaugeDistance(mm) <= maxDistance;
        }

        public static bool IsTypicalSheetThickness(double mm)
        {
            return mm >= 0.4d && mm <= MaxAutoDetectedSheetMm;
        }

        public static bool IsLikelyStandardHoleDiameter(double mm)
        {
            var roundedHalf = Math.Round(mm * 2d) / 2d;
            if (Math.Abs(mm - roundedHalf) > 0.06d)
                return false;

            var whole = (int)Math.Floor(roundedHalf);
            var fraction = roundedHalf - whole;
            return fraction >= 0.45d && fraction <= 0.55d && whole >= 3 && whole <= 25;
        }

        /// <summary>Typische Bohrungsdurchmesser wie 4,2 / 5,8 / 6,6 / 7,7 / 13,7 – keine Blechstärke.</summary>
        public static bool IsLikelyHoleDiameterMm(double mm)
        {
            if (mm < 3d || mm > 26d)
                return false;

            if (IsLikelyStandardHoleDiameter(mm))
                return true;

            if (HasFractionalPart(mm) && !IsStandardSheetGauge(mm, 0.08d))
                return true;

            return false;
        }

        public static bool IsLikelyHoleOrFeatureDimension(string rawText, double mm)
        {
            if (!string.IsNullOrWhiteSpace(rawText))
            {
                var upper = rawText.ToUpperInvariant();
                if (upper.Contains("Ø")
                    || upper.Contains("⌀")
                    || upper.Contains("DIA")
                    || upper.Contains("PHI")
                    || upper.Contains("R ")
                    || upper.StartsWith("R", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                if (System.Text.RegularExpressions.Regex.IsMatch(upper, @"\(\s*\d")
                    || System.Text.RegularExpressions.Regex.IsMatch(upper, @"\d\s*\)")
                    || System.Text.RegularExpressions.Regex.IsMatch(upper, @"\(\s*\d+\s*[xX]")
                    || System.Text.RegularExpressions.Regex.IsMatch(upper, @"\d+\s*[xX]\s*\)"))
                {
                    return true;
                }

                if (System.Text.RegularExpressions.Regex.IsMatch(upper, @"\d\s*[xX]\s*\d+\s*°"))
                {
                    return true;
                }

                // Bohrungsangaben „2 x Ø5,5“ / „4 x M5“ – keine Blechstärke
                if (System.Text.RegularExpressions.Regex.IsMatch(upper, @"\d+\s*[xX]\s*[Ø⌀O0]")
                    || System.Text.RegularExpressions.Regex.IsMatch(upper, @"\d+\s*[xX]\s*M\d")
                    || System.Text.RegularExpressions.Regex.IsMatch(upper, @"\d+\s*[xX]\s*R\d"))
                {
                    return true;
                }
            }

            if (IsLikelyStandardHoleDiameter(mm))
                return true;

            if (IsLikelyHoleDiameterMm(mm))
                return true;

            return false;
        }

        /// <summary>Segmente aus Zeichnungsnummern (z. B. RT1 10 00 0001) nicht als Blechstärke werten.</summary>
        public static bool IsLikelyDrawingNumberNoise(string rawText)
        {
            if (string.IsNullOrWhiteSpace(rawText))
                return false;

            var text = rawText.Trim();
            if (PDF_umbenenen.Models.OrderVerificationDrawingKey.IsLikelyDrawingToken(text))
                return true;

            if (System.Text.RegularExpressions.Regex.IsMatch(
                    text,
                    @"\bRT\d+(?:[\s\-_/]+\d{1,5}){2,}\b",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase))
                return true;

            if (System.Text.RegularExpressions.Regex.IsMatch(
                    text,
                    @"^RT\d+\s*$",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase))
                return true;

            return false;
        }

        public static bool IsLikelyMaterialNumberFragment(string rawText, double mm)
        {
            if (IsLikelyDrawingNumberNoise(rawText))
                return true;

            if (IsSuspiciousThinGauge(mm))
            {
                if (string.IsNullOrWhiteSpace(rawText))
                    return false;

                var text = rawText.Trim();
                if (System.Text.RegularExpressions.Regex.IsMatch(text, @"^\s*1\s*$"))
                    return true;

                if (System.Text.RegularExpressions.Regex.IsMatch(text, @"^1[.,]0\s*(?:mm|MM)?$"))
                    return true;

                if (System.Text.RegularExpressions.Regex.IsMatch(text, @"\b1\.\d{4}\b"))
                    return true;

                if (text.IndexOf("1.0038", StringComparison.OrdinalIgnoreCase) >= 0
                    || text.IndexOf("1.4301", StringComparison.OrdinalIgnoreCase) >= 0
                    || System.Text.RegularExpressions.Regex.IsMatch(text, @"\b1015\b"))
                {
                    return true;
                }
            }

            if (string.IsNullOrWhiteSpace(rawText))
                return false;

            var normalized = rawText.Trim();
            if (System.Text.RegularExpressions.Regex.IsMatch(normalized, @"\b1\.\d{4}\b") && mm >= 0.8d && mm <= 1.2d)
                return true;

            if (System.Text.RegularExpressions.Regex.IsMatch(normalized, @"\b3\.\d{4}\b") && mm >= 2.8d && mm <= 3.2d)
                return true;

            if (normalized.IndexOf("1.0038", StringComparison.OrdinalIgnoreCase) >= 0 && mm <= 2d)
                return true;

            if (normalized.IndexOf("1.4301", StringComparison.OrdinalIgnoreCase) >= 0 && mm <= 2d)
                return true;

            if (System.Text.RegularExpressions.Regex.IsMatch(normalized, @"\b1015\b") && Math.Abs(mm - 1d) < 0.25d)
                return true;

            return false;
        }

        public static bool IsSuspiciousThinGauge(double mm)
        {
            return mm >= 0.7d && mm <= 1.35d;
        }

        public static bool IsMaterialNumberThicknessFalsePositive(
            double mm,
            string materialRaw,
            string rawText = null,
            string source = null)
        {
            if (!IsSuspiciousThinGauge(mm))
                return false;

            if (IsSideViewSource(source)
                || string.Equals(source, "3-ansichten-bemassung", StringComparison.OrdinalIgnoreCase)
                || string.Equals(source, "seitenansicht-zone", StringComparison.OrdinalIgnoreCase))
            {
                return IsLikelyMaterialNumberFragment(rawText ?? string.Empty, mm);
            }

            if (IsLikelyMaterialNumberFragment(rawText ?? string.Empty, mm))
                return true;

            if (string.IsNullOrWhiteSpace(materialRaw))
                return false;

            var material = materialRaw.Trim();
            if ((material.IndexOf("1.0038", StringComparison.OrdinalIgnoreCase) >= 0
                 || material.IndexOf("1.4301", StringComparison.OrdinalIgnoreCase) >= 0
                 || material.IndexOf("1.4404", StringComparison.OrdinalIgnoreCase) >= 0
                 || System.Text.RegularExpressions.Regex.IsMatch(material, @"\b1015\b"))
                && System.Text.RegularExpressions.Regex.IsMatch((rawText ?? string.Empty).Trim(), @"^1[.,]0\s*(?:mm|MM)?$"))
            {
                return true;
            }

            return false;
        }

        public static bool IsPlausiblePlateThicknessMm(double mm)
        {
            return mm > MaxAutoDetectedSheetMm
                && mm <= MaxSideViewThicknessMm
                && IsWholeNumber(mm)
                && IsStandardSheetGauge(mm, 0.15d);
        }

        public static double? ResolveConflictingSideViewThicknessMm(
            IList<(double Value, float Score)> thinCandidates,
            IList<(double Value, float Score)> thickCandidates)
        {
            if (thickCandidates == null || thickCandidates.Count == 0)
            {
                if (thinCandidates == null || thinCandidates.Count == 0)
                    return null;

                return PickBestThinSideViewThicknessMm(thinCandidates);
            }

            if (thinCandidates == null || thinCandidates.Count == 0)
                return thickCandidates.OrderBy(c => c.Score).First().Value;

            var thin = PickBestThinSideViewThicknessMm(thinCandidates, out var thinScore);

            var thickFiltered = thickCandidates
                .Where(c => !IsLikelyHoleDiameterMm(c.Value))
                .OrderBy(c => c.Score)
                .ThenByDescending(c => c.Value)
                .ToList();

            if (thickCandidates.Any(c => IsLikelyHoleDiameterMm(c.Value)) && thinCandidates.Count > 0)
                return thin;

            if (thickFiltered.Count == 0)
                return thin;

            var thick = thickFiltered[0].Value;
            var thickScore = thickFiltered[0].Score;

            if (IsSuspiciousThinGauge(thin))
                return thick;

            // Winkel/Abkantung: 2–6 mm ist die Blechstärke, 8–20 mm das Schenkelmaß.
            // Platte ohne Dünnmaß: nur 15 mm in der Seitenansicht bleibt 15 mm.
            if (IsTypicalSheetThickness(thin)
                && IsStandardSheetGauge(thin, 0.15d)
                && thick > MaxAutoDetectedSheetMm)
            {
                if (IsSuspiciousThinGauge(thin)
                    && thickScore <= thinScore - 45f
                    && IsPlausiblePlateThicknessMm(thick))
                {
                    return thick;
                }

                return thin;
            }

            if (IsPlausiblePlateThicknessMm(thick) && thickScore <= thinScore - 40f)
                return thick;

            if (IsTypicalSheetThickness(thin)
                && thin <= 4d
                && thick > MaxAutoDetectedSheetMm
                && !IsPlausiblePlateThicknessMm(thick)
                && thickScore > thinScore + 25f)
            {
                return thin;
            }

            if (HasFractionalPart(thin)
                && thin <= 3.5d
                && IsStandardSheetGauge(thin, 0.12d)
                && IsWholeNumber(thick)
                && thick >= 4d
                && thickScore > thinScore + 15f)
            {
                return thin;
            }

            return thickScore <= thinScore - 20f ? thick : thin;
        }

        private static double PickBestThinSideViewThicknessMm(
            IList<(double Value, float Score)> thinCandidates,
            out float bestScore)
        {
            var value = PickBestThinSideViewThicknessMm(thinCandidates);
            bestScore = thinCandidates
                .Where(c => Math.Abs(c.Value - value) < 0.05d)
                .Select(c => c.Score)
                .DefaultIfEmpty(0f)
                .Min();
            return value;
        }

        private static double PickBestThinSideViewThicknessMm(IList<(double Value, float Score)> thinCandidates)
        {
            var usable = thinCandidates
                .Where(c => !IsLikelyHoleDiameterMm(c.Value))
                .ToList();
            if (usable.Count == 0)
                usable = thinCandidates.ToList();

            var thinFractional = usable
                .Where(c => HasFractionalPart(c.Value)
                            && c.Value >= 0.8d
                            && c.Value <= 3.5d
                            && IsStandardSheetGauge(c.Value, 0.12d))
                .OrderBy(c => c.Score)
                .ThenBy(c => StandardGaugeDistance(c.Value))
                .ToList();
            if (thinFractional.Count > 0)
                return thinFractional[0].Value;

            var standardGauge = usable
                .Where(c => IsStandardSheetGauge(c.Value, 0.12d) && c.Value <= MaxAutoDetectedSheetMm)
                .GroupBy(c => Math.Round(c.Value, 1))
                .Select(g => (Value: g.Key, Score: g.Min(x => x.Score), Count: g.Count()))
                .OrderBy(c => c.Score)
                .ThenByDescending(c => c.Count)
                .ThenBy(c => StandardGaugeDistance(c.Value))
                .ToList();
            if (standardGauge.Count > 0)
                return standardGauge[0].Value;

            var fractionalGauge = usable
                .Where(c => HasFractionalPart(c.Value) && IsStandardSheetGauge(c.Value, 0.05d))
                .OrderBy(c => c.Score)
                .ThenBy(c => StandardGaugeDistance(c.Value))
                .ToList();
            if (fractionalGauge.Count > 0)
                return fractionalGauge[0].Value;

            return usable
                .OrderBy(c => c.Score)
                .ThenBy(c => StandardGaugeDistance(c.Value))
                .First()
                .Value;
        }

        public static double? ResolveSideViewThicknessMm(IList<(double Value, float Score)> scoredCandidates)
        {
            if (scoredCandidates == null || scoredCandidates.Count == 0)
                return null;

            var good = scoredCandidates.ToList();

            var thin = good
                .Where(c => c.Value >= 0.3d && c.Value <= MaxAutoDetectedSheetMm)
                .ToList();

            var thick = good
                .Where(c => c.Value > MaxAutoDetectedSheetMm && c.Value <= MaxSideViewThicknessMm)
                .ToList();

            var resolved = ResolveConflictingSideViewThicknessMm(thin, thick);
            if (resolved.HasValue)
                return resolved;

            return good.OrderBy(c => c.Score).First().Value;
        }

        public static double SnapToStandardGauge(double mm)
        {
            if (mm <= 0)
                return mm;

            return StandardGaugesMm.OrderBy(g => Math.Abs(mm - g)).First();
        }

        public static void AddCandidate(ICollection<ThicknessCandidate> list, double valueMm, int priority, string source, bool suspectWatermark = false)
        {
            if (valueMm <= 0 || valueMm > 40)
                return;

            if (!IsExplicitThicknessSource(source)
                && !IsSideViewSource(source)
                && IsWholeNumber(valueMm)
                && valueMm >= 8d)
            {
                suspectWatermark = true;
            }

            list.Add(new ThicknessCandidate
            {
                ValueMm = valueMm,
                Priority = priority,
                Source = source ?? string.Empty,
                SuspectWatermark = suspectWatermark
            });
        }

        public static void AddCandidate(ICollection<ThicknessCandidate> list, string raw, int priority, string source, bool suspectWatermark = false)
        {
            if (string.IsNullOrWhiteSpace(raw))
                return;

            var text = System.Text.RegularExpressions.Regex.Replace(raw.Trim(), @"mm", string.Empty, System.Text.RegularExpressions.RegexOptions.IgnoreCase).Trim();
            if (!TryParseThicknessMm(text, out var value))
                return;

            AddCandidate(list, value, priority, source, suspectWatermark);
        }

        /// <summary>
        /// Parst Blechdicken. CAD nutzt oft "1.5" – unter de-DE darf der Punkt kein Tausender sein (sonst 15).
        /// </summary>
        public static bool TryParseThicknessMm(string text, out double value)
        {
            value = 0d;
            if (string.IsNullOrWhiteSpace(text))
                return false;

            var cleaned = text.Trim();
            if (System.Text.RegularExpressions.Regex.IsMatch(cleaned, @"^\d+[.,]\d{1,3}$"))
            {
                var invariant = cleaned.Replace(',', '.');
                return double.TryParse(invariant, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out value);
            }

            if (System.Text.RegularExpressions.Regex.IsMatch(cleaned, @"^\d{1,3}$"))
                return double.TryParse(cleaned, NumberStyles.None, CultureInfo.InvariantCulture, out value);

            var normalized = cleaned.Replace(',', '.');
            return double.TryParse(normalized, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out value);
        }

        public static double? ResolveBestMm(IList<ThicknessCandidate> candidates, string materialRaw = null)
        {
            if (candidates == null || candidates.Count == 0)
                return null;

            var usable = candidates
                .Where(c => !c.SuspectWatermark)
                .Where(c => !IsMaterialNumberThicknessFalsePositive(c.ValueMm, materialRaw, c.Source, c.Source))
                .ToList();
            if (usable.Count == 0)
            {
                usable = candidates
                    .Where(c => !IsMaterialNumberThicknessFalsePositive(c.ValueMm, materialRaw, c.Source, c.Source))
                    .ToList();
            }
            if (usable.Count == 0)
                usable = candidates.ToList();

            var explicitLabeled = usable
                .Where(c => IsExplicitThicknessSource(c.Source))
                .Where(c => !IsMaterialNumberThicknessFalsePositive(c.ValueMm, materialRaw, c.Source, c.Source))
                .Where(c => !IsLikelyHoleDiameterMm(c.ValueMm))
                .ToList();

            var sideViewEarly = usable
                .Where(c => IsSideViewSource(c.Source)
                            || c.Priority == PriorityThreeViewText
                            || c.Priority == PriorityRasterSideProfile
                            || c.Priority == PrioritySideViewText)
                .Where(c => c.ValueMm >= 0.8d && c.ValueMm <= MaxAutoDetectedSheetMm)
                .Where(c => IsStandardSheetGauge(c.ValueMm, 0.15d))
                .ToList();

            // Nur altes Wasserzeichen (8 mm) darf der Seitenansicht weichen – nicht THK/Blechdicke 8 mm.
            if (sideViewEarly.Count > 0 && explicitLabeled.Count > 0)
            {
                var bestSide = PickBestQuality(sideViewEarly);
                var bestExplicit = PickBestQuality(explicitLabeled);

                if (bestExplicit.SuspectWatermark
                    && bestExplicit.ValueMm > MaxAutoDetectedSheetMm
                    && bestSide.ValueMm <= MaxAutoDetectedSheetMm)
                {
                    return bestSide.ValueMm;
                }
            }

            if (explicitLabeled.Count > 0)
            {
                var bestExplicit = PickBestQuality(explicitLabeled);
                if (!IsSuspiciousThinGauge(bestExplicit.ValueMm)
                    || IsPlausiblePlateThicknessMm(bestExplicit.ValueMm))
                {
                    return bestExplicit.ValueMm;
                }

                if (!IsMaterialNumberThicknessFalsePositive(
                        bestExplicit.ValueMm, materialRaw, bestExplicit.Source, bestExplicit.Source))
                {
                    return bestExplicit.ValueMm;
                }
            }

            var sideView = usable
                .Where(c => IsSideViewSource(c.Source)
                            || c.Priority == PriorityThreeViewText
                            || c.Priority == PriorityRasterSideProfile
                            || c.Priority == PrioritySideViewText)
                .ToList();

            if (sideView.Count > 0)
            {
                var thinSide = sideView
                    .Where(c => c.ValueMm >= 0.3d && c.ValueMm <= MaxAutoDetectedSheetMm)
                    .Select(c => (c.ValueMm, (float)-ComputeQualityScore(c)))
                    .ToList();
                var thickSide = sideView
                    .Where(c => c.ValueMm > MaxAutoDetectedSheetMm && c.ValueMm <= MaxSideViewThicknessMm)
                    .Select(c => (c.ValueMm, (float)-ComputeQualityScore(c)))
                    .ToList();

                var sideResolved = ResolveConflictingSideViewThicknessMm(thinSide, thickSide);
                if (sideResolved.HasValue)
                    return sideResolved;

                return PickBestQuality(sideView).ValueMm;
            }

            var titleBlock = usable.Where(c => c.Priority >= PriorityTitleBlock).ToList();
            if (titleBlock.Count > 0)
            {
                var typicalTitle = titleBlock.Where(c => IsTypicalSheetThickness(c.ValueMm)).ToList();
                if (typicalTitle.Count > 0)
                    return PickBestQuality(typicalTitle).ValueMm;
            }

            var typicalAny = usable.Where(c => IsTypicalSheetThickness(c.ValueMm)).ToList();
            if (typicalAny.Count > 0)
                return PickBestQuality(typicalAny).ValueMm;

            return PickBestQuality(usable).ValueMm;
        }

        /// <summary>
        /// Korrigiert z. B. 20 mm aus Seitenansicht-Höhe, wenn Schriftfeld/OCR 5 mm liefert.
        /// Auch 15 statt 1,5 (Komma verloren) und 6 statt 1,5 (falsche Seitenansicht).
        /// </summary>
        public static double? CorrectOversizedPlateGuess(
            double? resultMm,
            IList<ThicknessCandidate> candidates,
            string materialRaw)
        {
            if (!resultMm.HasValue || candidates == null || candidates.Count == 0)
                return resultMm;

            var value = resultMm.Value;
            var valueFromSideView = candidates.Any(c =>
                Math.Abs(c.ValueMm - value) < 0.08d && IsSideViewSource(c.Source));
            var trustedThin = candidates
                .Where(c => !c.SuspectWatermark)
                .Where(c => c.ValueMm >= 0.8d && c.ValueMm <= 6.5d)
                .Where(c => !IsLikelyHoleDiameterMm(c.ValueMm))
                .Where(c => !IsMaterialNumberThicknessFalsePositive(c.ValueMm, materialRaw, c.Source, c.Source))
                .Where(c =>
                    IsExplicitThicknessSource(c.Source)
                    || string.Equals(c.Source, "schriftfeld-label", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(c.Source, "schriftfeld-thk", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(c.Source, "ocr-schriftfeld", StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(ComputeQualityScore)
                .ThenByDescending(c => c.Priority)
                .ToList();

            if (trustedThin.Count > 0)
            {
                var thin = trustedThin[0].ValueMm;

                // 15 statt 1,5 / 25 statt 2,5: Komma in OCR/Text verloren.
                if (IsWholeNumber(value)
                    && value >= 10d
                    && value <= 30d
                    && HasFractionalPart(thin)
                    && Math.Abs((thin * 10d) - value) < 0.15d)
                {
                    return thin;
                }

                // Explizite Blechdicke im Schriftfeld schlägt Seitenansicht.
                // Nicht: Rahmen-„2“ oder OCR-2 gegen echte 5/15 mm rechts.
                if (Math.Abs(value - thin) > 0.35d
                    && (value >= 4d || !HasExplicitSourceForValue(candidates, value)))
                {
                    var thinSource = trustedThin[0].Source ?? string.Empty;
                    var thinIsTitleBlock =
                        string.Equals(thinSource, "blechdicke", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(thinSource, "blechdicke-rect", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(thinSource, "blechdicke-nachbar", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(thinSource, "schriftfeld-thk", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(thinSource, "schriftfeld-label", StringComparison.OrdinalIgnoreCase);

                    if (!(valueFromSideView && IsPlausiblePlateThicknessMm(value) && !thinIsTitleBlock)
                        && !(valueFromSideView && IsStandardSheetGauge(value, 0.15d) && !thinIsTitleBlock))
                    {
                        return thin;
                    }
                }
            }

            if (value < 7.5d || !IsWholeNumber(value))
                return resultMm;

            if (HasExplicitSourceForValue(candidates, value))
                return resultMm;

            if (trustedThin.Count > 0
                && !(valueFromSideView && IsPlausiblePlateThicknessMm(value)))
                return trustedThin[0].ValueMm;

            var typicalThin = candidates
                .Where(c => !c.SuspectWatermark)
                .Where(c => IsTypicalSheetThickness(c.ValueMm))
                .Where(c => !IsLikelyHoleDiameterMm(c.ValueMm))
                .Where(c => !IsMaterialNumberThicknessFalsePositive(c.ValueMm, materialRaw, c.Source, c.Source))
                .OrderByDescending(ComputeQualityScore)
                .ThenByDescending(c => c.Priority)
                .ToList();

            if (typicalThin.Count > 0 && value > MaxAutoDetectedSheetMm + 2d)
            {
                if (valueFromSideView && IsPlausiblePlateThicknessMm(value))
                    return resultMm;

                return typicalThin[0].ValueMm;
            }

            return resultMm;
        }

        public static bool HasExplicitSourceForValue(IList<ThicknessCandidate> candidates, double valueMm)
        {
            if (candidates == null || candidates.Count == 0)
                return false;

            return candidates.Any(c =>
                Math.Abs(c.ValueMm - valueMm) < 0.08d
                && IsExplicitThicknessSource(c.Source));
        }

        public static bool HasConfidentExplicitThickness(IList<ThicknessCandidate> candidates, string materialRaw)
        {
            if (candidates == null || candidates.Count == 0)
                return false;

            var explicitCandidates = candidates
                .Where(c => !c.SuspectWatermark)
                .Where(c => IsExplicitThicknessSource(c.Source)
                            || string.Equals(c.Source, "schriftfeld-label", StringComparison.OrdinalIgnoreCase)
                            || string.Equals(c.Source, "schriftfeld-thk", StringComparison.OrdinalIgnoreCase))
                .Where(c => !IsLikelyHoleDiameterMm(c.ValueMm))
                .Where(c => !IsMaterialNumberThicknessFalsePositive(c.ValueMm, materialRaw, c.Source, c.Source))
                .ToList();

            return explicitCandidates.Count > 0;
        }

        public static bool HasFractionalPart(double mm)
        {
            return Math.Abs(mm - Math.Round(mm)) > 0.05d;
        }

        public static bool IsWholeNumber(double mm)
        {
            return Math.Abs(mm - Math.Round(mm)) <= 0.05d;
        }

        public static bool IsExplicitThicknessSource(string source)
        {
            if (string.IsNullOrWhiteSpace(source))
                return false;

            switch (source)
            {
                case "inline":
                case "inline-glued":
                case "inline-t":
                case "label":
                case "blechdicke":
                case "blechdicke-rect":
                case "blechdicke-nachbar":
                case "schriftfeld-thk":
                case "schriftfeld-label":
                case "ocr-schriftfeld":
                case "zeichnung-profil":
                    return true;
                default:
                    return false;
            }
        }

        internal static bool IsSideViewSource(string source)
        {
            if (string.IsNullOrWhiteSpace(source))
                return false;

            return source.IndexOf("seiten", StringComparison.OrdinalIgnoreCase) >= 0
                   || source.IndexOf("3-ansicht", StringComparison.OrdinalIgnoreCase) >= 0
                   || source.IndexOf("raster", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static ThicknessCandidate PickBestQuality(IList<ThicknessCandidate> list)
        {
            return list
                .OrderByDescending(ComputeQualityScore)
                .ThenByDescending(c => c.Priority)
                .First();
        }

        private static double ComputeQualityScore(ThicknessCandidate candidate)
        {
            var score = 0d;
            var mm = candidate.ValueMm;
            var isRasterProfile = string.Equals(candidate.Source, "raster-seitenprofil", StringComparison.OrdinalIgnoreCase);

            if (isRasterProfile)
            {
                score += 80d;
                if (IsPlausiblePlateThicknessMm(mm))
                    score += 45d;
                else if (IsTypicalSheetThickness(mm))
                    score += 35d;

                score -= StandardGaugeDistance(mm) * 3d;
                return score;
            }

            if (IsSideViewSource(candidate.Source))
                score += 30d;

            if (IsExplicitThicknessSource(candidate.Source))
                score += 35d;

            if (mm > MaxAutoDetectedSheetMm && mm <= MaxSideViewThicknessMm && IsSideViewSource(candidate.Source))
                score += 40d;

            if (HasFractionalPart(mm))
                score += 25d;

            if (IsStandardSheetGauge(mm, 0.12d) && mm <= MaxAutoDetectedSheetMm)
                score += 40d;

            if (IsLikelyHoleDiameterMm(mm) && !IsExplicitThicknessSource(candidate.Source))
                score -= 200d;

            if (IsTypicalSheetThickness(mm))
                score += 35d;
            else if (mm > MaxAutoDetectedSheetMm && !IsExplicitThicknessSource(candidate.Source))
                score -= 80d;

            if (IsWholeNumber(mm) && mm >= 4d && !IsExplicitThicknessSource(candidate.Source))
                score -= 45d;

            if (IsWholeNumber(mm) && mm >= 8d && !IsExplicitThicknessSource(candidate.Source))
                score -= 60d;

            if (mm >= 0.5d && mm <= 6d)
                score += 15d;

            if (IsStandardSheetGauge(mm))
                score += 10d;

            score -= StandardGaugeDistance(mm) * 5d;
            return score;
        }
    }
}
