// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.CommandLine;

namespace Azure.Functions.Cli.Commands.Workload;

/// <summary>
/// Parent <c>func workload</c> command. Subcommands manage workload installation,
/// inspection, and updates. Today the only subcommand wired in is
/// <see cref="WorkloadListCommand"/>; install / uninstall land in a follow-up PR.
/// </summary>
public class WorkloadCommand : Command
{
    public WorkloadCommand(WorkloadListCommand listCommand)
        : base("workload", "Manage Func CLI workloads.")
    {
        Subcommands.Add(listCommand);
    }
}
