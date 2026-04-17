using IndustrialProcessingSystem.Config;
using IndustrialProcessingSystem.Logging;
using IndustrialProcessingSystem.Models;
using IndustrialProcessingSystem.Processing;

namespace IPS.Tests
{
    public class ProcessingSystemTests
    {
        // ── Helpers ──────────────────────────────────────────────────────────────

        // Creates a JobLogger that writes to a unique temp file per test run,
        // so parallel test runs never step on each other's log files.
        private static JobLogger MakeLogger()
        {
            return new JobLogger(Path.Combine(
                Path.GetTempPath(), "test_" + Guid.NewGuid() + ".log"));
        }

        // Creates a ProcessingSystem with sensible defaults for most tests:
        // 2 worker threads and a queue capacity of 50.
        private static ProcessingSystem MakeSystem(int workers = 2, int maxQueue = 50)
        {
            return new ProcessingSystem(workers, maxQueue, MakeLogger());
        }

        // ── Submit behaviour ─────────────────────────────────────────────────────

        // Verifies that Submit() returns a non-null JobHandle whose Id matches
        // the submitted Job's Id exactly.
        [Fact]
        public void Submit_ReturnsHandleWithMatchingId()
        {
            var sys = MakeSystem();
            var job = new Job { Type = JobType.IO, Payload = "delay:10", Priority = 1 };

            var handle = sys.Submit(job);

            Assert.NotNull(handle);
            Assert.Equal(job.Id, handle.Id);
        }

        // Verifies idempotency: submitting the exact same Job object (same Guid Id)
        // a second time is rejected and returns null, so a job is never executed twice.
        [Fact]
        public void Submit_SameIdTwice_SecondIsRejected()
        {
            var sys = MakeSystem();
            var job = new Job { Type = JobType.IO, Payload = "delay:10", Priority = 1 };

            var h1 = sys.Submit(job);
            var h2 = sys.Submit(job);

            Assert.NotNull(h1);
            Assert.Null(h2);
        }

        // Verifies the MaxQueueSize limit: the system is created with capacity 3 and
        // 0 workers (so jobs are never dequeued). After filling the queue with 3 jobs,
        // a 4th submission must be rejected (null returned).
        [Fact]
        public void Submit_QueueFull_NewJobRejected()
        {
            var sys = new ProcessingSystem(0, 3, MakeLogger());

            for (int i = 0; i < 3; i++)
            {
                var job = new Job { Type = JobType.IO, Payload = "delay:10", Priority = 1 };
                Assert.NotNull(sys.Submit(job));
            }

            var overflow = new Job { Type = JobType.IO, Payload = "delay:10", Priority = 1 };
            Assert.Null(sys.Submit(overflow));
        }

        // ── Priority ordering ────────────────────────────────────────────────────

        // Verifies that GetTopJobs() returns jobs sorted by priority value ascending
        // (lower number = higher priority). Three jobs with priorities 5, 1, and 3
        // are submitted; the returned list must be [priority-1, priority-3, priority-5].
        [Fact]
        public void GetTopJobs_ReturnsInPriorityOrder()
        {
            var sys = new ProcessingSystem(0, 50, MakeLogger());
            var low = new Job { Type = JobType.IO, Payload = "delay:10", Priority = 5 };
            var high = new Job { Type = JobType.IO, Payload = "delay:10", Priority = 1 };
            var mid = new Job { Type = JobType.IO, Payload = "delay:10", Priority = 3 };

            sys.Submit(low);
            sys.Submit(high);
            sys.Submit(mid);

            var top = sys.GetTopJobs(3).ToList();

            Assert.Equal(high.Id, top[0].Id);
            Assert.Equal(mid.Id, top[1].Id);
            Assert.Equal(low.Id, top[2].Id);
        }

        // ── GetJob lookup ────────────────────────────────────────────────────────

        // Verifies that GetJob(Guid) returns the correct Job object for a known Id
        // that was previously submitted to the system.
        [Fact]
        public void GetJob_KnownId_ReturnsJob()
        {
            var sys = new ProcessingSystem(0, 50, MakeLogger());
            var job = new Job { Type = JobType.IO, Payload = "delay:10", Priority = 2 };
            sys.Submit(job);

            var found = sys.GetJob(job.Id);

            Assert.NotNull(found);
            Assert.Equal(job.Id, found.Id);
        }

        // ── Actual execution ─────────────────────────────────────────────────────

        // Verifies end-to-end execution of an IO job:
        // the handle's Task must complete within 5 seconds and the result
        // must be in the valid range [0, 100] as specified for IO jobs.
        [Fact]
        public async Task IOJob_CompletesWithResultInRange()
        {
            var sys = MakeSystem(workers: 2);
            var job = new Job { Type = JobType.IO, Payload = "delay:50", Priority = 1 };
            var handle = sys.Submit(job);

            Assert.NotNull(handle);

            var timeout = Task.Delay(5000);
            var finished = await Task.WhenAny(handle.Result, timeout);

            // If finished == timeout the job took longer than 5 s → test fails
            Assert.Equal(handle.Result, finished);

            int result = await handle.Result;
            Assert.InRange(result, 0, 100);
        }

        // Verifies end-to-end execution of a Prime job:
        // counting primes up to 10 must return exactly 4 (2, 3, 5, 7).
        [Fact]
        public async Task PrimeJob_Returns4PrimesUpTo10()
        {
            var sys = MakeSystem(workers: 2);
            var job = new Job
            {
                Type = JobType.Prime,
                Payload = "numbers:10,threads:1",
                Priority = 1
            };
            var handle = sys.Submit(job);

            Assert.NotNull(handle);

            var timeout = Task.Delay(5000);
            var finished = await Task.WhenAny(handle.Result, timeout);

            Assert.Equal(handle.Result, finished);
            Assert.Equal(4, await handle.Result);
        }

        // ── Event system ─────────────────────────────────────────────────────────

        // Verifies that the JobCompleted event fires after a job finishes and that
        // the event argument contains the correct Job Id.
        // Uses TaskCompletionSource (time-independent) instead of Thread.Sleep.
        [Fact]
        public async Task JobCompleted_EventFiresAfterExecution()
        {
            var sys = MakeSystem(workers: 2);
            var signal = new TaskCompletionSource<Guid>();

            // Subscribe after construction to test external subscription works too
            sys.JobCompleted += (sender, e) => signal.TrySetResult(e.Job.Id);

            var job = new Job { Type = JobType.IO, Payload = "delay:50", Priority = 1 };
            var handle = sys.Submit(job);

            var timeout = Task.Delay(5000);
            var finished = await Task.WhenAny(signal.Task, timeout);

            // If finished == timeout, the event never fired → test fails
            Assert.Equal(signal.Task, finished);
            Assert.Equal(job.Id, await signal.Task);
        }

        // ── Payload parsing ──────────────────────────────────────────────────────

        // Verifies that ParsePrime correctly extracts the number limit and thread count
        // from the payload string, including:
        //   - underscore separators in numbers (10_000 → 10000)
        //   - thread count clamped to [1, 8]: 0 → 1, 99 → 8
        [Theory]
        [InlineData("numbers:10_000,threads:3", 10000, 3)]
        [InlineData("numbers:20_000,threads:2", 20000, 2)]
        [InlineData("numbers:100,threads:0", 100, 1)]   // 0 threads → clamped to 1
        [InlineData("numbers:100,threads:99", 100, 8)]   // 99 threads → clamped to 8
        public void PayloadParser_Prime_ParsesCorrectly(
            string payload, int expectedLimit, int expectedThreads)
        {
            var result = PayloadParser.ParsePrime(payload);
            Assert.Equal(expectedLimit, result.Item1);
            Assert.Equal(expectedThreads, result.Item2);
        }

        // Verifies that ParseIO correctly extracts the delay in milliseconds,
        // including underscore separators (1_000 → 1000, 3_000 → 3000).
        [Theory]
        [InlineData("delay:1_000", 1000)]
        [InlineData("delay:3_000", 3000)]
        [InlineData("delay:500", 500)]
        public void PayloadParser_IO_ParsesCorrectly(string payload, int expectedMs)
        {
            int ms = PayloadParser.ParseIO(payload);
            Assert.Equal(expectedMs, ms);
        }
    }
}