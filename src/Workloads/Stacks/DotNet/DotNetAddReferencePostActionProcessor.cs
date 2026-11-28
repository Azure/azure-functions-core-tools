// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Text;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Templates.PostActions;

namespace Azure.Functions.Cli.Workloads.DotNet;

/// <summary>
/// Adds package and project references to .NET projects after template creation.
/// </summary>
internal sealed class DotNetAddReferencePostActionProcessor : AddReferencePostAction<PostActionReference>
{
    private readonly IDotnetCliRunner _dotnetCli;
    private readonly DotNetProjectStack _stack;
    private readonly IProjectFileLocator _projectFileLocator;

    public DotNetAddReferencePostActionProcessor(
        IDotnetCliRunner dotnetCli, DotNetProjectStack stack, IProjectFileLocator projectFileLocator)
    {
        _dotnetCli = dotnetCli ?? throw new ArgumentNullException(nameof(dotnetCli));
        _stack = stack ?? throw new ArgumentNullException(nameof(stack));
        _projectFileLocator = projectFileLocator ?? throw new ArgumentNullException(nameof(projectFileLocator));
        RegisterReferenceHandlers(("package", AddPackageReferenceAsync), ("project", AddProjectReferenceAsync));
    }

    /// <inheritdoc />
    public override bool CanProcess(PostActionSelectionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return context.IsCompatibleWith(_stack);
    }

    /// <inheritdoc />
    protected override bool TryFindTargets(PostActionContext context, out IReadOnlyList<string> targets)
    {
        ArgumentNullException.ThrowIfNull(context);
        targets = [];
        HashSet<string> extensions = [with(StringComparer.OrdinalIgnoreCase)];
        if (context.Arguments.TryGetValue("projectFileExtensions", out string? projectFileExtensions))
        {
            if (projectFileExtensions.IndexOfAny(['/', '\\', '*']) >= 0)
            {
                context.Reporter.WriteLog(
                    "The add-reference post action is misconfigured: "
                    + "'projectFileExtensions' must contain literal extensions.",
                    FunctionsReportSeverity.Error);
                return false;
            }

            extensions.UnionWith(
                projectFileExtensions.Split(
                    ';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        }

        IReadOnlyList<string> projects = _projectFileLocator.FindProjectFilesAtOrAbove(
            Path.GetFullPath(context.OutputBasePath), extensions);
        if (projects.Count > 1)
        {
            StringBuilder message = new(
                "Found multiple project files. Specify the 'targetFiles' argument to choose one:");
            foreach (string project in projects)
            {
                message.AppendLine().Append('\t').Append(project);
            }

            context.Reporter.WriteLog(message.ToString(), FunctionsReportSeverity.Error);
            return false;
        }

        targets = projects.Count == 1
            ? projects
            : [.. context.GetConfiguredPaths("targetFiles").Where(_projectFileLocator.FileExists)];
        return true;
    }

    private Task<bool> AddPackageReferenceAsync(
        PostActionContext context,
        string targetPath,
        PostActionReference reference,
        CancellationToken cancellationToken)
    {
        string target = Path.GetRelativePath(Path.GetFullPath(context.OutputBasePath), targetPath);
        context.Reporter.ReportStatus(reference.Version is { Length: > 0 } version
            ? $"Adding package reference '{reference.Reference}' version '{version}' to '{target}'."
            : $"Adding package reference '{reference.Reference}' to '{target}'.");
        List<string> arguments = ["add", targetPath, "package", reference.Reference];
        if (!string.IsNullOrWhiteSpace(reference.Version))
        {
            arguments.AddRange(["--version", reference.Version]);
        }

        return _dotnetCli.TryRunPostActionAsync(context, arguments, "dotnet add package", cancellationToken);
    }

    private Task<bool> AddProjectReferenceAsync(
        PostActionContext context,
        string targetPath,
        PostActionReference reference,
        CancellationToken cancellationToken)
    {
        // Templates reference source paths; follow sourceName renames to the generated project when there is one match.
        IReadOnlyList<string> renamed = context.FileMatcher?.MatchTargetPaths(reference.Reference) ?? [];
        string projectPath = Path.GetFullPath(
            renamed.Count == 1 ? renamed[0] : reference.Reference, Path.GetFullPath(context.OutputBasePath));
        string target = Path.GetRelativePath(Path.GetFullPath(context.OutputBasePath), targetPath);
        context.Reporter.ReportStatus($"Adding project reference '{reference.Reference}' to '{target}'.");
        return _dotnetCli.TryRunPostActionAsync(
            context, ["add", targetPath, "reference", projectPath], "dotnet add reference", cancellationToken);
    }
}
