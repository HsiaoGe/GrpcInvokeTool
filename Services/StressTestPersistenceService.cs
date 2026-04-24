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
        private readonly string _groupsDirectory;
        private readonly string _plansDirectory;
        private readonly string _reportsDirectory;
        private readonly string _oldGroupsFilePath;  // 用于迁移
        private readonly string _oldPlansFilePath;   // 用于迁移

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        public StressTestPersistenceService()
        {
            var appPath = AppDomain.CurrentDomain.BaseDirectory;
            
            // 所有stress test相关文件都保存在stress_test目录下
            _basePath = Path.Combine(appPath, "stress_test");
            _groupsDirectory = Path.Combine(_basePath, "groups");
            _plansDirectory = Path.Combine(_basePath, "plans");
            _reportsDirectory = Path.Combine(_basePath, "reports");
            
            // 旧文件路径（用于迁移）
            _oldGroupsFilePath = Path.Combine(_basePath, "groups.json");
            _oldPlansFilePath = Path.Combine(_basePath, "plans.json");
            
            // 确保目录存在
            Directory.CreateDirectory(_basePath);
            Directory.CreateDirectory(_groupsDirectory);
            Directory.CreateDirectory(_plansDirectory);
            Directory.CreateDirectory(_reportsDirectory);
            
            // 迁移旧文件（如果存在）
            MigrateOldFiles(appPath);
        }
        
        private void MigrateOldFiles(string oldBasePath)
        {
            try
            {
                // 迁移根目录下的groups.json
                var oldRootGroupsPath = Path.Combine(oldBasePath, "groups.json");
                if (File.Exists(oldRootGroupsPath))
                {
                    MigrateGroupsFile(oldRootGroupsPath);
                    File.Delete(oldRootGroupsPath);
                }
                
                // 迁移根目录下的plans.json
                var oldRootPlansPath = Path.Combine(oldBasePath, "plans.json");
                if (File.Exists(oldRootPlansPath))
                {
                    MigratePlansFile(oldRootPlansPath);
                    File.Delete(oldRootPlansPath);
                }
                
                // 迁移stress_test目录下的groups.json（旧的单文件格式）
                if (File.Exists(_oldGroupsFilePath))
                {
                    MigrateGroupsFile(_oldGroupsFilePath);
                    File.Delete(_oldGroupsFilePath);
                }
                
                // 迁移stress_test目录下的plans.json（旧的单文件格式）
                if (File.Exists(_oldPlansFilePath))
                {
                    MigratePlansFile(_oldPlansFilePath);
                    File.Delete(_oldPlansFilePath);
                }
            }
            catch
            {
                // 忽略迁移错误（可能文件正在被使用）
            }
        }
        
        private void MigrateGroupsFile(string filePath)
        {
            try
            {
                var json = File.ReadAllText(filePath);
                var groups = JsonSerializer.Deserialize<List<CallGroup>>(json, JsonOptions);
                if (groups != null)
                {
                    foreach (var group in groups)
                    {
                        SaveGroup(group);
                    }
                }
            }
            catch
            {
                // 忽略解析错误
            }
        }
        
        private void MigratePlansFile(string filePath)
        {
            try
            {
                var json = File.ReadAllText(filePath);
                var plans = JsonSerializer.Deserialize<List<StressTestPlan>>(json, JsonOptions);
                if (plans != null)
                {
                    foreach (var plan in plans)
                    {
                        SavePlan(plan);
                    }
                }
            }
            catch
            {
                // 忽略解析错误
            }
        }

        #region Groups

        public List<CallGroup> LoadGroups()
        {
            var groups = new List<CallGroup>();
            try
            {
                foreach (var file in Directory.GetFiles(_groupsDirectory, "*.json"))
                {
                    try
                    {
                        var json = File.ReadAllText(file);
                        var group = JsonSerializer.Deserialize<CallGroup>(json, JsonOptions);
                        if (group != null)
                            groups.Add(group);
                    }
                    catch { /* skip corrupt files */ }
                }
            }
            catch { /* directory not found */ }

            return groups.OrderByDescending(g => g.UpdatedAt).ToList();
        }

        public void SaveGroup(CallGroup group)
        {
            try
            {
                var filePath = Path.Combine(_groupsDirectory, $"{group.Id}.json");
                var json = JsonSerializer.Serialize(group, JsonOptions);
                File.WriteAllText(filePath, json);
            }
            catch { /* ignore save errors */ }
        }

        public void DeleteGroup(string groupId)
        {
            try
            {
                var filePath = Path.Combine(_groupsDirectory, $"{groupId}.json");
                if (File.Exists(filePath))
                    File.Delete(filePath);
            }
            catch { /* ignore delete errors */ }
        }

        #endregion

        #region Plans

        public List<StressTestPlan> LoadPlans()
        {
            var plans = new List<StressTestPlan>();
            try
            {
                foreach (var file in Directory.GetFiles(_plansDirectory, "*.json"))
                {
                    try
                    {
                        var json = File.ReadAllText(file);
                        var plan = JsonSerializer.Deserialize<StressTestPlan>(json, JsonOptions);
                        if (plan != null)
                            plans.Add(plan);
                    }
                    catch { /* skip corrupt files */ }
                }
            }
            catch { /* directory not found */ }

            return plans.OrderByDescending(p => p.UpdatedAt).ToList();
        }

        public void SavePlan(StressTestPlan plan)
        {
            try
            {
                var filePath = Path.Combine(_plansDirectory, $"{plan.Id}.json");
                var json = JsonSerializer.Serialize(plan, JsonOptions);
                File.WriteAllText(filePath, json);
            }
            catch { /* ignore save errors */ }
        }

        public void DeletePlan(string planId)
        {
            try
            {
                var filePath = Path.Combine(_plansDirectory, $"{planId}.json");
                if (File.Exists(filePath))
                    File.Delete(filePath);
            }
            catch { /* ignore delete errors */ }
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

        // 保留这些方法用于迁移旧的列表文件格式
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

        #endregion
    }
}
