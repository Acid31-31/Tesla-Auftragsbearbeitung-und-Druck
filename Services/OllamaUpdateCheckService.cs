using System;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using PDF_umbenenen.Models;
using PDF_umbenenen.Services.Ai;

namespace PDF_umbenenen.Services
{
    public sealed class OllamaUpdateInfo
    {
        public string InstalledVersion { get; set; } = string.Empty;

        public string LatestVersion { get; set; } = string.Empty;

        public string DownloadUrl { get; set; } = string.Empty;

        public string AssetName { get; set; } = string.Empty;

        public string ExpectedSha256 { get; set; } = string.Empty;

        public long AssetSizeBytes { get; set; }

        public string KiDirectory { get; set; } = string.Empty;

        public bool FolderFound { get; set; }

        public bool NewerAvailable { get; set; }

        public string ErrorMessage { get; set; } = string.Empty;
    }

    /// <summary>
    /// Prüft die portable Ollama-Runtime in AI_DOK-V01 beim Hersteller (ollama.com).
    /// Modelle im Ordner models bleiben erhalten.
    /// </summary>
    internal static class OllamaUpdateCheckService
    {
        public const string ManufacturerZipUrl = "https://ollama.com/download/ollama-windows-amd64.zip";
        public const string WindowsAssetName = "ollama-windows-amd64.zip";

        public static async Task<OllamaUpdateInfo> CheckAsync(CancellationToken cancellationToken = default)
        {
            var settings = AiSettingsStore.Load();
            var info = new OllamaUpdateInfo
            {
                KiDirectory = OllamaPortableService.ResolveKiDirectory(settings),
                FolderFound = OllamaPortableService.IsPortableLayoutReady(settings),
                InstalledVersion = OllamaPortableService.TryReadVersion(settings) ?? string.Empty,
                AssetName = WindowsAssetName,
                DownloadUrl = ManufacturerZipUrl
            };

            if (!ShouldOfferRuntimeUpdate(settings))
                return info;

            try
            {
                AppSecurityService.EnsureSecureTransport();
                await ProbeManufacturerAsync(info, cancellationToken).ConfigureAwait(false);

                if (info.FolderFound
                    && TryParseVersion(info.LatestVersion, out var latest)
                    && TryParseVersion(info.InstalledVersion, out var installed))
                {
                    info.NewerAvailable = latest > installed;
                }
            }
            catch (Exception ex)
            {
                info.ErrorMessage = ex.Message;
            }

            return info;
        }

        /// <summary>
        /// Ollama-Update nur, wenn die große KI manuell eingeschaltet ist.
        /// Nach Neuinstallation ist sie aus – dann kein Hinweis.
        /// </summary>
        public static bool ShouldOfferRuntimeUpdate(AiSettings settings = null)
        {
            settings = settings ?? AiSettingsStore.Load();
            return settings != null && settings.OllamaEnabled;
        }

        public static async Task<string> UpdateRuntimeAsync(
            OllamaUpdateInfo info,
            IProgress<UpdateProgressInfo> progress,
            CancellationToken cancellationToken)
        {
            if (info == null)
                throw new InvalidOperationException("Kein Ollama-Update gefunden.");

            info.DownloadUrl = ManufacturerZipUrl;
            if (!Uri.TryCreate(info.DownloadUrl, UriKind.Absolute, out var startUri)
                || !AppSecurityService.IsTrustedOllamaManufacturerUrl(startUri))
            {
                throw new InvalidOperationException("Ollama-Download-URL ist nicht vertrauenswürdig.");
            }

            var settings = AiSettingsStore.Load();
            var kiDir = string.IsNullOrWhiteSpace(info.KiDirectory)
                ? OllamaPortableService.ResolveKiDirectory(settings)
                : info.KiDirectory;
            if (string.IsNullOrWhiteSpace(kiDir))
                throw new InvalidOperationException("KI-Ordner AI_DOK-V01 nicht gefunden.");

            Directory.CreateDirectory(kiDir);
            var downloadDir = Path.Combine(AppInfo.UserDataDirectory, "Tools", "ollama-updates");
            Directory.CreateDirectory(downloadDir);
            var zipPath = Path.Combine(downloadDir, AppSecurityService.SanitizeFileName(
                string.IsNullOrWhiteSpace(info.AssetName) ? WindowsAssetName : info.AssetName));

            Report(progress, 2, "Ollama wird vom Hersteller geladen…");
            AppSecurityService.EnsureSecureTransport();
            await FollowManufacturerAsync(ManufacturerZipUrl, zipPath, progress, cancellationToken).ConfigureAwait(false);

            if (!File.Exists(zipPath) || new FileInfo(zipPath).Length < 1024 * 1024)
                throw new InvalidOperationException("Ollama-Paket konnte nicht heruntergeladen werden.");

            Report(progress, 82, "Ollama-Dienst wird beendet…");
            OllamaPortableService.StopServer();
            await Task.Delay(800, cancellationToken).ConfigureAwait(false);

            Report(progress, 86, "Ollama wird entpackt (Modelle bleiben)…");
            ExtractRuntimeZip(zipPath, kiDir, cancellationToken);

            var exe = Path.Combine(kiDir, OllamaPortableService.OllamaExeName);
            if (!File.Exists(exe))
                throw new InvalidOperationException("Nach dem Update fehlt ollama.exe im KI-Ordner.");

            Report(progress, 96, "Signatur wird geprüft…");
            if (!AppSecurityService.TryVerifyFileSignature(exe, out var signatureMessage))
            {
                throw new InvalidOperationException(
                    "Ollama-Datei ist nicht gültig signiert. Installation abgebrochen.\n" + signatureMessage);
            }

            Report(progress, 100, "Ollama wurde aktualisiert.");
            return exe;
        }

        private static async Task ProbeManufacturerAsync(OllamaUpdateInfo info, CancellationToken cancellationToken)
        {
            var probe = await FollowManufacturerAsync(ManufacturerZipUrl, saveToPath: null, progress: null, cancellationToken)
                .ConfigureAwait(false);
            info.LatestVersion = probe.LatestVersion ?? string.Empty;
            info.AssetSizeBytes = probe.SizeBytes;
            info.DownloadUrl = ManufacturerZipUrl;
        }

        private static async Task<ManufacturerProbe> FollowManufacturerAsync(
            string startUrl,
            string saveToPath,
            IProgress<UpdateProgressInfo> progress,
            CancellationToken cancellationToken)
        {
            var result = new ManufacturerProbe();
            var handler = new HttpClientHandler { AllowAutoRedirect = false };
            using (handler)
            using (var client = new HttpClient(handler))
            {
                client.Timeout = string.IsNullOrWhiteSpace(saveToPath)
                    ? TimeSpan.FromSeconds(20)
                    : TimeSpan.FromMinutes(40);
                client.DefaultRequestHeaders.UserAgent.ParseAdd(AppInfo.ProductName + "/" + AppInfo.DisplayVersion);

                var url = startUrl;
                for (var hop = 0; hop < 8; hop++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)
                        || !AppSecurityService.IsTrustedOllamaManufacturerUrl(uri))
                    {
                        throw new InvalidOperationException("Ollama-Hersteller-URL ist nicht vertrauenswürdig.");
                    }

                    TryReadVersionFromUrl(url, result);

                    using (var request = new HttpRequestMessage(HttpMethod.Get, uri))
                    using (var response = await client.SendAsync(
                        request,
                        HttpCompletionOption.ResponseHeadersRead,
                        cancellationToken).ConfigureAwait(false))
                    {
                        var code = (int)response.StatusCode;
                        if (code >= 300 && code < 400)
                        {
                            var location = response.Headers.Location;
                            if (location == null)
                                throw new InvalidOperationException("Ollama-Download ohne Weiterleitung.");
                            url = new Uri(uri, location).AbsoluteUri;
                            TryReadVersionFromUrl(url, result);
                            continue;
                        }

                        response.EnsureSuccessStatusCode();
                        result.SizeBytes = response.Content.Headers.ContentLength ?? 0L;
                        result.FinalUrl = url;
                        if (string.IsNullOrWhiteSpace(saveToPath))
                            return result;

                        using (var source = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
                        using (var destination = File.Create(saveToPath))
                        {
                            var buffer = new byte[81920];
                            long readTotal = 0;
                            int read;
                            var total = result.SizeBytes;
                            while ((read = await source.ReadAsync(buffer, 0, buffer.Length, cancellationToken).ConfigureAwait(false)) > 0)
                            {
                                await destination.WriteAsync(buffer, 0, read, cancellationToken).ConfigureAwait(false);
                                readTotal += read;
                                var percent = total > 0
                                    ? 2 + (int)(readTotal * 74L / total)
                                    : 40;
                                Report(progress, percent, "Ollama wird vom Hersteller geladen…", readTotal, total);
                            }
                        }

                        return result;
                    }
                }
            }

            throw new InvalidOperationException("Ollama-Hersteller-Download fehlgeschlagen.");
        }

        private static void TryReadVersionFromUrl(string url, ManufacturerProbe result)
        {
            if (result == null || !string.IsNullOrWhiteSpace(result.LatestVersion) || string.IsNullOrWhiteSpace(url))
                return;

            var match = Regex.Match(url, @"/download/v?(\d+\.\d+\.\d+)/", RegexOptions.IgnoreCase);
            if (match.Success)
                result.LatestVersion = match.Groups[1].Value;
        }

        private sealed class ManufacturerProbe
        {
            public string LatestVersion { get; set; }

            public long SizeBytes { get; set; }

            public string FinalUrl { get; set; }
        }

        private static bool TryParseVersion(string text, out Version version)
        {
            version = null;
            if (string.IsNullOrWhiteSpace(text))
                return false;

            var match = Regex.Match(text.Trim(), @"(\d+)\.(\d+)\.(\d+)");
            if (!match.Success)
                return false;

            version = new Version(
                int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture),
                int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture),
                int.Parse(match.Groups[3].Value, CultureInfo.InvariantCulture));
            return true;
        }

        private static void ExtractRuntimeZip(string zipFilePath, string kiDirectory, CancellationToken cancellationToken)
        {
            var root = Path.GetFullPath(kiDirectory);
            if (!root.EndsWith(Path.DirectorySeparatorChar.ToString()))
                root += Path.DirectorySeparatorChar;

            using (var archive = ZipFile.OpenRead(zipFilePath))
            {
                foreach (var entry in archive.Entries)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (string.IsNullOrWhiteSpace(entry.Name))
                        continue;

                    var relative = (entry.FullName ?? string.Empty).Replace('/', Path.DirectorySeparatorChar);
                    if (StartsWithFolder(relative, "models") || StartsWithFolder(relative, "home"))
                        continue;

                    var entryPath = Path.GetFullPath(Path.Combine(kiDirectory, relative));
                    if (!entryPath.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidOperationException("Ungültiger ZIP-Eintrag blockiert: " + entry.FullName);

                    var entryDir = Path.GetDirectoryName(entryPath);
                    if (!string.IsNullOrWhiteSpace(entryDir))
                        Directory.CreateDirectory(entryDir);

                    entry.ExtractToFile(entryPath, true);
                }
            }
        }

        private static bool StartsWithFolder(string relativePath, string folderName)
        {
            if (string.IsNullOrWhiteSpace(relativePath) || string.IsNullOrWhiteSpace(folderName))
                return false;

            var prefix = folderName + Path.DirectorySeparatorChar;
            return relativePath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                   || string.Equals(relativePath, folderName, StringComparison.OrdinalIgnoreCase);
        }

        private static void Report(
            IProgress<UpdateProgressInfo> progress,
            int percent,
            string message,
            long bytesRead = 0,
            long totalBytes = 0)
        {
            progress?.Report(new UpdateProgressInfo(percent, message, bytesRead, totalBytes));
        }
    }
}
