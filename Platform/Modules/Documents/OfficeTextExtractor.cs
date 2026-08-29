using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Xml.Linq;
using PDF_umbenenen.Platform.Contracts;

namespace PDF_umbenenen.Platform.Modules.Documents
{
    internal static class OfficeTextExtractor
    {
        private static readonly XNamespace SpreadsheetNs =
            "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

        public static string ExtractText(string filePath)
        {
            if (!File.Exists(filePath))
                return string.Empty;

            var ext = Path.GetExtension(filePath)?.ToLowerInvariant();
            switch (ext)
            {
                case ".xlsx":
                    return ExtractXlsxText(filePath);
                case ".xls":
                    return ExtractLegacyHint(filePath);
                case ".docx":
                    return ExtractDocxText(filePath);
                case ".doc":
                    return ExtractLegacyHint(filePath);
                default:
                    return string.Empty;
            }
        }

        private static string ExtractXlsxText(string filePath)
        {
            var builder = new StringBuilder();
            try
            {
                using (var archive = ZipFile.OpenRead(filePath))
                {
                    var shared = LoadSharedStrings(archive);
                    var sheetEntries = archive.Entries
                        .Where(e => e.FullName.StartsWith("xl/worksheets/sheet", StringComparison.OrdinalIgnoreCase)
                                    && e.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase));

                    foreach (var entry in sheetEntries)
                    {
                        using (var stream = entry.Open())
                        {
                            var doc = XDocument.Load(stream);
                            foreach (var cell in doc.Descendants(SpreadsheetNs + "c"))
                            {
                                var value = ReadCell(cell, shared);
                                if (!string.IsNullOrWhiteSpace(value))
                                    builder.AppendLine(value);
                            }
                        }
                    }
                }
            }
            catch
            {
            }

            return builder.ToString();
        }

        private static string ExtractDocxText(string filePath)
        {
            try
            {
                using (var archive = ZipFile.OpenRead(filePath))
                {
                    var entry = archive.GetEntry("word/document.xml");
                    if (entry == null)
                        return string.Empty;

                    using (var stream = entry.Open())
                    {
                        var doc = XDocument.Load(stream);
                        return string.Join(" ",
                            doc.Descendants()
                                .Where(e => e.Name.LocalName == "t")
                                .Select(e => e.Value)
                                .Where(v => !string.IsNullOrWhiteSpace(v)));
                    }
                }
            }
            catch
            {
                return string.Empty;
            }
        }

        private static string ExtractLegacyHint(string filePath)
            => "Legacy-Office-Datei: " + Path.GetFileName(filePath);

        private static IList<string> LoadSharedStrings(ZipArchive archive)
        {
            var list = new List<string>();
            var entry = archive.GetEntry("xl/sharedStrings.xml");
            if (entry == null)
                return list;

            using (var stream = entry.Open())
            {
                var doc = XDocument.Load(stream);
                foreach (var si in doc.Descendants(SpreadsheetNs + "si"))
                {
                    var text = string.Concat(si.Descendants(SpreadsheetNs + "t").Select(t => t.Value));
                    list.Add(text);
                }
            }

            return list;
        }

        private static string ReadCell(XElement cell, IList<string> shared)
        {
            var type = cell.Attribute("t")?.Value;
            var valueEl = cell.Element(SpreadsheetNs + "v");
            if (valueEl == null)
                return string.Empty;

            if (type == "s" && int.TryParse(valueEl.Value, out var idx) && idx >= 0 && idx < shared.Count)
                return shared[idx];

            return valueEl.Value;
        }
    }
}
