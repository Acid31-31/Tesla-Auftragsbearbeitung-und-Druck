using System.Windows;
using PDF_umbenenen.Platform.UI;

namespace PDF_umbenenen
{
    public partial class AppSettingsWindow : Window
    {
        private readonly MainWindow _mainWindow;

        public AppSettingsWindow(MainWindow mainWindow)
        {
            InitializeComponent();
            _mainWindow = mainWindow;
            UpdateThemeHint();
        }

        private void UpdateThemeHint()
        {
            if (_mainWindow == null)
                return;

            var isDark = _mainWindow.IsDarkThemeActive;
            ThemeIconTextBlock.Text = isDark ? "🌙" : "☀";
            ThemeHintTextBlock.Text = isDark
                ? "Dunkles Design ( aktiv ) — klicken für Hell"
                : "Helles Design ( aktiv ) — klicken für Dunkel";
        }

        private void OpenAiSettings_Click(object sender, RoutedEventArgs e)
        {
            _mainWindow?.OpenAiSettingsDialog();
        }

        private void OpenWatermarkSettings_Click(object sender, RoutedEventArgs e)
        {
            _mainWindow?.OpenWatermarkSettingsDialog();
        }

        private void OpenCorrectWatermark_Click(object sender, RoutedEventArgs e)
        {
            Close();
            _mainWindow?.OpenCorrectWatermarkDialog();
        }

        private void OpenWorkspaceFolder_Click(object sender, RoutedEventArgs e)
        {
            _mainWindow?.OpenWorkspaceFolderFromUi();
        }

        private void OpenManageCustomers_Click(object sender, RoutedEventArgs e)
        {
            _mainWindow?.OpenManageCustomersDialog();
        }

        private void OpenGeoSettings_Click(object sender, RoutedEventArgs e)
        {
            var win = new GeoSettingsWindow { Owner = this };
            ThemeWindowService.Attach(win);
            win.ShowDialog();
        }

        private void CheckUpdates_Click(object sender, RoutedEventArgs e)
        {
            _mainWindow?.CheckForUpdatesFromUi();
        }

        private void ToggleTheme_Click(object sender, RoutedEventArgs e)
        {
            _mainWindow?.ToggleThemeFromSettings();
            UpdateThemeHint();
        }

        private void OpenPlatformDashboard_Click(object sender, RoutedEventArgs e)
        {
            Close();
            var dashboard = new PlatformDashboardWindow
            {
                Owner = _mainWindow,
                WindowStartupLocation = WindowStartupLocation.CenterOwner
            };
            ThemeWindowService.Attach(dashboard);
            dashboard.Show();
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
