using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Google.Protobuf;
using Grpc.Core;
using GrpcInvokeTool.Models;

namespace GrpcInvokeTool.Services
{
    public class GrpcInvokeService
    {
        public sealed class DuplexStreamingSession : IAsyncDisposable
        {
            private readonly object _client;
            private readonly object _callResult;
            private readonly object _requestStream;
            private readonly object _responseStream;
            private readonly MethodInfo? _writeAsyncMethod;
            private readonly MethodInfo? _completeMethod;
            private readonly MethodInfo? _moveNextMethod;
            private readonly PropertyInfo? _currentProp;
            private readonly CancellationToken _cancellationToken;
            private readonly SemaphoreSlim _writeLock = new(1, 1);
            private readonly Task _responseDrainTask;
            private bool _isCompleted;

            internal DuplexStreamingSession(
                object client,
                object callResult,
                object requestStream,
                object responseStream,
                MethodInfo? writeAsyncMethod,
                MethodInfo? completeMethod,
                MethodInfo? moveNextMethod,
                PropertyInfo? currentProp,
                CancellationToken cancellationToken)
            {
                _client = client;
                _callResult = callResult;
                _requestStream = requestStream;
                _responseStream = responseStream;
                _writeAsyncMethod = writeAsyncMethod;
                _completeMethod = completeMethod;
                _moveNextMethod = moveNextMethod;
                _currentProp = currentProp;
                _cancellationToken = cancellationToken;
                _responseDrainTask = Task.Run(DrainResponsesAsync, cancellationToken);
            }

            public async Task SendAsync(IMessage request, CancellationToken cancellationToken)
            {
                if (_isCompleted)
                    throw new InvalidOperationException("Duplex streaming session has already been completed.");

                if (_writeAsyncMethod == null)
                    throw new InvalidOperationException("Request stream does not expose WriteAsync.");

                await _writeLock.WaitAsync(cancellationToken);
                try
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var writeTask = (Task)_writeAsyncMethod.Invoke(_requestStream, new object[] { request })!;
                    await writeTask;
                }
                finally
                {
                    _writeLock.Release();
                }
            }

            public async Task CompleteAsync()
            {
                if (_isCompleted)
                    return;

                _isCompleted = true;

                if (_completeMethod != null)
                {
                    var completeTask = (Task)_completeMethod.Invoke(_requestStream, Array.Empty<object>())!;
                    await completeTask;
                }

                try
                {
                    await _responseDrainTask;
                }
                catch (OperationCanceledException)
                {
                }
            }

            private async Task DrainResponsesAsync()
            {
                if (_moveNextMethod == null || _currentProp == null)
                    return;

                while (!_cancellationToken.IsCancellationRequested)
                {
                    var moveNextTask = (Task<bool>)_moveNextMethod.Invoke(_responseStream, new object[] { _cancellationToken })!;
                    var hasNext = await moveNextTask;
                    if (!hasNext)
                        break;

                    _ = _currentProp.GetValue(_responseStream);
                }
            }

            public async ValueTask DisposeAsync()
            {
                try
                {
                    await CompleteAsync();
                }
                finally
                {
                    _writeLock.Dispose();
                    GC.KeepAlive(_client);
                    GC.KeepAlive(_callResult);
                }
            }
        }

        private static readonly JsonFormatter PrettyFormatter = new JsonFormatter(JsonFormatter.Settings.Default.WithIndentation());
        private Channel? _channel;
        private string? _currentAddress;
        private CancellationTokenSource? _streamCts;

        public bool IsStreamingActive => _streamCts != null && !_streamCts.IsCancellationRequested;

        /// <summary>
        /// Creates a client instance and extracts its CallInvoker for direct gRPC calls.
        /// This avoids relying on method name matching on the client class.
        /// </summary>
        private (object client, CallInvoker callInvoker) CreateClientAndInvoker(GrpcMethodInfo methodInfo, string address)
        {
            var channel = GetOrCreateChannel(address);
            var client = Activator.CreateInstance(methodInfo.ClientType, channel)!;

            var callInvokerProp = methodInfo.ClientType.GetProperty("CallInvoker",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (callInvokerProp == null)
            {
                var baseType = methodInfo.ClientType.BaseType;
                while (baseType != null)
                {
                    callInvokerProp = baseType.GetProperty("CallInvoker",
                        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    if (callInvokerProp != null) break;
                    baseType = baseType.BaseType;
                }
            }

            if (callInvokerProp == null)
                throw new InvalidOperationException($"Cannot find CallInvoker on {methodInfo.ClientType.Name}");

            var callInvoker = (CallInvoker)callInvokerProp.GetValue(client)!;
            return (client, callInvoker);
        }

        private object? FindMethodDescriptor(GrpcMethodInfo methodInfo)
        {
            if (methodInfo.MethodDescriptor != null)
                return methodInfo.MethodDescriptor;

            var methodFields = methodInfo.ClientType.GetFields(
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
                .Where(f => f.Name.StartsWith("__Method_"))
                .ToList();

            // 1. Try matching by MethodType + Name property
            object? fallbackByTypes = null;
            foreach (var field in methodFields)
            {
                try
                {
                    var descriptor = field.GetValue(null);
                    if (descriptor == null) continue;

                    var descType = descriptor.GetType();
                    var typeProp = descType.GetProperty("Type");
                    if (typeProp == null) continue;

                    var methodType = (MethodType)typeProp.GetValue(descriptor)!;
                    if (methodType != methodInfo.MethodType) continue;

                    var nameProp = descType.GetProperty("Name");
                    if (nameProp != null)
                    {
                        var name = nameProp.GetValue(descriptor)?.ToString();
                        if (string.Equals(name, methodInfo.MethodName, StringComparison.OrdinalIgnoreCase))
                            return descriptor;
                    }

                    // 2. Fallback: match by MethodType + request/response generic args
                    var genericArgs = descType.GetGenericArguments();
                    if (genericArgs.Length == 2
                        && genericArgs[0] == methodInfo.RequestType
                        && genericArgs[1] == methodInfo.ResponseType)
                    {
                        fallbackByTypes = descriptor;
                    }
                }
                catch { }
            }

            // 3. Fallback: match by field name (e.g. __Method_NodeControl matches method "NodeControl")
            if (fallbackByTypes == null)
            {
                var targetFieldName = $"__Method_{methodInfo.MethodName}";
                var fieldMatch = methodFields.FirstOrDefault(f =>
                    string.Equals(f.Name, targetFieldName, StringComparison.OrdinalIgnoreCase));
                if (fieldMatch != null)
                {
                    try { fallbackByTypes = fieldMatch.GetValue(null); } catch { }
                }
            }

            return fallbackByTypes;
        }




        /// <summary>
        /// Finds a client method matching the given async call return type.
        /// Supports both single-generic-arg types (AsyncUnaryCall&lt;&gt;, AsyncServerStreamingCall&lt;&gt;)
        /// and double-generic-arg types (AsyncClientStreamingCall&lt;,&gt;, AsyncDuplexStreamingCall&lt;,&gt;).
        /// </summary>
        private MethodInfo? FindClientMethod(GrpcMethodInfo methodInfo, Type asyncCallGenericType)
        {
            var targetMethodName = methodInfo.MethodName.EndsWith("Async", StringComparison.OrdinalIgnoreCase)
                ? methodInfo.MethodName[..^5]
                : methodInfo.MethodName;

            var candidates = methodInfo.ClientType.GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .Where(m => m.ReturnType.IsGenericType
                    && m.ReturnType.GetGenericTypeDefinition() == asyncCallGenericType)
                .ToList();

            // 1. Match by method name
            MethodInfo? bestMatch = candidates.FirstOrDefault(m =>
                string.Equals(m.Name, targetMethodName, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(m.Name, targetMethodName + "Async", StringComparison.OrdinalIgnoreCase));

            if (bestMatch != null)
                return bestMatch;

            // 2. Match by response type (works for both 1-arg and 2-arg generic types)
            bestMatch = candidates.FirstOrDefault(m =>
            {
                var genericArgs = m.ReturnType.GetGenericArguments();
                if (genericArgs.Length == 1)
                    return genericArgs[0] == methodInfo.ResponseType;
                if (genericArgs.Length == 2)
                    return genericArgs[0] == methodInfo.RequestType && genericArgs[1] == methodInfo.ResponseType;
                return false;
            });

            return bestMatch;
        }

        /// <summary>
        /// Invokes a client method with auto-filled parameters.
        /// </summary>
        private object InvokeClientMethod(MethodInfo method, object client, IMessage? request = null)
        {
            var parameters = method.GetParameters();
            var args = new object?[parameters.Length];

            for (var i = 0; i < parameters.Length; i++)
            {
                var parameter = parameters[i];

                if (request != null && parameter.ParameterType.IsAssignableFrom(request.GetType()))
                {
                    args[i] = request;
                }
                else if (parameter.ParameterType == typeof(Metadata))
                {
                    args[i] = null;
                }
                else if (parameter.ParameterType == typeof(DateTime?))
                {
                    args[i] = null;
                }
                else if (parameter.ParameterType == typeof(CancellationToken))
                {
                    args[i] = CancellationToken.None;
                }
                else if (parameter.ParameterType == typeof(CallOptions))
                {
                    args[i] = new CallOptions();
                }
                else if (parameter.HasDefaultValue)
                {
                    args[i] = parameter.DefaultValue;
                }
                else if (parameter.ParameterType.IsValueType)
                {
                    args[i] = Activator.CreateInstance(parameter.ParameterType);
                }
                else
                {
                    args[i] = null;
                }
            }

            return method.Invoke(client, args)!;
        }

        public async Task<DuplexStreamingSession> OpenDuplexStreamingSessionAsync(
            GrpcMethodInfo methodInfo,
            string address,
            CancellationToken cancellationToken)
        {
            var (client, callInvoker) = CreateClientAndInvoker(methodInfo, address);

            object callResult;
            var methodDescriptor = FindMethodDescriptor(methodInfo);

            if (methodDescriptor != null)
            {
                var asyncDuplexStreamMethod = typeof(CallInvoker).GetMethods(BindingFlags.Public | BindingFlags.Instance)
                    .FirstOrDefault(m => m.Name == "AsyncDuplexStreamingCall" && m.IsGenericMethodDefinition && m.GetGenericArguments().Length == 2);

                if (asyncDuplexStreamMethod != null)
                {
                    var genericMethod = asyncDuplexStreamMethod.MakeGenericMethod(methodInfo.RequestType, methodInfo.ResponseType);
                    var callOptions = new CallOptions(cancellationToken: cancellationToken);
                    callResult = genericMethod.Invoke(callInvoker, new object[] { methodDescriptor, null!, callOptions })!;
                }
                else
                {
                    throw new InvalidOperationException("Cannot find AsyncDuplexStreamingCall method on CallInvoker");
                }
            }
            else
            {
                var clientMethod = FindClientMethod(methodInfo, typeof(AsyncDuplexStreamingCall<,>));
                if (clientMethod == null)
                    throw new InvalidOperationException($"Cannot find duplex streaming method for {methodInfo.MethodName}");

                callResult = InvokeClientMethod(clientMethod, client);
            }

            var requestStreamProp = callResult.GetType().GetProperty("RequestStream")!;
            var requestStream = requestStreamProp.GetValue(callResult)!;
            var responseStreamProp = callResult.GetType().GetProperty("ResponseStream")!;
            var responseStream = responseStreamProp.GetValue(callResult)!;

            var writeAsyncMethod = requestStream.GetType().GetMethod("WriteAsync");
            var completeMethod = requestStream.GetType().GetMethod("CompleteAsync");
            var moveNextMethod = responseStream.GetType().GetMethod("MoveNext");
            var currentProp = responseStream.GetType().GetProperty("Current");

            return await Task.FromResult(new DuplexStreamingSession(
                client,
                callResult,
                requestStream,
                responseStream,
                writeAsyncMethod,
                completeMethod,
                moveNextMethod,
                currentProp,
                cancellationToken));
        }

        public async Task<string> InvokeUnaryAsync(GrpcMethodInfo methodInfo, IMessage request, string address)
        {
            var (client, callInvoker) = CreateClientAndInvoker(methodInfo, address);
            var methodDescriptor = FindMethodDescriptor(methodInfo);

            if (methodDescriptor != null)
            {
                // Primary path: CallInvoker + MethodDescriptor
                var asyncUnaryMethod = typeof(CallInvoker).GetMethods(BindingFlags.Public | BindingFlags.Instance)
                    .FirstOrDefault(m => m.Name == "AsyncUnaryCall" && m.IsGenericMethodDefinition && m.GetGenericArguments().Length == 2);

                if (asyncUnaryMethod != null)
                {
                    var genericMethod = asyncUnaryMethod.MakeGenericMethod(methodInfo.RequestType, methodInfo.ResponseType);
                    var callOptions = new CallOptions();
                    var callResult = genericMethod.Invoke(callInvoker, new object[] { methodDescriptor, null!, callOptions, request })!;

                    var responseAsyncProp = callResult.GetType().GetProperty("ResponseAsync")!;
                    var responseTask = (Task)responseAsyncProp.GetValue(callResult)!;
                    await responseTask;

                    var resultProp = responseTask.GetType().GetProperty("Result")!;
                    var result = resultProp.GetValue(responseTask)!;
                    return PrettyFormatter.Format((IMessage)result);
                }
            }

            // Fallback: direct client method invocation
            var clientMethod = FindClientMethod(methodInfo, typeof(AsyncUnaryCall<>));
            if (clientMethod == null)
                throw new InvalidOperationException($"Cannot find unary method for {methodInfo.FullName}");

            var callResultFallback = InvokeClientMethod(clientMethod, client, request);
            var responseAsyncPropFallback = callResultFallback.GetType().GetProperty("ResponseAsync")!;
            var responseTaskFallback = (Task)responseAsyncPropFallback.GetValue(callResultFallback)!;
            await responseTaskFallback;

            var resultPropFallback = responseTaskFallback.GetType().GetProperty("Result")!;
            var resultFallback = resultPropFallback.GetValue(responseTaskFallback)!;
            return PrettyFormatter.Format((IMessage)resultFallback);
        }

        public async IAsyncEnumerable<StreamMessageItem> InvokeServerStreamingAsync(
            GrpcMethodInfo methodInfo, IMessage request, string address,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            var (client, callInvoker) = CreateClientAndInvoker(methodInfo, address);
            var methodDescriptor = FindMethodDescriptor(methodInfo);

            object responseStream;

            if (methodDescriptor != null)
            {
                var asyncServerStreamMethod = typeof(CallInvoker).GetMethods(BindingFlags.Public | BindingFlags.Instance)
                    .FirstOrDefault(m => m.Name == "AsyncServerStreamingCall" && m.IsGenericMethodDefinition && m.GetGenericArguments().Length == 2);

                if (asyncServerStreamMethod != null)
                {
                    var genericMethod = asyncServerStreamMethod.MakeGenericMethod(methodInfo.RequestType, methodInfo.ResponseType);
                    var callOptions = new CallOptions();
                    var callResult = genericMethod.Invoke(callInvoker, new object[] { methodDescriptor, null!, callOptions, request })!;

                    var responseStreamProp = callResult.GetType().GetProperty("ResponseStream")!;
                    responseStream = responseStreamProp.GetValue(callResult)!;
                }
                else
                {
                    throw new InvalidOperationException("Cannot find AsyncServerStreamingCall method on CallInvoker");
                }
            }
            else
            {
                // Fallback: direct client method invocation
                var clientMethod = FindClientMethod(methodInfo, typeof(AsyncServerStreamingCall<>));
                if (clientMethod == null)
                    throw new InvalidOperationException($"Cannot find server streaming method for {methodInfo.FullName}");

                var callResult = InvokeClientMethod(clientMethod, client, request);
                var responseStreamProp = callResult.GetType().GetProperty("ResponseStream")!;
                responseStream = responseStreamProp.GetValue(callResult)!;
            }

            var moveNextMethod = responseStream.GetType().GetMethod("MoveNext");
            var currentProp = responseStream.GetType().GetProperty("Current");

            if (moveNextMethod == null || currentProp == null)
                yield break;

            int index = 1;
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var moveNextTask = (Task<bool>)moveNextMethod.Invoke(responseStream, new object[] { cancellationToken })!;
                var hasNext = await moveNextTask;

                if (!hasNext) break;

                var current = currentProp.GetValue(responseStream)!;
                var json = JsonFormatter.Default.Format((IMessage)current);

                yield return new StreamMessageItem
                {
                    Index = index++,
                    Content = json,
                    IsError = false
                };
            }
        }

        public async IAsyncEnumerable<StreamMessageItem> InvokeClientStreamingAsync(
            GrpcMethodInfo methodInfo, IEnumerable<IMessage> requests, string address,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            var (client, callInvoker) = CreateClientAndInvoker(methodInfo, address);
            var methodDescriptor = FindMethodDescriptor(methodInfo);

            object callResult;
            object requestStream;

            if (methodDescriptor != null)
            {
                var asyncClientStreamMethod = typeof(CallInvoker).GetMethods(BindingFlags.Public | BindingFlags.Instance)
                    .FirstOrDefault(m => m.Name == "AsyncClientStreamingCall" && m.IsGenericMethodDefinition && m.GetGenericArguments().Length == 2);

                if (asyncClientStreamMethod == null)
                    throw new InvalidOperationException("Cannot find AsyncClientStreamingCall method on CallInvoker");

                var genericMethod = asyncClientStreamMethod.MakeGenericMethod(methodInfo.RequestType, methodInfo.ResponseType);
                var callOptions = new CallOptions();
                callResult = genericMethod.Invoke(callInvoker, new object[] { methodDescriptor, null!, callOptions })!;

                var requestStreamProp = callResult.GetType().GetProperty("RequestStream")!;
                requestStream = requestStreamProp.GetValue(callResult)!;
            }
            else
            {
                // Fallback: direct client method invocation
                var clientMethod = FindClientMethod(methodInfo, typeof(AsyncClientStreamingCall<,>));
                if (clientMethod == null)
                    throw new InvalidOperationException($"Cannot find client streaming method for {methodInfo.FullName}");

                callResult = InvokeClientMethod(clientMethod, client);
                var requestStreamProp = callResult.GetType().GetProperty("RequestStream")!;
                requestStream = requestStreamProp.GetValue(callResult)!;
            }

            var writeAsyncMethod = requestStream.GetType().GetMethod("WriteAsync");
            var completeMethod = requestStream.GetType().GetMethod("CompleteAsync");

            int index = 1;
            foreach (var req in requests)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (writeAsyncMethod != null)
                {
                    var writeTask = (Task)writeAsyncMethod.Invoke(requestStream, new object[] { req })!;
                    await writeTask;
                }
                yield return new StreamMessageItem
                {
                    Index = index++,
                    Content = $"Sent: {JsonFormatter.Default.Format(req)}",
                    IsError = false
                };
            }

            if (completeMethod != null)
            {
                var completeTask = (Task)completeMethod.Invoke(requestStream, Array.Empty<object>())!;
                await completeTask;
            }

            var responseAsyncProp = callResult.GetType().GetProperty("ResponseAsync")!;
            var responseTask = (Task)responseAsyncProp.GetValue(callResult)!;
            await responseTask;

            var resultProp = responseTask.GetType().GetProperty("Result")!;
            var result = resultProp.GetValue(responseTask)!;

            yield return new StreamMessageItem
            {
                Index = index,
                Content = JsonFormatter.Default.Format((IMessage)result),
                IsError = false
            };
        }

        public async IAsyncEnumerable<StreamMessageItem> InvokeDuplexStreamingAsync(
            GrpcMethodInfo methodInfo, IEnumerable<IMessage> requests, string address,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await using var session = await OpenDuplexStreamingSessionAsync(methodInfo, address, cancellationToken);

            int sendIndex = 1;
            foreach (var req in requests)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await session.SendAsync(req, cancellationToken);

                yield return new StreamMessageItem
                {
                    Index = sendIndex++,
                    Content = $"Sent: {PrettyFormatter.Format(req)}",
                    IsError = false,
                    IsReceived = false
                };
            }
        }

        public void CancelStreaming()
        {
            _streamCts?.Cancel();
            _streamCts = null;
        }

        public CancellationToken GetStreamingCancellationToken()
        {
            _streamCts?.Cancel();
            _streamCts = new CancellationTokenSource();
            return _streamCts.Token;
        }

        private Channel GetOrCreateChannel(string address)
        {
            if (_channel != null && _currentAddress == address)
                return _channel;

            if (_channel != null)
            {
                try { _channel.ShutdownAsync().Wait(TimeSpan.FromSeconds(5)); } catch { }
            }

            var (host, port) = ParseAddress(address);
            _channel = new Channel(host, port, ChannelCredentials.Insecure,
                new List<ChannelOption>
                {
                    new ChannelOption(ChannelOptions.MaxReceiveMessageLength, 64 * 1024 * 1024),
                    new ChannelOption(ChannelOptions.MaxSendMessageLength, 64 * 1024 * 1024),
                });
            _currentAddress = address;
            return _channel;
        }

        private (string host, int port) ParseAddress(string address)
        {
            var cleaned = address.Trim();
            if (cleaned.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
                cleaned = cleaned.Substring(7);
            else if (cleaned.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                cleaned = cleaned.Substring(8);

            var parts = cleaned.Split(':');
            if (parts.Length == 2 && int.TryParse(parts[1], out var port))
            {
                return (parts[0].Trim(), port);
            }

            return (cleaned.Trim(), 50051);
        }

        public void Dispose()
        {
            _streamCts?.Cancel();
            _streamCts?.Dispose();
            if (_channel != null)
            {
                try { _channel.ShutdownAsync().Wait(TimeSpan.FromSeconds(5)); } catch { }
            }
        }

        public void Reset()
        {
            _streamCts?.Cancel();
            _streamCts?.Dispose();
            _streamCts = null;

            if (_channel != null)
            {
                try { _channel.ShutdownAsync().Wait(TimeSpan.FromSeconds(5)); } catch { }
                _channel = null;
            }

            _currentAddress = null;
        }
    }
}
