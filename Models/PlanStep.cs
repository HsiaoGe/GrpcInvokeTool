using System;

namespace GrpcInvokeTool.Models
{
    public class PlanStep
    {
        public string GroupId { get; set; } = string.Empty;
        public string GroupName { get; set; } = string.Empty;
        public int IntervalMs { get; set; } = 0;
        public bool IsRandomInterval { get; set; }
        public int MinIntervalMs { get; set; } = 0;
        public int MaxIntervalMs { get; set; } = 1000;

        public int GetActualInterval()
        {
            if (!IsRandomInterval) return IntervalMs;
            var min = Math.Min(MinIntervalMs, MaxIntervalMs);
            var max = Math.Max(MinIntervalMs, MaxIntervalMs);
            return Random.Shared.Next(min, max + 1);
        }

        public string IntervalDisplayText
        {
            get
            {
                if (IntervalMs == 0 && !IsRandomInterval) return "No delay";
                if (IsRandomInterval) return $"{MinIntervalMs}-{MaxIntervalMs}ms (random)";
                return $"{IntervalMs}ms";
            }
        }
    }
}
