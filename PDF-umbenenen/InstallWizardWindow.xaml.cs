using System;
using System.Threading.Tasks;
using System.Windows;
using PDF_umbenenen.Models;
using PDF_umbenenen.Services;

namespace PDF_umbenenen
{
    public partial class InstallWizardWindow : Window
    {
        private int _step;
        private bool _isRunning;

        public InstallWizardWindow()
        {
            InitializeComponent();
            ThemeWindowService.Attach(this);
            Icon = ThemeWindowService.GetAppIcon();

            WelcomeVersionTextBlock.Text = "Version " + AppInfo.DisplayVersion + " · Build " + AppInfo.BuildDate;
            WelcomeCopyrightTextBlock.Text = LicenseContentService.GetCopyrightSummary();
            FooterCopyrightTextBlock.Text = LicenseContentService.GetCopyrightSummary();
            LicenseTextBlock.Text = LicenseContentService.LoadLicenseText();
            TargetPathTextBox.Text = AppInfo.DefaultInstallDirectory;
            UserDataPathTextBox.Text = System.IO.Path.Combine(AppInfo.UserDataDirectory, "Ablage");

            Loaded += InstallWizardWindow_Loaded;
            ShowStep(0);
        }

        private async void InstallWizardWindow_Loaded(object sender, RoutedEventArgs e)
        {
            if (!InstallSessionStore.TryConsumePending(out var pending))
                return;

            _isRunning = true;
            ShowStep(3);
            await RunInstallAsync(pending).ConfigureAwait(true);
        }

        private void ShowStep(int step)
        {
            _step = step;
            WelcomePanel.Visibility = step == 0 ? Visibility.Visible : Visibility.Collapsed;
            LicensePanel.Visibility = step == 1 ? Visibility.Visible : Visibility.Collapsed;
            OptionsPanel.Visibility = step == 2 ? Visibility.Visible : Visibility.Collapsed;
            ProgressPanel.Visibility = step == 3 ? Visibility.Visible : Visibility.Collapsed;
            FinishPanel.Visibility = step == 4 ? Visibility.Visible : Visibility.Collapsed;

            BackButton.IsEnabled = step > 0 && step < 3 && !_isRunning;
            NextButton.Visibility = step < 2 ? Visibility.Visible : Visibility.Collapsed;
            InstallButton.Visibility = step == 2 ? Visibility.Visible : Visibility.Collapsed;
            CloseButton.Visibility = step == 4 ? Visibility.Visible : Visibility.Collapsed;
            CancelButton.IsEnabled = !_isRunning && step < 4;

            switch (step)
            {
                case 0:
                    StepTitleTextBlock.Text = "Schritt 1 von 4 – Willkommen";
                    break;
                case 1:
                    StepTitleTextBlock.Text = "Schritt 2 von 4 – Lizenz & Urheberrecht";
                    break;
                case 2:
                    StepTitleTextBlock.Text = "Schritt 3 von 4 – Installationsoptionen";
                    break;
                case 3:
                    StepTitleTextBlock.Text = "Schritt 4 von 4 – Installation";
                    break;
                default:
                    StepTitleTextBlock.Text = "Abgeschlossen";
                    break;
            }
        }

        private void BackButton_Click(object sender, RoutedEventArgs e)
        {
            if (_step > 0)
                ShowStep(_step - 1);
        }

        private void NextButton_Click(object sender, RoutedEventArgs e)
        {
            if (_step == 1 && AcceptLicenseCheckBox.IsChecked != true)
            {
                MessageBox.Show(this, "Bitte akzeptieren Sie die Lizenz- und Urheberrechtsbedingungen.", Title, MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (_step < 2)
            {
                ShowStep(_step + 1);
                return;
            }
        }

        private async void InstallButton_Click(object sender, RoutedEventArgs e)
        {
            if (AcceptLicenseCheckBox.IsChecked != true)
            {
                MessageBox.Show(this, "Bitte akzeptieren Sie zuerst die Lizenzbedingungen.", Title, MessageBoxButton.OK, MessageBoxImage.Warning);
                ShowStep(1);
                return;
            }

            var options = new InstallSessionOptions
            {
                LicenseAccepted = true,
                InstallPublisherCertificate = InstallCertificateCheckBox.IsChecked == true,
                LaunchAfterInstall = LaunchAfterInstallCheckBox.IsChecked == true,
                SourceDirectory = InstallationService.GetSourceDirectory()
            };

            if (!AdminElevationService.IsRunningAsAdministrator())
            {
                InstallSessionStore.SavePending(options);
                if (!AdminElevationService.TryRelaunchAsAdministrator())
                {
                    MessageBox.Show(this, "Administratorfreigabe abgebrochen.", Title, MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                Close();
                return;
            }

            _isRunning = true;
            ShowStep(3);
            await RunInstallAsync(options).ConfigureAwait(true);
        }

        private async Task RunInstallAsync(InstallSessionOptions options)
        {
            var progress = new Progress<string>(message => ProgressStatusTextBlock.Text = message);
            var result = await Task.Run(() => InstallationService.RunInstall(
                options.SourceDirectory,
                options.InstallPublisherCertificate,
                options.LaunchAfterInstall,
                progress)).ConfigureAwait(true);

            _isRunning = false;
            if (!result.Success)
            {
                MessageBox.Show(this, result.Message, Title, MessageBoxButton.OK, MessageBoxImage.Error);
                ShowStep(2);
                return;
            }

            FinishMessageTextBlock.Text =
                "DOK-V01 wurde installiert unter:\n" + AppInfo.DefaultInstallDirectory
                + "\n\nArbeitsdaten:\n" + System.IO.Path.Combine(AppInfo.UserDataDirectory, "Ablage")
                + "\n\nBitte starten Sie die Anwendung künftig über die Desktop-Verknüpfung.";
            ShowStep(4);
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
