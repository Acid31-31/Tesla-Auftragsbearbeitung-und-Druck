using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using System.Windows;

namespace PDF_umbenenen.Services
{
    internal static class CertificateTrustService
    {
        public static bool TryInstallPublisherCertificateWithConsent()
        {
            var cerPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, AppInfo.CodeSigningCerFileName);
            if (!File.Exists(cerPath))
                return false;

            X509Certificate2 certificate;
            try
            {
                certificate = new X509Certificate2(cerPath);
            }
            catch
            {
                return false;
            }

            if (IsPublisherTrusted(certificate))
                return true;

            var answer = MessageBox.Show(
                "Für eine dauerhafte Freigabe durch Windows und Antivirus (Avira u. a.) "
                + "sollte das Herausgeber-Zertifikat jetzt einmalig installiert werden.\n\n"
                + "Herausgeber: " + certificate.Subject + "\n\n"
                + "Bitte auf JA klicken – danach sollte Antivirus nicht bei jedem Start erneut warnen.",
                AppInfo.ProductName + " – Einmalige Vertrauensfreigabe",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (answer != MessageBoxResult.Yes)
                return false;

            return TryInstallPublisherCertificate();
        }

        public static bool TryInstallPublisherCertificate()
        {
            var cerPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, AppInfo.CodeSigningCerFileName);
            if (!File.Exists(cerPath))
                return false;

            X509Certificate2 certificate;
            try
            {
                certificate = new X509Certificate2(cerPath);
            }
            catch
            {
                return false;
            }

            if (IsPublisherTrusted(certificate))
                return true;

            try
            {
                AddToStoreIfMissing(StoreName.TrustedPublisher, certificate);
                return true;
            }
            catch
            {
                return false;
            }
        }

        public static bool IsPublisherTrustedFromBundledCertificate()
        {
            var cerPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, AppInfo.CodeSigningCerFileName);
            if (!File.Exists(cerPath))
                return false;

            try
            {
                var certificate = new X509Certificate2(cerPath);
                return IsPublisherTrusted(certificate);
            }
            catch
            {
                return false;
            }
        }

        private static bool IsPublisherTrusted(X509Certificate2 certificate)
        {
            return IsInStore(StoreName.TrustedPublisher, certificate.Thumbprint);
        }

        private static bool IsInStore(StoreName storeName, string thumbprint)
        {
            using (var store = new X509Store(storeName, StoreLocation.CurrentUser))
            {
                store.Open(OpenFlags.ReadOnly);
                return store.Certificates
                    .Cast<X509Certificate2>()
                    .Any(c => string.Equals(c.Thumbprint, thumbprint, StringComparison.OrdinalIgnoreCase));
            }
        }

        private static void AddToStoreIfMissing(StoreName storeName, X509Certificate2 certificate)
        {
            if (IsInStore(storeName, certificate.Thumbprint))
                return;

            using (var store = new X509Store(storeName, StoreLocation.CurrentUser))
            {
                store.Open(OpenFlags.ReadWrite);
                store.Add(certificate);
            }
        }
    }
}
