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
internal sealed class StartHostInitializationStep(IHostProcessRunner hostProcessRunner, TimeProvider? timeProvider = null) : FuncStartInitializationStep
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
            ContentWorkloadInfo hostWorkload = context.State.HostWorkload ?? throw new InvalidOperationException("Host workload was not resolved.");
            FunctionsProject project = context.State.Project ?? throw new InvalidOperationException("Functions project was not resolved.");

            IHostOutputInterceptor? outputInterceptor = context.Options.StackHostConfigurations.TryGetValue(project.StackName, out StartHostConfiguration? stackConfiguration)
                ? stackConfiguration.OutputInterceptor
                : null;

            // Dispose interceptors for non-matching stacks so we don't leak file handles
            // opened by workloads that don't apply to this project.
            foreach (KeyValuePair<string, StartHostConfiguration> entry in context.Options.StackHostConfigurations)
            {
                if (!string.Equals(entry.Key, project.StackName, StringComparison.OrdinalIgnoreCase)
                    && entry.Value.OutputInterceptor is not null)
                {
                    await entry.Value.OutputInterceptor.DisposeAsync();
                }
            }

            var startContext = new HostProcessStartContext(hostWorkload, hostRunContext, context.Options, outputInterceptor);
            context.State.EventStream = await _hostProcessRunner.StartAsync(startContext, cancellationToken);
        }

        return StartInitializationStepResult.Completed("Host process started");
    }
}
