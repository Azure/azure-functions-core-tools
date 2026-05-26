// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Text.Json;
using System.Text.Json.Nodes;
using Azure.Functions.Cli.Configuration;

namespace Azure.Functions.Cli.Profiles;

/// <summary>
/// Default project profile config store.
/// </summary>
internal sealed class ProjectProfileConfigStore(IProfileFileSystem fileSystem) : IProjectProfileConfigStore
{
    private static readonly string _projectConfigDisplayName = CliConfigurationPathsOptions.ProjectConfigDisplayPath;

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
    };

    private readonly IProfileFileSystem _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));

    public async Task<ProjectProfileConfigUpdateResult> SetDefaultProfileAsync(DirectoryInfo projectDirectory, string profileName, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(projectDirectory);
        string normalizedProfileName = NormalizeProfileName(profileName);
        string configPath = CliConfigurationPathsOptions.GetProjectConfigPath(projectDirectory);

        string? json = await _fileSystem.ReadAllTextIfExistsAsync(configPath, cancellationToken);
        JsonObject root = ParseProjectConfig(json);
        List<string> profiles = ReadProfiles(root);
        string selectedProfile = EnsureProfile(profiles, normalizedProfileName, out bool addedProfile);

        root["profiles"] = CreateProfilesArray(profiles);
        root["defaultProfile"] = selectedProfile;

        string updatedJson = root.ToJsonString(_jsonOptions) + Environment.NewLine;
        await WriteProjectConfigAsync(configPath, updatedJson, cancellationToken);

        return new ProjectProfileConfigUpdateResult(selectedProfile, configPath, addedProfile, profiles);
    }

    private async Task WriteProjectConfigAsync(string configPath, string contents, CancellationToken cancellationToken)
    {
        try
        {
            await _fileSystem.WriteAllTextAsync(configPath, contents, cancellationToken);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new ProfileConfigurationException($"Could not write project config '{_projectConfigDisplayName}': {ex.Message}", ex);
        }
    }

    private static JsonObject ParseProjectConfig(string? json)
    {
        if (json is null)
        {
            throw new ProfileConfigurationException(
                $"Project not initialized. Run 'func init' to create a Functions project before setting a profile.");
        }

        try
        {
            var node = JsonNode.Parse(json);
            if (node is JsonObject root)
            {
                ValidateSchema(root);
                return root;
            }
        }
        catch (JsonException ex)
        {
            throw new ProfileConfigurationException($"Project config '{_projectConfigDisplayName}' contains invalid JSON.", ex);
        }

        throw new ProfileConfigurationException($"Project config '{_projectConfigDisplayName}' must contain a JSON object.");
    }

    private static void ValidateSchema(JsonObject root)
    {
        if (root["$schema"] is null)
        {
            return;
        }

        if (root["$schema"] is JsonValue schemaValue
            && schemaValue.TryGetValue(out string? schema)
            && !string.IsNullOrWhiteSpace(schema))
        {
            if (!string.Equals(schema, ProfileSchemas.ProjectConfigV1, StringComparison.OrdinalIgnoreCase))
            {
                throw new ProfileConfigurationException(
                    $"Project config '{_projectConfigDisplayName}' uses unsupported schema '{schema}'. "
                    + $"Expected '{ProfileSchemas.ProjectConfigV1}'.");
            }

            return;
        }

        throw new ProfileConfigurationException($"Project config '{_projectConfigDisplayName}' has an invalid '$schema' value.");
    }

    private static List<string> ReadProfiles(JsonObject root)
    {
        if (root["profiles"] is null)
        {
            return [];
        }

        if (root["profiles"] is not JsonArray profilesArray)
        {
            throw new ProfileConfigurationException($"Project config '{_projectConfigDisplayName}' property 'profiles' must be an array.");
        }

        List<string> profiles = [];
        HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);
        foreach (JsonNode? item in profilesArray)
        {
            if (item is not JsonValue profileValue
                || !profileValue.TryGetValue(out string? profile)
                || string.IsNullOrWhiteSpace(profile))
            {
                throw new ProfileConfigurationException($"Project config '{_projectConfigDisplayName}' contains an invalid profile name.");
            }

            string normalized = profile.Trim();
            if (!seen.Add(normalized))
            {
                throw new ProfileConfigurationException($"Project config '{_projectConfigDisplayName}' declares profile '{normalized}' more than once.");
            }

            profiles.Add(normalized);
        }

        return profiles;
    }

    private static string EnsureProfile(List<string> profiles, string profileName, out bool addedProfile)
    {
        string? existingProfile = profiles.FirstOrDefault(p => string.Equals(p, profileName, StringComparison.OrdinalIgnoreCase));
        if (existingProfile is not null)
        {
            addedProfile = false;
            return existingProfile;
        }

        profiles.Add(profileName);
        addedProfile = true;
        return profileName;
    }

    private static JsonArray CreateProfilesArray(IEnumerable<string> profiles)
    {
        JsonArray array = [];
        foreach (string profile in profiles)
        {
            array.Add(profile);
        }

        return array;
    }

    private static string NormalizeProfileName(string profileName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(profileName);
        return profileName.Trim();
    }
}
