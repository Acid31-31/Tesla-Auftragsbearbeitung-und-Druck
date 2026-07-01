using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using PDF_umbenenen.Models;
using PDF_umbenenen.Services;

namespace PDF_umbenenen
{
    public partial class UpdateAvailableWindow : Window
    {
        private readonly AppUpdateInfo _update;
        private bool _isUpdating;

        public UpdateAvailableWindow(AppUpdateInfo update)
        {
            InitializeComponent();
            ThemeWindowService.Attach(this);

            _update = update ?? new AppUpdateInfo();
            VersionTextBlock.Text = string.Format(
                "Installiert: {0}   →   Neu: {1}",
                AppInfo.DisplayVersion,
                string.IsNullOrWhiteSpace(_update.ReleaseTag) ? "unbekannt" : _update.ReleaseTag);

            NotesTextBlock.Text = string.IsNullOrWhiteSpace(_update.ReleaseNotes)
                ? "Keine Release-Notizen."
                : _update.ReleaseNotes.Trim();
        }

        private async void UpdateButton_Click(object sender, RoutedEventArgs e)
        {
            if (_isUpdating)
                return;

            _isUpdating = true;
            UpdateButton.IsEnabled = false;
            LaterButton.IsEnabled = false;
            StatusTextBlock.Text = "Update wird vorbereitet…";
            Mouse.OverrideCursor = Cursors.Wait;

            try
            {
                var progress = new Progress<string>(message =>
                {
                    Dispatcher.Invoke(() => StatusTextBlock.Text = message);
                });
                var stagedRoot = await GitHubUpdateService.DownloadAndStageUpdateAsync(
                    _update,
                    progress,
                    CancellationToken.None).ConfigureAwait(true);

                StatusTextBlock.Text = "Update wird installiert. Die App startet neu…";
                GitHubUpdateService.LaunchUpdaterAndShutdown(stagedRoot);
            }
            catch (Exception ex)
            {
                _isUpdating = false;
                UpdateButton.IsEnabled = true;
                LaterButton.IsEnabled = true;
                Mouse.OverrideCursor = null;
                StatusTextBlock.Text = "Update fehlgeschlagen.";
                MessageBox.Show(this, ex.Message, "Update", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void LaterButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
