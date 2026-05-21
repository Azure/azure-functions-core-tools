// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Bundles;

/// <summary>Read-only view over installed bundle workload rows in the global workload registry.</summary>
public interface IInstalledBundleWorkloads
{
    /// <summary>NuGet package id of the bundles workload. Matched case-insensitively.</summary>
    public const string BundleWorkloadPackageId = "Azure.Functions.Cli.Workloads.ExtensionBundles";

    public Task<IReadOnlyList<InstalledBundleWorkload>> ListInstalledAsync(CancellationToken cancellationToken = default);
}

/// <summary>One installed bundle workload row. <see cref="InstallDirectory"/> is the workload's extracted install dir.</summary>
public sealed record InstalledBundleWorkload(string PackageVersion, string InstallDirectory);
