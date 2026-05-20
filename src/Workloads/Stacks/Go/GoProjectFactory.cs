// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Projects;
using Azure.Functions.Cli.Workers;
using static Azure.Functions.Cli.Projects.ProjectCreationResults;

namespace Azure.Functions.Cli.Workloads.Go;

/// <summary>
/// Creates Go Functions projects from Go-specific fingerprints.
/// </summary>
internal sealed class GoProjectFactory : IFunctionsProjectFactory
{
    private static readonly FunctionsWorkerId _workerId = new("go");

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

        string? reason = TryGetReason(workingDirectory);
        if (reason is null)
        {
            return NotCreated("no Go project fingerprint found");
        }

        FunctionsWorkerResolutionResult workerResult =
            await context.WorkerResolver.ResolveWorkerAsync(_workerId, cancellationToken);
        return workerResult switch
        {
            FunctionsWorkerResolutionResult.Resolved resolved => Created(new GoFunctionsProject(context.WorkingDirectory, resolved.Worker), reason),
            FunctionsWorkerResolutionResult.NotResolved notResolved => Failed(ProjectCreationFailures.WorkerNotResolved(notResolved.Failure)),
            _ => throw new InvalidOperationException($"Unsupported worker resolution result: {workerResult.GetType().FullName}"),
        };
    }

    private static string? TryGetReason(DirectoryInfo workingDirectory)
    {
        if (File.Exists(Path.Combine(workingDirectory.FullName, "go.mod")))
        {
            return "found go.mod";
        }

        if (Directory.EnumerateFiles(workingDirectory.FullName, "*.go", SearchOption.TopDirectoryOnly).Any())
        {
            return "found *.go file";
        }

        return null;
    }

    private sealed record GoFunctionsProject(WorkingDirectory WorkingDirectory, IFunctionsWorker Worker) : IFunctionsProject
    {
        public string StackName => "go";

        public string StackDisplayName => "Go";

        public bool SupportsExtensionBundles => true;
    }
}
