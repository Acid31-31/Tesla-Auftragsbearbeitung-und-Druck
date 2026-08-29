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
        public const string RevisionLabel = "R62";
        public const string BuildDate = "29.08.2026";

#if ENTERPRISE
        public const bool IsDemoEdition = false;
#else
        public const bool IsDemoEdition = true;
#endif

        public const int DemoPeriodDays = 90;

        public const int VersionBuild = 62;

#if DEBUG
        public const bool RequireCodeSignature = false;
#else
        public const bool RequireCodeSignature = true;
#endif

        public const string GitHubOwner = "Acid31-31";
        public const string GitHubRepo = "Tesla-Auftragsbearbeitung-und-Druck";
        public const string UpdateAssetFileName = "DOK-V01-Release.zip";
        public const string InstallManifestFileName = "install.manifest";
        public const string CodeSigningCerFileName = "CodeSigning.cer";

        /// <summary>Repository ist öffentlich – Updates ohne github.token.</summary>
        public const bool GitHubUpdatesArePublic = true;

        public static string EditionLabel => IsDemoEdition
            ? "Demoversion (" + DemoPeriodDays + " Tage)"
            : "Enterprise Edition";

        public static string DisplayVersion => VersionLabel + " " + RevisionLabel;

        public static Version ApplicationVersion
        {
            get
            {
                var version = Assembly.GetExecutingAssembly().GetName().Version;
                if (version == null)
                    return new Version(1, 1, VersionBuild);

                // AssemblyVersion ist 1.1.0.Rxx – die Revisionsnummer (R21 → 21) steht in Revision, nicht in Build.
                var build = version.Revision > 0 ? version.Revision : version.Build;
                if (build <= 0)
                    build = VersionBuild;

                return new Version(version.Major, version.Minor, build);
            }
        }

        public static string GitHubLatestReleaseApiUrl =>
            "https://api.github.com/repos/" + GitHubOwner + "/" + GitHubRepo + "/releases/latest";

        public static string DefaultInstallDirectory => GetSuggestedInstallDirectory();

        /// <summary>Standard-Ziel beim Setup: Ordner der laufenden EXE (USB / kopierter Ordner).</summary>
        public static string GetSuggestedInstallDirectory()
        {
            var appDir = GetApplicationDirectory();
            if (!string.IsNullOrWhiteSpace(appDir))
                return appDir;

            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                ProductName);
        }

        /// <summary>Verzeichnis der laufenden EXE.</summary>
        public static string GetApplicationDirectory()
        {
            var location = Assembly.GetExecutingAssembly().Location;
            return string.IsNullOrWhiteSpace(location)
                ? AppDomain.CurrentDomain.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                : Path.GetDirectoryName(location);
        }

        /// <summary>Registriertes Installationsverzeichnis (Desktop-Installation).</summary>
        public static string GetInstallDirectory()
        {
            var registered = TryReadRegisteredInstallDirectory();
            if (!string.IsNullOrWhiteSpace(registered))
                return registered;

            return GetApplicationDirectory();
        }

        public static string UserDataDirectory => GetInstallDirectory();

        public static string WorkspaceDirectory => Path.Combine(GetInstallDirectory(), "Ablage");

        private static string InstallPointerPath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            ProductName,
            "install-location.txt");

        public static void SaveInstallLocation(string installDirectory)
        {
            if (string.IsNullOrWhiteSpace(installDirectory))
                return;

            try
            {
                var pointerDir = Path.GetDirectoryName(InstallPointerPath);
                if (!string.IsNullOrWhiteSpace(pointerDir))
                    Directory.CreateDirectory(pointerDir);

                File.WriteAllText(InstallPointerPath, Path.GetFullPath(installDirectory));
            }
            catch
            {
            }
        }

        public static string TryReadRegisteredInstallDirectory()
        {
            var current = GetApplicationDirectory();
            if (IsRegisteredInstallDirectory(current))
                return current;

            try
            {
                if (File.Exists(InstallPointerPath))
                {
                    var path = File.ReadAllText(InstallPointerPath).Trim();
                    if (IsRegisteredInstallDirectory(path))
                        return Path.GetFullPath(path);
                }
            }
            catch
            {
            }

            var legacyProgramFiles = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                ProductName);
            if (IsRegisteredInstallDirectory(legacyProgramFiles))
                return legacyProgramFiles;

            return null;
        }

        public static bool IsRegisteredInstallDirectory(string directory)
        {
            if (string.IsNullOrWhiteSpace(directory))
                return false;

            return File.Exists(Path.Combine(directory, InstallManifestFileName))
                && File.Exists(Path.Combine(directory, ExeFileName));
        }

        public static string GetInstalledExePath()
        {
            var registered = TryReadRegisteredInstallDirectory();
            if (!string.IsNullOrWhiteSpace(registered))
            {
                var registeredExe = Path.Combine(registered, ExeFileName);
                if (File.Exists(registeredExe))
                    return registeredExe;
            }

            var current = Assembly.GetExecutingAssembly().Location;
            return string.IsNullOrWhiteSpace(current)
                ? Path.Combine(DefaultInstallDirectory, ExeFileName)
                : current;
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





