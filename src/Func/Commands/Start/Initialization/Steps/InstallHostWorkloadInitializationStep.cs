// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Commands.Start.Initialization;

/// <summary>
/// Installs the resolved host workload.
/// </summary>
internal sealed class InstallHostWorkloadInitializationStep(string hostVersion) : DemoInitializationStep
{
    public const string StepId = "install_host_workload";

    private readonly string _hostVersion = string.IsNullOrWhiteSpace(hostVersion)
        ? throw new ArgumentException("Host version cannot be empty.", nameof(hostVersion))
        : hostVersion;

    public override string Id => StepId;

    public override string Title => "Install host workload";

    public override string Detail => $"Azure Functions host {_hostVersion}";

    public override StartInitializationDisplayKind DisplayKind => StartInitializationDisplayKind.Progress;

    public override async Task<StartInitializationStepResult> ExecuteAsync(
        StartInitializationStepContext context,
        CancellationToken cancellationToken)
    {
        (double Percent, string Message)[] progress =
        [
            (0, "Preparing download"),
            (25, "Downloading package"),
            (55, "Verifying package"),
            (80, "Installing files"),
            (100, "Finalizing"),
        ];

        foreach ((double percent, string message) in progress)
        {
            await SimulateWorkAsync(context, cancellationToken);
            await context.ReportProgressAsync(percent, message, cancellationToken);
        }

        return StartInitializationStepResult.Completed($"Installed host {_hostVersion}");
    }
}
