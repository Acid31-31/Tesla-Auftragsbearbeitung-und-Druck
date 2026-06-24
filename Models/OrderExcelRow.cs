using System.Collections.Generic;

namespace PDF_umbenenen.Models
{
    public sealed class OrderExcelRow
    {
        public int RowIndex { get; set; }

        public string DrawingNumber { get; set; }

        public string SearchKey { get; set; }

        public List<string> ArticleNumbers { get; set; } = new List<string>();

        public string ParentArticleNumber { get; set; }

        public string Amount { get; set; }

        public string DeliveryDate { get; set; }

        public string DeliveryLocation { get; set; }
    }
}
