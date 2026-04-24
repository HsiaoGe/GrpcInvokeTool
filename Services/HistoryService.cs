using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace GrpcInvokeTool.Services
{
    public class HistoryService
    {
        private readonly string _historyFilePath;
        private const int MaxDllHistory = 10;
        private const int MaxCallHistory = 20; // 最多保存20条历史调用记录
        private const int MaxServerHistory = 10; // 最多保存10条服务器地址记录

        public HistoryService()
        {
            var appPath = AppDomain.CurrentDomain.BaseDirectory;
            _historyFilePath = Path.Combine(appPath, "history.json");
        }

        public HistoryData Load()
        {
            try
            {
                if (File.Exists(_historyFilePath))
                {
                    var json = File.ReadAllText(_historyFilePath);
                    return JsonSerializer.Deserialize<HistoryData>(json) ?? new HistoryData();
                }
            }
            catch
            {
                // Ignore errors, return defaults
            }
            return new HistoryData();
        }

        public void Save(HistoryData data)
        {
            try
            {
                var options = new JsonSerializerOptions { WriteIndented = true };
                var json = JsonSerializer.Serialize(data, options);
                File.WriteAllText(_historyFilePath, json);
            }
            catch
            {
                // Ignore save errors
            }
        }

        public void AddDllPath(HistoryData data, string dllPath)
        {
            if (string.IsNullOrWhiteSpace(dllPath)) return;

            // Remove if already exists
            data.DllPaths.RemoveAll(p => string.Equals(p, dllPath, StringComparison.OrdinalIgnoreCase));

            // Add to front
            data.DllPaths.Insert(0, dllPath);

            // Trim to max
            if (data.DllPaths.Count > MaxDllHistory)
            {
                data.DllPaths = data.DllPaths.Take(MaxDllHistory).ToList();
            }

            Save(data);
        }

        public void AddServerAddress(HistoryData data, string address)
        {
            if (string.IsNullOrWhiteSpace(address)) return;
            
            // 更新当前服务器地址
            data.ServerAddress = address;
            
            // 添加到历史列表（去重并移到最前面）
            data.ServerAddresses.RemoveAll(s => string.Equals(s, address, StringComparison.OrdinalIgnoreCase));
            data.ServerAddresses.Insert(0, address);
            
            // 限制最大数量
            if (data.ServerAddresses.Count > MaxServerHistory)
            {
                data.ServerAddresses = data.ServerAddresses.Take(MaxServerHistory).ToList();
            }
            
            Save(data);
        }

        public void AddCallHistory(HistoryData data, CallHistoryItem item)
        {
            // Create key for deduplication: 每个接口只保存一份历史记录
            // Key = ServiceName + MethodName + DllPath (不包括ServerAddress)
            var key = $"{item.ServiceName}/{item.MethodName}|{item.DllPath}";

            // Remove existing entry with same key (覆盖旧的记录)
            data.CallHistory.RemoveAll(h =>
                $"{h.ServiceName}/{h.MethodName}|{h.DllPath}" == key);

            // Add to front (最新的记录在最前面)
            data.CallHistory.Insert(0, item);

            // Trim to max
            if (data.CallHistory.Count > MaxCallHistory)
            {
                data.CallHistory = data.CallHistory.Take(MaxCallHistory).ToList();
            }

            Save(data);
        }

        public CallHistoryItem? FindCallHistory(HistoryData data, string serviceName, string methodName, string dllPath)
        {
            // 查找历史记录时也使用相同的key（不包括ServerAddress）
            return data.CallHistory.FirstOrDefault(h =>
                h.ServiceName == serviceName &&
                h.MethodName == methodName &&
                string.Equals(h.DllPath, dllPath, StringComparison.OrdinalIgnoreCase));
        }
    }

    public class HistoryData
    {
        public List<string> DllPaths { get; set; } = new();
        public string ServerAddress { get; set; } = "127.0.0.1:50051";
        public List<string> ServerAddresses { get; set; } = new() { "127.0.0.1:50051" };
        public List<CallHistoryItem> CallHistory { get; set; } = new();
    }

    public class CallHistoryItem
    {
        public string DllPath { get; set; } = string.Empty;
        public string ServerAddress { get; set; } = string.Empty;
        public string ServiceName { get; set; } = string.Empty;
        public string MethodName { get; set; } = string.Empty;
        public string RequestJson { get; set; } = string.Empty;
        public long Timestamp { get; set; }
    }
}
