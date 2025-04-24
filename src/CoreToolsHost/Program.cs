// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Text.Json;

namespace CoreToolsHost
{
    internal class Program
    {
        private static bool _isVerbose = false;

        public static async Task Main(string[] args)
        {
            _isVerbose = args.Contains(DotnetConstants.Verbose);

            Logger.LogVerbose(_isVerbose, "Starting CoreToolsHost");

            var localSettingsJson = await LocalSettingsJsonParser.GetLocalSettingsJsonAsJObjectAsync();

            if (localSettingsJson is null)
            {
                Logger.LogVerbose(_isVerbose, "No local.settings.json file was found");
            }

            bool projectOptsIntoDotnet8 =

                // local.settings.json must be the source of the configuration
                localSettingsJson is not null && localSettingsJson.RootElement.TryGetProperty("Values", out JsonElement valuesElement)

                // The runtime must be specified as "dotnet"
                && ElementExistsWithValue(valuesElement, EnvironmentVariables.FunctionsWorkerRuntime, DotnetConstants.DotnetWorkerRuntime)

                // The .NET 8 enablement configuration must be provided
                && ElementExistsWithValue(valuesElement, EnvironmentVariables.FunctionsInProcNet8Enabled, "1");

            Logger.LogVerbose(_isVerbose, $"Loading .NET {(projectOptsIntoDotnet8 ? 8 : 6)} host");

            try
            {
                using var appLoader = new AppLoader();
                LoadHostAssembly(appLoader, args, projectOptsIntoDotnet8);
            }
            catch (Exception exception)
            {
                Logger.Log($"An error occurred while running CoreToolsHost: {exception}");
                Environment.Exit(1);
            }
        }

        private static bool ElementExistsWithValue(JsonElement element, string key, string value)
        {
            return !string.IsNullOrEmpty(key) && !string.IsNullOrEmpty(value)
                && element.TryGetProperty(key, out JsonElement property)
                && !string.IsNullOrEmpty(property.ToString())
                && string.Equals(property.ToString(), value, StringComparison.OrdinalIgnoreCase);
        }

        private static void LoadHostAssembly(AppLoader appLoader, string[] args, bool isNet8InProc)
        {
            string filePath = Path.Combine(
                AppContext.BaseDirectory, // current directory
                isNet8InProc ? DotnetConstants.InProc8DirectoryName : DotnetConstants.InProc6DirectoryName,
                DotnetConstants.ExecutableName);

            Logger.LogVerbose(_isVerbose, $"File path to load: {filePath}");

            appLoader.RunApplication(filePath, args);
        }
    }
}
