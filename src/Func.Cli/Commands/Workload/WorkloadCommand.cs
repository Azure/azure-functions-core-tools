// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Hosting;

namespace Azure.Functions.Cli.Commands.Workload;

/// <summary>
/// Parent <c>func workload</c> command. Subcommands manage workload installation,
/// inspection, and updates. Today the only subcommand wired in is
/// <see cref="WorkloadListCommand"/>; install / uninstall land in a follow-up PR.
///
/// Parent-only — relies on <see cref="BaseCommand.ExecuteAsync"/>'s default
/// implementation to render help when invoked without a subcommand.
/// </summary>
internal sealed class WorkloadCommand : BaseCommand, IBuiltInCommand
{
    public WorkloadCommand(WorkloadListCommand listCommand)
        : base("workload", "Manage Func CLI workloads.")
    {
        ArgumentNullException.ThrowIfNull(listCommand);
        Subcommands.Add(listCommand);
    }
}
