// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Commands.Start.Initialization;

/// <summary>
/// Validates extension bundle requirements for the resolved app stack.
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

        string stackName = context.State.StackName ?? "unknown";
        bool bundleRequired = RequiresExtensionBundle(stackName);
        context.State.BundleRequired = bundleRequired;

        string message = bundleRequired
            ? "Bundle validation is not implemented in the prototype"
            : $"No extension bundle required for {stackName}";

        return StartInitializationStepResult.Completed(message);
    }

    private static bool RequiresExtensionBundle(string stackName)
        => !string.Equals(stackName, ".NET", StringComparison.OrdinalIgnoreCase)
        && !string.Equals(stackName, "unknown", StringComparison.OrdinalIgnoreCase);
}
