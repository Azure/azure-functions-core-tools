// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Workloads.Install;

/// <summary>
/// Reacts to changes in the installed workloads, so dependent state such as caches can be refreshed.
/// </summary>
internal interface IWorkloadChangeObserver
{
    /// <summary>
    /// Called after an install, update, or uninstall changed the installed workloads on disk.
    /// </summary>
    /// <param name="cancellationToken">A token to observe while reacting to the change.</param>
    /// <returns>A task representing the notification.</returns>
    public Task OnWorkloadsChangedAsync(CancellationToken cancellationToken);
}
