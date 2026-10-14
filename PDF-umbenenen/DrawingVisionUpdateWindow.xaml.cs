using System;
using System.Threading;
using System.Windows;
using PDF_umbenenen.Models;
using PDF_umbenenen.Services;

namespace PDF_umbenenen
{
    public partial class DrawingVisionUpdateWindow : Window
    {
        private readonly DrawingVisionUpdateInfo _info;
        private bool _isUpdating;
        private CancellationTokenSource _updateCts;

        public bool Updated { get; private set; }

        public bool Postponed { get; private set; }

        public DrawingVisionUpdateWindow(DrawingVisionUpdateInfo info)
        {
            InitializeComponent();
            ThemeWindowService.Attach(this);
            _info = info ?? new DrawingVisionUpdateInfo();

            var installed = string.IsNullOrWhiteSpace(_info.InstalledVersion)
                ? "nicht gefunden"
                : _info.InstalledVersion;
            var latest = string.IsNullOrWhiteSpace(_info.LatestVersion)
                ? "unbekannt"
                : _info.LatestVersion;

            VersionTextBlock.Text = "Installiert: " + installed + "   →   Neu: " + latest;
            NotesTextBlock.Text =
                "Eingebautes Modul: " + (_info.ModuleDisplayName ?? "Ollama · moondream") + "\n\n"
                + "Die Zeichnungs-Vision kann unabhängig von DOK aktualisiert werden.\n"
                + "Es wird nur das Bildmodell in AI_DOK-V01 ersetzt (Ollama-Runtime bleibt).\n\n"
                + "Quelle: ollama.com (HTTPS). Download über den lokalen Ollama-Dienst.";
            Closing += DrawingVisionUpdateWindow_Closing;
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
            DownloadSizeTextBlock.Text = "Update-Größe: ca. 1,7 GB";
            StatusTextBlock.Text = "Zeichnungs-Vision-Update wird vorbereitet…";

            try
            {
                var progress = new Progress<UpdateProgressInfo>(info =>
                {
                    UpdateProgressBar.Value = info.Percent;
                    PercentTextBlock.Text = info.Percent + " %";
                    StatusTextBlock.Text = info.Message;
                });

                await DrawingVisionUpdateCheckService.UpdateModelAsync(
                    _info,
                    progress,
                    _updateCts.Token).ConfigureAwait(true);

                Updated = true;
                StatusTextBlock.Text = "Zeichnungs-Vision wurde aktualisiert.";
                PercentTextBlock.Text = "100 %";
                UpdateProgressBar.Value = 100;
                MessageBox.Show(
                    this,
                    "Zeichnungs-Vision wurde aktualisiert:\n" + (_info.ModuleDisplayName ?? "Ollama · moondream"),
                    "Zeichnungs-Vision-Update",
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
                MessageBox.Show(this, ex.Message, "Zeichnungs-Vision-Update", MessageBoxButton.OK, MessageBoxImage.Warning);
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

        private void DrawingVisionUpdateWindow_Closing(object sender, System.ComponentModel.CancelEventArgs e)
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
