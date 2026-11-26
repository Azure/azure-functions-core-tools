// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Workloads.Discovery;
using Azure.Functions.Cli.Workloads.Storage;

namespace Azure.Functions.Cli.Workloads.Install;

internal enum WorkloadPackageRole
{
    Ordinary,
    Pointer,
    RuntimeIdentifierImplementation,
}

internal sealed record WorkloadPackageIdentity(string PackageId, string Version, IReadOnlyList<string> PackageTypes,
    IReadOnlyList<string> Aliases, IReadOnlyList<string> RuntimeIdentifiers, string? Title, string? Description);

internal sealed record InspectedWorkloadPackage(string Path, WorkloadPackageIdentity Identity, WorkloadMetadata Metadata, WorkloadPackageRole Role);

internal sealed record WorkloadPointerSelection(string RuntimeIdentifier, string PackageId);

internal sealed record WorkloadStackRequirement(string Identifier, string RuntimeIdentifier)
{
    public void ValidateManifest(string packageId, WorkloadMetadata manifest)
        => Validate(packageId, manifest.Kind is WorkloadKind.Workload or WorkloadKind.RidPointer, manifest.Stack?.Identifier, manifest.RuntimeIdentifier);

    public void ValidateInstalled(WorkloadEntry entry, WorkloadMetadata manifest)
    {
        Validate(entry.PackageId, manifest.Kind == WorkloadKind.Workload, manifest.Stack?.Identifier, manifest.RuntimeIdentifier);
        Validate(entry.PackageId, entry.Kind == WorkloadKind.Workload, entry.Stack?.Identifier, entry.RuntimeIdentifier);
    }

    private void Validate(string packageId, bool supportsStack, string? stack, string? rid)
    {
        if (!supportsStack || !string.Equals(stack, Identifier, StringComparison.OrdinalIgnoreCase) || !WorkloadActivation.IsCompatible(rid, RuntimeIdentifier))
        {
            throw new InvalidWorkloadException($"Workload '{packageId}' does not provide stack '{Identifier}' for {RuntimeIdentifier}.");
        }
    }
}
