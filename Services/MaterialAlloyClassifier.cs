using System;
using System.Globalization;
using System.Text.RegularExpressions;

namespace PDF_umbenenen.Services
{
    /// <summary>
    /// Leitet aus Werkstoffnummer/-bezeichnung die Legierungsklasse für das Wasserzeichen ab.
    /// ST = Stahl, VA = Edelstahl, ALU = Aluminium.
    /// </summary>
    internal static class MaterialAlloyClassifier
    {
        private static readonly Regex EnSteelNumberRegex = new Regex(
            @"\b1\.(0\d{3}|1\d{3})\b",
            RegexOptions.CultureInvariant | RegexOptions.Compiled);

        private static readonly Regex EnStainlessNumberRegex = new Regex(
            @"\b1\.(4\d{3}|5\d{3})\b",
            RegexOptions.CultureInvariant | RegexOptions.Compiled);

        private static readonly Regex EnAluminumNumberRegex = new Regex(
            @"\b3\.\d{4}\b",
            RegexOptions.CultureInvariant | RegexOptions.Compiled);

        public static string ClassifyPrefix(string materialRaw)
        {
            if (string.IsNullOrWhiteSpace(materialRaw))
                return string.Empty;

            var text = materialRaw.Trim();
            var upper = text.ToUpperInvariant();

            if (IsAluminum(upper, text))
                return "ALU";

            if (IsStainlessSteel(upper, text))
                return "VA";

            if (IsCarbonSteel(upper, text))
                return "ST";

            if (EnSteelNumberRegex.IsMatch(text))
                return "ST";

            if (EnStainlessNumberRegex.IsMatch(text))
                return "VA";

            if (EnAluminumNumberRegex.IsMatch(text))
                return "ALU";

            if (Regex.IsMatch(upper, @"[A-Z]"))
                return "ST";

            return string.Empty;
        }

        public static string BuildWatermarkText(string materialRaw, string normalizedThickness)
        {
            var prefix = ClassifyPrefix(materialRaw);
            var thickness = FormatThicknessCompact(normalizedThickness);

            if (!string.IsNullOrWhiteSpace(prefix) && !string.IsNullOrWhiteSpace(thickness))
                return prefix + "-" + thickness;

            if (!string.IsNullOrWhiteSpace(thickness))
                return thickness;

            if (!string.IsNullOrWhiteSpace(prefix))
                return prefix;

            return string.Empty;
        }

        public static string FormatThicknessCompact(string normalizedThickness)
        {
            if (string.IsNullOrWhiteSpace(normalizedThickness))
                return string.Empty;

            var text = normalizedThickness.Trim();
            if (text.EndsWith("mm", StringComparison.OrdinalIgnoreCase))
            {
                text = text.Substring(0, text.Length - 2).TrimEnd();
                return text + "mm";
            }

            if (!text.EndsWith("mm", StringComparison.OrdinalIgnoreCase)
                && double.TryParse(text, NumberStyles.Any, CultureInfo.GetCultureInfo("de-DE"), out _))
            {
                return text + "mm";
            }

            return text;
        }

        private static bool IsAluminum(string upper, string raw)
        {
            if (upper.IndexOf("ALUMIN", StringComparison.Ordinal) >= 0
                || upper.IndexOf("ALUMINIUM", StringComparison.Ordinal) >= 0
                || upper.IndexOf("ALU-", StringComparison.Ordinal) >= 0
                || Regex.IsMatch(upper, @"\bALU\b"))
            {
                return true;
            }

            if (upper.IndexOf("ALMG", StringComparison.Ordinal) >= 0
                || upper.IndexOf("ALMN", StringComparison.Ordinal) >= 0
                || upper.IndexOf("ALSI", StringComparison.Ordinal) >= 0
                || upper.IndexOf("EN AW", StringComparison.Ordinal) >= 0
                || upper.IndexOf("EN-AW", StringComparison.Ordinal) >= 0
                || upper.IndexOf("ENAW", StringComparison.Ordinal) >= 0)
            {
                return true;
            }

            if (Regex.IsMatch(upper, @"\b(6061|6008|5083|5754|7075|1050|3003|3105)(?:[\-\s]*T\d+)?\b"))
                return true;

            return EnAluminumNumberRegex.IsMatch(raw);
        }

        private static bool IsStainlessSteel(string upper, string raw)
        {
            if (upper.IndexOf("EDELSTAHL", StringComparison.Ordinal) >= 0
                || upper.IndexOf("STAINLESS", StringComparison.Ordinal) >= 0
                || upper.IndexOf("INOX", StringComparison.Ordinal) >= 0
                || upper.IndexOf("NIROSTA", StringComparison.Ordinal) >= 0
                || Regex.IsMatch(upper, @"\bV[24]A\b")
                || Regex.IsMatch(upper, @"\bVA\b"))
            {
                return true;
            }

            if (Regex.IsMatch(upper, @"\bX\d+CRNI", RegexOptions.IgnoreCase)
                || Regex.IsMatch(upper, @"\bX\d+CRNIMO", RegexOptions.IgnoreCase)
                || Regex.IsMatch(upper, @"\bAISI\s*(304|316|321|430)\b")
                || Regex.IsMatch(upper, @"\b(304L?|316L?|321|430)\b"))
            {
                return true;
            }

            if (EnStainlessNumberRegex.IsMatch(raw))
            {
                var match = EnStainlessNumberRegex.Match(raw);
                if (match.Success)
                {
                    var digits = match.Groups[1].Value;
                    if (digits.StartsWith("4", StringComparison.Ordinal))
                        return true;
                }
            }

            return false;
        }

        private static bool IsCarbonSteel(string upper, string raw)
        {
            if (Regex.IsMatch(upper, @"\bDC0?[0-7]\b")
                || Regex.IsMatch(upper, @"\bHCT\d")
                || Regex.IsMatch(upper, @"\bCR\d")
                || Regex.IsMatch(upper, @"\bS235[A-Z0-9]*\b")
                || Regex.IsMatch(upper, @"\bS355[A-Z0-9]*\b")
                || Regex.IsMatch(upper, @"\bS420[A-Z0-9]*\b")
                || Regex.IsMatch(upper, @"\bZE\d")
                || Regex.IsMatch(upper, @"\bZSTE\d")
                || Regex.IsMatch(upper, @"\bST37\b")
                || Regex.IsMatch(upper, @"\bST52\b"))
            {
                return true;
            }

            return EnSteelNumberRegex.IsMatch(raw);
        }
    }
}
