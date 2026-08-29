using PDF_umbenenen.Platform.Contracts;
using PDF_umbenenen.Services.Cli;

namespace PDF_umbenenen.Platform.Modules.Cli
{
    internal sealed class TesseractOcrCliModule : IOcrEngine
    {
        public string ProviderId => "tesseract-cli";

        public string RecognizeTitleBlock(string filePath, int pageNumber = 1)
            => Services.TitleBlockOcrService.TryReadTitleBlock(filePath, pageNumber);
    }

    internal sealed class TesseractCliProbeModule
    {
        public static bool IsReady() => TesseractCliService.IsAvailable();

        public static string StatusText()
            => IsReady() ? "Tesseract bereit" : "Tesseract nicht gefunden (Program Files oder PATH)";
    }
}
