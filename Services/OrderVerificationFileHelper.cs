using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

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
            "bestellliste",
            "bestellnr",
            "ordernumber",
            "purchaseorder"
        };

        private static readonly Regex LeadingSequencePrefix = new Regex(
            @"^\d{1,3}_",
            RegexOptions.CultureInvariant | RegexOptions.Compiled);

        public static bool IsOrderVerificationFileName(string fileName)
        {
            if (string.IsNullOrWhiteSpace(fileName))
                return false;

            var name = Path.GetFileNameWithoutExtension(fileName).Trim();
            if (name.Length == 0)
                return false;

            // Bereits sortierte Dateien: 01_5101426055 → 5101426055
            while (LeadingSequencePrefix.IsMatch(name))
                name = LeadingSequencePrefix.Replace(name, string.Empty).Trim();

            if (name.Equals(Path.GetFileNameWithoutExtension(CanonicalFileName), StringComparison.OrdinalIgnoreCase))
                return true;

            var lower = name.ToLowerInvariant();
            foreach (var hint in NameHints)
            {
                if (lower.Contains(hint))
                    return true;
            }

            // Reine Auftrags-/Bestellnummern (z. B. 5101426055.pdf) – keine Zeichnungsnummer
            if (name.All(char.IsDigit) && name.Length >= 8)
                return true;

            return false;
        }

        public static string GetCanonicalPath(string workspaceFolder)
        {
            return Path.Combine(workspaceFolder, CanonicalFileName);
        }
    }
}
