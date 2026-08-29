using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace PDF_umbenenen.Services
{
    internal sealed class PdfSurfaceTreatmentInfo
    {
        public bool HasSurfaceTreatmentEntry { get; set; }

        public bool HasPowderCoating { get; set; }

        public bool HasAnodizing { get; set; }

        public string RalCode { get; set; } = string.Empty;

        public string SurfaceTreatmentText { get; set; } = string.Empty;
    }

    /// <summary>
    /// Liest Oberflächenbehandlung aus dem Schriftfeld (z. B. RAL 9003, Eloxierung).
    /// </summary>
    internal static class PdfSurfaceTreatmentExtractionService
    {
        private static readonly string[] SurfaceTreatmentLabels =
        {
            "OBERFLAECHENBEHANDLUNG",
            "OBERFLÄCHENBEHANDLUNG",
            "OBERFL.BEHANDLUNG",
            "OBERFL BEHANDLUNG",
            "OBERFLAECHEN BEHANDLUNG",
            "OBERFLAECHENBEHANDL",
            "SURFACE TREATMENT",
            "SURFACE FINISH",
            "FINISH",
            "COATING"
        };

        private static readonly Regex RalCodeRegex = new Regex(
            @"\bRAL\s*[-/]?\s*([0-9]{4})\b",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

        private static readonly Regex RalCodeLooseRegex = new Regex(
            @"\bRAL[^0-9A-Z]{0,12}([0-9OIl]{4})\b",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

        private static readonly Regex SurfaceSectionRegex = new Regex(
            @"(?:OBERFL[AÄ]?CHEN\s*BEHANDLUNG|SURFACE\s*TREATMENT|SURFACE\s*FINISH)\s*[:\-]?\s*([^\r\n|]{3,120})",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

        private static readonly string[] AnodizingTokens =
        {
            "ELOX", "ELOXIERT", "ELOXIERUNG", "ELOXAL", "ANODIS", "ANODIZED", "ANODISING",
            "ANODIZATION", "HARTELOX", "HARD ANOD", "ELOKTROLYTISCH"
        };

        private static readonly string[] PowderTokens =
        {
            "EINBRENNLACK", "PULVER", "POWDER", "BESCHICHTET", "LACKIERT", "COATING"
        };

        private const float TitleBlockBottomFraction = 0.42f;

        private static readonly Dictionary<string, PdfSurfaceTreatmentInfo> SurfaceCache =
            new Dictionary<string, PdfSurfaceTreatmentInfo>(StringComparer.OrdinalIgnoreCase);
        private static readonly object SurfaceCacheLock = new object();

        public static void ClearCache()
        {
            lock (SurfaceCacheLock)
                SurfaceCache.Clear();
        }

        public static PdfSurfaceTreatmentInfo Extract(string pdfPath, int pageNumber = 1)
        {
            var info = new PdfSurfaceTreatmentInfo();
            if (string.IsNullOrWhiteSpace(pdfPath) || !File.Exists(pdfPath))
                return info;

            var cacheKey = pdfPath + "|" + pageNumber + "|" + File.GetLastWriteTimeUtc(pdfPath).Ticks;
            lock (SurfaceCacheLock)
            {
                if (SurfaceCache.TryGetValue(cacheKey, out var cached))
                    return cached;
            }

            info = ExtractUncached(pdfPath, pageNumber);
            lock (SurfaceCacheLock)
                SurfaceCache[cacheKey] = info;

            return info;
        }

        private static PdfSurfaceTreatmentInfo ExtractUncached(string pdfPath, int pageNumber)
        {
            var info = new PdfSurfaceTreatmentInfo();
            var layout = PdfLayoutAnalysisService.AnalyzePage(pdfPath, pageNumber);
            var titleBlockText = BuildTitleBlockText(layout.TextRects, layout.PageWidth, layout.PageHeight);
            var pageText = PdfLayoutAnalysisService.ExtractPageText(pdfPath, pageNumber);
            var rectText = BuildRectSearchText(layout.TextRects);

            // Metadaten nur aus Schriftfeld-/Seitenkontext – RAL nie seitenweit raten.
            foreach (var source in new[] { titleBlockText, pageText })
                TryApplySurfaceMetadata(info, source);

            // RAL nur im Oberflächenabschnitt oder nahe am Label – nicht aus ganzem Seitenteil.
            TryApplyRalCode(info, ExtractSurfaceSection(Normalize(titleBlockText)), allowStandalonePowderNumber: true);
            if (string.IsNullOrWhiteSpace(info.RalCode))
                TryApplyRalCode(info, ExtractSurfaceSection(Normalize(pageText)), allowStandalonePowderNumber: true);

            TryApplyRalFromTitleBlockRects(info, layout.TextRects, layout.PageWidth, layout.PageHeight);

            if (string.IsNullOrWhiteSpace(info.RalCode) && info.HasSurfaceTreatmentEntry)
            {
                var ocrText = ReadOcrFallback(pdfPath, pageNumber);
                if (!string.IsNullOrWhiteSpace(ocrText))
                {
                    TryApplySurfaceMetadata(info, ocrText);
                    TryApplyRalCode(info, ExtractSurfaceSection(Normalize(ocrText)), allowStandalonePowderNumber: true);
                }
            }

            if (!string.IsNullOrWhiteSpace(info.RalCode) && IsPlausibleRalCode(info.RalCode))
            {
                info.HasPowderCoating = true;
                info.HasSurfaceTreatmentEntry = true;
                if (string.IsNullOrWhiteSpace(info.SurfaceTreatmentText))
                    info.SurfaceTreatmentText = "RAL " + info.RalCode;
            }
            else
            {
                // Ohne gültigen RAL-Code kein Pulver-Wasserzeichen erfinden.
                info.RalCode = string.Empty;
                if (!ContainsAnyToken(info.SurfaceTreatmentText, PowderTokens)
                    && !ContainsAnyToken(info.SurfaceTreatmentText, AnodizingTokens))
                {
                    info.HasPowderCoating = false;
                }
            }

            return info;
        }

        private static string ReadOcrFallback(string pdfPath, int pageNumber)
        {
            var ocrText = TitleBlockOcrService.TryReadTitleBlock(pdfPath, pageNumber) ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(ocrText))
                return ocrText;

            return TitleBlockOcrService.TryReadTitleBlockThorough(pdfPath, pageNumber) ?? string.Empty;
        }

        private static void TryApplySurfaceMetadata(PdfSurfaceTreatmentInfo info, string text)
        {
            if (info == null || string.IsNullOrWhiteSpace(text))
                return;

            var normalized = Normalize(text);
            var section = ExtractSurfaceSection(normalized);
            if (string.IsNullOrWhiteSpace(section) || !HasMeaningfulSurfaceContent(section))
                return;

            info.HasSurfaceTreatmentEntry = true;
            if (string.IsNullOrWhiteSpace(info.SurfaceTreatmentText))
                info.SurfaceTreatmentText = section.Trim();

            if (string.IsNullOrWhiteSpace(info.RalCode))
                TryApplyRalCode(info, section, allowStandalonePowderNumber: true);

            if (string.IsNullOrWhiteSpace(info.RalCode))
            {
                var standalone = ExtractStandaloneSurfaceRalNumber(section);
                if (!string.IsNullOrWhiteSpace(standalone))
                    info.RalCode = standalone;
            }

            info.HasPowderCoating = info.HasPowderCoating
                || !string.IsNullOrWhiteSpace(info.RalCode)
                || ContainsAnyToken(section, PowderTokens);

            info.HasAnodizing = info.HasAnodizing || ContainsAnyToken(section, AnodizingTokens);
        }

        private static void TryApplyRalCode(
            PdfSurfaceTreatmentInfo info,
            string text,
            bool allowStandalonePowderNumber = false)
        {
            if (info == null || string.IsNullOrWhiteSpace(text) || !string.IsNullOrWhiteSpace(info.RalCode))
                return;

            var code = ExtractRalCode(text, allowStandalonePowderNumber);
            if (string.IsNullOrWhiteSpace(code))
                return;

            info.RalCode = code;
            info.HasPowderCoating = true;
            info.HasSurfaceTreatmentEntry = true;
        }

        private static void TryApplyRalFromTitleBlockRects(
            PdfSurfaceTreatmentInfo info,
            IList<PdfTextRect> rects,
            float pageWidth,
            float pageHeight)
        {
            if (info == null || !string.IsNullOrWhiteSpace(info.RalCode) || rects == null || rects.Count == 0)
                return;

            var bottomLimit = pageHeight * TitleBlockBottomFraction;
            var titleRects = rects
                .Where(r => r != null && r.Bottom <= bottomLimit && !string.IsNullOrWhiteSpace(r.Text))
                .ToList();

            if (titleRects.Count == 0)
                return;

            var surfaceRects = titleRects
                .Where(r => ContainsAnyToken(r.Text, SurfaceTreatmentLabels)
                            || SurfaceSectionRegex.IsMatch(Normalize(r.Text)))
                .ToList();

            // Nur in/nahe der Oberflächenzeile suchen – nie den ganzen Schriftfeld-Block.
            if (surfaceRects.Count == 0)
                return;

            var searchRects = titleRects
                .Where(r => IsNearSurfaceTreatmentArea(r, surfaceRects, pageWidth, pageHeight)
                            || ContainsAnyToken(r.Text, SurfaceTreatmentLabels)
                            || (r.Text ?? string.Empty).IndexOf("RAL", StringComparison.OrdinalIgnoreCase) >= 0)
                .ToList();

            foreach (var rect in searchRects)
            {
                var text = (rect.Text ?? string.Empty).Trim();
                if (string.IsNullOrWhiteSpace(text))
                    continue;

                if (IsAppGeneratedRalWatermark(text)
                    && !ContainsAnyToken(text, PowderTokens)
                    && surfaceRects.All(s =>
                        s == null
                        || string.IsNullOrWhiteSpace(s.Text)
                        || !ContainsAnyToken(s.Text, PowderTokens)))
                {
                    // Reines „RAL 1234“ ohne Lack-/Pulver-Kontext: oft Wasserzeichen vom letzten Lauf.
                    if (!titleRects.Any(r =>
                            r != null
                            && !string.IsNullOrWhiteSpace(r.Text)
                            && ContainsAnyToken(r.Text, PowderTokens)
                            && IsNearSurfaceTreatmentArea(r, surfaceRects, pageWidth, pageHeight)))
                    {
                        continue;
                    }
                }

                var code = ExtractRalCode(text, allowStandalonePowderNumber: false);
                if (!string.IsNullOrWhiteSpace(code))
                {
                    info.RalCode = code;
                    info.HasPowderCoating = true;
                    info.HasSurfaceTreatmentEntry = true;
                    return;
                }

                if (!IsStandaloneRalCandidate(text))
                    continue;

                if (!IsNearSurfaceTreatmentArea(rect, surfaceRects, pageWidth, pageHeight))
                    continue;

                // Vierstellige Zahl allein reicht nicht – nur mit Pulver-/RAL-Kontext in der Nähe.
                var nearPowder = titleRects.Any(r =>
                    r != null
                    && r != rect
                    && !string.IsNullOrWhiteSpace(r.Text)
                    && (ContainsAnyToken(r.Text, PowderTokens)
                        || r.Text.IndexOf("RAL", StringComparison.OrdinalIgnoreCase) >= 0)
                    && IsNearSurfaceTreatmentArea(r, surfaceRects, pageWidth, pageHeight));

                if (!nearPowder)
                    continue;

                info.RalCode = text;
                info.HasPowderCoating = true;
                info.HasSurfaceTreatmentEntry = true;
                return;
            }
        }

        private static bool IsAppGeneratedRalWatermark(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return false;

            var trimmed = text.Trim();
            return Regex.IsMatch(trimmed, @"^RAL\s*[0-9]{4}$", RegexOptions.IgnoreCase);
        }

        private static string ExtractStandaloneSurfaceRalNumber(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return string.Empty;

            var trimmed = text.Trim();
            if (IsStandaloneRalCandidate(trimmed))
                return trimmed;

            if (trimmed.Length > 48)
                return string.Empty;

            var match = Regex.Match(trimmed, @"(?:^|\s)([1-9][0-9]{3})(?:\s|$)");
            if (!match.Success)
                return string.Empty;

            var tail = trimmed.Substring(match.Index + match.Length).TrimStart();
            if (tail.StartsWith("µ", StringComparison.Ordinal)
                || tail.StartsWith("um", StringComparison.OrdinalIgnoreCase)
                || tail.StartsWith("MM", StringComparison.OrdinalIgnoreCase))
            {
                return string.Empty;
            }

            return IsPlausibleRalCode(match.Groups[1].Value) ? match.Groups[1].Value : string.Empty;
        }

        private static bool IsStandaloneRalCandidate(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return false;

            var trimmed = text.Trim();
            return Regex.IsMatch(trimmed, @"^[1-9][0-9]{3}$");
        }

        private static bool IsNearSurfaceTreatmentArea(
            PdfTextRect candidate,
            IList<PdfTextRect> surfaceRects,
            float pageWidth,
            float pageHeight)
        {
            if (candidate == null || surfaceRects == null || surfaceRects.Count == 0)
                return false;

            var toleranceX = Math.Max(120f, pageWidth * 0.12f);
            var toleranceY = Math.Max(48f, pageHeight * 0.05f);

            foreach (var surface in surfaceRects)
            {
                if (surface == null)
                    continue;

                var dx = Math.Abs(candidate.Left - surface.Left);
                var dy = Math.Abs(candidate.Bottom - surface.Bottom);
                if (dx <= toleranceX && dy <= toleranceY)
                    return true;
            }

            return false;
        }

        private static string ExtractRalCode(string text, bool allowStandalonePowderNumber = false)
        {
            if (string.IsNullOrWhiteSpace(text))
                return string.Empty;

            var normalized = Normalize(text);
            foreach (var regex in new[] { RalCodeRegex, RalCodeLooseRegex })
            {
                var match = regex.Match(normalized);
                while (match.Success)
                {
                    var code = NormalizeRalDigits(match.Groups[1].Value);
                    if (IsPlausibleRalCode(code))
                        return code;

                    match = match.NextMatch();
                }
            }

            if (!allowStandalonePowderNumber)
                return string.Empty;

            return ExtractStandaloneRalInPowderContext(normalized);
        }

        private static string ExtractStandaloneRalInPowderContext(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return string.Empty;

            var hasPowderContext = ContainsAnyToken(text, PowderTokens)
                || text.IndexOf("RAL", StringComparison.OrdinalIgnoreCase) >= 0
                || text.IndexOf("LACK", StringComparison.OrdinalIgnoreCase) >= 0;

            if (!hasPowderContext)
                return string.Empty;

            var matches = Regex.Matches(text, @"\b([1-9][0-9]{3})\b");
            foreach (Match match in matches)
            {
                var code = NormalizeRalDigits(match.Groups[1].Value);
                if (!IsPlausibleRalCode(code))
                    continue;

                var end = match.Index + match.Length;
                var tail = end < text.Length
                    ? text.Substring(end, Math.Min(8, text.Length - end)).TrimStart()
                    : string.Empty;
                if (tail.StartsWith("µ", StringComparison.Ordinal)
                    || tail.StartsWith("um", StringComparison.OrdinalIgnoreCase)
                    || tail.StartsWith("MM", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                return code;
            }

            return string.Empty;
        }

        private static string NormalizeRalDigits(string digits)
        {
            if (string.IsNullOrWhiteSpace(digits))
                return string.Empty;

            return digits
                .Replace('O', '0')
                .Replace('o', '0')
                .Replace('I', '1')
                .Replace('l', '1');
        }

        private static bool IsPlausibleRalCode(string code)
        {
            if (string.IsNullOrWhiteSpace(code) || code.Length != 4)
                return false;

            return code.All(char.IsDigit);
        }

        private static string BuildRectSearchText(IList<PdfTextRect> rects)
        {
            if (rects == null || rects.Count == 0)
                return string.Empty;

            return string.Join(" ", rects
                .Where(r => r != null && !string.IsNullOrWhiteSpace(r.Text))
                .Select(r => r.Text.Trim()));
        }

        private static string ExtractSurfaceSection(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return string.Empty;

            var match = SurfaceSectionRegex.Match(text);
            if (match.Success)
                return CleanupSection(match.Groups[1].Value);

            foreach (var label in SurfaceTreatmentLabels)
            {
                var idx = text.IndexOf(label, StringComparison.OrdinalIgnoreCase);
                if (idx < 0)
                    continue;

                var start = idx + label.Length;
                if (start >= text.Length)
                    return string.Empty;

                var slice = text.Substring(start, Math.Min(140, text.Length - start));
                return CleanupSection(slice);
            }

            return string.Empty;
        }

        private static string CleanupSection(string section)
        {
            if (string.IsNullOrWhiteSpace(section))
                return string.Empty;

            var cleaned = section.Trim(' ', ':', '.', '-', '\t', '|', ';');
            cleaned = Regex.Replace(cleaned, @"\s{2,}", " ").Trim();

            var stopIdx = cleaned.IndexOf("ZULAESSIGE", StringComparison.OrdinalIgnoreCase);
            if (stopIdx < 0)
                stopIdx = cleaned.IndexOf("ZULÄSSIGE", StringComparison.OrdinalIgnoreCase);
            if (stopIdx < 0)
                stopIdx = cleaned.IndexOf("ROUGH", StringComparison.OrdinalIgnoreCase);
            if (stopIdx > 8)
                cleaned = cleaned.Substring(0, stopIdx).Trim();

            return cleaned;
        }

        private static bool HasMeaningfulSurfaceContent(string section)
        {
            if (string.IsNullOrWhiteSpace(section))
                return false;

            if (RalCodeRegex.IsMatch(section) || RalCodeLooseRegex.IsMatch(section))
                return true;

            if (ContainsAnyToken(section, PowderTokens) || ContainsAnyToken(section, AnodizingTokens))
                return true;

            // Keine bloße 4-stellige Zahl / DIN-Text als Oberflächenbehandlung werten.
            return false;
        }

        private static bool IsLabelOnly(string section)
        {
            var upper = section.ToUpperInvariant();
            return SurfaceTreatmentLabels.Any(label => upper.Equals(label, StringComparison.Ordinal))
                || upper.Equals("OBERFLAECHENBEHANDLUNG", StringComparison.Ordinal);
        }

        private static bool ContainsAnyToken(string text, IEnumerable<string> tokens)
        {
            if (string.IsNullOrWhiteSpace(text))
                return false;

            foreach (var token in tokens)
            {
                if (text.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            }

            return false;
        }

        private static string BuildTitleBlockText(IList<PdfTextRect> rects, float pageWidth, float pageHeight)
        {
            if (rects == null || rects.Count == 0)
                return string.Empty;

            var bottomLimit = pageHeight * TitleBlockBottomFraction;
            var titleRects = rects
                .Where(r => r != null && r.Bottom <= bottomLimit)
                .OrderByDescending(r => r.Top)
                .ThenBy(r => r.Left)
                .ToList();

            return string.Join(" ", titleRects
                .Select(r => (r.Text ?? string.Empty).Trim())
                .Where(t => !string.IsNullOrWhiteSpace(t)));
        }

        private static string Normalize(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return string.Empty;

            return text
                .Replace('\r', ' ')
                .Replace('\n', ' ')
                .Replace('\t', ' ')
                .Replace("Ä", "AE")
                .Replace("ä", "ae")
                .Replace("Ö", "OE")
                .Replace("ö", "oe")
                .Replace("Ü", "UE")
                .Replace("ü", "ue")
                .Replace("ß", "ss");
        }
    }
}
