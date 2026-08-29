using System;
using System.Collections.Generic;
using System.Linq;
using PDF_umbenenen.Models;
using PDF_umbenenen.Services.Ai;

namespace PDF_umbenenen.Services
{
    internal static class OrderVerificationLookup
    {
        public const string DeliveryTerminPrefix = "LT-";
        public const string ManufacturingTerminPrefix = "FT-";
        public const string BgSubDrawingDeliveryPlaceholder = ManufacturingTerminPrefix;

        public static string FormatWatermarkTermin(string rawDate, bool isManufacturingTermin)
        {
            if (string.IsNullOrWhiteSpace(rawDate))
                return string.Empty;

            var text = rawDate.Trim();

            if (text.Equals(ManufacturingTerminPrefix, StringComparison.OrdinalIgnoreCase)
                || text.Equals(DeliveryTerminPrefix, StringComparison.OrdinalIgnoreCase))
            {
                return isManufacturingTermin ? ManufacturingTerminPrefix : DeliveryTerminPrefix;
            }

            // Vorhandenes FT-/LT- nur abstreifen – der gewünschte Typ kommt vom Aufrufer
            // (Baugruppe/freies ET → LT, BG-Unterzeichnung → FT), nicht vom Excel-Präfix.
            if (text.StartsWith(ManufacturingTerminPrefix, StringComparison.OrdinalIgnoreCase))
            {
                text = text.Substring(ManufacturingTerminPrefix.Length).TrimStart();
            }
            else if (text.StartsWith(DeliveryTerminPrefix, StringComparison.OrdinalIgnoreCase))
            {
                text = text.Substring(DeliveryTerminPrefix.Length).TrimStart();
            }
            else if (text.StartsWith("FT:", StringComparison.OrdinalIgnoreCase))
            {
                text = text.Substring(3).TrimStart();
            }
            else if (text.StartsWith("LT:", StringComparison.OrdinalIgnoreCase))
            {
                text = text.Substring(3).TrimStart();
            }

            var prefix = isManufacturingTermin ? ManufacturingTerminPrefix : DeliveryTerminPrefix;
            var normalizedDate = OrderVerificationDateHelper.TryNormalizeExcelDate(text);
            if (string.IsNullOrWhiteSpace(normalizedDate))
            {
                if (isManufacturingTermin)
                    return BgSubDrawingDeliveryPlaceholder;

                return string.Empty;
            }

            return prefix + normalizedDate;
        }

        public static bool IsBgSubDrawing(string parentArticleNumber)
        {
            return !string.IsNullOrWhiteSpace(parentArticleNumber);
        }

        public static bool IsBgSubDrawing(OrderExcelRow row)
        {
            return row != null && IsBgSubDrawing(row.ParentArticleNumber);
        }

        public static string NormalizeDrawingKey(string value)
        {
            return OrderVerificationDrawingKey.Normalize(value);
        }

        public static bool DrawingKeysEqual(string a, string b)
        {
            return OrderVerificationDrawingKey.Equal(a, b);
        }

        public static OrderVerificationLine FindMatchingLine(OrderExcelRow row, IList<OrderVerificationLine> pdfLines)
        {
            return FindBestMatchingLine(row, pdfLines, null);
        }

        public static OrderVerificationLine FindBestMatchingLine(
            OrderExcelRow row,
            IList<OrderVerificationLine> pdfLines,
            string drawingSearchKey)
        {
            if (pdfLines == null || pdfLines.Count == 0)
                return null;

            if (row != null)
            {
                var byDrawing = FindMatchingLineByDrawingKey(row.DrawingNumber, pdfLines);
                if (byDrawing != null)
                    return byDrawing;

                if (!string.IsNullOrWhiteSpace(row.SearchKey))
                {
                    byDrawing = FindMatchingLineByDrawingKey(row.SearchKey, pdfLines);
                    if (byDrawing != null)
                        return byDrawing;
                }

                if (row.ArticleNumbers != null)
                {
                    foreach (var article in row.ArticleNumbers)
                    {
                        byDrawing = FindMatchingLineByDrawingKey(article, pdfLines);
                        if (byDrawing != null)
                            return byDrawing;
                    }
                }

                if (string.IsNullOrWhiteSpace(row.DrawingNumber)
                    && row.ArticleNumbers != null
                    && !IsBgSubDrawing(row))
                {
                    foreach (var article in row.ArticleNumbers)
                    {
                        foreach (var pdfLine in pdfLines)
                        {
                            if (!string.IsNullOrWhiteSpace(pdfLine.ArticleNumber)
                                && KeysEqual(pdfLine.ArticleNumber, article))
                            {
                                return pdfLine;
                            }
                        }
                    }
                }
            }

            if (!string.IsNullOrWhiteSpace(drawingSearchKey))
                return FindMatchingLineByDrawingKey(drawingSearchKey, pdfLines);

            return null;
        }

        public static OrderVerificationLine FindMatchingLineByDrawingKey(string drawingSearchKey, IList<OrderVerificationLine> pdfLines)
        {
            if (string.IsNullOrWhiteSpace(drawingSearchKey) || pdfLines == null)
                return null;

            var searchKey = NormalizeDrawingKey(drawingSearchKey);
            if (string.IsNullOrWhiteSpace(searchKey))
                return null;

            foreach (var pdfLine in pdfLines)
            {
                if (DrawingKeysEqual(pdfLine.DrawingNumber, drawingSearchKey))
                    return pdfLine;

                if (!string.IsNullOrWhiteSpace(pdfLine.ArticleNumber)
                    && DrawingKeysEqual(pdfLine.ArticleNumber, drawingSearchKey))
                {
                    return pdfLine;
                }
            }

            return null;
        }

        public static string ResolveWatermarkDeliveryDate(OrderExcelRow row, OrderVerificationLine orderLine)
        {
            var isManufacturingTermin = IsBgSubDrawing(row);

            if (row != null && !string.IsNullOrWhiteSpace(row.DeliveryDate))
                return FormatWatermarkTermin(row.DeliveryDate, isManufacturingTermin);

            if (orderLine != null && !string.IsNullOrWhiteSpace(orderLine.DeliveryDate))
                return FormatWatermarkTermin(orderLine.DeliveryDate, isManufacturingTermin);

            if (isManufacturingTermin)
                return BgSubDrawingDeliveryPlaceholder;

            return string.Empty;
        }

        public static string ResolveWatermarkDeliveryDate(string parentArticleNumber, OrderVerificationLine orderLine, string excelDeliveryDate = null)
        {
            var isManufacturingTermin = IsBgSubDrawing(parentArticleNumber);

            if (!string.IsNullOrWhiteSpace(excelDeliveryDate))
                return FormatWatermarkTermin(excelDeliveryDate, isManufacturingTermin);

            if (orderLine != null && !string.IsNullOrWhiteSpace(orderLine.DeliveryDate))
                return FormatWatermarkTermin(orderLine.DeliveryDate, isManufacturingTermin);

            if (isManufacturingTermin)
                return BgSubDrawingDeliveryPlaceholder;

            return string.Empty;
        }

        public static bool MatchesRow(OrderExcelRow row, OrderVerificationLine pdfLine)
        {
            if (row == null || pdfLine == null)
                return false;

            if (!string.IsNullOrWhiteSpace(row.DrawingNumber)
                && DrawingKeysEqual(row.DrawingNumber, pdfLine.DrawingNumber))
            {
                return true;
            }

            if (!string.IsNullOrWhiteSpace(row.SearchKey)
                && DrawingKeysEqual(row.SearchKey, pdfLine.DrawingNumber))
            {
                return true;
            }

            if (row.ArticleNumbers != null)
            {
                foreach (var article in row.ArticleNumbers)
                {
                    if (DrawingKeysEqual(article, pdfLine.DrawingNumber)
                        || DrawingKeysEqual(article, pdfLine.ArticleNumber))
                    {
                        return true;
                    }
                }
            }

            if (!IsBgSubDrawing(row)
                && !string.IsNullOrWhiteSpace(pdfLine.ArticleNumber)
                && row.ArticleNumbers != null
                && row.ArticleNumbers.Any(a => KeysEqual(a, pdfLine.ArticleNumber)))
            {
                return true;
            }

            return false;
        }

        public static bool KeysEqual(string a, string b)
        {
            return string.Equals(
                AiTextHelper.NormalizeKey(a),
                AiTextHelper.NormalizeKey(b),
                StringComparison.OrdinalIgnoreCase);
        }
    }
}
