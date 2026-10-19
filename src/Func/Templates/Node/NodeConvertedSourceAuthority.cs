// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Azure.Functions.Cli.Templates.V2;

using static Azure.Functions.Cli.Templates.Node.NodeItemCompatibility;

namespace Azure.Functions.Cli.Templates.Node;

/// <summary>
/// Matches reviewed native conversion output without reconstructing its pack-time transformations.
/// </summary>
internal class NodeConvertedSourceAuthority
{
    private readonly JsonElement[] _cohorts;

    public NodeConvertedSourceAuthority()
    {
        using Stream stream = typeof(NodeConvertedSourceAuthority).Assembly.GetManifestResourceStream(
            "Azure.Functions.Cli.Templates.Node.qualified-sources.json")
            ?? throw new InvalidOperationException("The approved Node source associations are missing.");
        using var document = JsonDocument.Parse(stream);
        _cohorts = ReadCohorts(document.RootElement);
    }

    internal NodeConvertedSourceAuthority(ReadOnlyMemory<byte> data)
    {
        using var document = JsonDocument.Parse(data);
        _cohorts = ReadCohorts(document.RootElement);
    }

    public virtual bool Matches(NodeItemSource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        const string templatesPath = "provenance/node/v2/templates/templates.json";
        if (!MatchesPin(source.ConfigurationPath, source.Configuration) || !MatchesPin(source.ContentPath, source.Content)
            || !MatchesPin(templatesPath, source.OriginalTemplates)
            || !source.Sources.Contains(source.Contract.Source)
            || source.ContentSource.TemplateIdentity != source.Contract.Source.TemplateIdentity
            || source.ContentSource.Identity != source.ContentPath || !source.Sources.Contains(source.ContentSource))
        {
            return false;
        }
        using var configuration = JsonDocument.Parse(source.Configuration);
        using var host = JsonDocument.Parse(source.HostConfiguration);
        ValidateUniqueProperties(configuration.RootElement);
        ValidateUniqueProperties(host.RootElement);
        string contentSha256 = Convert.ToHexString(SHA256.HashData(
            Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(source.Content.Span).ReplaceLineEndings("\n"))));
        JsonElement raw = JsonSerializer.SerializeToElement(source.Template);
        NewTemplate? capturedRaw = JsonSerializer.Deserialize<NewTemplate[]>(source.OriginalTemplates.Span)?
            .SingleOrDefault(template => template.Id == source.Template.Id);
        if (capturedRaw is null || !JsonElement.DeepEquals(raw, JsonSerializer.SerializeToElement(capturedRaw)))
        {
            return false;
        }
        foreach (JsonElement cohort in _cohorts)
        {
            if (Text(cohort, "packageId") != source.Contract.Package?.Identifier
                || Array(cohort, "sources").Any(expected => source.Sources.Count(actual =>
                    actual.TemplateIdentity == source.Contract.Source.TemplateIdentity && actual.Identity == Text(expected, "path")
                    && actual.Sha256.Equals(Text(expected, "sha256"), StringComparison.OrdinalIgnoreCase)) != 1))
            {
                continue;
            }
            foreach (JsonElement entry in Array(cohort, "templates").Where(entry =>
                Text(entry, "identity") == source.Contract.Source.TemplateIdentity && Text(entry, "legacyId") == source.Template.Id))
            {
                JsonElement expectedConfiguration = Property(entry, "configuration");
                JsonElement expectedHost = Property(entry, "host");
                JsonElement expectedContent = Property(entry, "content");
                if (Text(expectedConfiguration, "path") == source.ConfigurationPath
                    && Text(expectedContent, "path") == source.ContentPath
                    && contentSha256.Equals(Text(expectedContent, "sha256"), StringComparison.OrdinalIgnoreCase)
                    && MatchesPin(Text(expectedHost, "path"), source.HostConfiguration)
                    && JsonElement.DeepEquals(configuration.RootElement, Property(expectedConfiguration, "definition"))
                    && JsonElement.DeepEquals(host.RootElement, Property(expectedHost, "definition")))
                {
                    return true;
                }
            }
        }
        return false;

        bool MatchesPin(string path, ReadOnlyMemory<byte> bytes)
            => source.Sources.Count(pin => pin.TemplateIdentity == source.Contract.Source.TemplateIdentity && pin.Identity == path
                && pin.Sha256.Equals(Convert.ToHexString(SHA256.HashData(bytes.Span)), StringComparison.OrdinalIgnoreCase)) == 1;
    }

    private static JsonElement[] ReadCohorts(JsonElement root)
    {
        ValidateUniqueProperties(root);
        if (!Property(root, "schemaVersion").TryGetInt32(out int version) || version != 1)
        {
            throw new InvalidDataException("The approved Node source association schema is unsupported.");
        }
        JsonElement[] cohorts = [.. Array(root, "cohorts").Select(cohort => cohort.Clone())];
        if (cohorts.Length == 0 || cohorts.Any(cohort =>
            Array(cohort, "sources").Count() != 4 || !Array(cohort, "templates").Any()))
        {
            throw new InvalidDataException("Approved Node source associations require the complete original source inventory and native output.");
        }
        return cohorts;
    }
}
