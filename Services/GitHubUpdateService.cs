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
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using PDF_umbenenen.Models;

namespace PDF_umbenenen.Services
{
    internal static class GitHubUpdateService
    {
        private static string UpdateRoot => Path.Combine(AppInfo.UserDataDirectory, "Updates");

        private const string PrivateRepoHint =
            "Das GitHub-Repository ist privat. Für Updates wird ein Zugriffstoken benötigt.\n\n" +
            "Legen Sie eine Datei github.token mit einem GitHub-Token (nur Lesezugriff auf Releases) ab unter:\n" +
            "%LOCALAPPDATA%\\DOK-V01\\github.token";

        public static async Task<AppUpdateInfo> CheckForUpdateAsync(CancellationToken cancellationToken = default)
        {
            var result = new AppUpdateInfo();
            AppSecurityService.EnsureSecureTransport();

            try
            {
                var json = await FetchLatestReleaseJsonAsync(cancellationToken).ConfigureAwait(false);
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

                if (!result.UpdateAvailable)
                    return result;

                if (string.IsNullOrWhiteSpace(result.ExpectedSha256))
                {
                    result.ErrorMessage = "Release enthält keine SHA256-Prüfsumme. Update aus Sicherheitsgründen blockiert.";
                    result.UpdateAvailable = false;
                    return result;
                }

                var asset = ExtractPreferredAsset(json);
                if (asset == null || string.IsNullOrWhiteSpace(asset.DownloadUrl))
                {
                    result.UpdateAvailable = false;
                    result.ErrorMessage = "Kein Update-Paket (" + AppInfo.UpdateAssetFileName + ") in der Release gefunden.";
                    return result;
                }

                result.DownloadUrl = asset.DownloadUrl;
                result.AssetId = asset.AssetId;
                result.AssetName = asset.Name ?? AppInfo.UpdateAssetFileName;
            }
            catch (Exception ex)
            {
                result.ErrorMessage = DescribeUpdateError(ex);
            }

            return result;
        }

        public static async Task<string> DownloadAndStageUpdateAsync(
            AppUpdateInfo update,
            IProgress<UpdateProgressInfo> progress,
            CancellationToken cancellationToken = default)
        {
            if (update == null)
                throw new InvalidOperationException("Kein Update-Download verfügbar.");

            if (update.AssetId <= 0 && string.IsNullOrWhiteSpace(update.DownloadUrl))
                throw new InvalidOperationException("Kein Update-Download verfügbar.");

            Directory.CreateDirectory(UpdateRoot);
            var packagePath = Path.Combine(UpdateRoot, AppSecurityService.SanitizeFileName(update.AssetName));
            var extractPath = Path.Combine(UpdateRoot, "staging-" + DateTime.Now.ToString("yyyyMMddHHmmss"));

            if (Directory.Exists(extractPath))
                Directory.Delete(extractPath, true);

            ReportProgress(progress, 2, "Update wird heruntergeladen…");
            AppSecurityService.EnsureSecureTransport();

            var errors = new List<string>();
            if (!string.IsNullOrWhiteSpace(update.DownloadUrl))
            {
                if (await TryDownloadBrowserAssetAsync(update.DownloadUrl, packagePath, errors, progress, cancellationToken).ConfigureAwait(false))
                    goto downloaded;
            }

            if (update.AssetId > 0)
            {
                if (await TryDownloadReleaseAssetAsync(update.AssetId, packagePath, errors, progress, cancellationToken).ConfigureAwait(false))
                    goto downloaded;
            }

            throw new InvalidOperationException(BuildDownloadErrorMessage(errors));

        downloaded:
            if (string.IsNullOrWhiteSpace(update.ExpectedSha256))
            {
                File.Delete(packagePath);
                throw new InvalidOperationException(
                    "Release enthält keine SHA256-Prüfsumme. Update aus Sicherheitsgründen abgebrochen.");
            }

            ReportProgress(progress, 78, "Prüfsumme wird verifiziert…");
            var actual = ComputeSha256Hex(packagePath);
            if (!string.Equals(actual, update.ExpectedSha256, StringComparison.OrdinalIgnoreCase))
            {
                File.Delete(packagePath);
                throw new InvalidOperationException(
                    "Update-Prüfsumme ungültig. Download abgebrochen.\nErwartet: "
                    + update.ExpectedSha256 + "\nErhalten: " + actual);
            }

            ReportProgress(progress, 82, "Update wird entpackt…");
            ExtractZipWithProgress(packagePath, extractPath, progress, cancellationToken);

            var stagedAppRoot = FindApplicationRoot(extractPath);
            if (string.IsNullOrWhiteSpace(stagedAppRoot) || !File.Exists(Path.Combine(stagedAppRoot, AppInfo.ExeFileName)))
                throw new InvalidOperationException("Update-Paket enthält keine gültige " + AppInfo.ExeFileName + ".");

            ReportProgress(progress, 96, "Signatur wird geprüft…");
            if (!AppSecurityService.TryVerifyApplicationPackage(stagedAppRoot, out var signatureMessage, stagedAppRoot))
            {
                throw new InvalidOperationException(
                    "Update-Paket ist nicht vollständig oder nicht digital signiert. Installation abgebrochen.\n"
                    + signatureMessage);
            }

            ReportProgress(progress, 100, "Download abgeschlossen.");
            return stagedAppRoot;
        }

        public static void LaunchUpdaterAndShutdown(string stagedAppRoot)
        {
            if (string.IsNullOrWhiteSpace(stagedAppRoot))
                throw new ArgumentException("Kein Update-Staging-Verzeichnis.", nameof(stagedAppRoot));

            stagedAppRoot = Path.GetFullPath(stagedAppRoot);
            var appRoot = AppDomain.CurrentDomain.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (!AppInfo.IsRegisteredInstallDirectory(appRoot))
            {
                throw new InvalidOperationException(
                    "Updates sind nur in einer festen Installation erlaubt.\n"
                    + "Bitte \"" + AppInfo.UsbLauncherFileName + "\" ausführen und die Desktop-Verknüpfung "
                    + "auf den Installationsordner zeigen lassen – nicht auf den Debug-/USB-Ordner.");
            }

            var updaterExePath = Path.Combine(stagedAppRoot, AppInfo.ExeFileName);
            if (!File.Exists(updaterExePath))
                throw new InvalidOperationException("Update-EXE im Paket nicht gefunden: " + updaterExePath);

            var parentProcessId = Process.GetCurrentProcess().Id;
            var arguments = "--apply-update \"" + stagedAppRoot + "\" \"" + appRoot + "\" " + parentProcessId;
            var startInfo = new ProcessStartInfo
            {
                FileName = updaterExePath,
                Arguments = arguments,
                WorkingDirectory = stagedAppRoot,
                UseShellExecute = true
            };

            if (AppInfo.IsProtectedInstallDirectory(appRoot))
                startInfo.Verb = "runas";

            Process.Start(startInfo);

            System.Windows.Application.Current.Shutdown();
        }

        public static void EnsureUpdateTokenFromInstallFolder()
        {
            if (AppInfo.GitHubUpdatesArePublic)
            {
                RemoveBundledGithubTokenFiles();
                return;
            }

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
            return !AppInfo.GitHubUpdatesArePublic && !string.IsNullOrWhiteSpace(GetGitHubToken());
        }

        private static void RemoveBundledGithubTokenFiles()
        {
            foreach (var path in GetGitHubTokenFileCandidates())
            {
                try
                {
                    if (File.Exists(path))
                        File.Delete(path);
                }
                catch
                {
                }
            }
        }

        private static async Task<string> FetchLatestReleaseJsonAsync(CancellationToken cancellationToken)
        {
            var errors = new List<string>();

            foreach (var useAuth in GetAuthAttempts())
            {
                try
                {
                    using (var client = CreateHttpClient(useAuth))
                    using (var response = await client.GetAsync(AppInfo.GitHubLatestReleaseApiUrl, cancellationToken).ConfigureAwait(false))
                    {
                        if (response.IsSuccessStatusCode)
                            return await response.Content.ReadAsStringAsync().ConfigureAwait(false);

                        errors.Add(DescribeHttpStatus(response.StatusCode, useAuth));
                    }
                }
                catch (Exception ex)
                {
                    errors.Add(ex.Message);
                }
            }

            if (!AppInfo.GitHubUpdatesArePublic && string.IsNullOrWhiteSpace(GetGitHubToken()))
                throw new InvalidOperationException(PrivateRepoHint);

            throw new InvalidOperationException(
                "GitHub-Release konnte nicht gelesen werden.\n" + string.Join("\n", errors.Distinct()));
        }

        private static async Task<bool> TryDownloadBrowserAssetAsync(
            string downloadUrl,
            string packagePath,
            ICollection<string> errors,
            IProgress<UpdateProgressInfo> progress,
            CancellationToken cancellationToken)
        {
            if (!Uri.TryCreate(downloadUrl, UriKind.Absolute, out var uri)
                || !AppSecurityService.IsTrustedDownloadUrl(uri))
            {
                errors.Add("Unsicherer Download-Link blockiert.");
                return false;
            }

            foreach (var useAuth in GetAuthAttempts())
            {
                try
                {
                    using (var client = CreateHttpClient(useAuth))
                    using (var request = new HttpRequestMessage(HttpMethod.Get, uri))
                    using (var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false))
                    {
                        if (!response.IsSuccessStatusCode)
                        {
                            errors.Add("Browser-Download: " + DescribeHttpStatus(response.StatusCode, useAuth));
                            continue;
                        }

                        using (var stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
                        using (var file = File.Create(packagePath))
                        {
                            await CopyStreamWithProgressAsync(
                                stream,
                                file,
                                response.Content.Headers.ContentLength,
                                5,
                                75,
                                "Update wird heruntergeladen…",
                                progress,
                                cancellationToken).ConfigureAwait(false);
                        }

                        return true;
                    }
                }
                catch (Exception ex)
                {
                    errors.Add("Browser-Download: " + ex.Message);
                }
            }

            return false;
        }

        private static async Task<bool> TryDownloadReleaseAssetAsync(
            long assetId,
            string packagePath,
            ICollection<string> errors,
            IProgress<UpdateProgressInfo> progress,
            CancellationToken cancellationToken)
        {
            var apiUrl = string.Format(
                "https://api.github.com/repos/{0}/{1}/releases/assets/{2}",
                AppInfo.GitHubOwner,
                AppInfo.GitHubRepo,
                assetId);

            if (!Uri.TryCreate(apiUrl, UriKind.Absolute, out var uri)
                || !AppSecurityService.IsTrustedDownloadUrl(uri))
            {
                errors.Add("Unsicherer API-Download-Link blockiert.");
                return false;
            }

            foreach (var useAuth in GetAuthAttempts())
            {
                try
                {
                    using (var client = CreateHttpClient(useAuth))
                    using (var request = new HttpRequestMessage(HttpMethod.Get, uri))
                    {
                        request.Headers.Accept.Clear();
                        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/octet-stream"));

                        using (var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false))
                        {
                            if (!response.IsSuccessStatusCode)
                            {
                                errors.Add("API-Download: " + DescribeHttpStatus(response.StatusCode, useAuth));
                                continue;
                            }

                            using (var stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
                            using (var file = File.Create(packagePath))
                            {
                                await CopyStreamWithProgressAsync(
                                    stream,
                                    file,
                                    response.Content.Headers.ContentLength,
                                    5,
                                    75,
                                    "Update wird heruntergeladen…",
                                    progress,
                                    cancellationToken).ConfigureAwait(false);
                            }

                            return true;
                        }
                    }
                }
                catch (Exception ex)
                {
                    errors.Add("API-Download: " + ex.Message);
                }
            }

            return false;
        }

        private static IEnumerable<bool> GetAuthAttempts()
        {
            if (AppInfo.GitHubUpdatesArePublic)
            {
                yield return false;
                yield break;
            }

            yield return false;
            if (!string.IsNullOrWhiteSpace(GetGitHubToken()))
                yield return true;
        }

        private static string BuildDownloadErrorMessage(IEnumerable<string> errors)
        {
            var details = string.Join("\n", (errors ?? Enumerable.Empty<string>()).Distinct().Where(e => !string.IsNullOrWhiteSpace(e)));
            if (string.IsNullOrWhiteSpace(details))
                return "Update-Paket konnte nicht heruntergeladen werden.";

            return "Update-Paket konnte nicht heruntergeladen werden.\n\n" + details;
        }

        private static string DescribeHttpStatus(HttpStatusCode statusCode, bool usedAuth)
        {
            var suffix = usedAuth ? " (mit Token)" : " (ohne Token)";
            if (statusCode == HttpStatusCode.NotFound)
                return "404 Nicht gefunden" + suffix;

            if (statusCode == HttpStatusCode.Unauthorized)
                return "401 Nicht autorisiert" + suffix;

            if (statusCode == HttpStatusCode.Forbidden)
                return "403 Zugriff verweigert" + suffix;

            return ((int)statusCode) + " " + statusCode + suffix;
        }

        private static HttpClient CreateHttpClient(bool useAuth)
        {
            var client = new HttpClient
            {
                Timeout = TimeSpan.FromMinutes(10)
            };
            client.DefaultRequestHeaders.UserAgent.ParseAdd(AppInfo.ProductName + "/" + AppInfo.DisplayVersion);
            client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");

            if (useAuth)
            {
                var token = GetGitHubToken();
                if (!string.IsNullOrWhiteSpace(token))
                    client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
            }

            return client;
        }

        private static string GetGitHubToken()
        {
            if (AppInfo.GitHubUpdatesArePublic)
                return string.Empty;

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

        private sealed class ReleaseAssetInfo
        {
            public long AssetId { get; set; }

            public string Name { get; set; }

            public string DownloadUrl { get; set; }
        }

        private static ReleaseAssetInfo ExtractPreferredAsset(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
                return null;

            var preferred = FindAssetNearName(json, AppInfo.UpdateAssetFileName);
            if (preferred != null)
                return preferred;

            foreach (Match match in Regex.Matches(json, "\"name\"\\s*:\\s*\"([^\"]+)\"", RegexOptions.IgnoreCase))
            {
                var name = match.Groups[1].Value;
                if (!name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                    continue;

                if (name.IndexOf("DOK-V01", StringComparison.OrdinalIgnoreCase) < 0)
                    continue;

                var asset = FindAssetNearName(json, name, match.Index);
                if (asset != null)
                    return asset;
            }

            return null;
        }

        private static ReleaseAssetInfo FindAssetNearName(string json, string assetName, int? nameIndex = null)
        {
            if (string.IsNullOrWhiteSpace(json) || string.IsNullOrWhiteSpace(assetName))
                return null;

            int index;
            if (nameIndex.HasValue)
            {
                index = nameIndex.Value;
            }
            else
            {
                var nameMatch = Regex.Match(
                    json,
                    "\"name\"\\s*:\\s*\"" + Regex.Escape(assetName) + "\"",
                    RegexOptions.IgnoreCase);
                if (!nameMatch.Success)
                    return null;

                index = nameMatch.Index;
            }

            var sliceStart = Math.Max(0, index - 900);
            var sliceLength = Math.Min(json.Length - sliceStart, 1200);
            var slice = json.Substring(sliceStart, sliceLength);

            long assetId = 0;
            foreach (Match idMatch in Regex.Matches(slice, "\"id\"\\s*:\\s*(\\d+)"))
            {
                if (long.TryParse(idMatch.Groups[1].Value, out var parsed))
                    assetId = parsed;
            }

            if (assetId <= 0)
                return null;

            var url = FindDownloadUrlNearIndex(json, index);
            return new ReleaseAssetInfo
            {
                AssetId = assetId,
                Name = assetName,
                DownloadUrl = url
            };
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
                    && !AppInfo.GitHubUpdatesArePublic
                    && string.IsNullOrWhiteSpace(GetGitHubToken()))
                    return PrivateRepoHint;

                if (message.IndexOf("404", StringComparison.OrdinalIgnoreCase) >= 0)
                    return "GitHub-Release nicht gefunden (404). Prüfen Sie Internetverbindung und Repository-Zugriff.";

                if (message.IndexOf("403", StringComparison.OrdinalIgnoreCase) >= 0)
                    return "GitHub-Zugriff verweigert (403). Eventuell Rate-Limit – später erneut versuchen.";

                if (message.IndexOf("401", StringComparison.OrdinalIgnoreCase) >= 0)
                    return "GitHub-Zugriff nicht autorisiert (401).";
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
            if (string.IsNullOrEmpty(value))
                return string.Empty;

            var sb = new StringBuilder(value.Length);
            for (var i = 0; i < value.Length; i++)
            {
                if (value[i] != '\\' || i + 1 >= value.Length)
                {
                    sb.Append(value[i]);
                    continue;
                }

                switch (value[i + 1])
                {
                    case 'n':
                        sb.Append('\n');
                        i++;
                        break;
                    case 'r':
                        sb.Append('\r');
                        i++;
                        break;
                    case 't':
                        sb.Append('\t');
                        i++;
                        break;
                    case '"':
                        sb.Append('"');
                        i++;
                        break;
                    case '/':
                        sb.Append('/');
                        i++;
                        break;
                    case '\\':
                        sb.Append('\\');
                        i++;
                        break;
                    case 'u':
                        if (i + 5 < value.Length)
                        {
                            var hex = value.Substring(i + 2, 4);
                            int code;
                            if (int.TryParse(hex, System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture, out code))
                            {
                                sb.Append((char)code);
                                i += 5;
                                break;
                            }
                        }

                        sb.Append(value[i]);
                        break;
                    default:
                        sb.Append(value[i]);
                        break;
                }
            }

            return sb.ToString();
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

        private static void ReportProgress(IProgress<UpdateProgressInfo> progress, int percent, string message)
        {
            progress?.Report(new UpdateProgressInfo(percent, message));
        }

        private static async Task CopyStreamWithProgressAsync(
            Stream source,
            Stream destination,
            long? totalBytes,
            int percentStart,
            int percentEnd,
            string message,
            IProgress<UpdateProgressInfo> progress,
            CancellationToken cancellationToken)
        {
            const int bufferSize = 81920;
            var buffer = new byte[bufferSize];
            long totalRead = 0;
            int read;

            while ((read = await source.ReadAsync(buffer, 0, buffer.Length, cancellationToken).ConfigureAwait(false)) > 0)
            {
                await destination.WriteAsync(buffer, 0, read, cancellationToken).ConfigureAwait(false);
                totalRead += read;

                var percent = percentStart;
                if (totalBytes.HasValue && totalBytes.Value > 0)
                {
                    percent = percentStart + (int)(totalRead * (percentEnd - percentStart) / totalBytes.Value);
                }
                else if (totalRead > 0)
                {
                    percent = Math.Min(percentEnd - 1, percentStart + (int)(totalRead / 250000));
                }

                ReportProgress(progress, percent, message);
            }
        }

        private static void ExtractZipWithProgress(
            string zipFilePath,
            string destinationDirectory,
            IProgress<UpdateProgressInfo> progress,
            CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(zipFilePath) || !File.Exists(zipFilePath))
                throw new FileNotFoundException("ZIP-Datei nicht gefunden.", zipFilePath);

            Directory.CreateDirectory(destinationDirectory);
            var root = Path.GetFullPath(destinationDirectory);
            if (!root.EndsWith(Path.DirectorySeparatorChar.ToString()))
                root += Path.DirectorySeparatorChar;

            using (var archive = ZipFile.OpenRead(zipFilePath))
            {
                var entries = archive.Entries
                    .Where(entry => !string.IsNullOrWhiteSpace(entry.Name))
                    .ToArray();
                var total = Math.Max(1, entries.Length);

                for (var index = 0; index < entries.Length; index++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var entry = entries[index];
                    var entryPath = Path.GetFullPath(Path.Combine(destinationDirectory, entry.FullName));
                    if (!entryPath.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidOperationException("Unsicherer ZIP-Eintrag blockiert: " + entry.FullName);

                    var entryDir = Path.GetDirectoryName(entryPath);
                    if (!string.IsNullOrWhiteSpace(entryDir))
                        Directory.CreateDirectory(entryDir);

                    entry.ExtractToFile(entryPath, true);
                    var percent = 82 + (int)((index + 1) * 12.0 / total);
                    ReportProgress(progress, percent, "Entpacke: " + entry.Name);
                }
            }
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
