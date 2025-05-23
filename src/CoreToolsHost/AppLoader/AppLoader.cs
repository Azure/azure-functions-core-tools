﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Runtime.InteropServices;

namespace CoreToolsHost
{
    // If having problems with the managed host, enable the following:
    // Environment.SetEnvironmentVariable("COREHOST_TRACE", "1");
    // In Unix environment, you need to run the below command in the terminal to set the environment variable.
    // export COREHOST_TRACE=1

    /// <summary>
    /// Manages loading hostfxr.
    /// </summary>
    internal sealed class AppLoader : IDisposable
    {
        private IntPtr _hostfxrHandle = IntPtr.Zero;
        private IntPtr _hostContextHandle = IntPtr.Zero;
        private bool _disposed;
        private bool _isVerbose = false;

        internal int RunApplication(string? assemblyPath, string[] commandLineArgs)
        {
            ArgumentNullException.ThrowIfNull(assemblyPath, nameof(assemblyPath));

            unsafe
            {
                var parameters = new NetHost.HostFxrParameters
                {
                    Size = sizeof(NetHost.HostFxrParameters),
                    AssemblyPath = GetCharArrayPointer(assemblyPath)
                };

                _isVerbose = commandLineArgs.Contains(DotnetConstants.Verbose);

                var hostfxrFullPath = NetHost.GetHostFxrPath(&parameters);
                Logger.LogVerbose(_isVerbose, $"hostfxr path:{hostfxrFullPath}");

                _hostfxrHandle = NativeLibrary.Load(hostfxrFullPath);

                if (_hostfxrHandle == IntPtr.Zero)
                {
                    Logger.Log($"Failed to load hostfxr. hostfxrFullPath:{hostfxrFullPath}");
                    return -1;
                }

                Logger.LogVerbose(_isVerbose, $"hostfxr loaded.");

                var commandLineArguments = commandLineArgs.Prepend(assemblyPath).ToArray();
                var error = HostFxr.Initialize(commandLineArguments.Length, commandLineArguments, IntPtr.Zero, out _hostContextHandle);

                if (_hostContextHandle == IntPtr.Zero)
                {
                    Logger.Log($"Failed to initialize the .NET Core runtime. Assembly path:{assemblyPath}");
                    return -1;
                }

                if (error < 0)
                {
                    return error;
                }

                Logger.LogVerbose(_isVerbose, $"hostfxr initialized with {assemblyPath}");

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
                    Logger.LogVerbose(_isVerbose, $"Freed hostfxr library handle");
                    _hostfxrHandle = IntPtr.Zero;
                }

                if (_hostContextHandle != IntPtr.Zero)
                {
                    HostFxr.Close(_hostContextHandle);
                    Logger.LogVerbose(_isVerbose, $"Closed hostcontext handle");
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
