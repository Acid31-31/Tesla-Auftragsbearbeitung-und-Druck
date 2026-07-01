using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using PDF_umbenenen.Models;

namespace PDF_umbenenen.Services
{
    internal static class GitHubUpdateService
    {
        private static readonly string UpdateRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "DOK-V01",
            "Updates");

        public static async Task<AppUpdateInfo> CheckForUpdateAsync(CancellationToken cancellationToken = default)
        {
            var result = new AppUpdateInfo();
            AppSecurityService.EnsureSecureTransport();

            try
            {
                using (var client = CreateHttpClient())
                {
                    var json = await client.GetStringAsync(AppInfo.GitHubLatestReleaseApiUrl).ConfigureAwait(false);
                    cancellationToken.ThrowIfCancellationRequested();

                    result.ReleaseTag = ExtractJsonString(json, "tag_name") ?? string.Empty;
                    result.ReleaseNotes = ExtractJsonString(json, "body") ?? string.Empty;
                    result.ExpectedSha256 = ExtractSha256FromReleaseNotes(result.ReleaseNotes);

                    if (!TryParseReleaseVersion(result.ReleaseTag, out var remoteVersion))
                    {
                        result.ErrorMessage = "Release-Version konnte nicht gelesen werden: " + result.ReleaseTag;
                        return result;
                    }

                    result.RemoteVersion = remoteVersion;
                    var localVersion = AppInfo.ApplicationVersion;
                    result.UpdateAvailable = remoteVersion > localVersion;

                    if (result.UpdateAvailable && string.IsNullOrWhiteSpace(result.ExpectedSha256))
                    {
                        result.ErrorMessage = "Release enthält keine SHA256-Prüfsumme. Update aus Sicherheitsgründen blockiert.";
                        result.UpdateAvailable = false;
                        return result;
                    }

                    var assetUrl = ExtractPreferredAssetUrl(json);
                    if (string.IsNullOrWhiteSpace(assetUrl))
                    {
                        if (result.UpdateAvailable)
                            result.ErrorMessage = "Kein Update-Paket (" + AppInfo.UpdateAssetFileName + ") in der Release gefunden.";
                        return result;
                    }

                    result.DownloadUrl = assetUrl;
                    result.AssetName = AppInfo.UpdateAssetFileName;
                }
            }
            catch (Exception ex)
            {
                result.ErrorMessage = ex.Message;
            }

            return result;
        }

        public static async Task<string> DownloadAndStageUpdateAsync(
            AppUpdateInfo update,
            IProgress<string> progress,
            CancellationToken cancellationToken = default)
        {
            if (update == null || string.IsNullOrWhiteSpace(update.DownloadUrl))
                throw new InvalidOperationException("Kein Update-Download verfügbar.");

            if (!Uri.TryCreate(update.DownloadUrl, UriKind.Absolute, out var uri)
                || !AppSecurityService.IsTrustedDownloadUrl(uri))
            {
                throw new InvalidOperationException("Unsicherer Download-Link blockiert.");
            }

            Directory.CreateDirectory(UpdateRoot);
            var packagePath = Path.Combine(UpdateRoot, AppSecurityService.SanitizeFileName(update.AssetName));
            var extractPath = Path.Combine(UpdateRoot, "staging-" + DateTime.Now.ToString("yyyyMMddHHmmss"));

            if (Directory.Exists(extractPath))
                Directory.Delete(extractPath, true);

            progress?.Report("Update wird heruntergeladen…");
            AppSecurityService.EnsureSecureTransport();

            using (var client = CreateHttpClient())
            using (var response = await client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false))
            {
                response.EnsureSuccessStatusCode();
                using (var stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
                using (var file = File.Create(packagePath))
                {
                    await stream.CopyToAsync(file).ConfigureAwait(false);
                }
            }

            if (string.IsNullOrWhiteSpace(update.ExpectedSha256))
            {
                File.Delete(packagePath);
                throw new InvalidOperationException(
                    "Release enthält keine SHA256-Prüfsumme. Update aus Sicherheitsgründen abgebrochen.");
            }

            progress?.Report("Prüfsumme wird verifiziert…");
            var actual = ComputeSha256Hex(packagePath);
            if (!string.Equals(actual, update.ExpectedSha256, StringComparison.OrdinalIgnoreCase))
            {
                File.Delete(packagePath);
                throw new InvalidOperationException(
                    "Update-Prüfsumme ungültig. Download abgebrochen.\nErwartet: "
                    + update.ExpectedSha256 + "\nErhalten: " + actual);
            }

            progress?.Report("Update wird entpackt…");
            AppSecurityService.ExtractZipSafely(packagePath, extractPath);

            var stagedAppRoot = FindApplicationRoot(extractPath);
            if (string.IsNullOrWhiteSpace(stagedAppRoot) || !File.Exists(Path.Combine(stagedAppRoot, AppInfo.ExeFileName)))
                throw new InvalidOperationException("Update-Paket enthält keine gültige " + AppInfo.ExeFileName + ".");

            var stagedExe = Path.Combine(stagedAppRoot, AppInfo.ExeFileName);
            if (!AppSecurityService.TryVerifyFileSignature(stagedExe, out var signatureMessage))
            {
                throw new InvalidOperationException(
                    "Update-EXE ist nicht digital signiert. Installation abgebrochen.\n" + signatureMessage);
            }

            return stagedAppRoot;
        }

        public static void LaunchUpdaterAndShutdown(string stagedAppRoot)
        {
            var appRoot = AppDomain.CurrentDomain.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var exePath = Path.Combine(appRoot, AppInfo.ExeFileName);
            var arguments = "--apply-update \"" + stagedAppRoot + "\" \"" + appRoot + "\"";
            var startInfo = new ProcessStartInfo
            {
                FileName = exePath,
                Arguments = arguments,
                WorkingDirectory = appRoot,
                UseShellExecute = true
            };

            if (AppInfo.IsProtectedInstallDirectory(appRoot))
                startInfo.Verb = "runas";

            Process.Start(startInfo);

            System.Windows.Application.Current.Shutdown();
        }

        private static HttpClient CreateHttpClient()
        {
            var client = new HttpClient
            {
                Timeout = TimeSpan.FromMinutes(10)
            };
            client.DefaultRequestHeaders.UserAgent.ParseAdd(AppInfo.ProductName + "/" + AppInfo.DisplayVersion);
            client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");

            var token = GetGitHubToken();
            if (!string.IsNullOrWhiteSpace(token))
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

            return client;
        }

        private static string GetGitHubToken()
        {
            try
            {
                var tokenFile = Path.Combine(AppInfo.UserDataDirectory, "github.token");
                if (File.Exists(tokenFile))
                {
                    var fromFile = File.ReadAllText(tokenFile).Trim();
                    if (!string.IsNullOrWhiteSpace(fromFile))
                        return fromFile;
                }
            }
            catch
            {
            }

            return Environment.GetEnvironmentVariable("GITHUB_TOKEN") ?? string.Empty;
        }

        private static string FindApplicationRoot(string extractPath)
        {
            if (File.Exists(Path.Combine(extractPath, AppInfo.ExeFileName)))
                return extractPath;

            var nested = Directory.GetFiles(extractPath, AppInfo.ExeFileName, SearchOption.AllDirectories)
                .OrderBy(path => path.Length)
                .FirstOrDefault();

            return nested == null ? null : Path.GetDirectoryName(nested);
        }

        private static string ExtractPreferredAssetUrl(string json)
        {
            var matches = Regex.Matches(
                json,
                "\\{[^{}]*\"name\"\\s*:\\s*\"([^\"]+)\"[^{}]*\"browser_download_url\"\\s*:\\s*\"([^\"]+)\"[^{}]*\\}",
                RegexOptions.IgnoreCase);

            foreach (Match match in matches)
            {
                var name = match.Groups[1].Value;
                if (name.Equals(AppInfo.UpdateAssetFileName, StringComparison.OrdinalIgnoreCase))
                    return UnescapeJson(match.Groups[2].Value);
            }

            foreach (Match match in matches)
            {
                var name = match.Groups[1].Value;
                if (name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)
                    && name.IndexOf("DOK-V01", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return UnescapeJson(match.Groups[2].Value);
                }
            }

            return null;
        }

        private static string ExtractSha256FromReleaseNotes(string releaseNotes)
        {
            if (string.IsNullOrWhiteSpace(releaseNotes))
                return string.Empty;

            var match = Regex.Match(releaseNotes, @"SHA-?256\s*[:=]\s*([0-9a-fA-F]{64})", RegexOptions.IgnoreCase);
            return match.Success ? match.Groups[1].Value.ToLowerInvariant() : string.Empty;
        }

        private static string ExtractJsonString(string json, string propertyName)
        {
            var match = Regex.Match(json, "\"" + Regex.Escape(propertyName) + "\"\\s*:\\s*\"((?:\\\\.|[^\"\\\\])*)\"");
            return match.Success ? UnescapeJson(match.Groups[1].Value) : null;
        }

        private static string UnescapeJson(string value)
        {
            return (value ?? string.Empty)
                .Replace("\\/", "/")
                .Replace("\\\"", "\"")
                .Replace("\\\\", "\\");
        }

        public static bool TryParseReleaseVersion(string tag, out Version version)
        {
            version = null;
            if (string.IsNullOrWhiteSpace(tag))
                return false;

            var cleaned = tag.Trim().TrimStart('v', 'V');
            var numeric = Regex.Match(cleaned, @"^(\d+)\.(\d+)(?:\.(\d+))?");
            if (!numeric.Success)
                return false;

            var major = int.Parse(numeric.Groups[1].Value);
            var minor = int.Parse(numeric.Groups[2].Value);
            var build = numeric.Groups[3].Success
                ? int.Parse(numeric.Groups[3].Value)
                : 0;

            var revision = Regex.Match(cleaned, @"R(\d+)", RegexOptions.IgnoreCase);
            if (revision.Success)
                build = int.Parse(revision.Groups[1].Value);

            version = new Version(major, minor, build);
            return true;
        }

        private static string ComputeSha256Hex(string filePath)
        {
            using (var stream = File.OpenRead(filePath))
            using (var sha = SHA256.Create())
            {
                return string.Concat(sha.ComputeHash(stream).Select(b => b.ToString("x2")));
            }
        }
    }
}
