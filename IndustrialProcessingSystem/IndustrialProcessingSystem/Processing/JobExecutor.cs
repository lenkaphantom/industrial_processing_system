using IndustrialProcessingSystem.Config;
using IndustrialProcessingSystem.Models;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace IndustrialProcessingSystem.Processing
{
    public static class JobExecutor
    {
        [ThreadStatic]
        private static Random _rng;

        private static Random GetRandom()
        {
            if (_rng == null)
                _rng = new Random(Environment.TickCount ^ Thread.CurrentThread.ManagedThreadId);
            return _rng;
        }

        // FIX: accept a CancellationToken so that when a timeout occurs in
        //      ExecuteWithRetryAsync, the running task can actually be stopped
        //      instead of leaking silently in the background.
        public static Task<int> ExecuteAsync(Job job, CancellationToken ct = default(CancellationToken))
        {
            switch (job.Type)
            {
                case JobType.Prime:
                    return ExecutePrimeAsync(job.Payload, ct);
                case JobType.IO:
                    return ExecuteIOAsync(job.Payload, ct);
                default:
                    throw new ArgumentOutOfRangeException("job.Type",
                        "Unknown job type: " + job.Type);
            }
        }

        private static Task<int> ExecutePrimeAsync(string payload, CancellationToken ct)
        {
            var parsed = PayloadParser.ParsePrime(payload);
            int limit = parsed.Item1;
            int threadCount = parsed.Item2;

            return Task.Run(() =>
            {
                int count = 0;

                // FIX: pass the CancellationToken to ParallelOptions so Parallel.For
                //      stops as soon as the token is cancelled (on timeout/retry).
                Parallel.For(2, limit + 1,
                    new ParallelOptions
                    {
                        MaxDegreeOfParallelism = threadCount,
                        CancellationToken = ct
                    },
                    i =>
                    {
                        if (IsPrime(i))
                            Interlocked.Increment(ref count);
                    });

                return count;
            }, ct);
        }

        private static bool IsPrime(int n)
        {
            if (n < 2) return false;
            if (n == 2) return true;
            if (n % 2 == 0) return false;
            for (int i = 3; i * i <= n; i += 2)
                if (n % i == 0) return false;
            return true;
        }

        // FIX: spec requires Thread.Sleep (not Task.Delay).
        //      We sleep in small 50 ms chunks so the CancellationToken is
        //      checked regularly and the task stops promptly on timeout.
        private static Task<int> ExecuteIOAsync(string payload, CancellationToken ct)
        {
            int delayMs = PayloadParser.ParseIO(payload);

            return Task.Run(() =>
            {
                int elapsed = 0;
                while (elapsed < delayMs)
                {
                    ct.ThrowIfCancellationRequested();
                    int chunk = Math.Min(50, delayMs - elapsed);
                    Thread.Sleep(chunk);
                    elapsed += chunk;
                }
                return GetRandom().Next(0, 101);
            }, ct);
        }
    }
}