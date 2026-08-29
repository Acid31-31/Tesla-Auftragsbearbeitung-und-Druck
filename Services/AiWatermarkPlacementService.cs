using System;
using System.Collections.Generic;
using System.Linq;
using PDF_umbenenen.Services.Ai;

namespace PDF_umbenenen.Services
{
    /// <summary>
    /// KI-Vision (lokales Seitenraster): setzt Wasserzeichen nur in freie Randbereiche —
    /// nie auf Zeichnung oder Schriftfeld.
    /// </summary>
    internal static class AiWatermarkPlacementService
    {
        public static bool ShouldEnforceRasterLayout => PdfRasterLayoutService.IsAvailable;

        public static bool IsEnabled
        {
            get
            {
                var settings = AiSettingsStore.Load();
                return settings.AiEnabled
                    && settings.LocalVisionEnabled
                    && PdfRasterLayoutService.IsAvailable;
            }
        }

        public static void EnforceVisionLayout(
            IList<WatermarkTextPlacement> placements,
            string pdfPath,
            int pageNumber,
            float pageWidth,
            float pageHeight,
            IList<PdfTextRect> occupiedRects)
        {
            if (!ShouldEnforceRasterLayout || placements == null || placements.Count == 0)
                return;

            if (string.IsNullOrWhiteSpace(pdfPath) || pageWidth <= 0 || pageHeight <= 0)
                return;

            PdfRasterLayoutService.EnsurePageGrid(pdfPath, pageNumber, pageWidth, pageHeight);

            var occupied = (occupiedRects ?? Array.Empty<PdfTextRect>())
                .Select(r => InflateRect(r, 8f))
                .ToList();

            foreach (var placement in placements)
            {
                if (placement != null && !string.IsNullOrWhiteSpace(placement.Text))
                    occupied.Add(MakePlacementRect(placement, pageWidth));
            }

            foreach (var placement in placements.ToList())
            {
                if (placement == null || string.IsNullOrWhiteSpace(placement.Category))
                    continue;

                if (IsFixedCategory(placement.Category))
                    continue;

                if (string.Equals(placement.Category, "Material", StringComparison.OrdinalIgnoreCase))
                    continue;

                if (string.Equals(placement.Category, "PowderCoating", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(placement.Category, "Anodizing", StringComparison.OrdinalIgnoreCase))
                    continue;

                // Feste BG rechts senkrecht (−90°) nicht verschieben.
                if (string.Equals(placement.Category, "Bg", StringComparison.OrdinalIgnoreCase)
                    && Math.Abs(placement.Angle + 90f) < 0.5f)
                    continue;

                var fontSize = placement.FontSize > 0f ? placement.FontSize : 22f;
                var blockWidth = EstimateTextWidth(placement.Text, fontSize);
                var blockHeight = fontSize + 8f;

                if (IsPlacementGood(pdfPath, pageNumber, placement, blockWidth, blockHeight, pageWidth, pageHeight, occupied, isMaterial: false))
                    continue;

                if (!TryReposition(
                        placement,
                        pdfPath,
                        pageNumber,
                        blockWidth,
                        blockHeight,
                        pageWidth,
                        pageHeight,
                        occupied,
                        isMaterial: false,
                        materialLineCount: 1,
                        fontSize,
                        placements))
                {
                    continue;
                }

                occupied.Add(MakePlacementRect(placement, pageWidth));
            }
        }

        private static void SyncMaterialStackPositions(IList<WatermarkTextPlacement> placements, float fontSize)
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

        private static bool IsFixedCategory(string category)
        {
            return string.Equals(category, "Order", StringComparison.OrdinalIgnoreCase)
                   || string.Equals(category, "Location", StringComparison.OrdinalIgnoreCase)
                   || string.Equals(category, "Quantity", StringComparison.OrdinalIgnoreCase)
                   || string.Equals(category, "Date", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsPlacementGood(
            string pdfPath,
            int pageNumber,
            WatermarkTextPlacement placement,
            float blockWidth,
            float blockHeight,
            float pageWidth,
            float pageHeight,
            IList<PdfTextRect> occupied,
            bool isMaterial)
        {
            if (PdfRasterLayoutService.IsOverlappingTitleBlock(
                    placement.X,
                    placement.Y,
                    blockWidth,
                    blockHeight,
                    pageWidth,
                    pageHeight,
                    placement.Alignment,
                    materialYIsBottomAnchor: false))
            {
                return false;
            }

            if (PdfRasterLayoutService.IsInsideDrawingCore(
                    placement.X,
                    placement.Y,
                    blockWidth,
                    blockHeight,
                    pageWidth,
                    pageHeight,
                    placement.Alignment,
                    materialYIsBottomAnchor: false))
            {
                return false;
            }

            if (OverlapsOccupied(occupied, placement, blockWidth, blockHeight, pageWidth))
                return false;

            return PdfRasterLayoutService.IsPlacementAcceptable(
                pdfPath,
                pageNumber,
                placement.X,
                placement.Y,
                blockWidth,
                blockHeight,
                pageWidth,
                pageHeight,
                placement.Alignment,
                isMaterial,
                materialYIsBottomAnchor: false);
        }

        private static bool TryReposition(
            WatermarkTextPlacement placement,
            string pdfPath,
            int pageNumber,
            float blockWidth,
            float blockHeight,
            float pageWidth,
            float pageHeight,
            IList<PdfTextRect> occupied,
            bool isMaterial,
            int materialLineCount,
            float fontSize,
            IList<WatermarkTextPlacement> placements)
        {
            var isLocation = string.Equals(placement.Category, "Location", StringComparison.OrdinalIgnoreCase);

            if (isMaterial
                && PdfRasterLayoutService.TryFindVisionMarginMaterialAnchor(
                    pdfPath,
                    pageNumber,
                    blockWidth,
                    blockHeight,
                    pageWidth,
                    pageHeight,
                    out var materialCenterX,
                    out var materialAnchorBottomY))
            {
                var lineHeight = PdfRasterLayoutService.GetMaterialLineHeight(fontSize);
                placement.X = materialCenterX;
                placement.Y = materialAnchorBottomY + blockHeight - lineHeight;
                placement.Alignment = 1;
                return true;
            }

            var zones = GetVisionZones(placement.Category, pageWidth, pageHeight, blockWidth);
            foreach (var zone in zones)
            {
                if (PdfRasterLayoutService.TryFindFreeCenterAnchor(
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
                        zone.PreferredX,
                        zone.PreferredY,
                        out var centerX,
                        out var anchorY)
                    && !PdfRasterLayoutService.IsInsideDrawingCore(centerX, anchorY, blockWidth, blockHeight, pageWidth, pageHeight, 1, materialYIsBottomAnchor: isMaterial)
                    && !PdfRasterLayoutService.IsOverlappingTitleBlock(centerX, anchorY, blockWidth, blockHeight, pageWidth, pageHeight, 1, materialYIsBottomAnchor: isMaterial)
                    && !OverlapsOccupiedPoint(occupied, centerX, anchorY, blockWidth, blockHeight)
                    && PdfRasterLayoutService.IsPlacementAcceptable(
                        pdfPath,
                        pageNumber,
                        centerX,
                        anchorY,
                        blockWidth,
                        blockHeight,
                        pageWidth,
                        pageHeight,
                        1,
                        isMaterial,
                        materialYIsBottomAnchor: isMaterial))
                {
                    placement.X = centerX;
                    placement.Y = isMaterial
                        ? anchorY + blockHeight - PdfRasterLayoutService.GetMaterialLineHeight(fontSize)
                        : anchorY;
                    placement.Alignment = isLocation || isMaterial ? 1 : placement.Alignment;
                    return true;
                }
            }

            return false;
        }

        private static VisionZone[] GetVisionZones(string category, float pageWidth, float pageHeight, float blockWidth)
        {
            var titleTop = pageHeight * 0.22f;
            var titleLeft = pageWidth * 0.58f;
            var margin = 32f;

            if (string.Equals(category, "Material", StringComparison.OrdinalIgnoreCase))
            {
                return new[]
                {
                    new VisionZone(margin, Math.Min(pageWidth * 0.14f, titleLeft - 36f), margin, pageHeight * 0.82f, margin + 28f, pageHeight * 0.45f),
                    new VisionZone(margin, titleLeft - 28f, margin, Math.Min(titleTop - 12f, pageHeight * 0.40f), pageWidth * 0.12f, titleTop * 0.55f)
                };
            }

            if (string.Equals(category, "Location", StringComparison.OrdinalIgnoreCase))
            {
                return new[]
                {
                    new VisionZone(pageWidth * 0.22f, pageWidth * 0.78f, pageHeight * 0.84f, pageHeight - margin, pageWidth / 2f, pageHeight * 0.92f)
                };
            }

            if (string.Equals(category, "Bg", StringComparison.OrdinalIgnoreCase))
            {
                return new[]
                {
                    new VisionZone(margin, titleLeft - 28f, margin, titleTop + 12f, margin + 20f, titleTop + 48f),
                    new VisionZone(margin, pageWidth * 0.22f, margin, titleTop * 0.55f, margin + 24f, titleTop * 0.35f)
                };
            }

            if (string.Equals(category, "Date", StringComparison.OrdinalIgnoreCase)
                || string.Equals(category, "Quantity", StringComparison.OrdinalIgnoreCase))
            {
                return new[]
                {
                    new VisionZone(margin, pageWidth * 0.42f, pageHeight * 0.72f, pageHeight - margin, margin + 16f, pageHeight * 0.88f),
                    new VisionZone(margin, titleLeft - 24f, titleTop + 8f, titleTop + 150f, titleLeft - blockWidth - 40f, titleTop + 90f)
                };
            }

            return new[]
            {
                new VisionZone(margin, titleLeft - 24f, margin, pageHeight * 0.80f, pageWidth * 0.14f, pageHeight * 0.40f)
            };
        }

        internal readonly struct VisionZone
        {
            public VisionZone(float left, float right, float bottom, float top, float preferredX, float preferredY)
            {
                Left = left;
                Right = right;
                Bottom = bottom;
                Top = top;
                PreferredX = preferredX;
                PreferredY = preferredY;
            }

            public float Left { get; }
            public float Right { get; }
            public float Bottom { get; }
            public float Top { get; }
            public float PreferredX { get; }
            public float PreferredY { get; }
        }

        private static float EstimateTextWidth(string text, float fontSize)
        {
            var len = string.IsNullOrWhiteSpace(text) ? 4 : text.Trim().Length;
            return Math.Max(48f, len * fontSize * 0.56f);
        }

        private static PdfTextRect InflateRect(PdfTextRect rect, float padding)
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

        private static PdfTextRect MakePlacementRect(WatermarkTextPlacement placement, float pageWidth)
        {
            var width = EstimateTextWidth(placement.Text, placement.FontSize);
            var height = placement.FontSize + 8f;
            float left;
            float right;

            if (placement.Alignment == 1)
            {
                left = placement.X - (width / 2f);
                right = placement.X + (width / 2f);
            }
            else if (placement.Alignment == 2)
            {
                right = placement.X;
                left = placement.X - width;
            }
            else
            {
                left = placement.X;
                right = placement.X + width;
            }

            return new PdfTextRect
            {
                Text = placement.Text,
                Left = left,
                Right = right,
                Bottom = placement.Y - 4f,
                Top = placement.Y + height
            };
        }

        private static bool OverlapsOccupied(
            IList<PdfTextRect> occupied,
            WatermarkTextPlacement placement,
            float blockWidth,
            float blockHeight,
            float pageWidth)
        {
            var rect = MakePlacementRect(placement, pageWidth);
            return occupied.Any(o => Intersects(rect, o));
        }

        private static bool OverlapsOccupiedPoint(
            IList<PdfTextRect> occupied,
            float centerX,
            float anchorY,
            float blockWidth,
            float blockHeight)
        {
            var rect = new PdfTextRect
            {
                Left = centerX - (blockWidth / 2f),
                Right = centerX + (blockWidth / 2f),
                Bottom = anchorY - 4f,
                Top = anchorY + blockHeight
            };

            return occupied.Any(o => Intersects(rect, o));
        }

        private static bool Intersects(PdfTextRect a, PdfTextRect b)
        {
            if (a == null || b == null)
                return false;

            return a.Left < b.Right && a.Right > b.Left && a.Bottom < b.Top && a.Top > b.Bottom;
        }
    }
}
