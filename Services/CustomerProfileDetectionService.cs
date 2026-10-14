using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using PDF_umbenenen.Models;
using PDF_umbenenen.Services.Ai;

namespace PDF_umbenenen.Services
{
    internal static class CustomerProfileDetectionService
    {
        public static CustomerDetectionResult Detect(IEnumerable<string> drawingKeys, IReadOnlyList<CustomerProfile> profiles)
        {
            var keys = (drawingKeys ?? Enumerable.Empty<string>())
                .Where(k => !string.IsNullOrWhiteSpace(k))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (profiles == null || profiles.Count == 0)
            {
                return new CustomerDetectionResult
                {
                    Profile = null,
                    MatchCount = 0,
                    TotalCount = keys.Count
                };
            }

            CustomerProfile bestProfile = null;
            var bestScore = -1;
            var secondScore = -1;

            foreach (var profile in profiles)
            {
                var score = ScoreProfile(profile, keys);
                if (score > bestScore)
                {
                    secondScore = bestScore;
                    bestScore = score;
                    bestProfile = profile;
                }
                else if (score > secondScore)
                {
                    secondScore = score;
                }
            }

            // Nie einen Kunden mit 0 Treffern erzwingen (früher: einziges Profil = immer Tesla)
            if (bestProfile != null && bestScore > 0 && (profiles.Count == 1 || bestScore > secondScore))
            {
                return new CustomerDetectionResult
                {
                    Profile = bestProfile,
                    MatchCount = bestScore,
                    TotalCount = keys.Count
                };
            }

            return new CustomerDetectionResult
            {
                Profile = null,
                MatchCount = Math.Max(0, bestScore),
                TotalCount = keys.Count
            };
        }

        public static CustomerDetectionResult DetectWorkspace(
            string pdfFolder,
            SearchOption searchOption,
            string excludeOrderPdfPath,
            Func<string, SearchOption, string, IEnumerable<string>> loadPdfKeys)
        {
            var keys = loadPdfKeys?.Invoke(pdfFolder, searchOption, excludeOrderPdfPath)?.ToList()
                       ?? new List<string>();

            return Detect(keys, CustomerProfileStore.LoadAll());
        }

        public static bool MatchesDrawing(CustomerProfile profile, string drawingKey)
        {
            if (profile == null || string.IsNullOrWhiteSpace(drawingKey))
                return false;

            var key = drawingKey.Trim();
            var normalized = AiTextHelper.NormalizeKey(key);

            if (profile.SampleDrawingNumbers != null)
            {
                foreach (var sample in profile.SampleDrawingNumbers)
                {
                    if (string.IsNullOrWhiteSpace(sample))
                        continue;
                    if (OrderVerificationDrawingKey.Equal(sample, key)
                        || OrderVerificationDrawingKey.Equal(sample, normalized)
                        || string.Equals(AiTextHelper.NormalizeKey(sample), normalized, StringComparison.OrdinalIgnoreCase))
                        return true;
                }
            }

            if (string.IsNullOrWhiteSpace(profile.DrawingNumberPattern))
                return false;

            try
            {
                var regex = new Regex(profile.DrawingNumberPattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
                if (regex.IsMatch(key))
                    return true;

                // Normalisierte Keys ohne Leerzeichen: Muster mit Leerzeichen/Bindestrich anpassen
                if (!string.IsNullOrWhiteSpace(normalized) && normalized.Length >= 6)
                {
                    var spacedBack = Regex.Replace(normalized, @"([A-Z]+)(\d)", "$1 $2");
                    if (regex.IsMatch(normalized) || regex.IsMatch(spacedBack))
                        return true;

                    // RT110000001 ↔ Muster RT1(?:[\s\-_/]+\d{1,5}){3}
                    if (LooksLikeSpacedSegmentKey(normalized) && PatternLooksLikeSpacedSegment(profile.DrawingNumberPattern))
                    {
                        var prefix = Regex.Match(profile.DrawingNumberPattern, @"\\b([A-Z]{1,6}\d?)");
                        if (prefix.Success
                            && normalized.StartsWith(prefix.Groups[1].Value, StringComparison.OrdinalIgnoreCase)
                            && normalized.Length > prefix.Groups[1].Value.Length + 4)
                            return true;
                    }
                }
            }
            catch
            {
            }

            // KEIN Fallback auf „irgendeine Zeichnungsnummer“ – sonst matched Tesla auch EcoMark/RT.
            return false;
        }

        private static bool LooksLikeSpacedSegmentKey(string normalized)
        {
            return Regex.IsMatch(normalized ?? string.Empty, @"^[A-Z]{1,6}\d\d{6,}$", RegexOptions.IgnoreCase);
        }

        private static bool PatternLooksLikeSpacedSegment(string pattern)
        {
            if (string.IsNullOrWhiteSpace(pattern))
                return false;
            return pattern.IndexOf(@"[\s\-_/]", StringComparison.Ordinal) >= 0
                   || pattern.IndexOf("\\s", StringComparison.Ordinal) >= 0;
        }

        private static int ScoreProfile(CustomerProfile profile, IList<string> keys)
        {
            if (profile == null || keys.Count == 0)
                return 0;

            var score = 0;
            foreach (var key in keys)
            {
                if (MatchesDrawing(profile, key))
                    score++;
            }

            if (profile.SampleDrawingNumbers != null && profile.SampleDrawingNumbers.Count > 0)
            {
                var sampleHits = keys.Count(k =>
                    profile.SampleDrawingNumbers.Any(s => OrderVerificationDrawingKey.Equal(s, k)));
                score += sampleHits;
            }

            return score;
        }
    }
}
