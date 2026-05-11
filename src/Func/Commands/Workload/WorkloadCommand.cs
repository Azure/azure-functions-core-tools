// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Hosting;

namespace Azure.Functions.Cli.Commands.Workload;

/// <summary>
/// Parent <c>func workload</c> command. Subcommands manage workload installation,
/// inspection, and updates.
///
/// Parent-only: relies on <see cref="FuncCliCommand.ExecuteAsync"/>'s default
/// implementation to render help when invoked without a subcommand.
/// </summary>
internal sealed class WorkloadCommand : FuncCliCommand, IBuiltInCommand
{
    public WorkloadCommand(
        WorkloadListCommand listCommand,
        WorkloadInstallCommand installCommand,
        WorkloadUninstallCommand uninstallCommand)
        : base("workload", "Manage Func CLI workloads.")
    {
        ArgumentNullException.ThrowIfNull(listCommand);
        ArgumentNullException.ThrowIfNull(installCommand);
        ArgumentNullException.ThrowIfNull(uninstallCommand);

        Subcommands.Add(listCommand);
        Subcommands.Add(installCommand);
        Subcommands.Add(uninstallCommand);
    }
}
