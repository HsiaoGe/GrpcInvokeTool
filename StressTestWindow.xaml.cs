using System;
using System.Windows;
using System.Windows.Controls;
using GrpcInvokeTool.Models;
using GrpcInvokeTool.ViewModels;

namespace GrpcInvokeTool
{
    public partial class StressTestWindow : Window
    {
        private StressTestViewModel ViewModel => (StressTestViewModel)DataContext;

        public StressTestWindow(StressTestViewModel viewModel)
        {
            InitializeComponent();
            DataContext = viewModel;
            NavTabs.SelectionChanged += NavTabs_SelectionChanged;
            UpdatePanelVisibility();
        }

        private void NavTabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ViewModel != null)
            {
                ViewModel.SelectedTab = NavTabs.SelectedIndex;
            }
            UpdatePanelVisibility();
        }

        private void UpdatePanelVisibility()
        {
            if (ViewModel == null) return;

            // Sync NavTabs.SelectedIndex with ViewModel.SelectedTab
        if (NavTabs.SelectedIndex != ViewModel.SelectedTab)
        {
            NavTabs.SelectedIndex = ViewModel.SelectedTab;
        }

        var tab = ViewModel.SelectedTab;

            // Left panels
            GroupsPanel.Visibility = tab == 0 ? Visibility.Visible : Visibility.Collapsed;
            PlansPanel.Visibility = tab == 1 ? Visibility.Visible : Visibility.Collapsed;
            ReportsPanel.Visibility = tab == 2 ? Visibility.Visible : Visibility.Collapsed;

            // Right detail panels
            GroupDetailPanel.Visibility = tab == 0 && ViewModel.HasSelectedGroup
                ? Visibility.Visible : Visibility.Collapsed;
            PlanDetailPanel.Visibility = tab == 1 && ViewModel.HasSelectedPlan
                ? Visibility.Visible : Visibility.Collapsed;
            ReportDetailPanel.Visibility = tab == 2 && ViewModel.HasSelectedReport
                ? Visibility.Visible : Visibility.Collapsed;

            // Empty state
            EmptyStatePanel.Visibility =
                (tab == 0 && !ViewModel.HasSelectedGroup) ||
                (tab == 1 && !ViewModel.HasSelectedPlan) ||
                (tab == 2 && !ViewModel.HasSelectedReport)
                ? Visibility.Visible : Visibility.Collapsed;
        }

        protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
        {
            base.OnPropertyChanged(e);
            if (e.Property == DataContextProperty)
            {
                if (DataContext is StressTestViewModel vm)
                {
                    vm.PropertyChanged += (s, args) =>
                    {
                        if (args.PropertyName is "HasSelectedGroup" or "HasSelectedPlan" or "HasSelectedReport" or "SelectedTab")
                        {
                            UpdatePanelVisibility();
                        }
                    };
                }
            }
        }

        #region Call List Button Handlers

        private void MoveCallUp_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is InterfaceCall call)
                ViewModel.MoveCallUpCommand.Execute(call);
        }

        private void MoveCallDown_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is InterfaceCall call)
                ViewModel.MoveCallDownCommand.Execute(call);
        }

        private void RemoveCall_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is InterfaceCall call)
                ViewModel.RemoveCallFromGroupCommand.Execute(call);
        }

        #endregion

        #region Name Change Handlers

        private void GroupName_TextChanged(object sender, TextChangedEventArgs e)
        {
            ViewModel?.SaveCurrentGroup();
        }

        private void ParallelCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            ViewModel?.SaveCurrentGroup();
        }

        private void PlanName_TextChanged(object sender, TextChangedEventArgs e)
        {
            ViewModel?.SaveCurrentPlan();
        }

        #endregion

        #region Step List Button Handlers

        private void MoveStepUp_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is PlanStep step)
                ViewModel.MoveStepUpCommand.Execute(step);
        }

        private void MoveStepDown_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is PlanStep step)
                ViewModel.MoveStepDownCommand.Execute(step);
        }

        private void RemoveStep_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is PlanStep step)
                ViewModel.RemoveStepFromPlanCommand.Execute(step);
        }

        #endregion

        #region Double Click Handlers

        private void CallsList_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            // Get the clicked item
            var listView = sender as ListView;
            if (listView == null) return;

            var call = listView.SelectedItem as InterfaceCall;
            if (call == null) return;

            // Show detail dialog
            var title = $"Call Details: {call.DisplayText}";
            var infoContent = $"Service: {call.ServiceName}\nMethod: {call.MethodName}\nType: {call.MethodType}";
            var jsonContent = string.IsNullOrWhiteSpace(call.RequestJson) ? "(no request data)" : call.RequestJson;

            var dialog = new DetailDialog(title, infoContent, jsonContent)
            {
                Owner = this
            };
            dialog.ShowDialog();
        }

        private void CallResults_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            // Get the clicked item
            var listView = sender as ListView;
            if (listView == null) return;

            var result = listView.SelectedItem as CallResultDetail;
            if (result == null) return;

            // Show detail dialog
            var title = $"Result Details: {result.MethodDisplay}";
            var infoContent = $"Server: {result.ServerAddress}\nService: {result.ServiceName}\nMethod: {result.MethodName}\nStatus: {result.StatusText}\nDuration: {result.DurationDisplay}\nTime: {result.StartTime:HH:mm:ss}";

            var jsonContent = result.IsSuccess
                ? (string.IsNullOrWhiteSpace(result.ResponseJson) ? "(no response)" : result.ResponseJson)
                : $"Error: {result.ErrorMessage}\nStatusCode: {result.StatusCode}";

            var dialog = new DetailDialog(title, infoContent, jsonContent)
            {
                Owner = this
            };
            dialog.ShowDialog();
        }

        #endregion
    }
}