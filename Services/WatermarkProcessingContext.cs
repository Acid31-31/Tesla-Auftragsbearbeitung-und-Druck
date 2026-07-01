namespace PDF_umbenenen.Services
{
    /// <summary>
    /// Steuert schnellen Stapelmodus beim Sortieren (kein Ollama, kein Lernen pro Datei).
    /// </summary>
    internal static class WatermarkProcessingContext
    {
        [System.ThreadStatic]
        private static bool _batchMode;

        public static bool BatchMode
        {
            get => _batchMode;
            set => _batchMode = value;
        }
    }
}
