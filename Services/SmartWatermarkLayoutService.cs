using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace PDF_umbenenen.Services
{
    internal sealed class WatermarkFieldData
    {
        public List<string> QuantityLines { get; set; } = new List<string>();

        public string QuantityText { get; set; }

        public string DeliveryDateText { get; set; }

        public string DeliveryLocationText { get; set; }

        public string BgNumberText { get; set; }

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

            var occupied = layout.TextRects
                .Select(r => Inflate(r, 6f))
                .ToList();

            if (!string.IsNullOrWhiteSpace(fields.OrderNumberText))
            {
                var orderText = fields.OrderNumberText.Trim();
                var fontSize = FitFontSize(orderText, pageWidth * 0.30f, 17f, 9f, pageWidth, pageHeight);
                var placement = new WatermarkTextPlacement
                {
                    Text = orderText,
                    X = pageWidth - WatermarkPageScale.GetSideMargin(pageWidth, pageHeight),
                    Y = WatermarkPageScale.GetTopBaselineY(pageWidth, pageHeight, fontSize),
                    Alignment = 2,
                    FontSize = fontSize
                };
                placements.Add(placement);
                occupied.Add(MakeTextRect(placement, pageWidth));
            }

            if (!string.IsNullOrWhiteSpace(fields.DeliveryLocationText))
            {
                var locationText = fields.DeliveryLocationText.Trim();
                var fontSize = FitFontSize(locationText, pageWidth * 0.55f, 28f, 16f, pageWidth, pageHeight);
                var placement = PlaceTopCenter(locationText, fontSize, pageWidth, pageHeight, occupied);
                placements.Add(placement);
                occupied.Add(MakeTextRect(placement, pageWidth));
            }

            if (!string.IsNullOrWhiteSpace(fields.DeliveryDateText)
                && (fields.QuantityLines == null || fields.QuantityLines.Count <= 1))
            {
                var dateText = fields.DeliveryDateText.Trim();
                var fontSize = FitFontSize(dateText, pageWidth * 0.35f, 22f, 14f, pageWidth, pageHeight);
                var placement = PlaceTopLeftFree(dateText, fontSize, pageWidth, pageHeight, occupied);
                placements.Add(placement);
                occupied.Add(MakeTextRect(placement, pageWidth));
            }

            if (fields.QuantityLines != null && fields.QuantityLines.Count > 0)
            {
                var stackPlacements = PlaceQuantityStack(fields.QuantityLines, pdfPath, pageNumber, pageWidth, pageHeight, occupied);
                foreach (var placement in stackPlacements)
                {
                    placements.Add(placement);
                    occupied.Add(MakeTextRect(placement, pageWidth));
                }
            }
            else if (!string.IsNullOrWhiteSpace(fields.QuantityText))
            {
                var stackPlacements = PlaceQuantityStack(new List<string> { fields.QuantityText.Trim() }, pdfPath, pageNumber, pageWidth, pageHeight, occupied);
                foreach (var placement in stackPlacements)
                {
                    placements.Add(placement);
                    occupied.Add(MakeTextRect(placement, pageWidth));
                }
            }

            var materialInfo = PdfMaterialExtractionService.Extract(pdfPath, pageNumber);
            var materialLines = new List<string>();
            var showType = settings == null || settings.ExtractMaterialType;
            var showThickness = settings == null || settings.ExtractMaterialThickness;

            if (showType && showThickness && !string.IsNullOrWhiteSpace(materialInfo.MaterialWatermarkText))
            {
                materialLines.Add(materialInfo.MaterialWatermarkText.Trim());
            }
            else if (showThickness && !string.IsNullOrWhiteSpace(materialInfo.ThicknessText))
            {
                var compact = MaterialAlloyClassifier.FormatThicknessCompact(materialInfo.ThicknessText);
                if (!string.IsNullOrWhiteSpace(compact))
                    materialLines.Add(compact);
            }
            else if (showType && !string.IsNullOrWhiteSpace(materialInfo.MaterialAlloyPrefix))
            {
                materialLines.Add(materialInfo.MaterialAlloyPrefix.Trim());
            }

            if (materialLines.Count > 0)
            {
                var materialPlacements = PlaceMaterialCenterStack(
                    materialLines,
                    pdfPath,
                    pageNumber,
                    pageWidth,
                    pageHeight,
                    occupied);
                foreach (var placement in materialPlacements)
                {
                    placements.Add(placement);
                    occupied.Add(MakeTextRect(placement, pageWidth));
                }
            }

            if (!string.IsNullOrWhiteSpace(fields.BgNumberText))
            {
                var bgText = fields.BgNumberText.Trim();
                var fontSize = FitFontSize(bgText, pageWidth * 0.35f, 24f, 14f, pageWidth, pageHeight);
                var placement = PlaceBottomLeftFree(bgText, fontSize, pdfPath, pageNumber, pageWidth, pageHeight, occupied);
                placements.Add(placement);
                occupied.Add(MakeTextRect(placement, pageWidth));
            }

            return placements;
        }

        public static WatermarkFieldData BuildFieldData(
            IEnumerable<(string quantity, string deliveryDate, string deliveryLocation, string bgDrawingNumber, string orderNumber)> entries,
            IList<string> orderNumbers)
        {
            var list = entries?.ToList() ?? new List<(string, string, string, string, string)>();
            var data = new WatermarkFieldData();

            data.QuantityLines = BuildQuantityLines(list);
            data.QuantityText = data.QuantityLines.Count > 0
                ? string.Join(Environment.NewLine, data.QuantityLines)
                : string.Empty;
            data.DeliveryDateText = JoinDistinct(list.Select(e => NormalizeDate(e.deliveryDate)));
            data.DeliveryLocationText = JoinDistinct(list.Select(e => NormalizeLocation(e.deliveryLocation)));
            data.BgNumberText = JoinDistinct(list.Select(e => NormalizeBgNumber(e.bgDrawingNumber)));

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
            var step = Math.Max(10f, 12f * WatermarkPageScale.Get(pageWidth, pageHeight));

            // Nur im oberen Randstreifen bleiben – bei Kollision leicht seitlich verschieben, nicht in die Zeichnung.
            for (var attempt = 0; attempt < 6; attempt++)
            {
                if (!Overlaps(occupied, x, y, width, fontSize + 6f, 1))
                    break;

                if (attempt % 2 == 0)
                    x += step;
                else
                    x -= step * 2f;
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
            var x = sideMargin;

            if (Overlaps(occupied, x, y, width, fontSize + 6f, 0))
            {
                var step = Math.Max(12f, 14f * WatermarkPageScale.Get(pageWidth, pageHeight));
                for (var i = 1; i <= 4; i++)
                {
                    var tryX = sideMargin + (i * step);
                    if (!Overlaps(occupied, tryX, y, width, fontSize + 6f, 0))
                    {
                        x = tryX;
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

        private static WatermarkTextPlacement PlaceBottomLeftFree(
            string text,
            float fontSize,
            string pdfPath,
            int pageNumber,
            float pageWidth,
            float pageHeight,
            List<PdfTextRect> occupied)
        {
            var titleBlockTop = GetTitleBlockTopY(pageHeight);
            var titleBlockLeft = GetTitleBlockLeftX(pageWidth);
            var width = EstimateTextWidth(text, fontSize);
            var height = fontSize + 8f;
            var anchor = FindBestFreeAnchor(
                pdfPath,
                pageNumber,
                width,
                height,
                pageWidth,
                pageHeight,
                occupied,
                zoneLeft: 35f,
                zoneRight: Math.Max(120f, titleBlockLeft - 25f),
                zoneBottom: titleBlockTop + 8f,
                zoneTop: titleBlockTop + 135f,
                preferredX: 48f,
                preferredY: titleBlockTop + 58f);

            return new WatermarkTextPlacement
            {
                Text = text,
                X = anchor.X,
                Y = anchor.Y,
                Alignment = 0,
                FontSize = fontSize
            };
        }

        private static List<string> BuildQuantityLines(
            IList<(string quantity, string deliveryDate, string deliveryLocation, string bgDrawingNumber, string orderNumber)> entries)
        {
            var lines = new List<string>();
            if (entries == null || entries.Count == 0)
                return lines;

            var showDate = entries.Count > 1;
            foreach (var entry in entries)
            {
                var qty = NormalizeQuantity(entry.quantity);
                if (string.IsNullOrWhiteSpace(qty))
                    continue;

                if (showDate && !string.IsNullOrWhiteSpace(entry.deliveryDate))
                    lines.Add(NormalizeDate(entry.deliveryDate) + "   " + qty);
                else
                    lines.Add(qty);
            }

            return lines;
        }

        private static List<WatermarkTextPlacement> PlaceMaterialCenterStack(
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
            var fontSize = FitFontSize(longestLine, pageWidth * 0.55f, 44f, 26f, pageWidth, pageHeight);
            var lineHeight = fontSize + 10f;
            var blockWidth = cleanLines.Max(l => EstimateTextWidth(l, fontSize));
            var blockHeight = (cleanLines.Count * lineHeight) + 6f;

            var titleBlockTop = GetTitleBlockTopY(pageHeight);
            var titleBlockLeft = GetTitleBlockLeftX(pageWidth);
            var freeBandLeft = 35f;
            var freeBandRight = Math.Max(160f, titleBlockLeft - 40f);
            var freeBandBottom = titleBlockTop + 8f;
            var freeBandTop = titleBlockTop + 155f;
            var preferredCenterX = Math.Max(freeBandLeft + (blockWidth / 2f) + 8f, (freeBandLeft + freeBandRight) / 2f);
            var preferredAnchorY = titleBlockTop + 72f;

            var anchor = FindBestFreeAnchorInZones(
                pdfPath,
                pageNumber,
                blockWidth,
                blockHeight,
                pageWidth,
                pageHeight,
                occupied,
                preferFirstAcceptableZone: true,
                new[]
                {
                    // Freier Streifen links vom Schriftfeld – unterhalb der Zeichnung, nicht auf der Zeichnung
                    new AnchorSearchZone(
                        freeBandLeft,
                        freeBandRight,
                        freeBandBottom,
                        freeBandTop,
                        preferredCenterX - (blockWidth / 2f),
                        preferredAnchorY),
                    new AnchorSearchZone(
                        freeBandLeft,
                        Math.Max(120f, titleBlockLeft - 70f),
                        freeBandBottom,
                        titleBlockTop + 130f,
                        48f,
                        titleBlockTop + 66f)
                });

            var centerX = anchor.X + (blockWidth / 2f);
            var topY = anchor.Y + blockHeight - lineHeight;
            var result = new List<WatermarkTextPlacement>();
            for (var i = 0; i < cleanLines.Count; i++)
            {
                result.Add(new WatermarkTextPlacement
                {
                    Text = cleanLines[i],
                    X = centerX,
                    Y = topY - (i * lineHeight),
                    Alignment = 1,
                    FontSize = fontSize
                });
            }

            return result;
        }

        private static PdfPoint FindBestMaterialAnchor(
            string pdfPath,
            int pageNumber,
            float blockWidth,
            float blockHeight,
            float pageWidth,
            float pageHeight,
            List<PdfTextRect> occupied)
        {
            var titleBlockTop = GetTitleBlockTopY(pageHeight);
            var titleBlockLeft = GetTitleBlockLeftX(pageWidth);
            var leftBound = 55f;
            var rightBound = Math.Max(leftBound + 120f, titleBlockLeft - 50f);
            var bottomBound = titleBlockTop + 8f;
            var topBound = titleBlockTop + 155f;
            var centerX = (leftBound + rightBound) / 2f;
            var targetY = titleBlockTop + 72f - (blockHeight / 2f);
            var upperY = titleBlockTop + 95f - (blockHeight / 2f);
            var lowerY = titleBlockTop + 52f - (blockHeight / 2f);

            if (PdfRasterLayoutService.IsAvailable
                && PdfRasterLayoutService.TryFindMaterialCenterAnchor(
                    pdfPath,
                    pageNumber,
                    blockWidth,
                    blockHeight,
                    pageWidth,
                    pageHeight,
                    leftBound,
                    rightBound,
                    bottomBound,
                    topBound,
                    new List<(float, float)>
                    {
                        (centerX, targetY),
                        (leftBound + ((centerX - leftBound) * 0.45f), targetY),
                        (rightBound - ((rightBound - centerX) * 0.35f), targetY),
                        (centerX, upperY),
                        (centerX, lowerY)
                    },
                    out var centerAnchorX,
                    out var anchorY)
                && !OverlapsTextWatermark(occupied, centerAnchorX, anchorY, blockWidth, blockHeight))
            {
                return new PdfPoint { X = centerAnchorX, Y = anchorY };
            }

            return FindBestDrawingCenterAnchorTextFallback(
                blockWidth,
                blockHeight,
                pageWidth,
                pageHeight,
                occupied,
                centerX,
                targetY,
                leftBound,
                rightBound,
                bottomBound,
                topBound);
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

        private static PdfPoint FindBestDrawingCenterAnchorTextFallback(
            float blockWidth,
            float blockHeight,
            float pageWidth,
            float pageHeight,
            List<PdfTextRect> occupied,
            float centerX,
            float targetY,
            float leftBound,
            float rightBound,
            float bottomBound,
            float topBound)
        {
            if (topBound - blockHeight < bottomBound)
                topBound = bottomBound + blockHeight;

            var step = Math.Max(14f, 16f * WatermarkPageScale.Get(pageWidth, pageHeight));
            var bestScore = float.MaxValue;
            var bestX = centerX;
            var bestY = targetY;
            var maxCenterX = Math.Max(leftBound + (blockWidth / 2f) + 8f, rightBound - (blockWidth / 2f) - 8f);

            for (var y = bottomBound; y <= topBound - blockHeight; y += step)
            {
                for (var x = leftBound + (blockWidth / 2f); x <= maxCenterX; x += step)
                {
                    var score = ScoreMaterialCenterPlacement(
                        x,
                        y,
                        blockWidth,
                        blockHeight,
                        occupied,
                        pageWidth,
                        pageHeight,
                        centerX,
                        targetY);

                    if (score < bestScore)
                    {
                        bestScore = score;
                        bestX = x;
                        bestY = y;
                    }
                }
            }

            return new PdfPoint { X = bestX, Y = bestY };
        }

        private static float ScoreMaterialCenterPlacement(
            float centerX,
            float anchorY,
            float width,
            float height,
            List<PdfTextRect> occupied,
            float pageWidth,
            float pageHeight,
            float targetCenterX,
            float targetAnchorY)
        {
            var score = OverlapScore(occupied, centerX, anchorY, width, height, 1);
            score += LocalTextDensity(centerX - (width / 2f), anchorY, width, height, occupied) * 180f;

            var dx = (centerX - targetCenterX) / Math.Max(100f, pageWidth * 0.25f);
            var dy = (anchorY - targetAnchorY) / Math.Max(80f, pageHeight * 0.20f);
            score += ((dx * dx) + (dy * dy)) * 900f;

            var titleBlockTop = GetTitleBlockTopY(pageHeight);
            if (anchorY < titleBlockTop + 8f)
                score += 2500000f;

            if (anchorY > titleBlockTop + 170f)
                score += 5000000f;

            var left = centerX - (width / 2f);
            var titleBlockLeft = GetTitleBlockLeftX(pageWidth);
            if (left + width > pageWidth - 28f)
                score += 350000f;

            if (left < 28f)
                score += 350000f;

            if (left > titleBlockLeft - 30f)
                score += 450000f;

            return score;
        }

        private static List<WatermarkTextPlacement> PlaceQuantityStack(
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

            var titleBlockTop = GetTitleBlockTopY(pageHeight);
            var titleBlockLeft = GetTitleBlockLeftX(pageWidth);
            var anchor = FindBestFreeAnchorInZones(
                pdfPath,
                pageNumber,
                blockWidth,
                blockHeight,
                pageWidth,
                pageHeight,
                occupied,
                preferFirstAcceptableZone: true,
                new[]
                {
                    // Rechts über Schriftfeld (Standard laut Arbeitsweise)
                    new AnchorSearchZone(titleBlockLeft + 5f, pageWidth - 35f, titleBlockTop + 8f, titleBlockTop + 170f, pageWidth - blockWidth - 50f, titleBlockTop + 95f),
                    // Mitte rechts
                    new AnchorSearchZone(Math.Max(titleBlockLeft - 80f, pageWidth * 0.45f), pageWidth - 35f, titleBlockTop + 8f, titleBlockTop + 170f, pageWidth * 0.68f, titleBlockTop + 92f),
                    // Nur Fallback links
                    new AnchorSearchZone(35f, Math.Max(140f, titleBlockLeft - 50f), titleBlockTop + 8f, titleBlockTop + 170f, 52f, titleBlockTop + 95f)
                });
            var topY = anchor.Y + blockHeight - lineHeight;
            var result = new List<WatermarkTextPlacement>();

            for (var i = 0; i < cleanLines.Count; i++)
            {
                result.Add(new WatermarkTextPlacement
                {
                    Text = cleanLines[i],
                    X = anchor.X,
                    Y = topY - (i * lineHeight),
                    Alignment = 0,
                    FontSize = fontSize
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
                    zoneTop: zone.Top,
                    preferredX: zone.PreferredX,
                    preferredY: zone.PreferredY);

                if (preferFirstAcceptableZone)
                    return candidate;
            }

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
                zones[0].Top,
                zones[0].PreferredX,
                zones[0].PreferredY);
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
            var targetX = preferredX ?? leftBound;
            var targetY = preferredY ?? (titleBlockTop + 65f);

            rightBound = Math.Min(rightBound, pageWidth - blockWidth - 20f);
            if (rightBound < leftBound)
                rightBound = leftBound;
            if (topBound < bottomBound)
                topBound = bottomBound;

            var bestScore = float.MaxValue;
            var bestX = targetX;
            var bestY = targetY;

            // Schnelle Text-Analyse: bevorzugte Position zuerst, dann grobes Raster (ohne PDF-Bildrendering).
            var preferredScore = ScorePlacement(targetX, targetY, blockWidth, blockHeight, 0, occupied, pageWidth, pageHeight);
            preferredScore += LocalTextDensity(targetX, targetY, blockWidth, blockHeight, occupied) * 120f;
            if (preferredScore < 50000f)
                return new PdfPoint { X = targetX, Y = targetY };

            for (var y = bottomBound; y <= topBound; y += 16f)
            {
                for (var x = leftBound; x <= rightBound; x += 16f)
                {
                    var score = ScorePlacement(x, y, blockWidth, blockHeight, 0, occupied, pageWidth, pageHeight);
                    score += LocalTextDensity(x, y, blockWidth, blockHeight, occupied) * 120f;
                    score += DistancePenalty(x, y, targetX, targetY, pageWidth, pageHeight);

                    if (targetX > pageWidth * 0.55f && x < pageWidth * 0.45f)
                        score += 500000f;

                    if (score < bestScore)
                    {
                        bestScore = score;
                        bestX = x;
                        bestY = y;
                    }
                }
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

        private static float EstimateTextWidth(string text, float fontSize)
        {
            if (string.IsNullOrEmpty(text))
                return 10f;

            return Math.Max(20f, text.Length * fontSize * 0.55f);
        }

        private static float FitFontSize(string text, float maxWidth, float maxSize, float minSize, float pageWidth, float pageHeight)
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
            if (!q.EndsWith("X", StringComparison.OrdinalIgnoreCase))
                q += "X";

            return q;
        }

        private static string NormalizeDate(string deliveryDate)
        {
            if (string.IsNullOrWhiteSpace(deliveryDate))
                return string.Empty;

            var date = deliveryDate.Trim();
            if (date.StartsWith("LT-", StringComparison.OrdinalIgnoreCase))
                return date;

            if (date.StartsWith("LT:", StringComparison.OrdinalIgnoreCase))
                date = date.Substring(3).TrimStart();

            return "LT-" + date;
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
