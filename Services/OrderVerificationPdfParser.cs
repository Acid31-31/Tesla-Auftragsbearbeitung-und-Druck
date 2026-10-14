using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using PDF_umbenenen.Models;
using PDF_umbenenen.Services.Ai;

namespace PDF_umbenenen.Services
{
    internal sealed class OrderVerificationPdfParser
    {
        private static readonly Regex ArticleRegex = new Regex(@"\bTGA\d+\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex DrawingRegex = new Regex(
            @"\b(?:[A-Z]{2,6}-)?\d{4,6}-\d{2,6}(?:-[A-Z0-9]+)?\b",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex TeslaPartRegex = new Regex(
            @"\b(?:[A-Z]{2,6}-)?\d{4,6}-\d{2,6}(?:-[A-Z0-9]+)?\b",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex TableQtyRegex = new Regex(
            @"\b(\d+(?:[.,]\d+)?)\s*EA\b",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex TableRowRegex = new Regex(
            @"\bTGA\d+\b.*?\b(\d+(?:[.,]\d+)?)\s*EA\b.*?\b(20\d{2}-(?:0[1-9]|1[0-2])-(?:0[1-9]|[12]\d|3[01]))\b",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex EaWithIsoDateRegex = new Regex(
            @"\b(\d+(?:[.,]\d+)?)\s*EA\b[^\r\n]{0,40}?\b(20\d{2}-(?:0[1-9]|1[0-2])-(?:0[1-9]|[12]\d|3[01]))\b",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex AmountRegex = new Regex(
            @"(?:Menge|Qty|Quantity|Amount|Stück|Stk\.?)\s*[:\-]?\s*(\d{1,5})",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        public IReadOnlyList<OrderVerificationLine> Parse(string pdfPath, PdfTextExtractionService textService)
        {
            if (string.IsNullOrWhiteSpace(pdfPath) || textService == null)
                return Array.Empty<OrderVerificationLine>();

            var text = textService.ExtractText(pdfPath, 100);
            if (string.IsNullOrWhiteSpace(text))
                return Array.Empty<OrderVerificationLine>();

            var lines = text
                .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(line => line.Trim())
                .Where(line => line.Length > 0)
                .ToList();

            var results = new List<OrderVerificationLine>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            for (var i = 0; i < lines.Count; i++)
            {
                var line = lines[i];
                if (IsHeaderNoise(line))
                    continue;

                var rowMatch = TableRowRegex.Match(line);
                if (rowMatch.Success)
                {
                    TryAddFromLine(
                        results,
                        seen,
                        line,
                        OrderVerificationDateHelper.NormalizeToEu(rowMatch.Groups[2].Value),
                        NormalizeTableQty(rowMatch.Groups[1].Value));
                    continue;
                }

                var articles = ArticleRegex.Matches(line)
                    .Cast<Match>()
                    .Select(match => match.Value.ToUpperInvariant())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                var drawings = CollectDrawings(line);

                if (articles.Count == 0 && drawings.Count == 0)
                    continue;

                // Nur vorwärts bis zur nächsten TGA-Zeile – sonst greift Due Date der vorherigen Position.
                var date = ExtractDueDateForLineItem(lines, i);
                var rowContext = BuildContextBlock(lines, i, Math.Min(8, lines.Count - i));
                var amount = OrderVerificationAmountHelper.ExtractQuantity(rowContext)
                    ?? ExtractTableQuantity(line)
                    ?? ExtractNearestTableQuantity(lines, i, articles.FirstOrDefault(), drawings.FirstOrDefault())
                    ?? ExtractAmount(line)
                    ?? ExtractAmount(i + 1 < lines.Count ? lines[i + 1] : null);

                if (articles.Count == 0)
                {
                    foreach (var drawing in drawings)
                        AddLine(results, seen, null, drawing, date, amount, line);
                    continue;
                }

                foreach (var article in articles)
                {
                    var drawing = PickDrawingForArticle(drawings, article);
                    AddLine(results, seen, article, drawing, date, amount, line);
                }
            }

            EnrichMissingDates(results, lines);
            EnrichMissingAmounts(results, lines);

            return results;
        }

        private static void EnrichMissingAmounts(IList<OrderVerificationLine> results, IList<string> lines)
        {
            if (results == null || results.Count == 0 || lines == null || lines.Count == 0)
                return;

            var byArticle = new Dictionary<string, OrderVerificationLine>(StringComparer.OrdinalIgnoreCase);
            var byDrawing = new Dictionary<string, OrderVerificationLine>(StringComparer.OrdinalIgnoreCase);

            foreach (var result in results)
            {
                if (!string.IsNullOrWhiteSpace(result.ArticleNumber))
                    byArticle[result.ArticleNumber.ToUpperInvariant()] = result;

                if (!string.IsNullOrWhiteSpace(result.DrawingNumber))
                {
                    var drawKey = OrderVerificationDrawingKey.Normalize(result.DrawingNumber);
                    if (!string.IsNullOrWhiteSpace(drawKey))
                        byDrawing[drawKey] = result;
                }
            }

            for (var i = 0; i < lines.Count; i++)
            {
                var line = lines[i];
                var amount = OrderVerificationAmountHelper.ExtractQuantity(line);
                if (string.IsNullOrWhiteSpace(amount))
                    continue;

                var contextStart = Math.Max(0, i - 4);
                var context = BuildContextBlock(lines, contextStart, Math.Min(10, lines.Count - contextStart));
                if (!ArticleRegex.IsMatch(context) && !DrawingRegex.IsMatch(context))
                    continue;

                foreach (Match match in ArticleRegex.Matches(context))
                {
                    OrderVerificationLine target;
                    if (byArticle.TryGetValue(match.Value.ToUpperInvariant(), out target)
                        && string.IsNullOrWhiteSpace(target.Amount))
                    {
                        target.Amount = amount;
                    }
                }

                foreach (Match match in DrawingRegex.Matches(context))
                {
                    var drawKey = OrderVerificationDrawingKey.Normalize(match.Value);
                    if (string.IsNullOrWhiteSpace(drawKey))
                        continue;

                    OrderVerificationLine target;
                    if (byDrawing.TryGetValue(drawKey, out target)
                        && string.IsNullOrWhiteSpace(target.Amount))
                    {
                        target.Amount = amount;
                    }
                }
            }
        }

        private static void EnrichMissingDates(IList<OrderVerificationLine> results, IList<string> lines)
        {
            if (results == null || results.Count == 0 || lines == null || lines.Count == 0)
                return;

            var byArticle = new Dictionary<string, OrderVerificationLine>(StringComparer.OrdinalIgnoreCase);
            var byDrawing = new Dictionary<string, OrderVerificationLine>(StringComparer.OrdinalIgnoreCase);

            foreach (var result in results)
            {
                if (!string.IsNullOrWhiteSpace(result.ArticleNumber))
                    byArticle[result.ArticleNumber.ToUpperInvariant()] = result;

                if (!string.IsNullOrWhiteSpace(result.DrawingNumber))
                {
                    var drawKey = OrderVerificationDrawingKey.Normalize(result.DrawingNumber);
                    if (!string.IsNullOrWhiteSpace(drawKey))
                        byDrawing[drawKey] = result;
                }
            }

            for (var i = 0; i < lines.Count; i++)
            {
                var line = lines[i];
                var date = OrderVerificationDateHelper.ExtractDueDate(line);
                if (string.IsNullOrWhiteSpace(date))
                    continue;

                if (ArticleRegex.IsMatch(line))
                    continue;

                for (var j = i - 1; j >= Math.Max(0, i - 25); j--)
                {
                    var backLine = lines[j];
                    var articles = ArticleRegex.Matches(backLine)
                        .Cast<Match>()
                        .Select(m => m.Value.ToUpperInvariant());

                    foreach (var article in articles)
                    {
                        OrderVerificationLine target;
                        if (byArticle.TryGetValue(article, out target)
                            && string.IsNullOrWhiteSpace(target.DeliveryDate))
                        {
                            target.DeliveryDate = date;
                            goto NextDateLine;
                        }
                    }

                    var drawings = CollectDrawings(backLine);

                    foreach (var drawing in drawings)
                    {
                        var drawKey = OrderVerificationDrawingKey.Normalize(drawing);
                        if (string.IsNullOrWhiteSpace(drawKey))
                            continue;

                        OrderVerificationLine target;
                        if (byDrawing.TryGetValue(drawKey, out target)
                            && string.IsNullOrWhiteSpace(target.DeliveryDate))
                        {
                            target.DeliveryDate = date;
                            goto NextDateLine;
                        }
                    }
                }

                NextDateLine: ;
            }
        }

        private static void TryAddFromLine(
            ICollection<OrderVerificationLine> results,
            ISet<string> seen,
            string line,
            string date,
            string amount)
        {
            var articles = ArticleRegex.Matches(line)
                .Cast<Match>()
                .Select(match => match.Value.ToUpperInvariant())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            var drawings = CollectDrawings(line);

            foreach (var article in articles)
            {
                AddLine(results, seen, article, PickDrawingForArticle(drawings, article), date, amount, line);
            }
        }

        private static List<string> CollectDrawings(string line)
        {
            var drawings = new List<string>();
            if (string.IsNullOrWhiteSpace(line))
                return drawings;

            foreach (Match match in TeslaPartRegex.Matches(line))
                AddUniqueDrawing(drawings, match.Value.ToUpperInvariant());

            foreach (Match match in DrawingRegex.Matches(line))
                AddUniqueDrawing(drawings, match.Value.ToUpperInvariant());

            return drawings;
        }

        private static void AddUniqueDrawing(ICollection<string> drawings, string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return;

            if (!drawings.Any(d => string.Equals(d, value, StringComparison.OrdinalIgnoreCase)))
                drawings.Add(value);
        }

        private static string PickDrawingForArticle(IList<string> drawings, string article)
        {
            if (drawings == null || drawings.Count == 0)
                return null;

            if (drawings.Count == 1)
                return drawings[0];

            if (!string.IsNullOrWhiteSpace(article))
            {
                foreach (var drawing in drawings)
                {
                    if (OrderVerificationDrawingKey.Equal(drawing, article))
                        return drawing;
                }
            }

            var tslDrawing = drawings.FirstOrDefault(d => TeslaPartRegex.IsMatch(d));
            return tslDrawing ?? drawings[0];
        }

        private static string ExtractNearestTableQuantity(IList<string> lines, int lineIndex, string article, string drawing)
        {
            for (var delta = 0; delta <= 8; delta++)
            {
                foreach (var idx in new[] { lineIndex - delta, lineIndex + delta })
                {
                    if (idx < 0 || idx >= lines.Count)
                        continue;

                    var text = lines[idx];
                    var qty = OrderVerificationAmountHelper.ExtractQuantity(text)
                              ?? ExtractTableQuantity(text);
                    if (string.IsNullOrEmpty(qty))
                        continue;

                    if (!string.IsNullOrEmpty(article)
                        && text.IndexOf(article, StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        return qty;
                    }

                    if (!string.IsNullOrEmpty(drawing)
                        && text.IndexOf(drawing, StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        return qty;
                    }
                }
            }

            return string.Empty;
        }

        private static string BuildContextBlock(IList<string> lines, int startIndex, int lineCount)
        {
            var end = Math.Min(lines.Count, startIndex + lineCount);
            return string.Join(" ", lines.Skip(startIndex).Take(end - startIndex));
        }

        /// <summary>
        /// Due Date nur vorwärts ab der Positionszeile bis zur nächsten TGA-Zeile.
        /// Lookback greift sonst das Due Date der vorherigen Bestellposition.
        /// </summary>
        private static string ExtractDueDateForLineItem(IList<string> lines, int lineIndex)
        {
            if (lines == null || lineIndex < 0 || lineIndex >= lines.Count)
                return string.Empty;

            for (var i = lineIndex; i < lines.Count; i++)
            {
                if (i > lineIndex && ArticleRegex.IsMatch(lines[i]))
                    break;

                var eaDate = EaWithIsoDateRegex.Match(lines[i]);
                if (eaDate.Success)
                    return OrderVerificationDateHelper.NormalizeToEu(eaDate.Groups[2].Value);

                // In derselben Zeile wie TGA/Zeichnung: TableRow-Muster
                var rowMatch = TableRowRegex.Match(lines[i]);
                if (rowMatch.Success)
                    return OrderVerificationDateHelper.NormalizeToEu(rowMatch.Groups[2].Value);

                // Reine Datumszeile direkt hinter Qty/EA (ohne TGA auf der Zeile)
                if (i > lineIndex)
                {
                    var dateOnly = OrderVerificationDateHelper.ExtractDueDate(lines[i]);
                    if (!string.IsNullOrWhiteSpace(dateOnly)
                        && (TableQtyRegex.IsMatch(lines[i]) || TableQtyRegex.IsMatch(lines[i - 1])))
                    {
                        return dateOnly;
                    }
                }
            }

            return string.Empty;
        }

        private static void AddLine(
            ICollection<OrderVerificationLine> results,
            ISet<string> seen,
            string article,
            string drawing,
            string date,
            string amount,
            string sourceLine)
        {
            var key = !string.IsNullOrWhiteSpace(drawing)
                ? "D:" + OrderVerificationDrawingKey.Normalize(drawing)
                : (!string.IsNullOrWhiteSpace(article)
                    ? "A:" + article.ToUpperInvariant()
                    : string.Empty);

            if (string.IsNullOrWhiteSpace(key) || key.Length <= 2 || !seen.Add(key))
                return;

            results.Add(new OrderVerificationLine
            {
                ArticleNumber = article,
                DrawingNumber = drawing,
                DeliveryDate = OrderVerificationDateHelper.NormalizeToEu(date),
                Amount = OrderVerificationAmountHelper.NormalizeQuantity(amount),
                SourceHint = Truncate(sourceLine, 80)
            });
        }

        private static bool IsHeaderNoise(string line)
        {
            var normalized = AiTextHelper.NormalizeKey(line);
            if (normalized.Length < 4)
                return true;

            if (normalized.Contains("DUEDATE") || normalized.Contains("LIEFERTERMIN"))
                return true;

            return normalized.Contains("PAGENUMBER")
                || normalized.Contains("SEITE")
                || normalized.Contains("TOTAL")
                || normalized.Contains("SUMME")
                || normalized.Contains("UNITPRICE")
                || normalized.Contains("TESLAPARTNUMBER");
        }

        private static string ExtractTableQuantity(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return string.Empty;

            var match = TableQtyRegex.Match(text);
            if (!match.Success)
                return string.Empty;

            return NormalizeTableQty(match.Groups[1].Value);
        }

        private static string ExtractAmount(string line)
        {
            if (string.IsNullOrWhiteSpace(line))
                return string.Empty;

            var labeled = AmountRegex.Match(line);
            if (labeled.Success)
                return labeled.Groups[1].Value;

            return string.Empty;
        }

        private static string NormalizeTableQty(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
                return string.Empty;

            var text = raw.Trim().Replace(',', '.');
            if (double.TryParse(text, NumberStyles.Any, CultureInfo.InvariantCulture, out var value))
                return ((int)Math.Round(value)).ToString(CultureInfo.InvariantCulture);

            return OrderVerificationAmountHelper.NormalizeQuantity(raw);
        }

        private static string NormalizeAmount(string raw)
        {
            return OrderVerificationAmountHelper.NormalizeQuantity(raw);
        }

        private static string Truncate(string value, int maxLength)
        {
            if (string.IsNullOrEmpty(value))
                return string.Empty;
            return value.Length <= maxLength ? value : value.Substring(0, maxLength) + "…";
        }
    }
}

