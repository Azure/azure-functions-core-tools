// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Workloads.Install;

internal interface IWorkloadPackageInspector
{
    public Task<InspectedWorkloadPackage> InspectAsync(string path, CancellationToken cancellationToken = default);

    public bool MatchesIdentity(string path, string packageId, string version);

    public void ValidateIdentity(WorkloadPackageIdentity identity, string expectedPackageId, string expectedVersion);
}
