using System.Collections.Generic;

namespace PDF_umbenenen.Models
{
    public sealed class AiExcelRowInfo
    {
        public int RowIndex { get; set; }

        public List<string> NormalizedCells { get; set; } = new List<string>();

        public string RawDrawingNumber { get; set; }

        public List<string> DetectedArticleNumbers { get; set; } = new List<string>();

        public string RequestedAmount { get; set; }

        public string RequestedDeliveryDate { get; set; }

        public string RequestedDeliveryLocation { get; set; }
    }
}
