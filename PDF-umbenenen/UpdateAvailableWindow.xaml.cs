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
        private CancellationTokenSource _updateCts;

        public bool Postponed { get; private set; }

        public UpdateAvailableWindow(AppUpdateInfo update)
        {
            InitializeComponent();
            ThemeWindowService.Attach(this);

            _update = update ?? new AppUpdateInfo();

            var sizeText = _update.AssetSizeBytes > 0
                ? _update.AssetSizeDisplay
                : string.Empty;

            VersionTextBlock.Text = string.Format(
                "Installiert: {0}   →   Neu: {1}",
                AppInfo.DisplayVersion,
                string.IsNullOrWhiteSpace(_update.ReleaseTag) ? "unbekannt" : _update.ReleaseTag);

            SizeTextBlock.Text = string.IsNullOrWhiteSpace(sizeText)
                ? "Update-Größe: wird beim Download angezeigt"
                : "Update-Größe: " + sizeText;

            var notes = string.IsNullOrWhiteSpace(_update.ReleaseNotes)
                ? "Keine Release-Notizen."
                : FormatReleaseNotesForDisplay(_update.ReleaseNotes);

            if (!string.IsNullOrWhiteSpace(sizeText)
                && notes.IndexOf(sizeText, StringComparison.OrdinalIgnoreCase) < 0)
            {
                notes = "Update-Größe: " + sizeText + Environment.NewLine + Environment.NewLine + notes;
            }

            NotesTextBlock.Text = notes;
            Closing += UpdateAvailableWindow_Closing;
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
            _updateCts = new CancellationTokenSource();
            UpdateButton.IsEnabled = false;
            LaterButton.IsEnabled = true;
            LaterButton.Content = "Abbrechen – später";
            NotesBorder.Visibility = Visibility.Collapsed;
            ProgressPanel.Visibility = Visibility.Visible;
            UpdateProgressBar.Value = 0;
            PercentTextBlock.Text = "0 %";
            DownloadSizeTextBlock.Text = string.IsNullOrWhiteSpace(_update.AssetSizeDisplay)
                ? "Update-Größe: wird geladen…"
                : "Update-Größe: " + _update.AssetSizeDisplay;
            StatusTextBlock.Text = "Update wird vorbereitet…";

            try
            {
                var progress = new Progress<UpdateProgressInfo>(info =>
                {
                    UpdateProgressBar.Value = info.Percent;
                    PercentTextBlock.Text = info.Percent + " %";
                    StatusTextBlock.Text = info.Message;
                    if (info.TotalBytes > 0)
                    {
                        DownloadSizeTextBlock.Text = AppUpdateInfo.FormatMegabytes(info.BytesRead)
                            + " von " + AppUpdateInfo.FormatMegabytes(info.TotalBytes);
                    }
                    else if (info.BytesRead > 0)
                    {
                        DownloadSizeTextBlock.Text = AppUpdateInfo.FormatMegabytes(info.BytesRead) + " geladen";
                    }
                });

                var stagedRoot = await GitHubUpdateService.DownloadAndStageUpdateAsync(
                    _update,
                    progress,
                    _updateCts.Token).ConfigureAwait(true);

                StatusTextBlock.Text = "Installation wird gestartet…";
                PercentTextBlock.Text = "100 %";
                UpdateProgressBar.Value = 100;
                GitHubUpdateService.LaunchUpdaterAndShutdown(stagedRoot);
            }
            catch (OperationCanceledException)
            {
                ResetAfterCancel();
                Postponed = true;
                DialogResult = false;
                Close();
            }
            catch (Exception ex)
            {
                _isUpdating = false;
                DisposeUpdateCts();
                UpdateButton.IsEnabled = true;
                LaterButton.IsEnabled = true;
                LaterButton.Content = "Später";
                NotesBorder.Visibility = Visibility.Visible;
                ProgressPanel.Visibility = Visibility.Collapsed;
                StatusTextBlock.Text = "Update fehlgeschlagen.";
                MessageBox.Show(this, ex.Message, "Update", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void LaterButton_Click(object sender, RoutedEventArgs e)
        {
            if (_isUpdating)
            {
                RequestCancelUpdate();
                return;
            }

            Postponed = true;
            DialogResult = false;
            Close();
        }

        private void UpdateAvailableWindow_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            if (!_isUpdating)
                return;

            e.Cancel = true;
            RequestCancelUpdate();
        }

        private void RequestCancelUpdate()
        {
            try
            {
                _updateCts?.Cancel();
            }
            catch
            {
            }

            LaterButton.IsEnabled = false;
            if (StatusTextBlock != null)
                StatusTextBlock.Text = "Update wird abgebrochen…";
        }

        private void ResetAfterCancel()
        {
            _isUpdating = false;
            DisposeUpdateCts();
        }

        private void DisposeUpdateCts()
        {
            try
            {
                _updateCts?.Dispose();
            }
            catch
            {
            }

            _updateCts = null;
        }
    }
}
