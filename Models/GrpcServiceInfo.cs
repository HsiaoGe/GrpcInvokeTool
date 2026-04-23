using System.Collections.Generic;

namespace GrpcInvokeTool.Models
{
    public class GrpcServiceInfo
    {
        public string ServiceName { get; set; } = string.Empty;

        public string PackageName { get; set; } = string.Empty;

        public List<GrpcMethodInfo> Methods { get; set; } = new();

        public override string ToString() => string.IsNullOrEmpty(PackageName)
            ? ServiceName
            : $"{PackageName}.{ServiceName}";
    }
}
