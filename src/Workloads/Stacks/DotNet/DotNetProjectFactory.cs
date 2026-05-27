// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Projects;
using Azure.Functions.Cli.Workers;
using static Azure.Functions.Cli.Projects.ProjectCreationResults;

namespace Azure.Functions.Cli.Workloads.DotNet;

/// <summary>
/// Creates .NET Functions projects from .NET-specific fingerprints.
/// Recognizes both source directories (containing a project file) and
/// pre-built output directories (containing host.json, worker.config.json, and an .exe).
/// </summary>
internal sealed class DotNetProjectFactory(IDotnetCliRunner dotnetCli) : IFunctionsProjectFactory
{
    private static readonly FunctionsWorkerId _workerId = new("dotnet");
    private readonly IDotnetCliRunner _dotnetCli = dotnetCli ?? throw new ArgumentNullException(nameof(dotnetCli));

    public async Task<ProjectCreationResult> TryCreateProjectAsync(ProjectCreationContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(context.WorkerResolver);
        cancellationToken.ThrowIfCancellationRequested();

        DirectoryInfo workingDirectory = context.WorkingDirectory.Info;
        if (!workingDirectory.Exists)
        {
            return NotCreated("directory does not exist");
        }

        var projectFiles = workingDirectory
            .EnumerateFiles("*.*proj", SearchOption.TopDirectoryOnly)
            .Where(f => f.Extension is ".csproj" or ".fsproj")
            .ToList();

        if (projectFiles.Count > 1)
        {
            return NotCreated("multiple .NET project files found; cannot determine which to use");
        }

        if (projectFiles.Count == 1)
        {
            string projectFile = projectFiles[0].FullName;
            return await ResolveWorkerAndCreateAsync(
                context,
                $"found {Path.GetFileName(projectFile)}",
                (wd, worker) => new DotNetSourceProject(wd, worker, projectFile, _dotnetCli),
                cancellationToken);
        }

        // No project file — check for pre-built output directory.
        if (IsOutputDirectory(workingDirectory))
        {
            return await ResolveWorkerAndCreateAsync(
                context,
                "found .NET build output (host.json, worker.config.json, .azurefunctions)",
                (wd, worker) => new DotNetOutputProject(wd, worker),
                cancellationToken);
        }

        return NotCreated("no .NET project file or build output found");
    }

    /// <summary>
    /// Detects a pre-built .NET output directory by the presence of host.json, worker.config.json, and a .azurefunctions directory.
    /// </summary>
    private static bool IsOutputDirectory(DirectoryInfo directory)
    {
        string dirPath = directory.FullName;

        bool hasHostJson = File.Exists(Path.Combine(dirPath, "host.json"));
        bool hasWorkerConfig = File.Exists(Path.Combine(dirPath, "worker.config.json"));
        bool hasAzureFunctions = Directory.Exists(Path.Combine(dirPath, ".azurefunctions"));

        return hasHostJson && hasWorkerConfig && hasAzureFunctions;
    }

    private async Task<ProjectCreationResult> ResolveWorkerAndCreateAsync(
        ProjectCreationContext context,
        string reason,
        Func<WorkingDirectory, IFunctionsWorker, FunctionsProject> projectFactory,
        CancellationToken cancellationToken)
    {
        FunctionsWorkerResolutionResult workerResult =
            await context.WorkerResolver.ResolveWorkerAsync(_workerId, cancellationToken);

        return workerResult switch
        {
            FunctionsWorkerResolutionResult.Resolved resolved =>
                Created(projectFactory(context.WorkingDirectory, resolved.Worker), reason),
            FunctionsWorkerResolutionResult.NotResolved notResolved =>
                Failed(ProjectCreationFailures.WorkerNotResolved(notResolved.Failure)),
            _ => throw new InvalidOperationException(
                $"Unsupported worker resolution result: {workerResult.GetType().FullName}"),
        };
    }
}
