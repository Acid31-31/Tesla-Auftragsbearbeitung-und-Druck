using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using PDF_umbenenen.Models;
using PDF_umbenenen.Services;

namespace PDF_umbenenen
{
    public partial class UpdateApplyProgressWindow : Window
    {
        private readonly string _stagedRoot;
        private readonly string _targetRoot;
        private readonly int _parentProcessId;
        private CancellationTokenSource _cts;
        private bool _isApplying;

        public UpdateApplyProgressWindow(string stagedRoot, string targetRoot, int parentProcessId = 0)
        {
            InitializeComponent();
            ThemeWindowService.Attach(this);

            _stagedRoot = stagedRoot;
            _targetRoot = targetRoot;
            _parentProcessId = parentProcessId;
            Loaded += UpdateApplyProgressWindow_Loaded;
            Closing += UpdateApplyProgressWindow_Closing;
        }

        private async void UpdateApplyProgressWindow_Loaded(object sender, RoutedEventArgs e)
        {
            Loaded -= UpdateApplyProgressWindow_Loaded;
            _cts = new CancellationTokenSource();
            _isApplying = true;

            var progress = new Progress<UpdateProgressInfo>(info =>
            {
                InstallProgressBar.Value = info.Percent;
                PercentTextBlock.Text = info.Percent + " %";
                StatusTextBlock.Text = info.Message;
            });

            try
            {
                await Task.Run(
                    () => UpdateApplyRunner.ApplyUpdate(
                        _stagedRoot,
                        _targetRoot,
                        progress,
                        _cts.Token,
                        _parentProcessId),
                    _cts.Token).ConfigureAwait(true);

                _isApplying = false;
                DialogResult = true;
                Close();
            }
            catch (OperationCanceledException)
            {
                _isApplying = false;
                DialogResult = false;
                Close();
            }
            catch (AggregateException ex)
            {
                _isApplying = false;
                if (ex.GetBaseException() is OperationCanceledException)
                {
                    DialogResult = false;
                    Close();
                    return;
                }

                MessageBox.Show(
                    this,
                    ex.GetBaseException().Message,
                    "Update fehlgeschlagen",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                DialogResult = false;
                Close();
            }
            catch (Exception ex)
            {
                _isApplying = false;
                MessageBox.Show(
                    this,
                    ex.Message,
                    "Update fehlgeschlagen",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                DialogResult = false;
                Close();
            }
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            RequestCancel();
        }

        private void UpdateApplyProgressWindow_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            if (!_isApplying)
                return;

            e.Cancel = true;
            RequestCancel();
        }

        private void RequestCancel()
        {
            try { _cts?.Cancel(); } catch { }
            if (CancelButton != null)
                CancelButton.IsEnabled = false;
            if (StatusTextBlock != null)
                StatusTextBlock.Text = "Update wird abgebrochen…";
        }
    }
}
