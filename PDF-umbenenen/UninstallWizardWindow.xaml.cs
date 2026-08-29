using System;
using System.Threading.Tasks;
using System.Windows;
using PDF_umbenenen.Services;

namespace PDF_umbenenen
{
    public partial class UninstallWizardWindow : Window
    {
        private bool _isRunning;

        public UninstallWizardWindow()
        {
            InitializeComponent();
            ThemeWindowService.Attach(this);
            Icon = ThemeWindowService.GetAppIcon();
            StepTitleTextBlock.Text = "Bestätigung";

            if (!UninstallService.IsInstalled())
                NotInstalledTextBlock.Visibility = Visibility.Visible;
        }

        private async void UninstallButton_Click(object sender, RoutedEventArgs e)
        {
            if (_isRunning)
                return;

            if (!UninstallService.IsInstalled())
            {
                MessageBox.Show(this, "Es wurde keine DOK-V01-Installation gefunden.", Title, MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var confirm = MessageBox.Show(
                this,
                "DOK-V01 wirklich deinstallieren?",
                Title,
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (confirm != MessageBoxResult.Yes)
                return;

            _isRunning = true;
            ConfirmPanel.Visibility = Visibility.Collapsed;
            ProgressPanel.Visibility = Visibility.Visible;
            UninstallButton.Visibility = Visibility.Collapsed;
            CancelButton.IsEnabled = false;
            StepTitleTextBlock.Text = "Deinstallation";

            var removeUserData = RemoveUserDataCheckBox.IsChecked == true;
            var progress = new Progress<string>(message => ProgressStatusTextBlock.Text = message);
            var result = await Task.Run(() => UninstallService.RunUninstall(removeUserData, progress)).ConfigureAwait(true);

            _isRunning = false;
            ProgressPanel.Visibility = Visibility.Collapsed;
            FinishPanel.Visibility = Visibility.Visible;
            CloseButton.Visibility = Visibility.Visible;
            CancelButton.Visibility = Visibility.Collapsed;
            FinishMessageTextBlock.Text = result.Message;
            StepTitleTextBlock.Text = result.Success ? "Abgeschlossen" : "Fehler";

            if (!result.Success)
                MessageBox.Show(this, result.Message, Title, MessageBoxButton.OK, MessageBoxImage.Error);
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            if (_isRunning)
                return;

            DialogResult = false;
            Close();
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
            Close();
        }
    }
}
