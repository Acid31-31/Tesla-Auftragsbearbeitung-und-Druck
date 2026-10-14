using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Forms;
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
            TargetPathTextBox.Text = AppInfo.GetSuggestedInstallDirectory();
            UpdateWorkspacePreview();

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

        private void UpdateWorkspacePreview()
        {
            var target = (TargetPathTextBox.Text ?? string.Empty).Trim();
            UserDataPathTextBox.Text = string.IsNullOrWhiteSpace(target)
                ? string.Empty
                : Path.Combine(target, "Ablage");
        }

        private void BrowseTargetFolder_Click(object sender, RoutedEventArgs e)
        {
            using (var dialog = new FolderBrowserDialog())
            {
                dialog.Description = "Installationsordner für DOK-V01 wählen";
                dialog.ShowNewFolderButton = true;
                if (!string.IsNullOrWhiteSpace(TargetPathTextBox.Text))
                    dialog.SelectedPath = TargetPathTextBox.Text;

                if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK
                    && !string.IsNullOrWhiteSpace(dialog.SelectedPath))
                {
                    TargetPathTextBox.Text = dialog.SelectedPath;
                    UpdateWorkspacePreview();
                }
            }
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
                System.Windows.MessageBox.Show(this, "Bitte akzeptieren Sie die Lizenz- und Urheberrechtsbedingungen.", Title, MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (_step < 2)
            {
                ShowStep(_step + 1);
            }
        }

        private async void InstallButton_Click(object sender, RoutedEventArgs e)
        {
            if (AcceptLicenseCheckBox.IsChecked != true)
            {
                System.Windows.MessageBox.Show(this, "Bitte akzeptieren Sie zuerst die Lizenzbedingungen.", Title, MessageBoxButton.OK, MessageBoxImage.Warning);
                ShowStep(1);
                return;
            }

            var targetDirectory = (TargetPathTextBox.Text ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(targetDirectory))
            {
                System.Windows.MessageBox.Show(this, "Bitte wählen Sie einen Installationsordner.", Title, MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var options = new InstallSessionOptions
            {
                LicenseAccepted = true,
                InstallPublisherCertificate = InstallCertificateCheckBox.IsChecked == true,
                LaunchAfterInstall = LaunchAfterInstallCheckBox.IsChecked == true,
                InstallTesseractOcr = InstallTesseractCheckBox.IsChecked == true,
                SourceDirectory = InstallationService.GetSourceDirectory(),
                TargetDirectory = targetDirectory
            };

            _isRunning = true;
            ShowStep(3);
            await RunInstallAsync(options).ConfigureAwait(true);
        }

        private async Task RunInstallAsync(InstallSessionOptions options)
        {
            var progress = new Progress<string>(message => ProgressStatusTextBlock.Text = message);
            var result = await Task.Run(() => InstallationService.RunInstall(
                options.SourceDirectory,
                options.TargetDirectory,
                options.InstallPublisherCertificate,
                options.LaunchAfterInstall,
                options.InstallTesseractOcr,
                progress)).ConfigureAwait(true);

            _isRunning = false;
            if (!result.Success)
            {
                System.Windows.MessageBox.Show(this, result.Message, Title, MessageBoxButton.OK, MessageBoxImage.Error);
                ShowStep(2);
                return;
            }

            var installDir = Path.GetFullPath(options.TargetDirectory);
            var tessLine = string.IsNullOrWhiteSpace(result.TesseractStatus)
                ? string.Empty
                : "\n\nTesseract OCR: " + result.TesseractStatus;
            FinishMessageTextBlock.Text =
                "DOK-V01 wurde installiert unter:\n" + installDir
                + "\n\nAblage und Einstellungen:\n" + Path.Combine(installDir, "Ablage")
                + tessLine
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
