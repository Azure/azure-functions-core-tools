// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Commands.Start.Initialization;

/// <summary>
/// Validates extension bundle requirements for the resolved project.
/// </summary>
internal sealed class ValidateExtensionBundleInitializationStep : DemoInitializationStep
{
    public const string StepId = "resolve_bundle";

    public override string Id => StepId;

    public override string Title => "Validate extension bundle";

    public override async Task<StartInitializationStepResult> ExecuteAsync(
        StartInitializationStepContext context,
        CancellationToken cancellationToken)
    {
        await SimulateWorkAsync(context, cancellationToken);

        string stackName = context.State.Project?.StackDisplayName ?? "unknown";

        string message = context.State.Project?.SupportsExtensionBundles == true
            ? "Bundle validation is not implemented in the prototype"
            : $"No extension bundle required for {stackName}";

        return StartInitializationStepResult.Completed(message);
    }
}
