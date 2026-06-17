using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace PDF_umbenenen.Services
{
    internal sealed class PdfMaterialInfo
    {
        public string MaterialType { get; set; }

        public string ThicknessText { get; set; }
    }

    /// <summary>
    /// Liest Materialart und Blechstärke aus dem PDF-Schriftfeld (Seite 1).
    /// </summary>
    internal static class PdfMaterialExtractionService
    {
        private static readonly string[] MaterialLabels =
        {
            "RAW MATERIAL", "MATERIAL TYPE", "MATERIAL NR", "MATERIAL-NR", "MATERIAL",
            "MATL.", "MATL", "MAT'L", "MAT.", "MAT", "WERKSTOFF-NR", "WERKSTOFFNR", "WERKSTOFF"
        };

        private static readonly string[] ThicknessLabels =
        {
            "SHEET THK", "SHT THK", "MATERIAL THK", "MATERIAL THICKNESS", "THICKNESS",
            "THK.", "THK", "GAUGE", "GAGE", "BLECHDICKE", "STAERKE", "STÄRKE", "DICKE"
        };

        private static readonly Regex MaterialInlineRegex = new Regex(
            @"(?:Material|MATL\.?|MAT'L|Werkstoff)\s*[:\-]?\s*([A-Z0-9][A-Z0-9\.\+\-/]{1,24})",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

        private static readonly Regex ThicknessInlineRegex = new Regex(
            @"(?:Thickness|THK\.?|THK|Dicke|Blechdicke|Stärke|Staerke|Gauge|Gage)\s*[:\-]?\s*(\d+[.,]\d+|\d+)\s*(?:mm|MM)?",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

        private static readonly Regex ThicknessShortRegex = new Regex(
            @"\bt\s*[=:\-]?\s*(\d+[.,]\d+|\d+)\s*(?:mm|MM)?\b",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

        private static readonly Regex MaterialGradeRegex = new Regex(
            @"\b(DC0[0-9]|DC[1-9][0-9]|CR[0-9]|S235[A-Z0-9]*|S355[A-Z0-9]*|S420[A-Z0-9]*|HCT\d{3}X|ZE\d+/[\d]+|ZSTE\d+|1\.\d{4}[A-Z0-9]*|AlMg[0-9]+|EN\s*AW[\-\s]*[0-9]+(?:[A-Z0-9\-]+)?|6061[\-\s]*T[0-9]+|6008[\-\s]*T[0-9]+|HC\d{3}Y?)\b",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

        private static readonly Regex ThicknessValueRegex = new Regex(
            @"\b(\d+[.,]\d{1,2}|\d{1,2})\s*(?:mm|MM)\b",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

        private static readonly Regex LabelOnlyRegex = new Regex(
            @"^(Material|MATL\.?|MAT'L|MAT\.?|MAT|Werkstoff|Thickness|THK\.?|THK|Dicke|Blechdicke|Stärke|Staerke|Gauge|Gage|SHEET THK|SHT THK)$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

        private static readonly string[] StopWords =
        {
            "SCALE", "SHEET", "REV", "REVISION", "DATE", "DRAWN", "CHECKED", "SIZE", "FORMAT",
            "A0", "A1", "A2", "A3", "A4", "TITLE", "DESCRIPTION", "PROJECT", "MASS", "WEIGHT",
            "TESLA", "AUTOMATION", "UNLESS", "NOTED", "FINISH", "COATING", "TOLERANCE", "GENERAL"
        };

        public static PdfMaterialInfo Extract(string pdfPath, int pageNumber = 1)
        {
            var info = new PdfMaterialInfo();
            if (string.IsNullOrWhiteSpace(pdfPath) || !File.Exists(pdfPath))
                return info;

            var layout = PdfLayoutAnalysisService.AnalyzePage(pdfPath, pageNumber);
            var pageText = PdfLayoutAnalysisService.ExtractPageText(pdfPath, pageNumber);
            var rects = layout.TextRects ?? new List<PdfTextRect>();

            var titleBlockRects = FilterTitleBlockRects(rects, layout.PageWidth, layout.PageHeight);
            var titleBlockText = BuildTitleBlockText(titleBlockRects);
            var searchTexts = new[]
            {
                titleBlockText,
                pageText,
                BuildSearchText(rects)
            };

            var material = ExtractLabeledValue(searchTexts, MaterialLabels, IsPlausibleMaterial);
            var thickness = ExtractLabeledValue(searchTexts, ThicknessLabels, IsPlausibleThicknessRaw);

            if (string.IsNullOrWhiteSpace(material))
            {
                foreach (var text in searchTexts.Where(t => !string.IsNullOrWhiteSpace(t)))
                {
                    var match = MaterialInlineRegex.Match(text);
                    if (match.Success && IsPlausibleMaterial(match.Groups[1].Value))
                    {
                        material = match.Groups[1].Value.Trim();
                        break;
                    }
                }
            }

            if (string.IsNullOrWhiteSpace(material) && !string.IsNullOrWhiteSpace(titleBlockText))
            {
                var match = MaterialGradeRegex.Match(titleBlockText);
                if (match.Success)
                    material = match.Value.Trim();
            }

            if (string.IsNullOrWhiteSpace(thickness))
            {
                foreach (var text in searchTexts.Where(t => !string.IsNullOrWhiteSpace(t)))
                {
                    var match = ThicknessInlineRegex.Match(text);
                    if (match.Success)
                    {
                        thickness = match.Groups[1].Value.Trim();
                        break;
                    }

                    match = ThicknessShortRegex.Match(text);
                    if (match.Success)
                    {
                        thickness = match.Groups[1].Value.Trim();
                        break;
                    }
                }
            }

            if (string.IsNullOrWhiteSpace(thickness) && !string.IsNullOrWhiteSpace(titleBlockText))
            {
                var match = ThicknessValueRegex.Match(titleBlockText);
                if (match.Success)
                    thickness = match.Groups[1].Value.Trim();
            }

            if (string.IsNullOrWhiteSpace(material))
                material = FindValueNearLabel(titleBlockRects, MaterialLabels, IsPlausibleMaterial);

            if (string.IsNullOrWhiteSpace(thickness))
                thickness = FindValueNearLabel(titleBlockRects, ThicknessLabels, IsPlausibleThicknessRaw);

            if (IsPlausibleMaterial(material))
                info.MaterialType = material.Trim();

            var normalizedThickness = NormalizeThickness(thickness);
            if (!string.IsNullOrWhiteSpace(normalizedThickness))
                info.ThicknessText = normalizedThickness;

            return info;
        }

        private static List<PdfTextRect> FilterTitleBlockRects(IList<PdfTextRect> rects, float pageWidth, float pageHeight)
        {
            if (rects == null || rects.Count == 0)
                return new List<PdfTextRect>();

            var topLimit = pageHeight * 0.30f;
            var leftLimit = pageWidth * 0.40f;

            return rects
                .Where(r => r.Bottom <= topLimit && r.Right >= leftLimit)
                .ToList();
        }

        private static string BuildTitleBlockText(IList<PdfTextRect> rects)
        {
            if (rects == null || rects.Count == 0)
                return string.Empty;

            var ordered = rects
                .OrderByDescending(r => r.Bottom)
                .ThenBy(r => r.Left)
                .ToList();

            var builder = new StringBuilder();
            foreach (var rect in ordered)
            {
                var text = NormalizeToken(rect.Text);
                if (string.IsNullOrWhiteSpace(text))
                    continue;

                if (builder.Length > 0)
                    builder.Append(' ');
                builder.Append(text);
            }

            return builder.ToString();
        }

        private static string ExtractLabeledValue(IEnumerable<string> texts, string[] labels, Func<string, bool> validator)
        {
            foreach (var source in texts)
            {
                if (string.IsNullOrWhiteSpace(source))
                    continue;

                foreach (var rawLine in source.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    var line = NormalizeToken(rawLine);
                    if (string.IsNullOrWhiteSpace(line))
                        continue;

                    foreach (var label in labels)
                    {
                        if (line.Length <= label.Length)
                            continue;

                        if (line.StartsWith(label, StringComparison.OrdinalIgnoreCase))
                        {
                            var value = line.Substring(label.Length).Trim(' ', ':', '.', '-', '\t');
                            if (validator(value))
                                return value;
                        }

                        var marker = " " + label + " ";
                        var idx = line.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
                        if (idx >= 0)
                        {
                            var value = line.Substring(idx + marker.Length).Trim(' ', ':', '.', '-', '\t');
                            if (validator(value))
                                return value;
                        }
                    }
                }
            }

            return string.Empty;
        }

        private static string FindValueNearLabel(
            IList<PdfTextRect> rects,
            string[] labels,
            Func<string, bool> validator)
        {
            if (rects == null || rects.Count == 0)
                return string.Empty;

            foreach (var labelRect in rects)
            {
                var labelText = NormalizeToken(labelRect.Text);
                if (string.IsNullOrWhiteSpace(labelText))
                    continue;

                if (!labels.Any(label => labelText.Equals(label, StringComparison.OrdinalIgnoreCase)
                                         || labelText.StartsWith(label + " ", StringComparison.OrdinalIgnoreCase)
                                         || labelText.StartsWith(label + ":", StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                var inline = labels
                    .Select(label => TryParseInlineLabelValue(labelText, label, validator))
                    .FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));
                if (!string.IsNullOrWhiteSpace(inline))
                    return inline;

                var adjacent = FindAdjacentValue(rects, labelRect, labels, validator);
                if (!string.IsNullOrWhiteSpace(adjacent))
                    return adjacent;
            }

            return string.Empty;
        }

        private static string TryParseInlineLabelValue(string labelText, string label, Func<string, bool> validator)
        {
            if (!labelText.StartsWith(label, StringComparison.OrdinalIgnoreCase))
                return string.Empty;

            var value = labelText.Substring(label.Length).Trim(' ', ':', '.', '-', '\t');
            return validator(value) ? value : string.Empty;
        }

        private static string BuildSearchText(IEnumerable<PdfTextRect> rects)
        {
            if (rects == null)
                return string.Empty;

            var builder = new StringBuilder();
            foreach (var rect in rects)
            {
                var text = NormalizeToken(rect.Text);
                if (string.IsNullOrWhiteSpace(text))
                    continue;

                if (builder.Length > 0)
                    builder.Append(' ');
                builder.Append(text);
            }

            return builder.ToString();
        }

        private static string FindAdjacentValue(
            IList<PdfTextRect> rects,
            PdfTextRect label,
            string[] labels,
            Func<string, bool> validator)
        {
            PdfTextRect best = null;
            var bestScore = float.MaxValue;

            foreach (var rect in rects)
            {
                if (ReferenceEquals(rect, label))
                    continue;

                var text = NormalizeToken(rect.Text);
                if (string.IsNullOrWhiteSpace(text))
                    continue;

                if (labels.Any(l => text.Equals(l, StringComparison.OrdinalIgnoreCase)))
                    continue;

                if (!validator(text))
                    continue;

                if (rect.Left >= label.Right - 15f && rect.Left <= label.Right + 320f)
                {
                    var overlap = VerticalOverlap(label, rect);
                    if (overlap > -2f)
                    {
                        var score = rect.Left - label.Right;
                        if (score < bestScore)
                        {
                            bestScore = score;
                            best = rect;
                        }
                    }
                }

                if (rect.Top <= label.Bottom + 12f && rect.Bottom >= label.Bottom - 45f
                    && rect.Left >= label.Left - 35f && rect.Left <= label.Right + 320f)
                {
                    var score = (label.Bottom - rect.Top) + 600f;
                    if (score < bestScore)
                    {
                        bestScore = score;
                        best = rect;
                    }
                }
            }

            return best == null ? string.Empty : NormalizeToken(best.Text);
        }

        private static float VerticalOverlap(PdfTextRect a, PdfTextRect b)
        {
            return Math.Min(a.Top, b.Top) - Math.Max(a.Bottom, b.Bottom);
        }

        private static string NormalizeToken(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;

            return Regex.Replace(value.Trim(), @"\s+", " ");
        }

        private static bool IsPlausibleMaterial(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return false;

            var text = NormalizeToken(value);
            if (text.Length < 2 || text.Length > 28)
                return false;

            if (LabelOnlyRegex.IsMatch(text))
                return false;

            var upper = text.ToUpperInvariant();
            if (StopWords.Any(word => upper == word || upper.StartsWith(word + " ", StringComparison.Ordinal)))
                return false;

            if (Regex.IsMatch(text, @"^\d+[.,]?\d*$"))
                return false;

            if (Regex.IsMatch(text, @"^[A-Z]{1,4}\d{2,6}-\d", RegexOptions.IgnoreCase))
                return false;

            if (MaterialGradeRegex.IsMatch(text))
                return true;

            return Regex.IsMatch(text, @"[A-Za-z]");
        }

        private static bool IsPlausibleThicknessRaw(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return false;

            var text = Regex.Replace(value.Trim(), @"mm", string.Empty, RegexOptions.IgnoreCase).Trim();
            double parsed;
            return double.TryParse(text, NumberStyles.Any, CultureInfo.GetCultureInfo("de-DE"), out parsed)
                   || double.TryParse(text, NumberStyles.Any, CultureInfo.InvariantCulture, out parsed);
        }

        private static string NormalizeThickness(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
                return string.Empty;

            var text = Regex.Replace(raw.Trim(), @"mm", string.Empty, RegexOptions.IgnoreCase).Trim();

            double value;
            if (!double.TryParse(text, NumberStyles.Any, CultureInfo.GetCultureInfo("de-DE"), out value)
                && !double.TryParse(text, NumberStyles.Any, CultureInfo.InvariantCulture, out value))
            {
                return string.Empty;
            }

            if (value <= 0 || value > 200)
                return string.Empty;

            return value.ToString("0.0", CultureInfo.GetCultureInfo("de-DE")) + " mm";
        }
    }
}
