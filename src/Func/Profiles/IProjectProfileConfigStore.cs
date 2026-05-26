// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Profiles;

/// <summary>
/// Persists project-owned profile selection settings.
/// </summary>
internal interface IProjectProfileConfigStore
{
    /// <summary>
    /// Sets the project's default profile, adding it to the declared profile list when needed.
    /// </summary>
    /// <exception cref="ProfileConfigurationException">
    /// Thrown when the project config cannot be parsed or written.
    /// </exception>
    public Task<ProjectProfileConfigUpdateResult> SetDefaultProfileAsync(DirectoryInfo projectDirectory, string profileName, CancellationToken cancellationToken);
}

/// <summary>
/// Describes the project profile config update.
/// </summary>
internal sealed record ProjectProfileConfigUpdateResult(string Profile, string ConfigPath, bool AddedProfile, IReadOnlyList<string> Profiles);
