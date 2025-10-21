// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Helpers;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Azure.Functions.Cli.ConfigurationProfiles
{
    internal class McpCustomHandlerConfigurationProfile : IConfigurationProfile
    {
        // This feature flag enables MCP (Model Context Protocol) support for custom handlers
        // This flag is not required locally, but is required when deploying to Azure environments.
        private const string McpFeatureFlag = "EnableMcpCustomHandlerPreview";

        public string Name { get; } = "mcp-custom-handler";

        public async Task ApplyAsync(WorkerRuntime workerRuntime, bool force = false)
        {
            await ApplyHostJsonAsync(force);
            await ApplyLocalSettingsAsync(workerRuntime, force);
        }

        internal async Task ApplyHostJsonAsync(bool force)
        {
            string hostJsonPath = Path.Combine(Environment.CurrentDirectory, Constants.HostJsonFileName);
            bool exists = FileSystemHelpers.FileExists(hostJsonPath);

            // Load host json source: existing host.json or the static resource
            string source = exists
                ? await FileSystemHelpers.ReadAllTextFromFileAsync(hostJsonPath)
                : await StaticResources.HostJson;

            var hostJsonObj = string.IsNullOrWhiteSpace(source) ? new JObject() : JObject.Parse(source);

            // 1) Add configuration profile
            bool updatedConfigProfile = UpsertIfMissing(hostJsonObj, "configurationProfile", JToken.FromObject(Name), force);
            if (updatedConfigProfile)
            {
                SetupProgressLogger.Ok(Constants.HostJsonFileName, $"Set configuration profile to '{Name}'");
            }

            // 2) Add custom handler settings
            var customHandlerJson = JObject.Parse(await StaticResources.CustomHandlerConfig);
            bool updatedCustomHandler = UpsertIfMissing(hostJsonObj, "customHandler", customHandlerJson, force);
            if (updatedCustomHandler)
            {
                SetupProgressLogger.Ok(Constants.HostJsonFileName, "Added custom handler configuration");
            }

            if (updatedConfigProfile || updatedCustomHandler)
            {
                string content = JsonConvert.SerializeObject(hostJsonObj, Formatting.Indented);
                await FileSystemHelpers.WriteAllTextToFileAsync(hostJsonPath, content);

                if (!exists)
                {
                    SetupProgressLogger.FileCreated(Constants.HostJsonFileName, Path.GetFullPath(hostJsonPath));
                }
            }
            else
            {
                SetupProgressLogger.Warn(Constants.HostJsonFileName, "Already configured (use --force to overwrite)");
            }
        }

        internal async Task ApplyLocalSettingsAsync(WorkerRuntime workerRuntime, bool force)
        {
            string localSettingsPath = Path.Combine(Environment.CurrentDirectory, Constants.LocalSettingsJsonFileName);
            bool exists = FileSystemHelpers.FileExists(localSettingsPath);

            // Load source for local.settings.json: existing file or the static resource
            string source = exists
                ? await FileSystemHelpers.ReadAllTextFromFileAsync(localSettingsPath)
                : (await StaticResources.LocalSettingsJson)
                    .Replace($"{{{Constants.FunctionsWorkerRuntime}}}", WorkerRuntimeLanguageHelper.GetRuntimeMoniker(workerRuntime))
                    .Replace($"{{{Constants.AzureWebJobsStorage}}}", Constants.StorageEmulatorConnectionString);

            var localSettingsObj = string.IsNullOrWhiteSpace(source) ? new JObject() : JObject.Parse(source);

            var values = localSettingsObj["Values"] as JObject ?? new JObject();

            // 1) Set worker runtime setting
            bool updatedWorkerRuntime = UpsertIfMissing(
                values,
                Constants.FunctionsWorkerRuntime,
                WorkerRuntimeLanguageHelper.GetRuntimeMoniker(workerRuntime),
                force);

            if (updatedWorkerRuntime)
            {
                SetupProgressLogger.Ok(Constants.LocalSettingsJsonFileName, $"Set {Constants.FunctionsWorkerRuntime} to '{WorkerRuntimeLanguageHelper.GetRuntimeMoniker(workerRuntime)}'");
            }

            // 2) Set feature flag setting
            bool updatedFeatureFlag = false;
            bool hasFlagsKey = values.TryGetValue(Constants.AzureWebJobsFeatureFlags, StringComparison.OrdinalIgnoreCase, out var flagsToken);
            var flags = (flagsToken?.ToString() ?? string.Empty)
                                    .Split(',', StringSplitOptions.RemoveEmptyEntries)
                                    .Select(f => f.Trim())
                                    .Where(f => !string.IsNullOrWhiteSpace(f))
                                    .ToList();

            if (!flags.Contains(McpFeatureFlag, StringComparer.OrdinalIgnoreCase))
            {
                flags.Add(McpFeatureFlag);
                values[Constants.AzureWebJobsFeatureFlags] = string.Join(",", flags);
                updatedFeatureFlag = true;

                if (!hasFlagsKey)
                {
                    SetupProgressLogger.Ok(Constants.LocalSettingsJsonFileName, $"Added feature flag '{McpFeatureFlag}'");
                }
                else
                {
                    SetupProgressLogger.Ok(Constants.LocalSettingsJsonFileName, $"Appended feature flag '{McpFeatureFlag}'");
                }
            }

            if (updatedWorkerRuntime || updatedFeatureFlag)
            {
                localSettingsObj["Values"] = values;
                string content = JsonConvert.SerializeObject(localSettingsObj, Formatting.Indented);
                await FileSystemHelpers.WriteAllTextToFileAsync(localSettingsPath, content);

                if (!exists)
                {
                    SetupProgressLogger.FileCreated(Constants.LocalSettingsJsonFileName, localSettingsPath);
                }
            }
            else
            {
                SetupProgressLogger.Warn(Constants.LocalSettingsJsonFileName, "Already configured (use --force to overwrite)");
            }
        }

        private static bool UpsertIfMissing(JObject obj, string key, object desiredValue, bool forceSet)
        {
            JToken desired = JToken.FromObject(desiredValue);

            if (obj.TryGetValue(key, StringComparison.OrdinalIgnoreCase, out var existing))
            {
                if (!forceSet)
                {
                    return false;
                }

                obj[key] = desired;
                return true;
            }

            obj[key] = desired;
            return true;
        }
    }
}
