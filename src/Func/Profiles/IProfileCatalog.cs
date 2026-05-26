// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Profiles;

/// <summary>
/// Provides access to profile definitions and inherited profile resolution.
/// </summary>
internal interface IProfileCatalog
{
    /// <summary>
    /// Loads all configured profile sources for a working directory.
    /// </summary>
    public Task<IReadOnlyList<ProfileSourceSnapshot>> LoadAsync(
        ProfileSourceContext context,
        CancellationToken cancellationToken);

    /// <summary>
    /// Returns the first visible definition for each profile name from the supplied snapshots.
    /// </summary>
    public IReadOnlyList<ProfileDefinitionEntry> ListEffectiveProfiles(
        IReadOnlyList<ProfileSourceSnapshot> snapshots,
        IReadOnlySet<ProfileSourceKind>? sourceKinds = null);

    /// <summary>
    /// Finds the first profile definition matching the supplied name.
    /// </summary>
    public ProfileDefinitionEntry? FindProfile(string name, IReadOnlyList<ProfileSourceSnapshot> snapshots);

    /// <summary>
    /// Resolves a named profile from the supplied snapshots.
    /// </summary>
    /// <exception cref="ProfileConfigurationException">
    /// Thrown when the profile cannot be found or its inherited definition is invalid.
    /// </exception>
    public ResolvedProfile ResolveProfile(string name, IReadOnlyList<ProfileSourceSnapshot> snapshots);

    /// <summary>
    /// Resolves a specific profile definition from the supplied snapshots.
    /// </summary>
    /// <exception cref="ProfileConfigurationException">
    /// Thrown when the profile's inherited definition is invalid.
    /// </exception>
    public ResolvedProfile ResolveProfile(
        ProfileDefinitionEntry entry,
        IReadOnlyList<ProfileSourceSnapshot> snapshots);
}
