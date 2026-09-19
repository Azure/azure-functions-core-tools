// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Bundles;
using Azure.Functions.Cli.Commands.Workload;
using Azure.Functions.Cli.Console;
using Azure.Functions.Cli.Hosting.FirstRun;
using Azure.Functions.Cli.Profiles;

namespace Azure.Functions.Cli.Commands.Setup;

/// <summary>
/// Orchestrates <c>func setup</c>: resolve features, resolve profile scopes, then
/// plan and install dependencies for each scope through focused setup services.
/// </summary>
internal sealed class SetupRunner(
    IInteractionService interaction,
    ISetupFeatureResolver featureResolver,
    ISetupProfileScopeResolver profileScopeResolver,
    ISetupDependencyPlanBuilder dependencyPlanBuilder,
    ISetupDependencyInstaller dependencyInstaller,
    IFirstRunStateStore? firstRunStateStore = null) : ISetupRunner
{
    private readonly IInteractionService _interaction = interaction ?? throw new ArgumentNullException(nameof(interaction));
    private readonly ISetupFeatureResolver _featureResolver = featureResolver ?? throw new ArgumentNullException(nameof(featureResolver));
    private readonly ISetupProfileScopeResolver _profileScopeResolver = profileScopeResolver ?? throw new ArgumentNullException(nameof(profileScopeResolver));
    private readonly ISetupDependencyPlanBuilder _dependencyPlanBuilder = dependencyPlanBuilder ?? throw new ArgumentNullException(nameof(dependencyPlanBuilder));
    private readonly ISetupDependencyInstaller _dependencyInstaller = dependencyInstaller ?? throw new ArgumentNullException(nameof(dependencyInstaller));
    private readonly IFirstRunStateStore? _firstRunStateStore = firstRunStateStore;

    public async Task<SetupRunResult> RunAsync(SetupCommandOptions options, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);

        SetupRenderer renderer = new(_interaction, options.OutputMode);
        if (options.IncludePrerelease && options.OutputMode != SetupOutputMode.Json)
        {
            _interaction.WriteHint(WorkloadInstallCommand.PrereleasePreviewHint);
        }

        try
        {
            SetupFeaturePlan? featurePlan = await _featureResolver.ResolveFeaturesAsync(options, cancellationToken);
            if (featurePlan is null)
            {
                // No stacks were offered to install (every supported stack is
                // already installed). Treat that as a clean no-op, not a failure.
                renderer.SetupSkippedNoSelection();
                await TryMarkFirstRunCompleteAsync(cancellationToken);
                return new SetupRunResult(0);
            }

            IReadOnlyList<SetupProfileScope> profileScopes = await _profileScopeResolver.ResolveProfileScopesAsync(options, renderer, cancellationToken);

            renderer.SetupStarted(options, featurePlan, profileScopes);

            int failureCount = 0;
            foreach (SetupProfileScope profileScope in profileScopes)
            {
                cancellationToken.ThrowIfCancellationRequested();
                renderer.ProfileStarted(profileScope);

                ProfileSetupOutcome outcome = await RunProfileAsync(options, featurePlan, profileScope, renderer, cancellationToken);
                failureCount += outcome.FailureCount;
                renderer.ProfileCompleted(profileScope, outcome);

                if (outcome.FailureCount > 0 && !options.Check)
                {
                    renderer.SetupFailed(failureCount);
                    return new SetupRunResult(1);
                }
            }

            if (failureCount > 0)
            {
                renderer.SetupFailed(failureCount);
                return new SetupRunResult(1);
            }

            renderer.SetupCompleted();
            await TryMarkFirstRunCompleteAsync(cancellationToken);
            return new SetupRunResult(0);
        }
        catch (SetupConfigurationException ex)
        {
            renderer.SetupFailed(ex.Message);
            return new SetupRunResult(1);
        }
        catch (ProfileConfigurationException ex)
        {
            renderer.SetupFailed(ex.Message);
            return new SetupRunResult(1);
        }
        catch (ExtensionBundleConfigurationException ex)
        {
            renderer.SetupFailed(ex.Message);
            return new SetupRunResult(1);
        }
    }

    private async Task TryMarkFirstRunCompleteAsync(CancellationToken cancellationToken)
    {
        if (_firstRunStateStore is null)
        {
            return;
        }

        try
        {
            await _firstRunStateStore.MarkCompleteAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            // Failing to mark the first-run marker after a successful setup
            // is a minor nuisance (the user might see the first-run prompt
            // one more time), not a setup failure. Stay silent.
        }
    }

    private async Task<ProfileSetupOutcome> RunProfileAsync(SetupCommandOptions options, SetupFeaturePlan featurePlan, SetupProfileScope profileScope, SetupRenderer renderer, CancellationToken cancellationToken)
    {
        SetupDependencyPlan plan = await _dependencyPlanBuilder.BuildDependencyPlanAsync(options, featurePlan, profileScope, cancellationToken);
        int failures = 0;

        foreach (SetupDependency dependency in plan.Dependencies)
        {
            renderer.DependencyDetected(profileScope, dependency);
            SetupDependencyResult result = await _dependencyInstaller.EnsureDependencyAsync(options, dependency, cancellationToken);
            renderer.DependencyResult(profileScope, dependency, result);

            if (result.Status == SetupDependencyStatus.Failed)
            {
                failures++;
                if (!options.Check)
                {
                    return new ProfileSetupOutcome(failures);
                }
            }
        }

        foreach (SetupDependencyResult failure in plan.Failures)
        {
            renderer.DependencyResult(profileScope, failure.Dependency, failure);
            failures++;
            if (!options.Check)
            {
                return new ProfileSetupOutcome(failures);
            }
        }

        return new ProfileSetupOutcome(failures);
    }
}
