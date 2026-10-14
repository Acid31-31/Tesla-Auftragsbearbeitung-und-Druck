using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using PDF_umbenenen.Models;
using PDF_umbenenen.Services.Ai;

namespace PDF_umbenenen.Services
{
    internal static class CustomerOrderListParser
    {
        /// <summary>Menge mit Einheit: „1,00 Stk“ / „8,00 Stk“.</summary>
        private static readonly Regex DecimalQtyWithUnitRegex = new Regex(
            @"\b(\d+[.,]\d{1,2})\s*(?:EA|PCS|STK\.?|STÜCK|STUECK)\b",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

        /// <summary>
        /// BIOS/EcoMark Lesereihenfolge A: Pos + Menge + Einheit → „1 1,00 Stk“ / „16 8,00 Stk“.
        /// Group1=Pos (ignorieren), Group2=Menge.
        /// </summary>
        private static readonly Regex PosThenQtyRegex = new Regex(
            @"\b(\d{1,4})\s+(\d+[.,]\d{1,2})\s*(?:EA|PCS|STK\.?|STÜCK|STUECK)\b",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

        /// <summary>
        /// BIOS Lesereihenfolge B (SimpleText): Menge + Pos + Einheit → „1,00 1 Stk“ / „8,00 16 Stk“.
        /// Group1=Menge, Group2=Pos (ignorieren). Niemals Group2 als Stückzahl verwenden!
        /// </summary>
        private static readonly Regex QtyThenPosRegex = new Regex(
            @"\b(\d+[.,]\d{1,2})\s+(\d{1,4})\s*(?:EA|PCS|STK\.?|STÜCK|STUECK)\b",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

        public static IList<OrderVerificationLine> Parse(
            string orderFilePath,
            CustomerProfile profile,
            PdfTextExtractionService textService)
        {
            if (string.IsNullOrWhiteSpace(orderFilePath) || profile == null || !File.Exists(orderFilePath))
                return Array.Empty<OrderVerificationLine>();

            var text = ReadOrderFileText(orderFilePath, textService);
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

            var lines = text
                .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(l => l.Trim())
                .Where(l => l.Length > 0)
                .ToList();

            var drawings = new List<(int Line, int Start, int End, string Drawing, string Key)>();
            var quantities = new List<(int Line, int Start, string Amount)>();

            for (var i = 0; i < lines.Count; i++)
            {
                var line = lines[i];
                foreach (Match m in drawingRegex.Matches(line))
                {
                    var drawing = m.Value.ToUpperInvariant();
                    var key = OrderVerificationDrawingKey.Normalize(drawing);
                    if (string.IsNullOrWhiteSpace(key))
                        continue;
                    drawings.Add((i, m.Index, m.Index + m.Length, drawing, key));
                }

                CollectQuantitiesFromLine(line, i, quantities);
            }

            var results = new List<OrderVerificationLine>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            for (var d = 0; d < drawings.Count; d++)
            {
                var hit = drawings[d];
                if (!seen.Add(hit.Key))
                    continue;

                var previous = d > 0 ? drawings[d - 1] : ((int Line, int Start, int End, string Drawing, string Key)?)null;
                var amount = FindAmountBeforeDrawing(hit, previous, quantities);

                var dateBlock = BuildContext(lines, Math.Max(0, hit.Line - 3), 5);
                var deliveryDate = OrderVerificationDateHelper.ExtractDueDate(dateBlock);

                results.Add(new OrderVerificationLine
                {
                    DrawingNumber = hit.Drawing,
                    Amount = amount,
                    DeliveryDate = deliveryDate
                });
            }

            return results;
        }

        private static string ReadOrderFileText(string path, PdfTextExtractionService textService)
        {
            if (OrderVerificationFileHelper.IsOrderTextExtension(path))
            {
                try
                {
                    var bytes = File.ReadAllBytes(path);
                    if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
                        return Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
                    var utf8 = Encoding.UTF8.GetString(bytes);
                    if (utf8.IndexOf('\uFFFD') < 0)
                        return utf8;
                    return Encoding.Default.GetString(bytes);
                }
                catch
                {
                    return string.Empty;
                }
            }

            if (textService == null)
                return string.Empty;
            return textService.ExtractText(path, 120);
        }

        private static void CollectQuantitiesFromLine(
            string line,
            int lineIndex,
            List<(int Line, int Start, string Amount)> quantities)
        {
            if (string.IsNullOrWhiteSpace(line))
                return;

            // 1) „1,00 1 Stk“ / „8,00 16 Stk“ → Menge = Dezimalwert (nie die Pos-Zahl vor Stk)
            foreach (Match m in QtyThenPosRegex.Matches(line))
            {
                var amount = OrderVerificationAmountHelper.NormalizeQuantity(m.Groups[1].Value);
                if (string.IsNullOrWhiteSpace(amount))
                    continue;
                AddQuantity(quantities, lineIndex, m.Groups[1].Index, amount);
            }

            // 2) „1 1,00 Stk“ / „16 8,00 Stk“ → Menge = Dezimalwert
            foreach (Match m in PosThenQtyRegex.Matches(line))
            {
                var amount = OrderVerificationAmountHelper.NormalizeQuantity(m.Groups[2].Value);
                if (string.IsNullOrWhiteSpace(amount))
                    continue;
                AddQuantity(quantities, lineIndex, m.Groups[2].Index, amount);
            }

            // 3) Reines „1,00 Stk“ ohne Pos dazwischen
            foreach (Match m in DecimalQtyWithUnitRegex.Matches(line))
            {
                var amount = OrderVerificationAmountHelper.NormalizeQuantity(m.Groups[1].Value);
                if (string.IsNullOrWhiteSpace(amount))
                    continue;
                AddQuantity(quantities, lineIndex, m.Index, amount);
            }

            // Kein Integer+Stk-Fallback: bei „1,00 1 Stk“ wäre „1 Stk“ die Positionsnummer.
        }

        private static void AddQuantity(
            List<(int Line, int Start, string Amount)> quantities,
            int line,
            int start,
            string amount)
        {
            if (quantities.Any(q => q.Line == line && Math.Abs(q.Start - start) < 3))
                return;

            quantities.Add((line, start, amount));
        }

        private static string FindAmountBeforeDrawing(
            (int Line, int Start, int End, string Drawing, string Key) drawing,
            (int Line, int Start, int End, string Drawing, string Key)? previousDrawing,
            List<(int Line, int Start, string Amount)> quantities)
        {
            foreach (var qty in quantities.OrderByDescending(q => q.Line).ThenByDescending(q => q.Start))
            {
                if (qty.Line > drawing.Line)
                    continue;
                if (qty.Line == drawing.Line && qty.Start > drawing.Start)
                    continue;

                if (previousDrawing.HasValue)
                {
                    var prev = previousDrawing.Value;
                    if (qty.Line < prev.Line)
                        break;
                    if (qty.Line == prev.Line && qty.Start <= prev.End)
                        break;
                }

                return qty.Amount;
            }

            return string.Empty;
        }

        private static string BuildContext(IList<string> lines, int start, int count)
        {
            return string.Join(" ", lines.Skip(start).Take(count));
        }
    }
}
