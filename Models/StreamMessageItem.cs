using System;

namespace GrpcInvokeTool.Models
{
    public class StreamMessageItem
    {
        public int Index { get; set; }

        public DateTime Timestamp { get; set; } = DateTime.Now;

        public string Content { get; set; } = string.Empty;

        public bool IsError { get; set; }

        public bool IsReceived { get; set; } // true = received from server, false = sent to server

        public string TimeDisplay => Timestamp.ToString("HH:mm:ss.fff");

        public string IndexDisplay => IsReceived ? $"←[{Index}]" : $"→[{Index}]";
    }
}
