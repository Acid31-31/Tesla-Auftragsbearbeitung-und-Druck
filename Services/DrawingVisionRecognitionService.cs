using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using PDF_umbenenen.Services.Ai;

namespace PDF_umbenenen.Services
{
    internal sealed class DrawingVisionResult
    {
        public bool ServerAvailable { get; set; }

        public bool Attempted { get; set; }

        public double Confidence { get; set; }

        public double? ThicknessMm { get; set; }

        public string ThicknessText { get; set; }

        public string Material { get; set; }

        public string RalCode { get; set; }

        public string Reason { get; set; }

        public string Error { get; set; }

        public string Model { get; set; }

        public bool HasReliableThickness
        {
            get
            {
                return Attempted
                       && ThicknessMm.HasValue
                       && ThicknessMm.Value >= 0.3d
                       && ThicknessMm.Value <= 25d
                       && Confidence >= MinThicknessConfidence;
            }
        }

        public bool HasReliableMaterial
        {
            get
            {
                return Attempted
                       && !string.IsNullOrWhiteSpace(Material)
                       && Confidence >= MinMaterialConfidence;
            }
        }

        public bool HasReliableRal
        {
            get
            {
                return Attempted
                       && !string.IsNullOrWhiteSpace(RalCode)
                       && Confidence >= MinRalConfidence;
            }
        }

        public const double MinThicknessConfidence = 0.62d;
        public const double MinMaterialConfidence = 0.55d;
        public const double MinRalConfidence = 0.70d;
    }

    /// <summary>
    /// Liest Blechdicke, Werkstoff-Kürzel und RAL mit dem Bildmodell aus AI_DOK-V01.
    /// Läuft automatisch mit der App; in den Einstellungen nur abschaltbar.
    /// Erfindet keine Werte bei Unsicherheit.
    /// </summary>
    internal static class DrawingVisionRecognitionService
    {
        private const int MaxImageEdge = 896;
        private const long JpegQuality = 72L;

        private static readonly object CacheLock = new object();
        private static readonly Dictionary<string, DrawingVisionResult> ResultCache =
            new Dictionary<string, DrawingVisionResult>(StringComparer.OrdinalIgnoreCase);

        private static readonly HttpClient OllamaClient = CreateHttpClient();

        private static readonly string SystemPrompt =
            "Du liest technische Blechzeichnungen. Antworte ausschließlich mit JSON. "
            + "Erfinde keine Maße. Wenn unsicher: thickness_mm=null und confidence<=0.4.";

        private static readonly string UserPrompt =
            "Lies diese technische Zeichnung (Seitenansichten können links, rechts, oben oder unten liegen).\n"
            + "Gesucht ist die Blechdicke / Wandstärke des Teils (typisch 0,5–6 mm, selten bis 15 mm), "
            + "nicht Rahmennummer, Lochkreis, Biegewinkel, Abwicklungslänge, Flanschlänge oder Stückzahl.\n"
            + "RAL nur wenn ein RAL-Code sichtbar ist.\n"
            + "Antwort-JSON:\n"
            + "{\"thickness_mm\":number|null,\"material\":\"VA\"|\"ST\"|\"ALU\"|null,"
            + "\"ral\":\"9003\"|null,\"confidence\":0.0-1.0,\"reason\":\"kurz\"}";

        private static DateTime _probeUtc = DateTime.MinValue;
        private static bool _probeOk;
        private static string _probeError = string.Empty;
        private static string _probeUrl = string.Empty;
        private static string _probeModel = string.Empty;
        private static string _lastStatus = string.Empty;

        public static string LastStatus => _lastStatus ?? string.Empty;

        public static void ClearCache()
        {
            lock (CacheLock)
                ResultCache.Clear();
        }

        public static void ResetRuntimeState()
        {
            ClearCache();
            InvalidateProbe();
        }

        public static void PrepareBatch()
        {
            var settings = AiSettingsStore.Load();
            if (settings == null || !settings.DrawingVisionEnabled)
                return;

            InvalidateProbe();
            try
            {
                Task.Run(() => OllamaPortableService.RestartServerAsync(settings, CancellationToken.None))
                    .GetAwaiter()
                    .GetResult();
            }
            catch
            {
            }

            ProbeServer(settings);
        }

        public static string GetStartupStatus()
        {
            var settings = AiSettingsStore.Load();
            if (settings == null || !settings.DrawingVisionEnabled)
                return "Zeichnungs-Vision: deaktiviert.";

            if (!OllamaPortableService.IsPortableLayoutReady(settings))
                return "Zeichnungs-Vision: KI-Ordner AI_DOK-V01 fehlt.";

            var model = OllamaPortableService.FindInstalledVisionModel(settings);
            if (string.IsNullOrWhiteSpace(model))
                return "Zeichnungs-Vision: Modul moondream fehlt in AI_DOK-V01.";

            return "Zeichnungs-Vision: " + OllamaPortableService.GetVisionModuleDisplayName(settings)
                   + " – nur wenn das Schriftfeld keine Blechdicke hat.";
        }

        public static DrawingVisionResult TryAnalyze(string pdfPath, int pageNumber)
        {
            var empty = new DrawingVisionResult();
            if (string.IsNullOrWhiteSpace(pdfPath) || !File.Exists(pdfPath))
                return empty;

            var settings = AiSettingsStore.Load();
            if (settings == null || !settings.DrawingVisionEnabled)
                return empty;

            var cacheKey = pdfPath + "|" + pageNumber + "|" + File.GetLastWriteTimeUtc(pdfPath).Ticks;
            lock (CacheLock)
            {
                DrawingVisionResult cached;
                if (ResultCache.TryGetValue(cacheKey, out cached) && cached != null)
                    return cached;
            }

            var result = AnalyzeUncached(pdfPath, pageNumber, settings);
            lock (CacheLock)
                ResultCache[cacheKey] = result;
            return result;
        }

        private static DrawingVisionResult AnalyzeUncached(string pdfPath, int pageNumber, AiSettings settings)
        {
            var result = new DrawingVisionResult();
            if (!ProbeServer(settings))
            {
                result.Error = _probeError;
                _lastStatus = result.Error;
                return result;
            }

            result.ServerAvailable = true;
            result.Model = _probeModel;

            if (!PdfRasterLayoutService.IsAvailable)
            {
                result.Error = "pdfium fehlt – Zeichnung kann nicht gerendert werden.";
                _lastStatus = result.Error;
                return result;
            }

            Bitmap page = null;
            try
            {
                page = PdfRasterLayoutService.RenderPageForVision(pdfPath, pageNumber);
                if (page == null)
                {
                    result.Error = "Seite konnte nicht gerendert werden.";
                    return result;
                }

                var dataUrl = EncodeJpegDataUrl(page);
                if (string.IsNullOrWhiteSpace(dataUrl))
                {
                    result.Error = "Zeichnungsbild konnte nicht kodiert werden.";
                    return result;
                }

                result.Attempted = true;
                var raw = CallVision(settings, dataUrl);
                if (string.IsNullOrWhiteSpace(raw))
                {
                    result.Error = string.IsNullOrWhiteSpace(_lastStatus)
                        ? "Vision-Modell lieferte keine Antwort."
                        : _lastStatus;
                    return result;
                }

                ParseVisionJson(raw, result);
                _lastStatus = result.HasReliableThickness
                    ? "Vision: " + result.ThicknessText
                      + (string.IsNullOrWhiteSpace(result.RalCode) ? string.Empty : " · RAL " + result.RalCode)
                    : (result.Confidence > 0
                        ? "Vision unsicher (confidence " + result.Confidence.ToString("0.00", CultureInfo.InvariantCulture) + ") – keine Stärke erfunden."
                        : "Vision ohne verwertbares JSON.");
            }
            catch (Exception ex)
            {
                result.Error = ex.Message;
                _lastStatus = result.Error;
                InvalidateProbe();
            }
            finally
            {
                if (page != null)
                    page.Dispose();
            }

            return result;
        }

        private static string CallVision(AiSettings settings, string dataUrl)
        {
            return Task.Run(() => CallVisionAsync(settings, dataUrl)).GetAwaiter().GetResult();
        }

        private static async Task<string> CallVisionAsync(AiSettings settings, string dataUrl)
        {
            var ollama = await TryOllamaNativeAsync(settings, dataUrl).ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(ollama))
                return ollama;

            var backend = new OpenAiCompatibleBackend();
            backend.Configure(CloneForUrl(settings, _probeUrl, _probeModel));
            using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(8)))
            {
                var openAi = await backend.ChatWithImageAsync(
                    SystemPrompt,
                    UserPrompt,
                    dataUrl,
                    true,
                    cts.Token).ConfigureAwait(false);
                if (!string.IsNullOrWhiteSpace(openAi))
                    return openAi;

                _lastStatus = backend.LastError;
            }

            return string.Empty;
        }

        private static async Task<string> TryOllamaNativeAsync(AiSettings settings, string dataUrl)
        {
            var marker = "data:image/jpeg;base64,";
            var comma = dataUrl.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            var b64 = comma >= 0 ? dataUrl.Substring(comma + marker.Length) : string.Empty;
            if (string.IsNullOrWhiteSpace(b64))
                return string.Empty;

            var model = !string.IsNullOrWhiteSpace(_probeModel)
                ? _probeModel
                : (OllamaPortableService.FindInstalledVisionModel(settings) ?? string.Empty);
            if (string.IsNullOrWhiteSpace(model))
                return string.Empty;

            var ollamaUrl = OllamaUrlHelper.DefaultBaseUrl + "/api/chat";

            var body = SimpleJsonHelper.BuildChatRequestWithImages(
                model,
                SystemPrompt,
                UserPrompt,
                new[] { b64 },
                SimpleJsonHelper.ShouldUseStrictJsonFormat(model));

            try
            {
                using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(8)))
                using (var content = new StringContent(body, Encoding.UTF8, "application/json"))
                using (var response = await OllamaClient.PostAsync(ollamaUrl, content, cts.Token).ConfigureAwait(false))
                {
                    var raw = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                    if (!response.IsSuccessStatusCode)
                        return string.Empty;

                    return SimpleJsonHelper.ExtractResponseField(raw);
                }
            }
            catch
            {
                return string.Empty;
            }
        }

        private static bool ProbeServer(AiSettings settings)
        {
            var age = (DateTime.UtcNow - _probeUtc).TotalSeconds;
            if (_probeUtc != DateTime.MinValue)
            {
                if (_probeOk && age < 180)
                    return true;
                if (!_probeOk && age < 20)
                    return false;
            }

            _probeOk = Task.Run(() => ProbeServerAsync(settings)).GetAwaiter().GetResult();
            _probeUtc = DateTime.UtcNow;
            return _probeOk;
        }

        private static async Task<bool> ProbeServerAsync(AiSettings settings)
        {
            using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(8)))
            {
                if (!await OllamaPortableService.EnsureVisionRuntimeAsync(settings, cts.Token).ConfigureAwait(false))
                {
                    _probeError = OllamaPortableService.LastError;
                    _probeUrl = string.Empty;
                    _probeModel = string.Empty;
                    return false;
                }
            }

            var visionModel = OllamaPortableService.FindInstalledVisionModel(settings);
            if (string.IsNullOrWhiteSpace(visionModel))
            {
                _probeError = "Zeichnungs-Vision-Modell fehlt in AI_DOK-V01.";
                _probeUrl = string.Empty;
                _probeModel = string.Empty;
                return false;
            }

            _probeUrl = LocalLlmUrlHelper.NormalizeBaseUrl(OllamaUrlHelper.DefaultBaseUrl);
            _probeModel = visionModel;
            _probeError = string.Empty;
            return true;
        }

        private static AiSettings CloneForUrl(AiSettings settings, string url, string model)
        {
            return new AiSettings
            {
                DrawingVisionEnabled = true,
                LocalLlmBaseUrl = url,
                LocalLlmModel = model ?? string.Empty,
                LocalLlmApiKey = string.Empty
            };
        }

        private static void InvalidateProbe()
        {
            _probeUtc = DateTime.MinValue;
            _probeOk = false;
        }

        private static void ParseVisionJson(string raw, DrawingVisionResult result)
        {
            var json = SimpleJsonHelper.ExtractJsonObject(raw);
            if (string.IsNullOrWhiteSpace(json))
                json = SimpleJsonHelper.StripMarkdownCodeFence(raw);

            double confidence;
            if (SimpleJsonHelper.TryGetJsonNullableDouble(json, "confidence", out var conf) && conf.HasValue)
                confidence = conf.Value;
            else
                confidence = SimpleJsonHelper.GetJsonDouble(json, "confidence", 0d);

            if (confidence > 1.01d)
                confidence = confidence / 100d;
            result.Confidence = Math.Max(0d, Math.Min(1d, confidence));

            double? thickness;
            if (!SimpleJsonHelper.TryGetJsonNullableDouble(json, "thickness_mm", out thickness) || !thickness.HasValue)
                SimpleJsonHelper.TryGetJsonNullableDouble(json, "thicknessMm", out thickness);

            if (thickness.HasValue)
            {
                result.ThicknessMm = thickness.Value;
            }
            else
            {
                var asText = SimpleJsonHelper.GetJsonString(json, "thickness_mm");
                double parsed;
                if (double.TryParse(
                    (asText ?? string.Empty).Replace(',', '.'),
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out parsed))
                {
                    result.ThicknessMm = parsed;
                }
            }

            if (result.ThicknessMm.HasValue
                && result.ThicknessMm.Value >= 0.3d
                && result.ThicknessMm.Value <= 25d)
            {
                result.ThicknessText = result.ThicknessMm.Value.ToString("0.0", CultureInfo.GetCultureInfo("de-DE"))
                                       + " mm";
            }
            else
            {
                result.ThicknessMm = null;
                result.ThicknessText = string.Empty;
            }

            result.Material = NormalizeMaterial(SimpleJsonHelper.GetJsonString(json, "material"));
            var ral = SimpleJsonHelper.GetJsonString(json, "ral");
            if (string.IsNullOrWhiteSpace(ral))
                ral = SimpleJsonHelper.GetJsonString(json, "ral_code");
            result.RalCode = NormalizeRal(ral);
            result.Reason = SimpleJsonHelper.GetJsonString(json, "reason");
        }

        private static string NormalizeMaterial(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
                return string.Empty;

            var value = raw.Trim().ToUpperInvariant();
            if (value == "NULL" || value == "NONE" || value == "N/A")
                return string.Empty;

            if (value == "VA" || value == "ST" || value == "ALU")
                return value;

            if (value.IndexOf("VA", StringComparison.Ordinal) >= 0
                || value.IndexOf("EDELSTAHL", StringComparison.Ordinal) >= 0
                || value.IndexOf("STAINLESS", StringComparison.Ordinal) >= 0)
                return "VA";

            if (value.IndexOf("ALU", StringComparison.Ordinal) >= 0
                || value.IndexOf("ALUMINIUM", StringComparison.Ordinal) >= 0)
                return "ALU";

            if (value.IndexOf("ST", StringComparison.Ordinal) >= 0
                || value.IndexOf("STEEL", StringComparison.Ordinal) >= 0)
                return "ST";

            return string.Empty;
        }

        private static string NormalizeRal(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
                return string.Empty;

            var match = Regex.Match(raw, @"\b([0-9]{4})\b");
            if (!match.Success)
                return string.Empty;

            var code = match.Groups[1].Value;
            if (code[0] == '0')
                return string.Empty;

            return code;
        }

        private static string EncodeJpegDataUrl(Bitmap source)
        {
            using (var scaled = ScaleIfNeeded(source, MaxImageEdge))
            using (var stream = new MemoryStream())
            {
                var codec = FindJpegCodec();
                if (codec == null)
                {
                    scaled.Save(stream, ImageFormat.Jpeg);
                }
                else
                {
                    using (var parameters = new EncoderParameters(1))
                    {
                        parameters.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, JpegQuality);
                        scaled.Save(stream, codec, parameters);
                    }
                }

                return "data:image/jpeg;base64," + Convert.ToBase64String(stream.ToArray());
            }
        }

        private static Bitmap ScaleIfNeeded(Bitmap source, int maxEdge)
        {
            var longest = Math.Max(source.Width, source.Height);
            if (longest <= maxEdge)
                return (Bitmap)source.Clone();

            var scale = maxEdge / (double)longest;
            var width = Math.Max(1, (int)Math.Round(source.Width * scale));
            var height = Math.Max(1, (int)Math.Round(source.Height * scale));
            var copy = new Bitmap(width, height, PixelFormat.Format24bppRgb);
            using (var graphics = Graphics.FromImage(copy))
            {
                graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                graphics.DrawImage(source, 0, 0, width, height);
            }

            return copy;
        }

        private static ImageCodecInfo FindJpegCodec()
        {
            foreach (var codec in ImageCodecInfo.GetImageEncoders())
            {
                if (codec.FormatID == ImageFormat.Jpeg.Guid)
                    return codec;
            }

            return null;
        }

        private static HttpClient CreateHttpClient()
        {
            var handler = new HttpClientHandler { UseProxy = false };
            return new HttpClient(handler) { Timeout = TimeSpan.FromMinutes(2) };
        }
    }
}
