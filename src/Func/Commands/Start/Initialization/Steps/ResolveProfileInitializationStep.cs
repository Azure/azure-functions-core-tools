// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Commands.Start.Initialization;

/// <summary>
/// Resolves the active start profile.
/// </summary>
internal sealed class ResolveProfileInitializationStep : DemoInitializationStep
{
    public const string StepId = "resolve_profile";

    public override string Id => StepId;

    public override string Title => "Resolve profile";

    public override async Task<StartInitializationStepResult> ExecuteAsync(
        StartInitializationStepContext context,
        CancellationToken cancellationToken)
    {
        await SimulateWorkAsync(context, cancellationToken);
        context.State.ProfileName = "none";
        return StartInitializationStepResult.Completed("None (no profile applied)");
    }
}
