// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.CommandLine;

namespace Azure.Functions.Cli.Commands.Workload;

/// <summary>
/// Parent <c>func workload</c> command. Subcommands manage workload installation,
/// inspection, and updates. v1 prototype only ships <c>list</c>; install/search/update
/// land alongside the real workload loader.
/// </summary>
public class WorkloadCommand : Command
{
    public WorkloadCommand(WorkloadListCommand listCommand)
        : base("workload", "Manage Func CLI workloads.")
    {
        Subcommands.Add(listCommand);
    }
}
