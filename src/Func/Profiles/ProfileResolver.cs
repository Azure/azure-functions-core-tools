// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Console;
using Microsoft.Extensions.Options;

namespace Azure.Functions.Cli.Profiles;

/// <summary>
/// Default active profile resolver.
/// </summary>
internal sealed class ProfileResolver(
    IProfileCatalog catalog,
    IOptionsMonitor<ProjectProfileOptions> projectProfileOptions,
    IOptionsMonitor<UserProfilePreferenceOptions> userProfilePreferenceOptions,
    IInteractionService interaction) : IProfileResolver
{
    private readonly IProfileCatalog _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
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

        var sourceContext = new ProfileSourceContext(context.WorkingDirectory);
        IReadOnlyList<ProfileSourceSnapshot> snapshots = await _catalog.LoadAsync(sourceContext, cancellationToken);
        ResolvedProfile profile = _catalog.ResolveProfile(profileName, snapshots);

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

    private static string? NullIfWhiteSpace(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
