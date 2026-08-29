using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using PDF_umbenenen.Platform.Contracts;
using PDF_umbenenen.Platform.Models;

namespace PDF_umbenenen.Platform.Core
{
    internal sealed class JobScheduler : IJobScheduler
    {
        private readonly ConcurrentDictionary<string, PlatformJob> _jobs =
            new ConcurrentDictionary<string, PlatformJob>(StringComparer.OrdinalIgnoreCase);

        private readonly ConcurrentDictionary<string, int> _priorities =
            new ConcurrentDictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        public string Schedule(PlatformJob job, int priority = 0)
        {
            if (job == null)
                return string.Empty;

            if (string.IsNullOrWhiteSpace(job.JobId))
                job.JobId = Guid.NewGuid().ToString("N");

            job.Status = "scheduled";
            _jobs[job.JobId] = job;
            _priorities[job.JobId] = priority;
            return job.JobId;
        }

        public void Cancel(string jobId)
        {
            if (string.IsNullOrWhiteSpace(jobId))
                return;

            if (_jobs.TryGetValue(jobId, out var job))
            {
                job.Status = "cancelled";
                _jobs[jobId] = job;
            }
        }

        public IList<PlatformJob> GetPending()
        {
            return _jobs.Values
                .Where(j => j.Status == "scheduled" || j.Status == "queued")
                .OrderByDescending(j => _priorities.TryGetValue(j.JobId, out var p) ? p : 0)
                .ToList();
        }

        internal bool TryTakeNext(out PlatformJob job)
        {
            job = GetPending().FirstOrDefault();
            return job != null;
        }

        internal void Update(PlatformJob job)
        {
            if (job != null && !string.IsNullOrWhiteSpace(job.JobId))
                _jobs[job.JobId] = job;
        }
    }
}
