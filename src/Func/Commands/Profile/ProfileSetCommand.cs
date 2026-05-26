// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.CommandLine;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Configuration;
using Azure.Functions.Cli.Console;
using Azure.Functions.Cli.Profiles;

namespace Azure.Functions.Cli.Commands.Profile;

/// <summary>
/// Sets the default profile for a Functions project.
/// </summary>
internal sealed class ProfileSetCommand : FuncCliCommand
{
    public Argument<string> NameArgument { get; } = new("name")
    {
        Description = "The profile name to set as the project default."
    };

    private readonly IInteractionService _interaction;
    private readonly IProfileCatalog _catalog;
    private readonly IProjectProfileConfigStore _projectConfigStore;

    public ProfileSetCommand(IInteractionService interaction, IProfileCatalog catalog, IProjectProfileConfigStore projectConfigStore)
        : base("set", "Set the default profile for a Functions project.")
    {
        _interaction = interaction ?? throw new ArgumentNullException(nameof(interaction));
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        _projectConfigStore = projectConfigStore ?? throw new ArgumentNullException(nameof(projectConfigStore));

        Arguments.Add(NameArgument);
        AddPathArgument();
    }

    protected override async Task<int> ExecuteAsync(ParseResult parseResult, CancellationToken cancellationToken)
    {
        WorkingDirectory workingDirectory = parseResult.GetValue(PathArgument!)!;
        if (!workingDirectory.Exists)
        {
            string displayPath = workingDirectory.OriginalPath ?? workingDirectory.Info.FullName;
            throw new GracefulException($"The specified path does not exist: '{displayPath}'", isUserError: true);
        }

        string name = parseResult.GetValue(NameArgument)!;
        ProjectProfileConfigUpdateResult result;
        try
        {
            result = await SetProfileAsync(workingDirectory.Info, name, cancellationToken);
        }
        catch (ProfileConfigurationException ex)
        {
            throw new GracefulException(ex.Message, isUserError: true, verboseMessage: ex.ToString());
        }

        WriteResult(result);
        return 0;
    }

    /// <exception cref="ProfileConfigurationException">
    /// Thrown when the profile cannot be resolved or the project config cannot be written.
    /// </exception>
    private async Task<ProjectProfileConfigUpdateResult> SetProfileAsync(DirectoryInfo workingDirectory, string name, CancellationToken cancellationToken)
    {
        var sourceContext = new ProfileSourceContext(workingDirectory);
        IReadOnlyList<ProfileSourceSnapshot> snapshots = await _catalog.LoadAsync(sourceContext, cancellationToken);
        ResolvedProfile profile = _catalog.ResolveProfile(name, snapshots);

        return await _projectConfigStore.SetDefaultProfileAsync(workingDirectory, profile.Name, cancellationToken);
    }

    private void WriteResult(ProjectProfileConfigUpdateResult result)
    {
        _interaction.WriteSuccess($"Profile '{result.Profile}' set as this project's default.");
        if (result.AddedProfile)
        {
            _interaction.WriteHint($"Added '{result.Profile}' to this project's profiles list.");
        }

        _interaction.WriteLine(l => l.Muted("Wrote ").Code(CliConfigurationPathsOptions.ProjectConfigDisplayPath).Muted("."));
    }
}
