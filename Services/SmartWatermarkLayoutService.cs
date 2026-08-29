using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using PDF_umbenenen.Models;

namespace PDF_umbenenen.Services
{
    internal sealed class WatermarkFieldData
    {
        public List<string> QuantityLines { get; set; } = new List<string>();

        public string QuantityText { get; set; }

        public string DeliveryDateText { get; set; }

        public string DeliveryLocationText { get; set; }

        public string BgNumberText { get; set; }

        public List<string> BgLines { get; set; } = new List<string>();

        public string OrderNumberText { get; set; }
    }

    internal sealed class WatermarkTextPlacement
    {
        public string Text { get; set; }

        public float X { get; set; }

        public float Y { get; set; }

        public int Alignment { get; set; }

        public float Angle { get; set; }

        public float FontSize { get; set; } = 22f;

        public bool UseRedText { get; set; }

        public string Category { get; set; } = string.Empty;
    }

    internal static class SmartWatermarkLayoutService
    {
        public static List<WatermarkTextPlacement> Compute(
            string pdfPath,
            int pageNumber,
            WatermarkFieldData fields,
            string drawingNumberHint,
            float pageWidth,
            float pageHeight,
            WatermarkSettings settings = null)
        {
            var placements = new List<WatermarkTextPlacement>();
            if (fields == null)
                return placements;

            var layout = PdfLayoutAnalysisService.AnalyzePage(pdfPath, pageNumber);
            if (layout.PageWidth > 0)
                pageWidth = layout.PageWidth;
            if (layout.PageHeight > 0)
                pageHeight = layout.PageHeight;

            if (PdfRasterLayoutService.IsAvailable && !WatermarkProcessingContext.BatchMode)
                PdfRasterLayoutService.EnsurePageGrid(pdfPath, pageNumber, pageWidth, pageHeight);

            var occupied = layout.TextRects
                .Select(r => Inflate(r, 6f))
                .ToList();

            if (!string.IsNullOrWhiteSpace(fields.OrderNumberText))
            {
                var orderText = fields.OrderNumberText.Trim();
                var fontSize = WatermarkPageScale.GetPageNumberFontSize(pageWidth, pageHeight);
                var placement = new WatermarkTextPlacement
                {
                    Text = orderText,
                    Category = "Order",
                    FontSize = fontSize
                };
                ApplyFixedTopRightOrderPlacement(placement, pageWidth, pageHeight);
                placements.Add(placement);
                occupied.Add(MakeTextRect(placement, pageWidth));
            }

            if (!string.IsNullOrWhiteSpace(fields.DeliveryLocationText)
                && !IsSubgroupPartDrawing(drawingNumberHint, fields.BgNumberText))
            {
                var locationText = fields.DeliveryLocationText.Trim();
                var fontSize = WatermarkPageScale.GetPageNumberFontSize(pageWidth, pageHeight);
                var placement = PlaceTopCenter(locationText, fontSize, pageWidth, pageHeight, occupied);
                placement.Category = "Location";
                placements.Add(placement);
            }

            if (!string.IsNullOrWhiteSpace(fields.DeliveryDateText)
                && (fields.QuantityLines == null || fields.QuantityLines.Count == 0))
            {
                var dateText = fields.DeliveryDateText.Trim();
                var fontSize = WatermarkPageScale.GetPageNumberFontSize(pageWidth, pageHeight);
                var placement = PlaceTopLeftFree(dateText, fontSize, pageWidth, pageHeight, occupied);
                placement.UseRedText = true;
                placement.Category = "Date";
                placements.Add(placement);
            }

            if (fields.QuantityLines != null && fields.QuantityLines.Count > 0)
            {
                // Startposition links; ReflowCenteredTopBandRow setzt links oder rechts laut Einstellung.
                var stackPlacements = PlaceQuantityStackTopLeft(fields.QuantityLines, pageWidth, pageHeight);
                foreach (var placement in stackPlacements)
                {
                    placement.Category = "Quantity";
                    placements.Add(placement);
                }
            }
            else if (!string.IsNullOrWhiteSpace(fields.QuantityText))
            {
                var stackPlacements = PlaceQuantityStackTopLeft(new List<string> { fields.QuantityText.Trim() }, pageWidth, pageHeight);
                foreach (var placement in stackPlacements)
                {
                    placement.Category = "Quantity";
                    placements.Add(placement);
                }
            }

            EnsureTopBandSeparation(placements, pageWidth, pageHeight, pdfPath, pageNumber);
            foreach (var placement in placements.Where(p =>
                         p != null
                         && (string.Equals(p.Category, "Location", StringComparison.OrdinalIgnoreCase)
                             || string.Equals(p.Category, "Date", StringComparison.OrdinalIgnoreCase)
                             || string.Equals(p.Category, "Quantity", StringComparison.OrdinalIgnoreCase))))
            {
                occupied.Add(MakeTextRect(placement, pageWidth));
            }

            if (IsBgWatermarkEnabled(settings)
                && fields.BgLines != null
                && fields.BgLines.Count > 0)
            {
                var bgPlacements = PlaceBgStack(
                    fields.BgLines,
                    pdfPath,
                    pageNumber,
                    pageWidth,
                    pageHeight,
                    occupied,
                    settings);
                foreach (var placement in bgPlacements)
                {
                    placement.Category = "Bg";
                    placements.Add(placement);
                    occupied.Add(MakeTextRect(placement, pageWidth));
                }
            }
            else if (IsBgWatermarkEnabled(settings)
                     && !string.IsNullOrWhiteSpace(fields.BgNumberText))
            {
                var bgPlacements = PlaceBgStack(
                    SplitBgLines(fields.BgNumberText),
                    pdfPath,
                    pageNumber,
                    pageWidth,
                    pageHeight,
                    occupied,
                    settings);
                foreach (var placement in bgPlacements)
                {
                    placement.Category = "Bg";
                    placements.Add(placement);
                    occupied.Add(MakeTextRect(placement, pageWidth));
                }
            }

            MaterialPlacementGeometry.AppendReservedWatermarkRects(occupied, pageWidth, pageHeight);

            TryAddMaterialPlacements(
                placements,
                pdfPath,
                pageNumber,
                pageWidth,
                pageHeight,
                occupied,
                settings,
                drawingNumberHint);

            TryAddSurfaceTreatmentPlacements(
                placements,
                pdfPath,
                pageNumber,
                pageWidth,
                pageHeight,
                occupied,
                settings);

            EnforceFixedTopBandPlacements(placements, pageWidth, pageHeight, pdfPath, pageNumber);
            return ApplyLearning(drawingNumberHint, placements, pageWidth, pageHeight, pdfPath, pageNumber, occupied, settings);
        }

        public static bool IsLockedTopBandPlacement(
            WatermarkTextPlacement placement,
            IList<WatermarkTextPlacement> allPlacements = null)
        {
            if (placement == null || string.IsNullOrWhiteSpace(placement.Category))
                return false;

            if (string.Equals(placement.Category, "Location", StringComparison.OrdinalIgnoreCase)
                || string.Equals(placement.Category, "Date", StringComparison.OrdinalIgnoreCase)
                || string.Equals(placement.Category, "Order", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (!string.Equals(placement.Category, "Quantity", StringComparison.OrdinalIgnoreCase))
                return false;

            return true;
        }

        public static bool IsLockedSurfaceTreatmentPlacement(WatermarkTextPlacement placement)
        {
            if (placement == null || string.IsNullOrWhiteSpace(placement.Category))
                return false;

            return string.Equals(placement.Category, "PowderCoating", StringComparison.OrdinalIgnoreCase)
                   || string.Equals(placement.Category, "Anodizing", StringComparison.OrdinalIgnoreCase);
        }

        public static void PreserveMaterialFontSizes(
            List<WatermarkTextPlacement> placements,
            float pageWidth,
            float pageHeight)
        {
            UnifyMaterialAndSurfaceFontSizes(placements, pageWidth, pageHeight);
        }

        public static void PreserveSurfaceTreatmentFontSizes(
            List<WatermarkTextPlacement> placements,
            float pageWidth,
            float pageHeight)
        {
            if (placements == null || pageWidth <= 0f || pageHeight <= 0f)
                return;

            var fontSize = WatermarkPageScale.GetPageNumberFontSize(pageWidth, pageHeight);
            foreach (var placement in placements)
            {
                if (IsLockedSurfaceTreatmentPlacement(placement))
                    placement.FontSize = fontSize;
            }
        }

        public static void UnifyMaterialAndSurfaceFontSizes(
            List<WatermarkTextPlacement> placements,
            float pageWidth,
            float pageHeight)
        {
            if (placements == null || pageWidth <= 0f || pageHeight <= 0f)
                return;

            // Material Art/Stärke = gleiche Schriftgröße wie Auftragsnummer (Seitenband).
            var annotations = placements
                .Where(p => p != null
                            && string.Equals(p.Category, "Material", StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (annotations.Count == 0)
                return;

            var fontSize = WatermarkPageScale.GetPageNumberFontSize(pageWidth, pageHeight);
            foreach (var placement in annotations)
                placement.FontSize = fontSize;

            SyncMaterialStackFromTopLine(placements, fontSize);
        }

        public static float ResolveDrawingAnnotationFontSize(
            string text,
            float pageWidth,
            float pageHeight)
        {
            // Material Art/Stärke: gleiche Größe wie Auftragsnummer.
            return WatermarkPageScale.GetPageNumberFontSize(pageWidth, pageHeight);
        }

        public static float ResolveMaterialFontSizeForEditor(
            WatermarkTextPlacement placement,
            string longestLine,
            float pageWidth,
            float pageHeight)
        {
            return ResolveMaterialFontSize(placement, longestLine ?? string.Empty, pageWidth, pageHeight);
        }

        private static float ResolveMaterialFontSize(
            WatermarkTextPlacement placement,
            string longestLine,
            float pageWidth,
            float pageHeight)
        {
            return WatermarkPageScale.GetPageNumberFontSize(pageWidth, pageHeight);
        }

        public static void EnforceSurfaceTreatmentPlacements(
            List<WatermarkTextPlacement> placements,
            string layoutPdfPath,
            int pageNumber,
            float pageWidth,
            float pageHeight)
        {
            if (placements == null || pageWidth <= 0f || pageHeight <= 0f)
                return;

            var surface = new List<WatermarkTextPlacement>();
            foreach (var category in new[] { "PowderCoating", "Anodizing" })
            {
                var placement = placements.FirstOrDefault(p =>
                    p != null && string.Equals(p.Category, category, StringComparison.OrdinalIgnoreCase));
                if (placement == null || string.IsNullOrWhiteSpace(placement.Text))
                    continue;
                surface.Add(placement);
            }

            PlaceFixedBottomRightSurfaceStack(
                surface,
                layoutPdfPath,
                pageNumber,
                pageWidth,
                pageHeight);
        }

        /// <summary>
        /// Material fest unten links – gleiche Baseline wie Seitenzahl.
        /// </summary>
        public static void EnforceFixedBottomLeftMaterialPlacements(
            List<WatermarkTextPlacement> placements,
            string pdfPath,
            int pageNumber,
            float pageWidth,
            float pageHeight,
            WatermarkSettings settings = null)
        {
            if (placements == null || pageWidth <= 0f || pageHeight <= 0f)
                return;

            settings = settings ?? WatermarkSettingsStore.Load();
            if (!IsFixedBottomLeftMaterialPlacement(settings))
            {
                if (settings != null
                    && !settings.ExtractMaterialType
                    && !settings.ExtractMaterialThickness)
                {
                    placements.RemoveAll(p =>
                        p != null && string.Equals(p.Category, "Material", StringComparison.OrdinalIgnoreCase));
                }

                return;
            }

            var material = placements
                .Where(p => p != null
                            && string.Equals(p.Category, "Material", StringComparison.OrdinalIgnoreCase)
                            && !string.IsNullOrWhiteSpace(p.Text))
                .ToList();
            if (material.Count == 0)
                return;

            PlaceFixedBottomLeftMaterialStack(
                material,
                pdfPath,
                pageNumber,
                pageWidth,
                pageHeight);
        }

        public static bool IsFixedRightVerticalBgPlacement(WatermarkSettings settings)
        {
            // BG: feste Position rechts senkrecht, sofern aktiviert.
            return settings == null || settings.BgWatermarkEnabled;
        }

        /// <summary>
        /// BG fest rechts senkrecht (−90°), Schrift wie Seitenzahl.
        /// Seitenabstand = Abstand Lieferort von links (Rahmen/Blattrand).
        /// Mehrere BG nebeneinander (Spalten nach innen).
        /// </summary>
        public static void EnforceFixedRightVerticalBgPlacements(
            List<WatermarkTextPlacement> placements,
            string pdfPath,
            int pageNumber,
            float pageWidth,
            float pageHeight,
            WatermarkSettings settings = null)
        {
            if (placements == null || pageWidth <= 0f || pageHeight <= 0f)
                return;

            settings = settings ?? WatermarkSettingsStore.Load();
            if (!IsBgWatermarkEnabled(settings))
            {
                placements.RemoveAll(p =>
                    p != null && string.Equals(p.Category, "Bg", StringComparison.OrdinalIgnoreCase));
                return;
            }

            if (!IsFixedRightVerticalBgPlacement(settings))
                return;

            var bg = placements
                .Where(p => p != null
                            && string.Equals(p.Category, "Bg", StringComparison.OrdinalIgnoreCase)
                            && !string.IsNullOrWhiteSpace(p.Text))
                .ToList();
            if (bg.Count == 0)
                return;

            // Eine zusammengeklebte " | "-Zeile aufteilen.
            if (bg.Count == 1 && SplitBgLines(bg[0].Text).Count > 1)
            {
                var lines = SplitBgLines(bg[0].Text);
                placements.RemoveAll(p =>
                    p != null && string.Equals(p.Category, "Bg", StringComparison.OrdinalIgnoreCase));
                foreach (var line in lines)
                {
                    placements.Add(new WatermarkTextPlacement
                    {
                        Text = line,
                        Category = "Bg"
                    });
                }

                bg = placements
                    .Where(p => p != null
                                && string.Equals(p.Category, "Bg", StringComparison.OrdinalIgnoreCase)
                                && !string.IsNullOrWhiteSpace(p.Text))
                    .ToList();
            }

            PlaceFixedRightVerticalBgStack(bg, pdfPath, pageNumber, pageWidth, pageHeight);
        }

        private static void PlaceFixedRightVerticalBgStack(
            IList<WatermarkTextPlacement> bgPlacements,
            string pdfPath,
            int pageNumber,
            float pageWidth,
            float pageHeight)
        {
            if (bgPlacements == null || bgPlacements.Count == 0)
                return;

            var fontSize = WatermarkPageScale.GetPageNumberFontSize(pageWidth, pageHeight);
            var scale = WatermarkPageScale.Get(pageWidth, pageHeight);
            var edgeFallback = WatermarkPageScale.GetTopLeftMargin(pageWidth, pageHeight);
            var gapAboveFrame = Math.Max(2f, 2.5f * scale);

            float frameRight = pageWidth - edgeFallback;
            float frameTop = 0f;
            float detectedRight = 0f;
            var hasFrame = !string.IsNullOrWhiteSpace(pdfPath)
                && PdfRasterLayoutService.TryGetDrawingFrameBounds(
                    pdfPath,
                    pageNumber,
                    pageWidth,
                    pageHeight,
                    out _,
                    out detectedRight,
                    out frameTop);
            if (hasFrame)
                frameRight = detectedRight;

            // Gleicher Abstand Rahmen→Text wie bei Auftragsnummer oben (Baseline − Rahmenoberkante).
            var bandY = WatermarkPageScale.GetSharedTopBandBaselineY(
                pageWidth,
                pageHeight,
                fontSize,
                pdfPath,
                pageNumber,
                stackLines: 1);
            var frameGap = gapAboveFrame;
            if (hasFrame && frameTop > 0f)
                frameGap = Math.Max(gapAboveFrame, bandY - frameTop);

            // Senkrecht (−90°), zentriert: halbe Glyphenbreite, damit die Innenkante
            // denselben Abstand zum rechten Rahmen hat wie die Auftragsnummer oben.
            var halfGlyph = Math.Max(fontSize * 0.35f, 5f * scale);
            var firstX = frameRight + frameGap + halfGlyph;
            var baseY = pageHeight / 2f;
            var columnGap = Math.Max(fontSize + 6f, fontSize * 1.8f);
            var index = 0;
            foreach (var placement in bgPlacements)
            {
                if (placement == null || string.IsNullOrWhiteSpace(placement.Text))
                    continue;

                placement.FontSize = fontSize;
                // Mehrere BG nebeneinander: weitere Spalten nach außen (weg vom Rahmen).
                placement.X = firstX + (index * columnGap);
                placement.Y = baseY;
                placement.Alignment = 1;
                placement.Angle = -90f;
                index++;
            }
        }

        public static bool IsFixedBottomLeftMaterialPlacement(WatermarkSettings settings)
        {
            // Material: feste Position unten links, sofern Werkstoff/Stärke aktiv.
            if (settings == null)
                return true;
            return settings.ExtractMaterialType || settings.ExtractMaterialThickness;
        }

        public static bool IsBgWatermarkEnabled(WatermarkSettings settings)
        {
            return settings == null || settings.BgWatermarkEnabled;
        }

        /// <summary>
        /// Material fest unten links – seitlich wie Lieferort (Rahmen links),
        /// Höhe wie Seitenzahl.
        /// </summary>
        private static void PlaceFixedBottomLeftMaterialStack(
            IList<WatermarkTextPlacement> materialPlacements,
            string pdfPath,
            int pageNumber,
            float pageWidth,
            float pageHeight)
        {
            if (materialPlacements == null || materialPlacements.Count == 0)
                return;

            var fontSize = WatermarkPageScale.GetPageNumberFontSize(pageWidth, pageHeight);
            // Seitlich wie Lieferort oben: linker Zeichnungsrahmen (sonst Blattrand).
            var edgeFallback = WatermarkPageScale.GetTopLeftMargin(pageWidth, pageHeight);
            float leftX = edgeFallback;
            if (!string.IsNullOrWhiteSpace(pdfPath)
                && PdfRasterLayoutService.TryGetDrawingFrameBounds(
                    pdfPath,
                    pageNumber,
                    pageWidth,
                    pageHeight,
                    out var detectedLeft,
                    out _,
                    out _))
            {
                leftX = detectedLeft;
            }

            var baseline = WatermarkPageScale.GetSharedBottomBandBaselineY(
                pageWidth,
                pageHeight,
                fontSize,
                pdfPath,
                pageNumber);
            var lineHeight = fontSize + 6f;
            var index = 0;
            foreach (var placement in materialPlacements)
            {
                if (placement == null || string.IsNullOrWhiteSpace(placement.Text))
                    continue;

                placement.FontSize = fontSize;
                placement.X = leftX;
                // Erste Zeile auf Seitenzahl-Höhe; weitere Zeilen nach oben.
                placement.Y = baseline + (index * lineHeight);
                placement.Alignment = 0;
                placement.Angle = 0f;
                index++;
            }
        }

        /// <summary>
        /// RAL/Elox fest außen unten rechts – gleiche Schriftgröße wie Seitenzahl/Auftrag.
        /// </summary>
        private static void PlaceFixedBottomRightSurfaceStack(
            IList<WatermarkTextPlacement> surfacePlacements,
            string pdfPath,
            int pageNumber,
            float pageWidth,
            float pageHeight)
        {
            if (surfacePlacements == null || surfacePlacements.Count == 0)
                return;

            var fontSize = WatermarkPageScale.GetPageNumberFontSize(pageWidth, pageHeight);
            var edgeFallback = WatermarkPageScale.GetTopLeftMargin(pageWidth, pageHeight);
            float frameRight = pageWidth - edgeFallback;
            if (!string.IsNullOrWhiteSpace(pdfPath)
                && PdfRasterLayoutService.TryGetDrawingFrameBounds(
                    pdfPath,
                    pageNumber,
                    pageWidth,
                    pageHeight,
                    out _,
                    out var detectedRight,
                    out _))
            {
                frameRight = detectedRight;
            }

            var baseline = WatermarkPageScale.GetSharedBottomBandBaselineY(
                pageWidth,
                pageHeight,
                fontSize,
                pdfPath,
                pageNumber);
            var lineHeight = fontSize + 6f;
            var index = 0;
            foreach (var placement in surfacePlacements)
            {
                if (placement == null || string.IsNullOrWhiteSpace(placement.Text))
                    continue;

                placement.FontSize = fontSize;
                placement.X = frameRight;
                // Eng am Blattrand; weitere Zeilen nach oben Richtung Rahmen.
                placement.Y = baseline + (index * lineHeight);
                placement.Alignment = 2;
                placement.Angle = 0f;
                index++;
            }
        }

        public static void EnforceFixedTopBandPlacements(
            List<WatermarkTextPlacement> placements,
            float pageWidth,
            float pageHeight,
            string pdfPath = null,
            int pageNumber = 1,
            bool preserveTermPositions = false)
        {
            ReflowCenteredTopBandRow(
                placements,
                pageWidth,
                pageHeight,
                pdfPath,
                pageNumber,
                preserveTermPositions);
        }

        private static void ReflowCenteredTopBandRow(
            List<WatermarkTextPlacement> placements,
            float pageWidth,
            float pageHeight,
            string pdfPath = null,
            int pageNumber = 1,
            bool preserveTermPositions = false)
        {
            if (placements == null || pageWidth <= 0f || pageHeight <= 0f)
                return;

            // Feste Top-Band-Regel:
            // Links: Lieferort | Mitte: Auftragsnummer | Rechts: LT/FT (ggf. mit Menge).
            // Editor-Speichern: LT-Position nicht überschreiben (preserveTermPositions).
            var quantities = placements
                .Select((p, index) => new { Placement = p, Index = index })
                .Where(x => x.Placement != null
                            && string.Equals(x.Placement.Category, "Quantity", StringComparison.OrdinalIgnoreCase))
                .OrderBy(x => x.Index)
                .Select(x => x.Placement)
                .ToList();
            var dates = placements
                .Where(p => p != null && string.Equals(p.Category, "Date", StringComparison.OrdinalIgnoreCase))
                .ToList();
            var location = placements.FirstOrDefault(p =>
                p != null && string.Equals(p.Category, "Location", StringComparison.OrdinalIgnoreCase));
            var order = placements.FirstOrDefault(p =>
                p != null && string.Equals(p.Category, "Order", StringComparison.OrdinalIgnoreCase));

            var termStack = new List<WatermarkTextPlacement>();
            termStack.AddRange(quantities);

            foreach (var date in dates)
            {
                if (date == null || string.IsNullOrWhiteSpace(date.Text))
                    continue;

                var dateText = date.Text.Trim();
                if (quantities.Any(q =>
                        !string.IsNullOrWhiteSpace(q?.Text)
                        && q.Text.IndexOf(dateText, StringComparison.OrdinalIgnoreCase) >= 0))
                {
                    continue;
                }

                termStack.Add(date);
            }

            var pageNumberFont = WatermarkPageScale.GetPageNumberFontSize(pageWidth, pageHeight);
            var termFontSize = pageNumberFont;
            var locationFont = pageNumberFont;
            var orderFont = pageNumberFont;
            var bandFont = pageNumberFont;

            var hasOrder = order != null && !string.IsNullOrWhiteSpace(order.Text);
            // Location/Auftrag bleiben in der ersten Außenzeile.
            // LT/FT stapeln nach unten – sonst wandern Lieferort/Auftrag bei mehreren Terminen aus der Seite.
            var bandY = WatermarkPageScale.GetSharedTopBandBaselineY(
                pageWidth,
                pageHeight,
                bandFont,
                pdfPath,
                pageNumber,
                stackLines: 1);

            var edgeFallback = WatermarkPageScale.GetTopLeftMargin(pageWidth, pageHeight);
            float frameLeft = edgeFallback;
            float frameRight = pageWidth - edgeFallback;
            float frameCenter = pageWidth / 2f;
            float detectedLeft = 0f;
            float detectedRight = 0f;
            float detectedTop = 0f;
            var hasFrame = !string.IsNullOrWhiteSpace(pdfPath)
                && PdfRasterLayoutService.TryGetDrawingFrameBounds(
                    pdfPath,
                    pageNumber,
                    pageWidth,
                    pageHeight,
                    out detectedLeft,
                    out detectedRight,
                    out detectedTop);
            if (hasFrame)
            {
                frameLeft = detectedLeft;
                frameRight = detectedRight;
                frameCenter = (detectedLeft + detectedRight) / 2f;
            }

            if (location != null && !string.IsNullOrWhiteSpace(location.Text))
            {
                location.FontSize = locationFont > 0f ? locationFont : bandFont;
                location.X = frameLeft;
                location.Y = bandY;
                location.Alignment = 0;
                location.Angle = 0f;
            }

            if (hasOrder)
            {
                order.FontSize = orderFont > 0f ? orderFont : bandFont;
                order.X = frameCenter;
                order.Y = bandY;
                order.Alignment = 1;
                order.Angle = 0f;
            }

            if (!preserveTermPositions)
                ReflowTermStackRight(termStack, termFontSize, pageWidth, pageHeight, bandY, frameRight);
            else
            {
                foreach (var term in termStack)
                {
                    if (term != null)
                        term.FontSize = termFontSize;
                }
            }
        }

        /// <summary>
        /// Baseline für LT rechts innen: Glyphs klar unter der oberen Rahmenlinie.
        /// </summary>
        private static float GetInsideTopRightTermBaselineY(
            float fontSize,
            float pageWidth,
            float pageHeight,
            float? frameTopY)
        {
            var scale = WatermarkPageScale.Get(pageWidth, pageHeight);
            var font = fontSize > 0f ? fontSize : 22f;
            var ascent = Math.Max(font * 0.72f, 10f * scale);
            var gapInside = Math.Max(5f, 6f * scale);

            if (frameTopY.HasValue && frameTopY.Value > 0f)
                return frameTopY.Value - gapInside - ascent;

            // Fallback ohne Rahmen: etwas unter der äußeren Top-Band-Baseline.
            var outer = WatermarkPageScale.GetMaxTopBaselineY(pageWidth, pageHeight, font);
            return outer - (font + 10f);
        }

        private static void ReflowTermStackLeft(
            IList<WatermarkTextPlacement> terms,
            float fontSize,
            float pageWidth,
            float pageHeight,
            float? sharedBaselineY = null,
            float? frameLeftX = null)
        {
            if (terms == null || terms.Count == 0)
                return;

            var lineHeight = fontSize + 8f;
            var leftX = frameLeftX
                        ?? WatermarkPageScale.GetTopLeftMargin(pageWidth, pageHeight);
            var topY = sharedBaselineY
                       ?? WatermarkPageScale.GetMaxTopBaselineY(pageWidth, pageHeight, fontSize);

            for (var i = 0; i < terms.Count; i++)
            {
                var placement = terms[i];
                if (placement == null)
                    continue;

                placement.FontSize = fontSize;
                placement.X = leftX;
                placement.Y = topY - (i * lineHeight);
                placement.Alignment = 0;
                placement.Angle = 0f;
                placement.UseRedText = true;
            }
        }

        private static void ReflowTermStackRight(
            IList<WatermarkTextPlacement> terms,
            float fontSize,
            float pageWidth,
            float pageHeight,
            float? sharedBaselineY = null,
            float? frameRightX = null)
        {
            if (terms == null || terms.Count == 0)
                return;

            var lineHeight = fontSize + 8f;
            var rightX = frameRightX
                         ?? (pageWidth - WatermarkPageScale.GetTopLeftMargin(pageWidth, pageHeight));
            var topY = sharedBaselineY
                       ?? WatermarkPageScale.GetMaxTopBaselineY(pageWidth, pageHeight, fontSize);

            for (var i = 0; i < terms.Count; i++)
            {
                var placement = terms[i];
                if (placement == null)
                    continue;

                placement.FontSize = fontSize;
                placement.X = rightX;
                placement.Y = topY - (i * lineHeight);
                placement.Alignment = 2;
                placement.Angle = 0f;
                placement.UseRedText = true;
            }
        }

        private static void ApplyCenteredTopBandRow(
            IList<WatermarkTextPlacement> row,
            float fontSize,
            float pageWidth,
            float pageHeight)
        {
            if (row == null || row.Count == 0)
                return;

            var pageScale = WatermarkPageScale.Get(pageWidth, pageHeight);
            var minFont = Math.Max(14f, 16f * pageScale);
            var gap = Math.Max(10f, fontSize * 0.5f);

            float RowWidth(float size)
            {
                return row.Sum(p => EstimateTextWidth(p.Text, size)) + (gap * Math.Max(0, row.Count - 1));
            }

            while (row.Count > 1 && RowWidth(fontSize) > pageWidth * 0.94f && fontSize > minFont + 0.5f)
                fontSize -= 0.5f;

            var baselineY = WatermarkPageScale.GetMaxTopBaselineY(pageWidth, pageHeight, fontSize);
            var x = (pageWidth - RowWidth(fontSize)) / 2f;

            foreach (var placement in row)
            {
                if (placement == null)
                    continue;

                placement.FontSize = fontSize;
                placement.X = x;
                placement.Y = baselineY;
                placement.Alignment = 0;
                placement.Angle = 0f;
                x += EstimateTextWidth(placement.Text, fontSize) + gap;
            }
        }

        private static void TryAddMaterialPlacements(
            List<WatermarkTextPlacement> placements,
            string pdfPath,
            int pageNumber,
            float pageWidth,
            float pageHeight,
            List<PdfTextRect> occupied,
            WatermarkSettings settings,
            string drawingNumberHint)
        {
            try
            {
                var materialInfo = PdfMaterialExtractionService.Extract(pdfPath, pageNumber);
                var materialLines = BuildMaterialLines(materialInfo, settings, drawingNumberHint);
                if (materialLines.Count == 0)
                    return;

                var placementOccupied = WatermarkProcessingContext.BatchMode
                    ? new List<PdfTextRect>()
                    : occupied;

                List<WatermarkTextPlacement> materialPlacements;
                if (IsFixedBottomLeftMaterialPlacement(settings))
                {
                    materialPlacements = materialLines
                        .Select(line => new WatermarkTextPlacement
                        {
                            Text = line,
                            Category = "Material",
                            FontSize = WatermarkPageScale.GetPageNumberFontSize(pageWidth, pageHeight)
                        })
                        .ToList();
                    PlaceFixedBottomLeftMaterialStack(
                        materialPlacements,
                        pdfPath,
                        pageNumber,
                        pageWidth,
                        pageHeight);
                }
                else
                {
                    materialPlacements = PlaceMaterialCenterStack(
                        materialLines,
                        pdfPath,
                        pageNumber,
                        pageWidth,
                        pageHeight,
                        placementOccupied);
                }

                foreach (var placement in materialPlacements)
                {
                    placement.Category = "Material";
                    placements.Add(placement);
                    occupied.Add(MakeTextRect(placement, pageWidth));
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.WriteLine("Material placement failed: " + ex.Message);
            }
        }

        private static void TryAddSurfaceTreatmentPlacements(
            List<WatermarkTextPlacement> placements,
            string pdfPath,
            int pageNumber,
            float pageWidth,
            float pageHeight,
            List<PdfTextRect> occupied,
            WatermarkSettings settings)
        {
            try
            {
                var surfaceLines = BuildSurfaceTreatmentLines(
                    PdfSurfaceTreatmentExtractionService.Extract(pdfPath, pageNumber),
                    settings);

                var added = new List<WatermarkTextPlacement>();
                foreach (var surfaceLine in surfaceLines)
                {
                    var placement = new WatermarkTextPlacement
                    {
                        Text = surfaceLine.Text,
                        Category = surfaceLine.Category,
                        FontSize = WatermarkPageScale.GetPageNumberFontSize(pageWidth, pageHeight)
                    };
                    placements.Add(placement);
                    added.Add(placement);
                }

                PlaceFixedBottomRightSurfaceStack(
                    added,
                    pdfPath,
                    pageNumber,
                    pageWidth,
                    pageHeight);
                foreach (var placement in added)
                    occupied.Add(MakeTextRect(placement, pageWidth));
            }
            catch
            {
            }
        }

        private sealed class SurfaceTreatmentWatermarkLine
        {
            public string Text { get; set; }
            public string Category { get; set; }
        }

        private static List<SurfaceTreatmentWatermarkLine> BuildSurfaceTreatmentLines(
            PdfSurfaceTreatmentInfo info,
            WatermarkSettings settings)
        {
            var lines = new List<SurfaceTreatmentWatermarkLine>();
            if (info == null || !info.HasSurfaceTreatmentEntry)
                return lines;

            if (settings != null && settings.ExtractPowderCoating && info.HasPowderCoating)
            {
                var text = settings.PowderCoatingShowRalNumber && !string.IsNullOrWhiteSpace(info.RalCode)
                    ? "RAL " + info.RalCode
                    : "RAL";
                lines.Add(new SurfaceTreatmentWatermarkLine { Text = text, Category = "PowderCoating" });
            }

            if (settings != null && settings.ExtractAnodizing && info.HasAnodizing)
            {
                lines.Add(new SurfaceTreatmentWatermarkLine { Text = "ELOX", Category = "Anodizing" });
            }

            return lines;
        }

        public static List<WatermarkTextPlacement> ResolvePage1Placements(
            string pdfPath,
            int pageNumber,
            WatermarkFieldData fields,
            string drawingNumberHint,
            float pageWidth,
            float pageHeight,
            WatermarkSettings settings = null,
            string layoutPdfPath = null)
        {
            layoutPdfPath = string.IsNullOrWhiteSpace(layoutPdfPath) ? pdfPath : layoutPdfPath;

            if (WatermarkProcessingContext.BatchMode)
                return Compute(layoutPdfPath, pageNumber, fields, drawingNumberHint, pageWidth, pageHeight, settings);

            if (WatermarkMetadataStore.TryLoadPlacements(pdfPath, out _, out _, out var savedPlacements)
                && savedPlacements != null
                && savedPlacements.Count > 0)
            {
                var layout = PdfLayoutAnalysisService.AnalyzePage(layoutPdfPath, pageNumber);
                if (layout.PageWidth > 0)
                    pageWidth = layout.PageWidth;
                if (layout.PageHeight > 0)
                    pageHeight = layout.PageHeight;

                var placements = ClonePlacements(savedPlacements);
                if (!WatermarkLearningStore.HasLearnedPlacementForCategory(drawingNumberHint, "Material"))
                {
                    placements.RemoveAll(p =>
                        p != null && string.Equals(p.Category, "Material", StringComparison.OrdinalIgnoreCase));
                }

                if (!WatermarkLearningStore.HasLearnedPlacementForCategory(drawingNumberHint, "PowderCoating"))
                {
                    placements.RemoveAll(p =>
                        p != null && string.Equals(p.Category, "PowderCoating", StringComparison.OrdinalIgnoreCase));
                }

                if (!WatermarkLearningStore.HasLearnedPlacementForCategory(drawingNumberHint, "Anodizing"))
                {
                    placements.RemoveAll(p =>
                        p != null && string.Equals(p.Category, "Anodizing", StringComparison.OrdinalIgnoreCase));
                }
                SyncPlacementTextsFromFields(placements, fields, settings);
                WatermarkLearningStore.ApplyToPlacements(drawingNumberHint, placements);
                ReflowQuantityStack(placements, pageWidth, pageHeight, layoutPdfPath, pageNumber);
                ReflowBgStack(placements, pageWidth, pageHeight, layoutPdfPath, pageNumber, settings);
                EnsureTopBandSeparation(placements, pageWidth, pageHeight, layoutPdfPath, pageNumber);

                var occupied = PdfLayoutAnalysisService.AnalyzePage(layoutPdfPath, pageNumber).TextRects
                    .Select(r => Inflate(r, 6f))
                    .ToList();
                foreach (var p in placements)
                    occupied.Add(MakeTextRect(p, pageWidth));

                MergeFreshSurfaceTreatmentPlacements(
                    placements,
                    layoutPdfPath,
                    pageNumber,
                    pageWidth,
                    pageHeight,
                    drawingNumberHint,
                    settings,
                    occupied);

                MergeFreshMaterialPlacements(
                    placements,
                    layoutPdfPath,
                    pageNumber,
                    pageWidth,
                    pageHeight,
                    drawingNumberHint,
                    settings,
                    occupied);
                WatermarkLearningStore.ApplyLearnedDrawingCorrections(
                    drawingNumberHint,
                    placements,
                    layoutPdfPath,
                    pageNumber,
                    pageWidth,
                    pageHeight);
                EnforceFixedTopBandPlacements(placements, pageWidth, pageHeight, layoutPdfPath, pageNumber);
                EnforceSurfaceTreatmentPlacements(placements, layoutPdfPath, pageNumber, pageWidth, pageHeight);
                EnforceFixedBottomLeftMaterialPlacements(
                    placements, layoutPdfPath, pageNumber, pageWidth, pageHeight, settings);
                EnforceFixedRightVerticalBgPlacements(
                    placements, layoutPdfPath, pageNumber, pageWidth, pageHeight, settings);
                return placements;
            }

            return Compute(layoutPdfPath, pageNumber, fields, drawingNumberHint, pageWidth, pageHeight, settings);
        }

        private static List<string> BuildMaterialLines(
            PdfMaterialInfo materialInfo,
            WatermarkSettings settings,
            string drawingNumberHint)
        {
            var lines = new List<string>();
            if (materialInfo == null)
                return lines;

            var showType = settings == null || settings.ExtractMaterialType;
            var showThickness = settings == null || settings.ExtractMaterialThickness;
            if (WatermarkLearningStore.ShouldSuppressAutoMaterial(drawingNumberHint))
                return lines;

            if (materialInfo.IsAssembly || materialInfo.IsTube)
                return lines;

            if (showType && showThickness && !string.IsNullOrWhiteSpace(materialInfo.MaterialWatermarkText))
            {
                lines.Add(materialInfo.MaterialWatermarkText.Trim());
            }
            else if (showThickness && !string.IsNullOrWhiteSpace(materialInfo.ThicknessText))
            {
                var prefix = string.IsNullOrWhiteSpace(materialInfo.MaterialAlloyPrefix)
                    ? "ST"
                    : materialInfo.MaterialAlloyPrefix.Trim();
                var wm = MaterialAlloyClassifier.BuildWatermarkText(prefix, materialInfo.ThicknessText);
                if (!string.IsNullOrWhiteSpace(wm))
                    lines.Add(wm);
            }
            else if (showThickness && !string.IsNullOrWhiteSpace(materialInfo.MaterialWatermarkText))
            {
                lines.Add(materialInfo.MaterialWatermarkText.Trim());
            }
            else if (showType && !string.IsNullOrWhiteSpace(materialInfo.MaterialType))
            {
                var wm = MaterialAlloyClassifier.BuildWatermarkText(
                    materialInfo.MaterialType.Trim(),
                    materialInfo.ThicknessText);
                if (!string.IsNullOrWhiteSpace(wm))
                    lines.Add(wm.Trim());
            }
            else if (showType && !string.IsNullOrWhiteSpace(materialInfo.MaterialAlloyPrefix))
            {
                lines.Add(materialInfo.MaterialAlloyPrefix.Trim());
            }

            return lines;
        }

        private static void MergeFreshSurfaceTreatmentPlacements(
            List<WatermarkTextPlacement> placements,
            string pdfPath,
            int pageNumber,
            float pageWidth,
            float pageHeight,
            string drawingNumberHint,
            WatermarkSettings settings,
            List<PdfTextRect> occupied)
        {
            if (placements == null)
                return;

            var powderLearned = WatermarkLearningStore.HasLearnedPlacementForCategory(drawingNumberHint, "PowderCoating");
            var anodizingLearned = WatermarkLearningStore.HasLearnedPlacementForCategory(drawingNumberHint, "Anodizing");
            if (powderLearned && anodizingLearned)
                return;

            placements.RemoveAll(p =>
                p != null
                && ((string.Equals(p.Category, "PowderCoating", StringComparison.OrdinalIgnoreCase) && !powderLearned)
                    || (string.Equals(p.Category, "Anodizing", StringComparison.OrdinalIgnoreCase) && !anodizingLearned)));

            var surfaceLines = BuildSurfaceTreatmentLines(
                PdfSurfaceTreatmentExtractionService.Extract(pdfPath, pageNumber),
                settings);

            foreach (var surfaceLine in surfaceLines)
            {
                if (string.Equals(surfaceLine.Category, "PowderCoating", StringComparison.OrdinalIgnoreCase) && powderLearned)
                    continue;
                if (string.Equals(surfaceLine.Category, "Anodizing", StringComparison.OrdinalIgnoreCase) && anodizingLearned)
                    continue;

                var placement = new WatermarkTextPlacement
                {
                    Text = surfaceLine.Text,
                    Category = surfaceLine.Category,
                    FontSize = WatermarkPageScale.GetPageNumberFontSize(pageWidth, pageHeight)
                };
                placements.Add(placement);
                occupied?.Add(MakeTextRect(placement, pageWidth));
            }

            var freshSurface = placements
                .Where(p => p != null
                            && (string.Equals(p.Category, "PowderCoating", StringComparison.OrdinalIgnoreCase)
                                || string.Equals(p.Category, "Anodizing", StringComparison.OrdinalIgnoreCase)))
                .OrderBy(p => string.Equals(p.Category, "Anodizing", StringComparison.OrdinalIgnoreCase) ? 1 : 0)
                .ToList();
            PlaceFixedBottomRightSurfaceStack(
                freshSurface,
                pdfPath,
                pageNumber,
                pageWidth,
                pageHeight);
        }

        private static void MergeFreshMaterialPlacements(
            List<WatermarkTextPlacement> placements,
            string pdfPath,
            int pageNumber,
            float pageWidth,
            float pageHeight,
            string drawingNumberHint,
            WatermarkSettings settings,
            List<PdfTextRect> occupied)
        {
            if (placements == null)
                return;

            if (WatermarkLearningStore.HasLearnedPlacementForCategory(drawingNumberHint, "Material"))
                return;

            var materialLines = BuildMaterialLines(
                PdfMaterialExtractionService.Extract(pdfPath, pageNumber),
                settings,
                drawingNumberHint);

            placements.RemoveAll(p =>
                p != null && string.Equals(p.Category, "Material", StringComparison.OrdinalIgnoreCase));

            if (materialLines.Count == 0)
                return;

            if (PdfRasterLayoutService.IsAvailable && !WatermarkProcessingContext.BatchMode)
                PdfRasterLayoutService.EnsurePageGrid(pdfPath, pageNumber, pageWidth, pageHeight);

            var placementOccupied = WatermarkProcessingContext.BatchMode
                ? new List<PdfTextRect>()
                : BuildOccupiedWithoutDynamicCategories(occupied, placements, pageWidth, pageHeight);

            List<WatermarkTextPlacement> materialPlacements;
            if (IsFixedBottomLeftMaterialPlacement(settings))
            {
                materialPlacements = materialLines
                    .Select(line => new WatermarkTextPlacement
                    {
                        Text = line,
                        Category = "Material",
                        FontSize = WatermarkPageScale.GetPageNumberFontSize(pageWidth, pageHeight)
                    })
                    .ToList();
                PlaceFixedBottomLeftMaterialStack(
                    materialPlacements,
                    pdfPath,
                    pageNumber,
                    pageWidth,
                    pageHeight);
            }
            else
            {
                materialPlacements = MaterialWatermarkPlacementService.PlaceDynamic(
                    materialLines,
                    pdfPath,
                    pageNumber,
                    pageWidth,
                    pageHeight,
                    placementOccupied);
            }

            foreach (var placement in materialPlacements)
            {
                placement.Category = "Material";
                placements.Add(placement);
            }
        }

        private static List<PdfTextRect> BuildOccupiedWithoutDynamicCategories(
            List<PdfTextRect> occupied,
            List<WatermarkTextPlacement> placements,
            float pageWidth,
            float pageHeight)
        {
            var result = occupied?
                .Where(r => r != null)
                .Select(r => new PdfTextRect
                {
                    Text = r.Text,
                    Left = r.Left,
                    Right = r.Right,
                    Bottom = r.Bottom,
                    Top = r.Top
                })
                .ToList() ?? new List<PdfTextRect>();

            if (placements == null)
                return result;

            foreach (var placement in placements)
            {
                if (placement == null || string.IsNullOrWhiteSpace(placement.Category))
                    continue;

                if (string.Equals(placement.Category, "Material", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(placement.Category, "PowderCoating", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(placement.Category, "Anodizing", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                result.Add(MakeTextRect(placement, pageWidth));
            }

            return result;
        }

        private static List<WatermarkTextPlacement> ClonePlacements(IEnumerable<WatermarkTextPlacement> source)
        {
            return (source ?? Enumerable.Empty<WatermarkTextPlacement>())
                .Where(p => p != null)
                .Select(p => new WatermarkTextPlacement
                {
                    Text = p.Text,
                    X = p.X,
                    Y = p.Y,
                    Alignment = p.Alignment,
                    Angle = p.Angle,
                    FontSize = p.FontSize > 0 ? p.FontSize : 22f,
                    UseRedText = p.UseRedText,
                    Category = p.Category
                })
                .ToList();
        }

        private static void SyncPlacementTextsFromFields(
            List<WatermarkTextPlacement> placements,
            WatermarkFieldData fields,
            WatermarkSettings settings)
        {
            if (placements == null || fields == null)
                return;

            if (!string.IsNullOrWhiteSpace(fields.OrderNumberText))
            {
                var order = placements.FirstOrDefault(p =>
                    string.Equals(p.Category, "Order", StringComparison.OrdinalIgnoreCase));
                if (order == null)
                {
                    order = new WatermarkTextPlacement
                    {
                        Category = "Order",
                        FontSize = 22f
                    };
                    placements.Add(order);
                }

                order.Text = fields.OrderNumberText.Trim();
            }

            if (IsSubgroupPartDrawing(WatermarkProcessingContext.CurrentDrawingKey, fields.BgNumberText))
            {
                placements.RemoveAll(p =>
                    p != null && string.Equals(p.Category, "Location", StringComparison.OrdinalIgnoreCase));
            }
            else if (!string.IsNullOrWhiteSpace(fields.DeliveryLocationText))
            {
                var location = placements.FirstOrDefault(p =>
                    string.Equals(p.Category, "Location", StringComparison.OrdinalIgnoreCase));
                if (location == null)
                {
                    location = new WatermarkTextPlacement
                    {
                        Category = "Location",
                        FontSize = 22f
                    };
                    placements.Add(location);
                }

                location.Text = fields.DeliveryLocationText.Trim();
            }

            if (!string.IsNullOrWhiteSpace(fields.DeliveryDateText))
            {
                var date = placements.FirstOrDefault(p =>
                    string.Equals(p.Category, "Date", StringComparison.OrdinalIgnoreCase));
                if (date != null)
                    date.Text = fields.DeliveryDateText.Trim();
            }

            if (!string.IsNullOrWhiteSpace(fields.BgNumberText) || (fields.BgLines != null && fields.BgLines.Count > 0))
            {
                if (!IsBgWatermarkEnabled(settings))
                {
                    placements.RemoveAll(p =>
                        p != null && string.Equals(p.Category, "Bg", StringComparison.OrdinalIgnoreCase));
                }
                else
                {
                    var bgLines = (fields.BgLines != null && fields.BgLines.Count > 0)
                        ? fields.BgLines
                            .Where(l => !string.IsNullOrWhiteSpace(l))
                            .Select(l => l.Trim())
                            .Distinct(StringComparer.OrdinalIgnoreCase)
                            .ToList()
                        : SplitBgLines(fields.BgNumberText);

                    placements.RemoveAll(p =>
                        p != null && string.Equals(p.Category, "Bg", StringComparison.OrdinalIgnoreCase));

                    foreach (var line in bgLines)
                    {
                        placements.Add(new WatermarkTextPlacement
                        {
                            Category = "Bg",
                            Text = line,
                            FontSize = 22f,
                            Alignment = 0
                        });
                    }
                }
            }

            var quantityPlacements = placements
                .Where(p => string.Equals(p.Category, "Quantity", StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (fields.QuantityLines != null && fields.QuantityLines.Count > 0)
            {
                for (var i = 0; i < quantityPlacements.Count && i < fields.QuantityLines.Count; i++)
                    quantityPlacements[i].Text = fields.QuantityLines[i].Trim();
            }
            else if (!string.IsNullOrWhiteSpace(fields.QuantityText) && quantityPlacements.Count > 0)
            {
                quantityPlacements[0].Text = fields.QuantityText.Trim();
            }

            if (WatermarkLearningStore.ShouldSuppressAutoMaterial(WatermarkProcessingContext.CurrentDrawingKey))
            {
                placements.RemoveAll(p =>
                    p != null && string.Equals(p.Category, "Material", StringComparison.OrdinalIgnoreCase));
            }
        }

        public static List<WatermarkTextPlacement> ApplyLearning(
            string drawingNumberHint,
            List<WatermarkTextPlacement> placements,
            float pageWidth,
            float pageHeight,
            string pdfPath,
            int pageNumber,
            List<PdfTextRect> occupied,
            WatermarkSettings settings = null)
        {
            if (WatermarkProcessingContext.BatchMode)
            {
                ReflowQuantityStack(placements, pageWidth, pageHeight, pdfPath, pageNumber);
                ReflowBgStack(placements, pageWidth, pageHeight, pdfPath, pageNumber, settings);
                EnforceFixedTopRightOrderPlacements(placements, pageWidth, pageHeight, pdfPath, pageNumber);
                EnsureTopBandSeparation(placements, pageWidth, pageHeight, pdfPath, pageNumber);

                if (!WatermarkLearningStore.HasLearnedPlacementForCategory(drawingNumberHint, "PowderCoating")
                    && !WatermarkLearningStore.HasLearnedPlacementForCategory(drawingNumberHint, "Anodizing")
                    && !placements.Any(p =>
                        p != null
                        && (string.Equals(p.Category, "PowderCoating", StringComparison.OrdinalIgnoreCase)
                            || string.Equals(p.Category, "Anodizing", StringComparison.OrdinalIgnoreCase))))
                {
                    MergeFreshSurfaceTreatmentPlacements(
                        placements, pdfPath, pageNumber, pageWidth, pageHeight, drawingNumberHint, settings, occupied);
                }

                if (!WatermarkLearningStore.HasLearnedPlacementForCategory(drawingNumberHint, "Material")
                    && !placements.Any(p =>
                        p != null && string.Equals(p.Category, "Material", StringComparison.OrdinalIgnoreCase)))
                {
                    MergeFreshMaterialPlacements(
                        placements, pdfPath, pageNumber, pageWidth, pageHeight, drawingNumberHint, settings, occupied);
                }

                // Session-/Kategorie-Defaults zuerst, dann zeichnungsspezifische manuelle Korrekturen.
                WatermarkLearningStore.ApplyCategoryDefaults(
                    placements,
                    pageWidth,
                    pageHeight,
                    drawingNumberHint,
                    applyLayoutCategories: true);
                WatermarkLearningStore.ApplyLearnedDrawingCorrections(
                    drawingNumberHint,
                    placements,
                    pdfPath,
                    pageNumber,
                    pageWidth,
                    pageHeight);
                UnifyMaterialAndSurfaceFontSizes(placements, pageWidth, pageHeight);
                EnforceFixedTopBandPlacements(placements, pageWidth, pageHeight, pdfPath, pageNumber);
                ReflowBgStack(placements, pageWidth, pageHeight, pdfPath, pageNumber, settings);
                EnforceSurfaceTreatmentPlacements(placements, pdfPath, pageNumber, pageWidth, pageHeight);
                EnforceFixedBottomLeftMaterialPlacements(placements, pdfPath, pageNumber, pageWidth, pageHeight, settings);
                return placements;
            }

            var drawingSpecificCategories = WatermarkLearningStore.ApplyToPlacements(drawingNumberHint, placements);
            WatermarkLearningStore.ApplyCategoryDefaults(placements, pageWidth, pageHeight, drawingNumberHint);
            ReflowQuantityStack(placements, pageWidth, pageHeight, pdfPath, pageNumber);
            ReflowBgStack(placements, pageWidth, pageHeight, pdfPath, pageNumber, settings);
            EnforceFixedTopRightOrderPlacements(placements, pageWidth, pageHeight, pdfPath, pageNumber);
            EnsureTopBandSeparation(placements, pageWidth, pageHeight, pdfPath, pageNumber);

            RefinePlacementsToFreeAreas(placements, pdfPath, pageNumber, pageWidth, pageHeight, occupied, drawingSpecificCategories);
            ReflowQuantityStack(placements, pageWidth, pageHeight, pdfPath, pageNumber);
            EnsureTopBandSeparation(placements, pageWidth, pageHeight, pdfPath, pageNumber);
            AiWatermarkPlacementService.EnforceVisionLayout(placements, pdfPath, pageNumber, pageWidth, pageHeight, occupied);
            if (!WatermarkLearningStore.HasLearnedPlacementForCategory(drawingNumberHint, "Material"))
            {
                MergeFreshMaterialPlacements(
                    placements, pdfPath, pageNumber, pageWidth, pageHeight, drawingNumberHint, settings, occupied);
            }

            AutoLearnSuccessfulPlacements(pdfPath, pageNumber, placements, pageWidth, pageHeight, drawingSpecificCategories);
            WatermarkLearningStore.ApplyLearnedDrawingCorrections(
                drawingNumberHint,
                placements,
                pdfPath,
                pageNumber,
                pageWidth,
                pageHeight);
            UnifyMaterialAndSurfaceFontSizes(placements, pageWidth, pageHeight);
            EnforceFixedTopBandPlacements(placements, pageWidth, pageHeight, pdfPath, pageNumber);
            ReflowBgStack(placements, pageWidth, pageHeight, pdfPath, pageNumber, settings);
            EnforceSurfaceTreatmentPlacements(placements, pdfPath, pageNumber, pageWidth, pageHeight);
            EnforceFixedBottomLeftMaterialPlacements(placements, pdfPath, pageNumber, pageWidth, pageHeight, settings);
            return placements;
        }

        private static void EnforceFixedTopRightOrderPlacements(
            List<WatermarkTextPlacement> placements,
            float pageWidth,
            float pageHeight,
            string pdfPath = null,
            int pageNumber = 1)
        {
            ReflowCenteredTopBandRow(placements, pageWidth, pageHeight, pdfPath, pageNumber);
        }

        private static void ApplyFixedTopRightOrderPlacement(
            WatermarkTextPlacement placement,
            float pageWidth,
            float pageHeight,
            float? sharedBaselineY = null,
            float? frameRightX = null)
        {
            var fontSize = placement.FontSize > 0f
                ? placement.FontSize
                : WatermarkPageScale.GetPageNumberFontSize(pageWidth, pageHeight);

            placement.FontSize = fontSize;
            placement.X = frameRightX
                          ?? (pageWidth - WatermarkPageScale.GetSideMargin(pageWidth, pageHeight));
            placement.Y = sharedBaselineY
                          ?? WatermarkPageScale.GetTopBaselineY(pageWidth, pageHeight, fontSize);
            placement.Alignment = 2;
            placement.Angle = 0f;
        }

        private static void ApplyFixedTopLeftOrderPlacement(
            WatermarkTextPlacement placement,
            float pageWidth,
            float pageHeight,
            float? sharedBaselineY = null,
            float? frameLeftX = null)
        {
            var fontSize = placement.FontSize > 0f
                ? placement.FontSize
                : WatermarkPageScale.GetPageNumberFontSize(pageWidth, pageHeight);

            placement.FontSize = fontSize;
            placement.X = frameLeftX
                          ?? WatermarkPageScale.GetTopLeftMargin(pageWidth, pageHeight);
            placement.Y = sharedBaselineY
                          ?? WatermarkPageScale.GetTopBaselineY(pageWidth, pageHeight, fontSize);
            placement.Alignment = 0;
            placement.Angle = 0f;
        }

        private static int CountQuantityPlacements(List<WatermarkTextPlacement> placements)
        {
            return placements == null
                ? 0
                : placements.Count(p => p != null && string.Equals(p.Category, "Quantity", StringComparison.OrdinalIgnoreCase));
        }

        private static bool IsFixedTopLeftPlacement(WatermarkTextPlacement placement, List<WatermarkTextPlacement> placements)
        {
            return placement != null
                && string.Equals(placement.Category, "Location", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsFixedTopRightPlacement(WatermarkTextPlacement placement)
        {
            if (placement == null || string.IsNullOrWhiteSpace(placement.Category))
                return false;

            return string.Equals(placement.Category, "Date", StringComparison.OrdinalIgnoreCase)
                   || string.Equals(placement.Category, "Quantity", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsFixedTopCenterPlacement(WatermarkTextPlacement placement)
        {
            return placement != null
                && string.Equals(placement.Category, "Order", StringComparison.OrdinalIgnoreCase);
        }

        private static void ReflowQuantityStack(
            List<WatermarkTextPlacement> placements,
            float pageWidth,
            float pageHeight,
            string pdfPath = null,
            int pageNumber = 1)
        {
            ReflowCenteredTopBandRow(placements, pageWidth, pageHeight, pdfPath, pageNumber);
        }

        private static AnchorSearchZone[] GetQuantityStackZones(float pageWidth, float pageHeight, float blockWidth)
        {
            var titleBlockTop = GetTitleBlockTopY(pageHeight);
            var titleBlockLeft = GetTitleBlockLeftX(pageWidth);
            const float margin = 35f;
            return new[]
            {
                new AnchorSearchZone(
                    pageWidth * 0.58f,
                    pageWidth - margin,
                    pageHeight * 0.76f,
                    pageHeight - margin,
                    pageWidth - margin - 8f,
                    pageHeight * 0.92f),
                new AnchorSearchZone(
                    margin,
                    titleBlockLeft - 48f,
                    titleBlockTop + 24f,
                    titleBlockTop + 160f,
                    margin + 8f,
                    titleBlockTop + 120f)
            };
        }

        private static bool IsRasterPlacementAcceptable(
            string pdfPath,
            int pageNumber,
            float anchorX,
            float anchorY,
            float blockWidth,
            float blockHeight,
            float pageWidth,
            float pageHeight,
            int alignment,
            bool isMaterial = false)
        {
            if (!PdfRasterLayoutService.IsAvailable)
                return true;

            return PdfRasterLayoutService.IsPlacementAcceptable(
                pdfPath,
                pageNumber,
                anchorX,
                anchorY,
                blockWidth,
                blockHeight,
                pageWidth,
                pageHeight,
                alignment,
                isMaterial,
                materialYIsBottomAnchor: false);
        }

        private static void AutoLearnSuccessfulPlacements(
            string pdfPath,
            int pageNumber,
            List<WatermarkTextPlacement> placements,
            float pageWidth,
            float pageHeight,
            HashSet<string> drawingSpecificCategories)
        {
            if (WatermarkProcessingContext.BatchMode)
                return;

            if (WatermarkLearningStore.ShouldSkipLearningAfterPlacementError)
                return;

            if (!PdfRasterLayoutService.IsAvailable || placements == null || placements.Count == 0)
                return;

            var toLearn = new List<WatermarkTextPlacement>();
            foreach (var placement in placements)
            {
                if (placement == null || string.IsNullOrWhiteSpace(placement.Category))
                    continue;

                if (string.Equals(placement.Category, "Material", StringComparison.OrdinalIgnoreCase))
                    continue;

                if (placement.Category.Equals("Order", StringComparison.OrdinalIgnoreCase))
                    continue;

                var blockWidth = EstimateTextWidth(placement.Text, placement.FontSize);
                var blockHeight = placement.FontSize + 8f;
                var isMaterial = placement.Category.Equals("Material", StringComparison.OrdinalIgnoreCase);
                if (!PdfRasterLayoutService.IsPlacementAcceptable(
                        pdfPath,
                        pageNumber,
                        placement.X,
                        placement.Y,
                        blockWidth,
                        blockHeight,
                        pageWidth,
                        pageHeight,
                        placement.Alignment,
                        isMaterial))
                {
                    continue;
                }

                toLearn.Add(placement);
            }

            if (toLearn.Count > 0)
                WatermarkLearningStore.SaveCategoryDefaultsFromPlacements(toLearn, pageWidth, pageHeight);
        }

        private static void RefinePlacementsToFreeAreas(
            List<WatermarkTextPlacement> placements,
            string pdfPath,
            int pageNumber,
            float pageWidth,
            float pageHeight,
            List<PdfTextRect> occupied,
            HashSet<string> drawingSpecificCategories)
        {
            if (!PdfRasterLayoutService.IsAvailable || placements == null)
                return;

            foreach (var placement in placements)
            {
                if (placement == null || string.IsNullOrWhiteSpace(placement.Category))
                    continue;

                if (string.Equals(placement.Category, "Material", StringComparison.OrdinalIgnoreCase))
                    continue;

                if (string.Equals(placement.Category, "PowderCoating", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(placement.Category, "Anodizing", StringComparison.OrdinalIgnoreCase))
                    continue;

                // Feste BG rechts senkrecht nicht verschieben.
                if (string.Equals(placement.Category, "Bg", StringComparison.OrdinalIgnoreCase)
                    && Math.Abs(placement.Angle + 90f) < 0.5f)
                    continue;

                if (IsFixedTopLeftPlacement(placement, placements))
                    continue;

                if (IsFixedTopCenterPlacement(placement))
                    continue;

                if (IsFixedTopRightPlacement(placement))
                    continue;

                if (drawingSpecificCategories != null
                    && drawingSpecificCategories.Contains(placement.Category))
                    continue;

                if (string.Equals(placement.Category, "Quantity", StringComparison.OrdinalIgnoreCase)
                    && CountQuantityPlacements(placements) > 1)
                    continue;

                var isMaterial = placement.Category.Equals("Material", StringComparison.OrdinalIgnoreCase);
                var materialLineCount = isMaterial
                    ? placements.Count(p =>
                        p != null && string.Equals(p.Category, "Material", StringComparison.OrdinalIgnoreCase))
                    : 1;
                var fontSize = placement.FontSize > 0f ? placement.FontSize : 22f;
                var blockWidth = isMaterial
                    ? placements
                        .Where(p => p != null && string.Equals(p.Category, "Material", StringComparison.OrdinalIgnoreCase))
                        .Select(p => EstimateTextWidth(p.Text, p.FontSize > 0f ? p.FontSize : fontSize))
                        .DefaultIfEmpty(EstimateTextWidth(placement.Text, fontSize))
                        .Max()
                    : EstimateTextWidth(placement.Text, fontSize);
                var blockHeight = isMaterial
                    ? PdfRasterLayoutService.GetMaterialBlockHeight(fontSize, materialLineCount)
                    : fontSize + 8f;

                if (isMaterial
                    && placements is List<WatermarkTextPlacement> materialList
                    && materialList.IndexOf(placement) != materialList.FindIndex(p =>
                        p != null && string.Equals(p.Category, "Material", StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                if (PdfRasterLayoutService.IsPlacementAcceptable(
                        pdfPath,
                        pageNumber,
                        placement.X,
                        placement.Y,
                        blockWidth,
                        blockHeight,
                        pageWidth,
                        pageHeight,
                        placement.Alignment,
                        isMaterial)
                    && !Overlaps(occupied, placement.X, placement.Y, blockWidth, blockHeight, placement.Alignment))
                {
                    continue;
                }

                var zones = GetRefinementZonesForCategory(placement.Category, pageWidth, pageHeight);
                foreach (var zone in zones)
                {
                    if (isMaterial
                        && PdfRasterLayoutService.TryFindFreeCenterAnchor(
                            pdfPath,
                            pageNumber,
                            blockWidth,
                            blockHeight,
                            pageWidth,
                            pageHeight,
                            zone.Left,
                            zone.Right,
                            zone.Bottom,
                            zone.Top,
                            placement.X,
                            placement.Y,
                            out var centerX,
                            out var anchorBottomY))
                    {
                        var lineHeight = PdfRasterLayoutService.GetMaterialLineHeight(fontSize);
                        placement.X = centerX;
                        placement.Y = anchorBottomY + blockHeight - lineHeight;
                        placement.Alignment = 1;
                        SyncMaterialStackFromTopLine(placements, fontSize);
                        break;
                    }

                    if (PdfRasterLayoutService.TryFindFreeAnchor(
                            pdfPath,
                            pageNumber,
                            blockWidth,
                            blockHeight,
                            pageWidth,
                            pageHeight,
                            zone.Left,
                            zone.Right,
                            zone.Bottom,
                            zone.Top,
                            placement.X,
                            placement.Y,
                            out var anchorX,
                            out var anchorY)
                        && !Overlaps(occupied, anchorX, anchorY, blockWidth, blockHeight, placement.Alignment))
                    {
                        placement.X = anchorX;
                        placement.Y = anchorY;
                        break;
                    }
                }
            }
        }

        private static AnchorSearchZone[] GetRefinementZonesForCategory(string category, float pageWidth, float pageHeight)
        {
            var titleBlockTop = GetTitleBlockTopY(pageHeight);
            var titleBlockLeft = GetTitleBlockLeftX(pageWidth);

            if (category.Equals("Quantity", StringComparison.OrdinalIgnoreCase))
            {
                return GetQuantityStackZones(pageWidth, pageHeight, 120f);
            }

            if (category.Equals("Material", StringComparison.OrdinalIgnoreCase))
            {
                return Array.Empty<AnchorSearchZone>();
            }

            if (category.Equals("Bg", StringComparison.OrdinalIgnoreCase))
            {
                return new[]
                {
                    new AnchorSearchZone(35f, Math.Max(120f, titleBlockLeft - 25f), titleBlockTop + 8f, titleBlockTop + 135f, 48f, titleBlockTop + 58f)
                };
            }

            if (category.Equals("Date", StringComparison.OrdinalIgnoreCase))
            {
                return new[]
                {
                    new AnchorSearchZone(35f, pageWidth * 0.45f, pageHeight * 0.72f, pageHeight - 40f, 48f, pageHeight * 0.88f)
                };
            }

            if (category.Equals("Location", StringComparison.OrdinalIgnoreCase))
            {
                return new[]
                {
                    new AnchorSearchZone(pageWidth * 0.20f, pageWidth * 0.80f, pageHeight * 0.72f, pageHeight - 35f, pageWidth / 2f, pageHeight * 0.90f)
                };
            }

            return new[]
            {
                new AnchorSearchZone(35f, pageWidth - 35f, titleBlockTop + 8f, titleBlockTop + 170f, pageWidth / 2f, titleBlockTop + 90f)
            };
        }

        public static WatermarkFieldData BuildFieldData(
            IEnumerable<(string quantity, string deliveryDate, string deliveryLocation, string bgDrawingNumber, string orderNumber)> entries,
            IList<string> orderNumbers,
            string drawingHint = null)
        {
            var list = entries?.ToList() ?? new List<(string, string, string, string, string)>();
            var data = new WatermarkFieldData();

            data.QuantityLines = BuildQuantityLines(list);
            data.QuantityText = data.QuantityLines.Count > 0
                ? string.Join(Environment.NewLine, data.QuantityLines)
                : string.Empty;
            data.DeliveryDateText = JoinDistinct(list.Select(e => NormalizeDate(e.deliveryDate)));
            data.BgLines = BuildBgLines(list);
            data.BgNumberText = data.BgLines.Count > 0
                ? string.Join(" | ", data.BgLines)
                : string.Empty;
            data.DeliveryLocationText = JoinDistinct(list.Select(e => NormalizeLocation(e.deliveryLocation)));
            var drawingKey = !string.IsNullOrWhiteSpace(drawingHint)
                ? drawingHint
                : WatermarkProcessingContext.CurrentDrawingKey;
            if (IsSubgroupPartDrawing(drawingKey, data.BgNumberText))
                data.DeliveryLocationText = string.Empty;

            var orderFromEntries = JoinDistinct(list.Select(e => e.orderNumber));
            var orderFromList = orderNumbers == null
                ? string.Empty
                : string.Join(" | ", orderNumbers.Where(o => !string.IsNullOrWhiteSpace(o)).Distinct());
            data.OrderNumberText = !string.IsNullOrWhiteSpace(orderFromList) ? orderFromList : orderFromEntries;

            return data;
        }

        private static WatermarkTextPlacement PlaceTopCenter(
            string text,
            float fontSize,
            float pageWidth,
            float pageHeight,
            List<PdfTextRect> occupied)
        {
            var width = EstimateTextWidth(text, fontSize);
            var x = pageWidth / 2f;
            var y = WatermarkPageScale.GetTopBaselineY(pageWidth, pageHeight, fontSize);
            var floor = WatermarkPageScale.GetTopBandFloorY(pageWidth, pageHeight, fontSize);
            var stepX = Math.Max(10f, 12f * WatermarkPageScale.Get(pageWidth, pageHeight));
            var stepY = Math.Max(8f, fontSize * 0.55f);

            for (var row = 0; row < 4; row++)
            {
                var tryY = y - (row * stepY);
                if (tryY < floor)
                    break;

                for (var attempt = 0; attempt < 6; attempt++)
                {
                    var tryX = x;
                    if (attempt % 2 == 0)
                        tryX += (attempt / 2 + 1) * stepX;
                    else
                        tryX -= (attempt / 2 + 1) * stepX;

                    if (!Overlaps(occupied, tryX, tryY, width, fontSize + 6f, 1))
                    {
                        x = tryX;
                        y = tryY;
                        row = 99;
                        break;
                    }
                }
            }

            y = Math.Max(y, floor);

            return new WatermarkTextPlacement
            {
                Text = text,
                X = x,
                Y = y,
                Alignment = 1,
                FontSize = fontSize
            };
        }

        private static WatermarkTextPlacement PlaceTopLeftFree(
            string text,
            float fontSize,
            float pageWidth,
            float pageHeight,
            List<PdfTextRect> occupied)
        {
            var width = EstimateTextWidth(text, fontSize);
            var sideMargin = WatermarkPageScale.GetSideMargin(pageWidth, pageHeight);
            var y = WatermarkPageScale.GetTopBaselineY(pageWidth, pageHeight, fontSize);
            var floor = WatermarkPageScale.GetTopBandFloorY(pageWidth, pageHeight, fontSize);
            var x = sideMargin;
            var stepX = Math.Max(12f, 14f * WatermarkPageScale.Get(pageWidth, pageHeight));
            var stepY = Math.Max(8f, fontSize * 0.55f);
            var placed = false;

            for (var row = 0; row < 4 && !placed; row++)
            {
                var tryY = y - (row * stepY);
                if (tryY < floor)
                    break;

                for (var i = 0; i <= 4; i++)
                {
                    var tryX = sideMargin + (i * stepX);
                    if (!Overlaps(occupied, tryX, tryY, width, fontSize + 6f, 0))
                    {
                        x = tryX;
                        y = tryY;
                        placed = true;
                        break;
                    }
                }
            }

            return new WatermarkTextPlacement
            {
                Text = text,
                X = x,
                Y = y,
                Alignment = 0,
                FontSize = fontSize
            };
        }

        private static AnchorSearchZone[] GetSurfaceTreatmentZones(float pageWidth, float pageHeight, float fontSize)
        {
            var titleBlockTop = GetTitleBlockTopY(pageHeight);
            var titleBlockLeft = GetTitleBlockLeftX(pageWidth);
            var topBandFloor = WatermarkPageScale.GetTopBandFloorY(pageWidth, pageHeight, fontSize);
            var margin = 14f * WatermarkPageScale.Get(pageWidth, pageHeight);
            var zoneBottom = Math.Max(titleBlockTop + 88f, pageHeight * 0.37f);
            var zoneTop = Math.Max(zoneBottom + 48f, topBandFloor - margin);

            return new[]
            {
                new AnchorSearchZone(
                    pageWidth * 0.40f,
                    pageWidth - 32f,
                    zoneBottom,
                    zoneTop,
                    pageWidth * 0.62f,
                    zoneBottom + ((zoneTop - zoneBottom) * 0.62f)),
                new AnchorSearchZone(
                    38f,
                    Math.Max(pageWidth * 0.42f, titleBlockLeft - 28f),
                    zoneBottom,
                    zoneTop,
                    pageWidth * 0.30f,
                    zoneBottom + ((zoneTop - zoneBottom) * 0.50f))
            };
        }

        private static WatermarkTextPlacement PlaceSurfaceTreatmentInDrawingZone(
            string text,
            float fontSize,
            string pdfPath,
            int pageNumber,
            float pageWidth,
            float pageHeight,
            List<PdfTextRect> occupied)
        {
            var width = EstimateTextWidth(text, fontSize);
            var height = fontSize + 8f;
            var zones = GetSurfaceTreatmentZones(pageWidth, pageHeight, fontSize);
            var zone = zones[1];
            var centerX = zone.PreferredX;
            var baselineY = zone.PreferredY;

            if (!WatermarkProcessingContext.BatchMode)
            {
                var placed = false;
                if (PdfRasterLayoutService.IsAvailable)
                {
                    foreach (var searchZone in new[] { zones[1], zones[0] })
                    {
                        if (!PdfRasterLayoutService.TryFindFreeCenterAnchor(
                                pdfPath,
                                pageNumber,
                                width,
                                height,
                                pageWidth,
                                pageHeight,
                                searchZone.Left,
                                searchZone.Right,
                                searchZone.Bottom,
                                searchZone.Top,
                                searchZone.PreferredX,
                                searchZone.PreferredY,
                                out var rasterCenterX,
                                out var rasterBottomY))
                        {
                            continue;
                        }

                        var tryBaselineY = rasterBottomY + height - 6f;
                        if (Overlaps(occupied, rasterCenterX, tryBaselineY, width, height, 1))
                            continue;

                        centerX = rasterCenterX;
                        baselineY = tryBaselineY;
                        placed = true;
                        break;
                    }
                }

                if (!placed)
                {
                    var anchor = FindBestFreeAnchorInZones(
                        pdfPath,
                        pageNumber,
                        width,
                        height,
                        pageWidth,
                        pageHeight,
                        occupied,
                        preferFirstAcceptableZone: true,
                        new[] { zones[1], zones[0] });
                    centerX = anchor.X + (width / 2f);
                    baselineY = anchor.Y + height - 6f;
                }
            }

            return new WatermarkTextPlacement
            {
                Text = text,
                X = centerX,
                Y = baselineY,
                Alignment = 1,
                FontSize = fontSize
            };
        }

        private static void EnsureTopBandSeparation(
            List<WatermarkTextPlacement> placements,
            float pageWidth,
            float pageHeight,
            string pdfPath = null,
            int pageNumber = 1)
        {
            ReflowCenteredTopBandRow(placements, pageWidth, pageHeight, pdfPath, pageNumber);
        }

        private static bool TopBandHasOverlap(IList<WatermarkTextPlacement> topBand, float pageWidth)
        {
            var rects = topBand.Select(p => Inflate(MakeTextRect(p, pageWidth), 4f)).ToList();
            for (var i = 0; i < rects.Count; i++)
            {
                for (var j = i + 1; j < rects.Count; j++)
                {
                    if (Intersects(rects[i], rects[j]))
                        return true;
                }
            }

            return false;
        }

        private static float FitTopBandFontSize(
            string text,
            float maxWidth,
            float maxSize,
            float minSize,
            float pageWidth,
            float pageHeight)
        {
            var size = FitFontSize(text, maxWidth, maxSize, minSize, pageWidth, pageHeight);
            var bandCap = pageHeight * 0.034f;
            var scaledMin = minSize * WatermarkPageScale.Get(pageWidth, pageHeight);
            return Math.Max(scaledMin, Math.Min(size, bandCap));
        }

        private static WatermarkTextPlacement PlaceBottomLeftFree(
            string text,
            float fontSize,
            string pdfPath,
            int pageNumber,
            float pageWidth,
            float pageHeight,
            List<PdfTextRect> occupied)
        {
            var stack = PlaceBgStackBottomLeft(
                new[] { text },
                pdfPath,
                pageNumber,
                pageWidth,
                pageHeight,
                occupied);
            return stack.Count > 0
                ? stack[0]
                : new WatermarkTextPlacement
                {
                    Text = text,
                    X = 48f,
                    Y = GetTitleBlockTopY(pageHeight) + 58f,
                    Alignment = 0,
                    FontSize = fontSize
                };
        }

        private static List<WatermarkTextPlacement> PlaceBgStack(
            IList<string> lines,
            string pdfPath,
            int pageNumber,
            float pageWidth,
            float pageHeight,
            List<PdfTextRect> occupied,
            WatermarkSettings settings)
        {
            if (IsFixedRightVerticalBgPlacement(settings))
            {
                var cleanLines = (lines ?? Array.Empty<string>())
                    .Where(l => !string.IsNullOrWhiteSpace(l))
                    .Select(l => NormalizeBgNumber(l))
                    .Where(l => !string.IsNullOrWhiteSpace(l))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
                if (cleanLines.Count == 0)
                    return new List<WatermarkTextPlacement>();

                var result = cleanLines
                    .Select(line => new WatermarkTextPlacement
                    {
                        Text = line,
                        Category = "Bg",
                        FontSize = WatermarkPageScale.GetPageNumberFontSize(pageWidth, pageHeight)
                    })
                    .ToList();
                PlaceFixedRightVerticalBgStack(result, pdfPath, pageNumber, pageWidth, pageHeight);
                return result;
            }

            return PlaceBgStackBottomLeft(lines, pdfPath, pageNumber, pageWidth, pageHeight, occupied);
        }

        private static List<WatermarkTextPlacement> PlaceBgStackBottomLeft(
            IList<string> lines,
            string pdfPath,
            int pageNumber,
            float pageWidth,
            float pageHeight,
            List<PdfTextRect> occupied)
        {
            var cleanLines = (lines ?? Array.Empty<string>())
                .Where(l => !string.IsNullOrWhiteSpace(l))
                .Select(l => NormalizeBgNumber(l))
                .Where(l => !string.IsNullOrWhiteSpace(l))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (cleanLines.Count == 0)
                return new List<WatermarkTextPlacement>();

            var titleBlockTop = GetTitleBlockTopY(pageHeight);
            var titleBlockLeft = GetTitleBlockLeftX(pageWidth);
            var longestLine = cleanLines.OrderByDescending(l => l.Length).First();
            var fontSize = FitFontSize(longestLine, pageWidth * 0.35f, 24f, 14f, pageWidth, pageHeight);
            var lineHeight = fontSize + 8f;
            var blockWidth = cleanLines.Max(l => EstimateTextWidth(l, fontSize));
            var blockHeight = (cleanLines.Count * lineHeight) + 4f;

            PdfPoint anchor;
            if (WatermarkProcessingContext.BatchMode)
            {
                // Oberste BG-Zeile; weitere Zeilen darunter (Y nimmt ab).
                anchor = new PdfPoint
                {
                    X = 48f,
                    Y = titleBlockTop + 58f + Math.Max(0, cleanLines.Count - 1) * lineHeight
                };
            }
            else
            {
                anchor = FindBestFreeAnchor(
                    pdfPath,
                    pageNumber,
                    blockWidth,
                    blockHeight,
                    pageWidth,
                    pageHeight,
                    occupied,
                    zoneLeft: 35f,
                    zoneRight: Math.Max(120f, titleBlockLeft - 25f),
                    zoneBottom: titleBlockTop + 8f,
                    zoneTop: titleBlockTop + 135f + Math.Max(0, cleanLines.Count - 1) * lineHeight,
                    preferredX: 48f,
                    preferredY: titleBlockTop + 58f + Math.Max(0, cleanLines.Count - 1) * lineHeight);
            }

            var stackTopY = anchor.Y;
            var result = new List<WatermarkTextPlacement>();
            for (var i = 0; i < cleanLines.Count; i++)
            {
                result.Add(new WatermarkTextPlacement
                {
                    Text = cleanLines[i],
                    X = anchor.X,
                    Y = stackTopY - (i * lineHeight),
                    Alignment = 0,
                    FontSize = fontSize,
                    Category = "Bg"
                });
            }

            return result;
        }

        private static void ReflowBgStack(
            List<WatermarkTextPlacement> placements,
            float pageWidth,
            float pageHeight,
            string pdfPath = null,
            int pageNumber = 1,
            WatermarkSettings settings = null)
        {
            if (placements == null || pageWidth <= 0f || pageHeight <= 0f)
                return;

            settings = settings ?? WatermarkSettingsStore.Load();
            if (IsFixedRightVerticalBgPlacement(settings))
            {
                EnforceFixedRightVerticalBgPlacements(
                    placements, pdfPath, pageNumber, pageWidth, pageHeight, settings);
                return;
            }

            var bgPlacements = placements
                .Where(p => p != null && string.Equals(p.Category, "Bg", StringComparison.OrdinalIgnoreCase))
                .Where(p => !string.IsNullOrWhiteSpace(p.Text))
                .ToList();

            if (bgPlacements.Count == 0)
                return;

            // Eine Zeile mit " | " in mehrere Zeilen aufteilen.
            if (bgPlacements.Count == 1 && SplitBgLines(bgPlacements[0].Text).Count > 1)
            {
                var lines = SplitBgLines(bgPlacements[0].Text);
                placements.RemoveAll(p =>
                    p != null && string.Equals(p.Category, "Bg", StringComparison.OrdinalIgnoreCase));
                var rebuilt = PlaceBgStackBottomLeft(lines, pdfPath, pageNumber, pageWidth, pageHeight, new List<PdfTextRect>());
                placements.AddRange(rebuilt);
                return;
            }

            if (bgPlacements.Count == 1)
                return;

            // Nach Sync oft ohne gültige Y-Position → komplett neu unten links stapeln.
            if (bgPlacements.All(p => p.Y <= 1f))
            {
                var lines = bgPlacements.Select(p => p.Text).ToList();
                placements.RemoveAll(p =>
                    p != null && string.Equals(p.Category, "Bg", StringComparison.OrdinalIgnoreCase));
                placements.AddRange(
                    PlaceBgStackBottomLeft(lines, pdfPath, pageNumber, pageWidth, pageHeight, new List<PdfTextRect>()));
                return;
            }

            var fontSize = bgPlacements
                .Where(p => p.FontSize > 0f)
                .Select(p => p.FontSize)
                .DefaultIfEmpty(WatermarkPageScale.GetPageNumberFontSize(pageWidth, pageHeight))
                .Max();
            var lineHeight = fontSize + 8f;
            var topY = bgPlacements.Max(p => p.Y);
            var leftX = bgPlacements[0].X;
            if (leftX <= 0f)
                leftX = 48f;

            for (var i = 0; i < bgPlacements.Count; i++)
            {
                bgPlacements[i].FontSize = fontSize;
                bgPlacements[i].X = leftX;
                bgPlacements[i].Y = topY - (i * lineHeight);
                bgPlacements[i].Alignment = 0;
                bgPlacements[i].Angle = 0f;
            }
        }

        private static List<string> BuildBgLines(
            IList<(string quantity, string deliveryDate, string deliveryLocation, string bgDrawingNumber, string orderNumber)> entries)
        {
            if (entries == null || entries.Count == 0)
                return new List<string>();

            return entries
                .Select(e => NormalizeBgNumber(e.bgDrawingNumber))
                .Where(l => !string.IsNullOrWhiteSpace(l))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static List<string> SplitBgLines(string bgText)
        {
            if (string.IsNullOrWhiteSpace(bgText))
                return new List<string>();

            return bgText
                .Split(new[] { '|', '\n', '\r', ';' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(NormalizeBgNumber)
                .Where(l => !string.IsNullOrWhiteSpace(l))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static List<string> BuildQuantityLines(
            IList<(string quantity, string deliveryDate, string deliveryLocation, string bgDrawingNumber, string orderNumber)> entries)
        {
            var lines = new List<string>();
            if (entries == null || entries.Count == 0)
                return lines;

            foreach (var entry in entries)
            {
                var qty = NormalizeQuantity(entry.quantity);
                if (string.IsNullOrWhiteSpace(qty))
                    continue;

                if (!string.IsNullOrWhiteSpace(entry.deliveryDate))
                    lines.Add(NormalizeDate(entry.deliveryDate) + "   " + qty);
                else
                    lines.Add(qty);
            }

            return lines;
        }

        private static void SyncMaterialStackFromTopLine(List<WatermarkTextPlacement> placements, float fontSize)
        {
            var materialLines = placements
                .Where(p => p != null && string.Equals(p.Category, "Material", StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (materialLines.Count <= 1)
                return;

            var lineHeight = PdfRasterLayoutService.GetMaterialLineHeight(fontSize);
            var topY = materialLines[0].Y;
            for (var i = 1; i < materialLines.Count; i++)
            {
                materialLines[i].X = materialLines[0].X;
                materialLines[i].Y = topY - (i * lineHeight);
                materialLines[i].Alignment = 1;
            }
        }

        private static List<WatermarkTextPlacement> PlaceMaterialCenterStack(
            IList<string> lines,
            string pdfPath,
            int pageNumber,
            float pageWidth,
            float pageHeight,
            List<PdfTextRect> occupied)
        {
            var result = MaterialWatermarkPlacementService.PlaceDynamic(
                lines, pdfPath, pageNumber, pageWidth, pageHeight, occupied);
            foreach (var placement in result)
                placement.Category = "Material";
            return result;
        }

        private static bool OverlapsTextWatermark(
            List<PdfTextRect> occupied,
            float centerX,
            float anchorY,
            float width,
            float height)
        {
            return OverlapScore(occupied, centerX, anchorY, width, height, 1) > 75000f;
        }

        private static List<WatermarkTextPlacement> PlaceQuantityStackTopLeft(
            IList<string> lines,
            float pageWidth,
            float pageHeight)
        {
            var cleanLines = lines
                .Where(l => !string.IsNullOrWhiteSpace(l))
                .Select(l => l.Trim())
                .ToList();

            if (cleanLines.Count == 0)
                return new List<WatermarkTextPlacement>();

            var longestLine = cleanLines.OrderByDescending(l => l.Length).First();
            var fontSize = WatermarkPageScale.GetPageNumberFontSize(pageWidth, pageHeight);
            var lineHeight = fontSize + 8f;
            var sideMargin = WatermarkPageScale.GetTopLeftMargin(pageWidth, pageHeight);
            var topY = WatermarkPageScale.GetMaxTopBaselineY(pageWidth, pageHeight, fontSize);
            var result = new List<WatermarkTextPlacement>();

            for (var i = 0; i < cleanLines.Count; i++)
            {
                result.Add(new WatermarkTextPlacement
                {
                    Text = cleanLines[i],
                    X = sideMargin,
                    Y = topY - (i * lineHeight),
                    Alignment = 0,
                    FontSize = fontSize,
                    UseRedText = true
                });
            }

            return result;
        }

        private static List<WatermarkTextPlacement> PlaceQuantityStackRight(
            IList<string> lines,
            string pdfPath,
            int pageNumber,
            float pageWidth,
            float pageHeight,
            List<PdfTextRect> occupied)
        {
            var cleanLines = lines
                .Where(l => !string.IsNullOrWhiteSpace(l))
                .Select(l => l.Trim())
                .ToList();

            if (cleanLines.Count == 0)
                return new List<WatermarkTextPlacement>();

            var longestLine = cleanLines.OrderByDescending(l => l.Length).First();
            var fontSize = FitFontSize(longestLine, pageWidth * 0.30f, 28f, 14f, pageWidth, pageHeight);
            var lineHeight = fontSize + 8f;
            var blockWidth = cleanLines.Max(l => EstimateTextWidth(l, fontSize));
            var blockHeight = (cleanLines.Count * lineHeight) + 4f;

            PdfPoint anchor;
            if (WatermarkProcessingContext.BatchMode)
            {
                var margin = WatermarkPageScale.GetSideMargin(pageWidth, pageHeight);
                anchor = new PdfPoint
                {
                    X = pageWidth - margin - 8f,
                    Y = WatermarkPageScale.GetMaxTopBaselineY(pageWidth, pageHeight, fontSize)
                };
            }
            else
            {
                anchor = FindBestFreeAnchorInZones(
                    pdfPath,
                    pageNumber,
                    blockWidth,
                    blockHeight,
                    pageWidth,
                    pageHeight,
                    occupied,
                    preferFirstAcceptableZone: false,
                    GetQuantityStackZones(pageWidth, pageHeight, blockWidth));
            }

            var topY = anchor.Y + blockHeight - lineHeight;
            var result = new List<WatermarkTextPlacement>();

            for (var i = 0; i < cleanLines.Count; i++)
            {
                result.Add(new WatermarkTextPlacement
                {
                    Text = cleanLines[i],
                    X = anchor.X,
                    Y = topY - (i * lineHeight),
                    Alignment = 2,
                    FontSize = fontSize,
                    UseRedText = IsDateLeadingQuantityLine(cleanLines[i])
                });
            }

            return result;
        }

        private struct AnchorSearchZone
        {
            public float Left;
            public float Right;
            public float Bottom;
            public float Top;
            public float PreferredX;
            public float PreferredY;

            public AnchorSearchZone(float left, float right, float bottom, float top, float preferredX, float preferredY)
            {
                Left = left;
                Right = right;
                Bottom = bottom;
                Top = top;
                PreferredX = preferredX;
                PreferredY = preferredY;
            }
        }

        private static PdfPoint FindBestFreeAnchorInZones(
            string pdfPath,
            int pageNumber,
            float blockWidth,
            float blockHeight,
            float pageWidth,
            float pageHeight,
            List<PdfTextRect> occupied,
            bool preferFirstAcceptableZone,
            AnchorSearchZone[] zones)
        {
            if (WatermarkProcessingContext.BatchMode && zones != null && zones.Length > 0)
            {
                var zone = zones[0];
                return new PdfPoint { X = zone.PreferredX, Y = zone.PreferredY };
            }

            PdfPoint? best = null;
            var bestDark = float.MaxValue;

            foreach (var zone in zones)
            {
                var candidate = FindBestFreeAnchor(
                    pdfPath,
                    pageNumber,
                    blockWidth,
                    blockHeight,
                    pageWidth,
                    pageHeight,
                    occupied,
                    zoneLeft: zone.Left,
                    zoneRight: zone.Right,
                    zoneBottom: zone.Bottom,
                    zoneTop: zone.Top);

                if (preferFirstAcceptableZone
                    && IsRasterPlacementAcceptable(
                        pdfPath,
                        pageNumber,
                        candidate.X,
                        candidate.Y,
                        blockWidth,
                        blockHeight,
                        pageWidth,
                        pageHeight,
                        0,
                        isMaterial: false))
                {
                    return candidate;
                }

                var dark = PdfRasterLayoutService.IsAvailable
                    ? PdfRasterLayoutService.MeasureMaterialPlacementDarkness(
                        pdfPath,
                        pageNumber,
                        candidate.X + (blockWidth / 2f),
                        candidate.Y,
                        blockWidth,
                        blockHeight,
                        pageWidth,
                        pageHeight)
                    : 1f;

                if (dark < bestDark)
                {
                    bestDark = dark;
                    best = candidate;
                }
            }

            if (best.HasValue)
                return best.Value;

            return FindBestFreeAnchor(
                pdfPath,
                pageNumber,
                blockWidth,
                blockHeight,
                pageWidth,
                pageHeight,
                occupied,
                zones[0].Left,
                zones[0].Right,
                zones[0].Bottom,
                zones[0].Top);
        }

        private static PdfPoint FindBestFreeAnchor(
            string pdfPath,
            int pageNumber,
            float blockWidth,
            float blockHeight,
            float pageWidth,
            float pageHeight,
            List<PdfTextRect> occupied,
            float? zoneLeft = null,
            float? zoneRight = null,
            float? zoneBottom = null,
            float? zoneTop = null,
            float? preferredX = null,
            float? preferredY = null)
        {
            var titleBlockTop = GetTitleBlockTopY(pageHeight);
            var titleBlockLeft = GetTitleBlockLeftX(pageWidth);
            var leftBound = zoneLeft ?? 35f;
            var rightBound = zoneRight ?? Math.Max(leftBound + 40f, titleBlockLeft - 25f);
            var bottomBound = zoneBottom ?? (titleBlockTop + 8f);
            var topBound = zoneTop ?? (titleBlockTop + 135f);

            rightBound = Math.Min(rightBound, pageWidth - blockWidth - 20f);
            if (rightBound < leftBound)
                rightBound = leftBound;
            if (topBound < bottomBound)
                topBound = bottomBound;

            if (WatermarkProcessingContext.BatchMode)
            {
                var x = preferredX ?? leftBound;
                var y = preferredY ?? bottomBound;
                x = Math.Max(leftBound, Math.Min(x, rightBound));
                y = Math.Max(bottomBound, Math.Min(y, topBound));
                return new PdfPoint { X = x, Y = y };
            }

            var step = WatermarkProcessingContext.BatchMode ? 22f : 10f;
            var bestDark = float.MaxValue;
            var bestX = leftBound;
            var bestY = bottomBound;
            var found = false;

            for (var y = bottomBound; y <= topBound; y += step)
            {
                for (var x = leftBound; x <= rightBound; x += step)
                {
                    if (Overlaps(occupied, x, y, blockWidth, blockHeight, 0))
                        continue;

                    if (!IsRasterPlacementAcceptable(pdfPath, pageNumber, x, y, blockWidth, blockHeight, pageWidth, pageHeight, 0))
                        continue;

                    var dark = PdfRasterLayoutService.IsAvailable
                        ? PdfRasterLayoutService.MeasureMaterialPlacementDarkness(
                            pdfPath,
                            pageNumber,
                            x + (blockWidth / 2f),
                            y,
                            blockWidth,
                            blockHeight,
                            pageWidth,
                            pageHeight)
                        : ScorePlacement(x, y, blockWidth, blockHeight, 0, occupied, pageWidth, pageHeight) / 100000f;

                    if (dark >= bestDark)
                        continue;

                    bestDark = dark;
                    bestX = x;
                    bestY = y;
                    found = true;
                }
            }

            if (!found)
            {
                bestX = preferredX ?? leftBound;
                bestY = preferredY ?? bottomBound;
            }

            return new PdfPoint { X = bestX, Y = bestY };
        }

        private static float DistancePenalty(float anchorX, float anchorY, float targetX, float targetY, float pageWidth, float pageHeight)
        {
            var dx = (anchorX - targetX) / Math.Max(120f, pageWidth * 0.35f);
            var dy = (anchorY - targetY) / Math.Max(80f, pageHeight * 0.18f);
            return (dx * dx) + (dy * dy * 2.5f);
        }

        private static float LocalTextDensity(float anchorX, float anchorY, float width, float height, List<PdfTextRect> occupied)
        {
            var centerX = anchorX + (width / 2f);
            var centerY = anchorY + (height / 2f);
            var density = 0f;

            foreach (var rect in occupied)
            {
                var rectCenterX = (rect.Left + rect.Right) / 2f;
                var rectCenterY = (rect.Bottom + rect.Top) / 2f;
                var dx = centerX - rectCenterX;
                var dy = centerY - rectCenterY;
                var distance = (float)Math.Sqrt((dx * dx) + (dy * dy));
                if (distance > 220f)
                    continue;

                density += (rect.Width * rect.Height) / Math.Max(18f, distance);
            }

            return density;
        }

        private struct PdfPoint
        {
            public float X;
            public float Y;
        }

        private static float GetTitleBlockTopY(float pageHeight)
        {
            return pageHeight * 0.22f;
        }

        private static float GetTitleBlockLeftX(float pageWidth)
        {
            return pageWidth * 0.58f;
        }

        private static float ScorePlacement(
            float anchorX,
            float anchorY,
            float width,
            float height,
            int alignment,
            List<PdfTextRect> occupied,
            float pageWidth,
            float pageHeight)
        {
            var score = OverlapScore(occupied, anchorX, anchorY, width, height, alignment);
            var titleBlockTop = GetTitleBlockTopY(pageHeight);

            var left = alignment == 2
                ? anchorX - width
                : alignment == 1
                    ? anchorX - (width / 2f)
                    : anchorX;

            var bottom = anchorY - 4f;

            // Schriftfeld strikt meiden
            if (bottom < titleBlockTop + 8f)
                score += 1000000f;

            // Obere Blatt-Hälfte meiden (häufig leerer Rand, aber falsche Position)
            if (anchorY > pageHeight * 0.48f)
                score += 350000f;

            // Mitte unten (Logo-Bereich) extra meiden
            var titleBlockLeft = GetTitleBlockLeftX(pageWidth);
            if (bottom < titleBlockTop + 55f && left > titleBlockLeft - 40f && left < pageWidth - 20f)
                score += 500000f;

            return score;
        }

        private static PdfTextRect MakeTextRect(WatermarkTextPlacement placement, float pageWidth)
        {
            var width = EstimateTextWidth(placement.Text, placement.FontSize);
            var height = placement.FontSize + 8f;
            var left = placement.Alignment == 2
                ? placement.X - width
                : placement.Alignment == 1
                    ? placement.X - (width / 2f)
                    : placement.X;

            return new PdfTextRect
            {
                Text = placement.Text,
                Left = Math.Max(8f, left),
                Right = Math.Min(pageWidth - 8f, left + width),
                Bottom = placement.Y - 4f,
                Top = placement.Y + height
            };
        }

        private static PdfTextRect Inflate(PdfTextRect rect, float padding)
        {
            return new PdfTextRect
            {
                Text = rect.Text,
                Left = rect.Left - padding,
                Right = rect.Right + padding,
                Bottom = rect.Bottom - padding,
                Top = rect.Top + padding
            };
        }

        private static bool Overlaps(List<PdfTextRect> occupied, float anchorX, float anchorY, float width, float height, int alignment)
        {
            return OverlapScore(occupied, anchorX, anchorY, width, height, alignment) > 0f;
        }

        private static float OverlapScore(List<PdfTextRect> occupied, float anchorX, float anchorY, float width, float height, int alignment)
        {
            var left = alignment == 2
                ? anchorX - width
                : alignment == 1
                    ? anchorX - (width / 2f)
                    : anchorX;

            var candidate = new PdfTextRect
            {
                Left = left,
                Right = left + width,
                Bottom = anchorY - 4f,
                Top = anchorY + height
            };

            float score = 0f;
            foreach (var rect in occupied)
            {
                if (Intersects(candidate, rect))
                    score += rect.Width * rect.Height;
            }

            if (score > 0f)
                score += 75000f;

            return score;
        }

        private static bool Intersects(PdfTextRect a, PdfTextRect b)
        {
            return a.Left < b.Right && a.Right > b.Left && a.Bottom < b.Top && a.Top > b.Bottom;
        }

        internal static float EstimateTextWidth(string text, float fontSize)
        {
            if (string.IsNullOrEmpty(text))
                return 10f;

            return Math.Max(20f, text.Length * fontSize * 0.55f);
        }

        internal static float FitFontSize(string text, float maxWidth, float maxSize, float minSize, float pageWidth, float pageHeight)
        {
            var scale = WatermarkPageScale.Get(pageWidth, pageHeight);
            var size = maxSize * scale;
            var min = minSize * scale;

            while (size > min && EstimateTextWidth(text, size) > maxWidth)
                size -= 1f;

            return size;
        }

        private static string JoinDistinct(IEnumerable<string> values)
        {
            var list = values
                .Where(v => !string.IsNullOrWhiteSpace(v))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            return list.Count == 0 ? string.Empty : string.Join(" | ", list);
        }

        private static string NormalizeQuantity(string quantity)
        {
            if (string.IsNullOrWhiteSpace(quantity))
                return string.Empty;

            var q = quantity.Trim();
            if (q.EndsWith("X", StringComparison.OrdinalIgnoreCase))
                q = q.Substring(0, q.Length - 1) + "x";
            else
                q += "x";

            return q;
        }

        private static bool IsDateLeadingQuantityLine(string line)
        {
            if (string.IsNullOrWhiteSpace(line))
                return false;

            return Regex.IsMatch(line.Trim(), @"^(?:(?:LT|FT)-)?\d{1,2}\.\d{1,2}\.\d{4}");
        }

        private static string NormalizeDate(string deliveryDate)
        {
            if (string.IsNullOrWhiteSpace(deliveryDate))
                return string.Empty;

            var date = deliveryDate.Trim();
            if (date.Equals("FT-", StringComparison.OrdinalIgnoreCase)
                || date.Equals("LT-", StringComparison.OrdinalIgnoreCase))
                return date;

            var isManufacturing = false;
            if (date.StartsWith("FT-", StringComparison.OrdinalIgnoreCase))
            {
                isManufacturing = true;
                date = date.Substring(3).TrimStart();
            }
            else if (date.StartsWith("LT-", StringComparison.OrdinalIgnoreCase))
            {
                date = date.Substring(3).TrimStart();
            }
            else if (date.StartsWith("FT:", StringComparison.OrdinalIgnoreCase))
            {
                isManufacturing = true;
                date = date.Substring(3).TrimStart();
            }
            else if (date.StartsWith("LT:", StringComparison.OrdinalIgnoreCase))
            {
                date = date.Substring(3).TrimStart();
            }

            var normalized = OrderVerificationDateHelper.TryNormalizeExcelDate(date);
            if (string.IsNullOrWhiteSpace(normalized))
            {
                if (isManufacturing)
                    return "FT-";

                return string.Empty;
            }

            return (isManufacturing ? "FT-" : "LT-") + normalized;
        }

        internal static bool IsSubgroupPartDrawing(string drawingHint, string bgNumberText)
        {
            if (string.IsNullOrWhiteSpace(bgNumberText))
                return false;

            var currentKey = OrderVerificationDrawingKey.Normalize(drawingHint);
            if (string.IsNullOrWhiteSpace(currentKey))
                return true;

            // Mehrere BG-Marker möglich (JoinDistinct) – Treffer auf irgendeinen zählt als Baugruppe.
            var tokens = bgNumberText.Split(new[] { '|', ';', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
            var foundAnyBg = false;
            foreach (var token in tokens)
            {
                if (!OrderVerificationDrawingKey.TryExtractFromBgMarker(token, out var bgDrawing))
                    continue;

                foundAnyBg = true;
                if (OrderVerificationDrawingKey.Equal(currentKey, bgDrawing))
                    return false;
            }

            if (!foundAnyBg)
            {
                if (!OrderVerificationDrawingKey.TryExtractFromBgMarker(bgNumberText, out _))
                    return true;
            }

            return true;
        }

        private static string NormalizeLocation(string deliveryLocation)
        {
            return string.IsNullOrWhiteSpace(deliveryLocation) ? string.Empty : deliveryLocation.Trim();
        }

        private static string NormalizeBgNumber(string bgDrawingNumber)
        {
            if (string.IsNullOrWhiteSpace(bgDrawingNumber))
                return string.Empty;

            var bg = bgDrawingNumber.Trim();
            if (!bg.StartsWith("BG:", StringComparison.OrdinalIgnoreCase))
                bg = "BG: " + bg;

            return bg;
        }
    }
}
