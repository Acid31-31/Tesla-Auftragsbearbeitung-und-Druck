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

        /// <summary>Alle Parent-Artikel, wenn dasselbe Teil in mehreren Baugruppen steckt.</summary>
        public List<string> ParentArticleNumbers { get; set; } = new List<string>();

        /// <summary>BG-Zeichnungsnummern (aufgelöst aus Parent-Artikeln), z. B. TSL-… R00.</summary>
        public List<string> BgDrawingNumbers { get; set; } = new List<string>();

        public string RevisionNumber { get; set; }

        /// <summary>Materialstärke aus sortierter PDF/Wasserzeichen (z. B. 3mm).</summary>
        public string MaterialThicknessMm { get; set; }

        public string Amount { get; set; }

        public string DeliveryDate { get; set; }

        public string DeliveryLocation { get; set; }
    }
}
