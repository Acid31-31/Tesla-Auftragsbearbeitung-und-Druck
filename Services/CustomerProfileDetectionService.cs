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

            if (keys.Count == 0 || profiles == null || profiles.Count == 0)
            {
                return new CustomerDetectionResult
                {
                    Profile = CustomerProfileStore.GetBuiltInTesla(),
                    MatchCount = 0,
                    TotalCount = 0
                };
            }

            CustomerProfile bestProfile = null;
            var bestScore = -1;

            foreach (var profile in profiles)
            {
                var score = ScoreProfile(profile, keys);
                if (score > bestScore)
                {
                    bestScore = score;
                    bestProfile = profile;
                }
            }

            if (bestProfile == null)
                bestProfile = CustomerProfileStore.GetBuiltInTesla();

            return new CustomerDetectionResult
            {
                Profile = bestProfile,
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

            var key = AiTextHelper.NormalizeKey(drawingKey);
            if (profile.SampleDrawingNumbers != null)
            {
                foreach (var sample in profile.SampleDrawingNumbers)
                {
                    if (OrderVerificationDrawingKey.Equal(sample, key))
                        return true;
                }
            }

            try
            {
                var regex = new Regex(profile.DrawingNumberPattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
                if (regex.IsMatch(drawingKey) || regex.IsMatch(key))
                    return true;
            }
            catch
            {
            }

            return OrderVerificationDrawingKey.IsLikelyDrawingToken(drawingKey);
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
