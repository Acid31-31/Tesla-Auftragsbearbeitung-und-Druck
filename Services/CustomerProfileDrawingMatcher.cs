using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using PDF_umbenenen.Models;
using PDF_umbenenen.Services.Ai;

namespace PDF_umbenenen.Services
{
    /// <summary>
    /// Profilgesteuerte Zeichnungsnummern-Erkennung und Abgleich (nicht nur TSL/Tesla).
    /// </summary>
    internal static class CustomerProfileDrawingMatcher
    {
        private static readonly Regex GenericDrawingRegex = new Regex(
            @"(?<![A-Z0-9])((?:[A-Z]{2,8}[-_./]?\d{2,8}(?:[-_./]\d{2,8})*(?:[-_.][A-Z0-9]+)?)|(?:\d{4,8}[-_./]\d{2,8}(?:[-_.][A-Z0-9]+)?))(?![A-Z0-9])",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex RevisionTokenRegex = new Regex(
            @"[_\-\s](?:REV|R|V)\d{1,3}(?=$|[.\s_])",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

        public static string ExtractBest(string source, CustomerProfile profile)
        {
            if (string.IsNullOrWhiteSpace(source))
                return string.Empty;

            source = RevisionTokenRegex.Replace(source, string.Empty);

            var hits = new List<string>();

            if (profile != null && !string.IsNullOrWhiteSpace(profile.DrawingNumberPattern))
            {
                try
                {
                    var regex = new Regex(profile.DrawingNumberPattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
                    foreach (Match match in regex.Matches(source))
                    {
                        var value = match.Groups.Count > 1 && !string.IsNullOrWhiteSpace(match.Groups[1].Value)
                            ? match.Groups[1].Value
                            : match.Value;
                        if (!string.IsNullOrWhiteSpace(value))
                            hits.Add(value.Trim().ToUpperInvariant());
                    }
                }
                catch
                {
                }
            }

            if (profile?.SampleDrawingNumbers != null)
            {
                foreach (var sample in profile.SampleDrawingNumbers)
                {
                    if (string.IsNullOrWhiteSpace(sample))
                        continue;

                    if (source.IndexOf(sample, StringComparison.OrdinalIgnoreCase) >= 0)
                        hits.Add(sample.Trim().ToUpperInvariant());
                }
            }

            hits.AddRange(OrderVerificationDrawingKey.ExtractAllFromText(source).Select(t => t.ToUpperInvariant()));

            if (hits.Count == 0)
            {
                foreach (Match match in GenericDrawingRegex.Matches(source))
                {
                    var value = match.Groups[1].Value.Trim().ToUpperInvariant();
                    if (!OrderVerificationDrawingKey.IsLikelyDateToken(value))
                        hits.Add(value);
                }
            }

            return hits
                .Where(h => !string.IsNullOrWhiteSpace(h))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderByDescending(h => h.Length)
                .FirstOrDefault() ?? string.Empty;
        }

        public static string NormalizeKey(string value, CustomerProfile profile = null)
        {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;

            var extracted = ExtractBest(value, profile);
            if (string.IsNullOrWhiteSpace(extracted))
                extracted = value.Trim();

            var normalized = OrderVerificationDrawingKey.Normalize(extracted);
            if (!string.IsNullOrWhiteSpace(normalized))
                return normalized;

            return AiTextHelper.NormalizeKey(extracted);
        }

        public static bool KeysMatch(string pdfKey, string excelValue, CustomerProfile profile = null)
        {
            if (string.IsNullOrWhiteSpace(pdfKey) || string.IsNullOrWhiteSpace(excelValue))
                return false;

            var a = NormalizeKey(pdfKey, profile);
            var b = NormalizeKey(excelValue, profile);
            if (string.IsNullOrWhiteSpace(a) || string.IsNullOrWhiteSpace(b))
            {
                a = AiTextHelper.NormalizeKey(pdfKey);
                b = AiTextHelper.NormalizeKey(excelValue);
            }

            if (string.IsNullOrWhiteSpace(a) || string.IsNullOrWhiteSpace(b))
                return false;

            if (string.Equals(a, b, StringComparison.OrdinalIgnoreCase))
                return true;

            if (IsSpiegelbildDistinct(a, b))
                return false;

            if (OrderVerificationDrawingKey.Equal(a, b) || OrderVerificationDrawingKey.Equal(pdfKey, excelValue))
                return true;

            var coreA = StripProfilePrefix(a, profile);
            var coreB = StripProfilePrefix(b, profile);
            if (IsSpiegelbildDistinct(coreA, coreB))
                return false;
            if (!string.IsNullOrWhiteSpace(coreA) && !string.IsNullOrWhiteSpace(coreB)
                && string.Equals(coreA, coreB, StringComparison.OrdinalIgnoreCase)
                && coreA.Length >= 6)
                return true;

            // Kein „enthält“ / Similarity für lange Nummern – sonst RT110000001 ≈ RT110000002
            if (a.Length >= 8 && b.Length >= 8)
                return false;

            if (a.Length >= 6 && b.IndexOf(a, StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
            if (b.Length >= 6 && a.IndexOf(b, StringComparison.OrdinalIgnoreCase) >= 0)
                return true;

            return false;
        }

        public static bool RowMatchesPdf(
            string pdfSearchKey,
            string rawDrawingNumber,
            IEnumerable<string> normalizedCells,
            CustomerProfile profile = null)
        {
            if (string.IsNullOrWhiteSpace(pdfSearchKey))
                return false;

            if (!string.IsNullOrWhiteSpace(rawDrawingNumber)
                && KeysMatch(pdfSearchKey, rawDrawingNumber, profile))
                return true;

            if (normalizedCells == null)
                return false;

            return normalizedCells.Any(cell => KeysMatch(pdfSearchKey, cell, profile));
        }

        /// <summary>
        /// TSL-12073-323 und TSL-12073-323-S (Spiegelbild) sind verschiedene Bestellpositionen.
        /// </summary>
        private static bool IsSpiegelbildDistinct(string a, string b)
        {
            if (string.IsNullOrWhiteSpace(a) || string.IsNullOrWhiteSpace(b))
                return false;
            if (string.Equals(a, b, StringComparison.OrdinalIgnoreCase))
                return false;

            if (a.Length == b.Length + 1
                && a.StartsWith(b, StringComparison.OrdinalIgnoreCase)
                && EndsWithSpiegelbildSuffix(a))
                return true;

            if (b.Length == a.Length + 1
                && b.StartsWith(a, StringComparison.OrdinalIgnoreCase)
                && EndsWithSpiegelbildSuffix(b))
                return true;

            return false;
        }

        private static bool EndsWithSpiegelbildSuffix(string key)
        {
            return key != null && key.EndsWith("S", StringComparison.OrdinalIgnoreCase);
        }

        private static string StripProfilePrefix(string normalizedKey, CustomerProfile profile)
        {
            if (string.IsNullOrWhiteSpace(normalizedKey))
                return string.Empty;

            var prefix = GetProfilePrefix(profile);
            if (!string.IsNullOrWhiteSpace(prefix)
                && normalizedKey.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                && normalizedKey.Length > prefix.Length)
            {
                return normalizedKey.Substring(prefix.Length);
            }

            var i = 0;
            while (i < normalizedKey.Length && char.IsLetter(normalizedKey[i]))
                i++;

            if (i >= 2 && i <= 8 && i < normalizedKey.Length && char.IsDigit(normalizedKey[i]))
                return normalizedKey.Substring(i);

            return normalizedKey;
        }

        private static string GetProfilePrefix(CustomerProfile profile)
        {
            if (profile == null)
                return string.Empty;

            if (profile.SampleDrawingNumbers != null)
            {
                foreach (var sample in profile.SampleDrawingNumbers)
                {
                    var prefix = ExtractPrefixFromSample(sample);
                    if (!string.IsNullOrWhiteSpace(prefix))
                        return prefix;
                }
            }

            if (!string.IsNullOrWhiteSpace(profile.DrawingNumberPattern))
            {
                var match = Regex.Match(profile.DrawingNumberPattern, @"\\b([A-Z]{2,8})[-\\]");
                if (match.Success)
                    return AiTextHelper.NormalizeKey(match.Groups[1].Value);
            }

            return string.Empty;
        }

        private static string ExtractPrefixFromSample(string sample)
        {
            if (string.IsNullOrWhiteSpace(sample))
                return string.Empty;

            var match = Regex.Match(sample.Trim(), @"^([A-Za-z]{2,8})[-_.]?");
            return match.Success ? AiTextHelper.NormalizeKey(match.Groups[1].Value) : string.Empty;
        }

        public static bool LooksLikeDrawingNumber(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return false;

            var text = value.Trim().ToUpperInvariant();
            if (System.Text.RegularExpressions.Regex.IsMatch(text, @"^TGA\d+$"))
                return false;

            if (OrderVerificationDrawingKey.IsLikelyDrawingToken(text))
                return true;

            if (!text.Contains("-") && !text.Contains(" "))
                return false;

            return text.Any(char.IsLetter) && text.Any(char.IsDigit);
        }
    }
}
