// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Text.Json;
using System.Text.Json.Nodes;

namespace Azure.Functions.Cli.Configuration;

/// <summary>
/// Serializes project initialization metadata without owning filesystem effects.
/// </summary>
internal static class ProjectConfigurationJson
{
    public static string Create(string? runtime, string? language)
    {
        Dictionary<string, string> stack = new(StringComparer.Ordinal);
        if (!string.IsNullOrWhiteSpace(runtime))
        {
            stack[CliConfigurationNames.StackRuntimeKey] = runtime;
        }

        if (!string.IsNullOrWhiteSpace(language))
        {
            stack[CliConfigurationNames.StackLanguageKey] = language;
        }

        Dictionary<string, object> payload = new(StringComparer.Ordinal);
        if (stack.Count > 0)
        {
            payload[CliConfigurationNames.StackSectionName] = stack;
        }

        return JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true });
    }

    public static string MergeLanguage(string existing, string language, string? canonicalRuntime = null)
    {
        ArgumentNullException.ThrowIfNull(existing);
        ArgumentException.ThrowIfNullOrWhiteSpace(language);
        JsonObject root = JsonNode.Parse(existing) as JsonObject
            ?? throw new JsonException("Project configuration must contain a JSON object.");
        JsonObject stack = root[CliConfigurationNames.StackSectionName] as JsonObject
            ?? throw new JsonException("Project configuration must contain a stack object.");
        stack[CliConfigurationNames.StackLanguageKey] = language;
        if (canonicalRuntime is not null)
        {
            stack[CliConfigurationNames.StackRuntimeKey] = canonicalRuntime;
        }

        return root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }
}
