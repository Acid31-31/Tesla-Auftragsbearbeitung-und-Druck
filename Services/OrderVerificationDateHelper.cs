using System;
using System.Globalization;
using System.Text.RegularExpressions;

namespace PDF_umbenenen.Services
{
    internal static class OrderVerificationDateHelper
    {
        private static readonly Regex IsoDueDateRegex = new Regex(
            @"\b(20\d{2})-(0[1-9]|1[0-2])-(0[1-9]|[12]\d|3[01])\b",
            RegexOptions.Compiled);

        private static readonly Regex EuDueDateRegex = new Regex(
            @"\b(0?[1-9]|[12]\d|3[01])[./](0?[1-9]|1[0-2])[./](20\d{2}|\d{2})\b",
            RegexOptions.Compiled);

        private static readonly string[] IsoDateFormats =
        {
            "yyyy-MM-dd",
            "yyyy/MM/dd",
            "yyyy.MM.dd"
        };

        public static string ExtractDueDate(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return string.Empty;

            var iso = IsoDueDateRegex.Match(text);
            if (iso.Success)
                return NormalizeToEu(iso.Value);

            var eu = EuDueDateRegex.Match(text);
            if (eu.Success)
                return NormalizeToEu(eu.Value);

            return string.Empty;
        }

        public static string NormalizeToEu(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
                return string.Empty;

            var text = raw.Trim();
            if (text.StartsWith("LT-", StringComparison.OrdinalIgnoreCase))
                return text;

            var isoMatch = IsoDueDateRegex.Match(text);
            if (isoMatch.Success
                && DateTime.TryParseExact(
                    isoMatch.Value,
                    IsoDateFormats,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.None,
                    out var isoDate))
            {
                return isoDate.ToString("dd.MM.yyyy", CultureInfo.GetCultureInfo("de-DE"));
            }

            if (DateTime.TryParseExact(
                    text,
                    IsoDateFormats,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.None,
                    out var isoOnly))
            {
                return isoOnly.ToString("dd.MM.yyyy", CultureInfo.GetCultureInfo("de-DE"));
            }

            if (DateTime.TryParse(text, CultureInfo.GetCultureInfo("de-DE"), DateTimeStyles.None, out var dtDe))
                return dtDe.ToString("dd.MM.yyyy", CultureInfo.GetCultureInfo("de-DE"));

            if (DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dtInv))
                return dtInv.ToString("dd.MM.yyyy", CultureInfo.GetCultureInfo("de-DE"));

            return text;
        }

        public static bool AreEqual(string a, string b)
        {
            var normA = NormalizeToEu(a);
            var normB = NormalizeToEu(b);
            if (string.IsNullOrWhiteSpace(normA) || string.IsNullOrWhiteSpace(normB))
                return true;

            return string.Equals(normA, normB, StringComparison.OrdinalIgnoreCase);
        }
    }
}
