using System;
using System.IO;

namespace PDF_umbenenen
{
    internal static class AppInfo
    {
        public const string ProductName = "DOK-V01";
        public const string ExeFileName = "DOK-V01.exe";
        public const string ShortcutFileName = "DOK-V01.lnk";
        public const string UsbLauncherFileName = "Programm installieren.exe";
        public const string IconFileName = "AppIcon.ico";

        public const string VersionLabel = "1.1";
        public const string RevisionLabel = "R06";
        public const string BuildDate = "25.06.2026";

        public const bool IsDemoEdition = true;

        public const int DemoPeriodDays = 30;

        public static string EditionLabel => IsDemoEdition ? "Demoversion (30 Tage)" : "Enterprise Edition";

        public static string DisplayVersion => VersionLabel + " " + RevisionLabel;

        public static string WindowTitleSuffix => " v" + DisplayVersion;

        public static string GetIconPath()
        {
            var baseDir = AppDomain.CurrentDomain.BaseDirectory;
            var candidates = new[]
            {
                Path.Combine(baseDir, "Assets", IconFileName),
                Path.Combine(baseDir, IconFileName)
            };

            foreach (var candidate in candidates)
            {
                if (File.Exists(candidate))
                    return candidate;
            }

            return null;
        }
    }
}
