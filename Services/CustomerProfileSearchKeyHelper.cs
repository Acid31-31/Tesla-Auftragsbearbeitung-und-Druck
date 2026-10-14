using System;
using System.IO;
using PDF_umbenenen.Models;
using PDF_umbenenen.Services.Ai;

namespace PDF_umbenenen.Services
{
    internal static class CustomerProfileSearchKeyHelper
    {
        public static string ResolveSearchKey(
            string fullPath,
            string originalNameWithoutExtension,
            CustomerProfile profile,
            PdfTextExtractionService textService)
        {
            var baseName = StripRevSuffix(originalNameWithoutExtension ?? string.Empty);
            if (string.IsNullOrWhiteSpace(baseName))
                return string.Empty;

            var fromName = CustomerProfileDrawingMatcher.ExtractBest(baseName, profile);
            if (!string.IsNullOrWhiteSpace(fromName))
                return CustomerProfileDrawingMatcher.NormalizeKey(fromName, profile);

            if (textService != null && !string.IsNullOrWhiteSpace(fullPath) && File.Exists(fullPath))
            {
                try
                {
                    var text = textService.ExtractText(fullPath, 6);
                    if (string.IsNullOrWhiteSpace(text) || text.Trim().Length < 12)
                    {
                        var ocr = TitleBlockOcrService.TryReadTitleBlock(fullPath, 1);
                        if (!string.IsNullOrWhiteSpace(ocr))
                            text = ocr;
                    }

                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        var fromPdf = CustomerProfileDrawingMatcher.ExtractBest(text, profile);
                        if (!string.IsNullOrWhiteSpace(fromPdf))
                            return CustomerProfileDrawingMatcher.NormalizeKey(fromPdf, profile);
                    }
                }
                catch
                {
                }
            }

            return CustomerProfileDrawingMatcher.NormalizeKey(baseName, profile);
        }

        private static string StripRevSuffix(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return value;

            var idx = value.LastIndexOf('_');
            if (idx > 0)
            {
                var suffix = value.Substring(idx + 1).ToUpperInvariant();
                if (suffix.StartsWith("REV", StringComparison.Ordinal) || suffix.StartsWith("R0", StringComparison.Ordinal)
                    || suffix.StartsWith("R1", StringComparison.Ordinal) || suffix.StartsWith("V", StringComparison.Ordinal))
                    return value.Substring(0, idx);
            }

            return value;
        }
    }
}
