using System;

namespace PDF_umbenenen.Models
{
    internal sealed class DemoLicenseStatus
    {
        public bool IsDemoEdition { get; set; }

        public bool IsExpired { get; set; }

        public bool IsFirstRun { get; set; }

        public DateTime FirstRunLocal { get; set; }

        public DateTime ExpiresLocal { get; set; }

        public int DaysRemaining { get; set; }

        public string TitleSuffix { get; set; }

        public string SummaryText { get; set; }

        public string VersionLine { get; set; }
    }
}
