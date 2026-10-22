// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Workloads.Install;

/// <summary>
/// Owns a stable package snapshot and verifies publisher trust before inspection or extraction.
/// </summary>
internal interface IWorkloadPackageTrust
{
    public Task<WorkloadPackageSnapshot> AcquireAsync(string path, bool allowUntrusted, CancellationToken cancellationToken);
}
