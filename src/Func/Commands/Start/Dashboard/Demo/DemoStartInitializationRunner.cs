// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Commands.Start.Initialization.Rendering;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Projects;

namespace Azure.Functions.Cli.Commands.Start.Initialization;

/// <summary>
/// Prototype initialization runner that simulates the host-resolution workflow.
/// </summary>
internal sealed class DemoStartInitializationRunner(IFunctionsProjectResolver projectResolver, TimeProvider? timeProvider = null)
    : IStartInitializationRunner
{
    private readonly IFunctionsProjectResolver _projectResolver = projectResolver
        ?? throw new ArgumentNullException(nameof(projectResolver));

    private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;

    public async Task<StartInitializationResult> RunAsync(StartInitializationContext context, IStartInitializationRenderer renderer, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(renderer);

        var state = new StartInitializationState();
        await EmitAsync(renderer, new StartInitializationStartedEvent(Now(), state.ProfileName), cancellationToken);

        await RunStepsAsync(CreateSteps(), context, state, renderer, cancellationToken);

        return state.ToResult(context);
    }

    private StartInitializationStepCollection CreateSteps()
    {
        StartInitializationStepCollection steps =
        [
            new ResolveProfileInitializationStep(),
            new ResolveConstraintsInitializationStep(),
            new ValidateHostWorkloadInitializationStep(),
            new ResolveFunctionsProjectInitializationStep(_projectResolver),
            new ValidateExtensionBundleInitializationStep(),
            new StartHostInitializationStep(_time),
        ];

        return steps;
    }

    private async Task RunStepsAsync(
        StartInitializationStepCollection steps,
        StartInitializationContext context,
        StartInitializationState state,
        IStartInitializationRenderer renderer,
        CancellationToken cancellationToken)
    {
        Queue<IStartInitializationStep> pending = new(steps);
        while (pending.TryDequeue(out IStartInitializationStep? step))
        {
            await EmitAsync(renderer, new StartInitializationStepStartedEvent(Now(), new StartInitializationStep(step)), cancellationToken);

            var stepContext = new StartInitializationStepContext(context, state, step, renderer, _time);
            StartInitializationStepResult result = await step.ExecuteAsync(stepContext, cancellationToken);

            await EmitAsync(renderer, new StartInitializationStepCompletedEvent(Now(), step.Id, result.Message), cancellationToken);

            IReadOnlyList<IStartInitializationStep> nextSteps = stepContext.DrainNextSteps();
            if (nextSteps.Count > 0)
            {
                pending = Prepend(nextSteps, pending);
            }
        }
    }

    private static Queue<IStartInitializationStep> Prepend(
        IReadOnlyList<IStartInitializationStep> nextSteps,
        Queue<IStartInitializationStep> pending)
    {
        Queue<IStartInitializationStep> updated = new();
        foreach (IStartInitializationStep nextStep in nextSteps)
        {
            updated.Enqueue(nextStep);
        }

        while (pending.TryDequeue(out IStartInitializationStep? remainingStep))
        {
            updated.Enqueue(remainingStep);
        }

        return updated;
    }

    private async Task EmitAsync(
        IStartInitializationRenderer renderer,
        StartInitializationEvent initializationEvent,
        CancellationToken cancellationToken)
    {
        await renderer.OnEventAsync(initializationEvent, cancellationToken);
    }

    private DateTimeOffset Now() => _time.GetUtcNow();
}
