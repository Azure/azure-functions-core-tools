// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Hosting.AppStacks;

namespace Azure.Functions.Cli.Commands.Start.Initialization;

/// <summary>
/// Resolves the function app stack.
/// </summary>
internal sealed class ResolveAppStackInitializationStep(IAppStackProvider appStackProvider) : DemoInitializationStep
{
    public const string StepId = "resolve_stack";

    private readonly IAppStackProvider _appStackProvider = appStackProvider ?? throw new ArgumentNullException(nameof(appStackProvider));

    public override string Id => StepId;

    public override string Title => "Resolve app stack";

    public override async Task<StartInitializationStepResult> ExecuteAsync(
        StartInitializationStepContext context,
        CancellationToken cancellationToken)
    {
        await SimulateWorkAsync(context, cancellationToken);

        string stackName = await _appStackProvider.GetStackNameAsync(context.Options.WorkingDirectory, cancellationToken);
        context.State.StackName = stackName;

        return StartInitializationStepResult.Completed(stackName);
    }
}
