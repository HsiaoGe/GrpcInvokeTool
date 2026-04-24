using System;
using System.Collections.ObjectModel;
using System.Linq;

namespace GrpcInvokeTool.Models
{
    public class StressTestReport
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string PlanId { get; set; } = string.Empty;
        public string GroupId { get; set; } = string.Empty; // 用于记录从Group执行的报告
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
        public string DurationDisplay
        {
            get
            {
                var totalSeconds = TotalDuration.TotalSeconds;
                if (totalSeconds < 1)
                {
                    // 小于1秒时使用毫秒
                    return $"{(int)TotalDuration.TotalMilliseconds}ms";
                }
                else if (totalSeconds < 60)
                {
                    // 小于1分钟时，只显示秒（保留三位小数）
                    return $"{totalSeconds:F3}s";
                }
                else
                {
                    // 大于1分钟时，显示分钟和秒（秒保留三位小数）
                    var minutes = (int)TotalDuration.TotalMinutes;
                    var seconds = TotalDuration.Seconds + TotalDuration.Milliseconds / 1000.0;
                    return $"{minutes}m{seconds:F3}s";
                }
            }
        }
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
