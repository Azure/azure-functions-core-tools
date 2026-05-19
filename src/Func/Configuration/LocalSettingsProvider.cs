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
                return LocalSettingsSnapshot.Empty;
            }

            return new LocalSettingsSnapshot
            {
                Values = ReadValues(document.RootElement),
                Host = ReadHost(document.RootElement),
            };
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            return LocalSettingsSnapshot.Empty;
        }
    }

    private static IReadOnlyDictionary<string, string> ReadValues(JsonElement root)
    {
        if (!root.TryGetProperty(CliConfigurationNames.LocalSettingsValuesSectionName, out JsonElement values)
            || values.ValueKind != JsonValueKind.Object)
        {
            return LocalSettingsSnapshot.Empty.Values;
        }

        Dictionary<string, string> result = new(StringComparer.OrdinalIgnoreCase);
        foreach (JsonProperty property in values.EnumerateObject())
        {
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
