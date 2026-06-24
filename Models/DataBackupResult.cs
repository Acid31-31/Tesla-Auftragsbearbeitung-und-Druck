using System.Collections.Generic;

namespace PDF_umbenenen.Models
{
    internal sealed class DataBackupResult
    {
        public bool Success { get; set; }

        public string BackupFolder { get; set; } = string.Empty;

        public string ZipPath { get; set; } = string.Empty;

        public int FilesCopied { get; set; }

        public long TotalBytes { get; set; }

        public List<string> IncludedSections { get; } = new List<string>();

        public List<string> Warnings { get; } = new List<string>();

        public string ErrorMessage { get; set; } = string.Empty;

        public string SummaryText
        {
            get
            {
                if (!Success)
                    return "Datensicherung fehlgeschlagen: " + ErrorMessage;

                var sizeMb = TotalBytes / (1024.0 * 1024.0);
                var zipInfo = string.IsNullOrWhiteSpace(ZipPath)
                    ? string.Empty
                    : " · ZIP: " + ZipPath;

                return string.Format(
                    System.Globalization.CultureInfo.InvariantCulture,
                    "Datensicherung abgeschlossen: {0} Datei(en), {1:0.##} MB{2}",
                    FilesCopied,
                    sizeMb,
                    zipInfo);
            }
        }
    }
}
