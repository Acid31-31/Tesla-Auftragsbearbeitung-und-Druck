namespace PDF_umbenenen.Models
{
    public sealed class OrderVerificationDateMismatch
    {
        public string DrawingKey { get; set; }

        public string DrawingLabel { get; set; }

        public string ExcelDate { get; set; }

        public string PdfDate { get; set; }

        public string ToDisplayText()
        {
            return OrderVerificationReport.FormatFieldMismatch(
                DrawingLabel,
                "Liefertermin",
                ExcelDate,
                PdfDate);
        }
    }
}
