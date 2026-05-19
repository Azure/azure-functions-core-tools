// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Projects;

namespace Azure.Functions.Cli.Commands.Start.Initialization;

/// <summary>
/// Resolves the function app stack.
/// </summary>
internal sealed class ResolveAppStackInitializationStep(IProjectResolver projectResolver) : DemoInitializationStep
{
    public const string StepId = "resolve_stack";

    private readonly IProjectResolver _projectResolver = projectResolver ?? throw new ArgumentNullException(nameof(projectResolver));

    public override string Id => StepId;

    public override string Title => "Resolve app stack";

    public override async Task<StartInitializationStepResult> ExecuteAsync(
        StartInitializationStepContext context,
        CancellationToken cancellationToken)
    {
        await SimulateWorkAsync(context, cancellationToken);

        RuntimeStackInfo stackInfo = await _projectResolver.GetRuntimeStackInfoAsync(context.Options.WorkingDirectory, cancellationToken);
        context.State.StackInfo = stackInfo;

        return StartInitializationStepResult.Completed(stackInfo.StackName);
    }
}
