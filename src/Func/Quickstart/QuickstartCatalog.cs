// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Text.Json;
using Azure.Functions.Cli.Projects;

namespace Azure.Functions.Cli.Quickstart;

internal interface IQuickstartCatalogSource
{
    public Task<QuickstartCatalogDocument> GetCatalogDocumentAsync(CancellationToken cancellationToken);
}

internal interface IQuickstartCatalogService
{
    public Task<QuickstartCatalog> SearchAsync(QuickstartCatalogQuery query, CancellationToken cancellationToken);
}

/// <summary>
/// Filters legacy discovery metadata, not generated topology or installed template eligibility.
/// </summary>
internal sealed record QuickstartCatalogQuery(
    string? Language = null, string? Resource = null, string? Iac = null, string? Search = null,
    IProjectStack? LegacyStack = null, IQuickstartProvider? LegacyLanguageProvider = null);

internal enum QuickstartCatalogOrigin
{
    Download,
    FreshCache,
    RevalidatedCache,
    StaleCache,
    LocalFile,
}

/// <summary>
/// Describes metadata transport only. Neither cache freshness nor a source URL establishes artifact approval.
/// </summary>
internal sealed record QuickstartCatalogProvenance(QuickstartCatalogOrigin Origin, string? SourceUrl, DateTimeOffset? CachedAt);

internal sealed record QuickstartCatalogDocument(
    QuickstartManifest Manifest, IReadOnlyList<QuickstartManifestItem> Items, QuickstartCatalogProvenance Provenance);

internal sealed record QuickstartManifestItem(QuickstartEntry Entry, JsonElement PackageReference, string? MetadataDiagnostic = null);

internal sealed record QuickstartCatalog(IReadOnlyList<QuickstartCatalogEntry> Entries, QuickstartCatalogProvenance Provenance);

internal sealed record QuickstartCatalogEntry(
    QuickstartEntry Entry, QuickstartPackageMetadataStatus MetadataStatus,
    QuickstartPackageReference? PackageReference, string? MetadataDiagnostic);

internal enum QuickstartPackageMetadataStatus
{
    Absent,
    Null,
    Invalid,
    Unsupported,
    Conflicting,
    Unverified,
}

/// <summary>
/// Holds an advertised reference in the proposed discovery schema, never an acquisition authorization.
/// </summary>
internal sealed record QuickstartPackageReference(
    string PackageId, string Version, string Source, string TemplateIdentity, string? GroupIdentity,
    QuickstartAdvertisedArtifact? Artifact, JsonElement Projects, JsonElement RequiredWorkloads);

internal sealed record QuickstartAdvertisedArtifact(string Algorithm, string Digest, string EvidenceReference);
