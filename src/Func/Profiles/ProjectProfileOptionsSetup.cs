// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace Azure.Functions.Cli.Profiles;

/// <summary>
/// Binds and validates project-owned profile selection settings.
/// </summary>
internal sealed class ProjectProfileOptionsSetup(IConfiguration configuration, ICliConfigurationProvider? configurationProvider = null)
    : IConfigureNamedOptions<ProjectProfileOptions>
{
    private static readonly string _projectConfigDisplayName = Path.Combine(
        CliConfigurationNames.ProjectConfigFolderName,
        CliConfigurationNames.ConfigFileName);

    private readonly IConfiguration _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
    private readonly ICliConfigurationProvider? _configurationProvider = configurationProvider;

    public void Configure(ProjectProfileOptions options)
        => Configure(Options.DefaultName, options);

    public void Configure(string? name, ProjectProfileOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        GetConfiguration(name).Bind(options);
        ValidateSchema(options.Schema);
        NormalizeProfiles(options);
        options.DefaultProfile = NullIfWhiteSpace(options.DefaultProfile);
    }

    private static void ValidateSchema(string? schema)
    {
        if (string.IsNullOrWhiteSpace(schema))
        {
            return;
        }

        if (!string.Equals(schema, ProfileSchemas.ProjectConfigV1, StringComparison.OrdinalIgnoreCase))
        {
            throw new ProfileConfigurationException(
                $"Project config '{_projectConfigDisplayName}' uses unsupported schema '{schema}'. "
                + $"Expected '{ProfileSchemas.ProjectConfigV1}'.");
        }
    }

    private static void NormalizeProfiles(ProjectProfileOptions options)
    {
        List<string> profiles = [];
        HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);
        foreach (string? profile in options.Profiles)
        {
            if (string.IsNullOrWhiteSpace(profile))
            {
                throw new ProfileConfigurationException($"Project config '{_projectConfigDisplayName}' contains an empty profile name.");
            }

            string normalized = profile.Trim();
            if (!seen.Add(normalized))
            {
                string message = $"Project config '{_projectConfigDisplayName}' declares profile '{normalized}' more than once.";
                throw new ProfileConfigurationException(message);
            }

            profiles.Add(normalized);
        }

        options.Profiles = profiles;
    }

    private IConfiguration GetConfiguration(string? name)
        => string.IsNullOrEmpty(name) || _configurationProvider is null
            ? _configuration
            : _configurationProvider.GetProjectConfiguration(new DirectoryInfo(name));

    private static string? NullIfWhiteSpace(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
