using System;
using System.IO;
using System.Xml.Linq;

namespace PDF_umbenenen.Models
{
    internal sealed class InstallSessionOptions
    {
        public bool LicenseAccepted { get; set; }

        public bool InstallPublisherCertificate { get; set; } = true;

        public bool LaunchAfterInstall { get; set; } = true;

        public string SourceDirectory { get; set; } = string.Empty;
    }
}
