using System;

namespace IndustrialProcessingSystem.Models
{
    public class Job
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public JobType Type { get; set; }
        public string Payload { get; set; } = string.Empty;
        public int Priority { get; set; }
    }
}
