using System;
using System.IO;
using System.Reflection;

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
    }
}
