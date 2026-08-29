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
                : FormatReleaseNotesForDisplay(_update.ReleaseNotes);
        }

        private static string FormatReleaseNotesForDisplay(string notes)
        {
            if (string.IsNullOrWhiteSpace(notes))
                return string.Empty;

            var text = notes.Trim();
            if (text.Length > 0 && text[0] == '\uFEFF')
                text = text.Substring(1).TrimStart();
            // Falls alte Clients/APIs noch Escapes liefern.
            if (text.IndexOf("\\n", StringComparison.Ordinal) >= 0
                && text.IndexOf('\n') < 0)
            {
                text = text
                    .Replace("\\r\\n", "\n")
                    .Replace("\\n", "\n")
                    .Replace("\\r", "\n")
                    .Replace("\\t", "  ");
            }

            var lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            var sb = new System.Text.StringBuilder();
            foreach (var raw in lines)
            {
                var line = raw ?? string.Empty;
                if (line.StartsWith("### ", StringComparison.Ordinal))
                    line = line.Substring(4).Trim();
                else if (line.StartsWith("## ", StringComparison.Ordinal))
                    line = line.Substring(3).Trim();
                else if (line.StartsWith("# ", StringComparison.Ordinal))
                    line = line.Substring(2).Trim();
                else if (line.StartsWith("- ", StringComparison.Ordinal) || line.StartsWith("* ", StringComparison.Ordinal))
                    line = "• " + line.Substring(2).Trim();

                sb.AppendLine(line);
            }

            return sb.ToString().Trim();
        }

        private async void UpdateButton_Click(object sender, RoutedEventArgs e)
        {
            if (_isUpdating)
                return;

            _isUpdating = true;
            UpdateButton.IsEnabled = false;
            LaterButton.IsEnabled = false;
            NotesBorder.Visibility = Visibility.Collapsed;
            ProgressPanel.Visibility = Visibility.Visible;
            UpdateProgressBar.Value = 0;
            PercentTextBlock.Text = "0 %";
            StatusTextBlock.Text = "Update wird vorbereitet…";

            try
            {
                var progress = new Progress<UpdateProgressInfo>(info =>
                {
                    UpdateProgressBar.Value = info.Percent;
                    PercentTextBlock.Text = info.Percent + " %";
                    StatusTextBlock.Text = info.Message;
                });

                var stagedRoot = await GitHubUpdateService.DownloadAndStageUpdateAsync(
                    _update,
                    progress,
                    CancellationToken.None).ConfigureAwait(true);

                StatusTextBlock.Text = "Installation wird gestartet…";
                PercentTextBlock.Text = "100 %";
                UpdateProgressBar.Value = 100;
                GitHubUpdateService.LaunchUpdaterAndShutdown(stagedRoot);
            }
            catch (Exception ex)
            {
                _isUpdating = false;
                UpdateButton.IsEnabled = true;
                LaterButton.IsEnabled = true;
                NotesBorder.Visibility = Visibility.Visible;
                ProgressPanel.Visibility = Visibility.Collapsed;
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
