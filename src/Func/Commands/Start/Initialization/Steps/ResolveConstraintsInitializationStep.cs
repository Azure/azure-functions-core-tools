// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Commands.Start.Initialization;

/// <summary>
/// Resolves version constraints from the active start profile.
/// </summary>
internal sealed class ResolveConstraintsInitializationStep : DemoInitializationStep
{
    public const string StepId = "resolve_constraints";

    public override string Id => StepId;

    public override string Title => "Resolve profile version constraints";

    public override async Task<StartInitializationStepResult> ExecuteAsync(
        StartInitializationStepContext context,
        CancellationToken cancellationToken)
    {
        await SimulateWorkAsync(context, cancellationToken);
        return StartInitializationStepResult.Completed("No profile constraints applied");
    }
}
