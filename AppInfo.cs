using System;

namespace PDF_umbenenen
{
    internal static class AppInfo
    {
        public const string VersionLabel = "1.1";
        public const string RevisionLabel = "R03";
        public const string BuildDate = "17.06.2026";

        public static string DisplayVersion => VersionLabel + " " + RevisionLabel;

        public static string WindowTitleSuffix => " v" + DisplayVersion;
    }
}
