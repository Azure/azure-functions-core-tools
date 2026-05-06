// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Workloads.Storage;

namespace Azure.Functions.Cli.Workloads.Loading;

/// <summary>
/// Hydrates workload registry entries into runnable <see cref="WorkloadInfo"/>
/// instances. Pure: takes registry entries in, returns loaded workloads out.
/// The composition root reads the entries from <see cref="IWorkloadStore"/>
/// and hands them here, keeping the storage and assembly-loading concerns
/// independently testable.
/// </summary>
internal interface IWorkloadLoader
{
    /// <summary>
    /// Loads each entry into its own <see cref="System.Runtime.Loader.AssemblyLoadContext"/>
    /// and activates the declared <see cref="Workload"/> type.
    /// </summary>
    /// <param name="entries">The registry entries to hydrate.</param>
    /// <returns>One <see cref="WorkloadInfo"/> per input, in the same order.</returns>
    /// <exception cref="Common.GracefulException">
    /// Thrown when an entry's assembly is missing, the declared type cannot be
    /// found, or the type does not derive from <see cref="Workload"/>. Other
    /// entries are not partially loaded, the operation is all-or-nothing.
    /// </exception>
    public IReadOnlyList<WorkloadInfo> Load(IReadOnlyList<WorkloadEntry> entries);
}
