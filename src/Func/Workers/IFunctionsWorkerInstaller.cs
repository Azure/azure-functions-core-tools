// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Workloads.Catalog;
using Azure.Functions.Cli.Workloads.Discovery;
using Azure.Functions.Cli.Workloads.Install;
using NuGet.Versioning;

namespace Azure.Functions.Cli.Workers;

/// <summary>
/// Installs content workloads that provide Functions workers.
/// </summary>
internal interface IFunctionsWorkerInstaller
{
    /// <summary>
    /// Installs the workload package for <paramref name="workerId"/> and returns the worker resolved from the installed payload.
    /// </summary>
    /// <param name="workerId">Worker runtime identifier.</param>
    /// <param name="workerVersionRanges">Profile worker version constraints keyed by runtime.</param>
    /// <param name="cancellationToken">Cancellation propagated to catalog and install operations.</param>
    /// <exception cref="WorkloadPackageNotFoundException">
    /// No package or compatible package version could be found.
    /// </exception>
    /// <exception cref="AmbiguousPackageMatchException">
    /// The package identifier unexpectedly matched multiple packages.
    /// </exception>
    /// <exception cref="InvalidWorkloadException">
    /// The installed package is not a valid worker content workload.
    /// </exception>
    /// <exception cref="FileNotFoundException">
    /// The resolved package could not be downloaded.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// The install cannot proceed because the local workload state is inconsistent.
    /// </exception>
    public Task<FunctionsWorkerInstallResult> InstallAsync(FunctionsWorkerId workerId, IReadOnlyDictionary<string, VersionRange> workerVersionRanges, CancellationToken cancellationToken);
}
