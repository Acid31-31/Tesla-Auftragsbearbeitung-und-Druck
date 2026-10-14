namespace PDF_umbenenen.Models
{
    public sealed class CustomerSortPreviewLine
    {
        public int SortIndex { get; set; }

        public string PdfFileName { get; set; } = string.Empty;

        public string PdfFullPath { get; set; } = string.Empty;

        public string PdfSearchKey { get; set; } = string.Empty;

        public int ExcelRow { get; set; }

        public string ExcelDrawing { get; set; } = string.Empty;

        public string ExcelArticle { get; set; } = string.Empty;

        public string ParentArticle { get; set; } = string.Empty;

        public string ProposedFileName { get; set; } = string.Empty;

        public string Status { get; set; } = string.Empty;

        public string RequestedAmount { get; set; } = string.Empty;

        public string RequestedDeliveryDate { get; set; } = string.Empty;

        public string RequestedDeliveryLocation { get; set; } = string.Empty;

        public string OrderNumber { get; set; } = string.Empty;
    }
}
