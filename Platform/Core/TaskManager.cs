using System.Collections.Concurrent;
using PDF_umbenenen.Platform.Models;

namespace PDF_umbenenen.Platform.Core
{
    internal sealed class TaskManager : Contracts.ITaskManager
    {
        private readonly ConcurrentDictionary<string, PlatformJob> _jobs =
            new ConcurrentDictionary<string, PlatformJob>();

        public string Enqueue(PlatformJob job)
        {
            if (job == null)
                return string.Empty;

            _jobs[job.JobId] = job;
            return job.JobId;
        }

        public bool TryGetProgress(string jobId, out PlatformJob job)
        {
            if (string.IsNullOrWhiteSpace(jobId))
            {
                job = null;
                return false;
            }

            return _jobs.TryGetValue(jobId, out job);
        }

        public void Cancel(string jobId)
        {
            if (string.IsNullOrWhiteSpace(jobId))
                return;

            if (_jobs.TryGetValue(jobId, out var job))
                job.Status = "cancelled";
        }
    }
}
