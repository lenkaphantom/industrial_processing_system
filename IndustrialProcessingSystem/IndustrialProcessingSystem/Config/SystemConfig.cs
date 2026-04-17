using IndustrialProcessingSystem.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Serialization;

namespace IndustrialProcessingSystem.Config
{
    // ── XML shape ────────────────────────────────────────────────────────────────
    // [XmlRoot]      maps to the root tag:          <SystemConfig>
    // [XmlElement]   maps to a child tag:           <WorkerCount>5</WorkerCount>
    // [XmlArray]     maps to a wrapper list tag:    <Jobs>
    // [XmlArrayItem] maps to each item inside it:   <Job .../>
    // [XmlAttribute] maps to an XML attribute:      Type="Prime"
    // ─────────────────────────────────────────────────────────────────────────────

    [XmlRoot("SystemConfig")]
    public class SystemConfig
    {
        [XmlElement("WorkerCount")]
        public int WorkerCount { get; set; }

        [XmlElement("MaxQueueSize")]
        public int MaxQueueSize { get; set; }

        [XmlArray("Jobs")]
        [XmlArrayItem("Job")]
        public List<JobConfig> Jobs { get; set; } = new List<JobConfig>();

        
        public static SystemConfig Load(string path)
        {
            var serializer = new XmlSerializer(typeof(SystemConfig));
            using (var reader = new StreamReader(path))
            {
                return (SystemConfig)serializer.Deserialize(reader);
            }
        }

        public List<Job> GetInitialJobs()
        {
            return Jobs.Select(cfg => new Job
            {
                Id = Guid.NewGuid(),
                Type = (JobType)Enum.Parse(typeof(JobType), cfg.Type),
                Payload = cfg.Payload,
                Priority = cfg.Priority
            }).ToList();
        }
    }

    public class JobConfig
    {
        [XmlAttribute("Type")]
        public string Type { get; set; } = string.Empty;

        [XmlAttribute("Payload")]
        public string Payload { get; set; } = string.Empty;

        [XmlAttribute("Priority")]
        public int Priority { get; set; }
    }

    public static class PayloadParser
    {
        // "numbers:10_000,threads:3" → (limit: 10000, threadCount: 3)
        public static Tuple<int, int> ParsePrime(string payload)
        {
            string[] parts = payload.Split(',');

            int limit = int.Parse(parts[0].Split(':')[1].Replace("_", ""));
            int threads = int.Parse(parts[1].Split(':')[1].Replace("_", ""));

            threads = Math.Max(1, Math.Min(8, threads));

            return Tuple.Create(limit, threads);
        }

        // "delay:1_000" → 1000
        public static int ParseIO(string payload)
        {
            return int.Parse(payload.Split(':')[1].Replace("_", ""));
        }
    }
}
