// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Text.Json;
using System.Xml;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Templates.Engine;
using Azure.Functions.Cli.Workloads;
using NuGet.Packaging;
using NuGet.Versioning;

namespace Azure.Functions.Cli.Templates.Acquisition;

internal interface IBasicArtifactInspector
{
    public Task ValidateAsync(string archive, BasicTemplateArtifact artifact, CancellationToken cancellationToken);
}

/// <summary>
/// Checks exact archive bytes and the complete raw template inventory against qualified content evidence.
/// </summary>
internal sealed class BasicArtifactInspector(IFileSystem fileSystem) : IBasicArtifactInspector
{
    private readonly IFileSystem _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));

    public async Task ValidateAsync(string archive, BasicTemplateArtifact artifact, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(archive);
        ArgumentNullException.ThrowIfNull(artifact);
        if (!string.Equals(await _fileSystem.ComputeSha256Async(archive, cancellationToken), artifact.Sha256, StringComparison.OrdinalIgnoreCase))
        {
            throw Invalid(artifact, "The archive digest does not match the reviewed exact artifact.");
        }

        try
        {
            using PackageArchiveReader reader = new(archive);
            (string id, NuGetVersion version, string[] types) = ReadMetadata(reader, artifact);
            if (!string.Equals(id, artifact.PackageId, StringComparison.OrdinalIgnoreCase) || version != artifact.Version)
            {
                throw Invalid(artifact, "The source returned another package identity or version.");
            }

            if (!types.Contains("FuncTemplate", StringComparer.OrdinalIgnoreCase)
                || types.Contains(WorkloadPackageTypes.Workload, StringComparer.OrdinalIgnoreCase)
                || types.Contains(WorkloadPackageTypes.RuntimeIdentifierPackage, StringComparer.OrdinalIgnoreCase))
            {
                throw Invalid(artifact, "The archive must be owned exclusively by the template lifecycle, not the workload installer.");
            }

            string[] files = [.. await reader.GetFilesAsync(cancellationToken)];
            string[] configurations = [.. files.Where(path =>
                path.Equals(".template.config/template.json", StringComparison.OrdinalIgnoreCase)
                || path.EndsWith("/.template.config/template.json", StringComparison.OrdinalIgnoreCase))];
            if (configurations.Length != artifact.Templates.Count
                || configurations.Distinct(StringComparer.OrdinalIgnoreCase).Count() != configurations.Length
                || artifact.Templates.Any(template => !configurations.Contains(template.Path, StringComparer.Ordinal)))
            {
                throw Invalid(artifact, "The complete template inventory differs from the qualified inventory.");
            }

            foreach (BasicTemplateDefinition template in artifact.Templates)
            {
                using Stream stream = await reader.GetStreamAsync(template.Path, cancellationToken);
                using StreamReader text = new(stream);
                string configuration = await text.ReadToEndAsync(cancellationToken);
                TemplateConstraintDeclarationReader.Read(configuration, types);
                using var document = JsonDocument.Parse(configuration, new() { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
                JsonElement root = document.RootElement;
                if (root.ValueKind != JsonValueKind.Object
                    || !root.TryGetProperty("identity", out JsonElement identity) || identity.ValueKind != JsonValueKind.String
                    || identity.GetString() != template.Identity
                    || !root.TryGetProperty("tags", out JsonElement tags) || tags.ValueKind != JsonValueKind.Object
                    || !tags.TryGetProperty("type", out JsonElement type) || type.ValueKind != JsonValueKind.String
                    || type.GetString() != template.Type)
                {
                    throw Invalid(artifact, $"Configuration '{template.Path}' does not match its qualified identity/type.");
                }
            }
        }
        catch (Exception exception) when (exception is JsonException or InvalidDataException or InvalidTemplateMetadataException or NuGet.Packaging.Core.PackagingException)
        {
            throw new BasicAcquisitionException(BasicAcquisitionFailure.InvalidArtifact,
                $"Basic artifact '{artifact.PackageId}' {artifact.Version} is invalid: {exception.Message}", exception);
        }
    }

    private static BasicAcquisitionException Invalid(BasicTemplateArtifact artifact, string reason)
        => new(BasicAcquisitionFailure.InvalidArtifact, $"Basic artifact '{artifact.PackageId}' {artifact.Version}: {reason}");

    private static (string Id, NuGetVersion Version, string[] Types) ReadMetadata(PackageArchiveReader reader, BasicTemplateArtifact artifact)
    {
        try
        {
            NuspecReader nuspec = reader.NuspecReader;
            return (nuspec.GetId(), nuspec.GetVersion(), [.. nuspec.GetPackageTypes().Select(type => type.Name)]);
        }
        catch (Exception exception) when (exception is XmlException or ArgumentException)
        {
            throw new BasicAcquisitionException(BasicAcquisitionFailure.InvalidArtifact,
                $"Basic artifact '{artifact.PackageId}' {artifact.Version} has malformed NuGet metadata: {exception.Message}", exception);
        }
    }
}
