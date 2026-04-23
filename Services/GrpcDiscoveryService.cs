using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Grpc.Core;
using GrpcInvokeTool.Models;

namespace GrpcInvokeTool.Services
{
    public class GrpcDiscoveryService
    {
        public List<GrpcServiceInfo> DiscoverServices(Assembly assembly)
        {
            var services = new Dictionary<string, GrpcServiceInfo>(StringComparer.OrdinalIgnoreCase);
            var seenMethods = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            var clientBaseType = typeof(ClientBase<>);
            var clientTypes = assembly.GetTypes()
                .Where(t => t.IsClass && !t.IsAbstract && IsClientBase(t))
                .ToList();

            foreach (var clientType in clientTypes)
            {
                try
                {
                    var serviceInfo = ExtractServiceInfo(clientType, seenMethods);
                    if (serviceInfo != null)
                    {
                        if (!services.ContainsKey(serviceInfo.ServiceName))
                        {
                            services[serviceInfo.ServiceName] = new GrpcServiceInfo
                            {
                                ServiceName = serviceInfo.ServiceName,
                                PackageName = serviceInfo.PackageName
                            };
                        }

                        foreach (var method in serviceInfo.Methods)
                        {
                            services[serviceInfo.ServiceName].Methods.Add(method);
                        }
                    }
                }
                catch
                {
                    // Skip types that fail to process
                }
            }

            return services.Values.OrderBy(s => s.ServiceName).ToList();
        }

        private bool IsClientBase(Type type)
        {
            var current = type.BaseType;
            while (current != null)
            {
                if (current.IsGenericType && current.GetGenericTypeDefinition().FullName == "Grpc.Core.ClientBase`1")
                    return true;
                current = current.BaseType;
            }
            return false;
        }

        private GrpcServiceInfo? ExtractServiceInfo(Type clientType, HashSet<string> seenMethods)
        {
            var methods = new List<GrpcMethodInfo>();
            string serviceName = clientType.Name;
            string packageName = string.Empty;

            // Try to find Service descriptor field for service name
            var serviceField = clientType.GetField("Service",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);

            if (serviceField != null)
            {
                var serviceDescriptor = serviceField.GetValue(null);
                if (serviceDescriptor != null)
                {
                    var serviceDescType = serviceDescriptor.GetType();

                    // Try to get service name from ServiceDescriptor
                    var nameProp = serviceDescType.GetProperty("Name");
                    if (nameProp != null)
                        serviceName = nameProp.GetValue(serviceDescriptor)?.ToString() ?? serviceName;

                    var packageProp = serviceDescType.GetProperty("Package");
                    if (packageProp != null)
                        packageName = packageProp.GetValue(serviceDescriptor)?.ToString() ?? string.Empty;
                }
            }

            // Find all __Method_X fields (primary method discovery)
            var methodFields = clientType.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
                .Where(f => f.Name.StartsWith("__Method_"))
                .ToList();

            foreach (var field in methodFields)
            {
                try
                {
                    var methodDescriptor = field.GetValue(null);
                    if (methodDescriptor == null) continue;

                    var methodInfo = ExtractMethodInfo(methodDescriptor, clientType, serviceName);
                    if (methodInfo != null)
                    {
                        // Deduplicate by full method path
                        var key = $"{serviceName}/{methodInfo.MethodName}";
                        if (seenMethods.Add(key))
                        {
                            methods.Add(methodInfo);
                        }
                    }
                }
                catch
                {
                    // Skip failing fields
                }
            }

            // Fallback: if no __Method_ fields found, discover from CallInvoker-based methods
            // But still deduplicate against already-discovered methods
            if (methods.Count == 0)
            {
                methods = DiscoverFromClientMethods(clientType, serviceName, seenMethods);
            }

            if (methods.Count == 0) return null;

            return new GrpcServiceInfo
            {
                ServiceName = serviceName,
                PackageName = packageName,
                Methods = methods
            };
        }

        private GrpcMethodInfo? ExtractMethodInfo(object methodDescriptor, Type clientType, string serviceName)
        {
            var descType = methodDescriptor.GetType();

            // Method<TReq, TRes> properties
            var typeProp = descType.GetProperty("Type");
            var nameProp = descType.GetProperty("Name");
            var serviceNameProp = descType.GetProperty("ServiceName");

            if (typeProp == null || nameProp == null) return null;

            var methodType = (MethodType)typeProp.GetValue(methodDescriptor)!;
            var methodName = nameProp.GetValue(methodDescriptor)?.ToString() ?? "Unknown";
            var svcName = serviceNameProp?.GetValue(methodDescriptor)?.ToString() ?? serviceName;

            // Get generic type arguments for request/response
            var genericArgs = descType.GetGenericArguments();
            if (genericArgs.Length != 2) return null;

            return new GrpcMethodInfo
            {
                ServiceName = svcName,
                MethodName = methodName,
                MethodType = methodType,
                RequestType = genericArgs[0],
                ResponseType = genericArgs[1],
                MethodDescriptor = methodDescriptor,
                ClientType = clientType
            };
        }

        private List<GrpcMethodInfo> DiscoverFromClientMethods(Type clientType, string serviceName, HashSet<string> seenMethods)
        {
            var methods = new List<GrpcMethodInfo>();

            // Pre-load __Method_X fields for MethodDescriptor lookup
            var methodFields = clientType.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
                .Where(f => f.Name.StartsWith("__Method_"))
                .ToDictionary(f => f.Name, f => f);

            var publicMethods = clientType.GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .Where(m => m.DeclaringType == clientType && !m.Name.StartsWith("get_") && !m.Name.StartsWith("set_"))
                .ToList();

            foreach (var method in publicMethods)
            {
                var returnType = method.ReturnType;
                string methodTypeName = method.Name;

                if (returnType.IsGenericType)
                {
                    var genericDef = returnType.GetGenericTypeDefinition();
                    var genericArgs = returnType.GetGenericArguments();

                    MethodType mType = MethodType.Unary;
                    Type reqType = typeof(object);
                    Type resType = typeof(object);

                    if (genericDef.Name.StartsWith("AsyncUnaryCall") && genericArgs.Length >= 1)
                    {
                        mType = MethodType.Unary;
                        resType = genericArgs[0];
                        var parameters = method.GetParameters();
                        if (parameters.Length > 0) reqType = parameters[0].ParameterType;
                    }
                    else if (genericDef.Name.StartsWith("AsyncServerStreamingCall") && genericArgs.Length >= 1)
                    {
                        mType = MethodType.ServerStreaming;
                        resType = genericArgs[0];
                        var parameters = method.GetParameters();
                        if (parameters.Length > 0) reqType = parameters[0].ParameterType;
                    }
                    else if (genericDef.Name.StartsWith("AsyncClientStreamingCall") && genericArgs.Length >= 2)
                    {
                        mType = MethodType.ClientStreaming;
                        reqType = genericArgs[0];
                        resType = genericArgs[1];
                    }
                    else if (genericDef.Name.StartsWith("AsyncDuplexStreamingCall") && genericArgs.Length >= 2)
                    {
                        mType = MethodType.DuplexStreaming;
                        reqType = genericArgs[0];
                        resType = genericArgs[1];
                    }
                    else
                    {
                        continue;
                    }

                    // Remove "Async" suffix to get proto method name
                    var protoMethodName = methodTypeName;
                    if (protoMethodName.EndsWith("Async", StringComparison.OrdinalIgnoreCase))
                        protoMethodName = protoMethodName.Substring(0, protoMethodName.Length - 5);

                    // Try to find MethodDescriptor from __Method_X field
                    object? methodDescriptor = null;
                    var fieldKey = $"__Method_{protoMethodName}";
                    if (methodFields.TryGetValue(fieldKey, out var fieldInfo))
                    {
                        try { methodDescriptor = fieldInfo.GetValue(null); } catch { }
                    }

                    // If MethodDescriptor found, extract proto name from it for consistency
                    if (methodDescriptor != null)
                    {
                        var nameProp = methodDescriptor.GetType().GetProperty("Name");
                        if (nameProp != null)
                        {
                            var descriptorName = nameProp.GetValue(methodDescriptor)?.ToString();
                            if (!string.IsNullOrEmpty(descriptorName))
                                protoMethodName = descriptorName;
                        }
                    }

                    // Deduplicate
                    var key = $"{serviceName}/{protoMethodName}";
                    if (seenMethods.Add(key))
                    {
                        methods.Add(new GrpcMethodInfo
                        {
                            ServiceName = serviceName,
                            MethodName = protoMethodName,
                            MethodType = mType,
                            RequestType = reqType,
                            ResponseType = resType,
                            MethodDescriptor = methodDescriptor!,
                            ClientType = clientType
                        });
                    }
                }
            }

            return methods;
        }
    }
}
