// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.


using System.Text.Json;

namespace FunctionsCustomHost
{
    internal class Program
    {
        static async Task Main(string[] args)
        {
            try
            {
                Logger.LogTrace("Starting FunctionsCustomHost");

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
                        Logger.LogTrace("Loading inproc8 host");
                        LoadHostAssembly(appLoader, isNet8InProc: true);
                    }
                    else
                    {
                        // Load host assembly for .NET 6 in proc host
                        Logger.LogTrace("Loading inproc6 host");
                        LoadHostAssembly(appLoader, isNet8InProc: false);
                    }

                }
            }
            catch (Exception exception)
            {
                Logger.Log($"An error occurred while running FunctionsCustomHost.{exception}");
            }
        }

        private static void LoadHostAssembly(AppLoader appLoader, bool isNet8InProc)
        {
            var currentDirectory = AppContext.BaseDirectory;
            var executableName = DotnetConstants.ExecutableName;

            string filePath = "";
            filePath = Path.Combine(currentDirectory, isNet8InProc ? DotnetConstants.InProc8DirectoryName: DotnetConstants.InProc6DirectoryName, executableName);

            appLoader.RunApplication(filePath);

            var logMessage = $"FunctionApp assembly loaded successfully. ProcessId:{Environment.ProcessId}";
            Logger.LogTrace(logMessage);

            // Have this here to stall the process
            Console.ReadLine();
        }
    }
}
