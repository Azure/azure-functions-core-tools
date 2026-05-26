// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Projects;
using NuGet.Versioning;

namespace Azure.Functions.Cli.Commands.Start.Initialization;

/// <summary>
/// Resolves the Functions project.
/// </summary>
internal sealed class ResolveFunctionsProjectInitializationStep(IFunctionsProjectResolver projectResolver) : DemoInitializationStep
{
    public const string StepId = "resolve_project";

    private readonly IFunctionsProjectResolver _projectResolver = projectResolver
        ?? throw new ArgumentNullException(nameof(projectResolver));

    public override string Id => StepId;

    public override string Title => "Resolve project";

    public override async Task<StartInitializationStepResult> ExecuteAsync(StartInitializationStepContext context, CancellationToken cancellationToken)
    {
        await SimulateWorkAsync(context, cancellationToken);

        IReadOnlyDictionary<string, VersionRange> workerVersionRanges =
            context.State.ResolvedProfile?.WorkerVersionRanges
            ?? new Dictionary<string, VersionRange>(StringComparer.OrdinalIgnoreCase);
        var projectResolutionContext = new ProjectResolutionContext(context.Options.WorkingDirectory, workerVersionRanges);
        ProjectResolutionResult resolution = await _projectResolver.ResolveProjectAsync(projectResolutionContext, cancellationToken);

        if (resolution is ProjectResolutionResult.NotResolved notResolved)
        {
            throw new GracefulException(notResolved.Message, isUserError: true);
        }

        var resolved = (ProjectResolutionResult.Resolved)resolution;
        ValidateSupportedRuntime(context, resolved.Project);
        context.State.Project = resolved.Project;

        return StartInitializationStepResult.Completed(resolved.Project.StackDisplayName);
    }

    private static void ValidateSupportedRuntime(StartInitializationStepContext context, FunctionsProject project)
    {
        if (context.State.ResolvedProfile is not { SupportedRuntimes: { } supportedRuntimes } profile)
        {
            return;
        }

        if (supportedRuntimes.Any(runtime => string.Equals(runtime, project.Worker.WorkerRuntime, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        string message = $"Profile '{profile.Name}' does not support the detected runtime '{project.Worker.WorkerRuntime}'. "
            + $"Supported runtimes: {string.Join(", ", supportedRuntimes)}.";
        throw new GracefulException(message, isUserError: true);
    }
}
