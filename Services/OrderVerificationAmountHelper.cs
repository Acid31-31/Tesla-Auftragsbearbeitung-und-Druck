using System;
using System.Globalization;
using System.Text.RegularExpressions;

namespace PDF_umbenenen.Services
{
    internal static class OrderVerificationAmountHelper
    {
        private static readonly Regex QtyWithUnitRegex = new Regex(
            @"\b(\d+(?:[.,]\d+)?)\s*(?:EA|PCS|STK\.?|STÜCK|STUECK)\b",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex QtyBeforeIsoDateRegex = new Regex(
            @"\b(\d+(?:[.,]\d+)?)\s*(?:EA|PCS|STK\.?)?\s*(20\d{2}-(?:0[1-9]|1[0-2])-(?:0[1-9]|[12]\d|3[01]))\b",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        public static string ExtractQuantity(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return string.Empty;

            var withUnit = QtyWithUnitRegex.Match(text);
            if (withUnit.Success)
                return NormalizeQuantity(withUnit.Groups[1].Value);

            var beforeDate = QtyBeforeIsoDateRegex.Match(text);
            if (beforeDate.Success)
                return NormalizeQuantity(beforeDate.Groups[1].Value);

            return string.Empty;
        }

        public static string NormalizeQuantity(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
                return string.Empty;

            var text = raw.Trim().Replace(',', '.');
            if (decimal.TryParse(text, NumberStyles.Any, CultureInfo.InvariantCulture, out var value))
            {
                if (value == Math.Truncate(value))
                    return ((int)value).ToString(CultureInfo.InvariantCulture);

                return value.ToString("0.##", CultureInfo.InvariantCulture);
            }

            var digits = raw.Trim();
            return string.IsNullOrWhiteSpace(digits) ? string.Empty : digits;
        }

        public static bool AreEqual(string excelValue, string pdfValue)
        {
            var excel = TryParseQuantity(excelValue);
            var pdf = TryParseQuantity(pdfValue);

            if (!excel.HasValue && !pdf.HasValue)
                return true;

            if (!excel.HasValue || !pdf.HasValue)
                return false;

            return excel.Value == pdf.Value;
        }

        public static string FormatDisplay(string value)
        {
            var normalized = NormalizeQuantity(value);
            return string.IsNullOrWhiteSpace(normalized) ? "—" : normalized;
        }

        private static decimal? TryParseQuantity(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return null;

            var normalized = NormalizeQuantity(value);
            if (string.IsNullOrWhiteSpace(normalized))
                return null;

            if (decimal.TryParse(normalized, NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed))
                return parsed;

            return null;
        }
    }
}
