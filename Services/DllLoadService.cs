using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.Loader;

namespace GrpcInvokeTool.Services
{
    public class DllLoadService
    {
        private AssemblyLoadContext? _loadContext;
        private Assembly? _loadedAssembly;
        private string? _loadedDllPath;

        public Assembly? LoadedAssembly => _loadedAssembly;

        public string? LoadedDllPath => _loadedDllPath;

        public bool IsLoaded => _loadedAssembly != null;

        public event Action<string>? OnStatusChanged;

        public Assembly LoadDll(string path)
        {
            if (IsLoaded)
                UnloadDll();

            if (!File.Exists(path))
                throw new FileNotFoundException($"DLL not found: {path}");

            _loadedDllPath = path;
            var dir = Path.GetDirectoryName(path)!;

            _loadContext = new AssemblyLoadContext("GrpcDllContext", isCollectible: true);
            _loadContext.Resolving += (context, name) =>
            {
                var assemblyPath = Path.Combine(dir, $"{name.Name}.dll");
                if (File.Exists(assemblyPath))
                    return context.LoadFromAssemblyPath(assemblyPath);

                // Try loading from nested directories
                foreach (var subDir in Directory.GetDirectories(dir, "*", SearchOption.TopDirectoryOnly))
                {
                    var subPath = Path.Combine(subDir, $"{name.Name}.dll");
                    if (File.Exists(subPath))
                        return context.LoadFromAssemblyPath(subPath);
                }

                return null;
            };

            _loadedAssembly = _loadContext.LoadFromAssemblyPath(path);
            OnStatusChanged?.Invoke($"Loaded: {Path.GetFileName(path)}");

            return _loadedAssembly;
        }

        public void UnloadDll()
        {
            if (_loadedAssembly == null) return;

            var name = Path.GetFileName(_loadedDllPath);
            _loadedAssembly = null;
            _loadedDllPath = null;
            _loadContext?.Unload();
            _loadContext = null;

            OnStatusChanged?.Invoke($"Unloaded: {name}");

            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }

        public List<string> GetLoadedTypes()
        {
            if (_loadedAssembly == null) return new List<string>();

            var types = new List<string>();
            foreach (var type in _loadedAssembly.GetTypes())
            {
                types.Add(type.FullName ?? type.Name);
            }
            return types;
        }
    }
}
