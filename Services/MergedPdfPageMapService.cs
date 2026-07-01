using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using PDF_umbenenen.Models;

namespace PDF_umbenenen.Services
{
    internal static class MergedPdfPageMapService
    {
        public static List<MergedPdfPageEntry> Build(IEnumerable<string> orderedSourceFiles)
        {
            var map = new List<MergedPdfPageEntry>();
            var mergedPage = 1;

            foreach (var file in orderedSourceFiles ?? Enumerable.Empty<string>())
            {
                if (string.IsNullOrWhiteSpace(file) || !File.Exists(file))
                    continue;

                var pageCount = PdfWatermarkService.GetPageCount(file);
                for (var sourcePage = 1; sourcePage <= pageCount; sourcePage++)
                {
                    map.Add(new MergedPdfPageEntry
                    {
                        MergedPageNumber = mergedPage++,
                        SourcePdfPath = file,
                        SourcePageNumber = sourcePage
                    });
                }
            }

            return map;
        }

        public static void Save(string mergedPdfPath, IEnumerable<MergedPdfPageEntry> map)
        {
            if (string.IsNullOrWhiteSpace(mergedPdfPath))
                return;

            var root = new XElement("MergedPdfPageMap",
                new XElement("MergedPdf", mergedPdfPath),
                (map ?? Enumerable.Empty<MergedPdfPageEntry>()).Select(entry =>
                    new XElement("Page",
                        new XAttribute("merged", entry.MergedPageNumber),
                        new XAttribute("source", entry.SourcePdfPath ?? string.Empty),
                        new XAttribute("sourcePage", entry.SourcePageNumber))));

            File.WriteAllText(GetMapPath(mergedPdfPath), root.ToString());
        }

        public static List<MergedPdfPageEntry> TryLoad(string mergedPdfPath)
        {
            var mapPath = GetMapPath(mergedPdfPath);
            if (!File.Exists(mapPath))
                return new List<MergedPdfPageEntry>();

            try
            {
                return XDocument.Load(mapPath)
                    .Root?
                    .Elements("Page")
                    .Select(element => new MergedPdfPageEntry
                    {
                        MergedPageNumber = ParseInt(element.Attribute("merged")?.Value),
                        SourcePdfPath = element.Attribute("source")?.Value ?? string.Empty,
                        SourcePageNumber = ParseInt(element.Attribute("sourcePage")?.Value, 1)
                    })
                    .Where(entry => entry.MergedPageNumber > 0)
                    .OrderBy(entry => entry.MergedPageNumber)
                    .ToList()
                    ?? new List<MergedPdfPageEntry>();
            }
            catch
            {
                return new List<MergedPdfPageEntry>();
            }
        }

        public static string GetMapPath(string mergedPdfPath)
        {
            return mergedPdfPath + ".map.xml";
        }

        private static int ParseInt(string value, int fallback = 0)
        {
            return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result)
                ? result
                : fallback;
        }
    }
}
