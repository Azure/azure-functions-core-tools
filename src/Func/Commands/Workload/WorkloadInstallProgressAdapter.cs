// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Console;
using Azure.Functions.Cli.Workloads.Install;

namespace Azure.Functions.Cli.Commands.Workload;

/// <summary>
/// Bridges <see cref="IProgress{T}"/> reports from <see cref="IWorkloadInstaller"/>
/// onto the live progress bar exposed by <see cref="IInteractionService.RunWithProgressAsync{T}"/>.
/// </summary>
internal sealed class WorkloadInstallProgressAdapter(IProgressContext context) : IProgress<WorkloadInstallProgress>
{
    private readonly IProgressContext _context = context ?? throw new ArgumentNullException(nameof(context));

    public void Report(WorkloadInstallProgress value)
    {
        ArgumentNullException.ThrowIfNull(value);
        _context.SetDescription(value.Description);
    }
}
