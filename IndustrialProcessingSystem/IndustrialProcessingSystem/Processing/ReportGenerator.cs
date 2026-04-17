using IndustrialProcessingSystem.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

namespace IndustrialProcessingSystem.Processing
{
    public static class ReportGenerator
    {
        public static void Write(IReadOnlyCollection<JobResult> results, int slot)
        {
            var groups = results
                .GroupBy(r => r.Type)
                .OrderBy(g => g.Key.ToString())
                .Select(g => new
                {
                    Type = g.Key.ToString(),
                    Completed = g.Count(r => !r.Failed),
                    AvgMs = g.Any(r => !r.Failed)
                                      ? g.Where(r => !r.Failed).Average(r => r.ExecutionMs)
                                      : 0.0,
                    FailedCount = g.Count(r => r.Failed)
                });

            var doc = new XDocument(
                new XDeclaration("1.0", "utf-8", "yes"),
                new XElement("Report",
                    new XAttribute("GeneratedAt", DateTime.Now.ToString("o")),
                    new XAttribute("Slot", slot),
                    groups.Select(g =>
                        new XElement("JobType",
                            new XAttribute("Type", g.Type),
                            new XElement("CompletedCount", g.Completed),
                            new XElement("AvgExecutionMs", Math.Round(g.AvgMs, 2)),
                            new XElement("FailedCount", g.FailedCount)
                        )
                    )
                )
            );

            string path = string.Format("report_{0}.xml", slot);
            doc.Save(path);
        }
    }
}
