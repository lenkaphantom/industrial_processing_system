using IndustrialProcessingSystem.Logging;
using IndustrialProcessingSystem.Models;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace IndustrialProcessingSystem.Processing
{
    public class ProcessingSystem
    {
        public event EventHandler<JobEventArgs> JobCompleted;
        public event EventHandler<JobEventArgs> JobFailed;

        private readonly SimplePriorityQueue<Tuple<Job, TaskCompletionSource<int>>> _queue
            = new SimplePriorityQueue<Tuple<Job, TaskCompletionSource<int>>>();

        private readonly Dictionary<Guid, Job> _jobMap = new Dictionary<Guid, Job>();
        private readonly HashSet<Guid> _processedIds = new HashSet<Guid>();

        private readonly ConcurrentBag<JobResult> _completedResults
            = new ConcurrentBag<JobResult>();

        private readonly SemaphoreSlim _queueSignal = new SemaphoreSlim(0);

        private readonly object _lock = new object();

        private readonly int _maxQueueSize;
        private readonly JobLogger _logger;

        private const int TimeoutSeconds = 2;
        private const int MaxAttempts = 3;

        public ProcessingSystem(int workerCount, int maxQueueSize, JobLogger logger)
        {
            _maxQueueSize = maxQueueSize;
            _logger = logger;

            JobCompleted += async (sender, e) =>
                await _logger.LogCompletedAsync(e.Job.Id, e.Result);

            JobFailed += async (sender, e) =>
                await _logger.LogFailedAsync(e.Job.Id);

            for (int i = 0; i < workerCount; i++)
                Task.Run(() => WorkerLoopAsync());

            Task.Run(() => ReportLoopAsync());
        }

        public JobHandle Submit(Job job)
        {
            lock (_lock)
            {
                // Idempotency: reject duplicate IDs
                if (_processedIds.Contains(job.Id))
                    return null;

                // Reject when queue is at capacity
                if (_queue.Count >= _maxQueueSize)
                    return null;

                _processedIds.Add(job.Id);
                _jobMap[job.Id] = job;

                var tcs = new TaskCompletionSource<int>(
                    TaskCreationOptions.RunContinuationsAsynchronously);

                _queue.Enqueue(Tuple.Create(job, tcs), job.Priority);

                // Signal one waiting worker that there is work available
                _queueSignal.Release();

                return new JobHandle { Id = job.Id, Result = tcs.Task };
            }
        }

        private async Task WorkerLoopAsync()
        {
            while (true)
            {
                // Block (asynchronously) until a job is available in the queue
                await _queueSignal.WaitAsync();

                Tuple<Job, TaskCompletionSource<int>> item = null;
                lock (_lock)
                {
                    int priority;
                    _queue.TryDequeue(out item, out priority);
                }

                if (item != null)
                    await ExecuteWithRetryAsync(item.Item1, item.Item2);
            }
        }

        private async Task ExecuteWithRetryAsync(Job job, TaskCompletionSource<int> tcs)
        {
            var stopwatch = Stopwatch.StartNew();

            for (int attempt = 0; attempt < MaxAttempts; attempt++)
            {
                using (var cts = new CancellationTokenSource())
                {
                    var executeTask = JobExecutor.ExecuteAsync(job, cts.Token);
                    var timeoutTask = Task.Delay(TimeSpan.FromSeconds(TimeoutSeconds));

                    var winner = await Task.WhenAny(executeTask, timeoutTask);

                    if (winner == executeTask && !executeTask.IsFaulted && !executeTask.IsCanceled)
                    {
                        int result = await executeTask;

                        stopwatch.Stop();

                        _completedResults.Add(new JobResult
                        {
                            JobId = job.Id,
                            Type = job.Type,
                            Failed = false,
                            ExecutionMs = stopwatch.ElapsedMilliseconds,
                            CompletedAt = DateTime.Now
                        });

                        tcs.SetResult(result);
                        JobCompleted?.Invoke(this, new JobEventArgs(job, result));
                        return;
                    }
                    else
                    {
                        // Cancel the still-running executor task before retrying
                        cts.Cancel();

                        JobFailed?.Invoke(this, new JobEventArgs(job, -1));

                        if (attempt == MaxAttempts - 1)
                        {
                            // All retries exhausted — log ABORT and surface the exception
                            await _logger.LogAbortAsync(job.Id);

                            stopwatch.Stop();
                            _completedResults.Add(new JobResult
                            {
                                JobId = job.Id,
                                Type = job.Type,
                                Failed = true,
                                ExecutionMs = stopwatch.ElapsedMilliseconds,
                                CompletedAt = DateTime.Now
                            });

                            tcs.SetException(new TimeoutException(
                                string.Format("Job {0} aborted after {1} attempts.",
                                    job.Id, MaxAttempts)));
                        }
                    }
                }
            }
        }

        // Returns the first N jobs from the queue ordered by priority (lowest number = highest priority)
        public IEnumerable<Job> GetTopJobs(int n)
        {
            lock (_lock)
            {
                return _queue.PeekAll()
                    .Take(n)
                    .Select(t => t.Item1)
                    .ToList();
            }
        }

        // Looks up a job by ID (returns null if not found)
        public Job GetJob(Guid id)
        {
            lock (_lock)
            {
                Job job;
                _jobMap.TryGetValue(id, out job);
                return job;
            }
        }

        public IReadOnlyCollection<JobResult> GetResultSnapshot()
        {
            return _completedResults.ToArray();
        }

        // Writes an XML report every minute, cycling through 10 slots (slot 0-9).
        // After the 10th report, slot 0 is overwritten (circular / oldest-first).
        private async Task ReportLoopAsync()
        {
            int index = 0;
            while (true)
            {
                await Task.Delay(TimeSpan.FromMinutes(1));
                ReportGenerator.Write(GetResultSnapshot(), index % 10);
                index++;
            }
        }
    }
}