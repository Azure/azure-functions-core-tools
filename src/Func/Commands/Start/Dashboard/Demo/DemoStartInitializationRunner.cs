// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Bundles;
using Azure.Functions.Cli.Commands.Start.Initialization.Rendering;
using Azure.Functions.Cli.Commands.Start.Host;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Configuration;
using Azure.Functions.Cli.Console;
using Azure.Functions.Cli.Profiles;
using Azure.Functions.Cli.Projects;
using Azure.Functions.Cli.Workers;
using Azure.Functions.Cli.Workloads.Catalog;
using Azure.Functions.Cli.Workloads.Install;
using Azure.Functions.Cli.Workloads.Storage;
using Microsoft.Extensions.Logging;

namespace Azure.Functions.Cli.Commands.Start.Initialization;

/// <summary>
/// Runs the start initialization workflow for real host launch and hidden demo mode.
/// </summary>
internal sealed class DemoStartInitializationRunner(
    IFunctionsProjectResolver projectResolver,
    IExtensionBundleResolver bundleResolver,
    IHostJsonBundleSectionReader bundleSectionReader,
    IProfileResolver profileResolver,
    IHostWorkloadResolver hostWorkloadResolver,
    IFunctionsWorkerResolverFactory workerResolverFactory,
    IWorkloadCatalog workloadCatalog,
    IWorkloadInstaller workloadInstaller,
    IInteractionService interaction,
    ILocalSettingsProvider localSettingsProvider,
    IWorkloadPaths workloadPaths,
    IHostProcessRunner hostProcessRunner,
    IProcessEnvironment processEnvironment,
    ILoggerFactory loggerFactory,
    TimeProvider? timeProvider = null)
    : IStartInitializationRunner
{
    private readonly IFunctionsProjectResolver _projectResolver = projectResolver
        ?? throw new ArgumentNullException(nameof(projectResolver));

    private readonly IExtensionBundleResolver _bundleResolver = bundleResolver
        ?? throw new ArgumentNullException(nameof(bundleResolver));

    private readonly IHostJsonBundleSectionReader _bundleSectionReader = bundleSectionReader
        ?? throw new ArgumentNullException(nameof(bundleSectionReader));

    private readonly IProfileResolver _profileResolver = profileResolver
        ?? throw new ArgumentNullException(nameof(profileResolver));

    private readonly IHostWorkloadResolver _hostWorkloadResolver = hostWorkloadResolver
        ?? throw new ArgumentNullException(nameof(hostWorkloadResolver));

    private readonly IFunctionsWorkerResolverFactory _workerResolverFactory = workerResolverFactory
        ?? throw new ArgumentNullException(nameof(workerResolverFactory));

    private readonly IWorkloadCatalog _workloadCatalog = workloadCatalog ?? throw new ArgumentNullException(nameof(workloadCatalog));

    private readonly IWorkloadInstaller _workloadInstaller = workloadInstaller
        ?? throw new ArgumentNullException(nameof(workloadInstaller));

    private readonly IInteractionService _interaction = interaction ?? throw new ArgumentNullException(nameof(interaction));

    private readonly ILocalSettingsProvider _localSettingsProvider = localSettingsProvider
        ?? throw new ArgumentNullException(nameof(localSettingsProvider));

    private readonly IWorkloadPaths _workloadPaths = workloadPaths
        ?? throw new ArgumentNullException(nameof(workloadPaths));

    private readonly IHostProcessRunner _hostProcessRunner = hostProcessRunner
        ?? throw new ArgumentNullException(nameof(hostProcessRunner));

    private readonly IProcessEnvironment _processEnvironment = processEnvironment
        ?? throw new ArgumentNullException(nameof(processEnvironment));

    private readonly ILoggerFactory _loggerFactory = loggerFactory
        ?? throw new ArgumentNullException(nameof(loggerFactory));

    private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;

    public async Task<StartInitializationResult> RunAsync(StartInitializationContext context, IStartInitializationRenderer renderer, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(renderer);

        var state = new StartInitializationState
        {
            ProfileName = context.ProfileName,
        };
        await EmitAsync(renderer, new StartInitializationStartedEvent(Now(), state.ProfileName), cancellationToken);

        await RunStepsAsync(CreateSteps(), context, state, renderer, cancellationToken);

        return state.ToResult(context);
    }

    private StartInitializationStepCollection CreateSteps()
    {
        StartInitializationStepCollection steps =
        [
            new ResolveProfileInitializationStep(_profileResolver),
            new ResolveConstraintsInitializationStep(),
            new ValidateHostWorkloadInitializationStep(_hostWorkloadResolver, _workloadInstaller, _workloadPaths),
            new ResolveFunctionsProjectInitializationStep(_projectResolver),
            new ResolveFunctionsWorkerInitializationStep(_workerResolverFactory, _workloadCatalog, _workloadInstaller, _interaction),
            new ValidateExtensionBundleInitializationStep(
                _bundleResolver,
                _bundleSectionReader,
                _loggerFactory.CreateLogger<ValidateExtensionBundleInitializationStep>()),
            new PrepareProjectHostRunInitializationStep(_localSettingsProvider, _processEnvironment, _interaction),
            new StartHostInitializationStep(_hostProcessRunner, _time),
        ];

        return steps;
    }

    private async Task RunStepsAsync(StartInitializationStepCollection steps, StartInitializationContext context, StartInitializationState state,
        IStartInitializationRenderer renderer, CancellationToken cancellationToken)
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

    private static Queue<IStartInitializationStep> Prepend(IReadOnlyList<IStartInitializationStep> nextSteps, Queue<IStartInitializationStep> pending)
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

    private async Task EmitAsync(IStartInitializationRenderer renderer, StartInitializationEvent initializationEvent, CancellationToken cancellationToken)
        => await renderer.OnEventAsync(initializationEvent, cancellationToken);

    private DateTimeOffset Now() => _time.GetUtcNow();
}
