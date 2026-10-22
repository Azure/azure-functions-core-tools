// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using Azure.Functions.Cli.Projects;
using NuGet.Packaging;
using NuGet.Versioning;

namespace Azure.Functions.Cli.Quickstart;

/// <summary>
/// Projects proposed package references without opening the installed store or authorizing acquisition.
/// </summary>
internal sealed class QuickstartCatalogService(IQuickstartCatalogSource source) : IQuickstartCatalogService
{
    private readonly IQuickstartCatalogSource _source = source ?? throw new ArgumentNullException(nameof(source));

    public async Task<QuickstartCatalog> SearchAsync(QuickstartCatalogQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        cancellationToken.ThrowIfCancellationRequested();
        InstalledProjectStackCatalog? stack = query.LegacyStack is null ? null : new([query.LegacyStack]);
        if (query.LegacyLanguageProvider is not null
            && !string.Equals(query.LegacyLanguageProvider.Stack, query.LegacyStack?.Stack, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("The legacy language provider must belong to the supplied stack capability.", nameof(query));
        }

        QuickstartCatalogDocument document = await _source.GetCatalogDocumentAsync(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        List<QuickstartCatalogEntry> entries = [.. document.Items.Select(ReadMetadata)];
        var duplicateIds = document.Items.GroupBy(item => item.Entry.Id, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1).Select(group => group.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        QuickstartPackageReference[] references = [.. entries.Select(entry => entry.PackageReference).OfType<QuickstartPackageReference>()];
        var conflictingIdentities = references.GroupBy(reference => reference.TemplateIdentity, StringComparer.Ordinal)
            .Where(group => group.Select(reference => reference.PackageId).Distinct(StringComparer.OrdinalIgnoreCase).Count() > 1)
            .Select(group => group.Key).ToHashSet(StringComparer.Ordinal);
        var conflictingArtifacts = references.GroupBy(reference => (reference.PackageId.ToUpperInvariant(), Version: NuGetVersion.Parse(reference.Version)))
            .Where(group => group.Select(reference => reference.Source).Distinct(StringComparer.Ordinal).Count() > 1
                || group.Select(reference => reference.Artifact).OfType<QuickstartAdvertisedArtifact>()
                    .Select(artifact => artifact.Digest).Distinct(StringComparer.OrdinalIgnoreCase).Count() > 1)
            .SelectMany(group => group).ToHashSet();
        HashSet<QuickstartEntry> filtered = [.. document.Manifest.Filter(query.Language, query.Resource, query.Iac, query.Search)];
        List<QuickstartCatalogEntry> results = [];
        foreach (QuickstartCatalogEntry entry in entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            QuickstartCatalogEntry projected = entry;
            if (duplicateIds.Contains(entry.Entry.Id)
                || (entry.PackageReference is { } reference
                    && (conflictingIdentities.Contains(reference.TemplateIdentity) || conflictingArtifacts.Contains(reference))))
            {
                projected = entry with
                {
                    MetadataStatus = QuickstartPackageMetadataStatus.Conflicting,
                    MetadataDiagnostic = "Catalog IDs or template package ownership conflict. Resolve the manifest assignment before use.",
                };
            }

            if (filtered.Contains(entry.Entry) && MatchesLegacyStack(entry.Entry, query, stack))
            {
                results.Add(projected);
            }
        }

        return new(results.AsReadOnly(), document.Provenance);
    }

    private static bool MatchesLegacyStack(QuickstartEntry entry, QuickstartCatalogQuery query, InstalledProjectStackCatalog? stack)
    {
        if (stack is null)
        {
            return true;
        }

        string language = entry.Language;
        if (query.LegacyLanguageProvider is { } provider)
        {
            if (!provider.ManifestLanguages.Contains(language, StringComparer.OrdinalIgnoreCase))
            {
                return false;
            }

            language = provider.GetDisplayLanguage(language);
        }

        return stack.GetCanonicalLanguage(language) is not null;
    }

    private static QuickstartCatalogEntry ReadMetadata(QuickstartManifestItem item)
    {
        if (item.MetadataDiagnostic is not null)
        {
            return Reject(QuickstartPackageMetadataStatus.Invalid, item.MetadataDiagnostic);
        }

        JsonElement metadata = item.PackageReference;
        if (metadata.ValueKind == JsonValueKind.Undefined)
        {
            return new(item.Entry, QuickstartPackageMetadataStatus.Absent, null, null);
        }

        if (metadata.ValueKind == JsonValueKind.Null)
        {
            return new(item.Entry, QuickstartPackageMetadataStatus.Null, null, "No package reference was supplied.");
        }

        Dictionary<string, JsonElement>? properties = ReadProperties(metadata);
        if (properties is null || !properties.TryGetValue("schemaVersion", out JsonElement schema)
            || schema.ValueKind != JsonValueKind.Number || !schema.TryGetInt32(out int version) || version < 1)
        {
            return Reject(QuickstartPackageMetadataStatus.Invalid, "packageReference must be an object with a positive integer schemaVersion.");
        }

        if (version != 1)
        {
            return Reject(QuickstartPackageMetadataStatus.Unsupported, $"Package reference schema version {version} is not supported.");
        }

        string[] known = ["schemaVersion", "packageId", "version", "source", "templateIdentity", "groupIdentity",
            "artifact", "projects", "requiredWorkloads"];
        if (properties.Keys.Any(key => !known.Contains(key, StringComparer.OrdinalIgnoreCase)))
        {
            return Reject(QuickstartPackageMetadataStatus.Invalid, "Package reference schema version 1 contains an unknown property.");
        }

        string? id = ReadString(properties, "packageId");
        string? packageVersion = ReadString(properties, "version");
        string? source = ReadString(properties, "source");
        string? identity = ReadString(properties, "templateIdentity");
        string? group = ReadString(properties, "groupIdentity");
        if (!IsText(id) || !PackageIdValidator.IsValidPackageId(id)
            || !IsText(packageVersion) || !NuGetVersion.TryParse(packageVersion, out _)
            || !IsText(source) || !Uri.TryCreate(source, UriKind.Absolute, out Uri? uri)
            || uri.Scheme != Uri.UriSchemeHttps || string.IsNullOrEmpty(uri.Host)
            || uri.UserInfo.Length > 0 || uri.Fragment.Length > 0
            || !IsText(identity)
            || (properties.ContainsKey("groupIdentity") && !IsText(group)))
        {
            return Reject(QuickstartPackageMetadataStatus.Invalid,
                "Supply a package ID, exact NuGet version, absolute HTTPS source without credentials or fragment, and full template identity.");
        }

        QuickstartAdvertisedArtifact? artifact = null;
        if (properties.TryGetValue("artifact", out JsonElement artifactElement))
        {
            Dictionary<string, JsonElement>? artifactProperties = ReadProperties(artifactElement);
            string? algorithm = artifactProperties is null ? null : ReadString(artifactProperties, "algorithm");
            string? digest = artifactProperties is null ? null : ReadString(artifactProperties, "digest");
            string? evidence = artifactProperties is null ? null : ReadString(artifactProperties, "evidenceReference");
            string[] artifactKeys = ["algorithm", "digest", "evidenceReference"];
            if (artifactProperties is null || artifactProperties.Keys.Any(key => !artifactKeys.Contains(key, StringComparer.OrdinalIgnoreCase))
                || !IsText(algorithm) || !string.Equals(algorithm, "sha256", StringComparison.OrdinalIgnoreCase)
                || digest is null || digest.Length != 64 || !digest.All(Uri.IsHexDigit) || !IsText(evidence))
            {
                return Reject(QuickstartPackageMetadataStatus.Invalid,
                    "An advertised artifact requires a sha256 digest and an inert evidenceReference. No artifact approval is verified here.");
            }

            artifact = new(algorithm, digest, evidence);
        }

        properties.TryGetValue("projects", out JsonElement projects);
        properties.TryGetValue("requiredWorkloads", out JsonElement requirements);
        return new(item.Entry, QuickstartPackageMetadataStatus.Unverified,
            new(id, packageVersion, source, identity, group, artifact, projects, requirements),
            "Advertised package metadata only. Source policy, publication evidence and artifact approval have not been verified.");

        QuickstartCatalogEntry Reject(QuickstartPackageMetadataStatus status, string diagnostic) =>
            new(item.Entry, status, null, diagnostic);
    }

    private static Dictionary<string, JsonElement>? ReadProperties(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        Dictionary<string, JsonElement> properties = new(StringComparer.OrdinalIgnoreCase);
        foreach (JsonProperty property in element.EnumerateObject())
        {
            if (!properties.TryAdd(property.Name, property.Value))
            {
                return null;
            }
        }

        return properties;
    }

    private static string? ReadString(Dictionary<string, JsonElement> properties, string name) =>
        properties.TryGetValue(name, out JsonElement value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static bool IsText([NotNullWhen(true)] string? value) =>
        !string.IsNullOrWhiteSpace(value) && value == value.Trim() && !value.Any(char.IsControl);
}
