using System;

namespace IndustrialProcessingSystem.Models
{
    public class JobResult
    {
        public Guid JobId { get; set; }
        public JobType Type { get; set; }

        public bool Failed { get; set; }

        public long ExecutionMs { get; set; }

        public DateTime CompletedAt { get; set; }
    }
}
