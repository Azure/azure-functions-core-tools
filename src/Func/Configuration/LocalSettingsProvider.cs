// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json;

namespace Azure.Functions.Cli.Configuration;

internal sealed class LocalSettingsProvider : ILocalSettingsProvider
{
    private static readonly JsonDocumentOptions _documentOptions = new()
    {
        AllowTrailingCommas = true,
        CommentHandling = JsonCommentHandling.Skip,
    };

    private readonly ConcurrentDictionary<string, LocalSettingsSnapshot> _cache = new(StringComparer.OrdinalIgnoreCase);

    public LocalSettingsSnapshot Get(DirectoryInfo projectDirectory)
    {
        ArgumentNullException.ThrowIfNull(projectDirectory);

        string key = Path.GetFullPath(projectDirectory.FullName);
        return _cache.GetOrAdd(key, static (_, directory) => ReadCore(directory), projectDirectory);
    }

    private static LocalSettingsSnapshot ReadCore(DirectoryInfo projectDirectory)
    {
        string path = Path.Combine(projectDirectory.FullName, CliConfigurationNames.LocalSettingsFileName);
        if (!File.Exists(path))
        {
            return LocalSettingsSnapshot.Empty;
        }

        try
        {
            using FileStream stream = File.OpenRead(path);
            using var document = JsonDocument.Parse(stream, _documentOptions);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return new() { WorkerRuntimeReadError = "local.settings.json must contain a JSON object." };
            }

            bool isEncrypted = ReadNullableBoolean(document.RootElement, "IsEncrypted") == true;
            string? runtimeError = null;
            IReadOnlyDictionary<string, string> values = isEncrypted ? LocalSettingsSnapshot.Empty.Values : ReadValues(document.RootElement, out runtimeError);

            return new LocalSettingsSnapshot
            {
                IsEncrypted = isEncrypted,
                Values = values,
                WorkerRuntimeReadError = runtimeError,
                Host = ReadHost(document.RootElement),
            };
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            return new() { WorkerRuntimeReadError = $"Could not read {ProjectStackSettings.WorkerRuntimeSource}: {ex.Message}" };
        }
    }

    private static IReadOnlyDictionary<string, string> ReadValues(JsonElement root, out string? runtimeError)
    {
        runtimeError = null;
        if (!root.TryGetProperty(CliConfigurationNames.LocalSettingsValuesSectionName, out JsonElement values))
        {
            return LocalSettingsSnapshot.Empty.Values;
        }

        if (values.ValueKind != JsonValueKind.Object)
        {
            runtimeError = $"Cannot read {ProjectStackSettings.WorkerRuntimeSource}: 'Values' must be an object.";
            return LocalSettingsSnapshot.Empty.Values;
        }

        Dictionary<string, string> result = [with(StringComparer.OrdinalIgnoreCase)];
        foreach (JsonProperty property in values.EnumerateObject())
        {
            if (string.Equals(property.Name, CliConfigurationNames.WorkerRuntimeSettingName, StringComparison.OrdinalIgnoreCase))
            {
                if (property.Value.ValueKind != JsonValueKind.String)
                {
                    runtimeError = $"{ProjectStackSettings.WorkerRuntimeSource} must be a string, not {property.Value.ValueKind}.";
                }
                else if (result.ContainsKey(property.Name))
                {
                    runtimeError = $"{ProjectStackSettings.WorkerRuntimeSource} is declared more than once.";
                }
            }

            if (property.Value.ValueKind == JsonValueKind.String)
            {
                result[property.Name] = property.Value.GetString() ?? string.Empty;
            }
        }

        return result;
    }

    private static LocalSettingsHostSnapshot? ReadHost(JsonElement root)
    {
        if (!root.TryGetProperty(CliConfigurationNames.LocalSettingsHostSectionName, out JsonElement host)
            || host.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        return new LocalSettingsHostSnapshot
        {
            LocalHttpPort = ReadNullableInt32(host, "LocalHttpPort"),
            Cors = ReadNullableString(host, "CORS"),
            CorsCredentials = ReadNullableBoolean(host, "CORSCredentials"),
        };
    }

    private static int? ReadNullableInt32(JsonElement parent, string propertyName)
    {
        if (!parent.TryGetProperty(propertyName, out JsonElement value))
        {
            return null;
        }

        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out int number))
        {
            return number;
        }

        if (value.ValueKind == JsonValueKind.String
            && int.TryParse(value.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed))
        {
            return parsed;
        }

        return null;
    }

    private static string? ReadNullableString(JsonElement parent, string propertyName)
    {
        if (!parent.TryGetProperty(propertyName, out JsonElement value)
            || value.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        string? raw = value.GetString();
        return string.IsNullOrWhiteSpace(raw) ? null : raw;
    }

    private static bool? ReadNullableBoolean(JsonElement parent, string propertyName)
    {
        if (!parent.TryGetProperty(propertyName, out JsonElement value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.String when bool.TryParse(value.GetString(), out bool parsed) => parsed,
            _ => null,
        };
    }
}
