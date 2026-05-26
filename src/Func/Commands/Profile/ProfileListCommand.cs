// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.CommandLine;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Console;
using Azure.Functions.Cli.Profiles;
using Microsoft.Extensions.Options;
using NuGet.Versioning;

namespace Azure.Functions.Cli.Commands.Profile;

/// <summary>
/// Lists profiles available from project, user, and built-in sources.
/// </summary>
internal sealed class ProfileListCommand : FuncCliCommand
{
    public Option<string?> SourceOption { get; } = new("--source")
    {
        Description = "Comma-separated profile sources to include: project, user, built-in."
    };

    public Option<bool> JsonOption { get; } = new("--json")
    {
        Description = "Emit machine-readable JSON."
    };

    private readonly IInteractionService _interaction;
    private readonly IProfileCatalog _catalog;
    private readonly IOptionsMonitor<ProjectProfileOptions> _projectProfileOptions;

    public ProfileListCommand(
        IInteractionService interaction,
        IProfileCatalog catalog,
        IOptionsMonitor<ProjectProfileOptions> projectProfileOptions)
        : base("list", "List available Azure Functions profiles.")
    {
        _interaction = interaction ?? throw new ArgumentNullException(nameof(interaction));
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        _projectProfileOptions = projectProfileOptions
            ?? throw new ArgumentNullException(nameof(projectProfileOptions));

        AddPathArgument();
        Options.Add(SourceOption);
        Options.Add(JsonOption);
    }

    protected override async Task<int> ExecuteAsync(ParseResult parseResult, CancellationToken cancellationToken)
    {
        WorkingDirectory workingDirectory = parseResult.GetValue(PathArgument!)!;
        string? sourceValue = parseResult.GetValue(SourceOption);
        bool json = parseResult.GetValue(JsonOption);

        ProfileListResult result;
        try
        {
            result = await GetProfilesAsync(workingDirectory.Info, sourceValue, cancellationToken);
        }
        catch (ProfileConfigurationException ex)
        {
            throw new GracefulException(ex.Message, isUserError: true, verboseMessage: ex.ToString());
        }

        if (json)
        {
            WriteJson(result);
            return 0;
        }

        WriteTable(result);
        return 0;
    }

    /// <exception cref="ProfileConfigurationException">
    /// Thrown when profile documents or inheritance are invalid.
    /// </exception>
    private async Task<ProfileListResult> GetProfilesAsync(
        DirectoryInfo workingDirectory,
        string? sourceValue,
        CancellationToken cancellationToken)
    {
        IReadOnlySet<ProfileSourceKind>? sourceKinds = ParseSourceFilter(sourceValue);
        var sourceContext = new ProfileSourceContext(workingDirectory);
        IReadOnlyList<ProfileSourceSnapshot> snapshots = await _catalog.LoadAsync(sourceContext, cancellationToken);
        IReadOnlyList<ProfileDefinitionEntry> entries = _catalog.ListEffectiveProfiles(snapshots, sourceKinds);
        List<ProfileListRow> rows = [];
        foreach (ProfileDefinitionEntry entry in entries)
        {
            ResolvedProfile profile = _catalog.ResolveProfile(entry, snapshots);
            rows.Add(new ProfileListRow(
                profile.Name,
                profile.Source.KindDisplayName,
                RangeText(profile.HostVersionRange),
                RangeText(profile.ExtensionBundleVersionRange),
                profile.Status.ToString().ToLowerInvariant()));
        }

        string projectDirectory = Path.GetFullPath(workingDirectory.FullName);
        ProjectProfileOptions projectOptions = _projectProfileOptions.Get(projectDirectory);
        var project = new ProfileListProject(projectOptions.Profiles, projectOptions.DefaultProfile);
        return new ProfileListResult(project, rows);
    }

    private void WriteTable(ProfileListResult result)
    {
        if (result.Project.Profiles.Count > 0)
        {
            _interaction.WriteDefinitionList(
            [
                new DefinitionItem("Project profiles", FormatProjectProfiles(result.Project)),
            ]);
            _interaction.WriteBlankLine();
        }

        if (result.Profiles.Count == 0)
        {
            _interaction.WriteHint("No profiles found.");
            return;
        }

        _interaction.WriteTable(
            ["Name", "Source", "Host Version", "Extension Bundle", "Status"],
            result.Profiles.Select(row => new[]
            {
                row.Name,
                row.Source,
                row.HostVersion,
                row.ExtensionBundleVersion,
                row.Status,
            }));
    }

    private void WriteJson(ProfileListResult result)
    {
        var value = new
        {
            project = new
            {
                profiles = result.Project.Profiles,
                defaultProfile = result.Project.DefaultProfile,
            },
            profiles = result.Profiles,
        };
        _interaction.WriteJson(value);
    }

    private static IReadOnlySet<ProfileSourceKind>? ParseSourceFilter(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        HashSet<ProfileSourceKind> kinds =
        [
            .. value
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(ParseSourceKind),
        ];

        if (kinds.Count == 0)
        {
            throw new ProfileConfigurationException(
                "--source must include at least one source: project, user, built-in.");
        }

        return kinds;
    }

    private static ProfileSourceKind ParseSourceKind(string value)
        => value.Trim().ToLowerInvariant() switch
        {
            "project" => ProfileSourceKind.Project,
            "user" => ProfileSourceKind.User,
            "built-in" or "builtin" => ProfileSourceKind.BuiltIn,
            _ => throw new ProfileConfigurationException(
                $"Unknown profile source '{value}'. Use one of: project, user, built-in."),
        };

    private static string FormatProjectProfiles(ProfileListProject project)
        => string.Join(", ", project.Profiles.Select(profile =>
            string.Equals(profile, project.DefaultProfile, StringComparison.OrdinalIgnoreCase)
                ? $"{profile} (default)"
                : profile));

    private static string RangeText(VersionRange? range)
        => range?.OriginalString ?? range?.ToString() ?? "-";

    private sealed record ProfileListResult(ProfileListProject Project, IReadOnlyList<ProfileListRow> Profiles);

    private sealed record ProfileListProject(IReadOnlyList<string> Profiles, string? DefaultProfile);

    private sealed record ProfileListRow(
        string Name,
        string Source,
        string HostVersion,
        string ExtensionBundleVersion,
        string Status);
}
