namespace PDF_umbenenen.Models
{
    public sealed class AiMatchSuggestion
    {
        public string PdfPath { get; set; }

        public string PdfDisplayName { get; set; }

        public int ExcelRowIndex { get; set; }

        public string DrawingNumber { get; set; }

        public double Confidence { get; set; }

        public string Reason { get; set; }

        public bool IsAutoAccepted => Confidence >= 0.8;

        public bool IsWarning => Confidence >= 0.5 && Confidence < 0.8;
    }
}
