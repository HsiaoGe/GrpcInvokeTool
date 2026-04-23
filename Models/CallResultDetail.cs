using System;
using System.Linq;

namespace GrpcInvokeTool.Models
{
    public class CallResultDetail
    {
        public int Index { get; set; }
        public string StepName { get; set; } = string.Empty;
        public string ServiceName { get; set; } = string.Empty;
        public string MethodName { get; set; } = string.Empty;
        public string ServerAddress { get; set; } = string.Empty;
        public DateTime StartTime { get; set; } = DateTime.Now;
        public double DurationMs { get; set; }
        public bool IsSuccess { get; set; }
        public string ErrorMessage { get; set; } = string.Empty;
        public string StatusCode { get; set; } = string.Empty;
        public string ResponseJson { get; set; } = string.Empty;

        public string StatusText => IsSuccess ? "OK" : "Failed";
        public string MethodDisplay => $"{ServiceName}/{MethodName}";
        public string DurationDisplay => $"{DurationMs:F1}ms";

        /// <summary>
        /// Summary for display in ListView: error message if failed, truncated response if success
        /// </summary>
        public string Summary
        {
            get
            {
                if (!IsSuccess)
                    return ErrorMessage.Length > 60 ? ErrorMessage.Substring(0, 60) + "..." : ErrorMessage;

                if (string.IsNullOrEmpty(ResponseJson))
                    return "(no response)";

                // Truncate JSON for display (first ~80 chars)
                var trimmed = ResponseJson.Trim();
                if (trimmed.Length > 80)
                    return trimmed.Substring(0, 80) + "...";
                return trimmed;
            }
        }
    }
}
