// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Common;
using NuGet.Versioning;

namespace Azure.Functions.Cli.Templates.Acquisition;

internal sealed record BasicCompanionManifest(string Revision, IReadOnlyList<BasicStackMapping> Stacks, IReadOnlyList<BasicTemplateArtifact> Artifacts);

internal sealed record BasicStackMapping(string PackageId, VersionRange Versions, IReadOnlyList<string> Sources,
    IReadOnlyList<BasicCompanionRequirement> Companions);

internal sealed record BasicCompanionRequirement(string PackageId, VersionRange Versions);

internal sealed record BasicTemplateArtifact(string PackageId, NuGetVersion Version, string Source, string Sha256,
    string ContentRevision, IReadOnlyList<BasicTemplateDefinition> Templates);

internal sealed record BasicTemplateDefinition(string Path, string Identity, string Type);

internal sealed record BasicCompanionPlan(string PackageId, VersionRange Versions, IReadOnlyList<BasicTemplateArtifact> Candidates);

internal sealed record BasicAcquisitionPlan(string Revision, string Source, IReadOnlyList<BasicCompanionPlan> Companions);

internal sealed record BasicAcquisitionRequest(string StackPackageId, NuGetVersion StackVersion, WorkingDirectory Directory,
    string? Source = null, bool Check = false, bool Offline = false, bool IncludePrerelease = false);

internal enum BasicAcquisitionStatus
{
    Completed,
    NotApplicable,
    Unavailable,
    Rejected,
    Incomplete,
    PartialFailure,
    Canceled,
}

internal enum BasicAcquisitionFailure
{
    MissingMapping,
    InvalidMapping,
    SourceConflict,
    RangeConflict,
    OwnershipUnavailable,
    InstalledConflict,
    PayloadUnavailable,
    InvalidArtifact,
    InstallFailed,
}

internal sealed record BasicAcquisitionStep(string PackageId, string Version, bool Reused);

internal sealed record BasicAcquisitionResult(BasicAcquisitionStatus Status, string? Revision, string? Source,
    IReadOnlyList<BasicAcquisitionStep> Completed, BasicAcquisitionFailure? Failure = null, string? Message = null, Exception? Cause = null);

internal sealed class BasicAcquisitionException(BasicAcquisitionFailure failure, string message, Exception? innerException = null)
    : Exception(message, innerException)
{
    public BasicAcquisitionFailure Failure { get; } = failure;
}

/// <summary>
/// Supplies lifecycle-owned source and pin evidence for an actual installed artifact, not a guessed installed version.
/// </summary>
internal interface IBasicTemplateOwnership
{
    public Task<BasicTemplateOwner?> GetAsync(string packageId, string version, string sha256, CancellationToken cancellationToken);
}

internal sealed record BasicTemplateOwner(string Source, string? PinnedVersion);

internal sealed class UnavailableBasicTemplateOwnership : IBasicTemplateOwnership
{
    public Task<BasicTemplateOwner?> GetAsync(string packageId, string version, string sha256, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packageId);
        ArgumentException.ThrowIfNullOrWhiteSpace(version);
        ArgumentException.ThrowIfNullOrWhiteSpace(sha256);
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<BasicTemplateOwner?>(null);
    }
}
