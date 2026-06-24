using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using PDF_umbenenen.Models;

namespace PDF_umbenenen.Services.Ai
{
    internal sealed class OllamaMaterialExtractionResult
    {
        public string ThicknessText { get; set; }

        public string MaterialType { get; set; }

        public bool IsAssembly { get; set; }

        public double Confidence { get; set; }
    }

    /// <summary>
    /// Liest Blechdicke/Material per Ollama-Vision (localhost) aus gerenderter PDF-Seite.
    /// </summary>
    internal static class OllamaMaterialThicknessService
    {
        private const int MaxImageWidthPx = 768;
        private const int JpegQuality = 60;
        private const int AvailabilityCacheSeconds = 30;

        private static readonly HttpClient HttpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(60)
        };

        private static readonly object CacheLock = new object();
        private static readonly System.Collections.Generic.Dictionary<string, OllamaMaterialExtractionResult> ResultCache =
            new System.Collections.Generic.Dictionary<string, OllamaMaterialExtractionResult>(StringComparer.OrdinalIgnoreCase);

        private static DateTime _lastAvailabilityCheck = DateTime.MinValue;
        private static bool _ollamaAvailable;

        internal static string LastRawResponse { get; private set; }

        internal static string LastParseError { get; private set; }

        public static void ClearCache()
        {
            lock (CacheLock)
                ResultCache.Clear();
        }

        public static OllamaMaterialExtractionResult TryExtract(string pdfPath, int pageNumber)
        {
            if (string.IsNullOrWhiteSpace(pdfPath) || !File.Exists(pdfPath))
                return null;

            var settings = AiSettingsStore.Load();

            if (!PdfRasterLayoutService.IsAvailable)
                return null;

            if (!EnsureOllamaAvailable(settings))
                return null;

            var cacheKey = pdfPath + "|" + pageNumber + "|" + File.GetLastWriteTimeUtc(pdfPath).Ticks
                + "|" + (settings.VisionModelName ?? settings.ModelName);
            lock (CacheLock)
            {
                if (ResultCache.TryGetValue(cacheKey, out var cached))
                    return cached;
            }

            var result = ExtractUncached(pdfPath, pageNumber, settings);
            lock (CacheLock)
                ResultCache[cacheKey] = result;

            return result;
        }

        private static bool EnsureOllamaAvailable(AiSettings settings)
        {
            if ((DateTime.UtcNow - _lastAvailabilityCheck).TotalSeconds < AvailabilityCacheSeconds)
                return _ollamaAvailable;

            _ollamaAvailable = false;
            _lastAvailabilityCheck = DateTime.UtcNow;

            if (!OllamaRuntimeService.EnsureRunning(settings.OllamaBaseUrl))
                return false;

            try
            {
                var url = NormalizeBaseUrl(settings.OllamaBaseUrl) + "/api/tags";
                using (var response = HttpClient.GetAsync(url).GetAwaiter().GetResult())
                {
                    _ollamaAvailable = response.IsSuccessStatusCode;
                }
            }
            catch
            {
                _ollamaAvailable = false;
            }

            return _ollamaAvailable;
        }

        private static OllamaMaterialExtractionResult ExtractUncached(string pdfPath, int pageNumber, AiSettings settings)
        {
            var drawingHint = ExtractPrimaryDrawingNumber(pdfPath, pageNumber);

            Bitmap bitmap = null;
            try
            {
                LastRawResponse = string.Empty;
                LastParseError = string.Empty;

                bitmap = PdfRasterLayoutService.RenderPageForVision(pdfPath, pageNumber);
                if (bitmap == null)
                {
                    LastParseError = "render-null";
                    return null;
                }

                using (var resized = ResizeForVision(bitmap))
                {
                    var base64 = EncodeJpegBase64(resized);
                    if (string.IsNullOrWhiteSpace(base64))
                    {
                        LastParseError = "jpeg-empty";
                        return null;
                    }

                    if (string.Equals(Environment.GetEnvironmentVariable("PDF_OLLAMA_SAVE_JPEG"), "1", StringComparison.OrdinalIgnoreCase))
                    {
                        try
                        {
                            var debugPath = Path.Combine(Path.GetTempPath(), "pdf-ollama-page-" + pageNumber + ".jpg");
                            File.WriteAllBytes(debugPath, Convert.FromBase64String(base64));
                        }
                        catch
                        {
                        }
                    }

                    var modelsToTry = ResolveVisionModels(settings);
                    string json = string.Empty;
                    foreach (var model in modelsToTry)
                    {
                        json = CallOllamaVision(settings, model, drawingHint, base64);
                        if (!string.IsNullOrWhiteSpace(json))
                            break;
                    }

                    LastRawResponse = json ?? string.Empty;
                    return ParseResponse(json);
                }
            }
            catch (Exception ex)
            {
                LastParseError = ex.GetType().Name + ": " + ex.Message;
                return null;
            }
            finally
            {
                bitmap?.Dispose();
            }
        }

        private static string CallOllamaVision(AiSettings settings, string model, string drawingHint, string base64Image)
        {
            var systemPrompt =
                "Du bist ein erfahrener Arbeitsvorbereiter für Blechzeichnungen. " +
                "Antworte ausschließlich mit gültigem JSON ohne Erklärung.";

            var userPrompt =
                "Technische Blechzeichnung analysieren.\r\n" +
                "Zeichnungsnummer: " + (string.IsNullOrWhiteSpace(drawingHint) ? "unbekannt" : drawingHint) + "\r\n\r\n" +
                "Antwort nur als JSON:\r\n" +
                "{\"isAssembly\":false,\"thicknessMm\":2.0,\"material\":\"DC01\",\"confidence\":0.9}\r\n\r\n" +
                "Regeln:\r\n" +
                "- isAssembly=true bei Baugruppe/Stückliste/mehreren Bauteilen\r\n" +
                "- thicknessMm = Blechdicke/Profilhöhe in der Seitenansicht oder aus t=, THK, Blechdicke, PLATE THK im Schriftfeld\r\n" +
                "- NIEMALS Bohrungsdurchmesser (Ø, phi, Durchmesser, Werte in Klammern wie (7,7))\r\n" +
                "- NIEMALS Werkstoffnummern (1.0038, 1.4301 sind Material, keine Dicke)\r\n" +
                "- Bei PLATE/Platte: Dicke oft 8-20 mm aus schmalem Seitenprofil\r\n" +
                "- Bei Baugruppe: isAssembly=true, thicknessMm=null\r\n" +
                "- Keine Berechnung aus Gewicht\r\n" +
                "- material aus Schriftfeld MAT./MATERIAL/Werkstoff (z. B. 1.0330/DC01, DC01, AlMg3)\r\n" +
                "- material nur wenn im Schriftfeld lesbar, sonst \"\"\r\n" +
                "- confidence 0.0 bis 1.0 (niedrig wenn unsicher)";

            var url = NormalizeBaseUrl(settings.OllamaBaseUrl) + "/api/chat";
            var body = SimpleJsonHelper.BuildChatRequestWithImages(
                model,
                systemPrompt,
                userPrompt,
                new[] { base64Image },
                jsonFormat: SimpleJsonHelper.ShouldUseStrictJsonFormat(model));

            for (var attempt = 0; attempt < 2; attempt++)
            {
                using (var content = new StringContent(body, Encoding.UTF8, "application/json"))
                using (var response = HttpClient.PostAsync(url, content, CancellationToken.None).GetAwaiter().GetResult())
                {
                    var responseText = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                    if (response.IsSuccessStatusCode)
                        return responseText;

                    LastParseError = "http-" + (int)response.StatusCode + ": " + Truncate(responseText, 180);
                    if (attempt == 0 && (int)response.StatusCode >= 500)
                    {
                        Thread.Sleep(1500);
                        continue;
                    }

                    return string.Empty;
                }
            }

            return string.Empty;
        }

        private static OllamaMaterialExtractionResult ParseResponse(string rawJson)
        {
            if (string.IsNullOrWhiteSpace(rawJson))
                return null;

            var content = SimpleJsonHelper.ExtractResponseField(rawJson);
            if (string.IsNullOrWhiteSpace(content))
                content = rawJson;

            var jsonPayload = SimpleJsonHelper.ExtractJsonObject(content);
            if (string.IsNullOrWhiteSpace(jsonPayload))
                jsonPayload = content;

            var result = new OllamaMaterialExtractionResult
            {
                IsAssembly = SimpleJsonHelper.GetJsonBool(jsonPayload, "isAssembly", false)
                    || LooksLikeAssemblyResponse(content)
            };

            if (result.IsAssembly)
                return result;

            var confidence = SimpleJsonHelper.GetJsonDouble(jsonPayload, "confidence", 0d);
            result.Confidence = confidence;
            if (confidence > 0d && confidence < 0.35d)
                return result;

            double? thicknessMm = null;
            if (SimpleJsonHelper.TryGetJsonNullableDouble(jsonPayload, "thicknessMm", out var parsedThickness))
                thicknessMm = parsedThickness;

            if (!thicknessMm.HasValue)
                thicknessMm = TryParseThicknessFromLooseText(content);

            if (thicknessMm.HasValue && IsPlausibleSheetThickness(thicknessMm.Value))
            {
                result.ThicknessText = thicknessMm.Value.ToString("0.0", CultureInfo.GetCultureInfo("de-DE")) + " mm";
            }

            var material = SimpleJsonHelper.GetJsonString(jsonPayload, "material");
            if (string.IsNullOrWhiteSpace(material))
                material = TryParseMaterialFromLooseText(content);

            if (!string.IsNullOrWhiteSpace(material)
                && material.IndexOf("unknown", StringComparison.OrdinalIgnoreCase) < 0
                && material.Length <= 32)
            {
                result.MaterialType = material.Trim();
            }

            return result;
        }

        private static bool LooksLikeAssemblyResponse(string content)
        {
            if (string.IsNullOrWhiteSpace(content))
                return false;

            if (SimpleJsonHelper.GetJsonBool(content, "isAssembly", false))
                return true;

            return Regex.IsMatch(
                content,
                @"\b(baugruppe|assembly|st[üu]ckliste|bill\s*of\s*material|bom)\b",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        }

        private static double? TryParseThicknessFromLooseText(string content)
        {
            if (string.IsNullOrWhiteSpace(content))
                return null;

            var patterns = new[]
            {
                @"""thicknessMm""\s*:\s*([0-9]+(?:\.[0-9]+)?)",
                @"\bt\s*=\s*([0-9]+(?:\.[0-9]+)?)\s*mm?\b",
                @"\bTHK\s*[:=]?\s*([0-9]+(?:\.[0-9]+)?)\s*mm?\b",
                @"\bBlechdicke\s*[:=]?\s*([0-9]+(?:\.[0-9]+)?)\s*mm?\b",
                @"\bAUSSENMABE\s*[:=]?\s*([0-9]+(?:\.[0-9]+)?)\s*mm?\b"
            };

            foreach (var pattern in patterns)
            {
                var match = Regex.Match(content, pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
                if (!match.Success)
                    continue;

                if (double.TryParse(match.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var mm)
                    && IsPlausibleSheetThickness(mm))
                {
                    return mm;
                }
            }

            return null;
        }

        private static string TryParseMaterialFromLooseText(string content)
        {
            if (string.IsNullOrWhiteSpace(content))
                return string.Empty;

            var match = Regex.Match(
                content,
                @"""material""\s*:\s*""([^""]{1,32})""",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

            return match.Success ? match.Groups[1].Value.Trim() : string.Empty;
        }

        private static bool IsPlausibleSheetThickness(double mm)
        {
            return mm >= 0.3d && mm <= ThicknessCandidateResolver.MaxSideViewThicknessMm;
        }

        private static string[] ResolveVisionModels(AiSettings settings)
        {
            var primary = ResolveVisionModelName(settings);
            if (string.IsNullOrWhiteSpace(primary))
                return new[] { "llava:7b" };

            if (primary.IndexOf("moondream", StringComparison.OrdinalIgnoreCase) >= 0)
                return new[] { primary, "llava:7b" };

            return new[] { primary };
        }

        private static string ResolveVisionModelName(AiSettings settings)
        {
            if (!string.IsNullOrWhiteSpace(settings.VisionModelName))
                return settings.VisionModelName.Trim();

            return "llava:7b";
        }

        private static string ExtractPrimaryDrawingNumber(string pdfPath, int pageNumber)
        {
            var titleBlockText = PdfLayoutAnalysisService.ExtractPageText(pdfPath, pageNumber);
            return OrderVerificationDrawingKey.ExtractPrimaryFromTexts(titleBlockText);
        }

        private static int ExtractScaleDenominator(string pdfPath, int pageNumber)
        {
            var text = PdfLayoutAnalysisService.ExtractPageText(pdfPath, pageNumber);
            if (string.IsNullOrWhiteSpace(text))
                return 1;

            var match = Regex.Match(
                text,
                @"(?:MASSSTAB|MAßSTAB|SCALE)\s*1\s*:\s*(\d+)",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

            if (match.Success
                && int.TryParse(match.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var denom)
                && denom > 0)
            {
                return denom;
            }

            return 1;
        }

        private static Bitmap ResizeForVision(Bitmap source)
        {
            if (source.Width <= MaxImageWidthPx)
                return (Bitmap)source.Clone();

            var scale = MaxImageWidthPx / (float)source.Width;
            var width = MaxImageWidthPx;
            var height = Math.Max(1, (int)Math.Round(source.Height * scale));

            var resized = new Bitmap(width, height);
            using (var graphics = Graphics.FromImage(resized))
            {
                graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                graphics.DrawImage(source, 0, 0, width, height);
            }

            return resized;
        }

        private static string EncodeJpegBase64(Bitmap bitmap)
        {
            using (var stream = new MemoryStream())
            {
                using (var rgbBitmap = EnsureRgbBitmap(bitmap))
                {
                    var encoder = GetJpegEncoder();
                    if (encoder != null)
                    {
                        using (var parameters = new EncoderParameters(1))
                        {
                            parameters.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, (long)JpegQuality);
                            rgbBitmap.Save(stream, encoder, parameters);
                        }
                    }
                    else
                    {
                        rgbBitmap.Save(stream, ImageFormat.Jpeg);
                    }
                }

                return Convert.ToBase64String(stream.ToArray());
            }
        }

        private static Bitmap EnsureRgbBitmap(Bitmap source)
        {
            if (source.PixelFormat == PixelFormat.Format24bppRgb)
                return (Bitmap)source.Clone();

            var rgb = new Bitmap(source.Width, source.Height, PixelFormat.Format24bppRgb);
            using (var graphics = Graphics.FromImage(rgb))
            {
                graphics.Clear(Color.White);
                graphics.DrawImage(source, 0, 0, source.Width, source.Height);
            }

            return rgb;
        }

        private static ImageCodecInfo GetJpegEncoder()
        {
            var codecs = ImageCodecInfo.GetImageEncoders();
            foreach (var codec in codecs)
            {
                if (codec.FormatID == ImageFormat.Jpeg.Guid)
                    return codec;
            }

            return null;
        }

        private static string NormalizeBaseUrl(string baseUrl)
        {
            if (string.IsNullOrWhiteSpace(baseUrl))
                return "http://localhost:11434";

            return baseUrl.Trim().TrimEnd('/');
        }

        private static string Truncate(string value, int maxLength)
        {
            if (string.IsNullOrEmpty(value) || value.Length <= maxLength)
                return value ?? string.Empty;

            return value.Substring(0, maxLength) + "...";
        }
    }
}
