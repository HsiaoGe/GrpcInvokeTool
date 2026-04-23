using System;

namespace GrpcInvokeTool.Models
{
    public class InterfaceCall
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string ServerAddress { get; set; } = string.Empty;
        public string ServiceName { get; set; } = string.Empty;
        public string MethodName { get; set; } = string.Empty;
        public string MethodType { get; set; } = string.Empty; // "Unary" | "ServerStreaming" | "ClientStreaming"
        public string RequestJson { get; set; } = string.Empty;
        public string DllPath { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; } = DateTime.Now;

        public string DisplayText => string.IsNullOrEmpty(DisplayName)
            ? $"{ServiceName}/{MethodName}"
            : DisplayName;

        public string MethodTypeTag => MethodType switch
        {
            "Unary" => "U",
            "ServerStreaming" => "SS",
            "ClientStreaming" => "CS",
            "DuplexStreaming" => "DS",
            _ => "?"
        };
    }
}
