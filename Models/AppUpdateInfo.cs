using System;

namespace PDF_umbenenen.Models
{
    public sealed class AppUpdateInfo
    {
        public bool UpdateAvailable { get; set; }

        public Version RemoteVersion { get; set; }

        public string ReleaseTag { get; set; } = string.Empty;

        public string ReleaseNotes { get; set; } = string.Empty;

        public string DownloadUrl { get; set; } = string.Empty;

        public string AssetName { get; set; } = string.Empty;

        public string ExpectedSha256 { get; set; } = string.Empty;

        public long AssetId { get; set; }

        public string ErrorMessage { get; set; } = string.Empty;
    }
}
