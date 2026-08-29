namespace PDF_umbenenen.Models
{
    public sealed class OrderVerificationDialogResult
    {
        public bool Proceed { get; set; }

        public bool AdoptOrderPdfDates { get; set; }

        public bool DatesModified { get; set; }

        /// <summary>Aktuelle Excel-Zeilen inkl. manuell geänderter Liefertermine.</summary>
        public System.Collections.Generic.IList<OrderExcelRow> UpdatedExcelRows { get; set; }
    }
}
