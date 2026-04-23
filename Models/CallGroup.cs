using System;
using System.Collections.ObjectModel;

namespace GrpcInvokeTool.Models
{
    public class CallGroup
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string Name { get; set; } = "New Group";
        public string Description { get; set; } = string.Empty;
        public bool IsParallel { get; set; }
        public ObservableCollection<InterfaceCall> Calls { get; set; } = new();
        public DateTime CreatedAt { get; set; } = DateTime.Now;
        public DateTime UpdatedAt { get; set; } = DateTime.Now;

        public string ParallelText => IsParallel ? "Parallel" : "Serial";
        public string CallCountText => $"{Calls.Count} call(s)";
    }
}
