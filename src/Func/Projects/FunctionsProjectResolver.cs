// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Workloads;

namespace Azure.Functions.Cli.Projects;

/// <summary>
/// Default aggregate project resolver.
/// </summary>
internal sealed class FunctionsProjectResolver(
    IEnumerable<WorkloadProjectFactoryRegistration> factories,
    IFileSystem fileSystem)
    : IFunctionsProjectResolver
{
    private const string InspectionFailureMessage =
        "The project directory could not be inspected. Check that it exists and is accessible, then try again.";

    private readonly IReadOnlyList<WorkloadProjectFactoryRegistration> _factories =
        (factories ?? throw new ArgumentNullException(nameof(factories))).ToList();
    private readonly IFileSystem _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));

    public async Task<ProjectResolutionResult> ResolveProjectAsync(ProjectResolutionContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();

        var creationContext = new ProjectCreationContext(context.WorkingDirectory);
        foreach (WorkloadProjectFactoryRegistration registration in _factories)
        {
            cancellationToken.ThrowIfCancellationRequested();

            ProjectCreationResult result = await registration.Factory.TryCreateProjectAsync(creationContext, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            switch (result)
            {
                case ProjectCreationResult.Created created:
                    return ProjectResolutionResults.Resolved(created.Project, created.Reason);

                case ProjectCreationResult.Failed failed:
                    return ProjectResolutionResults.NotResolved(failed.Failure.Message, failed.Failure);

                case ProjectCreationResult.NotCreated:
                    continue;

                default:
                    throw new InvalidOperationException("The workload returned an unexpected project creation result.");
            }
        }

        // An empty directory is only an actionable init target when a workload can own the new project.
        if (_factories.Count > 0)
        {
            try
            {
                string path = context.WorkingDirectory.Info.FullName;
                if (!_fileSystem.DirectoryHasEntries(path))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    return ProjectResolutionResults.Uninitialized();
                }
            }
            catch (UnauthorizedAccessException)
            {
                cancellationToken.ThrowIfCancellationRequested();
                return ProjectResolutionResults.NotResolved(InspectionFailureMessage);
            }
            catch (IOException)
            {
                cancellationToken.ThrowIfCancellationRequested();
                return ProjectResolutionResults.NotResolved(InspectionFailureMessage);
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
        return ProjectResolutionResults.NotResolved(
            "No installed workload recognized this directory as an Azure Functions project. Run 'func setup' to set up your local environment for your chosen stack.");
    }
}
