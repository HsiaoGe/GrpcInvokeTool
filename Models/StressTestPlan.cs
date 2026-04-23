using System;
using System.Collections.ObjectModel;

namespace GrpcInvokeTool.Models
{
    public class StressTestPlan
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string Name { get; set; } = "New Plan";
        public string Description { get; set; } = string.Empty;
        public bool IsLoopEnabled { get; set; }
        public int LoopCount { get; set; } = 1; // 0 = infinite
        public ObservableCollection<PlanStep> Steps { get; set; } = new();
        public DateTime CreatedAt { get; set; } = DateTime.Now;
        public DateTime UpdatedAt { get; set; } = DateTime.Now;

        public string LoopText => IsLoopEnabled
            ? (LoopCount == 0 ? "Infinite loop" : $"Loop {LoopCount}x")
            : "Run once";

        public string StepCountText => $"{Steps.Count} step(s)";
    }
}
