using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using PDF_umbenenen.Platform.Contracts;
using PDF_umbenenen.Platform.Models;

namespace PDF_umbenenen.Platform.Core
{
    internal sealed class DocumentAnalysisPipeline : IDocumentAnalysisPipeline
    {
        private readonly IPluginManager _plugins;
        private readonly IPlatformLogger _logger;
        private readonly IIndexManager _index;
        private readonly IVectorStore _vectors;
        private readonly IEmbeddingModelProvider _embedder;
        private readonly IConfidenceScorer _confidence;

        public DocumentAnalysisPipeline(
            IPluginManager plugins,
            IPlatformLogger logger,
            IIndexManager index,
            IVectorStore vectors,
            IEmbeddingModelProvider embedder,
            IConfidenceScorer confidence)
        {
            _plugins = plugins;
            _logger = logger;
            _index = index;
            _vectors = vectors;
            _embedder = embedder;
            _confidence = confidence;
        }

        public Task<DocumentAnalysisResult> RunAsync(string filePath, CancellationToken cancellationToken = default)
            => Task.Run(() => Run(filePath, cancellationToken), cancellationToken);

        private DocumentAnalysisResult Run(string filePath, CancellationToken cancellationToken)
        {
            var sw = Stopwatch.StartNew();
            var result = new DocumentAnalysisResult
            {
                Document = new DocumentReference { FilePath = filePath },
                Fields = new ExtractedDocumentFields()
            };

            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                _logger.UserActivity("analyze-start", filePath);

                var detector = _plugins.Resolve<IDocumentFormatDetector>();
                result.Document.Format = detector?.Detect(filePath) ?? DetectFallback(filePath);
                result.ProcessingSteps.Add("format:" + result.Document.Format);

                if (result.Document.Format == DocumentFormat.Zip)
                {
                    var zip = _plugins.Resolve<IZipProcessor>();
                    var workspace = Path.Combine(Path.GetTempPath(), "DOK-V01", Guid.NewGuid().ToString("N"));
                    var zipFiles = zip?.ExtractToWorkspace(filePath, workspace) ?? new List<string>();
                    result.ProcessingSteps.Add("zip:" + zipFiles.Count);
                    var firstDoc = zipFiles.FirstOrDefault(f => DetectFallback(f) != DocumentFormat.Unknown);
                    if (!string.IsNullOrWhiteSpace(firstDoc))
                        return Run(firstDoc, cancellationToken);
                }

                var reader = _plugins.ResolveAll<IDocumentReader>()
                    .FirstOrDefault(r => r.SupportedFormat == result.Document.Format);
                if (reader != null)
                {
                    result.Document = reader.Open(filePath);
                    result.ProcessingSteps.Add("reader:" + reader.SupportedFormat);
                }

                if (result.Document.Format == DocumentFormat.Pdf)
                {
                    var meta = _plugins.Resolve<IPdfMetadataReader>();
                    var metadata = meta?.ReadMetadata(filePath);
                    if (metadata != null)
                        foreach (var pair in metadata)
                            result.Fields.Extensions["meta." + pair.Key] = pair.Value;

                    var pdfText = _plugins.Resolve<IPdfTextExtractor>();
                    result.PlainText = pdfText?.ExtractAllText(filePath, 5) ?? string.Empty;
                    result.ProcessingSteps.Add("pdf-text");

                    if (string.IsNullOrWhiteSpace(result.PlainText))
                    {
                        var ocr = _plugins.ResolveAll<IOcrEngine>().FirstOrDefault();
                        var ocrText = ocr?.RecognizeTitleBlock(filePath, 1) ?? string.Empty;
                        if (!string.IsNullOrWhiteSpace(ocrText))
                        {
                            result.PlainText = ocrText;
                            result.ProcessingSteps.Add("ocr:" + ocr.ProviderId);
                        }
                    }

                    var barcodes = _plugins.Resolve<IBarcodeReader>();
                    foreach (var code in barcodes?.ReadBarcodes(filePath) ?? Array.Empty<string>())
                        result.Fields.Extensions["barcode"] = code;

                    var qr = _plugins.Resolve<IQrCodeReader>();
                    var qrIndex = 0;
                    foreach (var code in qr?.ReadQrCodes(filePath) ?? Array.Empty<string>())
                        result.Fields.Extensions["qr." + (++qrIndex)] = code;
                }
                else if (result.Document.Format == DocumentFormat.Excel
                         || result.Document.Format == DocumentFormat.Word)
                {
                    var office = _plugins.Resolve<IOfficeDocumentReader>();
                    result.PlainText = office?.ExtractText(filePath) ?? string.Empty;
                    result.ProcessingSteps.Add("office-text");
                }
                else if (result.Document.Format == DocumentFormat.Image)
                {
                    var barcodes = _plugins.Resolve<IBarcodeReader>();
                    foreach (var code in barcodes?.ReadBarcodes(filePath) ?? Array.Empty<string>())
                        result.Fields.Extensions["barcode"] = code;
                }

                cancellationToken.ThrowIfCancellationRequested();

                var structuredVision = _plugins.Resolve<IStructuredVisionAnalyzer>();
                if (structuredVision != null && result.Document.Format == DocumentFormat.Pdf)
                {
                    var layout = structuredVision.AnalyzeStructured(filePath, 1);
                    result.Fields.Extensions["vision.confidence"] = layout.Confidence.ToString("0.###");
                    result.Fields.Extensions["vision.titleBlock"] = layout.HasTitleBlock.ToString();
                    result.Fields.Extensions["vision.table"] = layout.HasTable.ToString();
                    result.Fields.Extensions["vision.stamp"] = layout.HasStamp.ToString();
                    result.ProcessingSteps.Add("structured-vision");
                }

                var vision = _plugins.ResolveAll<IVisionAnalyzer>().FirstOrDefault();
                if (vision != null && result.Document.Format == DocumentFormat.Pdf)
                {
                    var layout = vision.AnalyzeLayout(filePath, 1);
                    if (layout != null && layout.Count > 0)
                    {
                        result.ProcessingSteps.Add("vision:" + vision.ProviderId);
                        foreach (var pair in layout)
                            result.Fields.Extensions[pair.Key] = pair.Value;
                    }
                }

                var semantic = _plugins.Resolve<ISemanticClassifier>();
                var semanticResult = semantic?.ClassifySemantic(result.PlainText);
                var classifier = _plugins.Resolve<IDocumentClassifier>();
                result.Classification = classifier?.Classify(result)
                    ?? semanticResult
                    ?? new ClassificationResult { Kind = DocumentKind.Unknown, Confidence = 0d };

                if (semanticResult != null && semanticResult.Confidence > result.Classification.Confidence)
                    result.Classification = semanticResult;

                var extractor = _plugins.Resolve<IInformationExtractor>();
                var extracted = extractor?.Extract(result) ?? new ExtractedDocumentFields();
                var conflict = _plugins.Resolve<IConflictResolver>();
                result.Fields = conflict?.Resolve(extracted, result.Fields) ?? extracted;

                var learning = _plugins.Resolve<ILearningEngine>();
                learning?.ApplyLearnedHints(result.Fields, filePath);

                result.Fields.Confidence = _confidence?.ScoreFields(result.Fields) ?? result.Fields.Confidence;
                result.ProcessingSteps.Add("extract");

                _index?.IndexDocument(filePath, result);
                if (_embedder != null && _vectors != null && !string.IsNullOrWhiteSpace(result.PlainText))
                {
                    var vector = _embedder.Embed(result.PlainText);
                    _vectors.UpsertEmbedding(Path.GetFileName(filePath), vector, result.Classification?.Kind.ToString());
                    result.ProcessingSteps.Add("vector-index");
                }

                _logger.Info("processing", "Dokument analysiert", new Dictionary<string, string>
                {
                    ["file"] = Path.GetFileName(filePath),
                    ["kind"] = result.Classification?.Kind.ToString(),
                    ["confidence"] = result.Fields.Confidence.ToString("0.###")
                });
            }
            catch (Exception ex)
            {
                result.Warnings.Add(ex.Message);
                _logger.Error("pipeline", "Analyse fehlgeschlagen", ex, new Dictionary<string, string> { ["file"] = filePath });
            }
            finally
            {
                _logger.Performance("document-analysis", sw.Elapsed, new Dictionary<string, string>
                {
                    ["file"] = Path.GetFileName(filePath)
                });
                _logger.UserActivity("analyze-end", filePath);
            }

            return result;
        }

        private static DocumentFormat DetectFallback(string filePath)
        {
            var ext = Path.GetExtension(filePath)?.ToLowerInvariant();
            switch (ext)
            {
                case ".pdf": return DocumentFormat.Pdf;
                case ".doc":
                case ".docx": return DocumentFormat.Word;
                case ".xls":
                case ".xlsx": return DocumentFormat.Excel;
                case ".png":
                case ".jpg":
                case ".jpeg":
                case ".tif":
                case ".tiff": return DocumentFormat.Image;
                case ".zip": return DocumentFormat.Zip;
                default: return DocumentFormat.Unknown;
            }
        }
    }
}
