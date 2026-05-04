// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Workloads.Storage;

namespace Azure.Functions.Cli.Workloads.Loading;

/// <summary>
/// Hydrates workload manifest entries into runnable <see cref="LoadedWorkload"/>
/// instances. Pure: takes manifest entries in, returns loaded workloads out.
/// The composition root reads the entries from <see cref="IGlobalManifestStore"/>
/// and hands them here, keeping the storage and assembly-loading concerns
/// independently testable.
/// </summary>
internal interface IWorkloadLoader
{
    /// <summary>
    /// Loads each entry into its own <see cref="System.Runtime.Loader.AssemblyLoadContext"/>
    /// and activates the declared <see cref="IWorkload"/> type.
    /// </summary>
    /// <param name="installed">The installed workloads to hydrate.</param>
    /// <returns>One <see cref="LoadedWorkload"/> per input, in the same order.</returns>
    /// <exception cref="Common.GracefulException">
    /// Thrown when an entry's assembly is missing, the declared type cannot be
    /// found, or the type does not implement <see cref="IWorkload"/>. Other
    /// entries are not partially loaded — the operation is all-or-nothing.
    /// </exception>
    public IReadOnlyList<LoadedWorkload> LoadInstalled(IReadOnlyList<InstalledWorkload> installed);
}
