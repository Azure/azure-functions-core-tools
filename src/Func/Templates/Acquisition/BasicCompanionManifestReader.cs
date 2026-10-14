// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Text.Json;
using System.Text.Json.Serialization;
using Azure.Functions.Cli.Bundles;
using NuGet.Versioning;

namespace Azure.Functions.Cli.Templates.Acquisition;

internal sealed class BasicCompanionManifestReader
{
    public BasicCompanionManifest Read(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        try
        {
            using var raw = JsonDocument.Parse(json);
            RejectDuplicates(raw.RootElement);
            BasicCompanionDocument document = JsonSerializer.Deserialize(json, BasicCompanionJsonContext.Default.BasicCompanionDocument)
                ?? throw Invalid("The document is null.");
            if (document.SchemaVersion != 1)
            {
                throw Invalid($"Unsupported schema version '{document.SchemaVersion}'.");
            }

            string revision = Text(document.Revision, "revision");
            List<BasicStackMapping> stacks = [];
            foreach (BasicStackDocument entry in document.Stacks ?? throw Invalid("stacks must be an array."))
            {
                if (entry is null)
                {
                    throw Invalid("A stack mapping is null.");
                }

                string id = PackageId(entry.PackageId);
                VersionRange range = Range(entry.Versions);
                string[] sources = [.. (entry.Sources ?? throw Invalid($"Stack '{id}' requires sources.")).Select(Source)];
                if (sources.Length == 0 || sources.Distinct(StringComparer.Ordinal).Count() != sources.Length)
                {
                    throw Invalid($"Stack '{id}' requires distinct approved sources.");
                }

                List<BasicCompanionRequirement> companions = [];
                foreach (BasicRequirementDocument requirement in entry.Companions ?? throw Invalid($"Stack '{id}' requires companions."))
                {
                    if (requirement is null)
                    {
                        throw Invalid($"Stack '{id}' has a null companion.");
                    }

                    companions.Add(new(PackageId(requirement.PackageId), Range(requirement.Versions)));
                }

                if (companions.Count == 0 || companions.Select(c => c.PackageId).Distinct(StringComparer.OrdinalIgnoreCase).Count() != companions.Count)
                {
                    throw Invalid($"Stack '{id}' requires distinct basic companions.");
                }

                if (stacks.Any(existing => string.Equals(id, existing.PackageId, StringComparison.OrdinalIgnoreCase)
                    && VersionRangeIntersection.Intersect(range.ToNormalizedString(), existing.Versions.ToNormalizedString()) is not null))
                {
                    throw Invalid($"Stack '{id}' has ambiguous overlapping version mappings.");
                }

                stacks.Add(new(id, range, Array.AsReadOnly(sources), companions.AsReadOnly()));
            }

            List<BasicTemplateArtifact> artifacts = [];
            foreach (BasicArtifactDocument entry in document.Artifacts ?? throw Invalid("artifacts must be an array."))
            {
                if (entry is null || !entry.BasicOnly)
                {
                    throw Invalid("Every artifact requires explicit basic-only qualification.");
                }

                string id = PackageId(entry.PackageId);
                string versionText = Text(entry.Version, "artifact version");
                if (!NuGetVersion.TryParse(versionText, out NuGetVersion? version) || !string.IsNullOrEmpty(version.Metadata))
                {
                    throw Invalid($"Artifact '{id}' requires an exact NuGet version without build metadata.");
                }

                string source = Source(entry.Source);
                string hash = Text(entry.Sha256, "sha256");
                if (hash.Length != 64 || hash.Any(c => !char.IsAsciiHexDigit(c)))
                {
                    throw Invalid($"Artifact '{id}' requires a SHA-256 digest.");
                }

                List<BasicTemplateDefinition> definitions = [];
                foreach (BasicDefinitionDocument definition in entry.Templates ?? throw Invalid($"Artifact '{id}' requires templates."))
                {
                    if (definition is null)
                    {
                        throw Invalid($"Artifact '{id}' has a null template.");
                    }

                    string path = Text(definition.Path, "template path");
                    if (!path.EndsWith("/.template.config/template.json", StringComparison.Ordinal)
                        && path != ".template.config/template.json")
                    {
                        throw Invalid($"Artifact '{id}' has an invalid configuration path.");
                    }

                    if (path.Contains('\\') || path.StartsWith('/') || path.Split('/').Any(segment => segment is "" or "." or ".."))
                    {
                        throw Invalid($"Artifact '{id}' has an unsafe configuration path.");
                    }

                    string type = Text(definition.Type, "template type");
                    if (type is not ("item" or "project"))
                    {
                        throw Invalid($"Artifact '{id}' contains a non-basic template type.");
                    }

                    definitions.Add(new(path, Text(definition.Identity, "template identity"), type));
                }

                if (definitions.Count == 0
                    || definitions.Select(d => d.Path).Distinct(StringComparer.OrdinalIgnoreCase).Count() != definitions.Count
                    || definitions.Select(d => d.Identity).Distinct(StringComparer.Ordinal).Count() != definitions.Count)
                {
                    throw Invalid($"Artifact '{id}' requires a nonempty, unambiguous complete template inventory.");
                }

                if (artifacts.Any(a => string.Equals(a.PackageId, id, StringComparison.OrdinalIgnoreCase) && a.Version == version && a.Source == source))
                {
                    throw Invalid($"Artifact '{id}' {version} has duplicate approval records.");
                }

                artifacts.Add(new(id, version, source, hash.ToLowerInvariant(), Text(entry.ContentRevision, "content revision"), definitions.AsReadOnly()));
            }

            return new(revision, stacks.AsReadOnly(), artifacts.AsReadOnly());
        }
        catch (JsonException exception)
        {
            throw new BasicAcquisitionException(BasicAcquisitionFailure.InvalidMapping, "The bundled basic companion mapping is invalid JSON.", exception);
        }
    }

    internal static string Source(string? value)
    {
        string text = Text(value, "source");
        if (!Uri.TryCreate(text, UriKind.Absolute, out Uri? uri) || uri.Scheme is not ("http" or "https")
            || uri.UserInfo.Length > 0 || uri.Fragment.Length > 0)
        {
            throw Invalid("An approved source must be an absolute HTTP(S) feed without credentials or a fragment.");
        }

        return uri.AbsoluteUri;
    }

    private static string PackageId(string? value)
    {
        string text = Text(value, "package identity");
        return NuGet.Packaging.PackageIdValidator.IsValidPackageId(text)
            ? text
            : throw Invalid($"Invalid literal package identity '{text}'.");
    }

    private static VersionRange Range(string? value)
    {
        string text = Text(value, "version range");
        if (!VersionRange.TryParse(text, out VersionRange? range) || range.IsFloating
            || VersionRangeIntersection.Intersect(text, text) is null)
        {
            throw Invalid($"Invalid non-floating version range '{text}'.");
        }

        return range;
    }

    private static string Text(string? value, string field)
        => !string.IsNullOrWhiteSpace(value) && value == value.Trim() ? value : throw Invalid($"{field} must be nonempty without outer whitespace.");

    private static void RejectDuplicates(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            HashSet<string> names = new(StringComparer.OrdinalIgnoreCase);
            foreach (JsonProperty property in element.EnumerateObject())
            {
                if (!names.Add(property.Name))
                {
                    throw Invalid($"Duplicate mapping property '{property.Name}'.");
                }

                RejectDuplicates(property.Value);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement item in element.EnumerateArray())
            {
                RejectDuplicates(item);
            }
        }
    }

    private static BasicAcquisitionException Invalid(string message) => new(BasicAcquisitionFailure.InvalidMapping, message);
}

internal sealed class BasicCompanionDocument
{
    public int SchemaVersion { get; init; }
    public string? Revision { get; init; }
    public List<BasicStackDocument>? Stacks { get; init; }
    public List<BasicArtifactDocument>? Artifacts { get; init; }
}

internal sealed class BasicStackDocument
{
    public string? PackageId { get; init; }
    public string? Versions { get; init; }
    public List<string>? Sources { get; init; }
    public List<BasicRequirementDocument>? Companions { get; init; }
}

internal sealed class BasicRequirementDocument
{
    public string? PackageId { get; init; }
    public string? Versions { get; init; }
}

internal sealed class BasicArtifactDocument
{
    public string? PackageId { get; init; }
    public string? Version { get; init; }
    public string? Source { get; init; }
    public string? Sha256 { get; init; }
    public string? ContentRevision { get; init; }
    public bool BasicOnly { get; init; }
    public List<BasicDefinitionDocument>? Templates { get; init; }
}

internal sealed class BasicDefinitionDocument
{
    public string? Path { get; init; }
    public string? Identity { get; init; }
    public string? Type { get; init; }
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow)]
[JsonSerializable(typeof(BasicCompanionDocument))]
internal sealed partial class BasicCompanionJsonContext : JsonSerializerContext;
