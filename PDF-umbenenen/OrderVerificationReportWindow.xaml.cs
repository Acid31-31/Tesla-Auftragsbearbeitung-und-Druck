using System.Windows;
using System.Windows.Media;
using PDF_umbenenen.Models;

namespace PDF_umbenenen
{
    public partial class OrderVerificationReportWindow : Window
    {
        public bool ContinueDespiteIssues { get; private set; }

        public bool AdoptOrderPdfDates { get; private set; }

        private OrderVerificationReportWindow(
            OrderVerificationReport report,
            string excelFileName,
            string orderPdfFileName,
            bool offerContinueOnIssues)
        {
            InitializeComponent();

            var hasIssues = report.HasBlockingIssues;
            HeadlineTextBlock.Text = hasIssues
                ? "Abweichungen bei Bestellung / Lieferterminen"
                : "Auftragsprüfung bestanden";

            SourceInfoTextBlock.Text =
                "Excel: " + (excelFileName ?? "—")
                + "   |   Bestell-PDF: " + (orderPdfFileName ?? "—");

            SummaryTextBlock.Text = hasIssues
                ? report.ToSummaryText()
                : "Keine Abweichungen bei Zuordnung, Mengen oder Lieferterminen.";
            SummaryTextBlock.Foreground = hasIssues
                ? (Brush)FindResource("DangerBrush")
                : new SolidColorBrush(Color.FromRgb(40, 167, 69));

            ReportTextBlock.Text = report.ToDetailedReportText(includeHeader: false, compactSuccess: false);

            if (offerContinueOnIssues && hasIssues)
            {
                CancelButton.Visibility = Visibility.Visible;
                ContinueButton.Visibility = Visibility.Visible;
                CloseButton.Visibility = Visibility.Collapsed;

                if (report.HasDateMismatches)
                {
                    AdoptDatesButton.Visibility = Visibility.Visible;
                }
            }
            else
            {
                CloseButton.Content = hasIssues ? "Schließen" : "OK";
            }
        }

        public static OrderVerificationDialogResult ShowDialog(
            Window owner,
            OrderVerificationReport report,
            string excelFileName,
            string orderPdfFileName,
            bool offerContinueOnIssues)
        {
            var window = new OrderVerificationReportWindow(
                report,
                excelFileName,
                orderPdfFileName,
                offerContinueOnIssues)
            {
                Owner = owner
            };
            ThemeWindowService.Attach(window);

            window.ShowDialog();

            var result = new OrderVerificationDialogResult();
            if (offerContinueOnIssues && report.HasBlockingIssues)
            {
                result.Proceed = window.DialogResult == true && window.ContinueDespiteIssues;
                result.AdoptOrderPdfDates = window.AdoptOrderPdfDates;
            }
            else
            {
                result.Proceed = window.DialogResult == true;
            }

            return result;
        }

        private void AdoptDates_Click(object sender, RoutedEventArgs e)
        {
            AdoptOrderPdfDates = true;
            ContinueDespiteIssues = true;
            DialogResult = true;
            Close();
        }

        private void Continue_Click(object sender, RoutedEventArgs e)
        {
            ContinueDespiteIssues = true;
            DialogResult = true;
            Close();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            ContinueDespiteIssues = false;
            DialogResult = false;
            Close();
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
            Close();
        }
    }
}
