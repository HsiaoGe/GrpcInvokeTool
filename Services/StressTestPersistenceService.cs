using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using GrpcInvokeTool.Models;

namespace GrpcInvokeTool.Services
{
    public class StressTestPersistenceService
    {
        private readonly string _basePath;
        private readonly string _groupsFilePath;
        private readonly string _plansFilePath;
        private readonly string _reportsDirectory;

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        public StressTestPersistenceService()
        {
            var appPath = AppDomain.CurrentDomain.BaseDirectory;
            
            _basePath = appPath;
            _groupsFilePath = Path.Combine(appPath, "groups.json");
            _plansFilePath = Path.Combine(appPath, "plans.json");
            _reportsDirectory = Path.Combine(appPath, "stress_test", "reports");
            
            Directory.CreateDirectory(_reportsDirectory);
        }

        #region Groups

        public List<CallGroup> LoadGroups()
        {
            return LoadListFile<CallGroup>(_groupsFilePath);
        }

        public void SaveGroups(List<CallGroup> groups)
        {
            SaveListFile(_groupsFilePath, groups);
        }

        #endregion

        #region Plans

        public List<StressTestPlan> LoadPlans()
        {
            return LoadListFile<StressTestPlan>(_plansFilePath);
        }

        public void SavePlans(List<StressTestPlan> plans)
        {
            SaveListFile(_plansFilePath, plans);
        }

        #endregion

        #region Reports

        public List<StressTestReport> LoadReports()
        {
            var reports = new List<StressTestReport>();
            try
            {
                foreach (var file in Directory.GetFiles(_reportsDirectory, "*.json"))
                {
                    try
                    {
                        var json = File.ReadAllText(file);
                        var report = JsonSerializer.Deserialize<StressTestReport>(json, JsonOptions);
                        if (report != null)
                            reports.Add(report);
                    }
                    catch { /* skip corrupt files */ }
                }
            }
            catch { /* directory not found */ }

            return reports.OrderByDescending(r => r.ExecutedAt).ToList();
        }

        public void SaveReport(StressTestReport report)
        {
            try
            {
                var filePath = Path.Combine(_reportsDirectory, $"{report.Id}.json");
                var json = JsonSerializer.Serialize(report, JsonOptions);
                File.WriteAllText(filePath, json);
            }
            catch { /* ignore save errors */ }
        }

        public void DeleteReport(string reportId)
        {
            try
            {
                var filePath = Path.Combine(_reportsDirectory, $"{reportId}.json");
                if (File.Exists(filePath))
                    File.Delete(filePath);
            }
            catch { /* ignore delete errors */ }
        }

        public void UpdateReport(StressTestReport report)
        {
            SaveReport(report);
        }

        #endregion

        #region Helpers

        private List<T> LoadListFile<T>(string filePath)
        {
            try
            {
                if (File.Exists(filePath))
                {
                    var json = File.ReadAllText(filePath);
                    return JsonSerializer.Deserialize<List<T>>(json, JsonOptions) ?? new List<T>();
                }
            }
            catch { /* ignore errors */ }
            return new List<T>();
        }

        private void SaveListFile<T>(string filePath, List<T> data)
        {
            try
            {
                var json = JsonSerializer.Serialize(data, JsonOptions);
                File.WriteAllText(filePath, json);
            }
            catch { /* ignore save errors */ }
        }

        #endregion
    }
}
