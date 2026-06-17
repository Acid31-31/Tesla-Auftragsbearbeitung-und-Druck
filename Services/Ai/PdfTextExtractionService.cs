using System;
using System.Collections.Generic;
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

            lock (_cacheLock)
            {
                string cached;
                if (_cache.TryGetValue(pdfPath, out cached))
                    return cached;
            }

            if (!IsAvailable)
                return string.Empty;

            try
            {
                var text = ExtractWithITextSharp(pdfPath, maxPages);
                lock (_cacheLock)
                {
                    _cache[pdfPath] = text ?? string.Empty;
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

        private static string ExtractWithITextSharp(string pdfPath, int maxPages)
        {
            var assembly = AppDomain.CurrentDomain.GetAssemblies()
                .FirstOrDefault(a => string.Equals(a.GetName().Name, "iTextSharp", StringComparison.OrdinalIgnoreCase));
            if (assembly == null)
                return string.Empty;

            var readerType = assembly.GetType("iTextSharp.text.pdf.PdfReader");
            var strategyType = assembly.GetType("iTextSharp.text.pdf.parser.SimpleTextExtractionStrategy");
            var parserType = assembly.GetType("iTextSharp.text.pdf.parser.PdfTextExtractor");
            if (readerType == null || strategyType == null || parserType == null)
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
