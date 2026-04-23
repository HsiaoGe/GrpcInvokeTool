using System;
using Grpc.Core;

namespace GrpcInvokeTool.Models
{
    public class GrpcMethodInfo
    {
        public string ServiceName { get; set; } = string.Empty;

        public string MethodName { get; set; } = string.Empty;

        public MethodType MethodType { get; set; }

        public Type RequestType { get; set; } = null!;

        public Type ResponseType { get; set; } = null!;

        public object MethodDescriptor { get; set; } = null!;

        public Type ClientType { get; set; } = null!;

        public string FullName => $"{ServiceName}/{MethodName}";

        public string MethodTypeDisplay => MethodType switch
        {
            MethodType.Unary => "Unary",
            MethodType.ServerStreaming => "Server Streaming",
            MethodType.ClientStreaming => "Client Streaming",
            MethodType.DuplexStreaming => "Duplex Streaming",
            _ => MethodType.ToString()
        };

        public string MethodTypeTag => MethodType switch
        {
            MethodType.Unary => "U",
            MethodType.ServerStreaming => "SS",
            MethodType.ClientStreaming => "CS",
            MethodType.DuplexStreaming => "DS",
            _ => "?"
        };

        public bool IsStreaming => MethodType is MethodType.ServerStreaming
            or MethodType.ClientStreaming
            or MethodType.DuplexStreaming;

        public override string ToString() => FullName;
    }
}
