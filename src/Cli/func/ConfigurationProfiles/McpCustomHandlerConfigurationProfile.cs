// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Helpers;
using Colors.Net;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Azure.Functions.Cli.ConfigurationProfiles
{
    internal class McpCustomHandlerConfigurationProfile : IConfigurationProfile
    {
        public string Name { get; set; } = "mcp-custom-handler";

        public async Task ApplyAsync(WorkerRuntime workerRuntime, bool shouldForce = false)
        {
            await ApplyHostJsonAsync(shouldForce);
            await ApplyLocalSettingsAsync(workerRuntime, shouldForce);
        }

        public async Task ApplyHostJsonAsync(bool shouldForce)
        {
            // Check if host.json exists and read it, otherwise use the default template
            var hostJsonPath = Path.Combine(Environment.CurrentDirectory, Constants.HostJsonFileName);
            var hostExists = FileSystemHelpers.FileExists(hostJsonPath);
            string baseHostJson;

            JObject hostJsonObj;
            if (hostExists)
            {
                ColoredConsole.WriteLine($"Applying MCP custom handler configuration profile to existing {hostJsonPath}...");
                baseHostJson = await FileSystemHelpers.ReadAllTextFromFileAsync(hostJsonPath);
            }
            else
            {
                baseHostJson = await StaticResources.HostJson;
            }

            hostJsonObj = JsonConvert.DeserializeObject<JObject>(baseHostJson);

            var changed = false;

            // Add configurationProfile if missing or if shouldForce is true
            if (!hostJsonObj.TryGetValue("configurationProfile", StringComparison.OrdinalIgnoreCase, out _) || shouldForce)
            {
                hostJsonObj["configurationProfile"] = "mcp-custom-handler";
                changed = true;
            }

            // Add customHandler section if missing or if shouldForce is true
            if (!hostJsonObj.TryGetValue("customHandler", StringComparison.OrdinalIgnoreCase, out _) || shouldForce)
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
                var hostJsonContent = JsonConvert.SerializeObject(hostJsonObj, Formatting.Indented);
                await FileSystemHelpers.WriteAllTextToFileAsync(hostJsonPath, hostJsonContent);
            }

            ColoredConsole.WriteLine(changed
                ? "Updated host.json with MCP configuration profile."
                : "host.json already contains MCP configuration profile. Please pass in `--force` to overwrite.\n");
        }

        public async Task ApplyLocalSettingsAsync(WorkerRuntime workerRuntime, bool shouldForce)
        {
            // Check if local.settings.json exists and read it, otherwise use the default template
            var localSettingsPath = Path.Combine(Environment.CurrentDirectory, "local.settings.json");
            var localExists = FileSystemHelpers.FileExists(localSettingsPath);
            string baseLocalSettings;

            JObject localObj;
            if (localExists)
            {
                ColoredConsole.WriteLine($"Applying MCP custom handler configuration profile to existing {localSettingsPath}...");
                baseLocalSettings = await FileSystemHelpers.ReadAllTextFromFileAsync(localSettingsPath);
            }
            else
            {
                baseLocalSettings = await StaticResources.LocalSettingsJson;

                // Replace placeholders in the template
                baseLocalSettings = baseLocalSettings.Replace($"{{{Constants.FunctionsWorkerRuntime}}}", WorkerRuntimeLanguageHelper.GetRuntimeMoniker(workerRuntime));
                baseLocalSettings = baseLocalSettings.Replace($"{{{Constants.AzureWebJobsStorage}}}", Constants.StorageEmulatorConnectionString);
            }

            localObj = JsonConvert.DeserializeObject<JObject>(baseLocalSettings);

            var changed = false;
            var values = localObj["Values"] as JObject ?? new JObject();
            var runtimeKey = Constants.FunctionsWorkerRuntime;

            // Determine moniker for default; if existing runtime present, do not overwrite unless shouldForce is true
            if (!values.TryGetValue(runtimeKey, StringComparison.OrdinalIgnoreCase, out _) || shouldForce)
            {
                var runtimeMoniker = WorkerRuntimeLanguageHelper.GetRuntimeMoniker(workerRuntime);

                values[runtimeKey] = runtimeMoniker;
                changed = true;
            }

            // Handle AzureWebJobsFeatureFlags - append if exists and shouldForce is enabled, create if not
            const string mcpFeatureFlag = "EnableMcpCustomHandlerPreview";
            var azureWebJobsFeatureFlagsExists = values.TryGetValue("AzureWebJobsFeatureFlags", StringComparison.OrdinalIgnoreCase, out var existingFlagsToken);
            if (azureWebJobsFeatureFlagsExists && shouldForce)
            {
                var existingFlags = existingFlagsToken?.ToString() ?? string.Empty;

                // Split by comma and trim whitespace
                var flagsList = existingFlags
                    .Split(',')
                    .Select(f => f.Trim())
                    .Where(f => !string.IsNullOrWhiteSpace(f))
                    .ToList();

                // Add the MCP feature flag if it's not already present
                if (!flagsList.Contains(mcpFeatureFlag, StringComparer.OrdinalIgnoreCase))
                {
                    flagsList.Add(mcpFeatureFlag);
                }

                // Rejoin with comma and space
                values["AzureWebJobsFeatureFlags"] = string.Join(",", flagsList);
                changed = true;
            }
            else if (!azureWebJobsFeatureFlagsExists)
            {
                // No existing feature flags, create with just our flag
                values["AzureWebJobsFeatureFlags"] = mcpFeatureFlag;
                changed = true;
            }

            if (changed)
            {
                localObj["Values"] = values;
                var localContent = JsonConvert.SerializeObject(localObj, Formatting.Indented);
                await FileSystemHelpers.WriteAllTextToFileAsync(localSettingsPath, localContent);
            }

            ColoredConsole.WriteLine(changed
                ? "Updated local.settings.json with MCP configuration profile."
                : "local.settings.json already contains MCP configuration profile. Please pass in `--force` to overwrite.\n");
        }
    }
}
