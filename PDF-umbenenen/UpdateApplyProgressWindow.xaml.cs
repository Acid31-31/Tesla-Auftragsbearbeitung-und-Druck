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

        public UpdateApplyProgressWindow(string stagedRoot, string targetRoot, int parentProcessId = 0)
        {
            InitializeComponent();
            ThemeWindowService.Attach(this);

            _stagedRoot = stagedRoot;
            _targetRoot = targetRoot;
            _parentProcessId = parentProcessId;
            Loaded += UpdateApplyProgressWindow_Loaded;
        }

        private async void UpdateApplyProgressWindow_Loaded(object sender, RoutedEventArgs e)
        {
            Loaded -= UpdateApplyProgressWindow_Loaded;

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
                        CancellationToken.None,
                        _parentProcessId),
                    CancellationToken.None).ConfigureAwait(true);

                DialogResult = true;
                Close();
            }
            catch (Exception ex)
            {
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
    }
}
