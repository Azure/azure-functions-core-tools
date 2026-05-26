// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Collections.Concurrent;
using Azure.Functions.Cli.Common;
using Microsoft.Extensions.Configuration;

namespace Azure.Functions.Cli.Configuration;

/// <summary>
/// Builds and caches scoped CLI configuration roots.
/// </summary>
internal sealed class CliConfigurationProvider(ILocalSettingsProvider localSettingsProvider, CliConfigurationPathsOptions configurationPaths)
    : ICliConfigurationProvider
{
    private readonly ILocalSettingsProvider _localSettingsProvider = localSettingsProvider ?? throw new ArgumentNullException(nameof(localSettingsProvider));
    private readonly CliConfigurationPathsOptions _configurationPaths = configurationPaths ?? throw new ArgumentNullException(nameof(configurationPaths));
    private readonly ConcurrentDictionary<string, IConfigurationRoot> _projectConfigurations = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, IConfigurationRoot> _effectiveConfigurations = new(StringComparer.OrdinalIgnoreCase);
    private IConfigurationRoot? _userConfiguration;
    private readonly Lock _userConfigurationLock = new();

    public CliConfigurationProvider(ILocalSettingsProvider localSettingsProvider)
        : this(localSettingsProvider, new CliConfigurationPathsOptions())
    {
    }

    internal CliConfigurationProvider(ILocalSettingsProvider localSettingsProvider, DirectoryInfo userConfigurationDirectory)
        : this(localSettingsProvider, CreateConfigurationPaths(userConfigurationDirectory))
    {
    }

    public IConfiguration GetUserConfiguration()
    {
        if (_userConfiguration is { } configuration)
        {
            return configuration;
        }

        lock (_userConfigurationLock)
        {
            _userConfiguration ??= BuildUserConfiguration(_configurationPaths);

            return _userConfiguration;
        }
    }

    public IConfiguration GetProjectConfiguration(DirectoryInfo projectDirectory)
    {
        ArgumentNullException.ThrowIfNull(projectDirectory);

        string key = NormalizeProjectDirectory(projectDirectory);
        DirectoryInfo normalizedProjectDirectory = new(key);

        (CliConfigurationProvider Provider, DirectoryInfo ProjectDirectory) state = (this, normalizedProjectDirectory);
        return _projectConfigurations.GetOrAdd(key, static (_, state) => state.Provider.BuildProjectConfiguration(state.ProjectDirectory), state);
    }

    public IConfiguration GetEffectiveConfiguration(DirectoryInfo projectDirectory)
    {
        ArgumentNullException.ThrowIfNull(projectDirectory);

        string key = NormalizeProjectDirectory(projectDirectory);
        DirectoryInfo normalizedProjectDirectory = new(key);

        (CliConfigurationProvider Provider, DirectoryInfo ProjectDirectory) state = (this, normalizedProjectDirectory);
        return _effectiveConfigurations.GetOrAdd(key, static (_, state) => state.Provider.BuildEffectiveConfiguration(state.ProjectDirectory), state);
    }

    private static IConfigurationRoot BuildUserConfiguration(CliConfigurationPathsOptions configurationPaths)
    {
        var builder = new ConfigurationBuilder();
        builder.AddEnvironmentVariables(prefix: Constants.EnvironmentVariablePrefix);
        builder.AddJsonFile(configurationPaths.ConfigPath, optional: true, reloadOnChange: false);

        return builder.Build();
    }

    private IConfigurationRoot BuildProjectConfiguration(DirectoryInfo projectDirectory)
    {
        var builder = new ConfigurationBuilder();
        builder.Add(new LocalSettingsConfigurationSource(projectDirectory, _localSettingsProvider));
        builder.AddJsonFile(CliConfigurationPathsOptions.GetProjectConfigPath(projectDirectory), optional: true, reloadOnChange: false);

        return builder.Build();
    }

    private IConfigurationRoot BuildEffectiveConfiguration(DirectoryInfo projectDirectory)
    {
        var builder = new ConfigurationBuilder();
        builder.AddConfiguration(GetUserConfiguration());
        builder.AddConfiguration(GetProjectConfiguration(projectDirectory));

        return builder.Build();
    }

    private static CliConfigurationPathsOptions CreateConfigurationPaths(DirectoryInfo userConfigurationDirectory)
    {
        ArgumentNullException.ThrowIfNull(userConfigurationDirectory);
        return new CliConfigurationPathsOptions(userConfigurationDirectory.FullName);
    }

    private static string NormalizeProjectDirectory(DirectoryInfo projectDirectory)
        => Path.GetFullPath(projectDirectory.FullName);
}
