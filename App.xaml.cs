using System;
using System.Windows;

namespace PDF_umbenenen
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            if (Services.UpdateApplyRunner.TryHandle(e.Args ?? Array.Empty<string>()))
            {
                Shutdown();
                return;
            }

            base.OnStartup(e);
            Services.AppSecurityService.Initialize();
            Services.GitHubUpdateService.EnsureUpdateTokenFromInstallFolder();

            if (!Services.AppSecurityService.EnforceStartupSecurity())
            {
                Shutdown();
                return;
            }

            if (Services.UsbInstallService.IsUsbUninstallerLaunch())
            {
                var uninstallWizard = new UninstallWizardWindow();
                uninstallWizard.ShowDialog();
                Shutdown();
                return;
            }

            if (Services.UsbInstallService.IsUsbInstallerLaunch())
            {
                var installWizard = new InstallWizardWindow();
                installWizard.ShowDialog();
                Shutdown();
                return;
            }

            var demoStatus = Services.DemoLicenseService.Evaluate();
            if (demoStatus.IsExpired)
            {
                var expiredWindow = new DemoExpiredWindow();
                expiredWindow.ShowDialog();
                Shutdown();
                return;
            }

            if (Services.DemoLicenseService.ShouldShowWelcome())
            {
                MessageBox.Show(
                    "Willkommen bei der Demoversion von " + AppInfo.ProductName + ".\n\n"
                    + "Die Demo ist " + AppInfo.DemoPeriodDays + " Tage ab dem ersten Start gültig"
                    + " (bis " + demoStatus.ExpiresLocal.ToString("dd.MM.yyyy") + ").",
                    AppInfo.ProductName + " – Demoversion",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                Services.DemoLicenseService.MarkWelcomeShown();
            }

            var mainWindow = new MainWindow();
            MainWindow = mainWindow;
            mainWindow.Show();
        }
    }
}
