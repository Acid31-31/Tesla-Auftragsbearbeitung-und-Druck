using System;
using System.Windows;

namespace PDF_umbenenen
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            if (Services.UpdateApplyRunner.TryParseApplyUpdateArgs(
                    e.Args ?? Array.Empty<string>(),
                    out var stagedRoot,
                    out var targetRoot,
                    out var parentProcessId))
            {
                var applyWindow = new UpdateApplyProgressWindow(stagedRoot, targetRoot, parentProcessId);
                applyWindow.ShowDialog();
                Shutdown();
                return;
            }

            base.OnStartup(e);
            Services.AppSecurityService.Initialize();
            Services.NativeDependencyService.TryRepairAndReload(out _);
            Services.PdfRasterLayoutService.ResetAvailability();
            if (Services.PdfRasterLayoutService.IsAvailable)
            {
                Services.WatermarkLearningStore.PruneInvalidCategoryDefaults();
                Services.WatermarkLearningStore.PurgeNonLearnableCategoryDefaults();
            }
            Platform.Core.PlatformHost.Initialize();
            Services.TesseractBootstrapService.ScheduleStartupBootstrap();
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

            if (!Services.UsbInstallService.EnforceInstalledExecution())
            {
                Shutdown();
                return;
            }

            if (AppInfo.IsRegisteredInstallDirectory(Services.UsbInstallService.GetApplicationDirectory())
                && !Services.CertificateTrustService.IsPublisherTrustedFromBundledCertificate())
            {
                Services.CertificateTrustService.TryInstallPublisherCertificateWithConsent();
            }

            var demoStatus = Services.DemoLicenseService.Evaluate();
            if (demoStatus.IsExpired)
            {
                var expiredWindow = new DemoExpiredWindow();
                var result = expiredWindow.ShowDialog();
                if (result != true && !expiredWindow.ActivatedFullVersion
                    && !Services.LicenseActivationService.IsActivated())
                {
                    Shutdown();
                    return;
                }

                demoStatus = Services.DemoLicenseService.Evaluate();
                if (demoStatus.IsExpired)
                {
                    Shutdown();
                    return;
                }
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
            mainWindow.Closed += (_, __) => Platform.Core.PlatformHost.Shutdown();
            mainWindow.Show();
        }
    }
}
