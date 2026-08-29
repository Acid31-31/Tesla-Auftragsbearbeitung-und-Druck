using System;
using System.Windows;
using System.Windows.Input;
using PDF_umbenenen.Services;

namespace PDF_umbenenen
{
    public partial class DemoExpiredWindow : Window
    {
        public bool ActivatedFullVersion { get; private set; }

        public DemoExpiredWindow()
        {
            InitializeComponent();
            ThemeWindowService.Attach(this);
            Icon = ThemeWindowService.GetAppIcon();

            var status = DemoLicenseService.Evaluate();
            var machineCode = LicenseActivationService.GetMachineCode();

            MessageTextBlock.Text =
                "Die Demoversion von " + AppInfo.ProductName + " ist abgelaufen.";

            DetailTextBlock.Text =
                "Erststart: " + status.FirstRunLocal.ToString("dd.MM.yyyy")
                + "   |   Gültig bis: " + status.ExpiresLocal.ToString("dd.MM.yyyy")
                + "\n\nOption 1: Update installieren (längere Demo-Laufzeit in neuer Version)."
                + "\nOption 2: Lizenzschlüssel eingeben und Vollversion freischalten.";

            MachineCodeTextBlock.Text =
                "PC-Code (an Anbieter senden): " + machineCode;
        }

        private void Activate_Click(object sender, RoutedEventArgs e)
        {
            if (!LicenseActivationService.TryActivate(LicenseKeyTextBox.Text, out var message))
            {
                MessageBox.Show(this, message, "Freischaltung", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            ActivatedFullVersion = true;
            MessageBox.Show(this, message, "Freischaltung", MessageBoxButton.OK, MessageBoxImage.Information);
            DialogResult = true;
            Close();
        }

        private async void CheckUpdate_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Mouse.OverrideCursor = Cursors.Wait;
                GitHubUpdateService.EnsureUpdateTokenFromInstallFolder();
                var update = await GitHubUpdateService.CheckForUpdateAsync().ConfigureAwait(true);

                if (!string.IsNullOrWhiteSpace(update.ErrorMessage))
                {
                    MessageBox.Show(
                        this,
                        update.ErrorMessage
                        + "\n\nAlternative: Neues Installationspaket einspielen oder Lizenzschlüssel verwenden.",
                        "Update-Prüfung",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                    return;
                }

                if (update.UpdateAvailable)
                {
                    var dialog = new UpdateAvailableWindow(update)
                    {
                        Owner = this,
                        WindowStartupLocation = WindowStartupLocation.CenterOwner
                    };
                    ThemeWindowService.Attach(dialog);
                    dialog.ShowDialog();
                    return;
                }

                MessageBox.Show(
                    this,
                    "Kein neueres Update auf GitHub gefunden.\n\n"
                    + "Bitte neues Installationspaket einspielen oder Lizenzschlüssel freischalten.",
                    "Update-Prüfung",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    this,
                    "Update-Prüfung fehlgeschlagen: " + ex.Message,
                    "Update-Prüfung",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
            finally
            {
                Mouse.OverrideCursor = null;
            }
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
