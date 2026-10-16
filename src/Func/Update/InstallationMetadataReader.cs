// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Text.Json;
using Azure.Functions.Cli.Common;
using Microsoft.Extensions.Options;

namespace Azure.Functions.Cli.Update;

/// <inheritdoc cref="IInstallationMetadataReader" />
internal sealed class InstallationMetadataReader(
    IFileSystem fileSystem,
    IOptions<CliEnvironmentOptions> environmentOptions) : IInstallationMetadataReader
{
    internal const string MetadataFileName = "func.installation.json";
    internal const int MaximumMetadataByteCount = 4 * 1024;
    private const int SupportedSchemaVersion = 1;

    private readonly IFileSystem _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
    private readonly CliEnvironmentOptions _environment =
        (environmentOptions ?? throw new ArgumentNullException(nameof(environmentOptions))).Value;

    /// <inheritdoc />
    public async Task<InstallationMetadata> ReadAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string metadataPath = GetMetadataPath();
        Stream? source = _fileSystem.OpenReadIfExists(metadataPath);
        if (source is null)
        {
            throw new FileNotFoundException(
                $"Could not determine the installation method because metadata '{metadataPath}' was not found. " +
                $"See {Constants.DocsUrl} for installation options.",
                metadataPath);
        }

        await using Stream json = source;
        if (json.Length > MaximumMetadataByteCount)
        {
            throw new InstallationMetadataTooLargeException(
                $"Installation metadata '{metadataPath}' exceeds the {MaximumMetadataByteCount}-byte limit.",
                new InvalidDataException(
                    $"Installation metadata is {json.Length} bytes."));
        }

        InstallationMetadataDocument document;
        try
        {
            using JsonDocument jsonDocument = await JsonDocument.ParseAsync(
                json,
                cancellationToken: cancellationToken);
            int? schemaVersion = ReadSchemaVersion(jsonDocument.RootElement);
            if (schemaVersion is not null && schemaVersion != SupportedSchemaVersion)
            {
                throw new UnsupportedInstallationMetadataSchemaException(
                    $"Installation metadata '{metadataPath}' declares unsupported schema version {schemaVersion}. " +
                    $"Only schema version {SupportedSchemaVersion} is supported.");
            }

            document = JsonSerializer.Deserialize(
                jsonDocument.RootElement,
                InstallationMetadataJsonContext.Default.InstallationMetadataDocument)
                ?? throw new InvalidInstallationMetadataException(
                    $"Installation metadata '{metadataPath}' contains the JSON null value.");
        }
        catch (JsonException ex)
        {
            throw new InvalidInstallationMetadataException(
                $"Installation metadata '{metadataPath}' is not valid schema v{SupportedSchemaVersion} JSON.",
                ex);
        }

        ValidateInstallMethod(document.InstallMethod, metadataPath);
        InstallationUpdatePolicy updatePolicy = ParseUpdatePolicy(document.UpdatePolicy, metadataPath);

        return new InstallationMetadata(document.InstallMethod, updatePolicy);
    }

    private static int? ReadSchemaVersion(JsonElement root)
    {
        if (root.ValueKind is not JsonValueKind.Object)
        {
            return null;
        }

        HashSet<string> propertyNames = new(StringComparer.Ordinal);
        int? schemaVersion = null;
        foreach (JsonProperty property in root.EnumerateObject())
        {
            if (!propertyNames.Add(property.Name))
            {
                throw new JsonException($"Duplicate JSON property '{property.Name}' is not allowed.");
            }

            if (property.NameEquals("schemaVersion") &&
                property.Value.ValueKind is JsonValueKind.Number &&
                property.Value.TryGetInt32(out int value))
            {
                schemaVersion = value;
            }
        }

        return schemaVersion;
    }

    private string GetMetadataPath()
    {
        string baseDirectory = _environment.BaseDirectory;
        if (string.IsNullOrWhiteSpace(baseDirectory) || !Path.IsPathFullyQualified(baseDirectory))
        {
            throw new InvalidOperationException("The running CLI application base directory is unavailable or is not fully qualified.");
        }

        return Path.Combine(baseDirectory, MetadataFileName);
    }

    private static void ValidateInstallMethod(string value, string metadataPath)
    {
        if (string.IsNullOrWhiteSpace(value) || !string.Equals(value, value.Trim(), StringComparison.Ordinal))
        {
            throw new InvalidInstallationMetadataException(
                $"Installation metadata '{metadataPath}' contains an invalid installMethod.");
        }
    }

    private static InstallationUpdatePolicy ParseUpdatePolicy(string value, string metadataPath) => value switch
    {
        "in-place" => InstallationUpdatePolicy.InPlace,
        "external" => InstallationUpdatePolicy.External,
        _ => throw new InvalidInstallationMetadataException(
            $"Installation metadata '{metadataPath}' contains unknown updatePolicy '{value}'."),
    };

}
