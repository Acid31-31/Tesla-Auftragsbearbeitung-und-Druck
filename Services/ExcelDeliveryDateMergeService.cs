using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace PDF_umbenenen.Services
{
    internal static class ExcelDeliveryDateMergeService
    {
        public static bool IsAngebotExcelFile(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return false;

            var name = Path.GetFileNameWithoutExtension(path) ?? string.Empty;
            return name.IndexOf("Angebot", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        public static string FindAngebotExcelPath(string folder, string primaryExcelPath)
        {
            if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
                return null;

            var fixedCandidates = new[]
            {
                Path.Combine(folder, "Daten_Angebot.xlsx"),
                Path.Combine(folder, "Daten_Angebot.xls")
            };

            foreach (var candidate in fixedCandidates)
            {
                if (File.Exists(candidate))
                    return candidate;
            }

            var primaryFullPath = string.IsNullOrWhiteSpace(primaryExcelPath)
                ? string.Empty
                : Path.GetFullPath(primaryExcelPath);

            return Directory.GetFiles(folder, "*Angebot*.xls*", SearchOption.TopDirectoryOnly)
                .Where(path => !string.Equals(Path.GetFullPath(path), primaryFullPath, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(path => File.GetLastWriteTimeUtc(path))
                .FirstOrDefault();
        }

        /// <summary>
        /// Eine Excel reicht. Bei zwei Dateien: Standard (Daten.xlsx) als Basis, Angebot separat.
        /// Nur Angebot vorhanden → diese Datei wird verwendet.
        /// </summary>
        public static string ResolvePrimaryExcelPath(string folder)
        {
            if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
                return null;

            var standardCandidates = new[]
            {
                Path.Combine(folder, "Daten.xlsx"),
                Path.Combine(folder, "Daten.xls")
            };

            foreach (var candidate in standardCandidates)
            {
                if (File.Exists(candidate))
                    return candidate;
            }

            var angebotCandidates = new[]
            {
                Path.Combine(folder, "Daten_Angebot.xlsx"),
                Path.Combine(folder, "Daten_Angebot.xls")
            };

            foreach (var candidate in angebotCandidates)
            {
                if (File.Exists(candidate))
                    return candidate;
            }

            return Directory.GetFiles(folder, "*.xls*", SearchOption.TopDirectoryOnly)
                .OrderByDescending(path => File.GetLastWriteTimeUtc(path))
                .FirstOrDefault();
        }

        public static bool HasSeparateAngebotExcel(string folder, string primaryExcelPath)
        {
            var angebotPath = FindAngebotExcelPath(folder, primaryExcelPath);
            return !string.IsNullOrWhiteSpace(angebotPath) && File.Exists(angebotPath);
        }

        public static string ResolveEffectiveDeliveryDate(string standardDate, string quoteDate)
        {
            if (!string.IsNullOrWhiteSpace(quoteDate))
                return quoteDate.Trim();

            if (!string.IsNullOrWhiteSpace(standardDate))
                return standardDate.Trim();

            return string.Empty;
        }

        public static void ApplyQuoteDates<T>(
            IList<T> targetRows,
            IList<T> quoteRows,
            Func<T, IEnumerable<string>> getKeys,
            Func<T, string> getStandardDate,
            Func<T, string> getQuoteDate,
            Action<T, string> setDeliveryDate)
        {
            if (targetRows == null || targetRows.Count == 0 || quoteRows == null || quoteRows.Count == 0)
                return;

            var quoteByKey = new Dictionary<string, T>(StringComparer.OrdinalIgnoreCase);
            foreach (var quoteRow in quoteRows)
            {
                foreach (var key in getKeys(quoteRow) ?? Enumerable.Empty<string>())
                {
                    if (string.IsNullOrWhiteSpace(key) || quoteByKey.ContainsKey(key))
                        continue;

                    quoteByKey[key] = quoteRow;
                }
            }

            foreach (var row in targetRows)
            {
                T matchedQuote = default;
                var hasMatch = false;

                foreach (var key in getKeys(row) ?? Enumerable.Empty<string>())
                {
                    if (string.IsNullOrWhiteSpace(key))
                        continue;

                    if (quoteByKey.TryGetValue(key, out matchedQuote))
                    {
                        hasMatch = true;
                        break;
                    }
                }

                if (!hasMatch || matchedQuote == null)
                    continue;

                var quoteDate = getQuoteDate(matchedQuote);
                if (string.IsNullOrWhiteSpace(quoteDate))
                    quoteDate = getStandardDate(matchedQuote);

                var effective = ResolveEffectiveDeliveryDate(getStandardDate(row), quoteDate);
                if (!string.IsNullOrWhiteSpace(effective))
                    setDeliveryDate(row, effective);
            }
        }
    }
}
