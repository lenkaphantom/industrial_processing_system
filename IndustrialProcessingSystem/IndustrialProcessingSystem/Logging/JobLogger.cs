using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace IndustrialProcessingSystem.Logging
{
    public class JobLogger
    {
        private readonly string _logPath;
        private readonly SemaphoreSlim _mutex = new SemaphoreSlim(1, 1);

        public JobLogger(string logPath = "jobs.log")
        {
            _logPath = logPath;
        }

        public Task LogCompletedAsync(Guid jobId, int result)
        {
            string line = string.Format("[{0:yyyy-MM-dd HH:mm:ss}] [COMPLETED] {1}, {2}",
                DateTime.Now, jobId, result);
            return WriteAsync(line);
        }

        public Task LogFailedAsync(Guid jobId)
        {
            string line = string.Format("[{0:yyyy-MM-dd HH:mm:ss}] [FAILED] {1}, -1",
                DateTime.Now, jobId);
            return WriteAsync(line);
        }

        public Task LogAbortAsync(Guid jobId)
        {
            string line = string.Format("[{0:yyyy-MM-dd HH:mm:ss}] [ABORT] {1}, -1",
                DateTime.Now, jobId);
            return WriteAsync(line);
        }

        private async Task WriteAsync(string line)
        {
            await _mutex.WaitAsync();
            try
            {
                using (var sw = new StreamWriter(_logPath, append: true))
                    await sw.WriteLineAsync(line);
            }
            finally
            {
                _mutex.Release();
            }
        }
    }
}