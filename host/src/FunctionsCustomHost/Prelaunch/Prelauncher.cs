// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Diagnostics;

namespace FunctionsNetHost.Prelaunch
{
    internal static class PreLauncher
    {
        private const string DotNet = "dotnet";
        private const string AssemblyName = "App.dll";
        private const string PrelaunchAppsDirName = "prelaunchapps";
        private const int ProcessWaitForExitTimeInMilliSeconds = 10000;

        /// <summary>
        /// Attempts to start a minimal .NET application to preload/warmup the .NET runtime bits.
        /// </summary>
        internal static void Run()
        {
            // Adding this here for testing; remove later
            Environment.SetEnvironmentVariable(EnvironmentVariables.FunctionsWorkerRuntimeVersion, "8.0");
            Environment.SetEnvironmentVariable(EnvironmentVariables.FunctionsWorkerRuntime, "dotnet-isolated");
            Environment.SetEnvironmentVariable(EnvironmentVariables.FunctionsInProcNet8Enabled, "1");


            var runtimeVersion = EnvironmentUtils.GetValue(EnvironmentVariables.FunctionsWorkerRuntimeVersion)!;
            if (string.IsNullOrEmpty(runtimeVersion))
            {
                Logger.Log($"Environment variable '{EnvironmentVariables.FunctionsWorkerRuntimeVersion}' is not set.");
                return;
            }

            string appAssemblyPath = string.Empty;
            try
            {
                appAssemblyPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, PrelaunchAppsDirName, runtimeVersion, AssemblyName));

                if (!File.Exists(appAssemblyPath))
                {
                    Logger.Log($"File not found: {appAssemblyPath}");
                    return;
                }

                var process = Process.Start(new ProcessStartInfo
                {
                    FileName = DotNet,
                    Arguments = $"\"{appAssemblyPath}\"",
                    UseShellExecute = false
                });

                if (process == null)
                {
                    Logger.Log($"Failed to start process: {appAssemblyPath}");
                    return;
                }

                Logger.Log($"Started process: {appAssemblyPath}, PID: {process.Id}");

                process.EnableRaisingEvents = true;
                process.Exited += (sender, e) => Logger.Log($"Process exited: {appAssemblyPath}, PID: {process.Id}");

                if (!process.WaitForExit(ProcessWaitForExitTimeInMilliSeconds))
                {
                    try
                    {
                        process.Kill();
                        Logger.Log(
                            $"Process was still running after 10 seconds and was killed: {appAssemblyPath}, PID: {process.Id}");
                    }
                    catch (Exception ex)
                    {
                        Logger.Log($"Failed to kill process: {appAssemblyPath}, PID: {process.Id}. {ex}");
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Log($"FunctionsNetHost.Prelauncher. Failed to load: {appAssemblyPath}. {ex}");
            }
        }
    }
}
