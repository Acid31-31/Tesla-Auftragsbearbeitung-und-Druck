using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace PDF_umbenenen.Models
{
    public sealed class OrderVerificationReport
    {
        public string OrderPdfPath { get; set; }

        public int PdfLineCount { get; set; }

        public int ExcelRowCount { get; set; }

        public List<string> MissingInPdf { get; } = new List<string>();

        public List<string> MissingInExcel { get; } = new List<string>();

        public List<string> FieldMismatches { get; } = new List<string>();

        public List<OrderVerificationDateMismatch> DateMismatches { get; } = new List<OrderVerificationDateMismatch>();

        public int BgSubDrawingsSkipped { get; set; }

        public List<string> MissingDrawingPdfs { get; } = new List<string>();

        public List<string> DrawingsNotInOrderPdf { get; } = new List<string>();

        public List<string> InfoMessages { get; } = new List<string>();

        public IList<OrderVerificationLine> ParsedOrderLines { get; set; }

        public int DrawingPdfCount { get; set; }

        public bool HasBlockingIssues =>
            MissingInPdf.Count > 0
            || MissingInExcel.Count > 0
            || FieldMismatches.Count > 0
            || DateMismatches.Count > 0
            || MissingDrawingPdfs.Count > 0
            || DrawingsNotInOrderPdf.Count > 0;

        public bool HasDateMismatches => DateMismatches.Count > 0;

        public static string FormatFieldMismatch(
            string rowLabel,
            string fieldLabel,
            string excelValue,
            string pdfValue)
        {
            var excel = FormatComparisonValue(excelValue);
            var pdf = FormatComparisonValue(pdfValue);
            var nl = System.Environment.NewLine;

            return string.Format(
                System.Globalization.CultureInfo.InvariantCulture,
                "{0} – {1}{2}  Excel:        {3}{2}  Bestell-PDF:  {4}",
                rowLabel,
                fieldLabel,
                nl,
                excel,
                pdf);
        }

        private static string FormatComparisonValue(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? "—" : value.Trim();
        }

        public string ToDisplayText()
        {
            return ToDetailedReportText(includeHeader: true, compactSuccess: true);
        }

        public string ToDetailedReportText(bool includeHeader = true, bool compactSuccess = false)
        {
            var lines = new List<string>();
            if (includeHeader)
                lines.Add("── Auftragsprüfung (Bestell-PDF vs Excel) ──");

            lines.Add(string.Format(
                "Erkannt: {0} Position(en) in Bestell-PDF, {1} Excel-Zeile(n), {2} Zeichnungs-PDF(s).",
                PdfLineCount,
                ExcelRowCount,
                DrawingPdfCount));

            if (PdfLineCount == 0)
                lines.Add("⚠ In der Bestell-PDF wurden keine Artikel/Zeichnungen erkannt.");

            if (!HasBlockingIssues && PdfLineCount > 0)
            {
                if (compactSuccess)
                {
                    lines.Add(string.Format(
                        "✓ Keine Abweichungen – Liefertermine aus Bestell-PDF, {0} Zeichnung(en) geprüft.",
                        DrawingPdfCount > 0 ? DrawingPdfCount : ExcelRowCount));
                }
                else
                {
                    lines.Add(string.Format(
                        "✓ Keine Abweichungen bei Mengen, Lieferterminen oder Zuordnung ({0} Excel-Zeile(n) geprüft).",
                        ExcelRowCount));
                }
            }
            else
            {
                AppendSection(lines, "In Excel, fehlt in Bestell-PDF", MissingInPdf);
                AppendSection(lines, "In Bestell-PDF, fehlt in Excel", MissingInExcel);
                AppendSection(lines, "Zeichnungs-PDF fehlt in Ablage", MissingDrawingPdfs);
                AppendSection(lines, "Zeichnungs-PDF nicht in Bestell-PDF", DrawingsNotInOrderPdf);
                AppendSection(lines, "Abweichungen (Bestellmenge)", FieldMismatches);
                AppendSection(lines, "Abweichungen (Liefertermin)", DateMismatches.Select(m => m.ToDisplayText()).ToList());
            }

            foreach (var info in InfoMessages)
                lines.Add(info);

            if (includeHeader)
                lines.Add("────────────────────────────────────");

            return string.Join(System.Environment.NewLine, lines);
        }

        public string ToSummaryText()
        {
            var sb = new StringBuilder();
            sb.AppendLine("Die Bestell-PDF weicht von der Excel-Datei ab:");
            if (MissingInPdf.Count > 0)
                sb.AppendLine("• " + MissingInPdf.Count + " Excel-Zeile(n) fehlen in der PDF");
            if (MissingInExcel.Count > 0)
                sb.AppendLine("• " + MissingInExcel.Count + " PDF-Position(en) fehlen in Excel");
            if (FieldMismatches.Count > 0)
                sb.AppendLine("• " + FieldMismatches.Count + " Abweichung(en) bei Bestellmenge");
            if (DateMismatches.Count > 0)
                sb.AppendLine("• " + DateMismatches.Count + " Abweichung(en) bei Liefertermin (aus Bestell-PDF übernehmbar)");
            if (MissingDrawingPdfs.Count > 0)
                sb.AppendLine("• " + MissingDrawingPdfs.Count + " Bestell-Position(en) ohne Zeichnungs-PDF");
            if (DrawingsNotInOrderPdf.Count > 0)
                sb.AppendLine("• " + DrawingsNotInOrderPdf.Count + " Zeichnungs-PDF(s) ohne Bestell-Position");
            if (PdfLineCount == 0)
                sb.AppendLine("• Keine Bestellpositionen in der PDF erkannt");
            return sb.ToString().TrimEnd();
        }

        private static void AppendSection(List<string> lines, string title, List<string> items)
        {
            if (items.Count == 0)
                return;

            lines.Add(string.Empty);
            lines.Add(title + ":");
            foreach (var item in items.Take(25))
            {
                var parts = item.Split(new[] { '\r', '\n' }, System.StringSplitOptions.RemoveEmptyEntries);
                for (var i = 0; i < parts.Length; i++)
                    lines.Add(i == 0 ? "  • " + parts[i] : "      " + parts[i]);
            }
            if (items.Count > 25)
                lines.Add("  • … und " + (items.Count - 25) + " weitere");
        }
    }
}
