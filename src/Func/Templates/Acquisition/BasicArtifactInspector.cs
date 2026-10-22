// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Security.Cryptography;
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

internal interface IBasicArchiveInspector
{
    public Task ValidateArchiveAsync(Stream archive, BasicTemplateArtifact artifact, CancellationToken cancellationToken);
}

internal interface IBasicOwnedArchiveInspector
{
    public Task<BasicTemplateArchiveInventory> InspectOwnedArchiveAsync(string archive, NativeTemplateOwnerReceipt receipt,
        CancellationToken cancellationToken);
}

/// <summary>
/// Checks exact archive bytes and the complete raw template inventory against qualified content evidence.
/// </summary>
internal sealed class BasicArtifactInspector(IFileSystem fileSystem) : IBasicArtifactInspector, IBasicArchiveInspector, IBasicOwnedArchiveInspector
{
    private readonly IFileSystem _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));

    public async Task ValidateAsync(string archive, BasicTemplateArtifact artifact, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(archive);
        ArgumentNullException.ThrowIfNull(artifact);
        using Stream stream = _fileSystem.OpenReadIfExists(archive) ?? throw new FileNotFoundException("The qualified template archive is missing.", archive);
        await ValidateArchiveAsync(stream, artifact, cancellationToken);
    }

    public async Task ValidateArchiveAsync(Stream archive, BasicTemplateArtifact artifact, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(archive);
        ArgumentNullException.ThrowIfNull(artifact);
        cancellationToken.ThrowIfCancellationRequested();
        if (!string.Equals(Convert.ToHexString(await SHA256.HashDataAsync(archive, cancellationToken)), artifact.Sha256, StringComparison.OrdinalIgnoreCase))
        {
            throw Invalid(artifact, "The archive digest does not match the reviewed exact artifact.");
        }

        archive.Position = 0;
        try
        {
            using PackageArchiveReader reader = new(archive, leaveStreamOpen: true);
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

    public async Task<BasicTemplateArchiveInventory> InspectOwnedArchiveAsync(string archive, NativeTemplateOwnerReceipt receipt,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(archive);
        ArgumentNullException.ThrowIfNull(receipt);
        if (!NuGetVersion.TryParse(receipt.Version, out NuGetVersion? version))
        {
            throw Invalid(receipt.PackageId, receipt.Version, "The retained owner receipt has an invalid package version.");
        }

        using Stream stream = _fileSystem.OpenReadIfExists(archive)
            ?? throw new FileNotFoundException("The owned template archive is missing.", archive);
        BasicTemplateArtifact expected = new(receipt.PackageId, version, receipt.Source, receipt.Sha256, receipt.ContentRevision, []);
        string[] files;
        string[] packageTypes;
        List<BasicTemplateDefinition> definitions = [];
        try
        {
            using PackageArchiveReader reader = new(stream, leaveStreamOpen: true);
            (string id, NuGetVersion actualVersion, string[] actualTypes) = ReadMetadata(reader, expected);
            packageTypes = actualTypes;
            if (!string.Equals(id, receipt.PackageId, StringComparison.OrdinalIgnoreCase) || actualVersion != version)
            {
                throw Invalid(receipt.PackageId, receipt.Version, "The owned archive metadata differs from its retained receipt.");
            }

            files = [.. (await reader.GetFilesAsync(cancellationToken)).Order(StringComparer.Ordinal)];
            if (files.Distinct(StringComparer.Ordinal).Count() != files.Length)
            {
                throw Invalid(receipt.PackageId, receipt.Version, "The owned archive has duplicate file paths.");
            }

            string[] configurations = [.. files.Where(path =>
                path.Equals(".template.config/template.json", StringComparison.OrdinalIgnoreCase)
                || path.EndsWith("/.template.config/template.json", StringComparison.OrdinalIgnoreCase))];
            if (configurations.Length == 0
                || configurations.Distinct(StringComparer.OrdinalIgnoreCase).Count() != configurations.Length)
            {
                throw Invalid(receipt.PackageId, receipt.Version, "The owned archive has no unique complete template inventory.");
            }

            foreach (string path in configurations)
            {
                cancellationToken.ThrowIfCancellationRequested();
                using Stream configurationStream = await reader.GetStreamAsync(path, cancellationToken);
                using StreamReader text = new(configurationStream);
                string configuration = await text.ReadToEndAsync(cancellationToken);
                TemplateConstraintDeclarationReader.Read(configuration, packageTypes);
                using var document = JsonDocument.Parse(configuration,
                    new() { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
                JsonElement root = document.RootElement;
                if (root.ValueKind != JsonValueKind.Object
                    || !root.TryGetProperty("identity", out JsonElement identity) || identity.ValueKind != JsonValueKind.String
                    || !root.TryGetProperty("tags", out JsonElement tags) || tags.ValueKind != JsonValueKind.Object
                    || !tags.TryGetProperty("type", out JsonElement type) || type.ValueKind != JsonValueKind.String
                    || type.GetString() is not ("item" or "project"))
                {
                    throw Invalid(receipt.PackageId, receipt.Version, $"Configuration '{path}' has invalid template identity or type metadata.");
                }

                definitions.Add(new(path, identity.GetString()!, type.GetString()!));
            }
        }
        catch (Exception exception) when (exception is JsonException or InvalidDataException or InvalidTemplateMetadataException
            or NuGet.Packaging.Core.PackagingException)
        {
            throw new BasicAcquisitionException(BasicAcquisitionFailure.InvalidArtifact,
                $"Owned archive '{receipt.PackageId}' {receipt.Version} has invalid template inventory: {exception.Message}", exception);
        }

        if (definitions.Select(template => template.Identity).Distinct(StringComparer.Ordinal).Count() != definitions.Count)
        {
            throw Invalid(receipt.PackageId, receipt.Version, "The owned archive has ambiguous template identities.");
        }

        BasicTemplateArtifact observed = expected with { Templates = definitions.AsReadOnly() };
        stream.Position = 0;
        await ValidateArchiveAsync(stream, observed, cancellationToken);
        return new(receipt.PackageId, version.ToNormalizedString(), packageTypes, files,
            Array.AsReadOnly<BasicTemplateDefinition>([.. definitions.OrderBy(template => template.Path, StringComparer.Ordinal)]));
    }

    private static BasicAcquisitionException Invalid(BasicTemplateArtifact artifact, string reason)
        => new(BasicAcquisitionFailure.InvalidArtifact, $"Basic artifact '{artifact.PackageId}' {artifact.Version}: {reason}");

    private static BasicAcquisitionException Invalid(string packageId, string version, string reason)
        => new(BasicAcquisitionFailure.InvalidArtifact, $"Owned archive '{packageId}' {version}: {reason}");

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
