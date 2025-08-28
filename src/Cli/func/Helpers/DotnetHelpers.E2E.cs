// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Common;

namespace Azure.Functions.Cli.Helpers
{
    // Partial class to hold E2E test related helpers
    public static partial class DotnetHelpers
    {
        // Environment variable names to control custom hive usage in E2E tests
        internal const string CustomHiveFlag = "FUNC_E2E_USE_CUSTOM_HIVE";
        internal const string CustomHiveRoot = "FUNC_E2E_HIVE_ROOT";
        internal const string CustomHiveKey = "FUNC_E2E_HIVE_KEY";

        private static bool UseCustomTemplateHive() => string.Equals(Environment.GetEnvironmentVariable(CustomHiveFlag), "1", StringComparison.Ordinal);

        private static string GetHiveRoot()
        {
            string root = Environment.GetEnvironmentVariable(CustomHiveRoot);

            if (!string.IsNullOrWhiteSpace(root))
            {
                return root;
            }

            string coreToolsLocalDataPath = Utilities.EnsureCoreToolsLocalData();
            return Path.Combine(coreToolsLocalDataPath, "dotnet-templates-custom-hives");
        }

        // By default, each worker runtime shares a hive. This can be overridden by setting the FUNC_E2E_HIVE_KEY
        // environment variable to a custom value, which will cause a separate hive to be used.
        private static string GetHivePath(WorkerRuntime workerRuntime)
        {
            string key = Environment.GetEnvironmentVariable(CustomHiveKey);
            string leaf = !string.IsNullOrWhiteSpace(key) ? key : $"{workerRuntime.ToString().ToLowerInvariant()}-hive";
            return Path.Combine(GetHiveRoot(), leaf);
        }

        private static bool TryGetCustomHiveArg(WorkerRuntime workerRuntime, out string customHiveArg)
        {
            customHiveArg = string.Empty;

            if (!UseCustomTemplateHive())
            {
                return false;
            }

            string hive = GetHivePath(workerRuntime);
            FileSystemHelpers.EnsureDirectory(hive);

            customHiveArg = $" --debug:custom-hive \"{hive}\"";
            return true;
        }
    }
}
