// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using AbstractionsConstants = Azure.Functions.Cli.Abstractions.Common.Constants;

namespace Azure.Functions.Cli.Configuration;

/// <summary>
/// Computed filesystem layout for CLI configuration.
/// </summary>
internal sealed class CliConfigurationPathsOptions
{
    public const string VersionCacheFileName = ".version-check";
    public const string ProfilesFileName = "profiles.json";

    public CliConfigurationPathsOptions()
        : this(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            AbstractionsConstants.FuncHomeDirectoryName))
    {
    }

    internal CliConfigurationPathsOptions(string home)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(home);
        Home = Path.GetFullPath(home);
    }

    public string Home { get; }

    public string ConfigPath => Path.Combine(Home, CliConfigurationNames.ConfigFileName);

    public string ProfilesPath => Path.Combine(Home, ProfilesFileName);

    public string VersionCachePath => Path.Combine(Home, VersionCacheFileName);

    public static string ProjectConfigDisplayPath => Path.Combine(CliConfigurationNames.ProjectConfigFolderName, CliConfigurationNames.ConfigFileName);

    public static string GetProjectConfigFolderPath(DirectoryInfo projectDirectory)
    {
        ArgumentNullException.ThrowIfNull(projectDirectory);
        return Path.Combine(projectDirectory.FullName, CliConfigurationNames.ProjectConfigFolderName);
    }

    public static string GetProjectConfigPath(DirectoryInfo projectDirectory)
    {
        ArgumentNullException.ThrowIfNull(projectDirectory);
        return Path.Combine(projectDirectory.FullName, ProjectConfigDisplayPath);
    }
}
