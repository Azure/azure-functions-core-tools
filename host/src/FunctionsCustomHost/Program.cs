// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.


using System.Text.Json;

namespace FunctionsCustomHost
{
    internal class Program
    {
        static bool isVerbose = false;
        static async Task Main(string[] args)
        {
            try
            {
                if (args.Contains("--verbose"))
                {
                    isVerbose = true;
                }

                Logger.LogVerbose(isVerbose, "Starting FunctionsCustomHost");

                using var appLoader = new AppLoader();

                var localSettingsJson = await LocalSettingsJsonParser.GetLocalSettingsJsonAsJObjectAsync();
                localSettingsJson.RootElement.TryGetProperty("Values", out JsonElement valuesElement);
                string workerRuntime = "";

                if (valuesElement.TryGetProperty(EnvironmentVariables.FunctionsWorkerRuntime, out JsonElement workerRuntimeElement))
                {
                    workerRuntime = workerRuntimeElement.GetString();
                }
                
                if (string.IsNullOrEmpty(workerRuntime))
                {
                    Logger.Log($"Environment variable '{EnvironmentVariables.FunctionsWorkerRuntime}' is not set.");
                    return;
                }

                if (workerRuntime == DotnetConstants.DotnetWorkerRuntime)
                {
                    string isInProc8 = "";
                    if (valuesElement.TryGetProperty(EnvironmentVariables.FunctionsInProcNet8Enabled, out JsonElement inProc8EnabledElement))
                    {
                        isInProc8 = inProc8EnabledElement.GetString();
                    }

                    // Load host assembly for .NET 8 in proc host
                    if (!string.IsNullOrEmpty(isInProc8) && string.Equals("1", isInProc8))
                    {
                        Logger.LogVerbose(isVerbose, "Loading inproc8 host");
                        LoadHostAssembly(appLoader, args, isNet8InProc: true);
                    }
                    else
                    {
                        // Load host assembly for .NET 6 in proc host
                        Logger.LogVerbose(isVerbose, "Loading inproc6 host");
                        LoadHostAssembly(appLoader, args, isNet8InProc: false);
                    }

                }
            }
            catch (Exception exception)
            {
                Logger.Log($"An error occurred while running FunctionsCustomHost.{exception}");
            }
        }

        private static void LoadHostAssembly(AppLoader appLoader, string[] args, bool isNet8InProc)
        {
            var currentDirectory = AppContext.BaseDirectory;
            var executableName = DotnetConstants.ExecutableName;

            string filePath = "";
            filePath = Path.Combine(currentDirectory, isNet8InProc ? DotnetConstants.InProc8DirectoryName: DotnetConstants.InProc6DirectoryName, executableName);

            int response = appLoader.RunApplication(filePath, args);

            if (response < 0)
            {
                return;
            }

            var logMessage = $"FunctionApp assembly loaded successfully. ProcessId:{Environment.ProcessId}";
            Logger.LogVerbose(isVerbose, logMessage);
        }
    }
}
