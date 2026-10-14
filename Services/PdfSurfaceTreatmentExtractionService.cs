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

        /// <summary>Bürstrichtung + Oberflächenbehandlung „…gebürstet“.</summary>
        public bool HasBrushed { get; set; }

        public bool HasNickelPlating { get; set; }

        /// <summary>Freier Text aus dem Schriftfeld, der kein RAL/Elox/Vernickelt/Gebürstet ist.</summary>
        public bool HasGenericSurfaceTreatment { get; set; }

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
            "OBERFL CHENBEHANDLUNG",
            "OBERFL.BEHANDLUNG",
            "OBERFL BEHANDLUNG",
            "OBERFLAECHEN BEHANDLUNG",
            "OBERFLAECHENBEHANDL",
            "PULVERBESCHICHTUNG",
            "PULVERLACK",
            "EINBRENNLACK",
            "SURFACE TREATMENT",
            "SURFACE FINISH",
            "POWDER COAT",
            "FINISH",
            "COATING"
        };

        private static readonly Regex RalCodeRegex = new Regex(
            @"\bRAL\s*[-/:]?\s*([0-9]{4})\b",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

        private static readonly Regex RalCodeLooseRegex = new Regex(
            @"\bRAL[^0-9A-Z]{0,12}([0-9OIl]{4})\b",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

        private static readonly Regex SurfaceSectionRegex = new Regex(
            @"(?:OBERFL(?:AE|[AÄ]|\s*)?CHE?N?\s*\.?\s*BEHANDLUNG|PULVERBESCHICHTUNG|SURFACE\s*TREATMENT|SURFACE\s*FINISH|POWDER\s*COAT)\s*[:\-]?\s*([^\r\n|]{0,120})",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

        private static readonly string[] AnodizingTokens =
        {
            "ELOX", "ELOXIERT", "ELOXIERUNG", "ELOXAL", "ANODIS", "ANODIZED", "ANODISING",
            "ANODIZATION", "HARTELOX", "HARD ANOD", "ELOKTROLYTISCH"
        };

        private static readonly string[] NickelPlatingTokens =
        {
            "VERNICKELT", "VERNICKELUNG", "VERNICKELN", "NICKELPLAT", "NICKEL PLAT",
            "NICKEL-PLAT", "CHEMISCH NICKEL", "CHEMISCHES NICKEL"
        };

        private static readonly Regex NickelPlatingRegex = new Regex(
            @"VERN(?:I|\s*I)CKELT|VERNICKELUNG|NICKEL[\s\-]?PLAT|CHEMISCH(?:ES)?\s+NICKEL",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

        private static readonly string[] PowderTokens =
        {
            "EINBRENNLACK", "PULVER", "POWDER", "BESCHICHTET", "LACKIERT", "COATING"
        };

        private static readonly string[] RoughnessTableTokens =
        {
            "ROHE OBERFL", "ROHE OBER", "ROUGH",
            "GESCHRUPPT", "GESCHLICHTET", "GESCHLIFFEN", "POLIERT"
        };

        private static readonly string[] SurfaceFieldStopTokens =
        {
            "ZULAESSIGE", "ZULÄSSIGE", "ZULASSIGE", "ROUGH",
            "GESCHRUPPT", "GESCHLICHTET", "GESCHLIFFEN",
            "ROHE OBER", "ROHE OBERFL", "ALLGEMEINTOLERANZ",
            "MASSSTAB", "MAßSTAB", "GEWICHT", "WERKSTOFF",
            "ZEICHNUNGS", "AENDERUNG", "ÄNDERUNG", "BLATT",
            "BEMERKUNG", "TOLERANZ", "EUROPEAN", "PROJECTION",
            "FORMAT", "BAUTEIL", "KONSTRUKTEUR", "GEZEICHNET",
            "GEPRUEFT", "GEPRÜFT", "ABWEICHUNG", "DATEIPFAD",
            "DATIPFAD", "KATEGORIE", "ENTSTANDEN", "AUSSENMA",
            "ZEICHNUNGSNR", "ZEICHNUNGSNUMMER"
        };

        private static readonly string[] TitleBlockBoilerplateTokens =
        {
            "EUROPEAN", "PROJECTION", "FORMAT", "MASSSTAB", "MAßSTAB",
            "BAUTEILBENENNUNG", "BAUTEIL", "ZEICHNUNGSNUMMER", "KONSTRUKTEUR",
            "GEZEICHNET", "GEPRUEFT", "GEPRÜFT", "ZULAESSIGE", "ZULÄSSIGE",
            "ABWEICHUNG", "ALLGEMEINTOLERANZ", "DATEIPFAD", "DATIPFAD",
            "KATEGORIE", "ENTSTANDEN", "AENDERUNGSLAGE", "ÄNDERUNGSLAGE",
            "TESLA AUTOMATION", "AUSSENMA"
        };

        private static readonly Regex SurfacePlaceholderRegex = new Regex(
            @"^(?:[-–—./]+|keine|ohne|nein|n\.?\s*a\.?|n/?a|none|blank|nicht\s+angegeben|nicht\s+vorgesehen)$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

        private static readonly Regex SurfaceThicknessRegex = new Regex(
            @"\b(\d+(?:[.,]\d+)?)\s*(?:µ\s*m|μm|um)\b",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

        // PDF-Textlayer: Umlaute oft als Leerzeichen → „geb rstet“, „B rstrichtung“, „Oberfl chen…“
        private static readonly Regex BrushDirectionRegex = new Regex(
            @"B(?:UE|[ÜüUu]|\s*)R?S\s*T\s*RICHTUNG|BUERSTRICHTUNG|B\s*RSTRICHTUNG|BRUSH(?:ING)?\s*DIRECTION",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

        private static readonly Regex BrushedFinishRegex = new Regex(
            @"einseitig\s+geb(?:ue|[üu]|\s+)rstet|\bgeb(?:ue|[üu]|\s+)rstet\b|\bgebrstet\b|\bbrushed\b",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

        private static readonly Regex SurfaceBrushedContextRegex = new Regex(
            @"OBERFL(?:AE|[AÄ]|\s*)?CHE?N?\s*\.?\s*BEHANDLUNG[^\r\n]{0,120}?(?:einseitig\s+)?geb(?:ue|[üu]|\s+)rstet",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

        private static readonly Regex OneSidedBrushedRegex = new Regex(
            @"einseitig\s+geb(?:ue|[üu]|\s+)rstet",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

        private static readonly Regex GebuerstetLooseRegex = new Regex(
            @"geb(?:ue|[üu]|\s+)rstet|\bgebrstet\b|\bbrushed\b",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

        private const float TitleBlockBottomFraction = 0.42f;

        private static readonly Dictionary<string, PdfSurfaceTreatmentInfo> SurfaceCache =
            new Dictionary<string, PdfSurfaceTreatmentInfo>(StringComparer.OrdinalIgnoreCase);
        private static readonly object SurfaceCacheLock = new object();

        public static void ClearCache()
        {
            lock (SurfaceCacheLock)
                SurfaceCache.Clear();
        }

        /// <summary>Letzter Extract-Lauf: wurde Gebürstet erkannt?</summary>
        public static bool LastExtractHadBrushed { get; private set; }

        public static PdfSurfaceTreatmentInfo Extract(string pdfPath, int pageNumber = 1)
        {
            LastExtractHadBrushed = false;
            var info = new PdfSurfaceTreatmentInfo();
            if (string.IsNullOrWhiteSpace(pdfPath) || !File.Exists(pdfPath))
                return info;

            var teslaSurface = WatermarkProcessingContext.IsTeslaSurfaceStampEnabled;
            var cacheKey = pdfPath + "|" + pageNumber + "|" + File.GetLastWriteTimeUtc(pdfPath).Ticks
                           + "|tesla=" + teslaSurface;
            lock (SurfaceCacheLock)
            {
                if (SurfaceCache.TryGetValue(cacheKey, out var cached))
                {
                    LastExtractHadBrushed = cached != null && cached.HasBrushed;
                    return cached;
                }
            }

            info = ExtractUncached(pdfPath, pageNumber);
            LastExtractHadBrushed = info != null && info.HasBrushed;
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

            // Metadaten aus Schriftfeld; RAL zusätzlich direkt im Schriftfeld-Text
            // (Label und Wert oft in getrennten Rects / Umlaute als Leerzeichen).
            foreach (var source in new[] { titleBlockText, pageText })
                TryApplySurfaceMetadata(info, source);

            TryApplyRalCode(info, ExtractSurfaceSection(Normalize(titleBlockText)), allowStandalonePowderNumber: true);
            if (string.IsNullOrWhiteSpace(info.RalCode))
                TryApplyRalCode(info, Normalize(titleBlockText), allowStandalonePowderNumber: false);
            if (string.IsNullOrWhiteSpace(info.RalCode))
                TryApplyRalCode(info, ExtractSurfaceSection(Normalize(pageText)), allowStandalonePowderNumber: true);

            TryApplyRalFromTitleBlockRects(info, layout.TextRects, layout.PageWidth, layout.PageHeight);

            if (string.IsNullOrWhiteSpace(info.RalCode)
                || string.IsNullOrWhiteSpace(info.SurfaceTreatmentText))
            {
                var ocrText = ReadOcrFallback(pdfPath, pageNumber);
                if (!string.IsNullOrWhiteSpace(ocrText))
                {
                    TryApplySurfaceMetadata(info, ocrText);
                    TryApplyRalCode(info, ExtractSurfaceSection(Normalize(ocrText)), allowStandalonePowderNumber: true);
                    if (string.IsNullOrWhiteSpace(info.RalCode))
                        TryApplyRalCode(info, Normalize(ocrText), allowStandalonePowderNumber: false);
                    if (WatermarkProcessingContext.IsTeslaSurfaceStampEnabled)
                        TryCaptureSurfaceFieldText(info, ocrText);
                }
            }

            if (WatermarkProcessingContext.IsTeslaSurfaceStampEnabled)
            {
                TryCaptureSurfaceFieldFromRects(info, layout.TextRects, layout.PageWidth, layout.PageHeight);
                SanitizeCapturedSurfaceField(info);
            }

            if (string.IsNullOrWhiteSpace(info.RalCode)
                && !WatermarkProcessingContext.BatchMode)
            {
                var vision = DrawingVisionRecognitionService.TryAnalyze(pdfPath, pageNumber);
                if (vision != null && vision.HasReliableRal)
                    info.RalCode = vision.RalCode;
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

            TryApplyBrushedFinish(info, titleBlockText, pageText, rectText, pdfPath, pageNumber);
            if (WatermarkProcessingContext.IsTeslaSurfaceStampEnabled)
            {
                TryApplyNickelPlating(info, titleBlockText, pageText, rectText, pdfPath, pageNumber);
                SanitizeCapturedSurfaceField(info);
                info.HasGenericSurfaceTreatment = IsGenericSurfaceField(info);
            }
            else
            {
                info.HasNickelPlating = false;
                info.HasGenericSurfaceTreatment = false;
            }

            return info;
        }

        internal static string ResolveNickelPlatingStampText(PdfSurfaceTreatmentInfo info)
        {
            if (info == null || !info.HasNickelPlating)
                return string.Empty;

            if (IsNickelLikeText(info.SurfaceTreatmentText))
                return FormatSurfaceStampText(info.SurfaceTreatmentText);

            return "Vernickelt";
        }

        internal static string ResolveGenericSurfaceStampText(PdfSurfaceTreatmentInfo info)
        {
            if (info == null || !info.HasGenericSurfaceTreatment)
                return string.Empty;

            return FormatSurfaceStampText(info.SurfaceTreatmentText);
        }

        /// <summary>
        /// Gebürstet-Erkennung: Textlayer + OCR. „gebürstet“ mit Oberflächen-/Bürstrichtungs-Kontext.
        /// </summary>
        private static void TryApplyBrushedFinish(
            PdfSurfaceTreatmentInfo info,
            string titleBlockText,
            string pageText,
            string rectText,
            string pdfPath = null,
            int pageNumber = 1)
        {
            if (info == null)
                return;

            var combined = Normalize(
                (titleBlockText ?? string.Empty) + "\n"
                + (pageText ?? string.Empty) + "\n"
                + (rectText ?? string.Empty));

            // OCR Schriftfeld immer dazunehmen – EcoMark oft ohne brauchbaren Textlayer
            if (!string.IsNullOrWhiteSpace(pdfPath))
            {
                var ocrText = Normalize(ReadOcrFallback(pdfPath, pageNumber));
                if (!string.IsNullOrWhiteSpace(ocrText))
                    combined = combined + "\n" + ocrText;
            }

            if (string.IsNullOrWhiteSpace(combined))
            {
                LogBrushedDetect(pdfPath, false, "empty-text");
                return;
            }

            // App-Wasserzeichen „Gebürstet“ nicht als Nachweis werten
            combined = Regex.Replace(
                combined,
                @"^\s*GEB(?:UE|[ÜüUu])RSTET\s*$",
                " ",
                RegexOptions.Multiline | RegexOptions.IgnoreCase);

            var compact = Regex.Replace(combined, @"[^A-Za-z0-9]", string.Empty);
            var hasGebuerstet = GebuerstetLooseRegex.IsMatch(combined)
                || compact.IndexOf("GEBRSTET", StringComparison.OrdinalIgnoreCase) >= 0
                || compact.IndexOf("GEBUERSTET", StringComparison.OrdinalIgnoreCase) >= 0;
            var hasEinseitigGebuerstet = OneSidedBrushedRegex.IsMatch(combined)
                || Regex.IsMatch(
                    combined,
                    @"einseitig.{0,24}geb(?:ue|[üu]|\s+)rstet",
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)
                || (compact.IndexOf("EINSEITIG", StringComparison.OrdinalIgnoreCase) >= 0
                    && (compact.IndexOf("GEBRSTET", StringComparison.OrdinalIgnoreCase) >= 0
                        || compact.IndexOf("GEBUERSTET", StringComparison.OrdinalIgnoreCase) >= 0));
            var hasOberfl = SurfaceBrushedContextRegex.IsMatch(combined)
                || combined.IndexOf("OBERFLAECHENBEHANDLUNG", StringComparison.OrdinalIgnoreCase) >= 0
                || combined.IndexOf("OBERFLACHEBEHANDLUNG", StringComparison.OrdinalIgnoreCase) >= 0
                || (combined.IndexOf("OBERFL", StringComparison.OrdinalIgnoreCase) >= 0
                    && combined.IndexOf("BEHANDLUNG", StringComparison.OrdinalIgnoreCase) >= 0);
            var hasBrushDirection = BrushDirectionRegex.IsMatch(combined)
                                    || compact.IndexOf("BUESTRICHTUNG", StringComparison.OrdinalIgnoreCase) >= 0
                                    || compact.IndexOf("BURSTRICHTUNG", StringComparison.OrdinalIgnoreCase) >= 0
                                    || compact.IndexOf("BRSTRICHTUNG", StringComparison.OrdinalIgnoreCase) >= 0
                                    || compact.IndexOf("BUESTRICHT", StringComparison.OrdinalIgnoreCase) >= 0;

            var surfaceSection = ExtractSurfaceSection(combined);
            var surfaceOk = (!string.IsNullOrWhiteSpace(surfaceSection)
                             && BrushedFinishRegex.IsMatch(surfaceSection))
                            || hasEinseitigGebuerstet
                            || SurfaceBrushedContextRegex.IsMatch(combined);

            // Entscheidung: gebürstet + (Oberfläche / einseitig / Bürstrichtung)
            var ok = surfaceOk
                     || (hasGebuerstet && (hasOberfl || hasBrushDirection || hasEinseitigGebuerstet))
                     || (hasBrushDirection && hasGebuerstet);

            LogBrushedDetect(
                pdfPath,
                ok,
                "geb=" + hasGebuerstet
                + " einseitig=" + hasEinseitigGebuerstet
                + " oberfl=" + hasOberfl
                + " dir=" + hasBrushDirection
                + " surfaceOk=" + surfaceOk);

            if (!ok)
                return;

            info.HasBrushed = true;
            info.HasSurfaceTreatmentEntry = true;
            if (string.IsNullOrWhiteSpace(info.SurfaceTreatmentText))
                info.SurfaceTreatmentText = "einseitig gebürstet";
        }

        private static void TryApplyNickelPlating(
            PdfSurfaceTreatmentInfo info,
            string titleBlockText,
            string pageText,
            string rectText,
            string pdfPath = null,
            int pageNumber = 1)
        {
            if (info == null || info.HasNickelPlating)
                return;

            var combined = Normalize(
                (titleBlockText ?? string.Empty) + "\n"
                + (pageText ?? string.Empty) + "\n"
                + (rectText ?? string.Empty));

            if (!string.IsNullOrWhiteSpace(pdfPath))
            {
                var ocrText = Normalize(ReadOcrFallback(pdfPath, pageNumber));
                if (!string.IsNullOrWhiteSpace(ocrText))
                    combined = combined + "\n" + ocrText;
            }

            if (string.IsNullOrWhiteSpace(combined))
                return;

            var compact = Regex.Replace(combined, @"[^A-Za-z0-9]", string.Empty);
            var found = NickelPlatingRegex.IsMatch(combined)
                        || ContainsAnyToken(combined, NickelPlatingTokens)
                        || compact.IndexOf("VERNICKELT", StringComparison.OrdinalIgnoreCase) >= 0
                        || compact.IndexOf("NICKELPLAT", StringComparison.OrdinalIgnoreCase) >= 0;
            if (!found)
                return;

            info.HasNickelPlating = true;
            info.HasSurfaceTreatmentEntry = true;
            if (string.IsNullOrWhiteSpace(info.SurfaceTreatmentText) || !IsNickelLikeText(info.SurfaceTreatmentText))
            {
                var fromSection = ExtractSurfaceSection(combined);
                info.SurfaceTreatmentText = IsNickelLikeText(fromSection)
                    ? FormatSurfaceStampText(fromSection)
                    : "Vernickelt";
            }
        }

        private static void LogBrushedDetect(string pdfPath, bool hasBrushed, string detail)
        {
            try
            {
                var dir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "DOK-V01");
                Directory.CreateDirectory(dir);
                var line = DateTime.Now.ToString("s")
                           + " HasBrushed=" + hasBrushed
                           + " " + detail
                           + " file=" + (pdfPath ?? string.Empty)
                           + Environment.NewLine;
                File.AppendAllText(Path.Combine(dir, "brushed-debug.log"), line);
            }
            catch
            {
                // ignore
            }
        }

        private static string ReadOcrFallback(string pdfPath, int pageNumber)
        {
            var ocrText = TitleBlockOcrService.TryReadTitleBlockThorough(pdfPath, pageNumber) ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(ocrText))
                return ocrText;

            return TitleBlockOcrService.TryReadTitleBlock(pdfPath, pageNumber) ?? string.Empty;
        }

        private static void TryApplySurfaceMetadata(PdfSurfaceTreatmentInfo info, string text)
        {
            if (info == null || string.IsNullOrWhiteSpace(text))
                return;

            var normalized = Normalize(text);
            var section = ExtractSurfaceSection(normalized);
            var originalSection = ExtractSurfaceSection(text);
            if (string.IsNullOrWhiteSpace(section) && string.IsNullOrWhiteSpace(originalSection))
                return;

            if (!HasMeaningfulSurfaceContent(section) && !HasMeaningfulSurfaceContent(originalSection))
                return;

            var display = !string.IsNullOrWhiteSpace(originalSection) && HasKnownSurfaceContent(originalSection)
                ? originalSection
                : section;
            if (string.IsNullOrWhiteSpace(info.SurfaceTreatmentText)
                && HasKnownSurfaceContent(display)
                && !ContainsTitleBlockBoilerplate(display))
            {
                info.SurfaceTreatmentText = FormatSurfaceStampText(display);
            }

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
            info.HasNickelPlating = info.HasNickelPlating
                || ContainsAnyToken(section, NickelPlatingTokens)
                || NickelPlatingRegex.IsMatch(section);

            if (info.HasPowderCoating || info.HasAnodizing || info.HasNickelPlating
                || !string.IsNullOrWhiteSpace(info.SurfaceTreatmentText))
            {
                info.HasSurfaceTreatmentEntry = true;
            }
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

            var watermarkBand = Math.Max(28f, pageHeight * 0.045f);
            var bottomLimit = pageHeight * TitleBlockBottomFraction;
            var titleRects = rects
                .Where(r => r != null
                            && r.Bottom <= bottomLimit
                            && r.Bottom >= watermarkBand
                            && !string.IsNullOrWhiteSpace(r.Text))
                .ToList();

            if (titleRects.Count == 0)
                return;

            var surfaceRects = titleRects
                .Where(r => ContainsAnyToken(r.Text, SurfaceTreatmentLabels)
                            || SurfaceSectionRegex.IsMatch(Normalize(r.Text)))
                .ToList();

            var searchRects = titleRects
                .Where(r => surfaceRects.Count == 0
                            || IsNearSurfaceTreatmentArea(r, surfaceRects, pageWidth, pageHeight)
                            || ContainsAnyToken(r.Text, SurfaceTreatmentLabels)
                            || (r.Text ?? string.Empty).IndexOf("RAL", StringComparison.OrdinalIgnoreCase) >= 0)
                .ToList();

            foreach (var rect in searchRects)
            {
                var text = (rect.Text ?? string.Empty).Trim();
                if (string.IsNullOrWhiteSpace(text))
                    continue;

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

        private static void TryCaptureSurfaceFieldFromRects(
            PdfSurfaceTreatmentInfo info,
            IList<PdfTextRect> rects,
            float pageWidth,
            float pageHeight)
        {
            if (info == null || rects == null || rects.Count == 0)
                return;

            var bottomLimit = pageHeight * TitleBlockBottomFraction;
            var watermarkBand = Math.Max(28f, pageHeight * 0.045f);
            var titleRects = rects
                .Where(r => r != null
                            && r.Bottom <= bottomLimit
                            && r.Bottom >= watermarkBand
                            && !string.IsNullOrWhiteSpace(r.Text))
                .ToList();
            if (titleRects.Count == 0)
                return;

            var labels = titleRects.Where(r => IsSurfaceLabelText(r.Text)).ToList();
            if (labels.Count == 0)
                return;

            foreach (var label in labels.OrderByDescending(r => r.Top))
            {
                var inline = StripSurfaceLabelPrefix(label.Text);
                if (TryAssignSurfaceFieldText(info, inline, overwrite: true))
                    return;

                var nearby = new List<PdfTextRect>();
                var belowLimit = Math.Max(48f, pageHeight * 0.055f);
                foreach (var candidate in titleRects)
                {
                    if (candidate == label
                        || IsSurfaceLabelText(candidate.Text)
                        || IsSurfaceStopText(candidate.Text)
                        || ContainsTitleBlockBoilerplate(candidate.Text))
                    {
                        continue;
                    }

                    var below = candidate.Top <= label.Bottom + 4f
                                && candidate.Bottom >= label.Bottom - belowLimit
                                && candidate.Left >= label.Left - 10f
                                && candidate.Right <= label.Right + 28f;
                    if (below)
                        nearby.Add(candidate);
                }

                var joined = string.Join(
                    " ",
                    nearby.OrderByDescending(r => r.Top).ThenBy(r => r.Left)
                        .Select(r => (r.Text ?? string.Empty).Trim())
                        .Where(t => !string.IsNullOrWhiteSpace(t)));
                if (TryAssignSurfaceFieldText(info, joined, overwrite: true))
                    return;
            }
        }

        private static void TryCaptureSurfaceFieldText(PdfSurfaceTreatmentInfo info, string text)
        {
            if (info == null || string.IsNullOrWhiteSpace(text))
                return;

            TryAssignSurfaceFieldText(info, ExtractSurfaceSection(text));
        }

        private static bool TryAssignSurfaceFieldText(PdfSurfaceTreatmentInfo info, string raw, bool overwrite = false)
        {
            var formatted = FormatSurfaceStampText(raw);
            if (!IsStampableGenericSurfaceValue(formatted) && !HasKnownSurfaceContent(formatted))
                return false;

            info.HasSurfaceTreatmentEntry = true;
            if (overwrite
                || string.IsNullOrWhiteSpace(info.SurfaceTreatmentText)
                || IsWeakSurfaceFieldText(info.SurfaceTreatmentText)
                || ContainsTitleBlockBoilerplate(info.SurfaceTreatmentText))
            {
                info.SurfaceTreatmentText = formatted;
            }

            ApplySurfaceFlagsFromText(info, formatted);
            return true;
        }

        private static void ApplySurfaceFlagsFromText(PdfSurfaceTreatmentInfo info, string text)
        {
            if (info == null || string.IsNullOrWhiteSpace(text))
                return;

            if (string.IsNullOrWhiteSpace(info.RalCode))
                TryApplyRalCode(info, text, allowStandalonePowderNumber: true);

            info.HasPowderCoating = info.HasPowderCoating
                || !string.IsNullOrWhiteSpace(info.RalCode)
                || ContainsAnyToken(text, PowderTokens);
            info.HasAnodizing = info.HasAnodizing || ContainsAnyToken(text, AnodizingTokens);
            info.HasNickelPlating = info.HasNickelPlating
                || ContainsAnyToken(text, NickelPlatingTokens)
                || NickelPlatingRegex.IsMatch(text);
            info.HasBrushed = info.HasBrushed || BrushedFinishRegex.IsMatch(text);
        }

        private static bool IsGenericSurfaceField(PdfSurfaceTreatmentInfo info)
        {
            if (info == null || !IsStampableGenericSurfaceValue(info.SurfaceTreatmentText))
                return false;

            var text = info.SurfaceTreatmentText;
            if (info.HasPowderCoating && IsCoveredByKnownSurface(text, PowderTokens, isPowder: true, isAnodizing: false, isNickel: false, isBrushed: false))
                return false;
            if (info.HasAnodizing && IsCoveredByKnownSurface(text, AnodizingTokens, isPowder: false, isAnodizing: true, isNickel: false, isBrushed: false))
                return false;
            if (info.HasNickelPlating && IsCoveredByKnownSurface(text, NickelPlatingTokens, isPowder: false, isAnodizing: false, isNickel: true, isBrushed: false))
                return false;
            if (info.HasBrushed && IsCoveredByKnownSurface(text, null, isPowder: false, isAnodizing: false, isNickel: false, isBrushed: true))
                return false;

            return true;
        }

        private static bool IsCoveredByKnownSurface(
            string text,
            IEnumerable<string> tokens,
            bool isPowder,
            bool isAnodizing,
            bool isNickel,
            bool isBrushed)
        {
            var leftover = text ?? string.Empty;
            leftover = Regex.Replace(leftover, @"\bRAL\s*[-/:]?\s*[0-9OIl]{4}\b", " ", RegexOptions.IgnoreCase);
            leftover = Regex.Replace(leftover, @"\bRAL\b", " ", RegexOptions.IgnoreCase);
            leftover = SurfaceThicknessRegex.Replace(leftover, " ");
            leftover = Regex.Replace(leftover, @"\b\d+(?:[.,]\d+)?\b", " ");

            if (tokens != null)
            {
                foreach (var token in tokens)
                    leftover = Regex.Replace(leftover, Regex.Escape(token), " ", RegexOptions.IgnoreCase);
            }

            if (isNickel)
            {
                leftover = NickelPlatingRegex.Replace(leftover, " ");
                leftover = Regex.Replace(leftover, @"CHEMISCH(?:ES)?", " ", RegexOptions.IgnoreCase);
            }

            if (isAnodizing)
                leftover = Regex.Replace(leftover, @"ELOKTROLYTISCH|ELEKTROLYTISCH", " ", RegexOptions.IgnoreCase);

            if (isBrushed)
            {
                leftover = GebuerstetLooseRegex.Replace(leftover, " ");
                leftover = Regex.Replace(leftover, @"einseitig", " ", RegexOptions.IgnoreCase);
            }

            leftover = Regex.Replace(leftover, @"[^A-Za-zÄÖÜäöüß]", string.Empty);
            return leftover.Length < 3;
        }

        private static bool IsNickelLikeText(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return false;

            return ContainsAnyToken(text, NickelPlatingTokens) || NickelPlatingRegex.IsMatch(text);
        }

        private static bool IsWeakSurfaceFieldText(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return true;

            var trimmed = text.Trim();
            return trimmed.Equals("RAL", StringComparison.OrdinalIgnoreCase)
                   || trimmed.Equals("ELOX", StringComparison.OrdinalIgnoreCase)
                   || trimmed.Equals("Vernickelt", StringComparison.OrdinalIgnoreCase)
                   || trimmed.Equals("Gebürstet", StringComparison.OrdinalIgnoreCase)
                   || trimmed.Equals("einseitig gebürstet", StringComparison.OrdinalIgnoreCase);
        }

        private static string FormatSurfaceStampText(string text)
        {
            var cleaned = CleanupSection(text);
            if (string.IsNullOrWhiteSpace(cleaned) || ContainsTitleBlockBoilerplate(cleaned))
                return string.Empty;

            cleaned = SurfaceThicknessRegex.Replace(cleaned, "$1µm");
            if (cleaned.Length > 40)
                cleaned = cleaned.Substring(0, 40).Trim();

            return cleaned;
        }

        private static bool IsSurfaceLabelText(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return false;

            var compact = Regex.Replace(Normalize(text), @"[^A-Z0-9]", string.Empty);
            return compact.IndexOf("OBERFLAECHENBEHANDL", StringComparison.OrdinalIgnoreCase) >= 0
                   || compact.IndexOf("OBERFLCHENBEHANDL", StringComparison.OrdinalIgnoreCase) >= 0
                   || compact.IndexOf("SURFACETREATMENT", StringComparison.OrdinalIgnoreCase) >= 0
                   || compact.IndexOf("SURFACEFINISH", StringComparison.OrdinalIgnoreCase) >= 0
                   || ContainsAnyToken(text, SurfaceTreatmentLabels);
        }

        private static bool IsSurfaceStopText(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return false;

            if (ContainsTitleBlockBoilerplate(text)
                || IsRoughnessTableNoise(text))
                return true;

            var compact = Regex.Replace(Normalize(text), @"\s+", " ").Trim();
            if (compact.Equals("ROHE", StringComparison.OrdinalIgnoreCase)
                || compact.Equals("FEIN", StringComparison.OrdinalIgnoreCase)
                || compact.Equals("POLIERT", StringComparison.OrdinalIgnoreCase)
                || compact.StartsWith("ROHE ", StringComparison.OrdinalIgnoreCase)
                || compact.StartsWith("OBERFLAECHE RZ", StringComparison.OrdinalIgnoreCase)
                || compact.StartsWith("OBERFLACHE RZ", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return ContainsAnyToken(compact, SurfaceFieldStopTokens);
        }

        private static bool ContainsTitleBlockBoilerplate(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return false;

            if (ContainsAnyToken(text, TitleBlockBoilerplateTokens))
                return true;

            if (Regex.IsMatch(text, @"\bR[az]\s*\d", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
                return true;

            var compact = Regex.Replace(Normalize(text), @"[^A-Z0-9]", string.Empty);
            return compact.IndexOf("EUROPEANPROJECTION", StringComparison.OrdinalIgnoreCase) >= 0
                   || compact.IndexOf("FORMATA4", StringComparison.OrdinalIgnoreCase) >= 0
                   || compact.IndexOf("OBERFLAECHERZ", StringComparison.OrdinalIgnoreCase) >= 0
                   || compact.IndexOf("OBERFLACHERZ", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static void SanitizeCapturedSurfaceField(PdfSurfaceTreatmentInfo info)
        {
            if (info == null)
                return;

            if (ContainsTitleBlockBoilerplate(info.SurfaceTreatmentText)
                || IsRoughnessTableNoise(info.SurfaceTreatmentText)
                || IsSurfaceStopText(info.SurfaceTreatmentText)
                || SurfacePlaceholderRegex.IsMatch((info.SurfaceTreatmentText ?? string.Empty).Trim()))
            {
                info.SurfaceTreatmentText = string.Empty;
                info.HasGenericSurfaceTreatment = false;
                if (!info.HasPowderCoating && !info.HasAnodizing && !info.HasNickelPlating && !info.HasBrushed)
                    info.HasSurfaceTreatmentEntry = false;
            }
        }

        private static string StripSurfaceLabelPrefix(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return string.Empty;

            var cleaned = text.Trim();
            foreach (var label in SurfaceTreatmentLabels.OrderByDescending(l => l.Length))
            {
                if (cleaned.StartsWith(label, StringComparison.OrdinalIgnoreCase))
                {
                    cleaned = cleaned.Substring(label.Length);
                    break;
                }
            }

            return CleanupSection(cleaned);
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

            foreach (var stop in SurfaceFieldStopTokens.Concat(TitleBlockBoilerplateTokens))
            {
                var stopIdx = cleaned.IndexOf(stop, StringComparison.OrdinalIgnoreCase);
                if (stopIdx == 0)
                    return string.Empty;
                if (stopIdx > 0)
                    cleaned = cleaned.Substring(0, stopIdx).Trim();
            }

            var roughIdx = cleaned.IndexOf("ROHE", StringComparison.OrdinalIgnoreCase);
            if (roughIdx == 0)
                return string.Empty;
            if (roughIdx > 0)
                cleaned = cleaned.Substring(0, roughIdx).Trim();

            if (ContainsTitleBlockBoilerplate(cleaned) || IsRoughnessTableNoise(cleaned))
                return string.Empty;

            return cleaned;
        }

        private static bool IsRoughnessTableNoise(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return false;

            var compact = Regex.Replace(Normalize(text), @"[^A-Z0-9]", string.Empty);
            if (compact.IndexOf("BEHANDLUNG", StringComparison.OrdinalIgnoreCase) >= 0)
                return false;

            if (compact.Equals("OBERFLAECHE", StringComparison.OrdinalIgnoreCase)
                || compact.Equals("OBERFLACHE", StringComparison.OrdinalIgnoreCase)
                || compact.Equals("ROHEOBERFLAECHE", StringComparison.OrdinalIgnoreCase)
                || compact.Equals("ROHEOBERFLACHE", StringComparison.OrdinalIgnoreCase)
                || compact.Equals("ROHE", StringComparison.OrdinalIgnoreCase)
                || compact.Equals("ROUGH", StringComparison.OrdinalIgnoreCase)
                || compact.Equals("GESCHRUPPT", StringComparison.OrdinalIgnoreCase)
                || compact.Equals("GESCHLICHTET", StringComparison.OrdinalIgnoreCase)
                || compact.Equals("FEINGESCHLICHTET", StringComparison.OrdinalIgnoreCase)
                || compact.Equals("POLIERT", StringComparison.OrdinalIgnoreCase)
                || compact.Equals("FEIN", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (ContainsAnyToken(text, RoughnessTableTokens))
                return true;

            return Regex.IsMatch(
                text.Trim(),
                @"^(?:rohe\s*)?oberfl(?:ae|[äa])che$",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        }

        private static bool HasKnownSurfaceContent(string section)
        {
            if (string.IsNullOrWhiteSpace(section) || ContainsTitleBlockBoilerplate(section))
                return false;

            if (RalCodeRegex.IsMatch(section) || RalCodeLooseRegex.IsMatch(section))
                return true;

            if (section.IndexOf("RAL", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;

            if (ContainsAnyToken(section, PowderTokens) || ContainsAnyToken(section, AnodizingTokens))
                return true;

            if (ContainsAnyToken(section, NickelPlatingTokens) || NickelPlatingRegex.IsMatch(section))
                return true;

            if (BrushedFinishRegex.IsMatch(section))
                return true;

            return IsStandaloneRalCandidate(section.Trim())
                   || !string.IsNullOrWhiteSpace(ExtractStandaloneSurfaceRalNumber(section));
        }

        private static bool IsStampableGenericSurfaceValue(string section)
        {
            if (string.IsNullOrWhiteSpace(section)
                || IsLabelOnly(section)
                || IsSurfaceStopText(section)
                || ContainsTitleBlockBoilerplate(section)
                || IsRoughnessTableNoise(section)
                || SurfacePlaceholderRegex.IsMatch(section.Trim()))
            {
                return false;
            }

            if (HasKnownSurfaceContent(section))
                return true;

            if (section.Length > 40)
                return false;

            var letters = Regex.Replace(section, @"[^A-Za-zÄÖÜäöüß]", string.Empty);
            return letters.Length >= 4
                   && !Regex.IsMatch(section, @"\bR[az]\b", RegexOptions.IgnoreCase);
        }

        private static bool HasMeaningfulSurfaceContent(string section)
        {
            if (string.IsNullOrWhiteSpace(section)
                || IsLabelOnly(section)
                || IsSurfaceStopText(section)
                || ContainsTitleBlockBoilerplate(section))
            {
                return false;
            }

            var trimmed = section.Trim();
            if (SurfacePlaceholderRegex.IsMatch(trimmed))
                return false;

            return HasKnownSurfaceContent(section);
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
            var watermarkBand = Math.Max(28f, pageHeight * 0.045f);
            var titleRects = rects
                .Where(r => r != null && r.Bottom <= bottomLimit && r.Bottom >= watermarkBand)
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
