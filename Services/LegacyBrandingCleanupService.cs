using System;
using System.IO;

namespace PDF_umbenenen.Services
{
    internal static class LegacyBrandingCleanupService
    {
        private const string LegacyDesktopShortcutName = "Tesla Auftragsbearbeitung & Druck.lnk";
        private const string LegacyInstallFolderName = "Tesla-Auftragsbearbeitung-und-Druck";
        private const string LegacyUsbFolderName = "DOK-V01_Legacy";

        public static LegacyCleanupResult Cleanup()
        {
            var result = new LegacyCleanupResult();

            try
            {
                var desktopShortcut = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
                    LegacyDesktopShortcutName);
                if (File.Exists(desktopShortcut))
                {
                    File.Delete(desktopShortcut);
                    result.RemovedDesktopShortcut = true;
                }
            }
            catch
            {
                result.Errors++;
            }

            try
            {
                var legacyInstallDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    LegacyInstallFolderName);
                if (Directory.Exists(legacyInstallDir))
                {
                    Directory.Delete(legacyInstallDir, true);
                    result.RemovedLegacyInstall = true;
                }
            }
            catch
            {
                result.Errors++;
            }

            // Optional: alte mitgelieferte USB-Ordnerbezeichnung aufräumen (nur lokal im App-Verzeichnis).
            try
            {
                var baseDir = AppDomain.CurrentDomain.BaseDirectory;
                var usbLegacyDir = Path.Combine(baseDir, "..", "USB-Version", LegacyUsbFolderName);
                usbLegacyDir = Path.GetFullPath(usbLegacyDir);
                if (Directory.Exists(usbLegacyDir))
                {
                    // Nicht löschen – nur umbenennen/aus dem Weg räumen wäre zu riskant. Wir lassen es hier bewusst unangetastet.
                }
            }
            catch
            {
                // Ignorieren
            }

            return result;
        }
    }

    internal sealed class LegacyCleanupResult
    {
        public bool RemovedDesktopShortcut { get; set; }
        public bool RemovedLegacyInstall { get; set; }
        public int Errors { get; set; }

        public bool Changed => RemovedDesktopShortcut || RemovedLegacyInstall;
    }
}
