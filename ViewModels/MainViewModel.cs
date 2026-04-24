using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Google.Protobuf;
using Google.Protobuf.Reflection;
using Grpc.Core;
using GrpcInvokeTool.Models;
using GrpcInvokeTool.Services;
using Microsoft.Win32;

namespace GrpcInvokeTool.ViewModels
{
    public partial class MainViewModel : ObservableObject
    {
        private readonly DllLoadService _dllLoadService;
        private readonly GrpcDiscoveryService _discoveryService;
        private readonly GrpcInvokeService _invokeService;
        private readonly MessageTemplateService _templateService;
        private readonly MessageEditorService _editorService;
        private readonly HistoryService _historyService;
        private HistoryData _history;
        private string _currentDllPath = string.Empty;

        // Expose services for sharing with StressTestWindow
        public DllLoadService SharedDllLoadService => _dllLoadService;
        public GrpcInvokeService SharedInvokeService => _invokeService;
        public MessageEditorService SharedEditorService => _editorService;

        public MainViewModel()
        {
            _dllLoadService = new DllLoadService();
            _discoveryService = new GrpcDiscoveryService();
            _invokeService = new GrpcInvokeService();
            _templateService = new MessageTemplateService();
            _editorService = new MessageEditorService();
            _historyService = new HistoryService();

            _history = _historyService.Load();

            ServerAddress = _history.ServerAddress;
            ServerAddressHistory = new ObservableCollection<string>(_history.ServerAddresses);
            DllHistory = new ObservableCollection<string>(_history.DllPaths);

            // 自动加载最近使用的DLL（如果文件还存在）
            AutoLoadRecentDll();
        }

        private void AutoLoadRecentDll()
        {
            // 尝试加载最近使用的DLL
            if (_history.DllPaths.Count > 0)
            {
                var recentDllPath = _history.DllPaths[0];
                
                // 检查文件是否存在
                if (System.IO.File.Exists(recentDllPath))
                {
                    try
                    {
                        DllPath = recentDllPath;
                        DoLoadDll();
                        StatusText = $"Auto-loaded: {System.IO.Path.GetFileName(recentDllPath)}";
                    }
                    catch
                    {
                        // 如果自动加载失败，不显示错误，让用户手动加载
                        StatusText = "Ready - Select a DLL to load";
                    }
                }
                else
                {
                    // 文件不存在，从历史记录中移除
                    _history.DllPaths.Remove(recentDllPath);
                    _historyService.Save(_history);
                    DllHistory = new ObservableCollection<string>(_history.DllPaths);
                    StatusText = "Recent DLL not found - Select a DLL to load";
                }
            }
        }

        #region DLL Properties

        [ObservableProperty]
        private string _dllPath = string.Empty;

        [ObservableProperty]
        private ObservableCollection<string> _dllHistory = new();

        [ObservableProperty]
        private string _dllStatus = "No DLL loaded";

        [ObservableProperty]
        private bool _isDllLoaded;

        #endregion

        #region Server Properties

        [ObservableProperty]
        private string _serverAddress = "127.0.0.1:50051";

        [ObservableProperty]
        private ObservableCollection<string> _serverAddressHistory = new();

        partial void OnServerAddressChanged(string value)
        {
            // 不在这里保存服务器地址，只有在调用接口时才保存
        }

        #endregion

        #region Service/Method Properties

        [ObservableProperty]
        private ObservableCollection<GrpcServiceInfo> _services = new();

        [ObservableProperty]
        private GrpcServiceInfo? _selectedService;

        [ObservableProperty]
        private GrpcMethodInfo? _selectedMethod;

        [ObservableProperty]
        private string _methodInfoText = "Select a method to view details";

        #endregion

        #region Request Properties

        [ObservableProperty]
        private MessageFieldNode? _requestRootNode;

        [ObservableProperty]
        private string _requestJson = string.Empty;

        #endregion

        #region Response Properties

        [ObservableProperty]
        private string _responseText = string.Empty;

        [ObservableProperty]
        private ObservableCollection<StreamMessageItem> _streamMessages = new();

        [ObservableProperty]
        private bool _isUnaryResult = true;

        [ObservableProperty]
        private bool _isStreamingResult;

        #endregion

        #region Status Properties

        [ObservableProperty]
        private bool _isCalling;

        [ObservableProperty]
        private string _statusText = "Ready";

        [ObservableProperty]
        private bool _canCall;

        #endregion

        #region Commands

        [RelayCommand]
        private void LoadDll()
        {
            var dialog = new OpenFileDialog
            {
                Filter = "DLL files (*.dll)|*.dll|All files (*.*)|*.*",
                Title = "Select gRPC DLL"
            };

            if (dialog.ShowDialog() == true)
            {
                DllPath = dialog.FileName;
                DoLoadDll();
            }
        }

        private void DoLoadDll()
        {
            if (string.IsNullOrWhiteSpace(DllPath)) return;

            try
            {
                StatusText = "Loading DLL...";
                var assembly = _dllLoadService.LoadDll(DllPath);
                IsDllLoaded = true;
                _currentDllPath = DllPath;
                DllStatus = $"Loaded: {System.IO.Path.GetFileName(DllPath)}";

                _historyService.AddDllPath(_history, DllPath);
                RefreshDllHistory();

                var discoveredServices = _discoveryService.DiscoverServices(assembly);
                Services = new ObservableCollection<GrpcServiceInfo>(discoveredServices);

                StatusText = $"Found {discoveredServices.Count} services, {discoveredServices.Sum(s => s.Methods.Count)} methods";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to load DLL:\n{ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                StatusText = "Load failed";
                IsDllLoaded = false;
                DllStatus = "Load failed";
            }
        }

        private void RefreshDllHistory()
        {
            DllHistory = new ObservableCollection<string>(_history.DllPaths);
        }

        partial void OnDllPathChanged(string value)
        {
            if (!string.IsNullOrWhiteSpace(value) && value != _currentDllPath && System.IO.File.Exists(value))
            {
                DoLoadDll();
            }
        }

        [RelayCommand]
        private void UnloadDll()
        {
            try
            {
                _invokeService.CancelStreaming();
                _invokeService.Reset();
                _dllLoadService.UnloadDll();

                IsDllLoaded = false;
                DllStatus = "No DLL loaded";
                Services.Clear();
                SelectedService = null;
                SelectedMethod = null;
                RequestRootNode = null;
                RequestJson = string.Empty;
                ResponseText = string.Empty;
                StreamMessages.Clear();
                _currentDllPath = string.Empty;
                StatusText = "DLL unloaded";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to unload DLL:\n{ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        partial void OnSelectedMethodChanged(GrpcMethodInfo? value)
        {
            if (value == null)
            {
                MethodInfoText = "Select a method to view details";
                RequestRootNode = null;
                RequestJson = string.Empty;
                CanCall = false;
                return;
            }

            MethodInfoText = $"Service: {value.ServiceName}\nMethod: {value.MethodName}\n" +
                           $"Type: {value.MethodTypeDisplay}\n" +
                           $"Request: {value.RequestType.Name}\nResponse: {value.ResponseType.Name}";
            CanCall = true;

            // Try to load from history first
            if (!string.IsNullOrEmpty(_currentDllPath))
            {
                var historyItem = _historyService.FindCallHistory(_history, value.ServiceName, value.MethodName, _currentDllPath);
                if (historyItem != null)
                {
                    if (!string.IsNullOrWhiteSpace(historyItem.ServerAddress))
                    {
                        ServerAddress = historyItem.ServerAddress;
                    }
                    // Build editor from history JSON
                    var msg = (IMessage)Activator.CreateInstance(value.RequestType)!;
                    RequestRootNode = _editorService.BuildEditorTreeFromJson(msg.Descriptor, historyItem.RequestJson);
                    RequestJson = historyItem.RequestJson;
                    return;
                }
            }

            // Build full editor tree with defaults
            BuildEditorTree();
        }

        [RelayCommand]
        private void GenerateTemplate()
        {
            if (SelectedMethod == null) return;

            try
            {
                var msg = (IMessage)Activator.CreateInstance(SelectedMethod.RequestType)!;
                RequestRootNode = _editorService.BuildFullEditorTree(msg.Descriptor);
                RequestJson = _editorService.SerializeToJson(RequestRootNode);
            }
            catch (Exception ex)
            {
                StatusText = $"Template error: {ex.Message}";
            }
        }

        private void BuildEditorTree()
        {
            if (SelectedMethod == null)
            {
                RequestRootNode = null;
                return;
            }

            try
            {
                var msg = (IMessage)Activator.CreateInstance(SelectedMethod.RequestType)!;
                RequestRootNode = _editorService.BuildFullEditorTree(msg.Descriptor);
                RequestJson = _editorService.SerializeToJson(RequestRootNode);
            }
            catch
            {
                RequestRootNode = null;
            }
        }

        /// <summary>
        /// Called by the View when user adds a field via "+" button.
        /// </summary>
        [RelayCommand]
        private void AddField(MessageFieldNode parent)
        {
            if (parent == null) return;

            // Show a popup menu handled by the View via code-behind
            // This command just signals the View; the actual field selection is done via ContextMenu
            // The AddFieldCommand parameter binding is in XAML
        }

        /// <summary>
        /// Add a specific field to a parent node (called from code-behind after field selection).
        /// </summary>
        public void AddFieldToParent(MessageFieldNode parent, FieldDescriptor field)
        {
            if (parent == null || field == null) return;

            // Handle oneof
            if (field.ContainingOneof != null)
            {
                // Check if oneof group already exists
                var existingOneof = parent.Children.FirstOrDefault(c =>
                    c.IsOneofGroup && c.OneofDescriptor == field.ContainingOneof);

                if (existingOneof != null)
                {
                    // Select this option in the existing oneof group
                    var option = existingOneof.OneofOptions.FirstOrDefault(o => o.FieldName == field.Name);
                    if (option != null)
                    {
                        existingOneof.SelectedOneofField = option;
                        existingOneof.Children.Clear();
                        _editorService.AddChildWithDefaults(existingOneof, option);
                    }
                    return;
                }

                // Create new oneof group
                var oneofNode = _editorService.CreateOneofGroupNode(field.ContainingOneof);
                var selectedOption = oneofNode.OneofOptions.FirstOrDefault(o => o.FieldName == field.Name);
                if (selectedOption != null)
                {
                    oneofNode.SelectedOneofField = selectedOption;
                    _editorService.AddChildWithDefaults(oneofNode, selectedOption);
                }
                parent.Children.Add(oneofNode);
                oneofNode.Parent = parent;
            }
            else
            {
                var node = _editorService.CreateFieldNode(field);
                _editorService.SetDefaultValue(node);
                parent.Children.Add(node);
                node.Parent = parent;
            }

            SyncJsonFromTree();
        }

        /// <summary>
        /// Add a repeated item to a repeated field.
        /// </summary>
        public void AddRepeatedItem(MessageFieldNode repeatedNode)
        {
            if (repeatedNode?.FieldDescriptor == null || !repeatedNode.IsRepeated) return;

            var index = repeatedNode.Children.Count;
            var item = _editorService.CreateRepeatedItemNode(repeatedNode.FieldDescriptor, index);
            _editorService.SetDefaultValue(item);

            // If it's a message type, populate all sub-fields
            if (item.IsMessage && item.MessageDescriptor != null)
            {
                var processedOneofs = new HashSet<OneofDescriptor>();
                foreach (var field in item.MessageDescriptor.Fields.InFieldNumberOrder())
                {
                    if (field.ContainingOneof != null)
                    {
                        if (processedOneofs.Contains(field.ContainingOneof)) continue;
                        processedOneofs.Add(field.ContainingOneof);
                        var oneofNode = _editorService.CreateOneofGroupNode(field.ContainingOneof);
                        if (oneofNode.OneofOptions.Count > 0)
                        {
                            oneofNode.SelectedOneofField = oneofNode.OneofOptions[0];
                            _editorService.AddChildWithDefaults(oneofNode, oneofNode.OneofOptions[0]);
                        }
                        item.Children.Add(oneofNode);
                        oneofNode.Parent = item;
                    }
                    else
                    {
                        var subField = _editorService.CreateFieldNode(field);
                        _editorService.SetDefaultValue(subField);
                        item.Children.Add(subField);
                        subField.Parent = item;
                    }
                }
            }

            repeatedNode.Children.Add(item);
            item.Parent = repeatedNode;
            SyncJsonFromTree();
        }

        /// <summary>
        /// Add a map entry to a map field.
        /// </summary>
        public void AddMapEntry(MessageFieldNode mapNode)
        {
            if (mapNode?.FieldDescriptor == null || !mapNode.IsMap) return;

            var index = mapNode.Children.Count;
            var entry = _editorService.CreateMapEntryNode(mapNode.FieldDescriptor, "", index);

            // If value is message type without oneof, populate sub-fields
            var valueNode = entry.Children.FirstOrDefault(c => c.FieldName == "value");
            if (valueNode != null && valueNode.IsMessage && valueNode.MessageDescriptor != null)
            {
                // Only populate if value doesn't already have children (oneof cases are handled in CreateMapEntryNode)
                if (valueNode.Children.Count == 0)
                {
                    foreach (var field in valueNode.MessageDescriptor.Fields.InFieldNumberOrder())
                    {
                        var subField = _editorService.CreateFieldNode(field);
                        _editorService.SetDefaultValue(subField);
                        valueNode.Children.Add(subField);
                        subField.Parent = valueNode;
                    }
                }
            }

            mapNode.Children.Add(entry);
            entry.Parent = mapNode;
            SyncJsonFromTree();
        }

        /// <summary>
        /// Delete a field node from its parent.
        /// </summary>
        [RelayCommand]
        private void DeleteField(MessageFieldNode node)
        {
            if (node == null || node.Parent == null) return;

            node.Parent.Children.Remove(node);
            SyncJsonFromTree();
        }

        /// <summary>
        /// Handle oneof selection change.
        /// </summary>
        public void SelectOneofOption(MessageFieldNode oneofGroup, MessageFieldNode selectedOption)
        {
            if (oneofGroup == null || selectedOption == null) return;

            oneofGroup.SelectedOneofField = selectedOption;
            oneofGroup.Children.Clear();
            _editorService.AddChildWithDefaults(oneofGroup, selectedOption);
            SyncJsonFromTree();
        }

        /// <summary>
        /// Sync the JSON from the editor tree. Call this after any edit.
        /// </summary>
        public void SyncJsonFromTree()
        {
            if (RequestRootNode != null)
            {
                try
                {
                    RequestJson = _editorService.SerializeToJson(RequestRootNode);
                }
                catch
                {
                    // Ignore serialization errors during editing
                }
            }
        }

        [RelayCommand]
        private void FormatJson()
        {
            // This is now a no-op since the tree editor manages JSON
            // Keep for compatibility
        }

        [RelayCommand]
        private async Task CallAsync()
        {
            if (SelectedMethod == null) return;

            // Sync latest JSON from tree
            SyncJsonFromTree();

            // Parse request
            IMessage? request = null;
            try
            {
                if (SelectedMethod.MethodType != MethodType.ClientStreaming &&
                    SelectedMethod.MethodType != MethodType.DuplexStreaming)
                {
                    request = _editorService.ParseMessage(RequestJson, SelectedMethod.RequestType);
                    if (request == null)
                    {
                        MessageBox.Show("Failed to parse request. Please check the field values.", "Parse Error",
                            MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to parse request:\n{ex.Message}", "Parse Error",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            IsCalling = true;
            StreamMessages.Clear();
            ResponseText = string.Empty;

            try
            {
                switch (SelectedMethod.MethodType)
                {
                    case MethodType.Unary:
                        IsUnaryResult = true;
                        IsStreamingResult = false;
                        StatusText = "Calling (Unary)...";
                        var result = await _invokeService.InvokeUnaryAsync(SelectedMethod, request!, ServerAddress);
                        ResponseText = result;
                        StatusText = "Call completed";
                        break;

                    case MethodType.ServerStreaming:
                        IsUnaryResult = false;
                        IsStreamingResult = true;
                        StatusText = "Calling (Server Streaming)...";
                        var ct = _invokeService.GetStreamingCancellationToken();
                        await foreach (var item in _invokeService.InvokeServerStreamingAsync(SelectedMethod, request!, ServerAddress, ct))
                        {
                            StreamMessages.Add(item);
                        }
                        StatusText = "Stream completed";
                        break;

                    case MethodType.ClientStreaming:
                        IsUnaryResult = false;
                        IsStreamingResult = true;
                        StatusText = "Calling (Client Streaming)...";
                        var requests = ParseMultipleRequests(RequestJson, SelectedMethod.RequestType);
                        var csCt = _invokeService.GetStreamingCancellationToken();
                        await foreach (var item in _invokeService.InvokeClientStreamingAsync(SelectedMethod, requests, ServerAddress, csCt))
                        {
                            StreamMessages.Add(item);
                        }
                        StatusText = "Stream completed";
                        break;

                    case MethodType.DuplexStreaming:
                        IsUnaryResult = false;
                        IsStreamingResult = true;
                        StatusText = "Calling (Duplex Streaming)...";
                        var dupRequests = ParseMultipleRequests(RequestJson, SelectedMethod.RequestType);
                        var dupCt = _invokeService.GetStreamingCancellationToken();
                        await foreach (var item in _invokeService.InvokeDuplexStreamingAsync(SelectedMethod, dupRequests, ServerAddress, dupCt))
                        {
                            StreamMessages.Add(item);
                        }
                        StatusText = "Stream completed";
                        break;
                }
            }
            catch (OperationCanceledException)
            {
                StatusText = "Call cancelled";
            }
            catch (RpcException ex)
            {
                var errorMsg = $"gRPC Error: {ex.StatusCode}\n{ex.Message}";
                if (IsUnaryResult)
                {
                    ResponseText = errorMsg;
                }
                else
                {
                    StreamMessages.Add(new StreamMessageItem
                    {
                        Index = StreamMessages.Count + 1,
                        Content = errorMsg,
                        IsError = true
                    });
                }
                StatusText = $"Call failed: {ex.StatusCode}";
            }
            catch (Exception ex)
            {
                var errorMsg = $"Error: {ex.Message}";
                if (IsUnaryResult)
                {
                    ResponseText = errorMsg;
                }
                else
                {
                    StreamMessages.Add(new StreamMessageItem
                    {
                        Index = StreamMessages.Count + 1,
                        Content = errorMsg,
                        IsError = true
                    });
                }
                StatusText = "Call failed";
            }
            finally
            {
                IsCalling = false;
                // 无论成功或失败都保存历史记录
                SaveCallHistory();
                // 无论成功或失败都保存服务器地址到历史记录
                if (!string.IsNullOrWhiteSpace(ServerAddress))
                {
                    _historyService.AddServerAddress(_history, ServerAddress);
                    ServerAddressHistory = new ObservableCollection<string>(_history.ServerAddresses);
                }
            }
        }

        private void SaveCallHistory()
        {
            if (SelectedMethod == null || string.IsNullOrEmpty(_currentDllPath)) return;

            var item = new CallHistoryItem
            {
                DllPath = _currentDllPath,
                ServerAddress = ServerAddress,
                ServiceName = SelectedMethod.ServiceName,
                MethodName = SelectedMethod.MethodName,
                RequestJson = RequestJson,
                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            };

            _historyService.AddCallHistory(_history, item);
        }

        [RelayCommand]
        private void CancelCall()
        {
            _invokeService.CancelStreaming();
            StatusText = "Cancelling...";
        }

        [RelayCommand]
        private void ClearResponse()
        {
            ResponseText = string.Empty;
            StreamMessages.Clear();
        }

        #endregion

        #region Stress Test Commands

        [RelayCommand]
        private void OpenStressTest()
        {
            var stressTestService = new StressTestService(_invokeService, _editorService);
            var persistenceService = new StressTestPersistenceService();
            var vm = new StressTestViewModel(stressTestService, persistenceService, _invokeService, _dllLoadService);

            var window = new StressTestWindow(vm)
            {
                Owner = Application.Current.MainWindow
            };
            window.Show();
        }

        [RelayCommand]
        private void AddToGroup()
        {
            if (SelectedMethod == null)
            {
                MessageBox.Show("Please select a method first.", "Info", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            // Sync latest JSON from tree
            SyncJsonFromTree();

            var call = new InterfaceCall
            {
                ServiceName = SelectedMethod.ServiceName,
                MethodName = SelectedMethod.MethodName,
                MethodType = SelectedMethod.MethodType.ToString(),
                RequestJson = RequestJson,
                DllPath = _currentDllPath,
                DisplayName = $"{SelectedMethod.ServiceName}/{SelectedMethod.MethodName}",
            };

            // Load existing groups and show selection dialog
            var persistenceService = new StressTestPersistenceService();
            var existingGroups = new ObservableCollection<CallGroup>(persistenceService.LoadGroups());

            var dialog = new SelectGroupDialog(existingGroups)
            {
                Owner = Application.Current.MainWindow
            };

            if (dialog.ShowDialog() != true) return;

            // Add call to selected or new group directly (without opening stress test window)
            CallGroup? targetGroup = null;
            if (dialog.CreateNewGroup)
            {
                var groups = persistenceService.LoadGroups();
                targetGroup = new CallGroup { Name = $"Group {groups.Count + 1}" };
                targetGroup.Calls.Add(call);
                targetGroup.UpdatedAt = DateTime.Now;
                persistenceService.SaveGroup(targetGroup);
            }
            else if (dialog.SelectedGroup != null)
            {
                var groups = persistenceService.LoadGroups();
                targetGroup = groups.FirstOrDefault(g => g.Id == dialog.SelectedGroup.Id);
                if (targetGroup == null)
                {
                    targetGroup = new CallGroup { Name = dialog.SelectedGroup.Name, Id = dialog.SelectedGroup.Id };
                }
                targetGroup.Calls.Add(call);
                targetGroup.UpdatedAt = DateTime.Now;
                persistenceService.SaveGroup(targetGroup);
            }
        }

        #endregion

        #region Helper Methods

        private IEnumerable<IMessage> ParseMultipleRequests(string json, Type requestType)
        {
            var messages = new List<IMessage>();

            try
            {
                var doc = System.Text.Json.JsonDocument.Parse(json);
                if (doc.RootElement.ValueKind == System.Text.Json.JsonValueKind.Array)
                {
                    foreach (var element in doc.RootElement.EnumerateArray())
                    {
                        var itemJson = element.GetRawText();
                        var msg = _editorService.ParseMessage(itemJson, requestType);
                        if (msg != null)
                            messages.Add(msg);
                    }
                }
                else
                {
                    var msg = _editorService.ParseMessage(json, requestType);
                    if (msg != null)
                        messages.Add(msg);
                }
            }
            catch
            {
                var msg = _editorService.ParseMessage(json, requestType);
                if (msg != null)
                    messages.Add(msg);
            }

            return messages;
        }

        #endregion
    }
}
