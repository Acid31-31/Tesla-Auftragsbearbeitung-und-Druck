using System;
using System.IO;

namespace PDF_umbenenen.Services
{
    internal static class OrderVerificationFileHelper
    {
        public const string CanonicalFileName = "Bestellung.pdf";

        private static readonly string[] NameHints =
        {
            "bestellung",
            "bestell",
            "auftrag",
            "pruef",
            "prüf",
            "orderlist",
            "purchase",
            "lieferliste",
            "bestellliste"
        };

        public static bool IsOrderVerificationFileName(string fileName)
        {
            if (string.IsNullOrWhiteSpace(fileName))
                return false;

            var name = Path.GetFileNameWithoutExtension(fileName).Trim();
            if (name.Equals(Path.GetFileNameWithoutExtension(CanonicalFileName), StringComparison.OrdinalIgnoreCase))
                return true;

            var lower = name.ToLowerInvariant();
            foreach (var hint in NameHints)
            {
                if (lower.Contains(hint))
                    return true;
            }

            return false;
        }

        public static string GetCanonicalPath(string workspaceFolder)
        {
            return Path.Combine(workspaceFolder, CanonicalFileName);
        }
    }
}
