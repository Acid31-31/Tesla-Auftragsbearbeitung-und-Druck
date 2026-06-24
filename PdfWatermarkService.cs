using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using PDF_umbenenen.Services;

namespace PDF_umbenenen
{
    /// <summary>
    /// Service zum Hinzufügen von Wasserzeichen zu PDFs mit iTextSharp
    /// </summary>
    public static class PdfWatermarkService
    {
        private static bool _iTextSharpLoaded = false;
        private static string _loadError = string.Empty;
        private static Type _pdfReaderType;
        private static Type _pdfStamperType;
        private static Type _fontFactoryType;
        private static Type _elementType;
        private static Type _baseColorType;
        private static Type _baseFontType;

        static PdfWatermarkService()
        {
            AppDomain.CurrentDomain.AssemblyResolve += CurrentDomain_AssemblyResolve;
            LoadITextSharp();
        }

        private static Assembly CurrentDomain_AssemblyResolve(object sender, ResolveEventArgs args)
        {
            var requestedName = new AssemblyName(args.Name).Name;
            if (!string.Equals(requestedName, "iTextSharp", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(requestedName, "BouncyCastle.Cryptography", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            var assembly = AppDomain.CurrentDomain.GetAssemblies()
                .FirstOrDefault(a => string.Equals(a.GetName().Name, requestedName, StringComparison.OrdinalIgnoreCase));
            if (assembly != null)
                return assembly;

            var resourceSuffix = requestedName + ".dll";
            var currentAssembly = Assembly.GetExecutingAssembly();
            var resourceName = currentAssembly.GetManifestResourceNames()
                .FirstOrDefault(n => n.EndsWith(resourceSuffix, StringComparison.OrdinalIgnoreCase));

            if (resourceName == null)
                return null;

            using (var stream = currentAssembly.GetManifestResourceStream(resourceName))
            {
                if (stream == null)
                    return null;

                var raw = new byte[stream.Length];
                stream.Read(raw, 0, raw.Length);
                return Assembly.Load(raw);
            }
        }

        private static void LoadITextSharp()
        {
            try
            {
                var baseDir = AppDomain.CurrentDomain.BaseDirectory;

                var localBouncy = Path.Combine(baseDir, "BouncyCastle.Cryptography.dll");
                if (File.Exists(localBouncy))
                    Assembly.LoadFrom(localBouncy);

                var localITextSharp = Path.Combine(baseDir, "itextsharp.dll");
                if (File.Exists(localITextSharp))
                    Assembly.LoadFrom(localITextSharp);

                var assembly = AppDomain.CurrentDomain.GetAssemblies()
                    .FirstOrDefault(a => string.Equals(a.GetName().Name, "iTextSharp", StringComparison.OrdinalIgnoreCase))
                    ?? Assembly.Load("iTextSharp");

                if (assembly == null)
                {
                    _loadError = "Assembly iTextSharp wurde nicht gefunden.";
                    return;
                }

                _pdfReaderType = assembly.GetType("iTextSharp.text.pdf.PdfReader");
                _pdfStamperType = assembly.GetType("iTextSharp.text.pdf.PdfStamper");
                _fontFactoryType = assembly.GetType("iTextSharp.text.FontFactory");
                _elementType = assembly.GetType("iTextSharp.text.Element");
                _baseColorType = assembly.GetType("iTextSharp.text.BaseColor");
                _baseFontType = assembly.GetType("iTextSharp.text.pdf.BaseFont");

                _iTextSharpLoaded = _pdfReaderType != null && _pdfStamperType != null && _baseFontType != null;
                _loadError = _iTextSharpLoaded ? string.Empty : "iTextSharp API konnte nicht vollständig aufgelöst werden.";
            }
            catch (Exception ex)
            {
                _loadError = ex.Message;
                System.Diagnostics.Debug.WriteLine("iTextSharp nicht ladbar: " + ex.Message);
                _iTextSharpLoaded = false;
            }
        }

        /// <summary>
        /// Fügt Wasserzeichen zu einer PDF hinzu
        /// </summary>
        public static void AddWatermark(string pdfPath, string encodedWatermarks, string deliveryDate, string deliveryLocation, string drawingNumberHint = null, WatermarkSettings settings = null)
        {
            if (!File.Exists(pdfPath))
                throw new FileNotFoundException("PDF-Datei nicht gefunden: " + pdfPath);

            if (!_iTextSharpLoaded)
                throw new InvalidOperationException(
                    "iTextSharp ist nicht ladbar. Bitte prüfen: itextsharp.dll und BouncyCastle.Cryptography.dll im App-Ordner. " +
                    (string.IsNullOrWhiteSpace(_loadError) ? string.Empty : "Details: " + _loadError));

            try
            {
                AddWatermarkWithITextSharp(pdfPath, encodedWatermarks, deliveryDate, deliveryLocation, drawingNumberHint, settings);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException("Fehler beim Hinzufügen des Wasserzeichens: " + ex.Message, ex);
            }
        }

        private static void AddWatermarkWithITextSharp(string pdfPath, string encodedWatermarks, string deliveryDate, string deliveryLocation, string drawingNumberHint, WatermarkSettings settingsOverride)
        {
            string tempPath = pdfPath + ".tmp";

            try
            {
                var entries = ParseEncodedEntries(encodedWatermarks);

                var orderNumbers = entries
                    .Where(e => !string.IsNullOrWhiteSpace(e.orderNumber))
                    .Select(e => e.orderNumber.Trim())
                    .Distinct()
                    .ToList();

                var settings = settingsOverride ?? WatermarkSettingsStore.Load();
                var fieldData = SmartWatermarkLayoutService.BuildFieldData(entries, orderNumbers);

                // PdfReader erstellen
                var pdfReaderCtor = _pdfReaderType.GetConstructor(new[] { typeof(string) });
                var reader = pdfReaderCtor.Invoke(new object[] { pdfPath });

                // FileStream erstellen
                var fileStream = new FileStream(tempPath, FileMode.Create);

                // PdfStamper erstellen
                var pdfStamperCtor = _pdfStamperType.GetConstructor(new[] { _pdfReaderType, typeof(FileStream) });
                var stamper = pdfStamperCtor.Invoke(new object[] { reader, fileStream });

                try
                {
                    // Seiten verarbeiten
                    var numberOfPages = (int)_pdfReaderType.GetProperty("NumberOfPages").GetValue(reader);
                    var getOverContentMethod = _pdfStamperType.GetMethod("GetOverContent", new[] { typeof(int) });
                    var getPageSizeMethod = _pdfReaderType.GetMethod("GetPageSizeWithRotation", new[] { typeof(int) })
                        ?? _pdfReaderType.GetMethod("GetPageSize", new[] { typeof(int) });

                    for (int page = 1; page <= numberOfPages; page++)
                    {
                        var contentByte = getOverContentMethod.Invoke(stamper, new object[] { page });

                        float pageWidth = 595f;  // A4 Fallback
                        float pageHeight = 842f; // A4 Fallback
                        if (getPageSizeMethod != null)
                        {
                            var rect = getPageSizeMethod.Invoke(reader, new object[] { page });
                            if (rect != null)
                            {
                                var widthProp = rect.GetType().GetProperty("Width");
                                var heightProp = rect.GetType().GetProperty("Height");

                                if (widthProp != null)
                                {
                                    var w = widthProp.GetValue(rect, null);
                                    if (w != null)
                                        pageWidth = Convert.ToSingle(w, System.Globalization.CultureInfo.InvariantCulture);
                                }

                                if (heightProp != null)
                                {
                                    var h = heightProp.GetValue(rect, null);
                                    if (h != null)
                                        pageHeight = Convert.ToSingle(h, System.Globalization.CultureInfo.InvariantCulture);
                                }
                            }
                        }

                        // Für Seite 1: alle entries, für Seiten 2+: nur orderNumbers
                        List<WatermarkTextPlacement> smartPlacements = null;
                        if (page == 1 && settings.UseSmartPlacement)
                        {
                            smartPlacements = SmartWatermarkLayoutService.Compute(
                                pdfPath,
                                page,
                                fieldData,
                                drawingNumberHint,
                                pageWidth,
                                pageHeight,
                                settings);
                        }

                        if (page == 1)
                        {
                            AddWatermarkToPage(contentByte, entries, orderNumbers, pageWidth, pageHeight, smartPlacements, settings);
                        }
                        else
                        {
                            AddWatermarkToPage(contentByte, new System.Collections.Generic.List<(string, string, string, string, string)>(), orderNumbers, pageWidth, pageHeight, null, settings);
                        }
                    }
                }
                finally
                {
                    // Stamper schließen
                    _pdfStamperType.GetMethod("Close").Invoke(stamper, null);
                    fileStream.Dispose();
                    (reader as IDisposable)?.Dispose();
                }

                // Datei ersetzen
                if (File.Exists(pdfPath))
                    File.Delete(pdfPath);
                File.Move(tempPath, pdfPath);
            }
            catch
            {
                if (File.Exists(tempPath))
                    File.Delete(tempPath);
                throw;
            }
        }

        private static void AddWatermarkToPage(
            object contentByte,
            System.Collections.Generic.List<(string quantity, string deliveryDate, string deliveryLocation, string bgDrawingNumber, string orderNumber)> entries,
            System.Collections.Generic.List<string> orderNumbers,
            float pageWidth,
            float pageHeight,
            List<WatermarkTextPlacement> smartPlacements,
            WatermarkSettings settings)
        {
            var beginTextMethod = contentByte.GetType().GetMethod("BeginText", Type.EmptyTypes);
            var endTextMethod = contentByte.GetType().GetMethod("EndText", Type.EmptyTypes);
            var setFontAndSizeMethod = contentByte.GetType().GetMethod("SetFontAndSize", new[] { _baseFontType, typeof(float) });
            var setColorFillMethod = contentByte.GetType().GetMethod("SetColorFill", new[] { _baseColorType });
            var showTextAlignedMethod = contentByte.GetType().GetMethod("ShowTextAligned", new[] { typeof(int), typeof(string), typeof(float), typeof(float), typeof(float) });

            if (beginTextMethod == null || endTextMethod == null || setFontAndSizeMethod == null || setColorFillMethod == null || showTextAlignedMethod == null)
                throw new InvalidOperationException("iTextSharp API konnte nicht vollständig aufgelöst werden (PdfContentByte). Bitte NuGet-Pakete prüfen.");

            beginTextMethod.Invoke(contentByte, null);

            try
            {
                // Schriftart als BaseFont (erforderlich für SetFontAndSize)
                var baseFont = GetHelveticaBaseFont();
                if (baseFont == null)
                    throw new InvalidOperationException("Schriftart konnte nicht initialisiert werden (BaseFont). Prüfen Sie iTextSharp/BouncyCastle-Version.");

                // Farbe - dunkelgrau für bessere Sichtbarkeit
                var color = GetDarkGrayColor();
                if (color != null)
                    setColorFillMethod.Invoke(contentByte, new[] { color });

                if (settings == null)
                    settings = WatermarkSettingsStore.Load();

                if (smartPlacements != null && smartPlacements.Count > 0)
                {
                    foreach (var placement in smartPlacements)
                    {
                        if (string.IsNullOrWhiteSpace(placement.Text))
                            continue;

                        setFontAndSizeMethod.Invoke(contentByte, new object[] { baseFont, placement.FontSize });
                        showTextAlignedMethod.Invoke(contentByte, new object[]
                        {
                            placement.Alignment,
                            placement.Text,
                            placement.X,
                            placement.Y,
                            placement.Angle
                        });
                    }

                    return;
                }

                // Fallback: manuelle Positionen aus Einstellungen
                var validEntries = entries
                    .Where(e => !string.IsNullOrWhiteSpace(e.quantity) || !string.IsNullOrWhiteSpace(e.deliveryDate) || !string.IsNullOrWhiteSpace(e.deliveryLocation))
                    .ToList();

                if (validEntries.Count > 0)
                {
                    // Baue Texte, aber OHNE "n/a" Fallback - nur echte Daten
                    var texts = new System.Collections.Generic.List<string>();
                    foreach (var e in validEntries)
                    {
                        var textParts = new System.Collections.Generic.List<string>();

                        if (!string.IsNullOrWhiteSpace(e.quantity))
                            textParts.Add("Menge: " + e.quantity.Trim());
                        if (!string.IsNullOrWhiteSpace(e.deliveryDate))
                            textParts.Add("Lieferdatum: " + e.deliveryDate.Trim());
                        if (!string.IsNullOrWhiteSpace(e.deliveryLocation))
                            textParts.Add("Lieferort: " + e.deliveryLocation.Trim());
                        if (!string.IsNullOrWhiteSpace(e.bgDrawingNumber))
                        {
                            var bg = e.bgDrawingNumber.Trim();
                            if (!bg.StartsWith("BG:", StringComparison.OrdinalIgnoreCase))
                                bg = "BG: " + bg;
                            textParts.Add(bg);
                        }

                        if (textParts.Count > 0)
                            texts.Add(string.Join("   |   ", textParts));
                    }

                    if (texts.Count > 0)
                    {
                        DrawDataWatermark(showTextAlignedMethod, setFontAndSizeMethod, contentByte, baseFont, texts, settings.DataPosition, pageWidth, pageHeight);
                    }
                }

                if (orderNumbers.Count > 0)
                {
                    var orderNumberText = string.Join(" | ", orderNumbers.Where(o => !string.IsNullOrWhiteSpace(o)));

                    if (!string.IsNullOrWhiteSpace(orderNumberText))
                    {
                        var orderPosition = settings.UseSmartPlacement
                            ? OrderNumberPosition.TopRight
                            : settings.OrderPosition;
                        DrawOrderNumberWatermark(showTextAlignedMethod, setFontAndSizeMethod, contentByte, baseFont, orderNumberText, orderPosition, pageWidth, pageHeight);
                    }
                }
            }
            finally
            {
                endTextMethod.Invoke(contentByte, null);
            }
        }

        private static void DrawDataWatermark(
            System.Reflection.MethodInfo showTextAlignedMethod,
            System.Reflection.MethodInfo setFontAndSizeMethod,
            object contentByte,
            object baseFont,
            System.Collections.Generic.List<string> texts,
            WatermarkDataPosition position,
            float pageWidth,
            float pageHeight)
        {
            var longestText = texts.OrderByDescending(t => t.Length).First();
            var isVertical = position == WatermarkDataPosition.LeftMiddleVertical
                             || position == WatermarkDataPosition.RightMiddleVertical;
            var maxLength = isVertical ? pageHeight - 140f : pageWidth - 100f;
            var scale = WatermarkPageScale.Get(pageWidth, pageHeight);
            var sharedFontSize = CalculateFittingFontSize(
                baseFont,
                longestText,
                maxLength,
                26f * scale,
                10f * scale);
            setFontAndSizeMethod.Invoke(contentByte, new object[] { baseFont, sharedFontSize });

            if (isVertical)
            {
                var spacing = Math.Max(28f * scale, sharedFontSize * 1.8f);
                var angle = position == WatermarkDataPosition.RightMiddleVertical ? 270f : 90f;
                var baseY = pageHeight / 2f;
                var startX = position == WatermarkDataPosition.RightMiddleVertical
                    ? pageWidth - 34f
                    : 34f;

                for (var i = 0; i < texts.Count; i++)
                {
                    var drawX = position == WatermarkDataPosition.RightMiddleVertical
                        ? startX - (i * spacing)
                        : startX + (i * spacing);
                    showTextAlignedMethod.Invoke(contentByte, new object[] { 1, texts[i], drawX, baseY, angle });
                }

                return;
            }

            var lineHeight = sharedFontSize + (10f * WatermarkPageScale.Get(pageWidth, pageHeight));
            var alignment = 0;
            float anchorX;
            float startY;
            var sideMargin = WatermarkPageScale.GetSideMargin(pageWidth, pageHeight);
            var topBaseline = WatermarkPageScale.GetTopBaselineY(pageWidth, pageHeight, sharedFontSize);
            var bottomMargin = Math.Max(40f, 50f * WatermarkPageScale.Get(pageWidth, pageHeight));

            switch (position)
            {
                case WatermarkDataPosition.TopRight:
                    alignment = 2;
                    anchorX = pageWidth - sideMargin;
                    startY = topBaseline;
                    break;
                case WatermarkDataPosition.BottomLeft:
                    anchorX = sideMargin;
                    startY = bottomMargin + ((texts.Count - 1) * lineHeight);
                    break;
                case WatermarkDataPosition.BottomRight:
                    alignment = 2;
                    anchorX = pageWidth - sideMargin;
                    startY = bottomMargin + ((texts.Count - 1) * lineHeight);
                    break;
                default:
                    anchorX = sideMargin;
                    startY = topBaseline;
                    break;
            }

            for (var i = 0; i < texts.Count; i++)
            {
                var y = position == WatermarkDataPosition.BottomLeft || position == WatermarkDataPosition.BottomRight
                    ? startY - (i * lineHeight)
                    : startY - (i * lineHeight);
                showTextAlignedMethod.Invoke(contentByte, new object[] { alignment, texts[i], anchorX, y, 0f });
            }
        }

        private static void DrawOrderNumberWatermark(
            System.Reflection.MethodInfo showTextAlignedMethod,
            System.Reflection.MethodInfo setFontAndSizeMethod,
            object contentByte,
            object baseFont,
            string orderNumberText,
            OrderNumberPosition position,
            float pageWidth,
            float pageHeight)
        {
            var scale = WatermarkPageScale.Get(pageWidth, pageHeight);
            var orderFontSize = CalculateFittingFontSize(baseFont, orderNumberText, pageWidth - 100f, 18f * scale, 10f * scale);
            setFontAndSizeMethod.Invoke(contentByte, new object[] { baseFont, orderFontSize });

            var sideMargin = WatermarkPageScale.GetSideMargin(pageWidth, pageHeight);
            var topBaseline = WatermarkPageScale.GetTopBaselineY(pageWidth, pageHeight, orderFontSize);
            float orderX;
            float orderY;
            int alignment;

            switch (position)
            {
                case OrderNumberPosition.TopLeft:
                    orderX = sideMargin;
                    orderY = topBaseline;
                    alignment = 0;
                    break;
                case OrderNumberPosition.BottomLeft:
                    orderX = sideMargin;
                    orderY = 40f * scale;
                    alignment = 0;
                    break;
                case OrderNumberPosition.BottomRight:
                    orderX = pageWidth - sideMargin;
                    orderY = 40f * scale;
                    alignment = 2;
                    break;
                default:
                    orderX = pageWidth - sideMargin;
                    orderY = topBaseline;
                    alignment = 2;
                    break;
            }

            showTextAlignedMethod.Invoke(contentByte, new object[] { alignment, orderNumberText, orderX, orderY, 0f });
        }

        private static object GetHelveticaBaseFont()
        {
            try
            {
                if (_baseFontType == null)
                    return null;

                // BaseFont.CreateFont("Helvetica", "Cp1252", false)
                var createFontMethod = _baseFontType.GetMethod("CreateFont", new[] { typeof(string), typeof(string), typeof(bool) });
                if (createFontMethod == null)
                    return null;

                return createFontMethod.Invoke(null, new object[] { "Helvetica", "Cp1252", false });
            }
            catch
            {
                return null;
            }
        }

        private static float CalculateFittingFontSize(object baseFont, string text, float maxLength, float maxSize, float minSize)
        {
            if (baseFont == null || string.IsNullOrWhiteSpace(text))
                return minSize;

            try
            {
                var getWidthPointMethod = _baseFontType.GetMethod("GetWidthPoint", new[] { typeof(string), typeof(float) });
                if (getWidthPointMethod == null)
                    return minSize;

                var size = maxSize;
                while (size >= minSize)
                {
                    var widthObj = getWidthPointMethod.Invoke(baseFont, new object[] { text, size });
                    var width = widthObj != null ? Convert.ToSingle(widthObj, System.Globalization.CultureInfo.InvariantCulture) : float.MaxValue;
                    if (width <= maxLength)
                        return size;

                    size -= 1f;
                }
            }
            catch
            {
                // Fallback unten
            }

            return minSize;
        }

        private static string BuildWatermarkText(string quantity, string deliveryDate, string deliveryLocation, string bgDrawingNumber)
        {
            var normalizedQty = NormalizeQuantity(quantity);
            var dateText = NormalizeDateText(deliveryDate);
            var locationText = NormalizeLocationText(deliveryLocation);
            var bgText = NormalizeBgText(bgDrawingNumber);

            return "Menge: " + normalizedQty + "   |   Lieferdatum: " + dateText + "   |   Lieferort: " + locationText + bgText;
        }

        private static string NormalizeOrderNumber(string orderNumber)
        {
            if (string.IsNullOrWhiteSpace(orderNumber))
                return string.Empty;
            return orderNumber.Trim();
        }

        private static System.Collections.Generic.List<(string quantity, string deliveryDate, string deliveryLocation, string bgDrawingNumber, string orderNumber)> ParseEncodedEntries(string encodedWatermarks)
        {
            var result = new System.Collections.Generic.List<(string quantity, string deliveryDate, string deliveryLocation, string bgDrawingNumber, string orderNumber)>();

            if (string.IsNullOrWhiteSpace(encodedWatermarks))
                return result;

            // Format: Menge|Datum|Ort|BG|Auftragsnummer§§§Menge|Datum|Ort|BG|Auftragsnummer...
            var entryParts = encodedWatermarks.Split(new[] { "§§§" }, StringSplitOptions.None);
            foreach (var part in entryParts)
            {
                if (string.IsNullOrWhiteSpace(part))
                    continue;

                var fields = SplitEscaped(part);
                var q = fields.Length > 0 ? UnescapeWatermarkValue(fields[0]) : string.Empty;
                var d = fields.Length > 1 ? UnescapeWatermarkValue(fields[1]) : string.Empty;
                var l = fields.Length > 2 ? UnescapeWatermarkValue(fields[2]) : string.Empty;
                var bg = fields.Length > 3 ? UnescapeWatermarkValue(fields[3]) : string.Empty;
                var o = fields.Length > 4 ? UnescapeWatermarkValue(fields[4]) : string.Empty;
                result.Add((q, d, l, bg, o));
            }

            return result;
        }

        private static string[] SplitEscaped(string value)
        {
            var parts = new System.Collections.Generic.List<string>();
            var current = new System.Text.StringBuilder();
            var escape = false;

            foreach (var ch in value)
            {
                if (escape)
                {
                    current.Append(ch);
                    escape = false;
                    continue;
                }

                if (ch == '\\')
                {
                    escape = true;
                    continue;
                }

                if (ch == '|')
                {
                    parts.Add(current.ToString());
                    current.Clear();
                    continue;
                }

                current.Append(ch);
            }

            parts.Add(current.ToString());
            return parts.ToArray();
        }

        private static string UnescapeWatermarkValue(string value)
        {
            if (string.IsNullOrEmpty(value))
                return string.Empty;

            return value.Replace("\\|", "|").Replace("\\\\", "\\");
        }

        private static string NormalizeQuantity(string quantity)
        {
            if (string.IsNullOrWhiteSpace(quantity))
                return string.Empty;

            var q = quantity.Trim();
            if (LooksLikeDate(q) || LooksLikeLocation(q))
                return string.Empty;

            if (!q.EndsWith("X", StringComparison.OrdinalIgnoreCase))
                q += "X";

            return q;
        }

        private static string NormalizeDateText(string deliveryDate)
        {
            if (string.IsNullOrWhiteSpace(deliveryDate))
                return string.Empty;

            var d = deliveryDate.Trim();
            if (LooksLikeLocation(d))
                return string.Empty;

            if (d.StartsWith("LT-", StringComparison.OrdinalIgnoreCase))
                return d;

            if (d.StartsWith("LT:", StringComparison.OrdinalIgnoreCase))
                d = d.Substring(3).TrimStart();

            return "LT-" + d;
        }

        private static string NormalizeLocationText(string deliveryLocation)
        {
            if (string.IsNullOrWhiteSpace(deliveryLocation))
                return string.Empty;

            var l = deliveryLocation.Trim();
            if (LooksLikeDate(l))
                return string.Empty;

            return l;
        }

        private static string NormalizeBgText(string bgDrawingNumber)
        {
            if (string.IsNullOrWhiteSpace(bgDrawingNumber))
                return string.Empty;

            var bg = bgDrawingNumber.Trim();
            if (!IsLikelyDrawing(bg) && !bg.StartsWith("BG:", StringComparison.OrdinalIgnoreCase))
                return string.Empty;

            if (!bg.StartsWith("BG:", StringComparison.OrdinalIgnoreCase))
                bg = "BG: " + bg;

            return "   |   " + bg;
        }

        private static bool LooksLikeDate(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return false;

            var v = value.Trim();
            if (System.Text.RegularExpressions.Regex.IsMatch(v, @"^\d{1,2}[\.\-/]\d{1,2}[\.\-/]\d{2,4}$"))
                return true;

            if (double.TryParse(v, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var oa)
                || double.TryParse(v, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.GetCultureInfo("de-DE"), out oa))
            {
                return oa > 20000 && oa < 90000;
            }

            return false;
        }

        private static bool LooksLikeLocation(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return false;

            var v = value.Trim();
            if (v.Any(char.IsLetter) && !v.Contains("-"))
                return true;

            return false;
        }

        private static bool IsLikelyDrawing(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return false;

            var v = value.Trim().ToUpperInvariant();
            return v.Contains("-") && v.Any(char.IsLetter) && v.Any(char.IsDigit);
        }

        private static object GetDarkGrayColor()
        {
            try
            {
                // new BaseColor(100, 100, 100)
                var darkGrayField = _baseColorType.GetField("DARK_GRAY");
                if (darkGrayField != null)
                    return darkGrayField.GetValue(null);

                var ctor = _baseColorType.GetConstructor(new[] { typeof(int), typeof(int), typeof(int) });
                return ctor.Invoke(new object[] { 100, 100, 100 });
            }
            catch
            {
                return null;
            }
        }

        public static string GetStatus()
        {
            return _iTextSharpLoaded
                ? "iTextSharp verfügbar"
                : "iTextSharp nicht ladbar" + (string.IsNullOrWhiteSpace(_loadError) ? string.Empty : ": " + _loadError);
        }

        public static bool IsAvailable()
        {
            return _iTextSharpLoaded;
        }

        public static void MergePdfFiles(IReadOnlyList<string> inputFiles, string outputFile)
        {
            if (inputFiles == null || inputFiles.Count == 0)
                throw new InvalidOperationException("Keine PDF-Dateien zum Zusammenführen angegeben.");

            if (!_iTextSharpLoaded)
                throw new InvalidOperationException(
                    "iTextSharp ist nicht ladbar. Bitte prüfen: itextsharp.dll und BouncyCastle.Cryptography.dll im App-Ordner. " +
                    (string.IsNullOrWhiteSpace(_loadError) ? string.Empty : "Details: " + _loadError));

            var assembly = AppDomain.CurrentDomain.GetAssemblies()
                .FirstOrDefault(a => string.Equals(a.GetName().Name, "iTextSharp", StringComparison.OrdinalIgnoreCase));
            if (assembly == null)
                throw new InvalidOperationException("iTextSharp-Assembly konnte nicht geladen werden.");

            var documentType = assembly.GetType("iTextSharp.text.Document");
            var pdfReaderType = assembly.GetType("iTextSharp.text.pdf.PdfReader");
            var pdfCopyType = assembly.GetType("iTextSharp.text.pdf.PdfCopy");
            var pdfImportedPageType = assembly.GetType("iTextSharp.text.pdf.PdfImportedPage");

            if (documentType == null || pdfReaderType == null || pdfCopyType == null || pdfImportedPageType == null)
                throw new InvalidOperationException("iTextSharp-Typen für Merge konnten nicht geladen werden.");

            var document = Activator.CreateInstance(documentType);
            FileStream stream = null;
            object copy = null;

            try
            {
                stream = new FileStream(outputFile, FileMode.Create, FileAccess.Write, FileShare.None);
                var copyCtor = pdfCopyType.GetConstructor(new[] { documentType, typeof(Stream) });
                if (copyCtor == null)
                    throw new InvalidOperationException("PdfCopy-Konstruktor konnte nicht aufgelöst werden.");

                copy = copyCtor.Invoke(new object[] { document, stream });
                documentType.GetMethod("Open", Type.EmptyTypes)?.Invoke(document, null);

                var getImportedPage = pdfCopyType.GetMethod("GetImportedPage", new[] { pdfReaderType, typeof(int) });
                var addPage = pdfCopyType.GetMethod("AddPage", new[] { pdfImportedPageType });
                var numberOfPagesProp = pdfReaderType.GetProperty("NumberOfPages");
                var readerCtor = pdfReaderType.GetConstructor(new[] { typeof(string) });

                if (getImportedPage == null || addPage == null || numberOfPagesProp == null || readerCtor == null)
                    throw new InvalidOperationException("iTextSharp-Methoden für Merge konnten nicht aufgelöst werden.");

                foreach (var file in inputFiles)
                {
                    if (!File.Exists(file))
                        throw new FileNotFoundException("PDF-Datei nicht gefunden: " + file);

                    var reader = readerCtor.Invoke(new object[] { file });
                    try
                    {
                        var pageCount = (int)numberOfPagesProp.GetValue(reader, null);
                        for (var p = 1; p <= pageCount; p++)
                        {
                            var page = getImportedPage.Invoke(copy, new object[] { reader, p });
                            addPage.Invoke(copy, new[] { page });
                        }
                    }
                    finally
                    {
                        pdfReaderType.GetMethod("Close", Type.EmptyTypes)?.Invoke(reader, null);
                        (reader as IDisposable)?.Dispose();
                    }
                }
            }
            finally
            {
                pdfCopyType?.GetMethod("Close", Type.EmptyTypes)?.Invoke(copy, null);
                documentType?.GetMethod("Close", Type.EmptyTypes)?.Invoke(document, null);
                stream?.Dispose();
            }
        }
    }
}
