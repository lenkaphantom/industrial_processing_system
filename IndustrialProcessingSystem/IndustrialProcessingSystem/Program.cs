using IndustrialProcessingSystem.Config;
using IndustrialProcessingSystem.Logging;
using IndustrialProcessingSystem.Models;
using IndustrialProcessingSystem.Processing;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace IndustrialProcessingSystem
{
    public class Program
    {
        static void Main(string[] args)
        {
            RunAsync().GetAwaiter().GetResult();
        }

        static async Task RunAsync()
        {
            SystemConfig config;
            try
            {
                config = SystemConfig.Load("SystemConfig.xml");
                Console.WriteLine(string.Format(
                    "[INFO] Config loaded. Workers: {0}, MaxQueue: {1}",
                    config.WorkerCount, config.MaxQueueSize));
            }
            catch (Exception ex)
            {
                Console.WriteLine("[FATAL] Could not load SystemConfig.xml: " + ex.Message);
                Console.WriteLine("Make sure SystemConfig.xml is set to 'Copy to Output Directory'.");
                return;
            }

            var logger = new JobLogger("jobs.log");
            var system = new ProcessingSystem(config.WorkerCount, config.MaxQueueSize, logger);

            foreach (var job in config.GetInitialJobs())
            {
                var handle = system.Submit(job);
                if (handle == null)
                    Console.WriteLine("[WARN] Initial job rejected (queue full or duplicate).");
                else
                    Console.WriteLine(string.Format(
                        "[INIT] Submitted {0} job (priority {1})", job.Type, job.Priority));
            }

            var producerThreads = new List<Thread>();

            for (int i = 0; i < config.WorkerCount; i++)
            {
                int idx = i;
                var threadRng = new Random(Environment.TickCount ^ (idx * 9973));

                var thread = new Thread(() =>
                {
                    while (true)
                    {
                        try
                        {
                            var job = CreateRandomJob(threadRng);
                            var handle = system.Submit(job);

                            if (handle == null)
                            {
                                Thread.Sleep(50);
                                continue;
                            }

                            Console.WriteLine(string.Format(
                                "[Producer-{0}] Submitted {1} job {2} (priority {3})",
                                idx, job.Type, job.Id, job.Priority));

                            int capturedIdx = idx;
                            Task.Run(async () =>
                            {
                                try
                                {
                                    int result = await handle.Result;
                                    Console.WriteLine(string.Format(
                                        "[Result] Job {0} → {1}", handle.Id, result));
                                }
                                catch (Exception ex)
                                {
                                    Console.WriteLine(string.Format(
                                        "[Aborted] Job {0}: {1}", handle.Id, ex.Message));
                                }
                            });

                            Thread.Sleep(threadRng.Next(100, 500));
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine(string.Format(
                                "[Producer-{0}] Error: {1}", idx, ex.Message));
                        }
                    }
                });

                thread.IsBackground = true;
                thread.Name = "Producer-" + idx;
                producerThreads.Add(thread);
            }

            foreach (var t in producerThreads)
                t.Start();

            Console.WriteLine("[INFO] System running. Press Enter to exit.");
            Console.ReadLine();
        }

        static Job CreateRandomJob(Random rng)
        {
            bool isPrime = rng.Next(2) == 0;

            string payload = isPrime
                ? string.Format("numbers:{0},threads:{1}",
                    rng.Next(1000, 50000), rng.Next(1, 5))
                : string.Format("delay:{0}", rng.Next(200, 4000));

            return new Job
            {
                Id = Guid.NewGuid(),
                Type = isPrime ? JobType.Prime : JobType.IO,
                Payload = payload,
                Priority = rng.Next(1, 6)
            };
        }
    }
}
