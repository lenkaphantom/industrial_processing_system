using IndustrialProcessingSystem.Models;
using System;

namespace IndustrialProcessingSystem.Processing
{
    public class JobEventArgs : EventArgs
    {
        public Job Job { get; }
        public int Result { get; }

        public JobEventArgs(Job job, int result)
        {
            Job = job;
            Result = result;
        }
    }
}
