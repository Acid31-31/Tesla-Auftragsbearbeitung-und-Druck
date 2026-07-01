using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace PDF_umbenenen.Services
{
    internal static class CustomerProfilePatternBuilder
    {
        private static readonly Regex TokenRegex = new Regex(
            @"\b((?:[A-Z]{2,6}-)?\d{4,6}-\d{2,6}(?:-[A-Z0-9]+)?)\b",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex PrefixRegex = new Regex(
            @"^([A-Z]{2,6}-)",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        public static string BuildDrawingPattern(IEnumerable<string> samples)
        {
            var tokens = (samples ?? Enumerable.Empty<string>())
                .Select(NormalizeSample)
                .Where(t => !string.IsNullOrWhiteSpace(t))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (tokens.Count == 0)
                return Models.CustomerProfile.DefaultDrawingPattern;

            var matched = tokens.Where(t => TokenRegex.IsMatch(t)).ToList();
            if (matched.Count == 0)
            {
                var escaped = tokens.Take(8).Select(Regex.Escape);
                return @"\b(" + string.Join("|", escaped) + @")\b";
            }

            var prefixGroups = matched
                .Select(t =>
                {
                    var m = PrefixRegex.Match(t);
                    return m.Success ? m.Groups[1].Value.ToUpperInvariant() : string.Empty;
                })
                .Where(p => !string.IsNullOrWhiteSpace(p))
                .GroupBy(p => p, StringComparer.OrdinalIgnoreCase)
                .OrderByDescending(g => g.Count())
                .ToList();

            if (prefixGroups.Count > 0)
            {
                var top = prefixGroups[0];
                if (top.Count() >= Math.Max(1, matched.Count * 0.5))
                    return @"\b" + Regex.Escape(top.Key) + @"\d{2,6}-\d{2,6}(?:-[A-Z0-9]+)?\b";
            }

            return Models.CustomerProfile.DefaultDrawingPattern;
        }

        public static string BuildArticlePattern(IEnumerable<string> samples)
        {
            var tokens = (samples ?? Enumerable.Empty<string>())
                .Select(NormalizeSample)
                .Where(t => Regex.IsMatch(t, @"^TGA\d+$", RegexOptions.IgnoreCase))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (tokens.Count == 0)
                return string.Empty;

            return @"\bTGA\d+\b";
        }

        private static string NormalizeSample(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim().ToUpperInvariant();
        }
    }
}
