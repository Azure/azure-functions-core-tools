// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Workloads.Catalog;
using Azure.Functions.Cli.Workloads.Discovery;

namespace Azure.Functions.Cli.Workloads.Install;

internal sealed class WorkloadRidPackageSelector(IWorkloadRuntimeIdentifierProvider runtimeIdentifierProvider)
{
    private readonly IWorkloadRuntimeIdentifierProvider _runtimeIdentifierProvider =
        runtimeIdentifierProvider ?? throw new ArgumentNullException(nameof(runtimeIdentifierProvider));

    public void ValidateImplementation(WorkloadPackageIdentity identity, string manifestRuntimeIdentifier)
    {
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentException.ThrowIfNullOrWhiteSpace(manifestRuntimeIdentifier);

        string currentRuntimeIdentifier = CurrentRuntimeIdentifier;
        if (identity.RuntimeIdentifierTags.Count != 1)
        {
            throw new InvalidWorkloadException(
                $"RID implementation package '{identity.PackageId}' must declare exactly one rid:<rid> tag.");
        }

        string tagRuntimeIdentifier = identity.RuntimeIdentifierTags[0];
        string expectedSuffix = "." + manifestRuntimeIdentifier;
        if (!string.Equals(tagRuntimeIdentifier, manifestRuntimeIdentifier, StringComparison.Ordinal)
            || !identity.PackageId.EndsWith(expectedSuffix, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(manifestRuntimeIdentifier, currentRuntimeIdentifier, StringComparison.Ordinal))
        {
            throw new InvalidWorkloadException(
                $"RID metadata mismatch for package '{identity.PackageId}': manifest runtimeIdentifier='{manifestRuntimeIdentifier}', " +
                $"nuspec rid tag='{tagRuntimeIdentifier}', package-id suffix='{expectedSuffix}', " +
                $"current RID='{currentRuntimeIdentifier}'.");
        }
    }

    public WorkloadPointerSelection SelectImplementation(InspectedWorkloadPackage pointer)
    {
        ArgumentNullException.ThrowIfNull(pointer);

        string currentRuntimeIdentifier = CurrentRuntimeIdentifier;
        if (!pointer.Metadata.Packages!.TryGetValue(currentRuntimeIdentifier, out string? implementationId))
        {
            string supported = string.Join(", ", pointer.Metadata.Packages.Keys.OrderBy(r => r, StringComparer.Ordinal));
            throw new WorkloadPackageNotFoundException(
                $"Workload '{pointer.Identity.PackageId}' {pointer.Identity.Version} does not support runtime identifier " +
                $"'{currentRuntimeIdentifier}'. Supported runtime identifiers: {supported}.");
        }

        return new WorkloadPointerSelection(currentRuntimeIdentifier, implementationId.ToLowerInvariant());
    }

    public void ValidateImplementation(InspectedWorkloadPackage pointer, WorkloadPointerSelection selection,
        InspectedWorkloadPackage implementation)
    {
        ArgumentNullException.ThrowIfNull(pointer);
        ArgumentNullException.ThrowIfNull(selection);
        ArgumentNullException.ThrowIfNull(implementation);

        if (implementation.Role != WorkloadPackageRole.RuntimeIdentifierImplementation)
        {
            throw new InvalidWorkloadException(
                $"Pointer '{pointer.Identity.PackageId}' selected '{implementation.Identity.PackageId}', " +
                $"but it is not a '{WorkloadPackageTypes.RuntimeIdentifierPackage}' implementation package.");
        }

        if (!string.Equals(implementation.Identity.PackageId, selection.PackageId, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(implementation.Identity.Version, pointer.Identity.Version, StringComparison.Ordinal)
            || !string.Equals(implementation.Metadata.RuntimeIdentifier, selection.RuntimeIdentifier, StringComparison.Ordinal))
        {
            throw new InvalidWorkloadException(
                $"Pointer implementation mismatch: pointer='{pointer.Identity.PackageId}' {pointer.Identity.Version}, " +
                $"map RID='{selection.RuntimeIdentifier}', mapped package='{selection.PackageId}', returned package=" +
                $"'{implementation.Identity.PackageId}' {implementation.Identity.Version}, manifest runtimeIdentifier=" +
                $"'{implementation.Metadata.RuntimeIdentifier}'.");
        }
    }

    private string CurrentRuntimeIdentifier
    {
        get
        {
            string runtimeIdentifier = _runtimeIdentifierProvider.Current.Trim().ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(runtimeIdentifier))
            {
                throw new InvalidOperationException("Unable to determine the current runtime identifier.");
            }

            return runtimeIdentifier;
        }
    }
}
