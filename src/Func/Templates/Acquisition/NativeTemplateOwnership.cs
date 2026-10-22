// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Text.Json;
using System.Text.Json.Serialization;
using System.Security.Cryptography;
using System.Text;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Configuration;
using NuGet.Packaging;
using NuGet.Versioning;

namespace Azure.Functions.Cli.Templates.Acquisition;

internal interface INativeTemplateOwnership : IBasicTemplateOwnership
{
    public Task RecordAsync(BasicTemplateArtifact artifact, CancellationToken cancellationToken);
    public Task<NativeTemplateOwnerReceipt?> GetRetainedAsync(string packageId, CancellationToken cancellationToken);
    public Task<NativeTemplateOwnerProof?> GetRetainedProofAsync(string packageId, CancellationToken cancellationToken);
    public NativeTemplateOwnerProof CreateProof(BasicTemplateArtifact artifact);
    public Task TransitionAsync(NativeTemplateOwnerProof expected, BasicTemplateArtifact artifact, CancellationToken cancellationToken);
}

/// <summary>
/// Retains exact artifact provenance inside the native hive's existing snapshot and recovery boundary.
/// </summary>
internal sealed class NativeTemplateOwnership(IFileSystem fileSystem, CliConfigurationPathsOptions paths) : INativeTemplateOwnership
{
    private readonly IFileSystem _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
    private readonly CliConfigurationPathsOptions _paths = paths ?? throw new ArgumentNullException(nameof(paths));

    public async Task<BasicTemplateOwner?> GetAsync(string packageId, string version, string sha256, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packageId);
        ArgumentException.ThrowIfNullOrWhiteSpace(version);
        ArgumentException.ThrowIfNullOrWhiteSpace(sha256);
        ValidateHash(sha256);
        NativeTemplateOwnerProof? proof = await GetRetainedProofAsync(packageId, cancellationToken);
        if (proof is null)
        {
            return null;
        }
        NativeTemplateOwnerReceipt receipt = proof.Receipt;
        if (receipt.Version != version || !string.Equals(receipt.Sha256, sha256, StringComparison.OrdinalIgnoreCase))
        {
            throw Conflict(packageId);
        }
        return new(receipt.Source, receipt.PinnedVersion);
    }

    public async Task<NativeTemplateOwnerReceipt?> GetRetainedAsync(string packageId, CancellationToken cancellationToken)
        => (await GetRetainedProofAsync(packageId, cancellationToken))?.Receipt;

    public async Task<NativeTemplateOwnerProof?> GetRetainedProofAsync(string packageId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packageId);
        using Stream? stream = _fileSystem.OpenReadIfExists(ReceiptPath(packageId));
        if (stream is null)
        {
            return null;
        }

        using MemoryStream raw = new();
        await stream.CopyToAsync(raw, cancellationToken);
        byte[] bytes = raw.ToArray();
        using StreamReader text = new(new MemoryStream(bytes), Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        string json = await text.ReadToEndAsync(cancellationToken);
        NativeTemplateOwnerReceipt receipt;
        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object
                || document.RootElement.EnumerateObject().GroupBy(property => property.Name, StringComparer.OrdinalIgnoreCase).Any(group => group.Count() > 1))
            {
                throw Conflict(packageId);
            }
            receipt = JsonSerializer.Deserialize(json, NativeTemplateOwnershipJsonContext.Default.NativeTemplateOwnerReceipt)
                ?? throw Conflict(packageId);
        }
        catch (JsonException exception)
        {
            throw new BasicAcquisitionException(BasicAcquisitionFailure.InstalledConflict,
                $"Native provenance for '{packageId}' is malformed. Preserve the store and use its owning lifecycle to repair it.", exception);
        }

        if (!string.Equals(receipt.PackageId, packageId, StringComparison.OrdinalIgnoreCase)
            || !NuGetVersion.TryParse(receipt.Version, out _)
            || receipt.Sha256 is null || receipt.Sha256.Length != 64 || receipt.Sha256.Any(character => !char.IsAsciiHexDigit(character))
            || string.IsNullOrWhiteSpace(receipt.ContentRevision) || string.IsNullOrWhiteSpace(receipt.Source)
            || receipt.PinnedVersion != receipt.Version)
        {
            throw Conflict(packageId);
        }
        try
        {
            receipt = receipt with { Source = BasicCompanionManifestReader.Source(receipt.Source) };
        }
        catch (BasicAcquisitionException exception)
        {
            throw new BasicAcquisitionException(BasicAcquisitionFailure.InstalledConflict,
                $"Native provenance for '{packageId}' has an invalid source. Preserve the store and use its owning lifecycle to repair it.", exception);
        }

        return new(receipt, Convert.ToHexStringLower(SHA256.HashData(bytes)));
    }

    public async Task RecordAsync(BasicTemplateArtifact artifact, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(artifact);
        ArgumentNullException.ThrowIfNull(artifact.Version);
        ArgumentNullException.ThrowIfNull(artifact.ContentRoles);
        ArgumentException.ThrowIfNullOrWhiteSpace(artifact.ContentRevision);
        ValidateHash(artifact.Sha256);
        if (artifact.ContentRoles.Count == 0)
        {
            throw new ArgumentException("Native provenance requires an explicit qualified external content role.", nameof(artifact));
        }
        string version = artifact.Version.ToNormalizedString();
        NativeTemplateOwnerReceipt receipt = CreateProof(artifact).Receipt;
        NativeTemplateOwnerReceipt? retained = await GetRetainedAsync(artifact.PackageId, cancellationToken);
        if (retained is not null)
        {
            if (retained.Version != receipt.Version || retained.Source != receipt.Source
                || !string.Equals(retained.Sha256, receipt.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                throw Conflict(artifact.PackageId);
            }
            return;
        }
        await _fileSystem.WriteAllTextAsync(ReceiptPath(artifact.PackageId),
            JsonSerializer.Serialize(receipt, NativeTemplateOwnershipJsonContext.Default.NativeTemplateOwnerReceipt), cancellationToken);
    }

    public NativeTemplateOwnerProof CreateProof(BasicTemplateArtifact artifact)
    {
        ArgumentNullException.ThrowIfNull(artifact);
        ArgumentNullException.ThrowIfNull(artifact.Version);
        ArgumentException.ThrowIfNullOrWhiteSpace(artifact.ContentRevision);
        ValidateHash(artifact.Sha256);
        string version = artifact.Version.ToNormalizedString();
        NativeTemplateOwnerReceipt receipt = new(artifact.PackageId, version, artifact.Sha256,
            BasicCompanionManifestReader.Source(artifact.Source), artifact.ContentRevision, version);
        byte[] bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(receipt,
            NativeTemplateOwnershipJsonContext.Default.NativeTemplateOwnerReceipt));
        return new(receipt, Convert.ToHexStringLower(SHA256.HashData(bytes)));
    }

    public async Task TransitionAsync(NativeTemplateOwnerProof expected, BasicTemplateArtifact artifact,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(expected);
        ArgumentNullException.ThrowIfNull(artifact);
        if (!string.Equals(expected.Receipt.PackageId, artifact.PackageId, StringComparison.OrdinalIgnoreCase)
            || expected.Receipt.Source != BasicCompanionManifestReader.Source(artifact.Source)
            || !NuGetVersion.TryParse(expected.Receipt.Version, out NuGetVersion? oldVersion)
            || NuGet.Versioning.VersionComparer.VersionRelease.Compare(artifact.Version, oldVersion) <= 0
            || artifact.ContentRoles.Count == 0)
        {
            throw Conflict(artifact.PackageId);
        }

        NativeTemplateOwnerProof? actual = await GetRetainedProofAsync(artifact.PackageId, cancellationToken);
        if (actual is null || actual.Receipt != expected.Receipt
            || !string.Equals(actual.RawSha256, expected.RawSha256, StringComparison.OrdinalIgnoreCase))
        {
            throw Conflict(artifact.PackageId);
        }

        NativeTemplateOwnerProof replacement = CreateProof(artifact);
        string json = JsonSerializer.Serialize(replacement.Receipt,
            NativeTemplateOwnershipJsonContext.Default.NativeTemplateOwnerReceipt);
        await _fileSystem.WriteAllTextAsync(ReceiptPath(artifact.PackageId), json, cancellationToken);
    }

    private string ReceiptPath(string packageId)
    {
        if (!PackageIdValidator.IsValidPackageId(packageId))
        {
            throw new ArgumentException("A literal NuGet package identity is required.", nameof(packageId));
        }
        return Path.Combine(_paths.Home, "templates", ".native-template-owners", packageId.ToLowerInvariant() + ".json");
    }

    private static void ValidateHash(string sha256)
    {
        if (string.IsNullOrWhiteSpace(sha256) || sha256.Length != 64 || sha256.Any(character => !char.IsAsciiHexDigit(character)))
        {
            throw new ArgumentException("An exact SHA-256 artifact digest is required.", nameof(sha256));
        }
    }

    private static BasicAcquisitionException Conflict(string packageId)
        => new(BasicAcquisitionFailure.InstalledConflict,
            $"Native provenance for '{packageId}' conflicts with the installed identity, version or bytes. No replacement is authorized.");
}

internal sealed record NativeTemplateOwnerReceipt(string PackageId, string Version, string Sha256, string Source,
    string ContentRevision, string? PinnedVersion);

internal sealed record NativeTemplateOwnerProof(NativeTemplateOwnerReceipt Receipt, string RawSha256);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow)]
[JsonSerializable(typeof(NativeTemplateOwnerReceipt))]
internal sealed partial class NativeTemplateOwnershipJsonContext : JsonSerializerContext;
