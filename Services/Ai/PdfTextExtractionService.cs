using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;

namespace PDF_umbenenen.Services.Ai
{
    internal sealed class PdfTextExtractionService
    {
        private readonly Dictionary<string, string> _cache = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private readonly object _cacheLock = new object();

        public bool IsAvailable => PdfWatermarkService.IsAvailable();

        public string ExtractText(string pdfPath, int maxPages = 3)
        {
            if (string.IsNullOrWhiteSpace(pdfPath) || !File.Exists(pdfPath))
                return string.Empty;

            var pages = Math.Max(1, maxPages);
            var cacheKey = BuildCacheKey(pdfPath, pages);

            lock (_cacheLock)
            {
                string cached;
                if (_cache.TryGetValue(cacheKey, out cached))
                    return cached;

                // Früherer Aufruf mit weniger Seiten darf eine volle Extraktion nicht blockieren.
                string best = null;
                var bestPages = -1;
                foreach (var pair in _cache)
                {
                    int cachedPages;
                    string cachedPath;
                    if (!TryParseCacheKey(pair.Key, out cachedPath, out cachedPages))
                        continue;
                    if (!string.Equals(cachedPath, pdfPath, StringComparison.OrdinalIgnoreCase))
                        continue;
                    if (cachedPages >= pages && cachedPages > bestPages)
                    {
                        best = pair.Value;
                        bestPages = cachedPages;
                    }
                }

                if (best != null)
                    return best;
            }

            if (!IsAvailable)
                return string.Empty;

            try
            {
                var text = ExtractWithITextSharp(pdfPath, pages);
                lock (_cacheLock)
                {
                    _cache[cacheKey] = text ?? string.Empty;
                }

                return text ?? string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        public void ClearCache()
        {
            lock (_cacheLock)
            {
                _cache.Clear();
            }
        }

        private static string BuildCacheKey(string pdfPath, int maxPages)
        {
            return pdfPath + "\u0001" + maxPages.ToString(CultureInfo.InvariantCulture);
        }

        private static bool TryParseCacheKey(string key, out string pdfPath, out int maxPages)
        {
            pdfPath = null;
            maxPages = 0;
            if (string.IsNullOrWhiteSpace(key))
                return false;

            var sep = key.LastIndexOf('\u0001');
            if (sep <= 0 || sep >= key.Length - 1)
                return false;

            pdfPath = key.Substring(0, sep);
            return int.TryParse(
                key.Substring(sep + 1),
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out maxPages);
        }

        private static string ExtractWithITextSharp(string pdfPath, int maxPages)
        {
            var assembly = AppDomain.CurrentDomain.GetAssemblies()
                .FirstOrDefault(a => string.Equals(a.GetName().Name, "iTextSharp", StringComparison.OrdinalIgnoreCase));
            if (assembly == null)
                return string.Empty;

            var readerType = assembly.GetType("iTextSharp.text.pdf.PdfReader");
            var locationStrategyType = assembly.GetType("iTextSharp.text.pdf.parser.LocationTextExtractionStrategy");
            var simpleStrategyType = assembly.GetType("iTextSharp.text.pdf.parser.SimpleTextExtractionStrategy");
            var parserType = assembly.GetType("iTextSharp.text.pdf.parser.PdfTextExtractor");
            if (readerType == null || parserType == null)
                return string.Empty;

            // LocationTextExtractionStrategy: Spaltenreihenfolge wie „1 1,00 Stk“.
            // SimpleTextExtractionStrategy verdreht BIOS-Bestellungen zu „1,00 1 Stk“ (Pos als Menge).
            var strategyType = locationStrategyType ?? simpleStrategyType;
            if (strategyType == null)
                return string.Empty;

            object reader = null;
            try
            {
                reader = Activator.CreateInstance(readerType, pdfPath);
                var numberOfPagesProp = readerType.GetProperty("NumberOfPages");
                var pageCount = (int)numberOfPagesProp.GetValue(reader, null);
                var pagesToRead = Math.Min(maxPages, pageCount);

                var getTextFromPage = parserType.GetMethod(
                    "GetTextFromPage",
                    new[] { readerType, typeof(int), strategyType });

                if (getTextFromPage == null && simpleStrategyType != null && !ReferenceEquals(strategyType, simpleStrategyType))
                {
                    strategyType = simpleStrategyType;
                    getTextFromPage = parserType.GetMethod(
                        "GetTextFromPage",
                        new[] { readerType, typeof(int), strategyType });
                }

                if (getTextFromPage == null)
                    return string.Empty;

                var builder = new System.Text.StringBuilder();
                for (var page = 1; page <= pagesToRead; page++)
                {
                    var strategy = Activator.CreateInstance(strategyType);
                    var pageText = (string)getTextFromPage.Invoke(null, new[] { reader, page, strategy });
                    if (!string.IsNullOrWhiteSpace(pageText))
                    {
                        if (builder.Length > 0)
                            builder.AppendLine();
                        builder.Append(pageText);
                    }
                }

                return builder.ToString();
            }
            finally
            {
                if (reader != null)
                {
                    var closeMethod = readerType.GetMethod("Close");
                    closeMethod?.Invoke(reader, null);
                }
            }
        }
    }
}
