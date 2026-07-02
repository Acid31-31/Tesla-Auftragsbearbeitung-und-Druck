using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
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

        private const string PrivateRepoHint =
            "Das GitHub-Repository ist privat. Für Updates wird ein Zugriffstoken benötigt.\n\n" +
            "Legen Sie eine Datei github.token mit einem GitHub-Token (nur Lesezugriff auf Releases) ab unter:\n" +
            "%LOCALAPPDATA%\\DOK-V01\\github.token\n\n" +
            "Oder neben der DOK-V01.exe im Programmordner.\n\n" +
            "Nach Neuinstallation aus dem Release-ZIP ist die Datei bereits enthalten.";

        public static async Task<AppUpdateInfo> CheckForUpdateAsync(CancellationToken cancellationToken = default)
        {
            var result = new AppUpdateInfo();
            AppSecurityService.EnsureSecureTransport();

            try
            {
                using (var client = CreateHttpClient())
                {
                    var json = await FetchLatestReleaseJsonAsync(client, cancellationToken).ConfigureAwait(false);
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
                        {
                            result.UpdateAvailable = false;
                            result.ErrorMessage = "Kein Update-Paket (" + AppInfo.UpdateAssetFileName + ") in der Release gefunden.";
                        }
                        return result;
                    }

                    result.DownloadUrl = assetUrl;
                    result.AssetName = AppInfo.UpdateAssetFileName;
                }
            }
            catch (Exception ex)
            {
                result.ErrorMessage = DescribeUpdateError(ex);
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

        private static async Task<string> FetchLatestReleaseJsonAsync(HttpClient client, CancellationToken cancellationToken)
        {
            using (var response = await client.GetAsync(AppInfo.GitHubLatestReleaseApiUrl, cancellationToken).ConfigureAwait(false))
            {
                if (response.StatusCode == HttpStatusCode.NotFound && string.IsNullOrWhiteSpace(GetGitHubToken()))
                    throw new InvalidOperationException(PrivateRepoHint);

                if (response.StatusCode == HttpStatusCode.NotFound)
                    throw new InvalidOperationException(
                        "GitHub-Release nicht gefunden (404). Prüfen Sie, ob ein Release veröffentlicht ist.");

                if (response.StatusCode == HttpStatusCode.Unauthorized)
                    throw new InvalidOperationException(
                        "GitHub-Token ungültig (401). Bitte github.token mit einem gültigen Token ersetzen.");

                if (response.StatusCode == HttpStatusCode.Forbidden)
                    throw new InvalidOperationException(
                        "GitHub-Zugriff verweigert (403). Token prüfen oder später erneut versuchen.");

                if (!response.IsSuccessStatusCode)
                    throw new InvalidOperationException(
                        "GitHub-API Fehler: " + (int)response.StatusCode + " " + response.ReasonPhrase);

                return await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            }
        }

        public static void EnsureUpdateTokenFromInstallFolder()
        {
            try
            {
                var installToken = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "github.token");
                if (!File.Exists(installToken))
                    return;

                var targetDir = AppInfo.UserDataDirectory;
                Directory.CreateDirectory(targetDir);
                var target = Path.Combine(targetDir, "github.token");
                File.Copy(installToken, target, true);
            }
            catch
            {
            }
        }

        public static bool HasGitHubToken()
        {
            return !string.IsNullOrWhiteSpace(GetGitHubToken());
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
            foreach (var path in GetGitHubTokenFileCandidates())
            {
                try
                {
                    if (!File.Exists(path))
                        continue;

                    var token = File.ReadAllText(path).Trim();
                    if (!string.IsNullOrWhiteSpace(token))
                        return token;
                }
                catch
                {
                }
            }

            return Environment.GetEnvironmentVariable("GITHUB_TOKEN")
                ?? Environment.GetEnvironmentVariable("GITHUB_UPDATE_TOKEN")
                ?? string.Empty;
        }

        private static IEnumerable<string> GetGitHubTokenFileCandidates()
        {
            yield return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "github.token");
            yield return Path.Combine(AppInfo.UserDataDirectory, "github.token");
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
            if (string.IsNullOrWhiteSpace(json))
                return null;

            var preferred = FindDownloadUrlAfterAssetName(json, AppInfo.UpdateAssetFileName);
            if (!string.IsNullOrWhiteSpace(preferred))
                return preferred;

            foreach (Match match in Regex.Matches(json, "\"name\"\\s*:\\s*\"([^\"]+)\"", RegexOptions.IgnoreCase))
            {
                var name = match.Groups[1].Value;
                if (!name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                    continue;

                if (name.IndexOf("DOK-V01", StringComparison.OrdinalIgnoreCase) < 0)
                    continue;

                var url = FindDownloadUrlNearIndex(json, match.Index);
                if (!string.IsNullOrWhiteSpace(url))
                    return url;
            }

            return null;
        }

        private static string FindDownloadUrlAfterAssetName(string json, string assetName)
        {
            if (string.IsNullOrWhiteSpace(json) || string.IsNullOrWhiteSpace(assetName))
                return null;

            var nameMatch = Regex.Match(
                json,
                "\"name\"\\s*:\\s*\"" + Regex.Escape(assetName) + "\"",
                RegexOptions.IgnoreCase);

            return nameMatch.Success ? FindDownloadUrlNearIndex(json, nameMatch.Index) : null;
        }

        private static string FindDownloadUrlNearIndex(string json, int nameIndex)
        {
            if (nameIndex < 0 || nameIndex >= json.Length)
                return null;

            var sliceLength = Math.Min(2500, json.Length - nameIndex);
            var slice = json.Substring(nameIndex, sliceLength);
            var urlMatch = Regex.Match(
                slice,
                "\"browser_download_url\"\\s*:\\s*\"((?:\\\\.|[^\"\\\\])*)\"",
                RegexOptions.IgnoreCase);

            return urlMatch.Success ? UnescapeJson(urlMatch.Groups[1].Value) : null;
        }

        private static string DescribeUpdateError(Exception ex)
        {
            if (ex is InvalidOperationException)
                return ex.Message;

            if (ex is HttpRequestException httpEx)
            {
                var message = httpEx.Message ?? string.Empty;
                if (message.IndexOf("404", StringComparison.OrdinalIgnoreCase) >= 0
                    && string.IsNullOrWhiteSpace(GetGitHubToken()))
                    return PrivateRepoHint;

                if (message.IndexOf("404", StringComparison.OrdinalIgnoreCase) >= 0)
                    return "GitHub-Release nicht gefunden (404). Prüfen Sie Internetverbindung und Repository-Zugriff.";

                if (message.IndexOf("403", StringComparison.OrdinalIgnoreCase) >= 0)
                    return "GitHub-Zugriff verweigert (403). Eventuell Rate-Limit – später erneut versuchen oder github.token hinterlegen.";

                if (message.IndexOf("401", StringComparison.OrdinalIgnoreCase) >= 0)
                    return "GitHub-Zugriff nicht autorisiert (401). Bitte github.token mit einem gültigen Token ersetzen.";
            }

            if (ex is TaskCanceledException)
                return "Update-Prüfung hat zu lange gedauert (Zeitüberschreitung). Bitte Internetverbindung prüfen.";

            return ex.Message;
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
