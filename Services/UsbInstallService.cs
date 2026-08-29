using System;
using System.IO;
using System.Reflection;
using System.Windows;

namespace PDF_umbenenen.Services
{
    internal static class UsbInstallService
    {
        public static bool IsUsbInstallerLaunch()
        {
            var fileName = Path.GetFileName(Assembly.GetExecutingAssembly().Location);
            return fileName.Equals(AppInfo.UsbLauncherFileName, StringComparison.OrdinalIgnoreCase);
        }

        public static bool IsUsbUninstallerLaunch()
        {
            var fileName = Path.GetFileName(Assembly.GetExecutingAssembly().Location);
            return fileName.Equals(AppInfo.UsbUninstallerFileName, StringComparison.OrdinalIgnoreCase);
        }

        public static bool IsWizardLaunch()
        {
            return IsUsbInstallerLaunch() || IsUsbUninstallerLaunch();
        }

        /// <summary>
        /// Release: normale Nutzung nur aus dem registrierten Installationsordner.
        /// </summary>
        public static bool EnforceInstalledExecution()
        {
#if DEBUG
            return true;
#else
            if (IsWizardLaunch())
                return true;

            var baseDir = GetApplicationDirectory();
            if (AppInfo.IsRegisteredInstallDirectory(baseDir))
            {
                InstallRegistrationService.EnsureRegisteredInstall(baseDir);
                if (!InstallRegistrationService.IsRegisteredInstall(baseDir))
                {
                    ShowInstallRegistrationInvalidMessage();
                    return false;
                }

                return true;
            }

            ShowPortableLaunchBlockedMessage();
            return false;
#endif
        }

        public static string GetApplicationDirectory()
        {
            return AppInfo.GetApplicationDirectory();
        }

        private static void ShowPortableLaunchBlockedMessage()
        {
            MessageBox.Show(
                AppInfo.ProductName + " darf nicht direkt vom USB-Stick, aus Downloads oder als lose Ordnerkopie gestartet werden.\n\n"
                + "Bitte einmalig installieren:\n"
                + "1. USB-Ordner \"" + AppInfo.ProductName + "\" öffnen\n"
                + "2. \"" + AppInfo.UsbLauncherFileName + "\" starten\n"
                + "3. Installationsordner wählen (z. B. Dokumente\\" + AppInfo.ProductName + ")\n"
                + "4. Danach nur die Desktop-Verknüpfung oder die installierte EXE nutzen\n\n"
                + "Programm und Arbeitsdaten liegen danach in einem gemeinsamen Ordner.",
                AppInfo.ProductName + " – Installation erforderlich",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }

        private static void ShowInstallRegistrationInvalidMessage()
        {
            MessageBox.Show(
                "Die Installation von " + AppInfo.ProductName + " ist beschädigt oder wurde auf einen anderen PC kopiert.\n\n"
                + "Bitte \"" + AppInfo.UsbLauncherFileName + "\" erneut ausführen oder "
                + "\"" + AppInfo.UsbUninstallerFileName + "\" und danach neu installieren.",
                AppInfo.ProductName + " – Installation ungültig",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }
}
