// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.CommandLine;

namespace Azure.Functions.Cli.Commands.Workload;

/// <summary>
/// Parent <c>func workload</c> command. Subcommands manage workload installation,
/// inspection, and updates.
/// </summary>
public class WorkloadCommand : Command
{
    public WorkloadCommand(
        WorkloadListCommand listCommand,
        WorkloadInstallCommand installCommand,
        WorkloadUninstallCommand uninstallCommand)
        : base("workload", "Manage Func CLI workloads.")
    {
        Subcommands.Add(listCommand);
        Subcommands.Add(installCommand);
        Subcommands.Add(uninstallCommand);
    }
}
