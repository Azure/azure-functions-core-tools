// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Console;
using Microsoft.Extensions.Options;
using NuGet.Versioning;

namespace Azure.Functions.Cli.Profiles;

/// <summary>
/// Default active profile resolver.
/// </summary>
internal sealed class ProfileResolver(IEnumerable<IProfileSource> sources, IOptionsMonitor<ProjectProfileOptions> projectProfileOptions,
    IOptionsMonitor<UserProfilePreferenceOptions> userProfilePreferenceOptions, IInteractionService interaction) : IProfileResolver
{
    private const int MaxInheritanceDepth = 5;

    private readonly IReadOnlyList<IProfileSource> _sources = (sources ?? throw new ArgumentNullException(nameof(sources))).ToList();
    private readonly IOptionsMonitor<ProjectProfileOptions> _projectProfileOptions =
        projectProfileOptions ?? throw new ArgumentNullException(nameof(projectProfileOptions));
    private readonly IOptionsMonitor<UserProfilePreferenceOptions> _userProfilePreferenceOptions =
        userProfilePreferenceOptions ?? throw new ArgumentNullException(nameof(userProfilePreferenceOptions));
    private readonly IInteractionService _interaction = interaction ?? throw new ArgumentNullException(nameof(interaction));

    public async Task<ProfileResolution> ResolveAsync(ProfileResolutionContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        string projectDirectory = Path.GetFullPath(context.WorkingDirectory.FullName);
        ProjectProfileOptions projectConfig = _projectProfileOptions.Get(projectDirectory);
        UserProfilePreferenceOptions userPreferences = _userProfilePreferenceOptions.CurrentValue;

        List<ProfileDiagnostic> diagnostics = [];
        string? profileName = await ResolveActiveProfileNameAsync(projectConfig, userPreferences, context, diagnostics, cancellationToken);
        if (profileName is null)
        {
            return new ProfileResolution.None(diagnostics);
        }

        IReadOnlyList<ProfileSourceSnapshot> snapshots = await LoadSourcesAsync(context, cancellationToken);
        ResolvedProfile profile = ResolveProfile(profileName, snapshots);

        if (profile.Status == ProfileStatus.Deprecated)
        {
            string suffix = string.IsNullOrWhiteSpace(profile.DeprecationUrl)
                ? string.Empty
                : $" See {profile.DeprecationUrl}.";
            diagnostics.Add(new ProfileDiagnostic(
                ProfileDiagnosticSeverity.Warning,
                $"Profile '{profile.Name}' is deprecated.{suffix}"));
        }

        return new ProfileResolution.Resolved(profile, diagnostics);
    }

    private async Task<string?> ResolveActiveProfileNameAsync(
        ProjectProfileOptions projectConfig,
        UserProfilePreferenceOptions userPreferences,
        ProfileResolutionContext context,
        List<ProfileDiagnostic> diagnostics,
        CancellationToken cancellationToken)
    {
        string? requested = NullIfWhiteSpace(context.RequestedProfileName);
        if (requested is not null)
        {
            if (projectConfig.Profiles.Count > 0 && !IsDeclaredProfile(projectConfig, requested))
            {
                diagnostics.Add(new ProfileDiagnostic(
                    ProfileDiagnosticSeverity.Warning,
                    $"Profile '{requested}' is not declared in this project's .func/config.json."));
            }

            return requested;
        }

        if (projectConfig.DefaultProfile is { } projectDefaultProfile)
        {
            if (projectConfig.Profiles.Count > 0 && !IsDeclaredProfile(projectConfig, projectDefaultProfile))
            {
                diagnostics.Add(new ProfileDiagnostic(
                    ProfileDiagnosticSeverity.Warning,
                    $"Default profile '{projectDefaultProfile}' is not listed in this project's profiles."));
            }

            return projectDefaultProfile;
        }

        string? userDefaultProfile = NullIfWhiteSpace(userPreferences.DefaultProfile);
        if (userDefaultProfile is not null)
        {
            if (projectConfig.Profiles.Count == 0 || IsDeclaredProfile(projectConfig, userDefaultProfile))
            {
                return userDefaultProfile;
            }

            diagnostics.Add(new ProfileDiagnostic(
                ProfileDiagnosticSeverity.Warning,
                $"User default profile '{userDefaultProfile}' is not declared in this project's .func/config.json and will be ignored."));
        }

        if (projectConfig.Profiles.Count == 0)
        {
            return null;
        }

        if (projectConfig.Profiles.Count == 1)
        {
            return projectConfig.Profiles[0];
        }

        if (!context.CanPrompt)
        {
            throw new ProfileConfigurationException(
                "This project declares multiple profiles. Specify --profile or set defaultProfile in .func/config.json.");
        }

        return await _interaction.PromptForSelectionAsync("Select an Azure Functions profile", projectConfig.Profiles, cancellationToken);
    }

    private static bool IsDeclaredProfile(ProjectProfileOptions projectConfig, string profile)
        => projectConfig.Profiles.Any(p => string.Equals(p, profile, StringComparison.OrdinalIgnoreCase));

    private async Task<IReadOnlyList<ProfileSourceSnapshot>> LoadSourcesAsync(
        ProfileResolutionContext context,
        CancellationToken cancellationToken)
    {
        var sourceContext = new ProfileSourceContext(context.WorkingDirectory);
        List<ProfileSourceSnapshot> snapshots = [];
        foreach (IProfileSource source in _sources)
        {
            cancellationToken.ThrowIfCancellationRequested();
            snapshots.Add(await source.LoadAsync(sourceContext, cancellationToken));
        }

        return snapshots;
    }

    private ResolvedProfile ResolveProfile(string name, IReadOnlyList<ProfileSourceSnapshot> snapshots)
    {
        ProfileDefinitionEntry entry = ResolveDefinitionEntry(name, snapshots, []);
        VersionRange hostVersionRange = ParseRequiredRange(entry.Definition.Host?.Version, entry.Name, "host.version");
        Dictionary<string, VersionRange> workerRanges = new(StringComparer.OrdinalIgnoreCase);
        if (entry.Definition.Workers is { } workers)
        {
            foreach ((string runtime, ProfileWorkerConstraint? constraint) in workers)
            {
                if (constraint is null)
                {
                    continue;
                }

                workerRanges[runtime] = ParseRequiredRange(constraint.Version, entry.Name, $"workers.{runtime}.version");
            }
        }

        VersionRange? bundleRange = entry.Definition.ExtensionBundle?.Version is { } extensionBundleVersion
            ? ParseRequiredRange(extensionBundleVersion, entry.Name, "extensionBundle.version")
            : null;

        ProfileStatus status = entry.Definition.Status is null
            ? ProfileStatus.Stable
            : ProfileDocumentParser.ParseStatus(entry.Definition.Status, entry.Name, entry.Source.DisplayName);

        return new ResolvedProfile(
            entry.Name,
            entry.Source,
            entry.Definition.Sku,
            status,
            entry.Definition.DeprecationUrl,
            hostVersionRange,
            workerRanges,
            bundleRange,
            entry.Definition.SupportedRuntimes,
            entry.Definition.Notes);
    }

    private ProfileDefinitionEntry ResolveDefinitionEntry(
        string name,
        IReadOnlyList<ProfileSourceSnapshot> snapshots,
        IReadOnlyList<string> chain)
    {
        if (chain.Count > MaxInheritanceDepth)
        {
            throw new ProfileConfigurationException($"Profile inheritance chain exceeds maximum depth of {MaxInheritanceDepth}.");
        }

        string? repeated = chain.FirstOrDefault(p => string.Equals(p, name, StringComparison.OrdinalIgnoreCase));
        if (repeated is not null)
        {
            string cycle = string.Join(" -> ", [.. chain, name]);
            throw new ProfileConfigurationException($"Circular profile inheritance detected: {cycle}.");
        }

        ProfileDefinitionEntry own = FindProfile(name, snapshots)
            ?? throw new ProfileConfigurationException($"Profile '{name}' was not found.");

        if (NullIfWhiteSpace(own.Definition.Extends) is not { } parentName)
        {
            return own;
        }

        ProfileDefinitionEntry parent = ResolveDefinitionEntry(parentName, snapshots, [.. chain, name]);
        ProfileDefinition merged = Merge(parent.Definition, own.Definition);
        return own with { Definition = merged };
    }

    private static ProfileDefinitionEntry? FindProfile(string name, IReadOnlyList<ProfileSourceSnapshot> snapshots)
    {
        foreach (ProfileSourceSnapshot snapshot in snapshots)
        {
            if (snapshot.Profiles.TryGetValue(name, out ProfileDefinition? definition))
            {
                return new ProfileDefinitionEntry(name, definition, snapshot.Source);
            }
        }

        return null;
    }

    private static ProfileDefinition Merge(ProfileDefinition parent, ProfileDefinition child)
        => new()
        {
            Sku = child.Sku ?? parent.Sku,
            Status = child.Status ?? parent.Status,
            DeprecationUrl = child.DeprecationUrl ?? parent.DeprecationUrl,
            Host = child.Host ?? parent.Host,
            Workers = MergeWorkers(parent.Workers, child.Workers),
            ExtensionBundle = child.ExtensionBundle ?? parent.ExtensionBundle,
            SupportedRuntimes = child.SupportedRuntimes ?? parent.SupportedRuntimes,
            Notes = child.Notes ?? parent.Notes,
        };

    private static Dictionary<string, ProfileWorkerConstraint?>? MergeWorkers(
        Dictionary<string, ProfileWorkerConstraint?>? parent,
        Dictionary<string, ProfileWorkerConstraint?>? child)
    {
        if (parent is null && child is null)
        {
            return null;
        }

        Dictionary<string, ProfileWorkerConstraint?> merged = parent is null
            ? new(StringComparer.OrdinalIgnoreCase)
            : new(parent, StringComparer.OrdinalIgnoreCase);

        if (child is not null)
        {
            foreach ((string runtime, ProfileWorkerConstraint? constraint) in child)
            {
                if (constraint is null)
                {
                    merged.Remove(runtime);
                }
                else
                {
                    merged[runtime] = constraint;
                }
            }
        }

        return merged;
    }

    private static VersionRange ParseRequiredRange(string? value, string profileName, string propertyName)
    {
        if (string.IsNullOrWhiteSpace(value) || !VersionRange.TryParse(value, out VersionRange? range))
        {
            throw new ProfileConfigurationException(
                $"Profile '{profileName}' has invalid NuGet version range for '{propertyName}'.");
        }

        return range;
    }

    private static string? NullIfWhiteSpace(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
