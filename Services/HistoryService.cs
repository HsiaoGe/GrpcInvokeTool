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
        private const int MaxCallHistory = 50;

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

        public void UpdateServerAddress(HistoryData data, string address)
        {
            if (string.IsNullOrWhiteSpace(address)) return;
            data.ServerAddress = address;
            Save(data);
        }

        public void AddCallHistory(HistoryData data, CallHistoryItem item)
        {
            // Create key for deduplication
            var key = $"{item.ServiceName}/{item.MethodName}|{item.DllPath}|{item.ServerAddress}";

            // Remove existing entry with same key
            data.CallHistory.RemoveAll(h =>
                $"{h.ServiceName}/{h.MethodName}|{h.DllPath}|{h.ServerAddress}" == key);

            // Add to front
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
