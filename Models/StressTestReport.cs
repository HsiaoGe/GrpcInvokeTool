using System;
using System.Collections.ObjectModel;
using System.Linq;

namespace GrpcInvokeTool.Models
{
    public class StressTestReport
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string PlanId { get; set; } = string.Empty;
        public string PlanName { get; set; } = string.Empty;
        public string PlanDescription { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; } = DateTime.Now;
        public DateTime ExecutedAt { get; set; } = DateTime.Now;
        public TimeSpan TotalDuration { get; set; }
        public int TotalRequests { get; set; }
        public int SuccessCount { get; set; }
        public int FailureCount { get; set; }
        public string Status { get; set; } = "Running"; // "Running" | "Completed" | "Cancelled" | "Failed"
        public ObservableCollection<CallResultDetail> CallResults { get; set; } = new();

        // Summary statistics
        public double AvgResponseMs { get; set; }
        public double MinResponseMs { get; set; }
        public double MaxResponseMs { get; set; }
        public double P99ResponseMs { get; set; }
        public double ActualRps { get; set; }

        public string StatusDisplay => Status;
        public string DurationDisplay => TotalDuration.TotalSeconds >= 60
            ? $"{(int)TotalDuration.TotalMinutes}m {TotalDuration.Seconds}s"
            : $"{TotalDuration.TotalSeconds:F1}s";
        public string SuccessRateDisplay => TotalRequests > 0
            ? $"{(double)SuccessCount / TotalRequests * 100:F1}%"
            : "N/A";
        public string RpsDisplay => ActualRps > 0 ? $"{ActualRps:F1}" : "N/A";

        public void ComputeStatistics()
        {
            if (CallResults.Count == 0) return;

            var durations = CallResults.Select(r => r.DurationMs).OrderBy(d => d).ToList();
            AvgResponseMs = durations.Average();
            MinResponseMs = durations.First();
            MaxResponseMs = durations.Last();

            var p99Index = (int)Math.Ceiling(durations.Count * 0.99) - 1;
            P99ResponseMs = durations[Math.Max(0, p99Index)];

            if (TotalDuration.TotalSeconds > 0)
                ActualRps = TotalRequests / TotalDuration.TotalSeconds;
        }
    }
}
