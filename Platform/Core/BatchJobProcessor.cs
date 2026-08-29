using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using PDF_umbenenen.Platform.Contracts;
using PDF_umbenenen.Platform.Models;

namespace PDF_umbenenen.Platform.Core
{
    internal sealed class BatchJobProcessor : IBatchJobProcessor
    {
        private readonly IAiOrchestrator _orchestrator;
        private readonly JobScheduler _scheduler;
        private readonly ITaskManager _tasks;
        private readonly IPlatformLogger _logger;
        private readonly IResourceMonitor _resources;
        private readonly ConcurrentDictionary<string, PlatformJob> _jobs =
            new ConcurrentDictionary<string, PlatformJob>(StringComparer.OrdinalIgnoreCase);

        private CancellationTokenSource _cts;
        private Task[] _workers;

        public BatchJobProcessor(
            IAiOrchestrator orchestrator,
            JobScheduler scheduler,
            ITaskManager tasks,
            IPlatformLogger logger,
            IResourceMonitor resources)
        {
            _orchestrator = orchestrator;
            _scheduler = scheduler;
            _tasks = tasks;
            _logger = logger;
            _resources = resources;
        }

        public void Start(int workerCount)
        {
            Stop();
            _cts = new CancellationTokenSource();
            var count = workerCount > 0 ? workerCount : _resources.RecommendedWorkerCount;
            _workers = Enumerable.Range(0, count)
                .Select(i => Task.Run(() => WorkerLoop(_cts.Token), _cts.Token))
                .ToArray();

            _logger.Info("batch", "Background-Processor gestartet", new Dictionary<string, string>
            {
                ["workers"] = count.ToString(),
                ["gpu"] = _resources.PreferGpu.ToString()
            });
        }

        public void Stop()
        {
            try
            {
                _cts?.Cancel();
                _workers?.Where(w => w != null).ToList().ForEach(w =>
                {
                    try { w.Wait(2000); } catch { }
                });
            }
            catch
            {
            }
            finally
            {
                _cts?.Dispose();
                _cts = null;
                _workers = null;
            }
        }

        public string EnqueueBatch(IList<string> filePaths, string jobType = "analyze")
        {
            var batchId = Guid.NewGuid().ToString("N");
            if (filePaths == null)
                return batchId;

            foreach (var path in filePaths.Where(File.Exists))
            {
                var job = new PlatformJob
                {
                    JobId = Guid.NewGuid().ToString("N"),
                    FilePath = path,
                    JobType = jobType,
                    Status = "queued",
                    ProgressPercent = 0
                };

                _jobs[job.JobId] = job;
                _scheduler.Schedule(job, 0);
                _tasks.Enqueue(job);
            }

            _logger.Info("batch", "Batch eingereiht", new Dictionary<string, string>
            {
                ["batchId"] = batchId,
                ["count"] = filePaths.Count.ToString()
            });

            return batchId;
        }

        public bool TryGetJob(string jobId, out PlatformJob job)
            => _jobs.TryGetValue(jobId, out job);

        private async Task WorkerLoop(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                if (!_scheduler.TryTakeNext(out var job))
                {
                    await Task.Delay(200, cancellationToken).ConfigureAwait(false);
                    continue;
                }

                job.Status = "running";
                job.ProgressPercent = 10;
                _scheduler.Update(job);
                _jobs[job.JobId] = job;

                try
                {
                    var analysis = await _orchestrator.AnalyzeDocumentAsync(job.FilePath, cancellationToken)
                        .ConfigureAwait(false);
                    job.ProgressPercent = 60;

                    var rules = await _orchestrator.EvaluateRulesAsync(analysis, cancellationToken)
                        .ConfigureAwait(false);
                    job.ProgressPercent = 80;

                    await _orchestrator.DecideAsync(analysis, rules, cancellationToken).ConfigureAwait(false);
                    job.ProgressPercent = 100;
                    job.Status = "completed";
                }
                catch (OperationCanceledException)
                {
                    job.Status = "cancelled";
                }
                catch (Exception ex)
                {
                    job.Status = "failed";
                    _logger.Error("batch", "Job fehlgeschlagen", ex, new Dictionary<string, string>
                    {
                        ["jobId"] = job.JobId,
                        ["file"] = job.FilePath
                    });
                }

                _scheduler.Update(job);
                _jobs[job.JobId] = job;
            }
        }
    }
}
