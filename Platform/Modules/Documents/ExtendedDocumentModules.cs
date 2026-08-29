using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using PDF_umbenenen.Platform.Contracts;
using PDF_umbenenen.Platform.Models;

namespace PDF_umbenenen.Platform.Modules.Documents
{
    internal sealed class BarcodeReaderModule : IBarcodeReader
    {
        public IList<string> ReadBarcodes(string filePath)
        {
            var results = new List<string>();
            if (!File.Exists(filePath))
                return results;

            var ext = Path.GetExtension(filePath)?.ToLowerInvariant();
            if (ext == ".pdf")
            {
                var text = Services.PdfLayoutAnalysisService.ExtractPageText(filePath, 1);
                results.AddRange(ExtractCodeLikeTokens(text));
            }
            else if (IsImage(ext))
            {
                results.AddRange(ReadFromImageMetadata(filePath));
            }

            return results.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }

        private static bool IsImage(string ext)
            => ext == ".png" || ext == ".jpg" || ext == ".jpeg" || ext == ".bmp" || ext == ".tif" || ext == ".tiff";

        private static IEnumerable<string> ReadFromImageMetadata(string filePath)
        {
            var results = new List<string>();
            try
            {
                using (var img = Image.FromFile(filePath))
                {
                    foreach (var id in img.PropertyIdList ?? Array.Empty<int>())
                    {
                        try
                        {
                            var item = img.GetPropertyItem(id);
                            if (item?.Value == null)
                                continue;

                            var text = System.Text.Encoding.UTF8.GetString(item.Value).Trim('\0');
                            if (!string.IsNullOrWhiteSpace(text) && text.Length >= 6)
                                results.Add(text);
                        }
                        catch
                        {
                        }
                    }
                }
            }
            catch
            {
            }

            return results;
        }

        private static IEnumerable<string> ExtractCodeLikeTokens(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                yield break;

            foreach (Match match in Regex.Matches(text, @"\b[A-Z0-9]{8,24}\b"))
                yield return match.Value;
        }
    }

    internal sealed class QrCodeReaderModule : IQrCodeReader
    {
        public IList<string> ReadQrCodes(string filePath)
        {
            var results = new List<string>();
            if (!File.Exists(filePath))
                return results;

            var text = string.Empty;
            var ext = Path.GetExtension(filePath)?.ToLowerInvariant();
            if (ext == ".pdf")
                text = Services.PdfLayoutAnalysisService.ExtractPageText(filePath, 1);
            else if (ext == ".txt")
                text = File.ReadAllText(filePath);

            foreach (Match match in Regex.Matches(text ?? string.Empty, @"https?://\S+|www\.\S+"))
                results.Add(match.Value.Trim());

            return results.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }
    }

    internal sealed class ImagePreprocessorModule : IImagePreprocessor
    {
        public byte[] PrepareRegion(byte[] imageBytes, string profileId)
        {
            if (imageBytes == null || imageBytes.Length == 0)
                return imageBytes;

            try
            {
                using (var input = new MemoryStream(imageBytes))
                using (var bitmap = new Bitmap(input))
                using (var output = new MemoryStream())
                {
                    var contrast = profileId != null && profileId.IndexOf("high", StringComparison.OrdinalIgnoreCase) >= 0
                        ? 1.2f
                        : 1.0f;

                    using (var adjusted = AdjustContrast(bitmap, contrast))
                    {
                        adjusted.Save(output, System.Drawing.Imaging.ImageFormat.Png);
                        return output.ToArray();
                    }
                }
            }
            catch
            {
                return imageBytes;
            }
        }

        private static Bitmap AdjustContrast(Bitmap source, float contrast)
        {
            var result = new Bitmap(source.Width, source.Height);
            var factor = (259f * (contrast * 255f + 255f)) / (255f * (259f - contrast * 255f));

            for (var y = 0; y < source.Height; y++)
            {
                for (var x = 0; x < source.Width; x++)
                {
                    var pixel = source.GetPixel(x, y);
                    var r = Clamp((int)(factor * (pixel.R - 128) + 128));
                    var g = Clamp((int)(factor * (pixel.G - 128) + 128));
                    var b = Clamp((int)(factor * (pixel.B - 128) + 128));
                    result.SetPixel(x, y, Color.FromArgb(pixel.A, r, g, b));
                }
            }

            return result;
        }

        private static int Clamp(int value) => Math.Max(0, Math.Min(255, value));
    }

    internal sealed class MultiPageProcessorModule : IMultiPageProcessor
    {
        private readonly IPluginManager _plugins;

        public MultiPageProcessorModule(IPluginManager plugins)
        {
            _plugins = plugins;
        }

        public IList<DocumentAnalysisResult> ProcessAllPages(string filePath, CancellationToken cancellationToken)
        {
            var results = new List<DocumentAnalysisResult>();
            if (!File.Exists(filePath))
                return results;

            var reader = _plugins.ResolveAll<IDocumentReader>()
                .FirstOrDefault(r => r.SupportedFormat == DocumentFormat.Pdf);
            var pages = reader?.Open(filePath)?.PageCount ?? 1;
            pages = Math.Max(1, Math.Min(pages, 50));

            var extractor = _plugins.Resolve<IPdfTextExtractor>();
            var classifier = _plugins.Resolve<IDocumentClassifier>();
            var infoExtractor = _plugins.Resolve<IInformationExtractor>();

            for (var page = 1; page <= pages; page++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var partial = new DocumentAnalysisResult
                {
                    Document = new DocumentReference
                    {
                        FilePath = filePath,
                        Format = DocumentFormat.Pdf,
                        PageCount = pages
                    },
                    PlainText = extractor?.ExtractPageText(filePath, page) ?? string.Empty,
                    Fields = new ExtractedDocumentFields()
                };

                partial.Classification = classifier?.Classify(partial)
                    ?? new ClassificationResult { Kind = DocumentKind.Unknown, Confidence = 0d };
                partial.Fields = infoExtractor?.Extract(partial) ?? new ExtractedDocumentFields();
                partial.ProcessingSteps.Add("page:" + page);
                results.Add(partial);
            }

            return results;
        }
    }
}
