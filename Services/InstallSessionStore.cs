using System;
using System.IO;
using System.Xml.Linq;
using PDF_umbenenen.Models;

namespace PDF_umbenenen.Services
{
    internal static class InstallSessionStore
    {
        private static string StorePath => Path.Combine(
            Path.GetTempPath(),
            AppInfo.ProductName,
            "install-session.xml");

        public static void SavePending(InstallSessionOptions options)
        {
            if (options == null)
                throw new ArgumentNullException(nameof(options));

            var storeDir = Path.GetDirectoryName(StorePath);
            if (!string.IsNullOrWhiteSpace(storeDir))
                Directory.CreateDirectory(storeDir);

            new XDocument(
                new XElement("InstallSession",
                    new XElement("LicenseAccepted", options.LicenseAccepted),
                    new XElement("InstallPublisherCertificate", options.InstallPublisherCertificate),
                    new XElement("LaunchAfterInstall", options.LaunchAfterInstall),
                    new XElement("InstallTesseractOcr", options.InstallTesseractOcr),
                    new XElement("SourceDirectory", options.SourceDirectory ?? string.Empty),
                    new XElement("TargetDirectory", options.TargetDirectory ?? string.Empty),
                    new XElement("CreatedUtc", DateTime.UtcNow.ToString("o"))))
                .Save(StorePath);
        }

        public static bool TryConsumePending(out InstallSessionOptions options)
        {
            options = null;
            if (!File.Exists(StorePath))
                return false;

            try
            {
                var doc = XDocument.Load(StorePath);
                var root = doc.Root;
                if (root == null)
                    return false;

                var createdText = (string)root.Element("CreatedUtc");
                if (!string.IsNullOrWhiteSpace(createdText)
                    && DateTime.TryParse(createdText, out var createdUtc)
                    && createdUtc < DateTime.UtcNow.AddHours(-1))
                {
                    Clear();
                    return false;
                }

                if (!bool.TryParse((string)root.Element("LicenseAccepted"), out var accepted) || !accepted)
                    return false;

                options = new InstallSessionOptions
                {
                    LicenseAccepted = true,
                    InstallPublisherCertificate = ParseBool(root.Element("InstallPublisherCertificate"), true),
                    LaunchAfterInstall = ParseBool(root.Element("LaunchAfterInstall"), true),
                    InstallTesseractOcr = ParseBool(root.Element("InstallTesseractOcr"), true),
                    SourceDirectory = (string)root.Element("SourceDirectory") ?? string.Empty,
                    TargetDirectory = (string)root.Element("TargetDirectory") ?? string.Empty
                };

                Clear();
                return !string.IsNullOrWhiteSpace(options.SourceDirectory)
                    && !string.IsNullOrWhiteSpace(options.TargetDirectory);
            }
            catch
            {
                Clear();
                return false;
            }
        }

        public static void Clear()
        {
            try
            {
                if (File.Exists(StorePath))
                    File.Delete(StorePath);
            }
            catch
            {
            }
        }

        private static bool ParseBool(XElement element, bool defaultValue)
        {
            return element == null
                ? defaultValue
                : bool.TryParse((string)element, out var value) ? value : defaultValue;
        }
    }
}
