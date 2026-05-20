// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Projects;

namespace Azure.Functions.Cli.Commands.Start.Initialization;

/// <summary>
/// Lets the resolved project prepare host process state before startup.
/// </summary>
internal sealed class PrepareProjectHostRunInitializationStep : DemoInitializationStep
{
    public const string StepId = "prepare_host_run";

    public override string Id => StepId;

    public override string Title => "Prepare project";

    public override async Task<StartInitializationStepResult> ExecuteAsync(
        StartInitializationStepContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        await SimulateWorkAsync(context, cancellationToken);

        FunctionsProject project = context.State.Project
            ?? throw new InvalidOperationException("Functions project was not resolved.");

        Dictionary<string, string> environmentVariables = new(StringComparer.OrdinalIgnoreCase);
        var hostRunContext = new FunctionsProjectHostRunContext(
            project.WorkingDirectory.Info,
            project.Worker.WorkerRuntime,
            environmentVariables);

        await project.PrepareForHostRunAsync(hostRunContext, cancellationToken);

        _ = hostRunContext.WorkerRuntime;
        context.State.HostRunContext = hostRunContext;

        return StartInitializationStepResult.Completed(hostRunContext.StartupDirectory.FullName);
    }
}
