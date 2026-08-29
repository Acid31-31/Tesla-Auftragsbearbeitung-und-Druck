using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using PDF_umbenenen.Platform.Contracts;
using PDF_umbenenen.Platform.Models;
using PDF_umbenenen.Services;

namespace PDF_umbenenen.Platform.Modules.Vision
{
    internal sealed class StructuredVisionModule : IStructuredVisionAnalyzer
    {
        public VisionLayoutResult AnalyzeStructured(string filePath, int pageNumber = 1)
        {
            var result = new VisionLayoutResult();
            if (string.IsNullOrWhiteSpace(filePath))
                return result;

            try
            {
                var layout = PdfLayoutAnalysisService.AnalyzePage(filePath, pageNumber);
                var text = PdfLayoutAnalysisService.ExtractPageText(filePath, pageNumber) ?? string.Empty;
                if (string.IsNullOrWhiteSpace(text))
                    text = TitleBlockOcrService.TryReadTitleBlock(filePath, pageNumber) ?? string.Empty;

                var upper = text.ToUpperInvariant();

                result.HasTitleBlock = layout?.TextRects?.Count > 0
                    || upper.Contains("DRAWING")
                    || upper.Contains("ZEICHNUNGSNR")
                    || upper.Contains("TITLE");

                result.HasTable = Regex.IsMatch(text, @"\b(QTY|STÜCK|MENGE|POS\.?|ITEM)\b", RegexOptions.IgnoreCase)
                    || upper.Contains("BILL OF MATERIAL");

                result.HasStamp = upper.Contains("STAMP") || upper.Contains("STEMPEL") || upper.Contains("APPROVED");
                result.HasLogo = upper.Contains("TESLA") || upper.Contains("LOGO");
                result.HasSignature = upper.Contains("SIGN") || upper.Contains("UNTERSCHRIFT");
                result.HasTechnicalSymbols = Regex.IsMatch(text, @"[⌀Ø⌖⌯±°]", RegexOptions.CultureInvariant)
                    || upper.Contains("TOLERANCE");

                foreach (Match match in Regex.Matches(text, @"\b\d+(?:[.,]\d+)?\s*(?:MM|CM|M|IN)\b", RegexOptions.IgnoreCase))
                    result.Dimensions.Add(match.Value);

                foreach (Match match in Regex.Matches(text, @"\b(?:POS|ITEM|REF)\s*[#:.]?\s*(\d{1,4})\b", RegexOptions.IgnoreCase))
                    result.PositionNumbers.Add(match.Groups[1].Value);

                var localVision = new LocalDrawingVisionModule();
                var visionData = localVision.AnalyzeLayout(filePath, pageNumber);
                if (visionData != null)
                {
                    foreach (var pair in visionData)
                        result.RawFeatures[pair.Key] = pair.Value;
                }

                var featureCount = new[]
                {
                    result.HasTitleBlock,
                    result.HasTable,
                    result.HasStamp,
                    result.HasLogo,
                    result.HasSignature,
                    result.HasTechnicalSymbols
                }.Count(f => f);

                result.Confidence = Math.Min(0.95d, 0.35d + featureCount * 0.1d + result.Dimensions.Count * 0.02d);
            }
            catch
            {
                result.Confidence = 0d;
            }

            return result;
        }
    }
}
