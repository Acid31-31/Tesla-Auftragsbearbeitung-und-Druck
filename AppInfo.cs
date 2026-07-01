using System;
using System.IO;
using System.Reflection;

namespace PDF_umbenenen
{
    internal static class AppInfo
    {
        public const string ProductName = "DOK-V01";
        public const string ExeFileName = "DOK-V01.exe";
        public const string ShortcutFileName = "DOK-V01.lnk";
        public const string UsbLauncherFileName = "Programm installieren.exe";
        public const string UsbUninstallerFileName = "Programm deinstallieren.exe";
        public const string IconFileName = "AppIcon.ico";

        public const string VersionLabel = "1.1";
        public const string RevisionLabel = "R14";
        public const string BuildDate = "01.07.2026";

#if ENTERPRISE
        public const bool IsDemoEdition = false;
#else
        public const bool IsDemoEdition = true;
#endif

        public const int DemoPeriodDays = 30;

        public const int VersionBuild = 14;

#if DEBUG
        public const bool RequireCodeSignature = false;
#else
        public const bool RequireCodeSignature = true;
#endif

        public const string GitHubOwner = "Acid31-31";
        public const string GitHubRepo = "Tesla-Auftragsbearbeitung-und-Druck";
        public const string UpdateAssetFileName = "DOK-V01-Release.zip";

        public static string EditionLabel => IsDemoEdition ? "Demoversion (30 Tage)" : "Enterprise Edition";

        public static string DisplayVersion => VersionLabel + " " + RevisionLabel;

        public static Version ApplicationVersion
        {
            get
            {
                var version = Assembly.GetExecutingAssembly().GetName().Version;
                return version == null
                    ? new Version(1, 1, VersionBuild)
                    : new Version(version.Major, version.Minor, version.Build);
            }
        }

        public static string GitHubLatestReleaseApiUrl =>
            "https://api.github.com/repos/" + GitHubOwner + "/" + GitHubRepo + "/releases/latest";

        public static string DefaultInstallDirectory => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            ProductName);

        public static string UserDataDirectory => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            ProductName);

        public static string GetInstalledExePath()
        {
            var fixedExe = Path.Combine(DefaultInstallDirectory, ExeFileName);
            if (File.Exists(fixedExe))
                return fixedExe;

            var current = Assembly.GetExecutingAssembly().Location;
            return string.IsNullOrWhiteSpace(current) ? fixedExe : current;
        }

        public static bool IsProtectedInstallDirectory(string directory)
        {
            if (string.IsNullOrWhiteSpace(directory))
                return false;

            var fullPath = Path.GetFullPath(directory)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

            foreach (var root in GetProtectedInstallRoots())
            {
                if (fullPath.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(fullPath, root, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private static string[] GetProtectedInstallRoots()
        {
            return new[]
            {
                Path.GetFullPath(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles)),
                Path.GetFullPath(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86))
            };
        }

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

