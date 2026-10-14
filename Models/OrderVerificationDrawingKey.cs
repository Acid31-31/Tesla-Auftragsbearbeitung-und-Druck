using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using PDF_umbenenen.Services.Ai;

namespace PDF_umbenenen.Models
{
    /// <summary>
    /// Einheitliche Zeichnungsnummern-Erkennung für alle Bestellungen (beliebiges Präfix/Format).
    /// </summary>
    internal static class OrderVerificationDrawingKey
    {
        /// <summary>TSL-1227-125, ABC-12345-678, 1227-125-R00, …</summary>
        private static readonly Regex FullDrawingRegex = new Regex(
            @"(?<![A-Z0-9])((?:[A-Z]{2,6}-\d{4,6}-\d{2,6}(?:-[A-Z0-9]+)?)|(?:\d{4,5}-\d{3}(?:-[A-Z0-9]+)?))(?![A-Z0-9])",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>EcoMark u. a.: RT1 10 00 0001 / RT1-10-00-0001 (Leerzeichen oder Bindestrich).</summary>
        private static readonly Regex SpacedSegmentDrawingRegex = new Regex(
            @"\b([A-Z]{1,6}\d?(?:[\s\-_/]+\d{1,5}){2,4})\b",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>Hängt oft die Bestellmenge an: „RT1 10 00 0001 1“ → echte Nummer ohne trailing Qty.</summary>
        private static readonly Regex TrailingQtyOnDrawingRegex = new Regex(
            @"^(.+?\s\d{3,5})\s+\d{1,2}$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex PrefixedDrawingRegex = new Regex(
            @"^[A-Z]{2,6}\d?[\s\-_./]?\d",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex CoreDrawingRegex = new Regex(
            @"(?:[A-Z]{2,6}-)?(\d{4,6}-\d{2,6}(?:-[A-Z0-9]+)?)",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex BgMarkerRegex = new Regex(
            @"\bBG\s*:\s*((?:[A-Z]{2,6}-)?\d{4,6}-\d{2,6}(?:-[A-Z0-9]+)?)",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        public static string Normalize(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;

            var trimmed = StripTrailingQuantityArtifact(value);
            if (string.IsNullOrWhiteSpace(trimmed) || IsLikelyDateToken(trimmed))
                return string.Empty;

            var fullMatch = FullDrawingRegex.Match(trimmed);
            if (fullMatch.Success && !IsLikelyDateToken(fullMatch.Groups[1].Value))
                return AiTextHelper.NormalizeKey(fullMatch.Groups[1].Value);

            var spacedMatch = SpacedSegmentDrawingRegex.Match(trimmed);
            if (spacedMatch.Success)
            {
                var spaced = StripTrailingQuantityArtifact(spacedMatch.Groups[1].Value);
                return AiTextHelper.NormalizeKey(spaced);
            }

            var coreMatch = CoreDrawingRegex.Match(trimmed);
            if (coreMatch.Success && !IsLikelyDateToken(coreMatch.Groups[1].Value))
                return AiTextHelper.NormalizeKey(coreMatch.Groups[1].Value);

            var normalized = AiTextHelper.NormalizeKey(trimmed);
            if (normalized.StartsWith("TSL", StringComparison.OrdinalIgnoreCase) && normalized.Length > 3)
            {
                var withoutPrefix = normalized.Substring(3);
                if (withoutPrefix.Length >= 5)
                    return withoutPrefix;
            }

            return normalized;
        }

        public static bool Equal(string a, string b)
        {
            var keyA = Normalize(a);
            var keyB = Normalize(b);
            if (string.IsNullOrWhiteSpace(keyA) || string.IsNullOrWhiteSpace(keyB))
                return false;

            return string.Equals(keyA, keyB, StringComparison.OrdinalIgnoreCase);
        }

        public static string ExtractPrimaryFromTexts(params string[] sources)
        {
            if (sources == null)
                return string.Empty;

            foreach (var source in sources)
            {
                if (string.IsNullOrWhiteSpace(source))
                    continue;

                var tokens = ExtractDrawingTokens(source);
                if (tokens.Count == 0)
                    continue;

                var prefixed = tokens.FirstOrDefault(t => PrefixedDrawingRegex.IsMatch(t));
                return (prefixed ?? tokens[0]).ToUpperInvariant();
            }

            return string.Empty;
        }

        public static IReadOnlyList<string> ExtractAllFromText(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return Array.Empty<string>();

            return ExtractDrawingTokens(text);
        }

        /// <summary>
        /// Nur echte Zeichnungsnummern mit Präfix (TSL-, ABC-, …).
        /// Maße wie 1041-441 werden ignoriert – wichtig für Baugruppen-Erkennung.
        /// </summary>
        public static IReadOnlyList<string> ExtractPrefixedFromText(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return Array.Empty<string>();

            return ExtractDrawingTokens(text)
                .Where(t => PrefixedDrawingRegex.IsMatch(t))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        public static int CountPrefixedDrawings(string text)
        {
            return ExtractPrefixedFromText(text).Count;
        }

        public static int CountOtherPrefixedDrawings(string text, string primaryKey)
        {
            if (string.IsNullOrWhiteSpace(text) || string.IsNullOrWhiteSpace(primaryKey))
                return 0;

            var normalizedPrimary = Normalize(primaryKey);
            if (string.IsNullOrWhiteSpace(normalizedPrimary))
                return 0;

            return ExtractPrefixedFromText(text)
                .Select(Normalize)
                .Where(k => !string.IsNullOrWhiteSpace(k)
                            && !string.Equals(k, normalizedPrimary, StringComparison.OrdinalIgnoreCase))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count();
        }

        public static bool TryExtractFromBgMarker(string text, out string drawingNumber)
        {
            drawingNumber = string.Empty;
            if (string.IsNullOrWhiteSpace(text))
                return false;

            var match = BgMarkerRegex.Match(text);
            if (!match.Success || IsLikelyDateToken(match.Groups[1].Value))
                return false;

            drawingNumber = match.Groups[1].Value.ToUpperInvariant();
            return true;
        }

        public static bool IsLikelyDrawingToken(string value)
        {
            if (string.IsNullOrWhiteSpace(value) || IsLikelyDateToken(value))
                return false;

            var trimmed = StripTrailingQuantityArtifact(value);
            return FullDrawingRegex.IsMatch(trimmed) || SpacedSegmentDrawingRegex.IsMatch(trimmed);
        }

        private static List<string> ExtractDrawingTokens(string text)
        {
            var tokens = new List<string>();

            foreach (Match m in FullDrawingRegex.Matches(text))
            {
                var t = m.Groups[1].Value.ToUpperInvariant();
                if (!IsLikelyDateToken(t))
                    tokens.Add(t);
            }

            foreach (Match m in SpacedSegmentDrawingRegex.Matches(text))
            {
                var t = Regex.Replace(m.Groups[1].Value.Trim(), @"\s+", " ").ToUpperInvariant();
                t = StripTrailingQuantityArtifact(t);
                if (!IsLikelyDateToken(t) && IsLikelyDrawingToken(t))
                    tokens.Add(t);
            }

            return tokens
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        /// <summary>Entfernt angehängte Mengen aus Lernbeispielen (RT1 10 00 0001 1 → RT1 10 00 0001).</summary>
        public static string StripTrailingQuantityArtifact(string drawing)
        {
            if (string.IsNullOrWhiteSpace(drawing))
                return string.Empty;

            var value = Regex.Replace(drawing.Trim(), @"\s+", " ");
            // „… 0001 1/1“ (Blattangabe)
            value = Regex.Replace(value, @"\s+\d{1,2}/\d{1,2}$", string.Empty, RegexOptions.IgnoreCase);

            var trailing = TrailingQtyOnDrawingRegex.Match(value);
            if (trailing.Success)
            {
                var core = trailing.Groups[1].Value.Trim();
                // Nur strippen wenn Kern noch wie Zeichnung aussieht
                if (SpacedSegmentDrawingRegex.IsMatch(core) || FullDrawingRegex.IsMatch(core))
                    return core.ToUpperInvariant();
            }

            return value.ToUpperInvariant();
        }

        /// <summary>ISO-Datum (2021-05-13) nicht als Zeichnungsnummer werten.</summary>
        public static bool IsLikelyDateToken(string token)
        {
            if (string.IsNullOrWhiteSpace(token))
                return false;

            var match = Regex.Match(token.Trim(), @"^(\d{4})-(\d{1,2})(?:-(\d{1,2}))?$");
            if (!match.Success)
                return false;

            if (!int.TryParse(match.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var year))
                return false;

            if (year < 1900 || year > 2099)
                return false;

            if (!int.TryParse(match.Groups[2].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var month))
                return false;

            return month >= 1 && month <= 12;
        }
    }
}
