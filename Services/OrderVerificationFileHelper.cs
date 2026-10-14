using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace PDF_umbenenen.Services
{
    internal static class OrderVerificationFileHelper
    {
        public const string CanonicalFileName = "Bestellung.pdf";
        public const string CanonicalTextFileName = "Bestellung.txt";

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

        public static bool IsOrderTextExtension(string pathOrExtension)
        {
            var ext = pathOrExtension ?? string.Empty;
            if (!ext.StartsWith(".", StringComparison.Ordinal))
                ext = Path.GetExtension(ext) ?? string.Empty;
            ext = ext.ToLowerInvariant();
            return ext == ".txt" || ext == ".csv";
        }

        public static bool IsOrderListExtension(string pathOrExtension)
        {
            var ext = pathOrExtension ?? string.Empty;
            if (!ext.StartsWith(".", StringComparison.Ordinal))
                ext = Path.GetExtension(ext) ?? string.Empty;
            ext = ext.ToLowerInvariant();
            return ext == ".pdf" || IsOrderTextExtension(ext);
        }

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

            if (name.Equals(Path.GetFileNameWithoutExtension(CanonicalFileName), StringComparison.OrdinalIgnoreCase)
                || name.Equals(Path.GetFileNameWithoutExtension(CanonicalTextFileName), StringComparison.OrdinalIgnoreCase))
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

        public static string GetCanonicalTextPath(string workspaceFolder, string preferredExtension = ".txt")
        {
            var ext = (preferredExtension ?? ".txt").ToLowerInvariant();
            if (ext != ".txt" && ext != ".csv")
                ext = ".txt";
            return Path.Combine(workspaceFolder, "Bestellung" + ext);
        }

        public static string GetCanonicalPathForSource(string workspaceFolder, string sourceFile)
        {
            if (IsOrderTextExtension(sourceFile))
                return GetCanonicalTextPath(workspaceFolder, Path.GetExtension(sourceFile));
            return GetCanonicalPath(workspaceFolder);
        }

        /// <summary>Findet Bestellung.pdf / Bestellung.txt / Bestellung.csv oder Dateinamen mit Bestell-Hinweis.</summary>
        public static string FindOrderListInFolder(string workspaceFolder)
        {
            if (string.IsNullOrWhiteSpace(workspaceFolder) || !Directory.Exists(workspaceFolder))
                return string.Empty;

            foreach (var name in new[] { CanonicalFileName, CanonicalTextFileName, "Bestellung.csv" })
            {
                var path = Path.Combine(workspaceFolder, name);
                if (File.Exists(path))
                    return path;
            }

            return Directory.GetFiles(workspaceFolder, "*.*", SearchOption.TopDirectoryOnly)
                .Where(f => IsOrderListExtension(f) && IsOrderVerificationFileName(Path.GetFileName(f)))
                .OrderBy(f =>
                {
                    var ext = Path.GetExtension(f)?.ToLowerInvariant() ?? string.Empty;
                    if (ext == ".pdf") return 0;
                    if (ext == ".txt") return 1;
                    return 2;
                })
                .FirstOrDefault() ?? string.Empty;
        }
    }
}
