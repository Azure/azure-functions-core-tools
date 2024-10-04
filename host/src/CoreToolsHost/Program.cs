// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.


using System.Text.Json;

namespace CoreToolsHost
{
    internal class Program
    {
        static bool isVerbose = false;
        static async Task Main(string[] args)
        {
            try
            {
                isVerbose = args.Contains(DotnetConstants.Verbose);

                Logger.LogVerbose(isVerbose, "Starting CoreToolsHost");

                using var appLoader = new AppLoader();

                var localSettingsJson = await LocalSettingsJsonParser.GetLocalSettingsJsonAsJObjectAsync();
                localSettingsJson.RootElement.TryGetProperty("Values", out JsonElement valuesElement);
                string workerRuntime = string.Empty;

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
                Logger.Log($"An error occurred while running CoreToolsHost.{exception}");
            }
        }

        private static void LoadHostAssembly(AppLoader appLoader, string[] args, bool isNet8InProc)
        {
            var currentDirectory = AppContext.BaseDirectory;
            var executableName = DotnetConstants.ExecutableName;

            string filePath = "";
            filePath = Path.Combine(currentDirectory, isNet8InProc ? DotnetConstants.InProc8DirectoryName: DotnetConstants.InProc6DirectoryName, executableName);
            Logger.LogVerbose(isVerbose, $"File path to load: {filePath}");

            appLoader.RunApplication(filePath, args);
        }
    }
}
