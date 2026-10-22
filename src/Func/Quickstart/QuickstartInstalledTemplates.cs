// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Projects;
using Azure.Functions.Cli.Templates;
using Azure.Functions.Cli.Templates.Acquisition;
using Azure.Functions.Cli.Templates.Engine;

namespace Azure.Functions.Cli.Quickstart;

internal sealed record QuickstartInstalledQuery(WorkingDirectory Directory, NativeTemplateOwnerIdentity Owner, BundleChannel? Channel = null);

internal sealed record QuickstartInstalledProjection(QuickstartCatalog Catalog, IReadOnlyList<InstalledTemplateArtifact> Installed);

/// <summary>
/// Suppresses duplicate discovery rows only after independently proving a corresponding current installation.
/// </summary>
internal sealed class QuickstartInstalledTemplates(ITemplaterFactory templates, IBoundInitScopeFactory scopes)
{
    private readonly ITemplaterFactory _templates = templates ?? throw new ArgumentNullException(nameof(templates));
    private readonly IBoundInitScopeFactory _scopes = scopes ?? throw new ArgumentNullException(nameof(scopes));

    public async Task<QuickstartInstalledProjection> ProjectAsync(QuickstartCatalog catalog, QuickstartInstalledQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(query.Directory);
        ArgumentNullException.ThrowIfNull(query.Owner);
        using BoundInitScope scope = await _scopes.CreateAsync(cancellationToken);
        using Templater templater = await _templates.CreateProjectAsync(new(query.Directory) { RequireCleanStore = true },
            scope.Availability, scope.Support, cancellationToken);
        IReadOnlyList<InstalledTemplateArtifact> installed = await templater.ProjectArtifactsAsync(
            scope.Verifier, query.Owner, query.Channel, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        return new(catalog with
        {
            Entries = catalog.Entries.Where(entry => !installed.Any(item => IsExactDuplicate(entry, item.Selection))).ToList().AsReadOnly(),
        }, installed);
    }

    internal static bool IsExactDuplicate(QuickstartCatalogEntry entry, NativeTemplateSelection? installed)
    {
        ArgumentNullException.ThrowIfNull(entry);
        return installed is not null && entry.MetadataStatus == QuickstartPackageMetadataStatus.Unverified
            && entry.PackageReference is { Artifact: { } artifact } reference
            && string.Equals(reference.TemplateIdentity, installed.TemplateIdentity, StringComparison.Ordinal)
            && string.Equals(reference.PackageId, installed.Artifact.PackageId, StringComparison.OrdinalIgnoreCase)
            && reference.Version == installed.Artifact.Version
            && reference.Source == installed.Source
            && string.Equals(artifact.Algorithm, "sha256", StringComparison.OrdinalIgnoreCase)
            && string.Equals(artifact.Digest, installed.Artifact.Sha256, StringComparison.OrdinalIgnoreCase);
    }
}
