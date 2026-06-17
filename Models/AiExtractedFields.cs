namespace PDF_umbenenen.Models
{
    public sealed class AiExtractedFields
    {
        public string DrawingNumber { get; set; }

        public string RequestedAmount { get; set; }

        public string RequestedDeliveryDate { get; set; }

        public string RequestedDeliveryLocation { get; set; }

        public double Confidence { get; set; }
    }
}
