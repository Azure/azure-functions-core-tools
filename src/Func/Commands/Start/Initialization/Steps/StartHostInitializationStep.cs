// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Hosting.Dashboard.Demo;

namespace Azure.Functions.Cli.Commands.Start.Initialization;

/// <summary>
/// Starts the host event stream.
/// </summary>
internal sealed class StartHostInitializationStep(TimeProvider? timeProvider = null) : DemoInitializationStep
{
    public const string StepId = "start_host";

    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;

    public override string Id => StepId;

    public override string Title => "Start host";

    public override async Task<StartInitializationStepResult> ExecuteAsync(
        StartInitializationStepContext context,
        CancellationToken cancellationToken)
    {
        await SimulateWorkAsync(context, cancellationToken);

        _ = context.State.HostRunContext
            ?? throw new InvalidOperationException("Host run context was not prepared.");

        context.State.EventStream = new DemoEventSource(_timeProvider)
        {
            SpeedMultiplier = context.Options.DemoSpeedMultiplier,
            AutoExit = context.Options.DemoAutoExit,
            FunctionCount = context.Options.DemoFunctionCount,
        };

        return StartInitializationStepResult.Completed("Host process started");
    }
}
