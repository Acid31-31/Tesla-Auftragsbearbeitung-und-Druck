using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using PDF_umbenenen.Models;
using PDF_umbenenen.Services.Ai;

namespace PDF_umbenenen.Services
{
    internal static class CustomerOrderListParser
    {
        public static IList<OrderVerificationLine> Parse(
            string pdfPath,
            CustomerProfile profile,
            PdfTextExtractionService textService)
        {
            if (string.IsNullOrWhiteSpace(pdfPath) || profile == null || textService == null)
                return Array.Empty<OrderVerificationLine>();

            var text = textService.ExtractText(pdfPath, 120);
            if (string.IsNullOrWhiteSpace(text))
                return Array.Empty<OrderVerificationLine>();

            Regex drawingRegex;
            try
            {
                drawingRegex = new Regex(
                    profile.DrawingNumberPattern,
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);
            }
            catch
            {
                drawingRegex = new Regex(CustomerProfile.DefaultDrawingPattern, RegexOptions.IgnoreCase | RegexOptions.Compiled);
            }

            Regex qtyRegex;
            try
            {
                qtyRegex = new Regex(
                    string.IsNullOrWhiteSpace(profile.QuantityPattern)
                        ? @"\b(\d+(?:[.,]\d+)?)\s*(?:EA|Stk\.?|PCS|Stück)?\b"
                        : profile.QuantityPattern,
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);
            }
            catch
            {
                qtyRegex = new Regex(@"\b(\d+(?:[.,]\d+)?)\s*(?:EA|Stk\.?|PCS|Stück)?\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
            }

            var lines = text
                .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(l => l.Trim())
                .Where(l => l.Length > 0)
                .ToList();

            var results = new List<OrderVerificationLine>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            for (var i = 0; i < lines.Count; i++)
            {
                var line = lines[i];
                var drawingMatch = drawingRegex.Match(line);
                if (!drawingMatch.Success)
                    continue;

                var drawing = drawingMatch.Value.ToUpperInvariant();
                var key = OrderVerificationDrawingKey.Normalize(drawing);
                if (string.IsNullOrWhiteSpace(key) || !seen.Add(key))
                    continue;

                var qtyMatch = qtyRegex.Match(line);
                var amount = qtyMatch.Success ? NormalizeQty(qtyMatch.Groups[1].Value) : string.Empty;
                if (string.IsNullOrWhiteSpace(amount))
                {
                    var context = BuildContext(lines, i, 3);
                    qtyMatch = qtyRegex.Match(context);
                    if (qtyMatch.Success)
                        amount = NormalizeQty(qtyMatch.Groups[1].Value);
                }

                var dateBlock = BuildContext(lines, Math.Max(0, i - 2), 6);
                var deliveryDate = OrderVerificationDateHelper.ExtractDueDate(dateBlock);

                results.Add(new OrderVerificationLine
                {
                    DrawingNumber = drawing,
                    Amount = amount,
                    DeliveryDate = deliveryDate
                });
            }

            return results;
        }

        private static string BuildContext(IList<string> lines, int start, int count)
        {
            return string.Join(" ", lines.Skip(start).Take(count));
        }

        private static string NormalizeQty(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
                return string.Empty;

            var text = raw.Trim().Replace(',', '.');
            if (double.TryParse(text, NumberStyles.Any, CultureInfo.InvariantCulture, out var value))
                return ((int)Math.Round(value)).ToString(CultureInfo.InvariantCulture);

            return raw.Trim();
        }
    }
}
