using System;
using System.Collections.ObjectModel;

namespace GrpcInvokeTool.Models
{
    public class CallGroup
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string Name { get; set; } = "New Group";
        public string Description { get; set; } = string.Empty;
        public string ServerAddress { get; set; } = "127.0.0.1:50051"; // 最近使用的服务器地址
        public bool IsParallel { get; set; }
        public ObservableCollection<InterfaceCall> Calls { get; set; } = new();
        public DateTime CreatedAt { get; set; } = DateTime.Now;
        public DateTime UpdatedAt { get; set; } = DateTime.Now;

        public string ParallelText => IsParallel ? "Parallel" : "Serial";
        public string CallCountText => $"{Calls.Count} call(s)";
    }
}
