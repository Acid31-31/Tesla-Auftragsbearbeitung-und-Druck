using System;
using System.IO;
using PDF_umbenenen.Platform.Contracts;
using PDF_umbenenen.Platform.Models;

namespace PDF_umbenenen.Platform.Modules.Documents
{
    internal sealed class DocumentFormatDetectorModule : IDocumentFormatDetector
    {
        public DocumentFormat Detect(string filePath)
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
                case ".bmp":
                case ".tif":
                case ".tiff": return DocumentFormat.Image;
                case ".zip": return DocumentFormat.Zip;
                case ".txt": return DocumentFormat.Text;
                default: return DocumentFormat.Unknown;
            }
        }
    }

    internal sealed class PdfDocumentReaderModule : IDocumentReader
    {
        public DocumentFormat SupportedFormat => DocumentFormat.Pdf;

        public DocumentReference Open(string filePath)
        {
            var pages = 1;
            try
            {
                if (PdfWatermarkService.IsAvailable())
                {
                    var textService = new Services.Ai.PdfTextExtractionService();
                    var text = textService.ExtractText(filePath, 100);
                    if (!string.IsNullOrWhiteSpace(text))
                        pages = Math.Max(1, text.Split('\n').Length / 40);
                }
            }
            catch
            {
            }

            return new DocumentReference
            {
                FilePath = filePath,
                Format = DocumentFormat.Pdf,
                PageCount = pages
            };
        }
    }

    internal sealed class PdfTextExtractorModule : IPdfTextExtractor
    {
        public string ExtractPageText(string pdfPath, int pageNumber = 1)
            => Services.PdfLayoutAnalysisService.ExtractPageText(pdfPath, pageNumber);

        public string ExtractAllText(string pdfPath, int maxPages = 10)
        {
            var service = new Services.Ai.PdfTextExtractionService();
            return service.ExtractText(pdfPath, maxPages);
        }
    }

    internal sealed class PdfMetadataReaderModule : IPdfMetadataReader
    {
        public System.Collections.Generic.IDictionary<string, string> ReadMetadata(string pdfPath)
        {
            var metadata = new System.Collections.Generic.Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["fileName"] = Path.GetFileName(pdfPath),
                ["lastWriteUtc"] = File.GetLastWriteTimeUtc(pdfPath).ToString("o")
            };

            try
            {
                using (var reader = new iTextSharp.text.pdf.PdfReader(pdfPath))
                {
                    metadata["pageCount"] = reader.NumberOfPages.ToString();
                    var info = reader.Info;
                    if (info != null)
                    {
                        foreach (var key in info.Keys)
                        {
                            var text = info[key]?.ToString();
                            if (!string.IsNullOrWhiteSpace(text))
                                metadata["pdf." + key] = text;
                        }
                    }
                }
            }
            catch
            {
            }

            return metadata;
        }
    }

    internal sealed class OfficeDocumentReaderModule : IOfficeDocumentReader
    {
        public string ExtractText(string filePath)
            => OfficeTextExtractor.ExtractText(filePath);
    }

    internal sealed class ZipProcessorModule : IZipProcessor
    {
        public System.Collections.Generic.IList<string> ExtractToWorkspace(string zipPath, string targetFolder)
        {
            var extracted = new System.Collections.Generic.List<string>();
            if (!File.Exists(zipPath))
                return extracted;

            Directory.CreateDirectory(targetFolder);
            System.IO.Compression.ZipFile.ExtractToDirectory(zipPath, targetFolder);
            extracted.AddRange(Directory.GetFiles(targetFolder, "*", SearchOption.AllDirectories));
            return extracted;
        }
    }
}
