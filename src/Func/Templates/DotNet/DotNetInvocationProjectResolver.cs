// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Text;
using System.Text.Json;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Projects;
using Azure.Functions.Cli.Templates.DotNet.Shared;
using Azure.Functions.Cli.Templates.Engine;

namespace Azure.Functions.Cli.Templates.DotNet;

internal sealed record DotNetInvocationProject(WorkingDirectory Root, DotNetTemplateProjectContext Properties,
    string ProjectSha256, IReadOnlyList<string> CompileOrder);

/// <summary>
/// Resolves one source project through installed stack metadata before evaluating its exact MSBuild input.
/// </summary>
internal sealed class DotNetInvocationProjectResolver(IFunctionsProjectResolver projects, IFileSystem files,
    DotNetTemplateProjectPreparation preparation, IDotnetCliRunner dotnetCli, FSharpProjectFiles paths)
{
    private readonly IFunctionsProjectResolver _projects = projects ?? throw new ArgumentNullException(nameof(projects));
    private readonly IFileSystem _files = files ?? throw new ArgumentNullException(nameof(files));
    private readonly DotNetTemplateProjectPreparation _preparation = preparation ?? throw new ArgumentNullException(nameof(preparation));
    private readonly IDotnetCliRunner _dotnetCli = dotnetCli ?? throw new ArgumentNullException(nameof(dotnetCli));
    private readonly FSharpProjectFiles _paths = paths ?? throw new ArgumentNullException(nameof(paths));

    public async Task<DotNetInvocationProject> ResolveAsync(TemplateEngineContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();
        TemplateEngineProjectContext selected = context.Project
            ?? throw new InvalidOperationException("The controlled .NET workflow requires a resolved containing Functions project.");
        if (selected.Stack != "dotnet" || selected.Language is not ("C#" or "F#"))
        {
            throw new InvalidOperationException("The controlled .NET workflow requires the selected .NET stack and canonical C# or F# language.");
        }

        ProjectResolutionResult resolution = await _projects.ResolveProjectAsync(new(selected.RootDirectory), cancellationToken);
        if (resolution is not ProjectResolutionResult.Resolved resolved || resolved.Project.StackName != selected.Stack
            || resolved.Project.Language != selected.Language || !SamePath(resolved.Project.WorkingDirectory.Info.FullName, selected.RootDirectory.Info.FullName))
        {
            throw new InvalidOperationException("The installed stack no longer identifies the selected .NET source project and language.");
        }

        IReadOnlyList<string> candidates = DotNetProjectFileSelection.Candidates(
            _files.GetFileSystemEntries(selected.RootDirectory.Info.FullName).Where(_files.FileExists));
        if (candidates.Count != 1)
        {
            throw new InvalidOperationException("The selected .NET project requires exactly one source .csproj or .fsproj; build output and ambiguous projects are not qualified.");
        }

        string project = candidates[0];
        if (Path.GetExtension(project) != (selected.Language == "F#" ? ".fsproj" : ".csproj"))
        {
            throw new InvalidOperationException("The source project extension conflicts with the installed stack's selected language.");
        }

        _paths.ValidatePath(project, selected.RootDirectory.Info.FullName, mustExist: true);
        string digest = await _files.ComputeSha256Async(project, cancellationToken);
        DotNetTemplateProjectContext properties = await _preparation.ReadAsync(project, cancellationToken);
        IReadOnlyList<string> compileOrder = [];
        if (selected.Language == "F#")
        {
            StringBuilder output = new();
            await _dotnetCli.RunStreamingAsync(["msbuild", project, "-nologo", "-getItem:Compile"], selected.RootDirectory.Info.FullName,
                line => output.AppendLine(line), null, cancellationToken);
            try
            {
                using var document = JsonDocument.Parse(output.ToString());
                compileOrder = Array.AsReadOnly<string>([.. document.RootElement.GetProperty("Items").GetProperty("Compile").EnumerateArray()
                    .Select(item => item.GetProperty("FullPath").GetString()
                        ?? throw new InvalidOperationException("An evaluated F# compile item has no exact source path."))]);
            }
            catch (JsonException exception)
            {
                throw new InvalidOperationException("The selected F# project's compile evaluation is invalid JSON.", exception);
            }
            catch (KeyNotFoundException exception)
            {
                throw new InvalidOperationException("The selected F# project's compile evaluation is incomplete.", exception);
            }
        }

        _paths.ValidatePath(project, selected.RootDirectory.Info.FullName, mustExist: true);
        if (!string.Equals(digest, await _files.ComputeSha256Async(project, cancellationToken), StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("The selected .NET project changed during MSBuild preparation.");
        }

        return new(selected.RootDirectory, properties, digest, compileOrder);
    }

    private static bool SamePath(string left, string right) => string.Equals(Path.GetFullPath(left), Path.GetFullPath(right),
        Path.DirectorySeparatorChar == '\\' ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
}
