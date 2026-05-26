// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Commands.Start.Host;
using Azure.Functions.Cli.Hosting.Dashboard.Demo;
using Azure.Functions.Cli.Projects;
using Azure.Functions.Cli.Workloads;

namespace Azure.Functions.Cli.Commands.Start.Initialization;

/// <summary>
/// Starts the host event stream.
/// </summary>
internal sealed class StartHostInitializationStep(
    IHostProcessRunner hostProcessRunner,
    TimeProvider? timeProvider = null) : DemoInitializationStep
{
    public const string StepId = "start_host";

    private readonly IHostProcessRunner _hostProcessRunner = hostProcessRunner
        ?? throw new ArgumentNullException(nameof(hostProcessRunner));
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;

    public override string Id => StepId;

    public override string Title => "Start host";

    public override async Task<StartInitializationStepResult> ExecuteAsync(
        StartInitializationStepContext context,
        CancellationToken cancellationToken)
    {
        await SimulateWorkAsync(context, cancellationToken);

        FunctionsProjectHostRunContext hostRunContext = context.State.HostRunContext
            ?? throw new InvalidOperationException("Host run context was not prepared.");

        if (context.Options.DemoMode)
        {
            context.State.EventStream = new DemoEventSource(_timeProvider)
            {
                SpeedMultiplier = context.Options.DemoSpeedMultiplier,
                AutoExit = context.Options.DemoAutoExit,
                FunctionCount = context.Options.DemoFunctionCount,
            };
        }
        else
        {
            ContentWorkloadInfo hostWorkload = context.State.HostWorkload
                ?? throw new InvalidOperationException("Host workload was not resolved.");
            context.State.EventStream = await _hostProcessRunner.StartAsync(
                new HostProcessStartContext(hostWorkload, hostRunContext, context.Options),
                cancellationToken);
        }

        return StartInitializationStepResult.Completed("Host process started");
    }
}
