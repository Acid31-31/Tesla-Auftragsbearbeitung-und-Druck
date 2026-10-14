using System;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;

namespace PDF_umbenenen.Services
{
    /// <summary>
    /// Excel → PDF nur mit befülltem Bereich: Querformat, schmale Spalten,
    /// leere Zeilen/Spalten nicht mitdrucken.
    /// </summary>
    internal static class ExcelPrintExportService
    {
        public const string TrimmedFileName = "_EXCEL_DRUCK.pdf";

        private static readonly Regex PlaceholderRegex = new Regex(
            @"^(€\s*-?|-|–|—|\.|n/?a|null)?$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

        public static string TryExportForPrint(string excelPath, string outputDirectory, out string statusMessage)
        {
            statusMessage = string.Empty;
            if (string.IsNullOrWhiteSpace(excelPath) || !File.Exists(excelPath))
            {
                statusMessage = "Keine Excel-Datei für den Ausdruck.";
                return null;
            }

            if (string.IsNullOrWhiteSpace(outputDirectory))
                outputDirectory = Path.GetDirectoryName(excelPath) ?? string.Empty;

            Directory.CreateDirectory(outputDirectory);
            var outputPath = Path.Combine(outputDirectory, TrimmedFileName);

            var excelType = Type.GetTypeFromProgID("Excel.Application");
            if (excelType == null)
            {
                statusMessage = "Excel-Druck-PDF nicht möglich: Microsoft Excel ist nicht installiert.";
                return null;
            }

            var workCopy = Path.Combine(
                Path.GetTempPath(),
                "DOK-V01-ExcelPrint-" + Guid.NewGuid().ToString("N") + Path.GetExtension(excelPath));

            dynamic excel = null;
            dynamic workbook = null;

            try
            {
                File.Copy(excelPath, workCopy, overwrite: true);

                excel = Activator.CreateInstance(excelType);
                excel.Visible = false;
                excel.DisplayAlerts = false;
                excel.ScreenUpdating = false;

                workbook = excel.Workbooks.Open(workCopy, ReadOnly: false);

                var sheetCount = (int)workbook.Worksheets.Count;
                var preparedSheets = 0;
                for (var i = 1; i <= sheetCount; i++)
                {
                    dynamic sheet = workbook.Worksheets[i];
                    if (PrepareWorksheetForReadablePrint(excel, sheet))
                        preparedSheets++;
                    else
                    {
                        // Leere / Formatierungs-only Blätter nicht mitdrucken
                        try { sheet.Visible = 0; } // xlSheetHidden
                        catch { }
                    }
                }

                if (preparedSheets == 0)
                {
                    statusMessage = "Excel enthält kein nutzbares Blatt für den Ausdruck.";
                    return null;
                }

                if (File.Exists(outputPath))
                {
                    try { File.Delete(outputPath); }
                    catch { }
                }

                workbook.ExportAsFixedFormat(
                    Type: 0,
                    Filename: outputPath,
                    Quality: 0,
                    IncludeDocProperties: true,
                    IgnorePrintAreas: false,
                    OpenAfterPublish: false);

                if (!File.Exists(outputPath))
                {
                    statusMessage = "Excel-PDF-Export lieferte keine Datei.";
                    return null;
                }

                statusMessage = "Excel für Druck: Querformat, ausgefüllter Bereich maximiert auf eine Seite.";
                return outputPath;
            }
            catch (Exception ex)
            {
                statusMessage = "Excel-Druck-PDF fehlgeschlagen: " + ex.Message;
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
            finally
            {
                try
                {
                    if (workbook != null)
                    {
                        workbook.Close(SaveChanges: false);
                        Marshal.FinalReleaseComObject(workbook);
                    }
                }
                catch
                {
                }

                try
                {
                    if (excel != null)
                    {
                        excel.Quit();
                        Marshal.FinalReleaseComObject(excel);
                    }
                }
                catch
                {
                }

                try
                {
                    if (File.Exists(workCopy))
                        File.Delete(workCopy);
                }
                catch
                {
                }
            }
        }

        private static bool PrepareWorksheetForReadablePrint(dynamic excel, dynamic worksheet)
        {
            if (worksheet == null)
                return false;

            try
            {
                dynamic usedRange = worksheet.UsedRange;
                if (usedRange == null)
                    return false;

                var rows = (int)usedRange.Rows.Count;
                var columns = (int)usedRange.Columns.Count;
                if (rows < 1 || columns < 1)
                    return false;

                object[,] values;
                try
                {
                    values = (object[,])usedRange.Value2;
                }
                catch
                {
                    return false;
                }

                var startRow = (int)usedRange.Row;
                var startCol = (int)usedRange.Column;

                int lastContentRow;
                int lastContentCol;
                if (!TryFindContentBounds(values, rows, columns, out lastContentRow, out lastContentCol))
                    return false;

                // Spalten ohne echten Inhalt ausblenden (auch „€ -“)
                HideEmptyAndLowValueColumns(worksheet, values, lastContentRow, lastContentCol, startCol);

                // Nur bis letzte Datenzeile / -spalte drucken (keine leeren Gitterzeilen)
                var endRow = startRow + lastContentRow - 1;
                var endCol = startCol + lastContentCol - 1;
                var printAddress = worksheet.Range[
                    worksheet.Cells[startRow, startCol],
                    worksheet.Cells[endRow, endCol]].Address;
                worksheet.PageSetup.PrintArea = printAddress;

                // Sichtbare Spalten schmaler machen
                NarrowVisibleColumns(worksheet, values, lastContentRow, lastContentCol, startCol);

                var pageSetup = worksheet.PageSetup;
                pageSetup.Orientation = 2; // Landscape
                pageSetup.PaperSize = 9;   // A4
                pageSetup.Zoom = false;
                // Komplett auf EIN Querformat-Blatt maximieren (Breite + Höhe)
                pageSetup.FitToPagesWide = 1;
                pageSetup.FitToPagesTall = 1;

                try
                {
                    var narrow = excel.InchesToPoints(0.25);
                    pageSetup.LeftMargin = narrow;
                    pageSetup.RightMargin = narrow;
                    pageSetup.TopMargin = narrow;
                    pageSetup.BottomMargin = narrow;
                    pageSetup.HeaderMargin = excel.InchesToPoints(0.1);
                    pageSetup.FooterMargin = excel.InchesToPoints(0.1);
                }
                catch
                {
                }

                pageSetup.CenterHorizontally = true;
                pageSetup.CenterVertically = false;

                try
                {
                    pageSetup.PrintComments = -4142;
                    pageSetup.PrintGridlines = false;
                }
                catch
                {
                }

                return true;
            }
            catch
            {
                return false;
            }
        }

        private static bool TryFindContentBounds(
            object[,] values,
            int rows,
            int columns,
            out int lastContentRow,
            out int lastContentCol)
        {
            lastContentRow = 0;
            lastContentCol = 0;

            for (var r = 1; r <= rows; r++)
            {
                for (var c = 1; c <= columns; c++)
                {
                    if (!IsMeaningfulCell(values[r, c]))
                        continue;
                    if (r > lastContentRow)
                        lastContentRow = r;
                    if (c > lastContentCol)
                        lastContentCol = c;
                }
            }

            return lastContentRow > 0 && lastContentCol > 0;
        }

        private static void HideEmptyAndLowValueColumns(
            dynamic worksheet,
            object[,] values,
            int lastRow,
            int lastCol,
            int startCol)
        {
            const int headerScanRows = 10;

            for (var c = 1; c <= lastCol; c++)
            {
                var header = BuildHeaderText(values, c, Math.Min(lastRow, headerScanRows));
                var meaningfulData = false;
                for (var r = headerScanRows + 1; r <= lastRow; r++)
                {
                    if (IsMeaningfulCell(values[r, c]))
                    {
                        meaningfulData = true;
                        break;
                    }
                }

                // Immer behalten: Drawing / Amount / Line / Article / Description / Date / Category
                if (IsEssentialColumn(header))
                    continue;

                var hide = !meaningfulData || IsOptionalLowValueColumn(header, meaningfulData);
                if (!hide)
                    continue;

                try
                {
                    worksheet.Columns[startCol + c - 1].Hidden = true;
                }
                catch
                {
                }
            }
        }

        private static void NarrowVisibleColumns(
            dynamic worksheet,
            object[,] values,
            int lastRow,
            int lastCol,
            int startCol)
        {
            const int headerScanRows = 10;

            for (var c = 1; c <= lastCol; c++)
            {
                var excelCol = startCol + c - 1;
                try
                {
                    dynamic col = worksheet.Columns[excelCol];
                    if (col.Hidden == true)
                        continue;

                    var header = BuildHeaderText(values, c, Math.Min(lastRow, headerScanRows));
                    col.AutoFit();

                    double width = col.ColumnWidth;
                    if (IsDescriptionColumn(header))
                    {
                        // Description darf nicht die ganze Seite fressen
                        if (width > 28)
                            col.ColumnWidth = 28;
                        else if (width < 12)
                            col.ColumnWidth = 12;
                    }
                    else if (IsNarrowPreferColumn(header))
                    {
                        if (width > 8)
                            col.ColumnWidth = Math.Max(4, Math.Min(8, width));
                    }
                    else
                    {
                        if (width > 14)
                            col.ColumnWidth = 14;
                        if (width < 3.5)
                            col.ColumnWidth = 3.5;
                    }
                }
                catch
                {
                }
            }
        }

        private static bool IsMeaningfulCell(object value)
        {
            var text = Convert.ToString(value, CultureInfo.InvariantCulture);
            if (string.IsNullOrWhiteSpace(text))
                return false;

            text = text.Trim();
            if (PlaceholderRegex.IsMatch(text))
                return false;

            // Nur Währungssymbol / Strich
            if (string.Equals(text, "€", StringComparison.OrdinalIgnoreCase)
                || string.Equals(text, "€-", StringComparison.OrdinalIgnoreCase)
                || string.Equals(text, "€ -", StringComparison.OrdinalIgnoreCase))
                return false;

            return true;
        }

        private static string BuildHeaderText(object[,] values, int column, int maxRow)
        {
            var sb = new StringBuilder();
            for (var r = 1; r <= maxRow; r++)
            {
                var t = Convert.ToString(values[r, column], CultureInfo.InvariantCulture);
                if (string.IsNullOrWhiteSpace(t))
                    continue;
                if (sb.Length > 0)
                    sb.Append(' ');
                sb.Append(t.Trim());
            }

            return sb.ToString();
        }

        private static bool IsEssentialColumn(string header)
        {
            var n = (header ?? string.Empty).Trim().ToLowerInvariant();
            return n.Contains("line")
                   || n.Contains("article")
                   || n.Contains("amount")
                   || n.Contains("drawing")
                   || n.Contains("description")
                   || n.Contains("shop floor")
                   || n.Contains("delivery date")
                   || n.Contains("liefer")
                   || n.Contains("zeichnung")
                   || n.Contains("menge")
                   || n.Contains("pos");
        }

        private static bool IsDescriptionColumn(string header)
        {
            var n = (header ?? string.Empty).Trim().ToLowerInvariant();
            return n.Contains("description") || n.Contains("beschreibung");
        }

        private static bool IsNarrowPreferColumn(string header)
        {
            var n = (header ?? string.Empty).Trim().ToLowerInvariant();
            return n.Contains("line")
                   || n.Contains("rev")
                   || n.Contains("category")
                   || n.Contains("check")
                   || n.Contains("amount")
                   || n.Contains("menge");
        }

        private static bool IsOptionalLowValueColumn(string header, bool hasMeaningfulData)
        {
            if (hasMeaningfulData)
            {
                // Auch mit Platzhalter-Inhalt: Preis-/Ref-Spalten ausblenden wenn nur € -
                var n = (header ?? string.Empty).Trim().ToLowerInvariant();
                return n.Contains("price")
                       || n.Contains("preis")
                       || n.Contains("ref. order")
                       || n.Contains("ref. specification")
                       || n.Contains("ref. hersteller")
                       || n.Contains("according to quote");
            }

            if (string.IsNullOrWhiteSpace(header))
                return true;

            var h = header.Trim().ToLowerInvariant();
            return h.Contains("ref. order")
                   || h.Contains("ref. specification")
                   || h.Contains("ref. hersteller")
                   || h.Contains("hersteller")
                   || h.Contains("according to quote")
                   || h.Contains("price")
                   || h.Contains("preis");
        }
    }
}
