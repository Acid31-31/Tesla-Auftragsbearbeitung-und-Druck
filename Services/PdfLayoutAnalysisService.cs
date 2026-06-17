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
        public static PdfPageLayout AnalyzePage(string pdfPath, int pageNumber)
        {
            var layout = new PdfPageLayout { PageWidth = 595f, PageHeight = 842f };
            if (string.IsNullOrWhiteSpace(pdfPath) || !File.Exists(pdfPath) || !PdfWatermarkService.IsAvailable())
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
            var getLocations = strategyType.GetMethod("GetLocations", Type.EmptyTypes);
            if (getLocations == null)
                return result;

            var locations = getLocations.Invoke(strategy, null) as IEnumerable;
            if (locations == null)
                return result;

            foreach (var chunk in locations)
            {
                if (chunk == null)
                    continue;

                var chunkType = chunk.GetType();
                var textProp = chunkType.GetProperty("Text");
                var text = textProp?.GetValue(chunk, null) as string;
                if (string.IsNullOrWhiteSpace(text))
                {
                    var getText = chunkType.GetMethod("GetText", Type.EmptyTypes);
                    text = getText?.Invoke(chunk, null) as string;
                }

                if (string.IsNullOrWhiteSpace(text))
                    continue;

                var start = InvokeVector(chunkType.GetMethod("GetStartLocation", Type.EmptyTypes)?.Invoke(chunk, null));
                var end = InvokeVector(chunkType.GetMethod("GetEndLocation", Type.EmptyTypes)?.Invoke(chunk, null));
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

        private static VectorPoint? InvokeVector(object vector)
        {
            if (vector == null)
                return null;

            var vectorType = vector.GetType();
            var getMethod = vectorType.GetMethod("Get", new[] { typeof(int) });
            if (getMethod == null)
                return null;

            var xObj = getMethod.Invoke(vector, new object[] { 1 });
            var yObj = getMethod.Invoke(vector, new object[] { 2 });
            if (xObj == null || yObj == null)
                return null;

            return new VectorPoint
            {
                X = Convert.ToSingle(xObj, CultureInfo.InvariantCulture),
                Y = Convert.ToSingle(yObj, CultureInfo.InvariantCulture)
            };
        }

        private struct VectorPoint
        {
            public float X;
            public float Y;
        }
    }
}
