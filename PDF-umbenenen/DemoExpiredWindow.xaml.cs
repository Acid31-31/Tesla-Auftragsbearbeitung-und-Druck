using System.Windows;
using PDF_umbenenen.Services;

namespace PDF_umbenenen
{
    public partial class DemoExpiredWindow : Window
    {
        public DemoExpiredWindow()
        {
            InitializeComponent();
            ThemeWindowService.Attach(this);
            Icon = ThemeWindowService.GetAppIcon();

            var status = DemoLicenseService.Evaluate();

            MessageTextBlock.Text =
                "Die " + AppInfo.DemoPeriodDays + "-Tage-Demoversion von " + AppInfo.ProductName
                + " ist abgelaufen.";

            DetailTextBlock.Text =
                "Erststart: " + status.FirstRunLocal.ToString("dd.MM.yyyy")
                + "   |   Gültig bis: " + status.ExpiresLocal.ToString("dd.MM.yyyy")
                + "\n\nBitte wenden Sie sich an den Anbieter für eine Vollversion.";
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
