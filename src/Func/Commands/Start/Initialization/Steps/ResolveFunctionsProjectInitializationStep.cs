// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Projects;

namespace Azure.Functions.Cli.Commands.Start.Initialization;

/// <summary>
/// Resolves the Functions project.
/// </summary>
internal sealed class ResolveFunctionsProjectInitializationStep(IFunctionsProjectResolver projectResolver) : FuncStartInitializationStep
{
    public const string StepId = "resolve_project";

    private readonly IFunctionsProjectResolver _projectResolver = projectResolver
        ?? throw new ArgumentNullException(nameof(projectResolver));

    public override string Id => StepId;

    public override string Title => "Resolve project";

    public override async Task<StartInitializationStepResult> ExecuteAsync(StartInitializationStepContext context, CancellationToken cancellationToken)
    {
        var projectResolutionContext = new ProjectResolutionContext(context.Options.WorkingDirectory);
        ProjectResolutionResult resolution = await _projectResolver.ResolveProjectAsync(projectResolutionContext, cancellationToken);

        string? diagnostic = resolution switch
        {
            ProjectResolutionResult.Uninitialized uninitialized => uninitialized.Message,
            ProjectResolutionResult.NotResolved notResolved => notResolved.Message,
            _ => null
        };
        if (diagnostic is not null)
        {
            throw new GracefulException(diagnostic, isUserError: true);
        }

        if (resolution is not ProjectResolutionResult.Resolved resolved)
        {
            throw new InvalidOperationException($"Unexpected project resolution result: {resolution.GetType().Name}.");
        }
        context.State.Project = resolved.Project;

        return StartInitializationStepResult.Completed(resolved.Project.StackDisplayName);
    }
}
