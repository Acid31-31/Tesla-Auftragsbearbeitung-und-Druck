namespace PDF_umbenenen.Models
{
    internal sealed class UpdateProgressInfo
    {
        public UpdateProgressInfo(int percent, string message)
        {
            Percent = percent < 0 ? 0 : percent > 100 ? 100 : percent;
            Message = message ?? string.Empty;
        }

        public int Percent { get; }

        public string Message { get; }
    }
}
