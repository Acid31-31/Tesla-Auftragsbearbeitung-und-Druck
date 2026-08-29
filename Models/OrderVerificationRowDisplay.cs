using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace PDF_umbenenen.Models
{
    public enum OrderVerificationMatchStatus
    {
        Match,
        Deviation,
        MissingInPdf,
        MissingInExcel,
        Skipped
    }

    public sealed class OrderVerificationRowDisplay : INotifyPropertyChanged
    {
        private string _excelDate = "—";
        private string _pdfDate = "—";
        private string _description = "—";
        private bool _highlightPdfDate;
        private bool _highlightExcelDate;
        private OrderVerificationMatchStatus _status;
        private string _statusTitle = string.Empty;
        private string _statusDetail = string.Empty;
        private string _statusIcon = string.Empty;

        public event PropertyChangedEventHandler PropertyChanged;

        public int Position { get; set; }

        public string DrawingNumber { get; set; }

        public string Description
        {
            get { return _description; }
            set
            {
                if (_description == value)
                    return;
                _description = value;
                OnPropertyChanged();
            }
        }

        public string ExcelAmount { get; set; }

        public string ExcelDate
        {
            get { return _excelDate; }
            set
            {
                if (_excelDate == value)
                    return;
                _excelDate = value;
                OnPropertyChanged();
            }
        }

        public string PdfAmount { get; set; }

        public string PdfDate
        {
            get { return _pdfDate; }
            set
            {
                if (_pdfDate == value)
                    return;
                _pdfDate = value;
                OnPropertyChanged();
            }
        }

        public bool HighlightPdfDate
        {
            get { return _highlightPdfDate; }
            set
            {
                if (_highlightPdfDate == value)
                    return;
                _highlightPdfDate = value;
                OnPropertyChanged();
            }
        }

        public bool HighlightPdfAmount { get; set; }

        public bool HighlightExcelAmount { get; set; }

        public bool HighlightExcelDate
        {
            get { return _highlightExcelDate; }
            set
            {
                if (_highlightExcelDate == value)
                    return;
                _highlightExcelDate = value;
                OnPropertyChanged();
            }
        }

        public OrderVerificationMatchStatus Status
        {
            get { return _status; }
            set
            {
                if (_status == value)
                    return;
                _status = value;
                OnPropertyChanged();
            }
        }

        public string StatusTitle
        {
            get { return _statusTitle; }
            set
            {
                if (_statusTitle == value)
                    return;
                _statusTitle = value;
                OnPropertyChanged();
            }
        }

        public string StatusDetail
        {
            get { return _statusDetail; }
            set
            {
                if (_statusDetail == value)
                    return;
                _statusDetail = value;
                OnPropertyChanged();
            }
        }

        public string StatusIcon
        {
            get { return _statusIcon; }
            set
            {
                if (_statusIcon == value)
                    return;
                _statusIcon = value;
                OnPropertyChanged();
            }
        }

        public int SourceRowIndex { get; set; } = -1;

        public OrderExcelRow SourceRow { get; set; }

        /// <summary>True = Baugruppe (LT), False = Einzelteil (LT/FT).</summary>
        public bool IsAssembly { get; set; }

        /// <summary>True = BG-Unterzeichnung („verbaut in …“), dunkler Hintergrund.</summary>
        public bool IsBgSubDrawing { get; set; }

        public string KindLabel
        {
            get { return IsAssembly ? "Baugruppe" : "Einzelteil"; }
        }

        private void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            var handler = PropertyChanged;
            if (handler != null)
                handler(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
