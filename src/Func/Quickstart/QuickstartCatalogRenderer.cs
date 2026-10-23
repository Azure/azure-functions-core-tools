// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Text.Json;
using Azure.Functions.Cli.Console;

namespace Azure.Functions.Cli.Quickstart;

internal sealed class QuickstartCatalogRenderer(IInteractionService interaction)
{
    private readonly IInteractionService _interaction = interaction ?? throw new ArgumentNullException(nameof(interaction));

    public void WriteProvenance(QuickstartCatalogProvenance provenance)
    {
        ArgumentNullException.ThrowIfNull(provenance);
        _interaction.WriteHint($"Catalog transport: {provenance.Origin}. This is not artifact approval.");
        if (provenance.SourceUrl is { } source)
        {
            _interaction.WriteLine(line => line.Muted($"Catalog source: {source}"));
        }

        if (provenance.CachedAt is { } cachedAt)
        {
            _interaction.WriteLine(line => line.Muted($"Catalog cache timestamp: {cachedAt:O}"));
        }

        if (provenance.Origin == QuickstartCatalogOrigin.StaleCache)
        {
            _interaction.WriteWarning("Showing stale catalog metadata. Check your network connection before relying on current availability.");
        }
    }

    public void WriteResults(IReadOnlyList<QuickstartCatalogEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        if (entries.Count == 0)
        {
            _interaction.WriteWarning("No templates match these discovery filters. Change or clear the filters to browse again.");
            return;
        }

        _interaction.WriteTable(
            ["ID", "Name", "Language", "Resource", "IaC", "Package metadata"],
            entries.Select(item => new[]
            {
                item.Entry.Id, item.Entry.DisplayName, item.Entry.Language, item.Entry.Resource,
                item.Entry.Iac ?? "none", item.MetadataStatus.ToString(),
            }));
        _interaction.WriteHint("Discovery metadata only. No package or generated project topology has been verified.");
    }

    public void WriteActionableResults(IReadOnlyList<QuickstartActionableEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        if (entries.Count == 0)
        {
            _interaction.WriteWarning("No compatible templates are available through this CLI for the current stack and filters. Change or clear the filters, or run 'func quickstart list' for catalog diagnostics.");
            return;
        }

        _interaction.WriteTable(
            ["Name", "Availability", "Stack", "Resource", "ID", "Package version", "Description"],
            entries.Select(item => new[]
            {
                item.Entry.Entry.DisplayName,
                item.Availability switch
                {
                    QuickstartAvailability.AvailableToInstall => "Available to install",
                    QuickstartAvailability.Installed => "Installed",
                    QuickstartAvailability.UpdateAvailable => "Update available",
                    _ => throw new ArgumentOutOfRangeException(nameof(entries)),
                },
                item.Stack,
                item.Entry.Entry.Resource,
                item.Entry.Entry.Id,
                item.Entry.PackageReference?.Version ?? "-",
                item.Entry.Entry.ShortDescription ?? "-",
            }));
        _interaction.WriteHint("Only currently qualified templates are selectable. Acquisition and project creation still require their separate safeguards.");
    }

    public void WriteDetails(QuickstartCatalogEntry item, string displayLanguage)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentException.ThrowIfNullOrWhiteSpace(displayLanguage);
        QuickstartEntry entry = item.Entry;
        _interaction.WriteLine(line => line.Heading(entry.DisplayName));
        _interaction.WriteDefinitionList(
        [
            new("ID", entry.Id),
            new("Language (discovery)", displayLanguage),
            new("Resource", entry.Resource),
            new("IaC", entry.Iac ?? "none"),
            new("Git Ref", entry.GitRef ?? "-"),
            new("Repo", entry.RepositoryUrl),
            new("Path", entry.FolderPath),
            new("Package metadata", item.MetadataStatus.ToString()),
        ]);
        _interaction.WriteWarning("Package claims are unverified. Discovery language is not proof of single-stack generated projects.");
        if (item.MetadataDiagnostic is { } diagnostic)
        {
            _interaction.WriteWarning(diagnostic);
        }
        else if (item.MetadataStatus == QuickstartPackageMetadataStatus.Absent)
        {
            _interaction.WriteHint("Legacy entry without a package reference. Source details remain available for browsing.");
        }

        if (item.PackageReference is { } package)
        {
            _interaction.WriteDefinitionList(
            [
                new("Claimed package", package.PackageId),
                new("Claimed version", package.Version),
                new("Claimed package source", package.Source),
                new("Claimed template identity", package.TemplateIdentity),
                new("Claimed group identity", package.GroupIdentity ?? "-"),
            ]);
            if (package.Artifact is { } artifact)
            {
                _interaction.WriteLine(line => line.Plain($"Claimed {artifact.Algorithm}: {artifact.Digest}"));
                _interaction.WriteLine(line => line.Plain($"Unverified evidence reference (not fetched): {artifact.EvidenceReference}"));
            }

            WriteClaim("Claimed projects (uninterpreted)", package.Projects);
            WriteClaim("Claimed required workloads (uninterpreted)", package.RequiredWorkloads);
        }

        if (entry.ShortDescription is { } shortDescription)
        {
            _interaction.WriteLine(line => line.Plain(shortDescription));
        }

        if (entry.LongDescription is { } longDescription)
        {
            _interaction.WriteLine(line => line.Plain(longDescription));
        }

        if (entry.WhatsIncluded is { Count: > 0 } included)
        {
            _interaction.WriteSectionHeader("What's included");
            foreach (string text in included)
            {
                _interaction.WriteLine(line => line.Plain(text));
            }
        }
    }

    private void WriteClaim(string label, JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Undefined)
        {
            _interaction.WriteLine(line => line.Plain($"{label}: {value.GetRawText()}"));
        }
    }
}
