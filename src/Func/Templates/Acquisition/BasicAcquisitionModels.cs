// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Projects;
using Azure.Functions.Cli.Workloads.Storage;
using NuGet.Versioning;

namespace Azure.Functions.Cli.Templates.Acquisition;

internal sealed record BasicCompanionManifest(string Revision, IReadOnlyList<BasicStackMapping> Stacks, IReadOnlyList<BasicTemplateArtifact> Artifacts)
{
    public int SchemaVersion { get; init; } = 1;
}

internal sealed record BasicStackMapping(string PackageId, VersionRange Versions, IReadOnlyList<string> Sources,
    IReadOnlyList<BasicCompanionRequirement> Companions)
{
    public NativeTemplateAssociationPolicy? NativePolicy { get; init; }
}

internal sealed record BasicCompanionRequirement(string PackageId, VersionRange Versions)
{
    public IReadOnlyList<NativeTemplateContentRole> RequiredRoles { get; init; } = [];

    public BasicCompanionKind Kind { get; init; }
}

internal enum BasicCompanionKind
{
    Required,
    Optional,
}

internal sealed record NativeTemplateAssociationPolicy(string HostIdentifier, VersionRange EngineHostVersions, IReadOnlyList<BundleChannel> Channels);

internal sealed record NativeTemplateAssociationContext(WorkloadEntry SelectedStack, string HostIdentifier,
    NuGetVersion EngineHostVersion, BundleChannel? Channel, IReadOnlyList<NativeTemplateContentRole> RequiredRoles)
{
    public IReadOnlyList<NativeTemplateContentRole> RequiredRoles { get; init => field = Array.AsReadOnly(
        (value ?? throw new ArgumentNullException(nameof(value))).ToArray()); } = Array.AsReadOnly(
        (RequiredRoles ?? throw new ArgumentNullException(nameof(RequiredRoles))).ToArray());
}

internal enum NativeTemplateContentRole
{
    Function,
    AdditionalProject,
}

internal sealed record BasicTemplateArtifact(string PackageId, NuGetVersion Version, string Source, string Sha256,
    string ContentRevision, IReadOnlyList<BasicTemplateDefinition> Templates)
{
    public IReadOnlyList<BasicTemplateDefinition> Templates
    {
        get;
        init => field = Array.AsReadOnly<BasicTemplateDefinition>([.. value ?? throw new ArgumentNullException(nameof(value))]);
    } = Array.AsReadOnly<BasicTemplateDefinition>([.. Templates ?? throw new ArgumentNullException(nameof(Templates))]);

    public IReadOnlyList<NativeTemplateContentDeclaration> ContentRoles
    {
        get;
        init => field = Array.AsReadOnly<NativeTemplateContentDeclaration>([.. value ?? throw new ArgumentNullException(nameof(value))]);
    } = [];
}

internal sealed record NativeTemplateContentDeclaration(string Identity, NativeTemplateContentRole Role);

internal sealed record BasicTemplateDefinition(string Path, string Identity, string Type);

internal sealed record BasicTemplateArchiveInventory(string PackageId, string Version, IReadOnlyList<string> PackageTypes,
    IReadOnlyList<string> Files, IReadOnlyList<BasicTemplateDefinition> Templates)
{
    public IReadOnlyList<string> PackageTypes { get; init => field = Array.AsReadOnly(
        [.. value ?? throw new ArgumentNullException(nameof(value))]); } = Array.AsReadOnly(
        [.. PackageTypes ?? throw new ArgumentNullException(nameof(PackageTypes))]);

    public IReadOnlyList<string> Files { get; init => field = Array.AsReadOnly(
        [.. value ?? throw new ArgumentNullException(nameof(value))]); } = Array.AsReadOnly(
        [.. Files ?? throw new ArgumentNullException(nameof(Files))]);

    public IReadOnlyList<BasicTemplateDefinition> Templates { get; init => field = Array.AsReadOnly(
        [.. value ?? throw new ArgumentNullException(nameof(value))]); } = Array.AsReadOnly(
        [.. Templates ?? throw new ArgumentNullException(nameof(Templates))]);
}

internal sealed record BasicTemplateOwnedState(NativeTemplateOwnerReceipt Receipt, string ReceiptSha256, string ArchivePath,
    BasicTemplateArchiveInventory Archive, NativeTemplateOwnerIdentity Owner, string WorkloadInventory,
    string? SelectedProjectIdentity);

internal sealed record BasicOwnedTemplateUpdate(BasicTemplateOwnedState Before, BasicTemplateArtifact After);

internal sealed record BasicCompanionPlan(string PackageId, VersionRange Versions, IReadOnlyList<BasicTemplateArtifact> Candidates)
{
    public IReadOnlyList<BasicTemplateArtifact> Candidates { get; init => field = Freeze(value); } = Freeze(Candidates);

    internal static IReadOnlyList<BasicTemplateArtifact> Freeze(IReadOnlyList<BasicTemplateArtifact> artifacts)
    {
        ArgumentNullException.ThrowIfNull(artifacts);
        return Array.AsReadOnly<BasicTemplateArtifact>([.. artifacts]);
    }
}

internal sealed record BasicAcquisitionPlan(string Revision, string Source, IReadOnlyList<BasicCompanionPlan> Companions)
{
    public IReadOnlyList<BasicCompanionPlan> Companions { get; } = Array.AsReadOnly(
        (Companions ?? throw new ArgumentNullException(nameof(Companions))).ToArray());

    public IReadOnlyList<NativeTemplateSelection> OptionalChoices { get; init => field = Array.AsReadOnly(
        (value ?? throw new ArgumentNullException(nameof(value))).ToArray()); } = [];

    public NativeTemplateOwnerIdentity? Owner { get; init; }
    public string? HostIdentifier { get; init; }
    public NuGetVersion? EngineHostVersion { get; init; }
    public BundleChannel? Channel { get; init; }
    public IReadOnlyList<NativeTemplateContentRole> RequiredRoles { get; init => field = Array.AsReadOnly(
        (value ?? throw new ArgumentNullException(nameof(value))).ToArray()); } = [];
}

internal sealed record BasicAcquisitionRequest(string StackPackageId, NuGetVersion StackVersion, WorkingDirectory Directory,
    string? Source = null, bool Check = false, bool Offline = false, bool IncludePrerelease = false)
{
    public NativeTemplateAssociationContext? NativeContext { get; init; }

    public IReadOnlyList<NativeTemplateSelection> OptionalChoices { get; init => field = Array.AsReadOnly(
        (value ?? throw new ArgumentNullException(nameof(value))).ToArray()); } = [];

    public BasicAcquisitionApproval? Approval { get; init; }
}

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
    HostConflict,
    ChannelConflict,
    OwnershipUnavailable,
    InstalledConflict,
    PayloadUnavailable,
    InvalidArtifact,
    InstallFailed,
    PlanChanged,
    RecoveryRequired,
}

internal sealed record BasicAcquisitionStep(string PackageId, string Version, bool Reused);

/// <summary>
/// Reports the requested stack's acquisition or repair, not whole-machine companion completeness.
/// Retained stacks constrain compatibility but are not additional repair targets.
/// </summary>
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
