using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography.X509Certificates;

namespace PDF_umbenenen.Services
{
    internal static class CertificateTrustService
    {
        private const string CerFileName = "CodeSigning.cer";

        /// <summary>
        /// Installiert das mitgelieferte Herausgeber-Zertifikat still in den Benutzer-Speicher,
        /// falls es noch nicht vertrauenswürdig ist (einmalig pro PC/Benutzer).
        /// </summary>
        public static void EnsurePublisherTrusted()
        {
            var cerPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, CerFileName);
            if (!File.Exists(cerPath))
                return;

            X509Certificate2 certificate;
            try
            {
                certificate = new X509Certificate2(cerPath);
            }
            catch
            {
                return;
            }

            if (IsTrusted(certificate))
                return;

            try
            {
                AddToStoreIfMissing(StoreName.Root, certificate);
                AddToStoreIfMissing(StoreName.TrustedPublisher, certificate);
            }
            catch
            {
                // App startet trotzdem; ohne Zertifikat können AV-Warnungen auftreten.
            }
        }

        private static bool IsTrusted(X509Certificate2 certificate)
        {
            return IsInStore(StoreName.Root, certificate.Thumbprint)
                && IsInStore(StoreName.TrustedPublisher, certificate.Thumbprint);
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
