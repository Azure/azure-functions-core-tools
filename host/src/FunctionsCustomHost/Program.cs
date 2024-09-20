// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

namespace FunctionsCustomHost
{
    internal class Program
    {
        static async Task Main(string[] args)
        {
            try
            {
                Logger.Log("Starting FunctionsCustomHost");

                using var appLoader = new AppLoader();

                var workerRuntime = EnvironmentUtils.GetValue(EnvironmentVariables.FunctionsWorkerRuntime)!;
                if (string.IsNullOrEmpty(workerRuntime))
                {
                    Logger.Log($"Environment variable '{EnvironmentVariables.FunctionsWorkerRuntime}' is not set.");
                    return;
                }
                if (workerRuntime == DotnetConstants.DotnetWorkerRuntime)
                {
                    // Load host assembly for .NET 8 in proc host
                    if (string.Equals("1", EnvironmentUtils.GetValue(EnvironmentVariables.FunctionsInProcNet8Enabled)))
                    {
                        LoadHostAssembly(appLoader, isOutOfProc: false, isNet8InProc: true);
                    }
                    else
                    {
                        // Load host assembly for .NET 6 in proc host
                        LoadHostAssembly(appLoader, isOutOfProc: false, isNet8InProc: false);
                    }

                }
                else if (workerRuntime == DotnetConstants.DotnetIsolatedWorkerRuntime)
                {
                    // Start process for oop host
                   LoadHostAssembly(appLoader, isOutOfProc: true, isNet8InProc: false);
                }
            }
            catch (Exception exception)
            {
                Logger.Log($"An error occurred while running FunctionsNetHost.{exception}");
            }
        }

        private static void LoadHostAssembly(AppLoader appLoader, bool isOutOfProc, bool isNet8InProc)
        {
            var currentDirectory = Environment.CurrentDirectory;

            var executableName = DotnetConstants.ExecutableName;

            string fileName = "";

            if (isOutOfProc)
            {
                fileName = Path.Combine(currentDirectory, executableName);
            }
            else
            {
                fileName = isNet8InProc ? Path.Combine(currentDirectory, DotnetConstants.InProc8DirectoryName, executableName) : Path.Combine(currentDirectory, DotnetConstants.InProc6DirectoryName, executableName);

            }

            appLoader.RunApplication(fileName);
        }
    }
}
