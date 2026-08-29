using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;

namespace PDF_umbenenen.Services
{
    internal sealed class PdfTextRect
    {
        public string Text { get; set; }

        public float Left { get; set; }

        public float Right { get; set; }

        public float Bottom { get; set; }

        public float Top { get; set; }

        public float Width => Math.Max(1f, Right - Left);

        public float Height => Math.Max(1f, Top - Bottom);
    }

    internal sealed class PdfPageLayout
    {
        public float PageWidth { get; set; }

        public float PageHeight { get; set; }

        public List<PdfTextRect> TextRects { get; set; } = new List<PdfTextRect>();
    }

    internal static class PdfLayoutAnalysisService
    {
        private static readonly Dictionary<string, PdfPageLayout> PageLayoutCache =
            new Dictionary<string, PdfPageLayout>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, string> PageTextCache =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private static readonly object CacheLock = new object();

        public static void ClearCache()
        {
            lock (CacheLock)
            {
                PageLayoutCache.Clear();
                PageTextCache.Clear();
            }
        }

        public static PdfPageLayout AnalyzePage(string pdfPath, int pageNumber)
        {
            if (string.IsNullOrWhiteSpace(pdfPath) || !File.Exists(pdfPath))
                return new PdfPageLayout { PageWidth = 595f, PageHeight = 842f };

            var cacheKey = pdfPath + "|" + pageNumber + "|" + File.GetLastWriteTimeUtc(pdfPath).Ticks;
            lock (CacheLock)
            {
                if (PageLayoutCache.TryGetValue(cacheKey, out var cached))
                    return cached;
            }

            var layout = AnalyzePageUncached(pdfPath, pageNumber);
            lock (CacheLock)
                PageLayoutCache[cacheKey] = layout;

            return layout;
        }

        private static PdfPageLayout AnalyzePageUncached(string pdfPath, int pageNumber)
        {
            var layout = new PdfPageLayout { PageWidth = 595f, PageHeight = 842f };
            if (!PdfWatermarkService.IsAvailable())
                return layout;

            object reader = null;
            try
            {
                var assembly = AppDomain.CurrentDomain.GetAssemblies()
                    .FirstOrDefault(a => string.Equals(a.GetName().Name, "iTextSharp", StringComparison.OrdinalIgnoreCase));
                if (assembly == null)
                    return layout;

                var readerType = assembly.GetType("iTextSharp.text.pdf.PdfReader");
                var strategyType = assembly.GetType("iTextSharp.text.pdf.parser.LocationTextExtractionStrategy");
                var parserType = assembly.GetType("iTextSharp.text.pdf.parser.PdfTextExtractor");
                if (readerType == null || strategyType == null || parserType == null)
                    return layout;

                reader = Activator.CreateInstance(readerType, pdfPath);
                ReadPageSize(reader, readerType, pageNumber, layout);

                var strategy = Activator.CreateInstance(strategyType);
                var getTextFromPage = parserType.GetMethod(
                    "GetTextFromPage",
                    new[] { readerType, typeof(int), strategyType });
                getTextFromPage?.Invoke(null, new[] { reader, pageNumber, strategy });

                foreach (var rect in ExtractRects(strategy, strategyType))
                {
                    if (string.IsNullOrWhiteSpace(rect.Text))
                        continue;

                    layout.TextRects.Add(rect);
                }
            }
            catch
            {
            }
            finally
            {
                if (reader != null)
                {
                    var closeMethod = reader.GetType().GetMethod("Close");
                    closeMethod?.Invoke(reader, null);
                }
            }

            return layout;
        }

        public static string ExtractPageText(string pdfPath, int pageNumber)
        {
            if (string.IsNullOrWhiteSpace(pdfPath) || !File.Exists(pdfPath) || !PdfWatermarkService.IsAvailable())
                return string.Empty;

            var cacheKey = pdfPath + "|text|" + pageNumber + "|" + File.GetLastWriteTimeUtc(pdfPath).Ticks;
            lock (CacheLock)
            {
                if (PageTextCache.TryGetValue(cacheKey, out var cached))
                    return cached;
            }

            var text = ExtractPageTextUncached(pdfPath, pageNumber);
            lock (CacheLock)
                PageTextCache[cacheKey] = text;

            return text;
        }

        private static string ExtractPageTextUncached(string pdfPath, int pageNumber)
        {
            object reader = null;
            try
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

                reader = Activator.CreateInstance(readerType, pdfPath);
                var strategy = Activator.CreateInstance(strategyType);
                var getTextFromPage = parserType.GetMethod(
                    "GetTextFromPage",
                    new[] { readerType, typeof(int), strategyType });
                if (getTextFromPage == null)
                    return string.Empty;

                return (string)getTextFromPage.Invoke(null, new[] { reader, pageNumber, strategy }) ?? string.Empty;
            }
            catch
            {
                return string.Empty;
            }
            finally
            {
                if (reader != null)
                {
                    var closeMethod = reader.GetType().GetMethod("Close");
                    closeMethod?.Invoke(reader, null);
                }
            }
        }

        private static void ReadPageSize(object reader, Type readerType, int pageNumber, PdfPageLayout layout)
        {
            var getPageSizeMethod = readerType.GetMethod("GetPageSizeWithRotation", new[] { typeof(int) })
                                    ?? readerType.GetMethod("GetPageSize", new[] { typeof(int) });
            if (getPageSizeMethod == null)
                return;

            var rect = getPageSizeMethod.Invoke(reader, new object[] { pageNumber });
            if (rect == null)
                return;

            var widthProp = rect.GetType().GetProperty("Width");
            var heightProp = rect.GetType().GetProperty("Height");
            if (widthProp != null)
            {
                var w = widthProp.GetValue(rect, null);
                if (w != null)
                    layout.PageWidth = Convert.ToSingle(w, CultureInfo.InvariantCulture);
            }

            if (heightProp != null)
            {
                var h = heightProp.GetValue(rect, null);
                if (h != null)
                    layout.PageHeight = Convert.ToSingle(h, CultureInfo.InvariantCulture);
            }
        }

        private static IEnumerable<PdfTextRect> ExtractRects(object strategy, Type strategyType)
        {
            var result = new List<PdfTextRect>();
            var chunks = GetTextChunks(strategy, strategyType);
            if (chunks == null)
                return result;

            foreach (var chunk in chunks)
            {
                if (chunk == null)
                    continue;

                var chunkType = chunk.GetType();
                var text = ReadChunkText(chunkType, chunk);
                if (string.IsNullOrWhiteSpace(text))
                    continue;

                var start = ReadChunkVector(chunkType, chunk, "StartLocation", "GetStartLocation");
                var end = ReadChunkVector(chunkType, chunk, "EndLocation", "GetEndLocation");
                if (start == null || end == null)
                    continue;

                result.Add(new PdfTextRect
                {
                    Text = text.Trim(),
                    Left = Math.Min(start.Value.X, end.Value.X),
                    Right = Math.Max(start.Value.X, end.Value.X),
                    Bottom = Math.Min(start.Value.Y, end.Value.Y),
                    Top = Math.Max(start.Value.Y, end.Value.Y)
                });
            }

            return result;
        }

        private static IEnumerable GetTextChunks(object strategy, Type strategyType)
        {
            const BindingFlags binding = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

            var locationalField = strategyType.GetField("locationalResult", binding);
            if (locationalField != null)
            {
                var chunks = locationalField.GetValue(strategy) as IEnumerable;
                if (chunks != null)
                    return chunks;
            }

            var getLocations = strategyType.GetMethod("GetLocations", binding);
            return getLocations?.Invoke(strategy, null) as IEnumerable;
        }

        private static string ReadChunkText(Type chunkType, object chunk)
        {
            const BindingFlags binding = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

            var textField = chunkType.GetField("text", binding);
            var text = textField?.GetValue(chunk) as string;
            if (!string.IsNullOrWhiteSpace(text))
                return text;

            var textProp = chunkType.GetProperty("Text", binding);
            text = textProp?.GetValue(chunk, null) as string;
            if (!string.IsNullOrWhiteSpace(text))
                return text;

            var getText = chunkType.GetMethod("GetText", binding, null, Type.EmptyTypes, null);
            return getText?.Invoke(chunk, null) as string;
        }

        private static VectorPoint? ReadChunkVector(Type chunkType, object chunk, string propertyName, string methodName)
        {
            const BindingFlags binding = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

            var property = chunkType.GetProperty(propertyName, binding);
            if (property != null)
            {
                var vector = property.GetValue(chunk, null);
                var point = InvokeVector(vector);
                if (point != null)
                    return point;
            }

            var method = chunkType.GetMethod(methodName, binding, null, Type.EmptyTypes, null);
            return InvokeVector(method?.Invoke(chunk, null));
        }

        private static VectorPoint? InvokeVector(object vector)
        {
            if (vector == null)
                return null;

            var vectorType = vector.GetType();
            const BindingFlags binding = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;

            var indexer = vectorType.GetProperty("Item", binding, null, typeof(float), new[] { typeof(int) }, null)
                          ?? vectorType.GetProperty("Item", binding);
            if (indexer != null)
            {
                var xIndex = ReadVectorIndex(vectorType, "I1", 1);
                var yIndex = ReadVectorIndex(vectorType, "I2", 2);
                var xObj = indexer.GetValue(vector, new object[] { xIndex });
                var yObj = indexer.GetValue(vector, new object[] { yIndex });
                if (xObj != null && yObj != null)
                {
                    return new VectorPoint
                    {
                        X = Convert.ToSingle(xObj, CultureInfo.InvariantCulture),
                        Y = Convert.ToSingle(yObj, CultureInfo.InvariantCulture)
                    };
                }
            }

            var valsField = vectorType.GetField("vals", binding);
            if (valsField != null)
            {
                var vals = valsField.GetValue(vector) as float[];
                if (vals != null && vals.Length >= 2)
                {
                    return new VectorPoint
                    {
                        X = vals[0],
                        Y = vals[1]
                    };
                }
            }

            var getMethod = vectorType.GetMethod("Get", binding, null, new[] { typeof(int) }, null);
            if (getMethod == null)
                return null;

            var legacyX = getMethod.Invoke(vector, new object[] { 1 });
            var legacyY = getMethod.Invoke(vector, new object[] { 2 });
            if (legacyX == null || legacyY == null)
                return null;

            return new VectorPoint
            {
                X = Convert.ToSingle(legacyX, CultureInfo.InvariantCulture),
                Y = Convert.ToSingle(legacyY, CultureInfo.InvariantCulture)
            };
        }

        private static int ReadVectorIndex(Type vectorType, string fieldName, int fallback)
        {
            const BindingFlags binding = BindingFlags.Public | BindingFlags.Static;
            var field = vectorType.GetField(fieldName, binding);
            if (field == null)
                return fallback;

            return Convert.ToInt32(field.GetValue(null), CultureInfo.InvariantCulture);
        }

        private struct VectorPoint
        {
            public float X;
            public float Y;
        }
    }
}
