using System;
using System.Globalization;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace PDF_umbenenen.Services
{
    public sealed class TesseractUpdateInfo
    {
        public string InstalledVersion { get; set; } = string.Empty;

        public string LatestVersion { get; set; } = string.Empty;

        public string LatestFileName { get; set; } = string.Empty;

        public string DownloadUrl { get; set; } = string.Empty;

        public bool NewerAvailable { get; set; }

        public string ErrorMessage { get; set; } = string.Empty;
    }

    /// <summary>
    /// Liest lokale Tesseract-Version und prüft die UB-Mannheim-Liste (Anzeige, keine Auto-Installation).
    /// </summary>
    internal static class TesseractUpdateCheckService
    {
        public const string IndexUrl = "https://digi.bib.uni-mannheim.de/tesseract/";

        private static readonly Regex W64SetupRegex = new Regex(
            @"tesseract-ocr-w64-setup-v?(\d+\.\d+\.\d+)(?:\.(\d{8}))?\.exe",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        public static async Task<TesseractUpdateInfo> CheckAsync(CancellationToken cancellationToken = default)
        {
            var info = new TesseractUpdateInfo
            {
                InstalledVersion = Cli.TesseractCliService.TryReadVersion() ?? string.Empty
            };

            try
            {
                AppSecurityService.EnsureSecureTransport();
                if (!Uri.TryCreate(IndexUrl, UriKind.Absolute, out var uri)
                    || !AppSecurityService.IsTrustedDownloadUrl(uri))
                {
                    info.ErrorMessage = "Tesseract-Update-Prüfung blockiert (URL).";
                    return info;
                }

                string html;
                using (var client = new HttpClient())
                {
                    client.Timeout = TimeSpan.FromSeconds(8);
                    client.DefaultRequestHeaders.UserAgent.ParseAdd("DOK-V01-TesseractCheck");
                    html = await client.GetStringAsync(uri).ConfigureAwait(false);
                }

                cancellationToken.ThrowIfCancellationRequested();
                PickLatestStableW64(html, info);

                if (!string.IsNullOrWhiteSpace(info.InstalledVersion)
                    && TryParseVersion(info.LatestFileName, out var latest)
                    && TryParseVersion(info.InstalledVersion, out var installed))
                {
                    var latestCore = new Version(latest.Major, latest.Minor, latest.Build);
                    var installedCore = new Version(installed.Major, installed.Minor, installed.Build);
                    info.NewerAvailable = latestCore > installedCore;
                }
            }
            catch (Exception ex)
            {
                info.ErrorMessage = ex.Message;
            }

            return info;
        }

        public static string FormatStatus(TesseractUpdateInfo info)
        {
            if (info == null)
                return "Tesseract: Status unbekannt";

            var installed = string.IsNullOrWhiteSpace(info.InstalledVersion)
                ? "nicht gefunden"
                : info.InstalledVersion;

            if (!string.IsNullOrWhiteSpace(info.ErrorMessage) && string.IsNullOrWhiteSpace(info.LatestVersion))
                return "Tesseract: " + installed + "  ·  Neueste Version: Prüfung fehlgeschlagen";

            if (string.IsNullOrWhiteSpace(info.LatestVersion))
                return "Tesseract: " + installed;

            if (info.NewerAvailable)
            {
                return "Tesseract: " + installed
                    + "  ·  Neueste (UB Mannheim): " + info.LatestVersion
                    + " — nicht automatisch installiert";
            }

            return "Tesseract: " + installed + "  ·  Neueste: " + info.LatestVersion + " (aktuell)";
        }

        private static void PickLatestStableW64(string html, TesseractUpdateInfo info)
        {
            if (string.IsNullOrWhiteSpace(html) || info == null)
                return;

            Version best = null;
            string bestName = null;
            string bestLabel = null;

            foreach (Match match in W64SetupRegex.Matches(html))
            {
                var fileName = match.Value;
                if (fileName.IndexOf("alpha", StringComparison.OrdinalIgnoreCase) >= 0
                    || fileName.IndexOf("beta", StringComparison.OrdinalIgnoreCase) >= 0
                    || fileName.IndexOf("rc", StringComparison.OrdinalIgnoreCase) >= 0
                    || fileName.IndexOf("dev", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    continue;
                }

                var label = match.Groups[1].Value;
                if (match.Groups[2].Success)
                    label += "." + match.Groups[2].Value;

                if (!TryParseVersion(label, out var parsed))
                    continue;

                if (best == null || parsed > best)
                {
                    best = parsed;
                    bestName = fileName;
                    bestLabel = match.Groups[1].Value;
                    if (match.Groups[2].Success)
                        bestLabel += " (" + match.Groups[2].Value + ")";
                }
            }

            if (best != null)
            {
                info.LatestVersion = bestLabel;
                info.LatestFileName = bestName;
                info.DownloadUrl = IndexUrl + bestName;
            }
        }

        private static bool TryParseVersion(string text, out Version version)
        {
            version = null;
            if (string.IsNullOrWhiteSpace(text))
                return false;

            var match = Regex.Match(text.Trim(), @"(\d+)\.(\d+)\.(\d+)(?:\.(\d+))?");
            if (!match.Success)
                return false;

            var major = int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
            var minor = int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture);
            var build = int.Parse(match.Groups[3].Value, CultureInfo.InvariantCulture);
            var rev = match.Groups[4].Success
                ? int.Parse(match.Groups[4].Value, CultureInfo.InvariantCulture)
                : 0;
            version = new Version(major, minor, build, rev);
            return true;
        }
    }
}
