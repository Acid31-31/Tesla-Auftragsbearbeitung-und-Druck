using PDF_umbenenen.Services.Cli;

namespace PDF_umbenenen.Services
{
    internal static class MaterialExtractionDiagnostics
    {
        public static string GetMissingDependencyHint()
        {
            var tesseractMissing = !TesseractCliService.IsAvailable();
            var rasterMissing = !PdfRasterLayoutService.IsAvailable;

            if (!tesseractMissing && !rasterMissing)
                return string.Empty;

            if (tesseractMissing && rasterMissing)
                return " [Tesseract + pdfium fehlen — OCR/Raster eingeschränkt]";

            if (tesseractMissing)
                return " [Tesseract fehlt — KI-Einstellungen → Installieren]";

            var status = PdfRasterLayoutService.GetStatus();
            if (status.IndexOf("32-Bit", System.StringComparison.OrdinalIgnoreCase) >= 0
                || status.IndexOf("64-Bit", System.StringComparison.OrdinalIgnoreCase) >= 0
                || status.IndexOf("passt nicht", System.StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return " [pdfium passt nicht — Update R36 oder Neuinstallation]";
            }

            return " [pdfium fehlt — App neu installieren]";
        }
    }
}
