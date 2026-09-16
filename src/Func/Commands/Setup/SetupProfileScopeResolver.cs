// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Profiles;
using Microsoft.Extensions.Options;

namespace Azure.Functions.Cli.Commands.Setup;

internal interface ISetupProfileScopeResolver
{
    /// <summary>
    /// Resolves the profile scopes setup should run against, in priority order:
    /// explicit <c>--profile</c> values, then project-declared profiles, then the
    /// user's default profile, then a single unconstrained scope.
    /// </summary>
    public Task<IReadOnlyList<SetupProfileScope>> ResolveProfileScopesAsync(
        SetupCommandOptions options,
        SetupRenderer renderer,
        CancellationToken cancellationToken);
}

internal sealed class SetupProfileScopeResolver(
    IProfileCatalog profileCatalog,
    IOptionsMonitor<ProjectProfileOptions> projectProfileOptions,
    IOptionsMonitor<UserProfilePreferenceOptions> userProfilePreferenceOptions) : ISetupProfileScopeResolver
{
    private readonly IProfileCatalog _profileCatalog = profileCatalog ?? throw new ArgumentNullException(nameof(profileCatalog));
    private readonly IOptionsMonitor<ProjectProfileOptions> _projectProfileOptions = projectProfileOptions ?? throw new ArgumentNullException(nameof(projectProfileOptions));
    private readonly IOptionsMonitor<UserProfilePreferenceOptions> _userProfilePreferenceOptions = userProfilePreferenceOptions ?? throw new ArgumentNullException(nameof(userProfilePreferenceOptions));

    public async Task<IReadOnlyList<SetupProfileScope>> ResolveProfileScopesAsync(
        SetupCommandOptions options,
        SetupRenderer renderer,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(renderer);

        string projectDirectory = Path.GetFullPath(options.WorkingDirectory.FullName);
        ProjectProfileOptions projectOptions = _projectProfileOptions.Get(projectDirectory);
        IReadOnlyList<string> explicitProfiles = NormalizeDistinct(options.ProfileNames);

        if (explicitProfiles.Count > 0)
        {
            IReadOnlyList<ProfileSourceSnapshot> snapshots = await _profileCatalog.LoadAsync(new ProfileSourceContext(options.WorkingDirectory), cancellationToken);
            List<SetupProfileScope> scopes = [];
            foreach (string profileName in explicitProfiles)
            {
                if (projectOptions.Profiles.Count > 0 && !IsDeclaredProfile(projectOptions, profileName))
                {
                    renderer.Warning($"Profile '{profileName}' is not declared in this project's .func/config.json.");
                }

                scopes.Add(CreateProfileScope(profileName, snapshots, renderer));
            }

            return scopes;
        }

        if (projectOptions.Profiles.Count > 0)
        {
            IReadOnlyList<ProfileSourceSnapshot> snapshots = await _profileCatalog.LoadAsync(new ProfileSourceContext(options.WorkingDirectory), cancellationToken);
            return [.. projectOptions.Profiles.Select(profileName => CreateProfileScope(profileName, snapshots, renderer))];
        }

        string? userDefaultProfile = SetupRuntimes.NullIfWhiteSpace(_userProfilePreferenceOptions.CurrentValue.DefaultProfile);
        if (userDefaultProfile is not null)
        {
            IReadOnlyList<ProfileSourceSnapshot> snapshots = await _profileCatalog.LoadAsync(new ProfileSourceContext(options.WorkingDirectory), cancellationToken);
            return [CreateProfileScope(userDefaultProfile, snapshots, renderer)];
        }

        return [SetupProfileScope.Unconstrained];
    }

    private SetupProfileScope CreateProfileScope(string profileName, IReadOnlyList<ProfileSourceSnapshot> snapshots, SetupRenderer renderer)
    {
        ResolvedProfile profile = _profileCatalog.ResolveProfile(profileName, snapshots);
        if (profile.Status == ProfileStatus.Deprecated)
        {
            string suffix = string.IsNullOrWhiteSpace(profile.DeprecationUrl)
                ? string.Empty
                : $" See {profile.DeprecationUrl}.";

            renderer.Warning($"Profile '{profile.Name}' is deprecated.{suffix}");
        }

        return new SetupProfileScope(profile);
    }

    private static bool IsDeclaredProfile(ProjectProfileOptions projectOptions, string profile)
        => projectOptions.Profiles.Any(candidate => string.Equals(candidate, profile, StringComparison.OrdinalIgnoreCase));

    private static IReadOnlyList<string> NormalizeDistinct(IReadOnlyList<string> values)
    {
        List<string> result = [];
        HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);
        foreach (string value in values)
        {
            string? normalized = SetupRuntimes.NullIfWhiteSpace(value);
            if (normalized is not null && seen.Add(normalized))
            {
                result.Add(normalized);
            }
        }

        return result;
    }
}
