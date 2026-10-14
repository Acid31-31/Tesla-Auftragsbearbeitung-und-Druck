using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using PDF_umbenenen.Models;
using PDF_umbenenen.Services.Ai;

namespace PDF_umbenenen.Services
{
    public sealed class DrawingVisionUpdateInfo
    {
        public string ModuleName { get; set; } = OllamaPortableService.DefaultVisionModel;

        public string ModuleDisplayName { get; set; } = "Ollama · moondream";

        public string InstalledVersion { get; set; } = string.Empty;

        public string LatestVersion { get; set; } = string.Empty;

        public string InstalledDigest { get; set; } = string.Empty;

        public string LatestDigest { get; set; } = string.Empty;

        public bool FolderFound { get; set; }

        public bool ModelInstalled { get; set; }

        public bool NewerAvailable { get; set; }

        public string ErrorMessage { get; set; } = string.Empty;

        public string LibraryUrl { get; set; } = "https://ollama.com/library/moondream";
    }

    /// <summary>
    /// Eigenes Internet-Update für das eingebaute Zeichnungs-Vision-Modul (Ollama moondream).
    /// Quelle: ollama.com / registry.ollama.ai, Installation in AI_DOK-V01.
    /// </summary>
    internal static class DrawingVisionUpdateCheckService
    {
        public const string LibraryPageUrl = "https://ollama.com/library/moondream";
        public const string ManifestUrl = "https://registry.ollama.ai/v2/library/moondream/manifests/latest";

        public static async Task<DrawingVisionUpdateInfo> CheckAsync(CancellationToken cancellationToken = default)
        {
            var settings = AiSettingsStore.Load();
            var module = OllamaPortableService.GetVisionModuleDisplayName(settings);
            var info = new DrawingVisionUpdateInfo
            {
                ModuleName = OllamaPortableService.DefaultVisionModel,
                ModuleDisplayName = module,
                FolderFound = OllamaPortableService.IsPortableLayoutReady(settings),
                ModelInstalled = !string.IsNullOrWhiteSpace(OllamaPortableService.FindInstalledVisionModel(settings)),
                InstalledDigest = OllamaPortableService.ReadInstalledVisionDigest(settings) ?? string.Empty,
                LibraryUrl = LibraryPageUrl
            };

            info.InstalledVersion = FormatInstalled(info);

            try
            {
                AppSecurityService.EnsureSecureTransport();
                info.LatestDigest = await ReadRemoteDigestAsync(cancellationToken).ConfigureAwait(false);
                if (!string.IsNullOrWhiteSpace(info.LatestDigest))
                    info.LatestVersion = ShortDigest(info.LatestDigest);

                if (!info.FolderFound)
                {
                    info.NewerAvailable = false;
                }
                else if (!info.ModelInstalled)
                {
                    info.NewerAvailable = true;
                }
                else if (!string.IsNullOrWhiteSpace(info.LatestDigest)
                         && !string.IsNullOrWhiteSpace(info.InstalledDigest)
                         && !string.Equals(info.LatestDigest, info.InstalledDigest, StringComparison.OrdinalIgnoreCase))
                {
                    info.NewerAvailable = true;
                }
            }
            catch (Exception ex)
            {
                info.ErrorMessage = ex.Message;
                if (info.FolderFound && !info.ModelInstalled)
                    info.NewerAvailable = true;
            }

            return info;
        }

        public static async Task UpdateModelAsync(
            DrawingVisionUpdateInfo info,
            IProgress<UpdateProgressInfo> progress,
            CancellationToken cancellationToken)
        {
            var model = info != null && !string.IsNullOrWhiteSpace(info.ModuleName)
                ? info.ModuleName
                : OllamaPortableService.DefaultVisionModel;

            await OllamaPortableService.PullVisionModelAsync(model, progress, cancellationToken)
                .ConfigureAwait(false);
            DrawingVisionRecognitionService.ResetRuntimeState();
        }

        private static string FormatInstalled(DrawingVisionUpdateInfo info)
        {
            if (info == null || !info.ModelInstalled)
                return "nicht gefunden";

            if (!string.IsNullOrWhiteSpace(info.InstalledDigest))
                return info.ModuleDisplayName + "  " + ShortDigest(info.InstalledDigest);

            return info.ModuleDisplayName;
        }

        private static string ShortDigest(string digest)
        {
            var value = (digest ?? string.Empty).Trim();
            if (value.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase))
                value = value.Substring(7);
            if (value.Length <= 12)
                return value;
            return value.Substring(0, 12);
        }

        private static async Task<string> ReadRemoteDigestAsync(CancellationToken cancellationToken)
        {
            if (!Uri.TryCreate(ManifestUrl, UriKind.Absolute, out var uri)
                || !AppSecurityService.IsTrustedOllamaRegistryUrl(uri))
            {
                throw new InvalidOperationException("Vision-Update-Prüfung blockiert (URL).");
            }

            using (var client = new HttpClient())
            {
                client.Timeout = TimeSpan.FromSeconds(8);
                client.DefaultRequestHeaders.UserAgent.ParseAdd("DOK-V01-VisionCheck");
                client.DefaultRequestHeaders.Accept.Clear();
                client.DefaultRequestHeaders.Accept.Add(
                    new MediaTypeWithQualityHeaderValue("application/vnd.docker.distribution.manifest.v2+json"));

                using (var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
                {
                    cts.CancelAfter(TimeSpan.FromSeconds(8));
                    using (var response = await client.GetAsync(uri, cts.Token).ConfigureAwait(false))
                    {
                        var digestHeader = response.Headers.ETag != null
                            ? response.Headers.ETag.Tag
                            : null;
                        if (response.Headers.TryGetValues("Docker-Content-Digest", out var values))
                        {
                            foreach (var value in values)
                            {
                                var parsed = ExtractSha256(value);
                                if (!string.IsNullOrWhiteSpace(parsed))
                                    return parsed;
                            }
                        }

                        var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                        var fromBody = ExtractSha256(body) ?? ExtractSha256(digestHeader);
                        if (!string.IsNullOrWhiteSpace(fromBody))
                            return fromBody;

                        if (!response.IsSuccessStatusCode)
                            throw new InvalidOperationException("Vision-Update-Prüfung: HTTP " + (int)response.StatusCode);
                    }
                }
            }

            return string.Empty;
        }

        private static string ExtractSha256(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return string.Empty;

            var match = Regex.Match(text, @"sha256:([a-fA-F0-9]{64})");
            if (match.Success)
                return match.Groups[1].Value.ToLowerInvariant();

            match = Regex.Match(text, @"\b([a-fA-F0-9]{64})\b");
            return match.Success ? match.Groups[1].Value.ToLowerInvariant() : string.Empty;
        }
    }
}
