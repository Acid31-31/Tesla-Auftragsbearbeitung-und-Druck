using System.Collections.Generic;

namespace PDF_umbenenen.Models
{
    public sealed class MergedPdfPageEntry
    {
        public int MergedPageNumber { get; set; }

        public string SourcePdfPath { get; set; } = string.Empty;

        public int SourcePageNumber { get; set; }
    }
}
