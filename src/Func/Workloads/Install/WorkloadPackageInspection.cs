// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Workloads.Install;

internal enum WorkloadPackageRole
{
    Ordinary,
    Pointer,
    RuntimeIdentifierImplementation,
}

internal sealed record WorkloadPackageIdentity(string PackageId, string Version, IReadOnlyList<string> PackageTypes,
    IReadOnlyList<string> Aliases, IReadOnlyList<string> RuntimeIdentifierTags, string? Title, string? Description);

internal sealed record InspectedWorkloadPackage(string Path, WorkloadPackageIdentity Identity, WorkloadMetadata Metadata, WorkloadPackageRole Role);

internal sealed record WorkloadPointerSelection(string RuntimeIdentifier, string PackageId);
