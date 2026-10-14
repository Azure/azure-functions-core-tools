// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Workloads.Storage;

namespace Azure.Functions.Cli.Workloads.Install;

/// <summary>
/// Records the exact resolved pointer/implementation/source and advisory state, never live mutation authority.
/// </summary>
internal sealed class PreparedWorkloadInstall(string packageId, string version, string source, InspectedWorkloadPackage? pointer,
    string? pointerSha256, string? runtimeIdentifier, LogicalPackage? logicalPackage,
    PreparedWorkloadDeployment? deployment, string expectedState) : IDisposable
{
    public string PackageId { get; } = packageId;
    public string Version { get; } = version;
    public string Source { get; } = source;
    public InspectedWorkloadPackage? Pointer { get; } = pointer;
    public string? PointerSha256 { get; } = pointerSha256;
    public string? RuntimeIdentifier { get; } = runtimeIdentifier;
    public LogicalPackage? LogicalPackage { get; } = logicalPackage;
    public PreparedWorkloadDeployment? Deployment { get; } = deployment;
    public string ExpectedState { get; } = expectedState;
    public PreparedWorkloadReuse? Reuse { get; init; }
    public void Dispose()
    {
        Deployment?.Dispose();
        Reuse?.Dispose();
    }

    public string CaptureState(IReadOnlyList<WorkloadEntry> installed)
        => WorkloadStateSnapshot.CaptureAffected(installed, PackageId, LogicalPackage?.PackageId);
}
