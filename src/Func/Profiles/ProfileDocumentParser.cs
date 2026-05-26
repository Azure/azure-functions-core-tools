// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Text.Json;
using NuGet.Versioning;

namespace Azure.Functions.Cli.Profiles;

/// <summary>
/// Parses and validates profile JSON documents.
/// </summary>
internal sealed class ProfileDocumentParser
{
    public ProfileSourceSnapshot ParseBuiltInRegistry(string json, ProfileSourceInfo source)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        ArgumentNullException.ThrowIfNull(source);

        BuiltInProfileRegistryDocument document = Deserialize(
            json,
            ProfileJsonContext.Default.BuiltInProfileRegistryDocument,
            source.DisplayName);

        ValidateSchema(document.Schema, ProfileSchemas.BuiltInRegistryV1, source.DisplayName, schemaRequired: true);

        IReadOnlyDictionary<string, ProfileDefinition> normalizedProfiles = NormalizeProfiles(document.Profiles, source.DisplayName);

        return new ProfileSourceSnapshot(source, normalizedProfiles, document.GeneratedAt);
    }

    public ProfileSourceSnapshot ParseCustomProfiles(string json, ProfileSourceInfo source)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        ArgumentNullException.ThrowIfNull(source);

        try
        {
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions
            {
                AllowTrailingCommas = true,
                CommentHandling = JsonCommentHandling.Skip,
            });

            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                throw new ProfileConfigurationException($"Profile document '{source.DisplayName}' must contain a JSON object.");
            }

            string? schema = null;
            Dictionary<string, ProfileDefinition> profiles = new(StringComparer.OrdinalIgnoreCase);
            foreach (JsonProperty property in document.RootElement.EnumerateObject())
            {
                if (property.NameEquals("$schema"))
                {
                    schema = property.Value.GetString();
                    continue;
                }

                if (property.Name.StartsWith('$'))
                {
                    continue;
                }

                ProfileDefinition definition = Deserialize(
                    property.Value.GetRawText(),
                    ProfileJsonContext.Default.ProfileDefinition,
                    source.DisplayName);

                AddProfile(profiles, property.Name, definition, source.DisplayName);
            }

            ValidateSchema(schema, ProfileSchemas.CustomProfilesV1, source.DisplayName, schemaRequired: false);
            ValidateProfiles(profiles, source.DisplayName);

            return new ProfileSourceSnapshot(source, profiles);
        }
        catch (JsonException ex)
        {
            throw new ProfileConfigurationException($"Profile document '{source.DisplayName}' is not valid JSON: {ex.Message}", ex);
        }
    }

    private static TDocument Deserialize<TDocument>(
        string json,
        System.Text.Json.Serialization.Metadata.JsonTypeInfo<TDocument> typeInfo,
        string displayName)
    {
        try
        {
            TDocument? document = JsonSerializer.Deserialize(json, typeInfo);
            return document ?? throw new ProfileConfigurationException($"Profile document '{displayName}' is empty.");
        }
        catch (JsonException ex)
        {
            throw new ProfileConfigurationException($"Profile document '{displayName}' is not valid JSON: {ex.Message}", ex);
        }
    }

    private static IReadOnlyDictionary<string, ProfileDefinition> NormalizeProfiles(
        Dictionary<string, ProfileDefinition> profiles,
        string displayName)
    {
        Dictionary<string, ProfileDefinition> normalized = new(StringComparer.OrdinalIgnoreCase);
        foreach ((string name, ProfileDefinition definition) in profiles)
        {
            AddProfile(normalized, name, definition, displayName);
        }

        ValidateProfiles(normalized, displayName);
        return normalized;
    }

    private static void AddProfile(
        Dictionary<string, ProfileDefinition> profiles,
        string name,
        ProfileDefinition definition,
        string displayName)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ProfileConfigurationException($"Profile document '{displayName}' contains an empty profile name.");
        }

        if (!profiles.TryAdd(name, definition))
        {
            throw new ProfileConfigurationException($"Profile document '{displayName}' defines profile '{name}' more than once.");
        }
    }

    private static void ValidateProfiles(IReadOnlyDictionary<string, ProfileDefinition> profiles, string displayName)
    {
        foreach ((string name, ProfileDefinition definition) in profiles)
        {
            ValidateProfile(name, definition, displayName);
        }
    }

    private static void ValidateProfile(string name, ProfileDefinition profile, string displayName)
    {
        if (profile.Status is { } status)
        {
            ParseStatus(status, name, displayName);
        }

        if (profile.Host is { } host)
        {
            ValidateVersionRange(host.Version, name, "host.version", displayName);
        }

        if (profile.ExtensionBundle is { } extensionBundle)
        {
            ValidateVersionRange(extensionBundle.Version, name, "extensionBundle.version", displayName);
        }

        if (profile.Workers is { } workers)
        {
            foreach ((string runtime, ProfileWorkerConstraint? constraint) in workers)
            {
                ValidateIdentifier(runtime, name, "workers", displayName);
                if (constraint is not null)
                {
                    ValidateVersionRange(constraint.Version, name, $"workers.{runtime}.version", displayName);
                }
            }
        }

        if (profile.SupportedRuntimes is { } runtimes)
        {
            foreach (string runtime in runtimes)
            {
                ValidateIdentifier(runtime, name, "supportedRuntimes", displayName);
            }
        }
    }

    internal static ProfileStatus ParseStatus(string status, string profileName, string displayName)
        => status.Trim().ToLowerInvariant() switch
        {
            "stable" => ProfileStatus.Stable,
            "preview" => ProfileStatus.Preview,
            "deprecated" => ProfileStatus.Deprecated,
            _ => throw new ProfileConfigurationException(
                $"Profile '{profileName}' in '{displayName}' has unsupported status '{status}'. "
                + "Supported values are stable, preview, and deprecated."),
        };

    private static void ValidateVersionRange(string? range, string profileName, string propertyName, string displayName)
    {
        if (string.IsNullOrWhiteSpace(range))
        {
            throw new ProfileConfigurationException($"Profile '{profileName}' in '{displayName}' must define '{propertyName}'.");
        }

        if (!VersionRange.TryParse(range, out _))
        {
            throw new ProfileConfigurationException(
                $"Profile '{profileName}' in '{displayName}' has invalid NuGet version range '{range}' for '{propertyName}'.");
        }
    }

    private static void ValidateIdentifier(string value, string profileName, string propertyName, string displayName)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Any(char.IsWhiteSpace))
        {
            throw new ProfileConfigurationException(
                $"Profile '{profileName}' in '{displayName}' has invalid '{propertyName}' entry '{value}'.");
        }
    }

    private static void ValidateSchema(string? schema, string supportedSchema, string displayName, bool schemaRequired)
    {
        if (string.IsNullOrWhiteSpace(schema))
        {
            if (schemaRequired)
            {
                throw new ProfileConfigurationException($"Profile document '{displayName}' must declare schema '{supportedSchema}'.");
            }

            return;
        }

        if (!string.Equals(schema, supportedSchema, StringComparison.OrdinalIgnoreCase))
        {
            throw new ProfileConfigurationException(
                $"Profile document '{displayName}' uses unsupported schema '{schema}'. "
                + $"Supported schema is '{supportedSchema}'. Run 'func upgrade'.");
        }
    }

}
