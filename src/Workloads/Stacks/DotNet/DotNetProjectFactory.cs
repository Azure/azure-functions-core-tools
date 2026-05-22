// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Projects;
using Azure.Functions.Cli.Workers;
using static Azure.Functions.Cli.Projects.ProjectCreationResults;

namespace Azure.Functions.Cli.Workloads.DotNet;

/// <summary>
/// Creates .NET Functions projects from .NET-specific fingerprints.
/// </summary>
internal sealed class DotNetProjectFactory : IFunctionsProjectFactory
{
    private static readonly FunctionsWorkerId _workerId = new("dotnet");

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

        string? projectFile = TryFindSingleProjectFile(workingDirectory, out string? notCreatedReason);
        if (projectFile is null)
        {
            return NotCreated(notCreatedReason!);
        }

        string reason = $"found {Path.GetFileName(projectFile)}";

        FunctionsWorkerResolutionResult workerResult =
            await context.WorkerResolver.ResolveWorkerAsync(_workerId, cancellationToken);

        return workerResult switch
        {
            FunctionsWorkerResolutionResult.Resolved resolved =>
                Created(new DotNetFunctionsProject(context.WorkingDirectory, resolved.Worker), reason),
            FunctionsWorkerResolutionResult.NotResolved notResolved =>
                Failed(ProjectCreationFailures.WorkerNotResolved(notResolved.Failure)),
            _ => throw new InvalidOperationException(
                $"Unsupported worker resolution result: {workerResult.GetType().FullName}"),
        };
    }

    private static string? TryFindSingleProjectFile(DirectoryInfo directory, out string? notCreatedReason)
    {
        var projectFiles = directory
            .EnumerateFiles("*.*proj", SearchOption.TopDirectoryOnly)
            .Where(f => f.Extension is ".csproj" or ".fsproj")
            .ToList();

        if (projectFiles.Count == 0)
        {
            notCreatedReason = "no .csproj or .fsproj found";
            return null;
        }

        if (projectFiles.Count > 1)
        {
            notCreatedReason = "multiple .NET project files found; cannot determine which to use";
            return null;
        }

        notCreatedReason = null;
        return projectFiles[0].FullName;
    }

    private sealed class DotNetFunctionsProject(WorkingDirectory workingDirectory, IFunctionsWorker worker) : FunctionsProject
    {
        private readonly WorkingDirectory _workingDirectory = workingDirectory ?? throw new ArgumentNullException(nameof(workingDirectory));
        private readonly IFunctionsWorker _worker = worker ?? throw new ArgumentNullException(nameof(worker));

        public override WorkingDirectory WorkingDirectory => _workingDirectory;

        public override string StackName => "dotnet";

        public override string StackDisplayName => ".NET";

        public override bool SupportsExtensionBundles => false;

        public override IFunctionsWorker Worker => _worker;
    }
}
