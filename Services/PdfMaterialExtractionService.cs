using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using PDF_umbenenen.Models;
using PDF_umbenenen.Services.Ai;

namespace PDF_umbenenen.Services
{
    internal sealed class PdfMaterialInfo
    {
        public string MaterialType { get; set; }

        public string ThicknessText { get; set; }

        /// <summary>ST, VA oder ALU – aus Werkstoffnummer abgeleitet.</summary>
        public string MaterialAlloyPrefix { get; set; }

        /// <summary>Kombiniertes Wasserzeichen, z. B. ST-2,0mm.</summary>
        public string MaterialWatermarkText { get; set; }
    }

    /// <summary>
    /// Liest Materialart und Blechstärke aus Beschriftung, Schriftfeld oder Seitenansicht.
    /// </summary>
    internal static class PdfMaterialExtractionService
    {
        private static readonly string[] MaterialLabels =
        {
            "RAW MATERIAL", "MATERIAL TYPE", "MATERIAL NR", "MATERIAL-NR", "MATERIAL", "MAT.",
            "MATL.", "MATL", "MAT'L", "MAT.", "MAT", "WERKSTOFF-NR", "WERKSTOFFNR", "WERKSTOFF"
        };

        private static readonly string[] MaterialLabelsOrdered =
        {
            "RAW MATERIAL", "MATERIAL TYPE", "MATERIAL NR", "MATERIAL-NR", "WERKSTOFF-NR", "WERKSTOFFNR",
            "WERKSTOFF", "MATERIAL", "MATL.", "MAT'L", "MAT.", "MATL", "MAT"
        };

        private static readonly Regex ScaleRegex = new Regex(
            @"(?:MASSSTAB|MAßSTAB|SCALE)\s*1\s*:\s*(\d+)",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

        private static readonly string[] ThicknessLabels =
        {
            "MATERIAL THICKNESS", "MATERIAL THK", "MATL THK", "MAT'L THK", "MAT THK",
            "SHEET THK", "SHT THK", "SHEET THICKNESS", "PLATE THK", "BLANK THK",
            "PART THK", "NOM. THK", "NOM THK", "THK (MM)", "THK(MM)", "THK MM",
            "THICKNESS", "THK.", "THK", "GAUGE", "GAGE",
            "BLECHDICKE", "STAERKE", "STÄRKE", "DICKE"
        };

        private static readonly Regex MaterialInlineRegex = new Regex(
            @"(?:Material|MATL\.?|MAT'L|MAT\.?|Werkstoff)\s*[:\-]?\s*(\d\.\d{4}\s*/\s*[A-Z0-9][A-Z0-9./+\-\s]{0,20}|[A-Z0-9][A-Z0-9\.\+\-/]{1,24})",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

        private static readonly Regex ThicknessInlineRegex = new Regex(
            @"(?:Thickness|THK\.?|THK|Dicke|Blechdicke|Stärke|Staerke|Gauge|Gage|Material\s*Thk)\s*[:\-]?\s*(\d+[.,]\d+|\d+)\s*(?:mm|MM)?",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

        private static readonly Regex ThicknessShortRegex = new Regex(
            @"\b(?:t|thk)\s*[=:\-]?\s*(\d+[.,]\d+|\d+)\s*(?:mm|MM)?\b",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

        private static readonly Regex ThicknessGluedRegex = new Regex(
            @"(?:THK\.?|THICKNESS)\s*(\d+[.,]\d+|\d+)\s*(?:mm|MM)?",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

        private static readonly Regex MatFieldRegex = new Regex(
            @"(?:MAT\.?|MATERIAL)\s*[:\-]?\s*([^\r\n;|]+)",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

        private static readonly Regex WerkstoffComboRegex = new Regex(
            @"\b(\d\.\d{4}\s*/\s*[A-Z0-9][A-Z0-9./+\-\s]*)\b",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

        private static readonly Regex MaterialGradeRegex = new Regex(
            @"\b(DC0[0-9]|DC[1-9][0-9]|DX5[0-9]D|CR[0-9]{1,2}|HC\d{3}[A-Z]?|HX\d{3}LAD|HCT\d{3}X|ZE\d+/[\d]+|ZSTE\d+|S235[A-Z0-9]*|S355[A-Z0-9]*|S420[A-Z0-9]*|1\.\d{4}[A-Z0-9]*|AlMg[0-9]+|EN\s*AW[\-\s]*[0-9]+(?:[A-Z0-9\-]+)?|6061[\-\s]*T[0-9]+|6008[\-\s]*T[0-9]+|HC\d{3}Y?|X5CrNi[\d\-]+|AISI\s*316|AISI\s*304|X\dCrNi[\d\-]+)\b",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

        private static readonly Regex ThicknessValueRegex = new Regex(
            @"\b(\d+[.,]\d{1,2}|\d{1,2})\s*(?:mm|MM)\b",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

        private static readonly Regex OuterDimensionsRegex = new Regex(
            @"(?:AUSSENMA[BÄ]E?|AUSSENM\.?|OUTER\s*DIM(?:ENSIONS)?)\s*[:\-]?\s*(\d+(?:[.,]\d+)?)\s*[xX×]\s*\d",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

        private static readonly string[] AssemblyDrawingLabels =
        {
            "BAUGRUPPE", "BAUGR.", "BAUGRUPPENZEICHNUNG", "ASSEMBLY", "ASSY", "ASSY.",
            "ZUSAMMENBAU", "SUB-ASSEMBLY", "SUBASSEMBLY", "MODULE ASSY"
        };

        private static readonly string[] PartsListLabels =
        {
            "STÜCKLISTE", "STUECKLISTE", "STKLISTE", "PARTS LIST", "BILL OF MATERIAL", "BOM"
        };

        private static readonly Regex LabelOnlyRegex = new Regex(
            @"^(Material|MATL\.?|MAT'L|MAT\.?|MAT|Werkstoff|Thickness|THK\.?|THK|Dicke|Blechdicke|Stärke|Staerke|Gauge|Gage|SHEET THK|SHT THK)$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

        private static readonly Regex AppWatermarkMaterialRegex = new Regex(
            @"^(ST|VA|ALU)-\d+[.,]\d+\s*mm$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

        private static readonly Regex AppWatermarkQuantityRegex = new Regex(
            @"^\d+X$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

        private static readonly string[] StopWords =
        {
            "SCALE", "SHEET", "REV", "REVISION", "DATE", "DRAWN", "CHECKED", "SIZE", "FORMAT",
            "A0", "A1", "A2", "A3", "A4", "TITLE", "DESCRIPTION", "PROJECT", "MASS", "WEIGHT",
            "TESLA", "AUTOMATION", "UNLESS", "NOTED", "FINISH", "COATING", "TOLERANCE", "GENERAL"
        };

        private const float TitleBlockFilterFraction = 0.42f;
        private const float TitleBlockRightFraction = 0.48f;
        private const float TitleBlockRightHeightFraction = 0.58f;
        private const float MaterialLookupBottomFraction = 0.55f;
        private const float DrawingAreaBottomFraction = 0.26f;

        private static readonly Dictionary<string, PdfMaterialInfo> MaterialCache =
            new Dictionary<string, PdfMaterialInfo>(StringComparer.OrdinalIgnoreCase);
        private static readonly object MaterialCacheLock = new object();

        private static float GetPageScale(float pageWidth, float pageHeight)
        {
            return WatermarkPageScale.Get(pageWidth, pageHeight);
        }

        public static void ClearCache()
        {
            lock (MaterialCacheLock)
                MaterialCache.Clear();

            OllamaMaterialThicknessService.ClearCache();
        }

        public static PdfMaterialInfo Extract(string pdfPath, int pageNumber = 1)
        {
            if (string.IsNullOrWhiteSpace(pdfPath) || !File.Exists(pdfPath))
                return new PdfMaterialInfo();

            var cacheKey = pdfPath + "|" + pageNumber + "|" + File.GetLastWriteTimeUtc(pdfPath).Ticks;
            lock (MaterialCacheLock)
            {
                if (MaterialCache.TryGetValue(cacheKey, out var cached))
                    return cached;
            }

            var info = ExtractUncached(pdfPath, pageNumber);
            lock (MaterialCacheLock)
                MaterialCache[cacheKey] = info;

            return info;
        }

        private static PdfMaterialInfo ExtractUncached(string pdfPath, int pageNumber)
        {
            var info = new PdfMaterialInfo();
            var layout = PdfLayoutAnalysisService.AnalyzePage(pdfPath, pageNumber);
            var rects = layout.TextRects ?? new List<PdfTextRect>();
            var pageText = PdfLayoutAnalysisService.ExtractPageText(pdfPath, pageNumber);
            var rectText = BuildSearchText(rects);

            var titleBlockRects = FilterTitleBlockRects(rects, layout.PageWidth, layout.PageHeight);
            var titleBlockText = BuildTitleBlockText(titleBlockRects);
            var searchTexts = new[]
            {
                titleBlockText,
                pageText,
                rectText
            };

            var pageScale = GetPageScale(layout.PageWidth, layout.PageHeight);
            var materialLookupRects = GetMaterialLookupRects(rects, layout.PageHeight);

            var material = ResolveMaterial(
                titleBlockRects,
                materialLookupRects,
                titleBlockText,
                pageText,
                searchTexts,
                pageScale);
            var isAssembly = IsAssemblyDrawing(titleBlockText, pageText, searchTexts);
            var thickness = isAssembly
                ? string.Empty
                : ResolveThicknessCandidates(
                    pdfPath,
                    pageNumber,
                    layout,
                    rects,
                    titleBlockRects,
                    titleBlockText,
                    searchTexts,
                    material);

            if (!isAssembly && string.IsNullOrWhiteSpace(material))
            {
                var ollamaResult = OllamaMaterialThicknessService.TryExtract(pdfPath, pageNumber);
                if (ollamaResult != null)
                {
                    if (ollamaResult.IsAssembly)
                    {
                        isAssembly = true;
                        thickness = string.Empty;
                    }
                    else if (string.IsNullOrWhiteSpace(thickness) && !string.IsNullOrWhiteSpace(ollamaResult.ThicknessText))
                    {
                        thickness = ollamaResult.ThicknessText;
                    }

                    if (!string.IsNullOrWhiteSpace(ollamaResult.MaterialType))
                        material = ollamaResult.MaterialType;
                }
            }

            if (IsPlausibleMaterial(material))
                info.MaterialType = material.Trim();

            var normalizedThickness = NormalizeThickness(thickness);
            if (!string.IsNullOrWhiteSpace(normalizedThickness))
                info.ThicknessText = normalizedThickness;

            if (!string.IsNullOrWhiteSpace(info.MaterialType))
                info.MaterialAlloyPrefix = MaterialAlloyClassifier.ClassifyPrefix(info.MaterialType);

            info.MaterialWatermarkText = MaterialAlloyClassifier.BuildWatermarkText(
                info.MaterialType,
                info.ThicknessText);

            return info;
        }

        internal static bool DiagnoseIsAssemblyDrawing(string pdfPath, int pageNumber = 1)
        {
            var layout = PdfLayoutAnalysisService.AnalyzePage(pdfPath, pageNumber);
            var pageText = PdfLayoutAnalysisService.ExtractPageText(pdfPath, pageNumber);
            var titleBlockRects = FilterTitleBlockRects(layout.TextRects, layout.PageWidth, layout.PageHeight);
            var titleBlockText = BuildTitleBlockText(titleBlockRects);
            var searchTexts = new[] { titleBlockText, pageText };
            return IsAssemblyDrawing(titleBlockText, pageText, searchTexts);
        }

        private static string TryExtractMatFieldValue(string titleBlockText)
        {
            if (string.IsNullOrWhiteSpace(titleBlockText))
                return string.Empty;

            var match = MatFieldRegex.Match(titleBlockText);
            if (!match.Success)
                return string.Empty;

            var value = NormalizeToken(match.Groups[1].Value);
            value = value.Trim(' ', ':', '.', '-', '\t');

            if (IsPlausibleMaterial(value))
                return value;

            var combo = WerkstoffComboRegex.Match(match.Groups[1].Value);
            if (combo.Success && IsPlausibleMaterial(combo.Groups[1].Value))
                return combo.Groups[1].Value.Trim();

            var grade = MaterialGradeRegex.Match(match.Groups[1].Value);
            return grade.Success && IsPlausibleMaterial(grade.Value) ? grade.Value.Trim() : string.Empty;
        }

        private static string ResolveMaterial(
            IList<PdfTextRect> titleBlockRects,
            IList<PdfTextRect> materialLookupRects,
            string titleBlockText,
            string pageText,
            IEnumerable<string> searchTexts,
            float pageScale)
        {
            var candidates = new List<(string Value, int Priority)>();

            void Add(string value, int priority)
            {
                if (string.IsNullOrWhiteSpace(value) || !IsPlausibleMaterial(value))
                    return;

                candidates.Add((value.Trim(), priority));
            }

            var labelRects = EnumerateMaterialLabelRects(titleBlockRects, materialLookupRects).ToList();
            Add(FindMaterialNearMatLabel(labelRects, materialLookupRects, pageScale), 1);
            Add(ExtractLabeledValue(new[] { titleBlockText }, MaterialLabelsOrdered, IsPlausibleMaterial), 2);
            Add(TryExtractMatFieldValue(titleBlockText), 3);
            Add(TryExtractMatFieldValue(pageText), 4);
            Add(ScanRectsForMaterial(titleBlockRects), 5);
            Add(FindMaterialValueNearLabel(materialLookupRects, pageScale), 6);
            Add(ScanTextForMaterialGrade(titleBlockText), 7);
            Add(ScanTextForMaterialGrade(pageText), 8);
            Add(ScanRectsForMaterial(materialLookupRects), 9);

            foreach (var text in searchTexts.Where(t => !string.IsNullOrWhiteSpace(t)))
            {
                var match = MaterialInlineRegex.Match(text);
                if (match.Success)
                {
                    Add(match.Groups[1].Value, 10);
                    break;
                }
            }

            Add(ExtractLabeledValue(searchTexts, MaterialLabelsOrdered, IsPlausibleMaterial), 11);

            return candidates
                .OrderBy(c => c.Priority)
                .Select(c => c.Value)
                .FirstOrDefault() ?? string.Empty;
        }

        private static IList<PdfTextRect> FilterBottomPageRects(IList<PdfTextRect> rects, float pageHeight)
        {
            if (rects == null || rects.Count == 0 || pageHeight <= 0f)
                return new List<PdfTextRect>();

            var bottomLimit = pageHeight * TitleBlockFilterFraction;
            return rects.Where(r => r.Bottom <= bottomLimit).ToList();
        }

        private static IList<PdfTextRect> GetMaterialLookupRects(IList<PdfTextRect> rects, float pageHeight)
        {
            if (rects == null || rects.Count == 0 || pageHeight <= 0f)
                return new List<PdfTextRect>();

            var bottomLimit = pageHeight * MaterialLookupBottomFraction;
            return rects.Where(r => r.Bottom <= bottomLimit).ToList();
        }

        private static IEnumerable<PdfTextRect> EnumerateMaterialLabelRects(
            IList<PdfTextRect> titleBlockRects,
            IList<PdfTextRect> materialLookupRects)
        {
            var seen = new HashSet<PdfTextRect>();
            if (titleBlockRects != null)
            {
                foreach (var rect in titleBlockRects)
                {
                    if (seen.Add(rect))
                        yield return rect;
                }
            }

            if (materialLookupRects == null)
                yield break;

            foreach (var rect in materialLookupRects)
            {
                if (!seen.Add(rect))
                    continue;

                var text = NormalizeToken(rect.Text);
                if (IsMaterialLabelText(text))
                    yield return rect;
            }
        }

        private static string FindMaterialValueNearLabel(IList<PdfTextRect> rects, float pageScale)
        {
            return FindValueNearLabel(
                rects,
                MaterialLabelsOrdered,
                IsPlausibleMaterial,
                pageScale,
                IsMaterialLabelText);
        }

        private static string ScanTextForMaterialGrade(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return string.Empty;

            var combo = WerkstoffComboRegex.Match(text);
            if (combo.Success && IsPlausibleMaterial(combo.Groups[1].Value))
                return combo.Groups[1].Value.Trim();

            var best = string.Empty;
            var bestLen = 0;
            foreach (Match match in MaterialGradeRegex.Matches(text))
            {
                if (!IsPlausibleMaterial(match.Value))
                    continue;

                if (match.Value.Length > bestLen)
                {
                    bestLen = match.Value.Length;
                    best = match.Value;
                }
            }

            return best;
        }

        private static string ScanRectsForMaterial(IEnumerable<PdfTextRect> rects)
        {
            if (rects == null)
                return string.Empty;

            foreach (var rect in rects)
            {
                var text = NormalizeToken(rect.Text);
                if (string.IsNullOrWhiteSpace(text))
                    continue;

                if (IsMaterialLabelOnly(text) || IsThicknessLabelText(text))
                    continue;

                var combo = WerkstoffComboRegex.Match(text);
                if (combo.Success && IsPlausibleMaterial(combo.Groups[1].Value))
                    return combo.Groups[1].Value.Trim();
            }

            var best = string.Empty;
            var bestScore = -1;
            foreach (var rect in rects)
            {
                var text = NormalizeToken(rect.Text);
                if (string.IsNullOrWhiteSpace(text))
                    continue;

                if (IsMaterialLabelOnly(text) || IsThicknessLabelText(text))
                    continue;

                var grade = MaterialGradeRegex.Match(text);
                if (!grade.Success || !IsPlausibleMaterial(grade.Value))
                    continue;

                if (grade.Value.Length > bestScore)
                {
                    bestScore = grade.Value.Length;
                    best = grade.Value;
                }
            }

            return best;
        }

        private static string FindMaterialNearMatLabel(
            IList<PdfTextRect> labelRects,
            IList<PdfTextRect> lookupRects,
            float pageScale)
        {
            if (labelRects == null || labelRects.Count == 0 || lookupRects == null || lookupRects.Count == 0)
                return string.Empty;

            foreach (var labelRect in labelRects)
            {
                var labelText = NormalizeToken(labelRect.Text);
                if (string.IsNullOrWhiteSpace(labelText) || !IsMaterialLabelText(labelText))
                    continue;

                foreach (var label in MaterialLabelsOrdered)
                {
                    if (labelText.Length <= label.Length)
                        continue;

                    if (!labelText.StartsWith(label, StringComparison.OrdinalIgnoreCase))
                        continue;

                    var inline = labelText.Substring(label.Length).Trim(' ', ':', '.', '-', '\t', '/');
                    if (IsPlausibleMaterial(inline))
                        return inline;

                    var parsed = ScanTextForMaterialGrade(inline);
                    if (!string.IsNullOrWhiteSpace(parsed))
                        return parsed;
                }

                var chain = CollectHorizontalMaterialChain(lookupRects, labelRect, pageScale);
                if (!string.IsNullOrWhiteSpace(chain))
                    return chain;

                chain = CollectVerticalMaterialChain(lookupRects, labelRect, pageScale);
                if (!string.IsNullOrWhiteSpace(chain))
                    return chain;

                var adjacent = FindAdjacentValue(
                    lookupRects,
                    labelRect,
                    MaterialLabelsOrdered,
                    IsPlausibleMaterial,
                    pageScale);
                if (!string.IsNullOrWhiteSpace(adjacent))
                    return adjacent;
            }

            return string.Empty;
        }

        private static string CollectHorizontalMaterialChain(
            IList<PdfTextRect> rects,
            PdfTextRect anchor,
            float pageScale,
            int maxParts = 5)
        {
            var parts = new List<string>();
            var used = new HashSet<PdfTextRect> { anchor };
            var cursor = anchor;

            for (var part = 0; part < maxParts; part++)
            {
                PdfTextRect next = null;
                var bestScore = float.MaxValue;
                var reach = 260f * pageScale;
                var slack = 24f * pageScale;

                foreach (var rect in rects)
                {
                    if (used.Contains(rect))
                        continue;

                    var text = NormalizeToken(rect.Text);
                    if (string.IsNullOrWhiteSpace(text))
                        continue;

                    if (IsMaterialLabelOnly(text) || IsThicknessLabelText(text))
                        continue;

                    if (rect.Left < cursor.Right - slack || rect.Left > cursor.Right + reach)
                        continue;

                    if (VerticalOverlap(cursor, rect) <= -6f)
                        continue;

                    var score = rect.Left - cursor.Right;
                    if (score < bestScore)
                    {
                        bestScore = score;
                        next = rect;
                    }
                }

                if (next == null)
                    break;

                parts.Add(NormalizeToken(next.Text));
                used.Add(next);
                cursor = next;
            }

            if (parts.Count == 0)
                return string.Empty;

            var combined = string.Join(" ", parts).Replace(" / ", "/").Replace("/ ", "/").Replace(" /", "/");
            if (IsPlausibleMaterial(combined))
                return combined;

            combined = string.Concat(parts);
            return IsPlausibleMaterial(combined) ? combined : ScanTextForMaterialGrade(string.Join(" ", parts));
        }

        private static string CollectVerticalMaterialChain(
            IList<PdfTextRect> rects,
            PdfTextRect anchor,
            float pageScale,
            int maxParts = 4)
        {
            var parts = new List<string>();
            var used = new HashSet<PdfTextRect> { anchor };
            var cursor = anchor;
            var horizontalSlack = 90f * pageScale;
            var verticalReach = 70f * pageScale;

            for (var part = 0; part < maxParts; part++)
            {
                PdfTextRect next = null;
                var bestScore = float.MaxValue;

                foreach (var rect in rects)
                {
                    if (used.Contains(rect))
                        continue;

                    var text = NormalizeToken(rect.Text);
                    if (string.IsNullOrWhiteSpace(text))
                        continue;

                    if (IsMaterialLabelOnly(text) || IsThicknessLabelText(text))
                        continue;

                    if (rect.Top > cursor.Bottom + 4f * pageScale || rect.Bottom < cursor.Bottom - verticalReach)
                        continue;

                    if (rect.Left < cursor.Left - horizontalSlack || rect.Left > cursor.Right + horizontalSlack)
                        continue;

                    var score = (cursor.Bottom - rect.Top) + Math.Abs(rect.Left - cursor.Left);
                    if (score < bestScore)
                    {
                        bestScore = score;
                        next = rect;
                    }
                }

                if (next == null)
                    break;

                parts.Add(NormalizeToken(next.Text));
                used.Add(next);
                cursor = next;
            }

            if (parts.Count == 0)
                return string.Empty;

            var combined = string.Join(" ", parts).Replace(" / ", "/").Replace("/ ", "/").Replace(" /", "/");
            if (IsPlausibleMaterial(combined))
                return combined;

            return ScanTextForMaterialGrade(combined);
        }

        private static bool IsMaterialLabelText(string text)
        {
            if (string.IsNullOrWhiteSpace(text) || IsThicknessLabelText(text))
                return false;

            foreach (var label in MaterialLabelsOrdered)
            {
                if (text.Equals(label, StringComparison.OrdinalIgnoreCase)
                    || text.Equals(label + ".", StringComparison.OrdinalIgnoreCase)
                    || text.Equals(label + ":", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                if (text.Length > label.Length + 1
                    && text.StartsWith(label, StringComparison.OrdinalIgnoreCase))
                {
                    var next = text[label.Length];
                    if (next == ' ' || next == ':' || next == '.' || next == '-')
                        return true;
                }
            }

            return false;
        }

        private static bool IsMaterialLabelOnly(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return false;

            foreach (var label in MaterialLabelsOrdered)
            {
                if (text.Equals(label, StringComparison.OrdinalIgnoreCase)
                    || text.Equals(label + ".", StringComparison.OrdinalIgnoreCase)
                    || text.Equals(label + ":", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Baugruppenzeichnungen: mehrere Teile, keine sinnvolle Einzel-Blechstärke.
        /// </summary>
        private static bool IsAssemblyDrawing(string titleBlockText, string pageText, IEnumerable<string> searchTexts)
        {
            var sanitizedTitle = SanitizeTextForBomDetection(titleBlockText);
            var sanitizedPage = SanitizeTextForBomDetection(pageText);

            foreach (var label in AssemblyDrawingLabels)
            {
                if (ContainsAssemblyLabel(sanitizedTitle, label)
                    || ContainsAssemblyLabel(sanitizedPage, label))
                {
                    return true;
                }
            }

            if (!string.IsNullOrWhiteSpace(sanitizedTitle))
            {
                var hasPartsList = PartsListLabels.Any(label =>
                    sanitizedTitle.IndexOf(label, StringComparison.OrdinalIgnoreCase) >= 0);

                if (hasPartsList)
                {
                    var partCount = OrderVerificationDrawingKey.ExtractAllFromText(sanitizedTitle).Count;

                    if (partCount >= 2)
                        return true;
                }
            }

            if (!string.IsNullOrWhiteSpace(sanitizedPage))
            {
                var pagePartCount = OrderVerificationDrawingKey.ExtractAllFromText(sanitizedPage).Count;

                var hasPartsListOnPage = PartsListLabels.Any(label =>
                    sanitizedPage.IndexOf(label, StringComparison.OrdinalIgnoreCase) >= 0);

                if (hasPartsListOnPage && pagePartCount >= 3)
                    return true;

                if (sanitizedPage.IndexOf("Specification Manufacturer", StringComparison.OrdinalIgnoreCase) >= 0
                    && pagePartCount >= 2)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool ContainsAssemblyLabel(string text, string label)
        {
            if (string.IsNullOrWhiteSpace(text) || string.IsNullOrWhiteSpace(label))
                return false;

            return Regex.IsMatch(
                text,
                @"\b" + Regex.Escape(label) + @"\b",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        }

        private static bool IsAppGeneratedOverlayText(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return false;

            var trimmed = text.Trim();
            if (AppWatermarkMaterialRegex.IsMatch(trimmed))
                return true;

            if (trimmed.StartsWith("BG:", StringComparison.OrdinalIgnoreCase))
                return true;

            if (AppWatermarkQuantityRegex.IsMatch(trimmed))
                return true;

            if (trimmed.StartsWith("LT-", StringComparison.OrdinalIgnoreCase))
                return true;

            return false;
        }

        private static IEnumerable<string> EnumerateAssemblySources(
            string titleBlockText,
            string pageText,
            IEnumerable<string> searchTexts)
        {
            if (!string.IsNullOrWhiteSpace(titleBlockText))
                yield return titleBlockText;

            if (!string.IsNullOrWhiteSpace(pageText))
                yield return pageText;

            if (searchTexts == null)
                yield break;

            foreach (var text in searchTexts)
            {
                if (!string.IsNullOrWhiteSpace(text))
                    yield return text;
            }
        }

        private static string SanitizeTextForBomDetection(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return string.Empty;

            var sanitized = Regex.Replace(
                text,
                @"[A-Z]:\\[^\r\n]+",
                " ",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

            var lines = sanitized.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            var kept = new List<string>();
            foreach (var line in lines)
            {
                var trimmed = line.Trim();
                if (trimmed.StartsWith("BG:", StringComparison.OrdinalIgnoreCase)
                    || AppWatermarkMaterialRegex.IsMatch(trimmed)
                    || AppWatermarkQuantityRegex.IsMatch(trimmed)
                    || trimmed.StartsWith("LT-", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                kept.Add(line);
            }

            return string.Join("\n", kept);
        }

        private static bool HasMultiPartBillOfMaterials(string titleBlockText, string primaryDrawing)
        {
            if (string.IsNullOrWhiteSpace(titleBlockText) || string.IsNullOrWhiteSpace(primaryDrawing))
                return false;

            var sanitized = SanitizeTextForBomDetection(titleBlockText);

            var primaryKey = OrderVerificationDrawingKey.Normalize(primaryDrawing);
            var relatedParts = OrderVerificationDrawingKey.ExtractAllFromText(sanitized)
                .Select(OrderVerificationDrawingKey.Normalize)
                .Where(k => !string.IsNullOrWhiteSpace(k))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Where(k => !string.Equals(k, primaryKey, StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (relatedParts.Count < 2)
                return false;

            var hasBomIndicator = PartsListLabels.Any(label =>
                    sanitized.IndexOf(label, StringComparison.OrdinalIgnoreCase) >= 0)
                || sanitized.IndexOf("Specification Manufacturer", StringComparison.OrdinalIgnoreCase) >= 0
                || (sanitized.IndexOf("Position", StringComparison.OrdinalIgnoreCase) >= 0
                    && sanitized.IndexOf("Description", StringComparison.OrdinalIgnoreCase) >= 0);

            return hasBomIndicator;
        }

        private static string ResolveThicknessCandidates(
            string pdfPath,
            int pageNumber,
            PdfPageLayout layout,
            IList<PdfTextRect> rects,
            IList<PdfTextRect> titleBlockRects,
            string titleBlockText,
            IEnumerable<string> searchTexts,
            string materialRaw)
        {
            var candidates = new List<ThicknessCandidate>();
            var pageScale = GetPageScale(layout.PageWidth, layout.PageHeight);

            CollectExplicitThicknessTextCandidates(candidates, searchTexts, materialRaw);
            CollectThicknessMmFromRects(candidates, rects, layout.PageWidth, layout.PageHeight, materialRaw);

            ThicknessCandidateResolver.AddCandidate(
                candidates,
                FindValueNearLabel(titleBlockRects, ThicknessLabels, IsPlausibleThicknessRaw, pageScale),
                ThicknessCandidateResolver.PriorityTitleBlock,
                "schriftfeld-label");

            ThicknessCandidateResolver.AddCandidate(
                candidates,
                FindThicknessNearLabelRects(titleBlockRects, pageScale),
                ThicknessCandidateResolver.PriorityTitleBlock,
                "schriftfeld-thk");

            ThicknessCandidateResolver.AddCandidate(
                candidates,
                ExtractThicknessFromSideViewDimensions(rects, layout.PageWidth, layout.PageHeight, materialRaw),
                ThicknessCandidateResolver.PrioritySideViewText,
                "seitenansicht-text");

            CollectSideViewDimensionCandidates(candidates, rects, layout.PageWidth, layout.PageHeight, materialRaw);

            var scaleDenominator = ExtractScaleDenominator(titleBlockText, searchTexts);
            var textOnlyMm = ThicknessCandidateResolver.ResolveBestMm(candidates, materialRaw);
            var needsRaster = MaterialThicknessHybridResolver.NeedsRasterVerification(candidates, materialRaw, textOnlyMm);

            if (needsRaster && PdfRasterLayoutService.IsAvailable)
            {
                PdfThreeViewThicknessService.TryCollectSideViewTextCandidates(
                    pdfPath,
                    pageNumber,
                    layout.PageWidth,
                    layout.PageHeight,
                    rects,
                    candidates,
                    materialRaw);

                MaterialThicknessHybridResolver.AddRasterSideProfileCandidate(
                    candidates,
                    pdfPath,
                    pageNumber,
                    layout.PageWidth,
                    layout.PageHeight,
                    scaleDenominator);
            }

            var bestMm = MaterialThicknessHybridResolver.ResolveFinalMm(
                candidates,
                pdfPath,
                pageNumber,
                materialRaw);
            return bestMm.HasValue
                ? bestMm.Value.ToString("0.0", CultureInfo.GetCultureInfo("de-DE"))
                : string.Empty;
        }

        private static void CollectThicknessMmFromRects(
            ICollection<ThicknessCandidate> candidates,
            IList<PdfTextRect> rects,
            float pageWidth,
            float pageHeight,
            string materialRaw)
        {
            if (rects == null || rects.Count == 0)
                return;

            var titleBlockTop = pageHeight * DrawingAreaBottomFraction;

            foreach (var rect in rects)
            {
                var text = NormalizeToken(rect.Text);
                if (IsAppGeneratedOverlayText(text))
                    continue;

                var match = ThicknessValueRegex.Match(text);
                if (!match.Success || !IsPlausibleSheetThickness(match.Groups[1].Value, ThicknessCandidateResolver.MaxAutoDetectedSheetMm))
                    continue;

                var centerX = (rect.Left + rect.Right) / 2f;
                var relX = centerX / Math.Max(1f, pageWidth);
                var relY = ((rect.Bottom + rect.Top) / 2f) / Math.Max(1f, pageHeight);
                var inTitleBlock = rect.Bottom <= titleBlockTop;
                var suspectWatermark = !inTitleBlock && relX < 0.42f && relY > 0.18f && relY < 0.82f;

                if (!TryParseThicknessValue(match.Groups[1].Value, out var mmValue))
                    continue;

                if (ThicknessCandidateResolver.IsLikelyMaterialNumberFragment(rect.Text, mmValue)
                    || ThicknessCandidateResolver.IsMaterialNumberThicknessFalsePositive(
                        mmValue, materialRaw, rect.Text, suspectWatermark ? "wasserzeichen-links" : "mm-text"))
                    continue;

                if (ThicknessCandidateResolver.IsLikelyHoleDiameterMm(mmValue)
                    || ThicknessCandidateResolver.IsLikelyHoleOrFeatureDimension(rect.Text, mmValue))
                    continue;

                if (!inTitleBlock
                    && ThicknessCandidateResolver.IsWholeNumber(mmValue)
                    && mmValue >= 4d
                    && relX < 0.62f
                    && !(ThicknessCandidateResolver.IsStandardSheetGauge(mmValue, 0.5d)
                         && mmValue >= 5d
                         && relX >= 0.35f))
                {
                    suspectWatermark = true;
                }

                if (TryParseThicknessValue(match.Groups[1].Value, out mmValue)
                    && mmValue > ThicknessCandidateResolver.MaxAutoDetectedSheetMm
                    && !inTitleBlock
                    && !(ThicknessCandidateResolver.IsStandardSheetGauge(mmValue, 0.5d) && relX >= 0.35f))
                {
                    suspectWatermark = true;
                }

                ThicknessCandidateResolver.AddCandidate(
                    candidates,
                    match.Groups[1].Value.Trim(),
                    ThicknessCandidateResolver.PriorityExplicitText,
                    suspectWatermark ? "wasserzeichen-links" : "mm-text",
                    suspectWatermark);
            }
        }

        private static void CollectExplicitThicknessTextCandidates(
            ICollection<ThicknessCandidate> candidates,
            IEnumerable<string> searchTexts,
            string materialRaw)
        {
            foreach (var source in searchTexts.Where(t => !string.IsNullOrWhiteSpace(t)))
            {
                ThicknessCandidateResolver.AddCandidate(
                    candidates,
                    ExtractLabeledValue(new[] { source }, ThicknessLabels, IsPlausibleThicknessRaw),
                    ThicknessCandidateResolver.PriorityExplicitText,
                    "label");

                foreach (var rawLine in source.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    var line = NormalizeToken(rawLine);
                    if (string.IsNullOrWhiteSpace(line) || IsAppGeneratedOverlayText(line))
                        continue;

                    var inline = ThicknessInlineRegex.Match(line);
                    if (inline.Success)
                    {
                        TryAddParsedThicknessCandidate(
                            candidates,
                            inline.Groups[1].Value.Trim(),
                            line,
                            ThicknessCandidateResolver.PriorityExplicitText,
                            "inline",
                            materialRaw);
                    }

                    inline = ThicknessGluedRegex.Match(line);
                    if (inline.Success)
                    {
                        TryAddParsedThicknessCandidate(
                            candidates,
                            inline.Groups[1].Value.Trim(),
                            line,
                            ThicknessCandidateResolver.PriorityExplicitText,
                            "inline-glued",
                            materialRaw);
                    }

                    inline = ThicknessShortRegex.Match(line);
                    if (inline.Success)
                    {
                        TryAddParsedThicknessCandidate(
                            candidates,
                            inline.Groups[1].Value.Trim(),
                            line,
                            ThicknessCandidateResolver.PriorityExplicitText,
                            "inline-t",
                            materialRaw);
                    }

                    var outerMatch = OuterDimensionsRegex.Match(line);
                    if (outerMatch.Success && IsPlausibleSheetThickness(outerMatch.Groups[1].Value))
                    {
                        ThicknessCandidateResolver.AddCandidate(
                            candidates,
                            outerMatch.Groups[1].Value.Trim(),
                            ThicknessCandidateResolver.PriorityExplicitText,
                            "aussenmasse");
                    }
                }
            }
        }

        private static readonly Regex IsolatedThicknessDimensionRegex = new Regex(
            @"^(\d{1,2}([.,]\d{1,2})?)\s*(?:mm|MM)?$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

        private static readonly string[] SideViewHintLabels =
        {
            "SIDE", "SECTION", "SEITE", "SCHNITT", "END VIEW", "ENDE", "DETAIL", "DET.", "SCALE"
        };

        private static string ExtractThicknessFromSideViewDimensions(
            IList<PdfTextRect> allRects,
            float pageWidth,
            float pageHeight,
            string materialRaw)
        {
            if (allRects == null || allRects.Count == 0 || pageWidth <= 0f || pageHeight <= 0f)
                return string.Empty;

            var titleBlockTop = pageHeight * DrawingAreaBottomFraction;
            var topMarginBottom = pageHeight * 0.92f;
            var sideViewAnchors = FindSideViewAnchorRects(allRects, pageWidth, pageHeight, titleBlockTop, topMarginBottom);

            var scored = new List<(double Value, float Score)>();

            foreach (var rect in allRects)
            {
                if (rect.Bottom <= titleBlockTop || rect.Top >= topMarginBottom)
                    continue;

                var text = NormalizeToken(rect.Text);
                if (IsAppGeneratedOverlayText(text))
                    continue;

                if (!TryParseIsolatedThicknessDimension(text, out var value))
                    continue;

                if (value < 0.3d || value > ThicknessCandidateResolver.MaxSideViewThicknessMm)
                    continue;

                if (ThicknessCandidateResolver.IsLikelyMaterialNumberFragment(rect.Text, value)
                    || ThicknessCandidateResolver.IsMaterialNumberThicknessFalsePositive(
                        value, materialRaw, rect.Text, "seitenansicht-text"))
                    continue;

                if (ThicknessCandidateResolver.IsLikelyHoleOrFeatureDimension(rect.Text, value)
                    || ThicknessCandidateResolver.IsLikelyHoleDiameterMm(value))
                    continue;

                var isPlateGauge = ThicknessCandidateResolver.IsPlausiblePlateThicknessMm(value);
                if (!isPlateGauge
                    && (HasDiameterSymbolNearby(allRects, rect, GetPageScale(pageWidth, pageHeight))
                        || IsInParenthesesContext(allRects, rect, GetPageScale(pageWidth, pageHeight))))
                    continue;

                var score = ScoreSideViewThicknessCandidate(rect, value, pageWidth, pageHeight, sideViewAnchors, GetPageScale(pageWidth, pageHeight));
                scored.Add((value, score));
            }

            var resolved = ThicknessCandidateResolver.ResolveSideViewThicknessMm(scored);
            if (!resolved.HasValue)
                return string.Empty;

            return resolved.Value.ToString("0.0", CultureInfo.GetCultureInfo("de-DE"));
        }

        private static void CollectSideViewDimensionCandidates(
            ICollection<ThicknessCandidate> candidates,
            IList<PdfTextRect> allRects,
            float pageWidth,
            float pageHeight,
            string materialRaw)
        {
            if (candidates == null || allRects == null || allRects.Count == 0)
                return;

            var titleBlockTop = pageHeight * DrawingAreaBottomFraction;
            var topMarginBottom = pageHeight * 0.92f;
            var sideViewAnchors = FindSideViewAnchorRects(allRects, pageWidth, pageHeight, titleBlockTop, topMarginBottom);
            var pageScale = GetPageScale(pageWidth, pageHeight);
            var scored = new List<(double Value, float Score)>();

            foreach (var rect in allRects)
            {
                if (rect.Bottom <= titleBlockTop || rect.Top >= topMarginBottom)
                    continue;

                var text = NormalizeToken(rect.Text);
                if (IsAppGeneratedOverlayText(text))
                    continue;

                var centerX = (rect.Left + rect.Right) / 2f;
                var centerY = (rect.Bottom + rect.Top) / 2f;
                var relX = centerX / Math.Max(1f, pageWidth);
                var relY = centerY / Math.Max(1f, pageHeight);

                if (Regex.IsMatch(text, @"^(8|10|12|15|20)$", RegexOptions.CultureInvariant)
                    && relX >= 0.52f
                    && relY >= 0.48f
                    && relY <= 0.86f)
                {
                    ThicknessCandidateResolver.AddCandidate(
                        candidates,
                        double.Parse(text, CultureInfo.InvariantCulture),
                        ThicknessCandidateResolver.PriorityExplicitText,
                        "seitenansicht-platte");
                    continue;
                }

                if (!TryParseIsolatedThicknessDimension(text, out var value))
                    continue;

                if (value < 0.3d || value > ThicknessCandidateResolver.MaxSideViewThicknessMm)
                    continue;

                if (ThicknessCandidateResolver.IsLikelyMaterialNumberFragment(rect.Text, value)
                    || ThicknessCandidateResolver.IsMaterialNumberThicknessFalsePositive(
                        value, materialRaw, rect.Text, "seitenansicht-zone"))
                    continue;

                if (ThicknessCandidateResolver.IsLikelyHoleOrFeatureDimension(rect.Text, value)
                    || ThicknessCandidateResolver.IsLikelyHoleDiameterMm(value))
                    continue;

                var isPlateGauge = ThicknessCandidateResolver.IsPlausiblePlateThicknessMm(value);
                if (!isPlateGauge
                    && (HasDiameterSymbolNearby(allRects, rect, pageScale)
                        || IsInParenthesesContext(allRects, rect, pageScale)))
                    continue;

                var score = ScoreSideViewThicknessCandidate(rect, value, pageWidth, pageHeight, sideViewAnchors, pageScale);
                scored.Add((value, score));

                if (ThicknessCandidateResolver.IsPlausiblePlateThicknessMm(value)
                    && relX >= 0.52f
                    && relY >= 0.48f
                    && relY <= 0.86f)
                {
                    ThicknessCandidateResolver.AddCandidate(
                        candidates,
                        value,
                        ThicknessCandidateResolver.PriorityExplicitText,
                        "seitenansicht-platte");
                }
                else
                {
                    ThicknessCandidateResolver.AddCandidate(
                        candidates,
                        value,
                        ThicknessCandidateResolver.PrioritySideViewText,
                        "seitenansicht-zone");
                }
            }

            if (scored.Count == 0)
                return;

            var resolved = ThicknessCandidateResolver.ResolveSideViewThicknessMm(scored);
            if (!resolved.HasValue)
                return;

            ThicknessCandidateResolver.AddCandidate(
                candidates,
                resolved.Value,
                ThicknessCandidateResolver.PriorityThreeViewText,
                "seitenansicht-text");
        }

        private static List<PdfTextRect> FindSideViewAnchorRects(
            IList<PdfTextRect> allRects,
            float pageWidth,
            float pageHeight,
            float titleBlockTop,
            float topMarginBottom)
        {
            var anchors = new List<PdfTextRect>();
            foreach (var rect in allRects)
            {
                if (rect.Bottom <= titleBlockTop || rect.Top >= topMarginBottom)
                    continue;

                var text = NormalizeToken(rect.Text);
                if (string.IsNullOrWhiteSpace(text))
                    continue;

                if (SideViewHintLabels.Any(label => text.IndexOf(label, StringComparison.OrdinalIgnoreCase) >= 0))
                    anchors.Add(rect);
            }

            return anchors;
        }

        private static bool TryParseIsolatedThicknessDimension(string text, out double value)
        {
            value = 0d;
            if (string.IsNullOrWhiteSpace(text))
                return false;

            var trimmed = NormalizeToken(text);
            if (trimmed.EndsWith("X", StringComparison.OrdinalIgnoreCase))
                return false;

            if (trimmed.StartsWith("R", StringComparison.OrdinalIgnoreCase))
                return false;

            var match = IsolatedThicknessDimensionRegex.Match(trimmed);
            if (!match.Success)
                return false;

            return TryParseThicknessValue(match.Groups[1].Value, out value);
        }

        private static float ScoreSideViewThicknessCandidate(
            PdfTextRect rect,
            double value,
            float pageWidth,
            float pageHeight,
            IList<PdfTextRect> sideViewAnchors,
            float pageScale)
        {
            var score = 0f;
            var centerX = (rect.Left + rect.Right) / 2f;
            var centerY = (rect.Bottom + rect.Top) / 2f;
            var relY = centerY / Math.Max(1f, pageHeight);
            var relX = centerX / Math.Max(1f, pageWidth);

            if (relX >= 0.45f)
                score -= 45f;
            if (relX >= 0.58f)
                score -= 35f;

            if (relY >= 0.28f && relY <= 0.78f)
                score -= 40f;

            if (relX >= 0.45f && relY >= 0.38f && relY <= 0.72f)
                score -= 30f;

            if (relX >= 0.45f && relY >= 0.52f && relY <= 0.85f)
                score -= 40f;

            var narrowWidth = 22f * pageScale;
            var mediumWidth = 34f * pageScale;
            if (rect.Width <= narrowWidth)
                score -= 30f;
            else if (rect.Width <= mediumWidth)
                score -= 15f;

            if (value >= 0.5d && value <= 12d)
                score -= 35f;

            if (value >= 0.5d && value <= 10d)
                score -= 20f;

            if (value >= 7d && value <= 10d && relY < 0.50f)
                score += 30f;

            if (ThicknessCandidateResolver.IsWholeNumber(value)
                && value > ThicknessCandidateResolver.MaxAutoDetectedSheetMm
                && value <= 15d
                && ThicknessCandidateResolver.IsStandardSheetGauge(value, 0.15d)
                && relX >= 0.45f
                && relY >= 0.22f
                && relY <= 0.78f)
            {
                score -= 150f;
            }
            else if (ThicknessCandidateResolver.IsWholeNumber(value) && value >= 4d && relX < 0.45f)
                score += 100f;

            if (value > ThicknessCandidateResolver.MaxAutoDetectedSheetMm && relX < 0.45f)
                score += 120f;

            if (value >= 8d && ThicknessCandidateResolver.IsWholeNumber(value) && relX < 0.45f)
                score += 80f;

            if (value > ThicknessCandidateResolver.MaxAutoDetectedSheetMm
                && relX >= 0.45f
                && ThicknessCandidateResolver.IsStandardSheetGauge(value))
            {
                score -= 70f;
            }

            if (value >= 0.5d && value <= 3d)
                score -= 45f;

            if (ThicknessCandidateResolver.IsStandardSheetGauge(value, 0.12d)
                && value <= ThicknessCandidateResolver.MaxAutoDetectedSheetMm)
            {
                score -= 80f;
            }
            else if (ThicknessCandidateResolver.IsPlausiblePlateThicknessMm(value)
                && relX >= 0.40f)
            {
                score -= 120f;
            }

            // Typische Blechdicke mit Kommastelle (1,5 / 2,5) nahe der Seitenansicht bevorzugen.
            if (ThicknessCandidateResolver.HasFractionalPart(value)
                && value >= 0.5d
                && value <= 6d
                && relX >= 0.40f)
            {
                score -= 60f;
            }

            if (relX < 0.28f)
                score += 30f;

            if (ThicknessCandidateResolver.IsLikelyHoleDiameterMm(value))
                score += 120f;

            if (ThicknessCandidateResolver.HasFractionalPart(value)
                && ThicknessCandidateResolver.IsStandardSheetGauge(value, 0.05d)
                && value <= ThicknessCandidateResolver.MaxAutoDetectedSheetMm)
            {
                score -= 90f;
            }

            if (value >= 1d
                && value <= 6d
                && ThicknessCandidateResolver.IsStandardSheetGauge(value, 0.15d)
                && relX >= 0.72f
                && relY >= 0.72f)
            {
                score -= 160f;
            }

            if (ThicknessCandidateResolver.IsPlausiblePlateThicknessMm(value)
                && relX >= 0.55f
                && relX <= 0.88f
                && relY >= 0.55f
                && relY <= 0.82f)
            {
                score -= 170f;
            }

            if (value >= 20d)
                score += 220f;

            if (sideViewAnchors != null && sideViewAnchors.Count > 0)
            {
                var nearDist = 220f * pageScale;
                var midDist = 360f * pageScale;
                var nearest = sideViewAnchors.Min(a => DistanceBetween(rect, a));
                if (nearest < nearDist)
                    score -= 80f;
                else if (nearest < midDist)
                    score -= 35f;
            }

            return score;
        }

        private static float DistanceBetween(PdfTextRect a, PdfTextRect b)
        {
            var ax = (a.Left + a.Right) / 2f;
            var ay = (a.Bottom + a.Top) / 2f;
            var bx = (b.Left + b.Right) / 2f;
            var by = (b.Bottom + b.Top) / 2f;
            var dx = ax - bx;
            var dy = ay - by;
            return (float)Math.Sqrt((dx * dx) + (dy * dy));
        }

        private static bool HasDiameterSymbolNearby(IList<PdfTextRect> allRects, PdfTextRect target, float pageScale)
        {
            if (allRects == null || target == null)
                return false;

            var reach = 28f * pageScale;
            foreach (var rect in allRects)
            {
                if (ReferenceEquals(rect, target))
                    continue;

                var text = rect.Text ?? string.Empty;
                if (text.IndexOf("Ø", StringComparison.Ordinal) < 0
                    && text.IndexOf("⌀", StringComparison.Ordinal) < 0
                    && text.IndexOf("DIA", StringComparison.OrdinalIgnoreCase) < 0)
                {
                    continue;
                }

                if (rect.Right >= target.Left - reach
                    && rect.Left <= target.Right + (reach * 0.35f)
                    && VerticalOverlap(rect, target) > -(8f * pageScale))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsInParenthesesContext(IList<PdfTextRect> allRects, PdfTextRect target, float pageScale)
        {
            if (allRects == null || target == null)
                return false;

            var reach = 42f * pageScale;
            var hasOpen = false;
            var hasClose = false;

            foreach (var rect in allRects)
            {
                if (ReferenceEquals(rect, target))
                    continue;

                var text = rect.Text ?? string.Empty;
                if (string.IsNullOrWhiteSpace(text))
                    continue;

                if (text.IndexOf('(') >= 0
                    && rect.Right >= target.Left - reach
                    && rect.Right <= target.Left + (reach * 0.35f)
                    && VerticalOverlap(rect, target) > -(10f * pageScale))
                {
                    hasOpen = true;
                }

                if (text.IndexOf(')') >= 0
                    && rect.Left <= target.Right + reach
                    && rect.Left >= target.Right - (reach * 0.35f)
                    && VerticalOverlap(rect, target) > -(10f * pageScale))
                {
                    hasClose = true;
                }
            }

            return hasOpen && hasClose;
        }

        private static int ExtractScaleDenominator(string titleBlockText, IEnumerable<string> fallbackTexts)
        {
            var sources = new List<string>();
            if (!string.IsNullOrWhiteSpace(titleBlockText))
                sources.Add(titleBlockText);

            if (fallbackTexts != null)
            {
                foreach (var text in fallbackTexts)
                {
                    if (!string.IsNullOrWhiteSpace(text) && !sources.Contains(text))
                        sources.Add(text);
                }
            }

            foreach (var source in sources)
            {
                var match = ScaleRegex.Match(source);
                if (match.Success && int.TryParse(match.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var denom) && denom > 0)
                    return denom;
            }

            return 1;
        }

        private static List<PdfTextRect> FilterTitleBlockRects(IList<PdfTextRect> rects, float pageWidth, float pageHeight)
        {
            if (rects == null || rects.Count == 0)
                return new List<PdfTextRect>();

            // Unterer Schriftfeld-Streifen + rechtes Schriftfeld (Tesla/CAD)
            var bottomLimit = pageHeight * TitleBlockFilterFraction;
            var rightLimit = pageWidth * TitleBlockRightFraction;
            var rightBottomLimit = pageHeight * TitleBlockRightHeightFraction;

            return rects
                .Where(r => r.Bottom <= bottomLimit
                            || (r.Left >= rightLimit && r.Bottom <= rightBottomLimit))
                .ToList();
        }

        private static string FindThicknessNearLabelRects(IList<PdfTextRect> titleBlockRects, float pageScale)
        {
            if (titleBlockRects == null || titleBlockRects.Count == 0)
                return string.Empty;

            foreach (var labelRect in titleBlockRects)
            {
                var labelText = NormalizeToken(labelRect.Text);
                if (string.IsNullOrWhiteSpace(labelText))
                    continue;

                if (!IsThicknessLabelText(labelText))
                    continue;

                foreach (var label in ThicknessLabels)
                {
                    if (!labelText.StartsWith(label, StringComparison.OrdinalIgnoreCase))
                        continue;

                    var inline = labelText.Substring(label.Length).Trim(' ', ':', '.', '-', '\t');
                    if (IsPlausibleSheetThickness(inline))
                        return inline;
                }

                var best = FindNearestThicknessValue(titleBlockRects, labelRect, pageScale);
                if (!string.IsNullOrWhiteSpace(best))
                    return best;
            }

            return string.Empty;
        }

        private static bool IsThicknessLabelText(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return false;

            return ThicknessLabels.Any(label =>
                text.Equals(label, StringComparison.OrdinalIgnoreCase)
                || text.StartsWith(label + " ", StringComparison.OrdinalIgnoreCase)
                || text.StartsWith(label + ":", StringComparison.OrdinalIgnoreCase)
                || text.StartsWith(label + ".", StringComparison.OrdinalIgnoreCase));
        }

        private static string FindNearestThicknessValue(IList<PdfTextRect> rects, PdfTextRect label, float pageScale)
        {
            PdfTextRect best = null;
            var bestScore = float.MaxValue;
            var maxDistance = 420f * pageScale;

            foreach (var rect in rects)
            {
                if (ReferenceEquals(rect, label))
                    continue;

                var text = NormalizeToken(rect.Text);
                if (!IsPlausibleSheetThickness(text, ThicknessCandidateResolver.MaxSideViewThicknessMm))
                    continue;

                if (!TryParseThicknessValue(text, out var mmValue))
                    continue;

                if (ThicknessCandidateResolver.IsLikelyMaterialNumberFragment(rect.Text, mmValue))
                    continue;

                var dx = Math.Max(0f, Math.Max(label.Left - rect.Right, rect.Left - label.Right));
                var dy = Math.Max(0f, Math.Max(label.Bottom - rect.Top, rect.Bottom - label.Top));
                var score = dx + (dy * 1.5f);
                if (score > maxDistance)
                    continue;

                if (score < bestScore)
                {
                    bestScore = score;
                    best = rect;
                }
            }

            return best == null ? string.Empty : NormalizeToken(best.Text);
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
            Func<string, bool> validator,
            float pageScale,
            Func<string, bool> flexibleLabelMatcher = null)
        {
            if (rects == null || rects.Count == 0)
                return string.Empty;

            foreach (var labelRect in rects)
            {
                var labelText = NormalizeToken(labelRect.Text);
                if (string.IsNullOrWhiteSpace(labelText))
                    continue;

                if (!MatchesAnyLabel(labelText, labels, flexibleLabelMatcher))
                    continue;

                var inline = labels
                    .Select(label => TryParseInlineLabelValue(labelText, label, validator))
                    .FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));
                if (!string.IsNullOrWhiteSpace(inline))
                    return inline;

                var adjacent = FindAdjacentValue(rects, labelRect, labels, validator, pageScale);
                if (!string.IsNullOrWhiteSpace(adjacent))
                    return adjacent;
            }

            return string.Empty;
        }

        private static bool MatchesAnyLabel(
            string labelText,
            string[] labels,
            Func<string, bool> flexibleLabelMatcher)
        {
            if (flexibleLabelMatcher != null && flexibleLabelMatcher(labelText))
                return true;

            return labels.Any(label => labelText.Equals(label, StringComparison.OrdinalIgnoreCase)
                                       || labelText.StartsWith(label + " ", StringComparison.OrdinalIgnoreCase)
                                       || labelText.StartsWith(label + ":", StringComparison.OrdinalIgnoreCase)
                                       || labelText.StartsWith(label + ".", StringComparison.OrdinalIgnoreCase)
                                       || (labelText.Length > label.Length
                                           && labelText.StartsWith(label, StringComparison.OrdinalIgnoreCase)
                                           && (labelText[label.Length] == '.' || labelText[label.Length] == ':' || labelText[label.Length] == '-')));
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
            Func<string, bool> validator,
            float pageScale)
        {
            PdfTextRect best = null;
            var bestScore = float.MaxValue;
            var horizontalReach = 420f * pageScale;
            var horizontalSlack = 15f * pageScale;
            var verticalBelow = 55f * pageScale;
            var horizontalLeft = 80f * pageScale;

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

                if (rect.Left >= label.Right - horizontalSlack && rect.Left <= label.Right + horizontalReach)
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

                if (rect.Top <= label.Bottom + (12f * pageScale) && rect.Bottom >= label.Bottom - verticalBelow
                    && rect.Left >= label.Left - horizontalLeft && rect.Left <= label.Right + horizontalReach)
                {
                    var score = (label.Bottom - rect.Top) + (600f * pageScale);
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
            if (text.Length < 2 || text.Length > 36)
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

            if (WerkstoffComboRegex.IsMatch(text))
                return true;

            return Regex.IsMatch(text, @"[A-Za-z]");
        }

        private static void TryAddParsedThicknessCandidate(
            ICollection<ThicknessCandidate> candidates,
            string rawValue,
            string contextText,
            int priority,
            string source,
            string materialRaw = null)
        {
            if (string.IsNullOrWhiteSpace(rawValue))
                return;

            if (!TryParseThicknessValue(rawValue, out var mm))
                return;

            if (ThicknessCandidateResolver.IsLikelyMaterialNumberFragment(contextText ?? rawValue, mm)
                || ThicknessCandidateResolver.IsMaterialNumberThicknessFalsePositive(
                    mm, materialRaw, contextText ?? rawValue, source))
                return;

            ThicknessCandidateResolver.AddCandidate(candidates, rawValue, priority, source);
        }

        private static bool IsPlausibleThicknessRaw(string value)
        {
            return IsPlausibleSheetThickness(value, ThicknessCandidateResolver.MaxAutoDetectedSheetMm);
        }

        private static bool IsPlausibleSheetThickness(string value, double maxMm = 25d)
        {
            if (string.IsNullOrWhiteSpace(value))
                return false;

            var text = Regex.Replace(value.Trim(), @"mm", string.Empty, RegexOptions.IgnoreCase).Trim();
            if (!TryParseThicknessValue(text, out var parsed))
                return false;

            return parsed >= 0.3d && parsed <= maxMm;
        }

        private static bool TryParseThicknessValue(string text, out double value)
        {
            value = 0d;
            if (string.IsNullOrWhiteSpace(text))
                return false;

            return double.TryParse(text, NumberStyles.Any, CultureInfo.GetCultureInfo("de-DE"), out value)
                   || double.TryParse(text, NumberStyles.Any, CultureInfo.InvariantCulture, out value);
        }

        private static string NormalizeThickness(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
                return string.Empty;

            var text = Regex.Replace(raw.Trim(), @"mm", string.Empty, RegexOptions.IgnoreCase).Trim();

            if (!TryParseThicknessValue(text, out var value))
                return string.Empty;

            if (value <= 0 || value > 200)
                return string.Empty;

            return value.ToString("0.0", CultureInfo.GetCultureInfo("de-DE")) + " mm";
        }
    }
}
