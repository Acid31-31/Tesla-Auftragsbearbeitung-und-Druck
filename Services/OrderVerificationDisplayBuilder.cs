using System;

using System.Collections.Generic;

using System.Linq;

using PDF_umbenenen.Models;



namespace PDF_umbenenen.Services

{

    internal static class OrderVerificationDisplayBuilder

    {

        public static IList<OrderVerificationRowDisplay> Build(

            IList<OrderExcelRow> excelRows,

            OrderVerificationReport report)

        {

            var rows = new List<OrderVerificationRowDisplay>();

            if (excelRows == null)

                return rows;



            var assemblyKeys = BuildAssemblyDrawingKeys(excelRows);

            var pdfLines = report?.ParsedOrderLines ?? new List<OrderVerificationLine>();

            var matchedPdfKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            var position = 0;



            foreach (var row in excelRows)

            {

                // Alle Excel-Zeichnungen listen: Bestellpositionen und BG-Einzelzeichnungen („verbaut in …“).

                if (string.IsNullOrWhiteSpace(row.DrawingNumber))

                    continue;



                position++;

                var pdfLine = OrderVerificationLookup.FindMatchingLine(row, pdfLines);

                if (pdfLine != null && !string.IsNullOrWhiteSpace(pdfLine.MatchKey))

                    matchedPdfKeys.Add(pdfLine.MatchKey);



                rows.Add(CreateRow(position, row, pdfLine, assemblyKeys));

            }



            foreach (var pdfLine in pdfLines)

            {

                if (string.IsNullOrWhiteSpace(pdfLine.MatchKey) || matchedPdfKeys.Contains(pdfLine.MatchKey))

                    continue;



                position++;

                rows.Add(new OrderVerificationRowDisplay

                {

                    Position = position,

                    DrawingNumber = pdfLine.DrawingNumber ?? pdfLine.ArticleNumber ?? "—",

                    Description = "—",

                    ExcelAmount = "—",

                    ExcelDate = "—",

                    PdfAmount = OrderVerificationAmountHelper.FormatDisplay(pdfLine.Amount),

                    PdfDate = FormatValue(pdfLine.DeliveryDate),

                    Status = OrderVerificationMatchStatus.MissingInExcel,

                    StatusIcon = "✕",

                    StatusTitle = "Fehlt in Excel",

                    StatusDetail = "Position nur in PDF",

                    HighlightPdfDate = true,

                    HighlightPdfAmount = !string.IsNullOrWhiteSpace(pdfLine.Amount),

                    IsAssembly = false

                });

            }



            return rows;

        }



        /// <summary>

        /// Zeichnungen, auf die andere Positionen per „verbaut in“ / ParentArtikel zeigen = Baugruppe.

        /// </summary>

        public static HashSet<string> BuildAssemblyDrawingKeys(IList<OrderExcelRow> excelRows)

        {

            var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (excelRows == null || excelRows.Count == 0)

                return keys;



            var articleToDrawing = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (var row in excelRows)

            {

                if (string.IsNullOrWhiteSpace(row.DrawingNumber))

                    continue;



                var drawingKey = OrderVerificationDrawingKey.Normalize(row.DrawingNumber);

                if (string.IsNullOrWhiteSpace(drawingKey))

                    continue;



                if (row.ArticleNumbers == null)

                    continue;



                foreach (var article in row.ArticleNumbers)

                {

                    if (string.IsNullOrWhiteSpace(article))

                        continue;



                    articleToDrawing[article.Trim()] = drawingKey;

                }

            }



            foreach (var row in excelRows)

            {

                var parents = new List<string>();
                if (row.ParentArticleNumbers != null)
                    parents.AddRange(row.ParentArticleNumbers);
                if (!string.IsNullOrWhiteSpace(row.ParentArticleNumber))
                    parents.Add(row.ParentArticleNumber);

                foreach (var parent in parents)
                {
                    if (string.IsNullOrWhiteSpace(parent))
                        continue;

                    if (articleToDrawing.TryGetValue(parent.Trim(), out var parentDrawing)
                        && !string.IsNullOrWhiteSpace(parentDrawing))
                    {
                        keys.Add(parentDrawing);
                    }
                }

            }



            return keys;

        }



        public static bool IsAssemblyDrawing(OrderExcelRow row, ISet<string> assemblyKeys)

        {

            if (row == null || assemblyKeys == null || assemblyKeys.Count == 0)

                return false;



            var key = OrderVerificationDrawingKey.Normalize(row.DrawingNumber);

            if (string.IsNullOrWhiteSpace(key))

                return false;



            return assemblyKeys.Contains(key);

        }



        private static OrderVerificationRowDisplay CreateRow(

            int position,

            OrderExcelRow row,

            OrderVerificationLine pdfLine,

            ISet<string> assemblyKeys)

        {

            var isAssembly = IsAssemblyDrawing(row, assemblyKeys);

            var isBgSub = OrderVerificationLookup.IsBgSubDrawing(row);

            // Baugruppe → LT; freie Einzelteile → LT; nur BG-Unterzeichnungen („verbaut in“) → FT

            var useManufacturingTermin = isBgSub;

            var excelDate = FormatDeliveryDate(row.DeliveryDate, useManufacturingTermin);



            var display = new OrderVerificationRowDisplay

            {

                Position = position,

                DrawingNumber = row.DrawingNumber ?? "—",

                Description = isAssembly

                    ? "Baugruppe · LT"

                    : (isBgSub ? "Einzelteil · FT" : "Einzelteil · LT"),

                ExcelAmount = OrderVerificationAmountHelper.FormatDisplay(row.Amount),

                ExcelDate = excelDate,

                PdfAmount = pdfLine != null ? OrderVerificationAmountHelper.FormatDisplay(pdfLine.Amount) : "—",

                PdfDate = pdfLine != null ? FormatValue(pdfLine.DeliveryDate) : "—",

                SourceRowIndex = row.RowIndex,

                SourceRow = row,

                IsAssembly = isAssembly,

                IsBgSubDrawing = isBgSub

            };



            if (pdfLine == null)

            {

                // BG-Einzelzeichnungen stehen meist nicht in der Bestell-PDF – kein Fehler.

                if (isBgSub)

                {

                    display.Status = OrderVerificationMatchStatus.Skipped;

                    display.StatusIcon = "○";

                    display.StatusTitle = "BG-Einzelzeichnung";

                    display.StatusDetail = "Nur in Excel (verbaut in Baugruppe)";

                    return display;

                }



                display.Status = OrderVerificationMatchStatus.MissingInPdf;

                display.StatusIcon = "✕";

                display.StatusTitle = "Fehlt in der Bestellliste (Auftragsbestätigung)";

                display.StatusDetail = "Position nur in Excel";

                return display;

            }



            display.HighlightPdfDate = !DatesEqual(row.DeliveryDate, pdfLine.DeliveryDate);

            var amountMismatch = !AmountsEqual(row.Amount, pdfLine.Amount);

            display.HighlightPdfAmount = amountMismatch;

            display.HighlightExcelAmount = amountMismatch;

            display.HighlightExcelDate = display.HighlightPdfDate;



            if (display.HighlightPdfDate || display.HighlightPdfAmount)

            {

                display.Status = OrderVerificationMatchStatus.Deviation;

                display.StatusIcon = "⚠";

                display.StatusTitle = "Abweichung";

                display.StatusDetail = BuildDeviationDetail(display.HighlightPdfDate, display.HighlightPdfAmount);

                return display;

            }



            display.Status = OrderVerificationMatchStatus.Match;

            display.StatusIcon = "✓";

            display.StatusTitle = "Übereinstimmung";

            return display;

        }



        private static string FormatDeliveryDate(string raw, bool isManufacturingTermin)

        {

            if (string.IsNullOrWhiteSpace(raw))

                return "—";



            var formatted = OrderVerificationLookup.FormatWatermarkTermin(raw, isManufacturingTermin);

            return string.IsNullOrWhiteSpace(formatted) ? FormatValue(raw) : formatted;

        }



        private static string BuildDeviationDetail(bool dateMismatch, bool amountMismatch)

        {

            if (dateMismatch && amountMismatch)

                return "Liefertermin und Bestellmenge unterscheiden sich";



            if (dateMismatch)

                return "Liefertermin unterscheidet sich";



            return "Bestellmenge unterscheidet sich";

        }



        private static bool DatesEqual(string excel, string pdf)

        {

            if (string.IsNullOrWhiteSpace(excel) || string.IsNullOrWhiteSpace(pdf))

                return true;



            return OrderVerificationDateHelper.AreEqual(excel, pdf);

        }



        private static bool AmountsEqual(string excel, string pdf)

        {

            return OrderVerificationAmountHelper.AreEqual(excel, pdf);

        }



        private static string FormatValue(string value)

        {

            return string.IsNullOrWhiteSpace(value) ? "—" : value.Trim();

        }

    }

}

