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

    public Task<ProjectCreationResult> TryCreateProjectAsync(ProjectCreationContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();

        DirectoryInfo workingDirectory = context.WorkingDirectory.Info;
        if (!workingDirectory.Exists)
        {
            return Task.FromResult(NotCreated("directory does not exist"));
        }

        string? reason = TryGetReason(workingDirectory);
        if (reason is null)
        {
            return Task.FromResult(NotCreated("no Go project fingerprint found"));
        }

        FunctionsProject project = new GoFunctionsProject(context.WorkingDirectory);
        return Task.FromResult(Created(project, reason));
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

    private sealed class GoFunctionsProject(WorkingDirectory workingDirectory) : FunctionsProject
    {
        private readonly WorkingDirectory _workingDirectory = workingDirectory ?? throw new ArgumentNullException(nameof(workingDirectory));
        private readonly FunctionsWorkerReference _workerReference = FunctionsWorkerReference.FromWorkload(_workerId);

        public override WorkingDirectory WorkingDirectory => _workingDirectory;

        public override string StackName => "go";

        public override string StackDisplayName => "Go";

        public override bool SupportsExtensionBundles => true;

        public override FunctionsWorkerReference WorkerReference => _workerReference;
    }
}
