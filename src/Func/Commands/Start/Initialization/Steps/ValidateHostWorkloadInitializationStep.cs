// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Commands.Start.Initialization;

/// <summary>
/// Validates that the requested host workload is available.
/// </summary>
internal sealed class ValidateHostWorkloadInitializationStep : DemoInitializationStep
{
    public const string StepId = "resolve_host_workload";

    private const string DemoHostVersion = "4.834.0";

    public override string Id => StepId;

    public override string Title => "Validate host version";

    public override async Task<StartInitializationStepResult> ExecuteAsync(
        StartInitializationStepContext context,
        CancellationToken cancellationToken)
    {
        await SimulateWorkAsync(context, cancellationToken);

        string hostVersion = string.IsNullOrWhiteSpace(context.Options.RequestedHostVersion)
            ? DemoHostVersion
            : context.Options.RequestedHostVersion;

        context.State.HostVersion = hostVersion;
        context.AddNext(new InstallHostWorkloadInitializationStep(hostVersion));

        return StartInitializationStepResult.Completed($"No installed host workload found for {hostVersion}");
    }
}
