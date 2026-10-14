using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;

namespace PDF_umbenenen.Services
{
    /// <summary>
    /// Kürzt die Tesla-Bestell-PDF für den Ausdruck / die Gesamt-PDF:
    /// alle Seiten bis inkl. „Acknowledgment of General Terms and Conditions“.
    /// </summary>
    internal static class OrderPdfPrintTrimService
    {
        public const string TrimmedFileName = "_BESTELLUNG_DRUCK.pdf";

        private static readonly string[] EndPageMarkers =
        {
            "Acknowledgment of General Terms and Conditions",
            "Acknowledgement of General Terms and Conditions"
        };

        private static readonly Regex NwBlechMarker = new Regex(
            @"NW_BLECH-FTG_",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

        /// <summary>
        /// Erzeugt eine gekürzte Bestell-PDF im Zielordner. null = nicht möglich / Marker fehlt.
        /// </summary>
        public static string TryPrepareTrimmedPrintPdf(string orderPdfPath, string outputDirectory, out string statusMessage)
        {
            statusMessage = string.Empty;
            if (string.IsNullOrWhiteSpace(orderPdfPath) || !File.Exists(orderPdfPath))
            {
                statusMessage = "Keine Bestell-PDF für den Ausdruck.";
                return null;
            }

            if (!string.Equals(Path.GetExtension(orderPdfPath), ".pdf", StringComparison.OrdinalIgnoreCase))
            {
                statusMessage = "Bestellung ist keine PDF – wird nicht in die Gesamt-PDF aufgenommen.";
                return null;
            }

            if (string.IsNullOrWhiteSpace(outputDirectory))
                outputDirectory = Path.GetDirectoryName(orderPdfPath) ?? string.Empty;

            Directory.CreateDirectory(outputDirectory);
            var outputPath = Path.Combine(outputDirectory, TrimmedFileName);

            try
            {
                if (!TryFindLastPrintPage(orderPdfPath, out var lastPage, out var pageCount, out var error))
                {
                    statusMessage = string.IsNullOrWhiteSpace(error)
                        ? "Bestell-PDF: Abschlussseite (General Terms) nicht gefunden – Bestellung nicht in Gesamt-PDF."
                        : error;
                    return null;
                }

                ExtractPageRange(orderPdfPath, outputPath, 1, lastPage);
                statusMessage = "Bestell-PDF für Druck: Seiten 1–" + lastPage + " von " + pageCount
                    + " (bis Acknowledgment), einheitlich Hochformat.";
                return outputPath;
            }
            catch (Exception ex)
            {
                statusMessage = "Bestell-PDF kürzen fehlgeschlagen: " + ex.Message;
                try
                {
                    if (File.Exists(outputPath))
                        File.Delete(outputPath);
                }
                catch
                {
                }

                return null;
            }
        }

        public static bool TryFindLastPrintPage(
            string pdfPath,
            out int lastInclusivePage,
            out int totalPages,
            out string error)
        {
            lastInclusivePage = 0;
            totalPages = 0;
            error = string.Empty;

            if (!TryOpenReader(pdfPath, out var assembly, out var readerType, out var reader, out var numberOfPagesProp, out error))
                return false;

            try
            {
                totalPages = (int)numberOfPagesProp.GetValue(reader, null);
                if (totalPages <= 0)
                {
                    error = "Bestell-PDF hat keine Seiten.";
                    return false;
                }

                var parserType = assembly.GetType("iTextSharp.text.pdf.parser.PdfTextExtractor");
                var locationStrategyType = assembly.GetType("iTextSharp.text.pdf.parser.LocationTextExtractionStrategy");
                var simpleStrategyType = assembly.GetType("iTextSharp.text.pdf.parser.SimpleTextExtractionStrategy");
                var strategyType = locationStrategyType ?? simpleStrategyType;
                if (parserType == null || strategyType == null)
                {
                    error = "iTextSharp Text-Extraktion nicht verfügbar.";
                    return false;
                }

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
                {
                    error = "GetTextFromPage nicht gefunden.";
                    return false;
                }

                // Erste Seite mit dem Acknowledgment-Marker = letzte Druckseite
                for (var page = 1; page <= totalPages; page++)
                {
                    var strategy = Activator.CreateInstance(strategyType);
                    var pageText = (string)getTextFromPage.Invoke(null, new[] { reader, page, strategy }) ?? string.Empty;
                    if (IsEndPrintPage(pageText))
                    {
                        lastInclusivePage = page;
                        return true;
                    }
                }

                error = "Abschlussseite „Acknowledgment of General Terms…“ in der Bestell-PDF nicht gefunden.";
                return false;
            }
            finally
            {
                readerType.GetMethod("Close", Type.EmptyTypes)?.Invoke(reader, null);
                (reader as IDisposable)?.Dispose();
            }
        }

        public static bool IsEndPrintPage(string pageText)
        {
            if (string.IsNullOrWhiteSpace(pageText))
                return false;

            var normalized = CollapseWhitespace(pageText);
            foreach (var marker in EndPageMarkers)
            {
                if (normalized.IndexOf(marker, StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            }

            // Zusätzliche Absicherung: gleiche Seite trägt oft NW_BLECH-FTG_ + Angebot laut Mailanhang
            if (NwBlechMarker.IsMatch(normalized)
                && normalized.IndexOf("Angebot laut Mailanhang", StringComparison.OrdinalIgnoreCase) >= 0
                && normalized.IndexOf("General Terms", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;

            return false;
        }

        public static void ExtractPageRange(string sourcePdf, string outputPdf, int fromPage, int toPageInclusive)
        {
            if (fromPage < 1 || toPageInclusive < fromPage)
                throw new ArgumentOutOfRangeException("fromPage/toPageInclusive");

            if (!TryOpenReader(sourcePdf, out var assembly, out var readerType, out var reader, out var numberOfPagesProp, out var error))
                throw new InvalidOperationException(error);

            FileStream stream = null;
            object document = null;
            object copy = null;

            try
            {
                var total = (int)numberOfPagesProp.GetValue(reader, null);
                if (toPageInclusive > total)
                    toPageInclusive = total;

                // Querformat-Seiten der Bestellung → Hochformat drehen (wichtig für Duplex)
                ForcePagesToPortrait(assembly, readerType, reader, fromPage, toPageInclusive);

                var documentType = assembly.GetType("iTextSharp.text.Document");
                var pageSizeType = assembly.GetType("iTextSharp.text.PageSize");
                var pdfCopyType = assembly.GetType("iTextSharp.text.pdf.PdfCopy");
                var pdfImportedPageType = assembly.GetType("iTextSharp.text.pdf.PdfImportedPage");
                if (documentType == null || pdfCopyType == null || pdfImportedPageType == null)
                    throw new InvalidOperationException("iTextSharp-Typen für Seitenexport fehlen.");

                // Immer A4 Hochformat als Dokumentformat
                object a4 = null;
                try { a4 = pageSizeType?.GetField("A4")?.GetValue(null); } catch { }
                document = a4 != null
                    ? Activator.CreateInstance(documentType, new[] { a4 })
                    : Activator.CreateInstance(documentType);

                stream = new FileStream(outputPdf, FileMode.Create, FileAccess.Write, FileShare.None);
                var copyCtor = pdfCopyType.GetConstructor(new[] { documentType, typeof(Stream) });
                if (copyCtor == null)
                    throw new InvalidOperationException("PdfCopy-Konstruktor fehlt.");

                copy = copyCtor.Invoke(new object[] { document, stream });
                documentType.GetMethod("Open", Type.EmptyTypes)?.Invoke(document, null);

                var getImportedPage = pdfCopyType.GetMethod("GetImportedPage", new[] { readerType, typeof(int) });
                var addPage = pdfCopyType.GetMethod("AddPage", new[] { pdfImportedPageType });
                if (getImportedPage == null || addPage == null)
                    throw new InvalidOperationException("PdfCopy-Methoden fehlen.");

                for (var p = fromPage; p <= toPageInclusive; p++)
                {
                    var page = getImportedPage.Invoke(copy, new object[] { reader, p });
                    addPage.Invoke(copy, new[] { page });
                }
            }
            finally
            {
                var pdfCopyType = assembly?.GetType("iTextSharp.text.pdf.PdfCopy");
                var documentType = assembly?.GetType("iTextSharp.text.Document");
                pdfCopyType?.GetMethod("Close", Type.EmptyTypes)?.Invoke(copy, null);
                documentType?.GetMethod("Close", Type.EmptyTypes)?.Invoke(document, null);
                stream?.Dispose();
                readerType.GetMethod("Close", Type.EmptyTypes)?.Invoke(reader, null);
                (reader as IDisposable)?.Dispose();
            }
        }

        /// <summary>
        /// Seiten mit Breite &gt; Höhe (Querformat) um 90° drehen → einheitlich Hochformat.
        /// </summary>
        private static void ForcePagesToPortrait(
            Assembly assembly,
            Type readerType,
            object reader,
            int fromPage,
            int toPage)
        {
            try
            {
                var getPageSizeWithRotation = readerType.GetMethod("GetPageSizeWithRotation", new[] { typeof(int) });
                var getPageRotation = readerType.GetMethod("GetPageRotation", new[] { typeof(int) });
                var getPageN = readerType.GetMethod("GetPageN", new[] { typeof(int) });
                var pdfNameType = assembly.GetType("iTextSharp.text.pdf.PdfName");
                var pdfNumberType = assembly.GetType("iTextSharp.text.pdf.PdfNumber");
                if (getPageSizeWithRotation == null || getPageN == null || pdfNameType == null || pdfNumberType == null)
                    return;

                var rotateName = pdfNameType.GetField("ROTATE")?.GetValue(null);
                if (rotateName == null)
                    return;

                for (var p = fromPage; p <= toPage; p++)
                {
                    var rect = getPageSizeWithRotation.Invoke(reader, new object[] { p });
                    if (rect == null)
                        continue;

                    var widthProp = rect.GetType().GetProperty("Width");
                    var heightProp = rect.GetType().GetProperty("Height");
                    if (widthProp == null || heightProp == null)
                        continue;

                    var width = Convert.ToSingle(widthProp.GetValue(rect, null));
                    var height = Convert.ToSingle(heightProp.GetValue(rect, null));
                    if (width <= height)
                        continue; // schon Hochformat

                    var currentRot = 0;
                    if (getPageRotation != null)
                        currentRot = Convert.ToInt32(getPageRotation.Invoke(reader, new object[] { p }));

                    var newRot = (currentRot + 90) % 360;
                    var pageDict = getPageN.Invoke(reader, new object[] { p });
                    if (pageDict == null)
                        continue;

                    var put = pageDict.GetType().GetMethod("Put", new[] { pdfNameType, assembly.GetType("iTextSharp.text.pdf.PdfObject") })
                              ?? pageDict.GetType().GetMethod("Put");
                    var rotNumber = Activator.CreateInstance(pdfNumberType, new object[] { newRot });
                    put?.Invoke(pageDict, new[] { rotateName, rotNumber });
                }
            }
            catch
            {
                // Orientierung belassen, wenn Reflection scheitert
            }
        }

        private static bool TryOpenReader(
            string pdfPath,
            out Assembly assembly,
            out Type readerType,
            out object reader,
            out PropertyInfo numberOfPagesProp,
            out string error)
        {
            assembly = null;
            readerType = null;
            reader = null;
            numberOfPagesProp = null;
            error = string.Empty;

            if (!PdfWatermarkService.IsAvailable())
            {
                error = "iTextSharp nicht verfügbar.";
                return false;
            }

            assembly = AppDomain.CurrentDomain.GetAssemblies()
                .FirstOrDefault(a => string.Equals(a.GetName().Name, "iTextSharp", StringComparison.OrdinalIgnoreCase));
            if (assembly == null)
            {
                error = "iTextSharp-Assembly fehlt.";
                return false;
            }

            readerType = assembly.GetType("iTextSharp.text.pdf.PdfReader");
            if (readerType == null)
            {
                error = "PdfReader-Typ fehlt.";
                return false;
            }

            var ctor = readerType.GetConstructor(new[] { typeof(string) });
            if (ctor == null)
            {
                error = "PdfReader-Konstruktor fehlt.";
                return false;
            }

            numberOfPagesProp = readerType.GetProperty("NumberOfPages");
            if (numberOfPagesProp == null)
            {
                error = "NumberOfPages fehlt.";
                return false;
            }

            try
            {
                reader = ctor.Invoke(new object[] { pdfPath });
                return true;
            }
            catch (Exception ex)
            {
                error = "PDF lesen fehlgeschlagen: " + ex.Message;
                return false;
            }
        }

        private static string CollapseWhitespace(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return string.Empty;

            var sb = new StringBuilder(text.Length);
            var prevSpace = false;
            foreach (var ch in text)
            {
                if (char.IsWhiteSpace(ch))
                {
                    if (!prevSpace)
                    {
                        sb.Append(' ');
                        prevSpace = true;
                    }
                }
                else
                {
                    sb.Append(ch);
                    prevSpace = false;
                }
            }

            return sb.ToString().Trim();
        }
    }
}
