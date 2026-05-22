// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Workloads.Storage;

namespace Azure.Functions.Cli.Workloads.Loading;

/// <summary>
/// Hydrates runtime workload registry entries into runnable
/// <see cref="RuntimeWorkloadInfo"/> instances.
/// </summary>
internal interface IWorkloadLoader
{
    /// <summary>
    /// Loads each runtime entry into its own
    /// <see cref="System.Runtime.Loader.AssemblyLoadContext"/> and activates
    /// the declared <see cref="Workload"/> type.
    /// </summary>
    /// <param name="entries">The registry entries to hydrate.</param>
    /// <returns>One <see cref="RuntimeWorkloadInfo"/> per input, in the same order.</returns>
    /// <exception cref="Common.GracefulException">
    /// Thrown when an entry's assembly is missing, the declared type cannot be
    /// found, or the type does not derive from <see cref="Workload"/>. Other
    /// entries are not partially loaded, the operation is all-or-nothing.
    /// </exception>
    public IReadOnlyList<RuntimeWorkloadInfo> Load(IReadOnlyList<WorkloadEntry> entries);
}
