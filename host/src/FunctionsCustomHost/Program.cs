// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Microsoft.VisualBasic;
using Newtonsoft.Json.Linq;
using System.IO;

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

                var localSettingsJObject = await GetLocalSettingsJsonAsJObjectAsync();
                var workerRuntime = localSettingsJObject?["Values"]?[EnvironmentVariables.FunctionsWorkerRuntime]?.Value<string>();
                if (string.IsNullOrEmpty(workerRuntime))
                {
                    Logger.Log($"Environment variable '{EnvironmentVariables.FunctionsWorkerRuntime}' is not set.");
                    return;
                }
                if (workerRuntime == DotnetConstants.DotnetWorkerRuntime)
                {
                    var isInProc8 = localSettingsJObject?["Values"]?[EnvironmentVariables.FunctionsInProcNet8Enabled]?.Value<string>();
                    // Load host assembly for .NET 8 in proc host
                    if (!string.IsNullOrEmpty(isInProc8) && string.Equals("1", isInProc8))
                    {
                        Logger.Log("Loading inproc8 host");
                        LoadHostAssembly(appLoader, isNet8InProc: true);
                    }
                    else
                    {
                        // Load host assembly for .NET 6 in proc host
                        Logger.Log("Loading inproc6 host");
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

            string fileName = "";
            fileName = Path.Combine(currentDirectory, isNet8InProc ? DotnetConstants.InProc8DirectoryName: DotnetConstants.InProc6DirectoryName, executableName);

            appLoader.RunApplication(fileName);

            var logMessage = $"FunctionApp assembly loaded successfully. ProcessId:{Environment.ProcessId}";
            if (OperatingSystem.IsWindows())
            {
                logMessage += $", AppPoolId:{Environment.GetEnvironmentVariable(EnvironmentVariables.AppPoolId)}";
            }
            Logger.Log(logMessage);

            // Have this here to stall the process
            Console.ReadLine();
        }

        private static async Task<JObject> GetLocalSettingsJsonAsJObjectAsync()
        {
            var fullPath = Path.Combine(Environment.CurrentDirectory, "local.settings.json");
            if (File.Exists(fullPath))
            {
                string fileContent = "";
                using (var fileStream = File.Open(fullPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                using (var streamReader = new StreamReader(fileStream))
                {
                    fileContent = await streamReader.ReadToEndAsync();
                }
                if (!string.IsNullOrEmpty(fileContent))
                {
                    var localSettingsJObject = JObject.Parse(fileContent);
                    return localSettingsJObject;
                }
            }

            return null;
        }
    }
}
