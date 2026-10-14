using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using PDF_umbenenen.Services;

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
        public const string RevisionLabel = "R87";
        public const string BuildDate = "14.10.2026";

#if ENTERPRISE
        public const bool IsDemoEdition = false;
#else
        public const bool IsDemoEdition = true;
#endif

        public const int DemoPeriodDays = 180;

        /// <summary>
        /// Einmaliger Demo-Neustart mit dieser Revision (auch abgelaufene Installationen).
        /// Spätere Revisionen ändern den Schlüssel nicht, sonst würde die Frist jedes Update neu starten.
        /// </summary>
        public const string DemoTrialResetKey = "R82";

        public const int VersionBuild = 87;

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

        /// <summary>
        /// Zielordner für Auto-Updates: aktuelle Installation oder registrierter Pointer (Desktop-Ordner).
        /// </summary>
        public static string TryResolveUpdateTargetDirectory()
        {
            var current = GetApplicationDirectory();
            if (IsRegisteredInstallDirectory(current))
                return Path.GetFullPath(current);

            try
            {
                if (File.Exists(InstallPointerPath))
                {
                    var pointerPath = File.ReadAllText(InstallPointerPath).Trim();
                    if (!string.IsNullOrWhiteSpace(pointerPath))
                    {
                        pointerPath = Path.GetFullPath(pointerPath);
                        if (File.Exists(Path.Combine(pointerPath, ExeFileName)))
                        {
                            EnsureInstallManifest(pointerPath);
                            if (IsRegisteredInstallDirectory(pointerPath))
                                return pointerPath;
                        }
                    }
                }
            }
            catch
            {
            }

            var legacyProgramFiles = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                ProductName);
            if (IsRegisteredInstallDirectory(legacyProgramFiles))
                return Path.GetFullPath(legacyProgramFiles);

            return null;
        }

        private static void EnsureInstallManifest(string installDirectory)
        {
            if (IsRegisteredInstallDirectory(installDirectory))
                return;

            if (!File.Exists(Path.Combine(installDirectory, ExeFileName)))
                return;

            InstallRegistrationService.RegisterSuccessfulInstall(installDirectory);
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

            // Auch ohne install.manifest: gespeicherten Installationspointer bevorzugen
            // (sonst überschreibt Debug-Start die Desktop-Verknüpfung mit bin\Debug).
            try
            {
                if (File.Exists(InstallPointerPath))
                {
                    var pointerPath = File.ReadAllText(InstallPointerPath).Trim();
                    if (!string.IsNullOrWhiteSpace(pointerPath))
                    {
                        var pointerExe = Path.Combine(Path.GetFullPath(pointerPath), ExeFileName);
                        if (File.Exists(pointerExe) && !IsDevelopmentBuildPath(pointerExe))
                            return pointerExe;
                    }
                }
            }
            catch
            {
            }

            var current = Assembly.GetExecutingAssembly().Location;
            return string.IsNullOrWhiteSpace(current)
                ? Path.Combine(DefaultInstallDirectory, ExeFileName)
                : current;
        }

        public static bool IsDevelopmentBuildPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return false;

            var full = Path.GetFullPath(path);
            return full.IndexOf("\\bin\\Debug\\", StringComparison.OrdinalIgnoreCase) >= 0
                   || full.IndexOf("\\bin\\Release\\", StringComparison.OrdinalIgnoreCase) >= 0
                   || full.IndexOf("/bin/Debug/", StringComparison.OrdinalIgnoreCase) >= 0
                   || full.IndexOf("/bin/Release/", StringComparison.OrdinalIgnoreCase) >= 0;
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

        /// <summary>Externer KI-Ordner (Ollama), nie im Update-ZIP – neben dem DOK-Programmordner.</summary>
        public const string KiFolderName = "AI_DOK-V01";

        /// <summary>Standardpfad: Geschwisterordner zur EXE (z. B. …\DOK-V01-R68\ + …\AI_DOK-V01\).</summary>
        public static string GetDefaultPortableKiDirectory()
        {
            var found = TryFindPortableAiDirectory();
            if (!string.IsNullOrWhiteSpace(found))
                return found;

            var appDir = GetApplicationDirectory();
            if (string.IsNullOrWhiteSpace(appDir))
                return Path.Combine(Environment.CurrentDirectory, KiFolderName);

            var anchor = ResolvePortableKiAnchorDirectory(appDir);
            var dokRoot = @"Z:\DOK-V01";
            if (Directory.Exists(Path.Combine(dokRoot, KiFolderName)))
                return Path.Combine(dokRoot, KiFolderName);

            var parent = Directory.GetParent(anchor);
            if (parent != null)
                return Path.Combine(parent.FullName, KiFolderName);

            return Path.Combine(anchor, KiFolderName);
        }

        public static string TryFindPortableAiDirectory()
        {
            foreach (var candidate in EnumeratePortableKiCandidates())
            {
                if (HasPortableAiLayout(candidate))
                    return Path.GetFullPath(candidate);
            }

            return null;
        }

        public static bool HasPortableAiLayout(string directory)
        {
            if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
                return false;

            try
            {
                var root = Path.GetFullPath(directory);
                return File.Exists(Path.Combine(root, "ollama.exe"))
                       || File.Exists(Path.Combine(root, "ollama", "ollama.exe"));
            }
            catch
            {
                return false;
            }
        }

        public static string NormalizePortableKiPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return string.Empty;

            try
            {
                return Path.GetFullPath(path.Trim());
            }
            catch
            {
                return path.Trim();
            }
        }

        private static string ResolvePortableKiAnchorDirectory(string appDirectory)
        {
            var full = Path.GetFullPath(appDirectory);
            var leaf = Path.GetFileName(full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            if (string.Equals(leaf, "Debug", StringComparison.OrdinalIgnoreCase)
                || string.Equals(leaf, "Release", StringComparison.OrdinalIgnoreCase))
            {
                var bin = Directory.GetParent(full);
                var project = bin?.Parent;
                if (project != null)
                    return project.FullName;
            }

            return full;
        }

        private static IEnumerable<string> EnumeratePortableKiCandidates()
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var list = new List<string>();
            void Offer(string path)
            {
                if (string.IsNullOrWhiteSpace(path))
                    return;
                try
                {
                    var full = Path.GetFullPath(path);
                    if (seen.Add(full))
                        list.Add(full);
                }
                catch
                {
                }
            }

            var appDir = GetApplicationDirectory();
            if (!string.IsNullOrWhiteSpace(appDir))
            {
                var anchor = ResolvePortableKiAnchorDirectory(appDir);
                Offer(Path.Combine(anchor, KiFolderName));
                var parent = Directory.GetParent(anchor);
                if (parent != null)
                    Offer(Path.Combine(parent.FullName, KiFolderName));

                // …\DOK-V01\DOK-V01-Rxx\ → …\DOK-V01\AI_DOK-V01
                var walk = new DirectoryInfo(appDir);
                for (var i = 0; i < 6 && walk != null; i++)
                {
                    Offer(Path.Combine(walk.FullName, KiFolderName));
                    Offer(Path.Combine(walk.FullName, "DOK-V01", KiFolderName));
                    if (walk.Parent != null)
                    {
                        Offer(Path.Combine(walk.Parent.FullName, KiFolderName));
                        Offer(Path.Combine(walk.Parent.FullName, "DOK-V01", KiFolderName));
                    }
                    walk = walk.Parent;
                }
            }

            Offer(@"Z:\DOK-V01\" + KiFolderName);

            var installDir = TryReadRegisteredInstallDirectory();
            if (!string.IsNullOrWhiteSpace(installDir))
            {
                Offer(Path.Combine(installDir, KiFolderName));
                var installParent = Directory.GetParent(installDir);
                if (installParent != null)
                    Offer(Path.Combine(installParent.FullName, KiFolderName));
            }

            return list;
        }

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






