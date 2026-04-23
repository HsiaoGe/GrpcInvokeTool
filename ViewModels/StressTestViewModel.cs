using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GrpcInvokeTool.Models;
using GrpcInvokeTool.Services;

namespace GrpcInvokeTool.ViewModels
{
    public partial class StressTestViewModel : ObservableObject
    {
        private readonly StressTestService _stressTestService;
        private readonly StressTestPersistenceService _persistenceService;
        private readonly GrpcInvokeService _invokeService;
        private readonly DllLoadService _dllLoadService;

        private CancellationTokenSource? _executionCts;

        public StressTestViewModel(
            StressTestService stressTestService,
            StressTestPersistenceService persistenceService,
            GrpcInvokeService invokeService,
            DllLoadService dllLoadService)
        {
            _stressTestService = stressTestService;
            _persistenceService = persistenceService;
            _invokeService = invokeService;
            _dllLoadService = dllLoadService;

            LoadData();
        }

        #region Data Properties

        [ObservableProperty]
        private ObservableCollection<CallGroup> _groups = new();

        [ObservableProperty]
        private ObservableCollection<StressTestPlan> _plans = new();

        [ObservableProperty]
        private ObservableCollection<StressTestReport> _reports = new();

        [ObservableProperty]
        private int _selectedTab; // 0=Groups, 1=Plans, 2=Reports

        partial void OnSelectedTabChanged(int value)
        {
            // Auto-select first item when switching tabs
            if (value == 0 && SelectedGroup == null && Groups.Count > 0)
            {
                SelectedGroup = Groups[0];
            }
            else if (value == 1 && SelectedPlan == null && Plans.Count > 0)
            {
                SelectedPlan = Plans[0];
            }
            else if (value == 2 && SelectedReport == null && Reports.Count > 0)
            {
                SelectedReport = Reports[0];
            }
        }

        #endregion

        #region Group Properties

        [ObservableProperty]
        private CallGroup? _selectedGroup;

        partial void OnSelectedGroupChanged(CallGroup? value)
        {
            OnPropertyChanged(nameof(HasSelectedGroup));
            OnPropertyChanged(nameof(CanExecuteGroup));
        }

        public bool HasSelectedGroup => SelectedGroup != null;
        public bool CanExecuteGroup => SelectedGroup != null && SelectedGroup.Calls.Count > 0 && !IsExecuting;

        #endregion

        #region Plan Properties

        [ObservableProperty]
        private StressTestPlan? _selectedPlan;

        partial void OnSelectedPlanChanged(StressTestPlan? value)
        {
            OnPropertyChanged(nameof(HasSelectedPlan));
            OnPropertyChanged(nameof(CanExecutePlan));
        }

        public bool HasSelectedPlan => SelectedPlan != null;
        public bool CanExecutePlan => SelectedPlan != null && SelectedPlan.Steps.Count > 0 && !IsExecuting;

        #endregion

        #region Report Properties

        [ObservableProperty]
        private StressTestReport? _selectedReport;

        partial void OnSelectedReportChanged(StressTestReport? value)
        {
            OnPropertyChanged(nameof(HasSelectedReport));
        }

        public bool HasSelectedReport => SelectedReport != null;

        #endregion

        #region Execution Properties

        [ObservableProperty]
        private bool _isExecuting;

        [ObservableProperty]
        private string _executionStatus = "Ready";

        [ObservableProperty]
        private int _completedRequests;

        [ObservableProperty]
        private int _totalRequests;

        [ObservableProperty]
        private string _currentStepText = string.Empty;

        [ObservableProperty]
        private double _progressPercentage;

        #endregion

        #region Load Data

        private void LoadData()
        {
            var groups = _persistenceService.LoadGroups();
            Groups = new ObservableCollection<CallGroup>(groups);

            var plans = _persistenceService.LoadPlans();
            Plans = new ObservableCollection<StressTestPlan>(plans);

            var reports = _persistenceService.LoadReports();
            Reports = new ObservableCollection<StressTestReport>(reports);

            // Select first items on initial load
            if (Groups.Count > 0) SelectedGroup = Groups[0];
            if (Plans.Count > 0) SelectedPlan = Plans[0];
            if (Reports.Count > 0) SelectedReport = Reports[0];
        }

        #endregion

        #region Group Commands

        [RelayCommand]
        private void CreateGroup()
        {
            var group = new CallGroup { Name = $"Group {Groups.Count + 1}" };
            Groups.Add(group);
            SaveGroups();
            SelectedGroup = group;
        }

        [RelayCommand]
        private void DeleteGroup()
        {
            if (SelectedGroup == null) return;
            Groups.Remove(SelectedGroup);
            SaveGroups();
            SelectedGroup = Groups.FirstOrDefault();
        }

        [RelayCommand]
        private void RenameGroup()
        {
            if (SelectedGroup == null) return;
            SelectedGroup.UpdatedAt = DateTime.Now;
            SaveGroups();
        }

        [RelayCommand]
        private void ToggleGroupParallel()
        {
            if (SelectedGroup == null) return;
            SelectedGroup.IsParallel = !SelectedGroup.IsParallel;
            SelectedGroup.UpdatedAt = DateTime.Now;
            OnPropertyChanged(nameof(SelectedGroup));
            SaveGroups();
        }

        [RelayCommand]
        private void RemoveCallFromGroup(InterfaceCall call)
        {
            if (SelectedGroup == null || call == null) return;
            SelectedGroup.Calls.Remove(call);
            SelectedGroup.UpdatedAt = DateTime.Now;
            SaveGroups();
        }

        [RelayCommand]
        private void MoveCallUp(InterfaceCall call)
        {
            if (SelectedGroup == null || call == null) return;
            var idx = SelectedGroup.Calls.IndexOf(call);
            if (idx <= 0) return;
            SelectedGroup.Calls.Move(idx, idx - 1);
            SelectedGroup.UpdatedAt = DateTime.Now;
            SaveGroups();
        }

        [RelayCommand]
        private void MoveCallDown(InterfaceCall call)
        {
            if (SelectedGroup == null || call == null) return;
            var idx = SelectedGroup.Calls.IndexOf(call);
            if (idx < 0 || idx >= SelectedGroup.Calls.Count - 1) return;
            SelectedGroup.Calls.Move(idx, idx + 1);
            SelectedGroup.UpdatedAt = DateTime.Now;
            SaveGroups();
        }

        /// <summary>
        /// Add a call from the main interface to a group.
        /// </summary>
        public void AddCallToGroup(InterfaceCall call, string? targetGroupId = null)
        {
            if (call == null) return;

            CallGroup? targetGroup;
            if (targetGroupId != null)
            {
                targetGroup = Groups.FirstOrDefault(g => g.Id == targetGroupId);
            }
            else
            {
                targetGroup = SelectedGroup;
            }

            if (targetGroup == null)
            {
                // Create a new group if none selected
                targetGroup = new CallGroup { Name = $"Group {Groups.Count + 1}" };
                Groups.Add(targetGroup);
            }

            targetGroup.Calls.Add(call);
            targetGroup.UpdatedAt = DateTime.Now;
            SaveGroups();
            SelectedGroup = targetGroup;
        }

        [RelayCommand]
        private async Task ExecuteGroupAsync()
        {
            if (SelectedGroup == null || IsExecuting) return;
            if (_dllLoadService.LoadedAssembly == null)
            {
                MessageBox.Show("Please load a DLL first.", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var ct = StartExecution();
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                var results = await _stressTestService.ExecuteGroupAsync(
                    SelectedGroup, _dllLoadService.LoadedAssembly, ct);

                stopwatch.Stop();

                var report = new StressTestReport
                {
                    PlanName = $"Group: {SelectedGroup.Name}",
                    Status = "Completed",
                    ExecutedAt = DateTime.Now,
                    TotalDuration = stopwatch.Elapsed,
                    TotalRequests = results.Count,
                    SuccessCount = results.Count(r => r.IsSuccess),
                    FailureCount = results.Count(r => !r.IsSuccess),
                };
                foreach (var r in results)
                {
                    r.StepName = SelectedGroup.Name;
                    report.CallResults.Add(r);
                }
                report.ComputeStatistics();

                _persistenceService.SaveReport(report);
                Reports.Insert(0, report);
                SelectedReport = report;
                SelectedTab = 2; // Switch to reports tab
            }
            catch (OperationCanceledException)
            {
                ExecutionStatus = "Cancelled";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Execution failed: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                StopExecution();
            }
        }

        #endregion

        #region Plan Commands

        [RelayCommand]
        private void CreatePlan()
        {
            var plan = new StressTestPlan { Name = $"Plan {Plans.Count + 1}" };
            Plans.Add(plan);
            SavePlans();
            SelectedPlan = plan;
        }

        [RelayCommand]
        private void DeletePlan()
        {
            if (SelectedPlan == null) return;
            Plans.Remove(SelectedPlan);
            SavePlans();
            SelectedPlan = Plans.FirstOrDefault();
        }

        [RelayCommand]
        private void SavePlan()
        {
            if (SelectedPlan == null) return;
            SelectedPlan.UpdatedAt = DateTime.Now;
            SavePlans();
        }

        [RelayCommand]
        private void AddStepToPlan()
        {
            if (SelectedPlan == null) return;

            // Show available groups - pick the first one if only one group exists
            if (Groups.Count == 0)
            {
                MessageBox.Show("Please create a group first.", "Info", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var group = Groups.FirstOrDefault();
            if (group == null) return;

            var step = new PlanStep
            {
                GroupId = group.Id,
                GroupName = group.Name,
            };
            SelectedPlan.Steps.Add(step);
            SelectedPlan.UpdatedAt = DateTime.Now;
            SavePlans();
        }

        [RelayCommand]
        private void RemoveStepFromPlan(PlanStep step)
        {
            if (SelectedPlan == null || step == null) return;
            SelectedPlan.Steps.Remove(step);
            SelectedPlan.UpdatedAt = DateTime.Now;
            SavePlans();
        }

        [RelayCommand]
        private void MoveStepUp(PlanStep step)
        {
            if (SelectedPlan == null || step == null) return;
            var idx = SelectedPlan.Steps.IndexOf(step);
            if (idx <= 0) return;
            SelectedPlan.Steps.Move(idx, idx - 1);
            SelectedPlan.UpdatedAt = DateTime.Now;
            SavePlans();
        }

        [RelayCommand]
        private void MoveStepDown(PlanStep step)
        {
            if (SelectedPlan == null || step == null) return;
            var idx = SelectedPlan.Steps.IndexOf(step);
            if (idx < 0 || idx >= SelectedPlan.Steps.Count - 1) return;
            SelectedPlan.Steps.Move(idx, idx + 1);
            SelectedPlan.UpdatedAt = DateTime.Now;
            SavePlans();
        }

        [RelayCommand]
        private async Task ExecutePlanAsync()
        {
            if (SelectedPlan == null || IsExecuting) return;
            if (_dllLoadService.LoadedAssembly == null)
            {
                MessageBox.Show("Please load a DLL first.", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var ct = StartExecution();
            try
            {
                var progress = new Progress<StressTestProgress>(p =>
                {
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        CompletedRequests = p.CompletedRequests;
                        TotalRequests = p.TotalRequests;
                        CurrentStepText = p.CurrentStep;
                        ProgressPercentage = p.TotalRequests > 0 ? (double)p.CompletedRequests / p.TotalRequests * 100 : 0;
                        ExecutionStatus = $"Running - {p.CurrentStep}";
                    });
                });

                var report = await _stressTestService.ExecutePlanAsync(
                    SelectedPlan,
                    Groups.ToList(),
                    _dllLoadService.LoadedAssembly,
                    progress,
                    ct);

                _persistenceService.SaveReport(report);
                Reports.Insert(0, report);
                SelectedReport = report;
                SelectedTab = 2; // Switch to reports tab
            }
            catch (OperationCanceledException)
            {
                ExecutionStatus = "Cancelled";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Execution failed: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                StopExecution();
            }
        }

        #endregion

        #region Report Commands

        [RelayCommand]
        private void DeleteReport()
        {
            if (SelectedReport == null) return;
            _persistenceService.DeleteReport(SelectedReport.Id);
            Reports.Remove(SelectedReport);
            SelectedReport = Reports.FirstOrDefault();
        }

        #endregion

        #region Cancel Command

        [RelayCommand]
        private void CancelExecution()
        {
            _executionCts?.Cancel();
            ExecutionStatus = "Cancelling...";
        }

        #endregion

        #region Helpers

        private CancellationToken StartExecution()
        {
            IsExecuting = true;
            ExecutionStatus = "Running";
            CompletedRequests = 0;
            ProgressPercentage = 0;
            CurrentStepText = string.Empty;
            _executionCts = new CancellationTokenSource();
            return _executionCts.Token;
        }

        private void StopExecution()
        {
            IsExecuting = false;
            if (ExecutionStatus == "Running")
                ExecutionStatus = "Completed";
            _executionCts?.Dispose();
            _executionCts = null;
        }

        public void SaveGroups()
        {
            _persistenceService.SaveGroups(Groups.ToList());
        }

        public void SavePlans()
        {
            _persistenceService.SavePlans(Plans.ToList());
        }

        #endregion
    }
}
