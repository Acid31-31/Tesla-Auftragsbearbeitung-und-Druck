using System;
using System.Collections.Generic;
using PDF_umbenenen.Platform.Contracts;
using PDF_umbenenen.Services;
using PDF_umbenenen.Services.Ai;

namespace PDF_umbenenen.Platform.Modules.Vision
{
    internal sealed class TesseractTitleBlockOcrModule : IOcrEngine
    {
        public string ProviderId => "tesseract-cli";

        public string RecognizeTitleBlock(string filePath, int pageNumber = 1)
            => TitleBlockOcrService.TryReadTitleBlock(filePath, pageNumber);
    }

    /// <summary>
    /// Lokale Zeichnungsanalyse: PDF-Textlayer + OCR + Layout (ohne Ollama/Netzwerk).
    /// </summary>
    internal sealed class LocalDrawingVisionModule : IVisionAnalyzer
    {
        public string ProviderId => "local-drawing-vision";

        public IDictionary<string, string> AnalyzeLayout(string filePath, int pageNumber = 1)
        {
            var data = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var settings = AiSettingsStore.Load();
            if (!settings.LocalVisionEnabled)
                return data;

            var pageText = PdfLayoutAnalysisService.ExtractPageText(filePath, pageNumber) ?? string.Empty;
            if (string.IsNullOrWhiteSpace(pageText) && settings.LocalOcrEnabled)
                pageText = TitleBlockOcrService.TryReadTitleBlock(filePath, pageNumber) ?? string.Empty;

            if (!string.IsNullOrWhiteSpace(pageText))
                data["plainTextSample"] = pageText.Length > 400 ? pageText.Substring(0, 400) : pageText;

            var layout = PdfLayoutAnalysisService.AnalyzePage(filePath, pageNumber);
            if (layout != null)
            {
                data["textRectCount"] = (layout.TextRects?.Count ?? 0).ToString();
                data["pageWidth"] = layout.PageWidth.ToString("0");
                data["pageHeight"] = layout.PageHeight.ToString("0");
            }

            data["ocrStatus"] = TitleBlockOcrService.LastError ?? "ok";
            data["provider"] = ProviderId;
            data["confidence"] = string.IsNullOrWhiteSpace(pageText) ? "0.2" : "0.75";
            return data;
        }
    }
}
