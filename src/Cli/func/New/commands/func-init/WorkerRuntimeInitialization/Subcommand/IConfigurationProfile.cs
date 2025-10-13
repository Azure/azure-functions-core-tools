// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.CommandLine;
using System.Text.Json;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Helpers;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Spectre.Console;

namespace Azure.Functions.Cli.Commands.Init;

public interface IConfigurationProfile
{
    public string Name { get; }

    public Task ApplyAsync(WorkerRuntime workerRuntime, CancellationToken ct);
}

public class McpConfigurationProfile : IConfigurationProfile
{
    public string Name { get; } = "mcp-custom-handler";

    public async Task ApplyAsync(WorkerRuntime workerRuntime, CancellationToken ct)
    {
        await UpdateHostJson(false);
        await UpdateLocalSettingsJson(workerRuntime, false);
    }

    public async Task UpdateHostJson(bool shouldForce)
    {
        var hostJsonPath = Path.Combine(Environment.CurrentDirectory, Constants.HostJsonFileName);
        string baseHostJson;
        if (FileSystemHelpers.FileExists(hostJsonPath))
        {
            AnsiConsole.WriteLine($"Applying MCP custom handler configuration profile to existing {hostJsonPath}...");
            baseHostJson = await FileSystemHelpers.ReadAllTextFromFileAsync(hostJsonPath);
        }
        else
        {
            AnsiConsole.WriteLine($"Creating new {hostJsonPath} with MCP custom handler configuration profile...");
            baseHostJson = await StaticResources.HostJson;
        }

        JObject hostJsonObj = JsonConvert.DeserializeObject<JObject>(baseHostJson);

        var changed = false;

        // Add configurationProfile if missing or if shouldForce is true
        if (!hostJsonObj.TryGetValue("configurationProfile", StringComparison.OrdinalIgnoreCase, out _) || shouldForce)
        {
            hostJsonObj["configurationProfile"] = "mcp-custom-handler";
            changed = true;
        }

        // Add customHandler section if missing or if shouldForce is true
        if (!hostJsonObj.TryGetValue("customHandler", StringComparison.OrdinalIgnoreCase, out var customHandlerToken) || shouldForce)
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

        AnsiConsole.WriteLine(changed
            ? "Updated host.json with MCP configuration profile."
            : "host.json already contains MCP configuration profile. Please pass in `--force` to overwrite.\n");
    }

    public async Task UpdateLocalSettingsJson(WorkerRuntime workerRuntime, bool shouldForce)
    {
        var baseLocalSettings = string.Empty;
        var localSettingsPath = Path.Combine(Environment.CurrentDirectory, "local.settings.json");
        if (FileSystemHelpers.FileExists(localSettingsPath))
        {
            AnsiConsole.WriteLine($"Applying MCP custom handler configuration profile to existing {localSettingsPath}...");
            baseLocalSettings = await FileSystemHelpers.ReadAllTextFromFileAsync(localSettingsPath);
        }
        else
        {
            AnsiConsole.WriteLine($"Creating new {localSettingsPath} with MCP custom handler configuration profile...");
            baseLocalSettings = await StaticResources.LocalSettingsJson;
        }

        JObject localObj = JsonConvert.DeserializeObject<JObject>(baseLocalSettings);

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
        var valueExists = values.TryGetValue("AzureWebJobsFeatureFlags", StringComparison.OrdinalIgnoreCase, out var existingFlagsToken);
        if (shouldForce && valueExists)
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
        else if (!valueExists)
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

        AnsiConsole.WriteLine(changed
            ? "Updated local.settings.json with MCP configuration profile."
            : "local.settings.json already contains MCP configuration profile. Please pass in `--force` to overwrite.\n");
    }
}
