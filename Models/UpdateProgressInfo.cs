namespace PDF_umbenenen.Models
{
    internal sealed class UpdateProgressInfo
    {
        public UpdateProgressInfo(int percent, string message, long bytesRead = 0, long totalBytes = 0)
        {
            Percent = percent < 0 ? 0 : percent > 100 ? 100 : percent;
            Message = message ?? string.Empty;
            BytesRead = bytesRead < 0 ? 0 : bytesRead;
            TotalBytes = totalBytes < 0 ? 0 : totalBytes;
        }

        public int Percent { get; }

        public string Message { get; }

        public long BytesRead { get; }

        public long TotalBytes { get; }
    }
}
