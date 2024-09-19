// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Diagnostics;
using System.Runtime.InteropServices;

namespace FunctionsNetHost
{
    // If having problems with the managed host, enable the following:
    // Environment.SetEnvironmentVariable("COREHOST_TRACE", "1");
    // In Unix environment, you need to run the below command in the terminal to set the environment variable.
    // export COREHOST_TRACE=1

    /// <summary>
    /// Manages loading hostfxr & worker assembly.
    /// </summary>
    internal sealed class AppLoader : IDisposable
    {
        private IntPtr _hostfxrHandle = IntPtr.Zero;
        private IntPtr _hostContextHandle = IntPtr.Zero;
        private bool _disposed;

        internal AppLoader()
        {
        }

        internal int RunApplication(string? assemblyPath)
        {
            ArgumentNullException.ThrowIfNull(assemblyPath, nameof(assemblyPath));

            unsafe
            {
                var parameters = new NetHost.get_hostfxr_parameters
                {
                    size = sizeof(NetHost.get_hostfxr_parameters),
                    assembly_path = GetCharArrayPointer(assemblyPath)
                };

                var hostfxrFullPath = NetHost.GetHostFxrPath(&parameters);
                Logger.LogTrace($"hostfxr path:{hostfxrFullPath}");

                _hostfxrHandle = NativeLibrary.Load(hostfxrFullPath);

                if (_hostfxrHandle == IntPtr.Zero)
                {
                    Logger.Log($"Failed to load hostfxr. hostfxrFullPath:{hostfxrFullPath}");
                    return -1;
                }

                Logger.LogTrace($"hostfxr loaded.");

                //var commandLineArguments = _workerStartupOptions.CommandLineArgs.Prepend(assemblyPath).ToArray();
                //var error = HostFxr.Initialize(commandLineArguments.Length, commandLineArguments, IntPtr.Zero, out _hostContextHandle);

                if (_hostContextHandle == IntPtr.Zero)
                {
                    Logger.Log($"Failed to initialize the .NET Core runtime. Assembly path:{assemblyPath}");
                    return -1;
                }

                Logger.LogTrace($"hostfxr initialized with {assemblyPath}");
                HostFxr.SetAppContextData(_hostContextHandle, "AZURE_FUNCTIONS_NATIVE_HOST", "1");

                return HostFxr.Run(_hostContextHandle);
            }
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        private void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (!disposing)
                {
                    return;
                }

                if (_hostfxrHandle != IntPtr.Zero)
                {
                    NativeLibrary.Free(_hostfxrHandle);
                    Logger.LogTrace($"Freed hostfxr library handle");
                    _hostfxrHandle = IntPtr.Zero;
                }

                if (_hostContextHandle != IntPtr.Zero)
                {
                    HostFxr.Close(_hostContextHandle);
                    Logger.LogTrace($"Closed hostcontext handle");
                    _hostContextHandle = IntPtr.Zero;
                }

                _disposed = true;
            }
        }

        private static unsafe char* GetCharArrayPointer(string assemblyPath)
        {
#if OS_LINUX
            return (char*)Marshal.StringToHGlobalAnsi(assemblyPath).ToPointer();
#else
            return (char*)Marshal.StringToHGlobalUni(assemblyPath).ToPointer();
#endif
        }
    }
}
