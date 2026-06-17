namespace PDF_umbenenen.Models
{
    public sealed class AiPdfInfo
    {
        public string FullPath { get; set; }

        public string DisplayName { get; set; }

        public string OriginalName { get; set; }

        public string SearchKey { get; set; }

        public int Order { get; set; }

        public string ExtractedText { get; set; }
    }
}
