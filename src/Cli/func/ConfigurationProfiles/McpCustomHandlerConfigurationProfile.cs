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
        // This feature flag enables MCP (Multi-Container Platform) support for custom handlers
        // This flag is not required locally, but is required when deploying to Azure environments.
        private const string McpFeatureFlag = "EnableMcpCustomHandlerPreview";

        public string Name { get; } = "mcp-custom-handler";

        public async Task ApplyAsync(WorkerRuntime workerRuntime, bool shouldForce = false)
        {
            await ApplyHostJsonAsync(shouldForce);
            await ApplyLocalSettingsAsync(workerRuntime, shouldForce);
        }

        public async Task ApplyHostJsonAsync(bool force)
        {
            bool changed = false;
            string baseHostJson;

            // Check if host.json exists and read it, otherwise use the default template
            string hostJsonPath = Path.Combine(Environment.CurrentDirectory, Constants.HostJsonFileName);

            if (FileSystemHelpers.FileExists(hostJsonPath))
            {
                SetupProgressLogger.FileFound("host.json", hostJsonPath);
                baseHostJson = await FileSystemHelpers.ReadAllTextFromFileAsync(hostJsonPath);
            }
            else
            {
                SetupProgressLogger.FileCreated("host.json", hostJsonPath);
                baseHostJson = await StaticResources.HostJson;
            }

            JObject hostJsonObj = JsonConvert.DeserializeObject<JObject>(baseHostJson);

            // Add configurationProfile if missing or if force is true
            if (!hostJsonObj.TryGetValue("configurationProfile", StringComparison.OrdinalIgnoreCase, out _) || force)
            {
                hostJsonObj["configurationProfile"] = Name;
                changed = true;
            }

            // Add customHandler section if missing or if force is true
            if (!hostJsonObj.TryGetValue("customHandler", StringComparison.OrdinalIgnoreCase, out _) || force)
            {
                hostJsonObj["customHandler"] = new JObject
                {
                    ["description"] = new JObject
                    {
                        ["defaultExecutablePath"] = string.Empty,
                        ["arguments"] = new JArray()
                    }
                };
                changed = true;
            }

            if (changed)
            {
                string hostJsonContent = JsonConvert.SerializeObject(hostJsonObj, Formatting.Indented);
                await FileSystemHelpers.WriteAllTextToFileAsync(hostJsonPath, hostJsonContent);
                SetupProgressLogger.Ok("host.json", "Updated with MCP configuration profile");
            }
            else
            {
                SetupProgressLogger.Warn("host.json", "Already configured (use --force to overwrite)");
            }
        }

        public async Task ApplyLocalSettingsAsync(WorkerRuntime workerRuntime, bool force)
        {
            bool changed = false;
            string baseLocalSettings;

            // Check if local.settings.json exists and read it, otherwise use the default template
            string localSettingsPath = Path.Combine(Environment.CurrentDirectory, "local.settings.json");

            if (FileSystemHelpers.FileExists(localSettingsPath))
            {
                SetupProgressLogger.FileFound("local.settings.json", localSettingsPath);
                baseLocalSettings = await FileSystemHelpers.ReadAllTextFromFileAsync(localSettingsPath);
            }
            else
            {
                SetupProgressLogger.FileCreated("local.settings.json", localSettingsPath);
                baseLocalSettings = await StaticResources.LocalSettingsJson;

                // Replace placeholders in the template
                baseLocalSettings = baseLocalSettings.Replace($"{{{Constants.FunctionsWorkerRuntime}}}", WorkerRuntimeLanguageHelper.GetRuntimeMoniker(workerRuntime));
                baseLocalSettings = baseLocalSettings.Replace($"{{{Constants.AzureWebJobsStorage}}}", Constants.StorageEmulatorConnectionString);
            }

            JObject localObj = JsonConvert.DeserializeObject<JObject>(baseLocalSettings);
            JObject values = localObj["Values"] as JObject ?? [];

            // Determine moniker for default; if existing runtime present, do not overwrite unless force is true
            if (!values.TryGetValue(Constants.FunctionsWorkerRuntime, StringComparison.OrdinalIgnoreCase, out _) || force)
            {
                string runtimeMoniker = WorkerRuntimeLanguageHelper.GetRuntimeMoniker(workerRuntime);
                values[Constants.FunctionsWorkerRuntime] = runtimeMoniker;
                changed = true;
            }

            // Handle AzureWebJobsFeatureFlags - append if exists and force is enabled, create if not
            bool azureWebJobsFeatureFlagsExists = values.TryGetValue(Constants.AzureWebJobsFeatureFlags, StringComparison.OrdinalIgnoreCase, out var existingFlagsToken);
            if (azureWebJobsFeatureFlagsExists && force)
            {
                string existingFlags = existingFlagsToken?.ToString() ?? string.Empty;

                // Split by comma and trim whitespace
                var flagsList = existingFlags
                    .Split(',')
                    .Select(f => f.Trim())
                    .Where(f => !string.IsNullOrWhiteSpace(f))
                    .ToList();

                // Add the MCP feature flag if it's not already present
                if (!flagsList.Contains(McpFeatureFlag, StringComparer.OrdinalIgnoreCase))
                {
                    flagsList.Add(McpFeatureFlag);

                    // Rejoin with comma and space
                    values["AzureWebJobsFeatureFlags"] = string.Join(",", flagsList);
                    changed = true;
                }
            }
            else if (!azureWebJobsFeatureFlagsExists)
            {
                // No existing feature flags, create with just our flag
                values["AzureWebJobsFeatureFlags"] = McpFeatureFlag;
                changed = true;
                SetupProgressLogger.Warn("local.settings.json", $"Added feature flag '{McpFeatureFlag}'");
            }

            if (changed)
            {
                localObj["Values"] = values;
                string localContent = JsonConvert.SerializeObject(localObj, Formatting.Indented);
                await FileSystemHelpers.WriteAllTextToFileAsync(localSettingsPath, localContent);
                SetupProgressLogger.Ok("local.settings.json", "Updated settings");
            }
            else
            {
                SetupProgressLogger.Warn("local.settings.json", "Already configured (use --force to overwrite)");
            }
        }
    }
}
