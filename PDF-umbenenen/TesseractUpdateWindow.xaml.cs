using System;
using System.Threading;
using System.Windows;
using PDF_umbenenen.Models;
using PDF_umbenenen.Services;

namespace PDF_umbenenen
{
    public partial class TesseractUpdateWindow : Window
    {
        private readonly TesseractUpdateInfo _info;
        private bool _isUpdating;
        private CancellationTokenSource _updateCts;

        public bool Updated { get; private set; }

        public bool Postponed { get; private set; }

        public TesseractUpdateWindow(TesseractUpdateInfo info)
        {
            InitializeComponent();
            ThemeWindowService.Attach(this);
            _info = info ?? new TesseractUpdateInfo();

            var installed = string.IsNullOrWhiteSpace(_info.InstalledVersion)
                ? "unbekannt"
                : _info.InstalledVersion;
            var latest = string.IsNullOrWhiteSpace(_info.LatestVersion)
                ? "unbekannt"
                : _info.LatestVersion;

            VersionTextBlock.Text = "Installiert: " + installed + "   →   Neu: " + latest;
            NotesTextBlock.Text =
                "Tesseract kann unabhängig von DOK-Updates aktualisiert werden.\n\n"
                + "Download-Größe: ca. 50 MB\n\n"
                + "Die neue Version wird in den Benutzerordner installiert (kein Administrator nötig). "
                + "DOK verwendet danach diese Version.\n\n"
                + "Quelle: UB Mannheim (HTTPS). Der Installer wird vor der Installation auf eine gültige Signatur geprüft.";
            Closing += TesseractUpdateWindow_Closing;
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
            DownloadSizeTextBlock.Text = "Update-Größe: ca. 50 MB";
            StatusTextBlock.Text = "Tesseract-Update wird vorbereitet…";

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

                var path = await TesseractInstallService.UpdateToLatestAsync(
                    _info,
                    progress,
                    _updateCts.Token).ConfigureAwait(true);

                Updated = true;
                StatusTextBlock.Text = "Tesseract wurde aktualisiert.";
                PercentTextBlock.Text = "100 %";
                UpdateProgressBar.Value = 100;
                MessageBox.Show(
                    this,
                    "Tesseract wurde aktualisiert:\n" + path,
                    "Tesseract-Update",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                DialogResult = true;
                Close();
            }
            catch (OperationCanceledException)
            {
                _isUpdating = false;
                DisposeUpdateCts();
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
                MessageBox.Show(this, ex.Message, "Tesseract-Update", MessageBoxButton.OK, MessageBoxImage.Warning);
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

        private void TesseractUpdateWindow_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            if (!_isUpdating)
                return;

            e.Cancel = true;
            RequestCancelUpdate();
        }

        private void RequestCancelUpdate()
        {
            try { _updateCts?.Cancel(); } catch { }
            LaterButton.IsEnabled = false;
            if (StatusTextBlock != null)
                StatusTextBlock.Text = "Update wird abgebrochen…";
        }

        private void DisposeUpdateCts()
        {
            try { _updateCts?.Dispose(); } catch { }
            _updateCts = null;
        }
    }
}
