using System;

using System.Collections.Generic;

using System.Globalization;

using System.Linq;

using PDF_umbenenen.Models;

using PDF_umbenenen.Services.Ai;



namespace PDF_umbenenen.Services

{

    internal sealed class OrderExcelVerificationService

    {

        private readonly OrderVerificationPdfParser _parser = new OrderVerificationPdfParser();



        public OrderVerificationReport Verify(

            string orderPdfPath,

            IList<OrderExcelRow> excelRows,

            PdfTextExtractionService textService,

            IEnumerable<string> pdfDrawingSearchKeys = null)

        {

            var report = new OrderVerificationReport

            {

                OrderPdfPath = orderPdfPath,

                ExcelRowCount = excelRows?.Count ?? 0

            };



            if (string.IsNullOrWhiteSpace(orderPdfPath))

            {

                report.InfoMessages.Add("Keine Bestell-PDF angegeben.");

                return report;

            }



            if (excelRows == null || excelRows.Count == 0)

            {

                report.InfoMessages.Add("Keine Excel-Zeilen zum Abgleich vorhanden.");

                return report;

            }



            var pdfLines = _parser.Parse(orderPdfPath, textService).ToList();

            report.PdfLineCount = pdfLines.Count;

            report.ParsedOrderLines = pdfLines;



            if (pdfLines.Count == 0)

                return report;



            var matchedPdfKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);



            foreach (var row in excelRows)

            {

                if (OrderVerificationLookup.IsBgSubDrawing(row))

                {

                    report.BgSubDrawingsSkipped++;

                    continue;

                }



                var pdfLine = OrderVerificationLookup.FindMatchingLine(row, pdfLines);

                if (pdfLine == null)

                {

                    report.MissingInPdf.Add(FormatExcelRow(row));

                    continue;

                }



                if (!string.IsNullOrWhiteSpace(pdfLine.MatchKey))

                    matchedPdfKeys.Add(pdfLine.MatchKey);



                CompareFields(report, row, pdfLine);

            }



            foreach (var pdfLine in pdfLines)

            {

                if (string.IsNullOrWhiteSpace(pdfLine.MatchKey))

                    continue;



                if (matchedPdfKeys.Contains(pdfLine.MatchKey))

                    continue;



                report.MissingInExcel.Add(FormatPdfLine(pdfLine));

            }



            VerifyDrawingFiles(pdfDrawingSearchKeys, excelRows, pdfLines, report);



            return report;

        }



        private static void VerifyDrawingFiles(

            IEnumerable<string> pdfDrawingSearchKeys,

            IList<OrderExcelRow> excelRows,

            IList<OrderVerificationLine> pdfLines,

            OrderVerificationReport report)

        {

            if (pdfDrawingSearchKeys == null)

                return;



            var keys = pdfDrawingSearchKeys

                .Where(k => !string.IsNullOrWhiteSpace(k))

                .Distinct(StringComparer.OrdinalIgnoreCase)

                .ToList();



            report.DrawingPdfCount = keys.Count;



            foreach (var pdfKey in keys)

            {

                var excelRow = excelRows.FirstOrDefault(r =>

                    (!string.IsNullOrWhiteSpace(r.SearchKey)

                     && OrderVerificationLookup.KeysEqual(r.SearchKey, pdfKey))

                    || (!string.IsNullOrWhiteSpace(r.DrawingNumber)

                        && OrderVerificationLookup.KeysEqual(r.DrawingNumber, pdfKey)));



                if (excelRow != null && OrderVerificationLookup.IsBgSubDrawing(excelRow))

                    continue;



                OrderVerificationLine orderLine = null;

                if (excelRow != null)

                    orderLine = OrderVerificationLookup.FindMatchingLine(excelRow, pdfLines);

                else

                    orderLine = OrderVerificationLookup.FindMatchingLineByDrawingKey(pdfKey, pdfLines);



                if (orderLine == null)

                {

                    report.DrawingsNotInOrderPdf.Add(

                        "PDF «" + pdfKey + "»: keine passende Position in der Bestell-PDF");

                }

            }



            foreach (var pdfLine in pdfLines)

            {

                if (string.IsNullOrWhiteSpace(pdfLine.DrawingNumber))

                    continue;



                var drawKey = AiTextHelper.NormalizeKey(pdfLine.DrawingNumber);

                if (string.IsNullOrWhiteSpace(drawKey))

                    continue;



                if (!keys.Any(k => OrderVerificationLookup.KeysEqual(k, drawKey)))

                {

                    report.MissingDrawingPdfs.Add(

                        "Bestell-PDF «" + pdfLine.DrawingNumber + "»: keine Zeichnungs-PDF in der Ablage");

                }

            }

        }



        private static void CompareFields(

            OrderVerificationReport report,

            OrderExcelRow row,

            OrderVerificationLine pdfLine)

        {

            if (OrderVerificationLookup.IsBgSubDrawing(row))

                return;



            CompareValue(report, row, pdfLine, "Menge", row.Amount, pdfLine.Amount);

            CompareValue(report, row, pdfLine, "Lieferdatum", row.DeliveryDate, pdfLine.DeliveryDate);

        }



        private static void CompareValue(

            OrderVerificationReport report,

            OrderExcelRow row,

            OrderVerificationLine pdfLine,

            string label,

            string excelValue,

            string pdfValue)

        {

            if (label == "Lieferdatum")

            {

                if (OrderVerificationLookup.IsBgSubDrawing(row))

                    return;



                CompareDeliveryDate(report, row, pdfLine, excelValue, pdfValue);

                return;

            }



            if (string.IsNullOrWhiteSpace(pdfValue) || string.IsNullOrWhiteSpace(excelValue))

                return;



            var excelNorm = AiTextHelper.NormalizeKey(excelValue);

            var pdfNorm = AiTextHelper.NormalizeKey(pdfValue);

            if (excelNorm != pdfNorm)

            {

                report.FieldMismatches.Add(OrderVerificationReport.FormatFieldMismatch(

                    GetRowLabel(row, pdfLine),

                    label,

                    excelValue,

                    pdfValue));

            }

        }



        private static void CompareDeliveryDate(

            OrderVerificationReport report,

            OrderExcelRow row,

            OrderVerificationLine pdfLine,

            string excelValue,

            string pdfValue)

        {

            var excelHas = !string.IsNullOrWhiteSpace(excelValue);

            var pdfHas = !string.IsNullOrWhiteSpace(pdfValue);



            if (!pdfHas)

            {

                report.DateMismatches.Add(new OrderVerificationDateMismatch

                {

                    DrawingKey = OrderVerificationDrawingKey.Normalize(GetRowLabel(row, pdfLine)),

                    DrawingLabel = GetRowLabel(row, pdfLine),

                    ExcelDate = excelValue,

                    PdfDate = "(fehlt in Bestell-PDF)"

                });

                return;

            }



            if (excelHas && !DatesEqual(excelValue, pdfValue))

            {

                report.DateMismatches.Add(new OrderVerificationDateMismatch

                {

                    DrawingKey = OrderVerificationDrawingKey.Normalize(GetRowLabel(row, pdfLine)),

                    DrawingLabel = GetRowLabel(row, pdfLine),

                    ExcelDate = excelValue,

                    PdfDate = pdfValue

                });

            }

        }



        private static bool DatesEqual(string excelValue, string pdfValue)

        {

            return OrderVerificationDateHelper.AreEqual(excelValue, pdfValue);

        }



        private static string NormalizeDate(string raw)

        {

            return OrderVerificationDateHelper.NormalizeToEu(raw);

        }



        private static string FormatExcelRow(OrderExcelRow row)

        {

            var article = row.ArticleNumbers != null && row.ArticleNumbers.Count > 0

                ? row.ArticleNumbers[0]

                : row.ParentArticleNumber;

            return string.Format(

                "Zeichnung '{0}' Artikel='{1}' Datum='{2}' Menge='{3}'",

                row.DrawingNumber ?? "—",

                article ?? "—",

                row.DeliveryDate ?? "—",

                row.Amount ?? "—");

        }



        private static string FormatPdfLine(OrderVerificationLine line)

        {

            return string.Format(

                "PDF: Artikel='{0}' Zeichnung='{1}' Datum='{2}' Menge='{3}'",

                line.ArticleNumber ?? "—",

                line.DrawingNumber ?? "—",

                line.DeliveryDate ?? "—",

                line.Amount ?? "—");

        }



        private static string GetRowLabel(OrderExcelRow row, OrderVerificationLine pdfLine)

        {

            if (!string.IsNullOrWhiteSpace(row.DrawingNumber))

                return row.DrawingNumber;

            if (!string.IsNullOrWhiteSpace(pdfLine.ArticleNumber))

                return pdfLine.ArticleNumber;

            return "Position";

        }

    }

}


