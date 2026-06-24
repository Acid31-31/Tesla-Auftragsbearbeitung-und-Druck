using System.Collections.Generic;

namespace PDF_umbenenen.Models
{
    public sealed class OrderVerificationLine
    {
        public string ArticleNumber { get; set; }

        public string DrawingNumber { get; set; }

        public string DeliveryDate { get; set; }

        public string Amount { get; set; }

        public string SourceHint { get; set; }

        public string MatchKey
        {
            get
            {
                if (!string.IsNullOrWhiteSpace(DrawingNumber))
                {
                    var drawKey = OrderVerificationDrawingKey.Normalize(DrawingNumber);
                    if (!string.IsNullOrWhiteSpace(drawKey))
                        return "D:" + drawKey;
                }

                if (!string.IsNullOrWhiteSpace(ArticleNumber))
                    return "A:" + ArticleNumber.ToUpperInvariant();

                return string.Empty;
            }
        }
    }
}
