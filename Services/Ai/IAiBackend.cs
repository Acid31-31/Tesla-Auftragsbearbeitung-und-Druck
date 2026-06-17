using System.Threading;
using System.Threading.Tasks;

namespace PDF_umbenenen.Services.Ai
{
    internal interface IAiBackend
    {
        string Name { get; }

        Task<bool> CheckAvailabilityAsync(CancellationToken cancellationToken);

        Task<string> CompleteAsync(string prompt, bool jsonFormat, CancellationToken cancellationToken);

        Task<string> ChatAsync(string systemPrompt, string userPrompt, CancellationToken cancellationToken);
    }
}
