// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Console;
using Azure.Functions.Cli.Workloads;

namespace Azure.Functions.Cli.Commands;

/// <summary>
/// Shared messaging for the case where no workload satisfied a request.
/// </summary>
internal static class WorkloadHints
{
    public static void WriteNoMatchingWorkload(
        IInteractionService interaction,
        IReadOnlyList<IWorkload> workloads,
        string actionDescription,
        string? requestedRuntime = null)
    {
        if (workloads.Count == 0)
        {
            interaction.WriteError($"No language workloads installed.");
            interaction.WriteBlankLine();
            interaction.WriteHint($"Install a workload to {actionDescription}:");
            interaction.WriteBlankLine();
            Common.WorkerRuntimes.WriteWorkloadInstallHints(interaction);
            interaction.WriteBlankLine();
            interaction.WriteLine(l => l
                .Muted("Run ")
                .Command("func workload search")
                .Muted(" to discover available workloads."));
            return;
        }

        if (requestedRuntime is not null)
        {
            interaction.WriteError($"No installed workload supports worker runtime '{requestedRuntime}'.");
        }
        else
        {
            interaction.WriteError("No worker runtime specified and the installed workloads couldn't be auto-selected.");
        }

        interaction.WriteBlankLine();
        interaction.WriteHint("Installed workloads:");
        foreach (var workload in workloads)
        {
            interaction.WriteLine(l => l
                .Muted("  ")
                .Code(workload.Id)
                .Muted($" — {workload.Description}"));
        }
    }
}
