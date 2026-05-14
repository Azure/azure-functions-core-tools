// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Commands.Start.Initialization.Rendering;
using Azure.Functions.Cli.Hosting.AppStacks;
using Azure.Functions.Cli.Hosting.Dashboard;
using Azure.Functions.Cli.Hosting.Dashboard.Demo;

namespace Azure.Functions.Cli.Commands.Start.Initialization;

/// <summary>
/// Prototype initialization runner that simulates the host-resolution workflow.
/// </summary>
internal sealed class DemoStartInitializationRunner(
    IAppStackProvider appStackProvider,
    TimeProvider? timeProvider = null) : IStartInitializationRunner
{
    private const string DemoHostVersion = "4.834.0";
    private readonly IAppStackProvider _appStackProvider = appStackProvider ?? throw new ArgumentNullException(nameof(appStackProvider));
    private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;

    public async Task<StartInitializationResult> RunAsync(
        StartInitializationContext context,
        IStartInitializationRenderer renderer,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(renderer);

        await EmitAsync(renderer, new StartInitializationStartedEvent(Now(), context.ProfileName), cancellationToken);

        await RunStatusStepAsync(
            renderer,
            new StartInitializationStep(StartInitializationStepKind.ResolveProfile, "Resolve profile"),
            "none (no profile applied)",
            context,
            cancellationToken);

        await RunStatusStepAsync(
            renderer,
            new StartInitializationStep(StartInitializationStepKind.ResolveConstraints, "Resolve version constraints"),
            "No profile constraints applied",
            context,
            cancellationToken);

        await RunStatusStepAsync(
            renderer,
            new StartInitializationStep(StartInitializationStepKind.ResolveHostWorkload, "Validate host workload"),
            $"No installed host workload found for {DemoHostVersion}",
            context,
            cancellationToken);

        await RunProgressStepAsync(
            renderer,
            new StartInitializationStep(
                StartInitializationStepKind.InstallHostWorkload,
                "Install host workload",
                $"Azure Functions host {DemoHostVersion}",
                StartInitializationDisplayKind.Progress),
            context,
            cancellationToken);

        string stackName = await RunResolveStackStepAsync(renderer, context, cancellationToken);
        bool bundleRequired = RequiresExtensionBundle(stackName);
        await RunStatusStepAsync(
            renderer,
            new StartInitializationStep(StartInitializationStepKind.ResolveBundle, "Validate extension bundle"),
            bundleRequired
                ? "Bundle validation is not implemented in the prototype"
                : $"No extension bundle required for {stackName}",
            context,
            cancellationToken);

        await RunStatusStepAsync(
            renderer,
            new StartInitializationStep(StartInitializationStepKind.StartHost, "Start host"),
            "Host process started",
            context,
            cancellationToken);

        var runInfo = new DashboardRunInfo(
            CliVersion: context.CliVersion,
            ProfileName: context.ProfileName,
            StackName: stackName);
        var source = new DemoEventSource(_time)
        {
            SpeedMultiplier = context.DemoSpeedMultiplier,
            AutoExit = context.DemoAutoExit,
            FunctionCount = context.DemoFunctionCount,
        };

        var result = new StartInitializationResult(
            runInfo,
            source,
            DemoHostVersion,
            BundleRequired: bundleRequired,
            BundleVersion: null);

        await EmitAsync(renderer, new StartInitializationCompletedEvent(Now(), result), cancellationToken);
        return result;
    }

    private async Task RunStatusStepAsync(
        IStartInitializationRenderer renderer,
        StartInitializationStep step,
        string completionMessage,
        StartInitializationContext context,
        CancellationToken cancellationToken)
    {
        await EmitAsync(renderer, new StartInitializationStepStartedEvent(Now(), step), cancellationToken);
        await DelayAsync(context, cancellationToken);
        await EmitAsync(renderer, new StartInitializationStepCompletedEvent(Now(), step.Kind, completionMessage), cancellationToken);
    }

    private async Task<string> RunResolveStackStepAsync(
        IStartInitializationRenderer renderer,
        StartInitializationContext context,
        CancellationToken cancellationToken)
    {
        var step = new StartInitializationStep(StartInitializationStepKind.ResolveStack, "Resolve app stack");
        await EmitAsync(renderer, new StartInitializationStepStartedEvent(Now(), step), cancellationToken);
        await DelayAsync(context, cancellationToken);
        string stackName = await _appStackProvider.GetStackNameAsync(context.WorkingDirectory, cancellationToken);
        await EmitAsync(renderer, new StartInitializationStepCompletedEvent(Now(), step.Kind, stackName), cancellationToken);
        return stackName;
    }

    private async Task RunProgressStepAsync(
        IStartInitializationRenderer renderer,
        StartInitializationStep step,
        StartInitializationContext context,
        CancellationToken cancellationToken)
    {
        await EmitAsync(renderer, new StartInitializationStepStartedEvent(Now(), step), cancellationToken);
        (double Percent, string Message)[] progress =
        [
            (0, "Preparing download"),
            (25, "Downloading package"),
            (55, "Verifying package"),
            (80, "Installing files"),
            (100, "Finalizing"),
        ];

        foreach ((double percent, string message) in progress)
        {
            await DelayAsync(context, cancellationToken);
            await EmitAsync(
                renderer,
                new StartInitializationProgressEvent(Now(), step.Kind, percent, message),
                cancellationToken);
        }

        await EmitAsync(renderer, new StartInitializationStepCompletedEvent(Now(), step.Kind, $"Installed host {DemoHostVersion}"), cancellationToken);
    }

    private static bool RequiresExtensionBundle(string stackName)
        => !string.Equals(stackName, ".NET", StringComparison.OrdinalIgnoreCase)
        && !string.Equals(stackName, "unknown", StringComparison.OrdinalIgnoreCase);

    private async Task DelayAsync(StartInitializationContext context, CancellationToken cancellationToken)
    {
        double multiplier = context.DemoSpeedMultiplier <= 0 ? 0.25 : context.DemoSpeedMultiplier;
        await Task.Delay(TimeSpan.FromMilliseconds(120 * multiplier), _time, cancellationToken);
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
