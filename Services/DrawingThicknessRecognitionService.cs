using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace PDF_umbenenen.Services
{
    /// <summary>
    /// Schnelle Texterkennung der Blechstärke: beschriftete Kante / Biegung.
    /// Keine Rahmenzahlen, keine ISO-2768-Toleranz (±2,0).
    /// </summary>
    internal static class DrawingThicknessRecognitionService
    {
        private static readonly Regex IsolatedNumberRegex = new Regex(
            @"^(\d{1,2}(?:[.,]\d{1,2})?)\s*(?:mm)?$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

        private static readonly Regex GridZoneNumberRegex = new Regex(
            @"^(?:[1-9]|1[0-9]|20)$",
            RegexOptions.CultureInvariant | RegexOptions.Compiled);

        private static readonly string[] UnrollingTokens =
        {
            "UNROLLING", "ABWICKLUNG", "ABWICKL", "FLAT PATTERN", "UNFOLD", "ENTFALTUNG"
        };

        public static bool TryRecognize(
            IList<PdfTextRect> rects,
            float pageWidth,
            float pageHeight,
            string materialRaw,
            out double thicknessMm)
        {
            thicknessMm = 0d;
            if (rects == null || rects.Count == 0 || pageWidth <= 0f || pageHeight <= 0f)
                return false;

            var unrolling = FindUnrollingBounds(rects, pageWidth, pageHeight);
            var dims = CollectDimensions(rects, unrolling, pageWidth, pageHeight, materialRaw);
            if (dims.Count == 0)
                return false;

            var picked = PickWallThickness(dims);
            if (!picked.HasValue)
                return false;

            thicknessMm = ThicknessCandidateResolver.SnapToStandardGauge(picked.Value);
            if (Math.Abs(thicknessMm - picked.Value) > 0.35d)
                thicknessMm = picked.Value;

            return thicknessMm >= 0.4d && thicknessMm <= ThicknessCandidateResolver.MaxSideViewThicknessMm;
        }

        private static PdfTextRect FindUnrollingBounds(
            IList<PdfTextRect> rects,
            float pageWidth,
            float pageHeight)
        {
            PdfTextRect label = null;
            foreach (var rect in rects)
            {
                if (rect == null || string.IsNullOrWhiteSpace(rect.Text))
                    continue;

                var text = rect.Text.Trim();
                if (!UnrollingTokens.Any(t => text.IndexOf(t, StringComparison.OrdinalIgnoreCase) >= 0))
                    continue;

                if (label == null || rect.Bottom > label.Bottom)
                    label = rect;
            }

            if (label == null)
                return null;

            var padX = pageWidth * 0.08f;
            var down = pageHeight * 0.22f;
            return new PdfTextRect
            {
                Left = Math.Max(0f, label.Left - padX),
                Right = Math.Min(pageWidth, label.Right + (pageWidth * 0.45f)),
                Bottom = Math.Max(pageHeight * 0.28f, label.Bottom - down),
                Top = Math.Min(pageHeight, label.Top + (pageHeight * 0.02f)),
                Text = label.Text
            };
        }

        private static List<(double Mm, float RelX, float RelY, bool NearBend)> CollectDimensions(
            IList<PdfTextRect> rects,
            PdfTextRect unrolling,
            float pageWidth,
            float pageHeight,
            string materialRaw)
        {
            var result = new List<(double Mm, float RelX, float RelY, bool NearBend)>();
            var searchBottom = pageHeight * 0.26f;
            var searchTop = pageHeight * 0.94f;
            var searchLeft = pageWidth * 0.05f;
            var searchRight = pageWidth * 0.94f;

            foreach (var rect in rects)
            {
                if (rect == null || string.IsNullOrWhiteSpace(rect.Text))
                    continue;

                var cx = (rect.Left + rect.Right) / 2f;
                var cy = (rect.Bottom + rect.Top) / 2f;
                if (cx < searchLeft || cx > searchRight || cy < searchBottom || cy > searchTop)
                    continue;

                if (IsInside(unrolling, cx, cy))
                    continue;

                var nearBend = IsNearBendMark(rects, rect, pageWidth, pageHeight);
                if (!nearBend && IsGridZoneNumber(rects, rect, pageWidth, pageHeight))
                    continue;

                var text = (rect.Text ?? string.Empty).Trim();
                if (text.StartsWith("R", StringComparison.OrdinalIgnoreCase)
                    || text.StartsWith("Ø", StringComparison.Ordinal)
                    || text.StartsWith("M", StringComparison.OrdinalIgnoreCase)
                    || text.IndexOf("x", StringComparison.OrdinalIgnoreCase) >= 0
                    || text.StartsWith("±", StringComparison.Ordinal)
                    || text.StartsWith("+", StringComparison.Ordinal))
                {
                    continue;
                }

                if (!IsolatedNumberRegex.IsMatch(text))
                    continue;

                var match = IsolatedNumberRegex.Match(text);
                if (!ThicknessCandidateResolver.TryParseThicknessMm(match.Groups[1].Value, out var mm))
                    continue;

                if (mm < 0.4d || mm > ThicknessCandidateResolver.MaxSideViewThicknessMm)
                    continue;

                if (ThicknessCandidateResolver.HasFractionalPart(mm)
                    && Math.Abs(mm - 2.0d) < 0.05d
                    && !nearBend)
                {
                    continue;
                }

                if (ThicknessCandidateResolver.IsLikelyHoleOrFeatureDimension(text, mm)
                    || ThicknessCandidateResolver.IsLikelyHoleDiameterMm(mm)
                    || ThicknessCandidateResolver.IsLikelyMaterialNumberFragment(text, mm))
                {
                    continue;
                }

                result.Add((mm, cx / pageWidth, cy / pageHeight, nearBend));
            }

            return result;
        }

        private static double? PickWallThickness(List<(double Mm, float RelX, float RelY, bool NearBend)> dims)
        {
            if (dims == null || dims.Count == 0)
                return null;

            var typical = dims
                .Where(d => ThicknessCandidateResolver.IsTypicalSheetThickness(d.Mm)
                            && ThicknessCandidateResolver.IsStandardSheetGauge(d.Mm, 0.18d))
                .ToList();

            if (typical.Count > 0)
            {
                var nearBend = typical.Where(d => d.NearBend).ToList();
                if (nearBend.Count > 0)
                    return nearBend.OrderBy(d => d.Mm).First().Mm;

                var rightSide = typical.Where(d => d.RelX >= 0.55f).ToList();
                var pool = rightSide.Count > 0 ? rightSide : typical;
                return pool
                    .OrderByDescending(d => d.RelX)
                    .ThenBy(d => d.Mm)
                    .ThenByDescending(d => d.RelY)
                    .First()
                    .Mm;
            }

            // 8–20 mm in der Draufsicht ist ein Schenkel-/Breitenmaß, keine Blechdicke.
            var rightPlates = dims
                .Where(d => ThicknessCandidateResolver.IsPlausiblePlateThicknessMm(d.Mm) && d.RelX >= 0.58f)
                .ToList();

            if (rightPlates.Count == 1)
                return rightPlates[0].Mm;

            if (rightPlates.Count > 1)
            {
                var nearBendPlate = rightPlates.Where(d => d.NearBend).ToList();
                if (nearBendPlate.Count > 0)
                    return nearBendPlate.OrderBy(d => d.Mm).First().Mm;

                return rightPlates
                    .OrderByDescending(d => d.RelX)
                    .ThenBy(d => d.Mm)
                    .First()
                    .Mm;
            }

            return null;
        }

        private static bool IsInside(PdfTextRect box, float x, float y)
        {
            if (box == null)
                return false;

            return x >= box.Left && x <= box.Right && y >= box.Bottom && y <= box.Top;
        }

        private static bool IsGridZoneNumber(
            IList<PdfTextRect> allRects,
            PdfTextRect target,
            float pageWidth,
            float pageHeight)
        {
            if (target == null || allRects == null)
                return false;

            var text = (target.Text ?? string.Empty).Trim();
            if (!GridZoneNumberRegex.IsMatch(text))
                return false;

            var targetY = (target.Bottom + target.Top) / 2f;
            var targetX = (target.Left + target.Right) / 2f;
            var bandY = Math.Max(10f, pageHeight * 0.02f);
            var bandX = Math.Max(10f, pageWidth * 0.02f);
            var count = 0;
            foreach (var rect in allRects)
            {
                if (rect == null || ReferenceEquals(rect, target))
                    continue;

                var other = (rect.Text ?? string.Empty).Trim();
                if (!GridZoneNumberRegex.IsMatch(other))
                    continue;

                var y = (rect.Bottom + rect.Top) / 2f;
                var x = (rect.Left + rect.Right) / 2f;
                var sameRow = Math.Abs(y - targetY) <= bandY && Math.Abs(x - targetX) > 8f;
                var sameCol = Math.Abs(x - targetX) <= bandX && Math.Abs(y - targetY) > 8f;
                if (sameRow || sameCol)
                    count++;
            }

            return count >= 2;
        }

        private static bool IsNearBendMark(
            IList<PdfTextRect> rects,
            PdfTextRect target,
            float pageWidth,
            float pageHeight)
        {
            if (rects == null || target == null)
                return false;

            var tx = (target.Left + target.Right) / 2f;
            var ty = (target.Bottom + target.Top) / 2f;
            var radiusX = Math.Max(36f, pageWidth * 0.10f);
            var radiusY = Math.Max(28f, pageHeight * 0.07f);

            foreach (var rect in rects)
            {
                if (rect == null || ReferenceEquals(rect, target))
                    continue;

                var text = (rect.Text ?? string.Empty).Trim();
                if (text.IndexOf('°') < 0
                    && text.IndexOf("165", StringComparison.Ordinal) < 0
                    && !string.Equals(text, "25,4", StringComparison.Ordinal)
                    && !string.Equals(text, "25.4", StringComparison.Ordinal))
                {
                    continue;
                }

                var cx = (rect.Left + rect.Right) / 2f;
                var cy = (rect.Bottom + rect.Top) / 2f;
                if (Math.Abs(cx - tx) <= radiusX && Math.Abs(cy - ty) <= radiusY)
                    return true;
            }

            return false;
        }
    }
}
