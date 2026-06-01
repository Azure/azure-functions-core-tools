// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Workloads;

namespace Azure.Functions.Cli.Projects;

/// <summary>
/// Default aggregate project resolver.
/// </summary>
internal sealed class FunctionsProjectResolver(IEnumerable<WorkloadProjectFactoryRegistration> factories)
    : IFunctionsProjectResolver
{
    private readonly IReadOnlyList<WorkloadProjectFactoryRegistration> _factories =
        (factories ?? throw new ArgumentNullException(nameof(factories))).ToList();

    public async Task<ProjectResolutionResult> ResolveProjectAsync(ProjectResolutionContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        var creationContext = new ProjectCreationContext(context.WorkingDirectory);
        foreach (WorkloadProjectFactoryRegistration registration in _factories)
        {
            cancellationToken.ThrowIfCancellationRequested();

            ProjectCreationResult result = await registration.Factory.TryCreateProjectAsync(creationContext, cancellationToken);
            switch (result)
            {
                case ProjectCreationResult.Created created:
                    return ProjectResolutionResults.Resolved(created.Project, created.Reason);

                case ProjectCreationResult.Failed failed:
                    return ProjectResolutionResults.NotResolved(failed.Failure.Message, failed.Failure);

                case ProjectCreationResult.NotCreated:
                    continue;
            }
        }

        return ProjectResolutionResults.NotResolved(
            "No installed workload recognized this directory as an Azure Functions project. Run 'func setup' to install a stack.");
    }
}
