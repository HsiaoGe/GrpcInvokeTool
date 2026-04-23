using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Google.Protobuf;
using Grpc.Core;
using GrpcInvokeTool.Models;

namespace GrpcInvokeTool.Services
{
    public class StressTestProgress
    {
        public int CompletedRequests { get; set; }
        public int TotalRequests { get; set; }
        public string CurrentStep { get; set; } = string.Empty;
        public int CurrentLoop { get; set; }
        public int TotalLoops { get; set; }
        public CallResultDetail? LatestResult { get; set; }
    }

    public class StressTestService
    {
        private sealed class ExecutionContext : IAsyncDisposable
        {
            private readonly GrpcInvokeService _invokeService;
            private readonly Assembly _assembly;
            private readonly Dictionary<string, GrpcMethodInfo?> _methodCache = new(StringComparer.OrdinalIgnoreCase);
            private readonly Dictionary<string, GrpcInvokeService.DuplexStreamingSession> _duplexSessions = new(StringComparer.OrdinalIgnoreCase);

            public ExecutionContext(GrpcInvokeService invokeService, Assembly assembly)
            {
                _invokeService = invokeService;
                _assembly = assembly;
            }

            public GrpcMethodInfo? FindMethodInfo(InterfaceCall call)
            {
                var key = $"{call.ServiceName}/{call.MethodName}";
                if (_methodCache.TryGetValue(key, out var cached))
                    return cached;

                var discoveryService = new GrpcDiscoveryService();
                var services = discoveryService.DiscoverServices(_assembly);

                foreach (var service in services)
                {
                    if (!string.Equals(service.ServiceName, call.ServiceName, StringComparison.OrdinalIgnoreCase))
                        continue;

                    var method = service.Methods.FirstOrDefault(m =>
                        string.Equals(m.MethodName, call.MethodName, StringComparison.OrdinalIgnoreCase));
                    _methodCache[key] = method;
                    return method;
                }

                _methodCache[key] = null;
                return null;
            }

            public async Task<GrpcInvokeService.DuplexStreamingSession> GetOrCreateDuplexSessionAsync(
                GrpcMethodInfo methodInfo,
                string address,
                string reuseScopeKey,
                CancellationToken cancellationToken)
            {
                var sessionKey = $"{reuseScopeKey}|{address}|{methodInfo.ServiceName}|{methodInfo.MethodName}";
                if (_duplexSessions.TryGetValue(sessionKey, out var existing))
                    return existing;

                var created = await _invokeService.OpenDuplexStreamingSessionAsync(methodInfo, address, cancellationToken);
                _duplexSessions[sessionKey] = created;
                return created;
            }

            public async ValueTask DisposeAsync()
            {
                foreach (var session in _duplexSessions.Values)
                {
                    await session.DisposeAsync();
                }
                _duplexSessions.Clear();
            }
        }

        private readonly GrpcInvokeService _invokeService;
        private readonly MessageEditorService _editorService;
        private CancellationTokenSource? _cts;

        public bool IsRunning => _cts != null && !_cts.IsCancellationRequested;

        public StressTestService(GrpcInvokeService invokeService, MessageEditorService editorService)
        {
            _invokeService = invokeService;
            _editorService = editorService;
        }

        public CancellationToken GetCancellationToken()
        {
            _cts?.Cancel();
            _cts = new CancellationTokenSource();
            return _cts.Token;
        }

        public void Cancel()
        {
            _cts?.Cancel();
        }

        public async Task<List<CallResultDetail>> ExecuteGroupAsync(
            CallGroup group,
            Assembly assembly,
            CancellationToken cancellationToken)
        {
            await using var context = new ExecutionContext(_invokeService, assembly);
            return await ExecuteGroupInternalAsync(group, context, cancellationToken, $"group:{group.Id}");
        }

        private async Task<List<CallResultDetail>> ExecuteGroupInternalAsync(
            CallGroup group,
            ExecutionContext context,
            CancellationToken cancellationToken,
            string reuseScopeKey)
        {
            var results = new List<CallResultDetail>();

            if (group.IsParallel)
            {
                var nonDuplexCalls = group.Calls
                    .Select((call, index) => new { call, index })
                    .Where(x => !string.Equals(x.call.MethodType, "DuplexStreaming", StringComparison.OrdinalIgnoreCase))
                    .Select(x => ExecuteCallAsync(x.call, context, x.index, cancellationToken, reuseScopeKey))
                    .ToList();

                if (nonDuplexCalls.Count > 0)
                {
                    var taskResults = await Task.WhenAll(nonDuplexCalls);
                    results.AddRange(taskResults);
                }

                var duplexCalls = group.Calls
                    .Select((call, index) => new { call, index })
                    .Where(x => string.Equals(x.call.MethodType, "DuplexStreaming", StringComparison.OrdinalIgnoreCase))
                    .ToList();

                foreach (var item in duplexCalls)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var result = await ExecuteCallAsync(item.call, context, item.index, cancellationToken, reuseScopeKey);
                    results.Add(result);
                }
            }
            else
            {
                for (int i = 0; i < group.Calls.Count; i++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var result = await ExecuteCallAsync(group.Calls[i], context, i, cancellationToken, reuseScopeKey);
                    results.Add(result);
                }
            }

            return results;
        }

        public async Task<StressTestReport> ExecutePlanAsync(
            StressTestPlan plan,
            IReadOnlyList<CallGroup> groups,
            Assembly assembly,
            IProgress<StressTestProgress>? progress,
            CancellationToken cancellationToken)
        {
            var report = new StressTestReport
            {
                PlanId = plan.Id,
                PlanName = plan.Name,
                PlanDescription = plan.Description,
                Status = "Running",
                ExecutedAt = DateTime.Now,
            };

            var totalLoops = plan.IsLoopEnabled ? plan.LoopCount : 1;
            var isInfinite = totalLoops == 0;
            if (isInfinite) totalLoops = int.MaxValue;

            const int MaxDetailResults = 10000;

            var totalRequestsPerLoop = plan.Steps.Sum(s =>
            {
                var g = groups.FirstOrDefault(gr => gr.Id == s.GroupId);
                return g?.Calls.Count ?? 0;
            });
            var estimatedTotal = isInfinite ? totalRequestsPerLoop * 100 : totalRequestsPerLoop * Math.Min(totalLoops, 100);
            var completedRequests = 0;

            var overallStopwatch = Stopwatch.StartNew();
            var allDurations = new List<double>();
            var successCount = 0;
            var failureCount = 0;

            await using var context = new ExecutionContext(_invokeService, assembly);

            try
            {
                for (int loop = 0; loop < totalLoops; loop++)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    for (int stepIdx = 0; stepIdx < plan.Steps.Count; stepIdx++)
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        var step = plan.Steps[stepIdx];
                        var group = groups.FirstOrDefault(g => g.Id == step.GroupId);
                        if (group == null || group.Calls.Count == 0) continue;

                        progress?.Report(new StressTestProgress
                        {
                            CompletedRequests = completedRequests,
                            TotalRequests = estimatedTotal,
                            CurrentStep = $"Loop {loop + 1} - {group.Name}",
                            CurrentLoop = loop + 1,
                            TotalLoops = plan.LoopCount == 0 ? 0 : totalLoops,
                        });

                        var stepResults = await ExecuteGroupInternalAsync(group, context, cancellationToken, $"plan:{plan.Id}");

                        foreach (var r in stepResults)
                        {
                            allDurations.Add(r.DurationMs);
                            if (r.IsSuccess) successCount++;
                            else failureCount++;

                            if (report.CallResults.Count < MaxDetailResults)
                            {
                                r.StepName = group.Name;
                                report.CallResults.Add(r);
                            }
                        }

                        completedRequests += stepResults.Count;
                        report.TotalRequests = completedRequests;
                        report.SuccessCount = successCount;
                        report.FailureCount = failureCount;

                        if (isInfinite && completedRequests > estimatedTotal)
                        {
                            estimatedTotal = completedRequests + totalRequestsPerLoop * 10;
                        }

                        if (stepIdx < plan.Steps.Count - 1 || loop < totalLoops - 1)
                        {
                            var interval = step.GetActualInterval();
                            if (interval > 0)
                            {
                                await Task.Delay(interval, cancellationToken);
                            }
                        }
                    }
                }

                report.Status = "Completed";
            }
            catch (OperationCanceledException)
            {
                report.Status = "Cancelled";
            }
            catch (Exception)
            {
                report.Status = "Failed";
                report.FailureCount++;
            }

            overallStopwatch.Stop();
            report.TotalDuration = overallStopwatch.Elapsed;
            ComputeIncrementalStatistics(report, allDurations);
            return report;
        }

        private void ComputeIncrementalStatistics(StressTestReport report, List<double> durations)
        {
            if (durations.Count == 0) return;

            report.AvgResponseMs = durations.Average();
            report.MinResponseMs = durations.Min();
            report.MaxResponseMs = durations.Max();

            if (durations.Count > 10000)
            {
                var sampleSize = Math.Max(100, durations.Count / 100);
                var sampled = durations.OrderBy(d => d).Take(sampleSize).ToList();
                var p99SampleIndex = (int)Math.Ceiling(sampleSize * 0.99) - 1;
                report.P99ResponseMs = sampled[Math.Max(0, p99SampleIndex)];
            }
            else
            {
                var sorted = durations.OrderBy(d => d).ToList();
                var p99Index = (int)Math.Ceiling(durations.Count * 0.99) - 1;
                report.P99ResponseMs = sorted[Math.Max(0, p99Index)];
            }

            if (report.TotalDuration.TotalSeconds > 0)
                report.ActualRps = report.TotalRequests / report.TotalDuration.TotalSeconds;
        }

        private async Task<CallResultDetail> ExecuteCallAsync(
            InterfaceCall call,
            ExecutionContext context,
            int index,
            CancellationToken cancellationToken,
            string reuseScopeKey)
        {
            var result = new CallResultDetail
            {
                Index = index,
                ServiceName = call.ServiceName,
                MethodName = call.MethodName,
                ServerAddress = call.ServerAddress,
                StartTime = DateTime.Now,
            };

            var sw = Stopwatch.StartNew();

            try
            {
                cancellationToken.ThrowIfCancellationRequested();

                var methodInfo = context.FindMethodInfo(call);
                if (methodInfo == null)
                {
                    result.IsSuccess = false;
                    result.ErrorMessage = $"Method not found: {call.ServiceName}/{call.MethodName}";
                    result.StatusCode = "NotFound";
                    sw.Stop();
                    result.DurationMs = sw.Elapsed.TotalMilliseconds;
                    return result;
                }

                IMessage? request = null;
                if (!string.IsNullOrWhiteSpace(call.RequestJson))
                {
                    request = _editorService.ParseMessage(call.RequestJson, methodInfo.RequestType);
                }

                if (request == null && methodInfo.MethodType != MethodType.ClientStreaming && methodInfo.MethodType != MethodType.DuplexStreaming)
                {
                    result.IsSuccess = false;
                    result.ErrorMessage = "Failed to parse request JSON";
                    result.StatusCode = "ParseError";
                    sw.Stop();
                    result.DurationMs = sw.Elapsed.TotalMilliseconds;
                    return result;
                }

                switch (methodInfo.MethodType)
                {
                    case MethodType.Unary:
                        var responseJson = await _invokeService.InvokeUnaryAsync(methodInfo, request!, call.ServerAddress);
                        result.IsSuccess = true;
                        result.StatusCode = "OK";
                        result.ResponseJson = responseJson;
                        break;

                    case MethodType.ServerStreaming:
                        var sb = new System.Text.StringBuilder();
                        int ssIndex = 0;
                        await foreach (var item in _invokeService.InvokeServerStreamingAsync(methodInfo, request!, call.ServerAddress, cancellationToken))
                        {
                            ssIndex++;
                            if (ssIndex <= 10) // Collect first 10 responses
                            {
                                sb.AppendLine($"[{ssIndex}] {item.Content}");
                            }
                            if (ssIndex > 100) break;
                        }
                        result.IsSuccess = true;
                        result.StatusCode = "OK";
                        result.ResponseJson = sb.ToString();
                        break;

                    case MethodType.ClientStreaming:
                        result.IsSuccess = false;
                        result.ErrorMessage = "Client streaming not supported in stress test";
                        result.StatusCode = "NotSupported";
                        break;

                    case MethodType.DuplexStreaming:
                        if (request == null)
                        {
                            result.IsSuccess = false;
                            result.ErrorMessage = "Failed to parse request JSON";
                            result.StatusCode = "ParseError";
                            break;
                        }

                        var session = await context.GetOrCreateDuplexSessionAsync(methodInfo, call.ServerAddress, reuseScopeKey, cancellationToken);
                        await session.SendAsync(request, cancellationToken);
                        result.IsSuccess = true;
                        result.StatusCode = "OK";
                        result.ResponseJson = "Message sent via duplex stream";
                        break;

                    default:
                        result.IsSuccess = false;
                        result.ErrorMessage = $"Unsupported method type: {methodInfo.MethodType}";
                        result.StatusCode = "NotSupported";
                        break;
                }
            }
            catch (OperationCanceledException)
            {
                result.IsSuccess = false;
                result.ErrorMessage = "Cancelled";
                result.StatusCode = "Cancelled";
            }
            catch (RpcException ex)
            {
                result.IsSuccess = false;
                result.ErrorMessage = $"{ex.StatusCode}: {ex.Message}";
                result.StatusCode = ex.StatusCode.ToString();
            }
            catch (Exception ex)
            {
                result.IsSuccess = false;
                result.ErrorMessage = ex.Message;
                result.StatusCode = "Error";
            }

            sw.Stop();
            result.DurationMs = sw.Elapsed.TotalMilliseconds;
            return result;
        }
    }
}
