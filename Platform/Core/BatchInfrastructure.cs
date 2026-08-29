using System.Collections.Concurrent;
using PDF_umbenenen.Platform.Contracts;
using PDF_umbenenen.Platform.Models;

namespace PDF_umbenenen.Platform.Core
{
    internal sealed class JobQueue : IJobQueue
    {
        private readonly ConcurrentQueue<PlatformJob> _queue = new ConcurrentQueue<PlatformJob>();

        public void Enqueue(PlatformJob job)
        {
            if (job != null)
                _queue.Enqueue(job);
        }

        public bool TryDequeue(out PlatformJob job) => _queue.TryDequeue(out job);
    }

    internal sealed class BackgroundProcessor : IBackgroundProcessor
    {
        public void Start(int workerCount)
        {
            // Erweiterungspunkt für Multi-Thread-Batch ohne feste Modulabhängigkeiten.
        }

        public void Stop()
        {
        }
    }
}
