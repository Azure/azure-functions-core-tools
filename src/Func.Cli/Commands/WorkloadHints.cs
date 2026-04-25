// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Console;

namespace Azure.Functions.Cli.Commands;

/// <summary>
/// Shared messaging for the case where no workload satisfied a request.
/// </summary>
internal static class WorkloadHints
{
    public static void WriteNoMatchingWorkload(
        IInteractionService interaction,
        IReadOnlyList<string> installedStacks,
        string actionDescription,
        string? requestedStack = null)
    {
        if (installedStacks.Count == 0)
        {
            interaction.WriteError($"No language workloads installed.");
            interaction.WriteBlankLine();
            interaction.WriteHint($"Install a workload to {actionDescription}:");
            interaction.WriteBlankLine();
            Common.Stacks.WriteWorkloadInstallHints(interaction);
            interaction.WriteBlankLine();
            interaction.WriteLine(l => l
                .Muted("Run ")
                .Command("func workload search")
                .Muted(" to discover available workloads."));
            return;
        }

        if (requestedStack is not null)
        {
            interaction.WriteError($"No installed workload supports stack '{requestedStack}'.");
        }
        else
        {
            interaction.WriteError("No stack specified and the installed workloads couldn't be auto-selected.");
        }

        interaction.WriteBlankLine();
        interaction.WriteHint("Installed stacks:");
        foreach (var stack in installedStacks)
        {
            interaction.WriteLine(l => l
                .Muted("  ")
                .Code(stack));
        }
    }
}
