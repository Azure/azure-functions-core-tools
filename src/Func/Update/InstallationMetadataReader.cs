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
        byte[]? json;

        try
        {
            json = await _fileSystem.ReadAllBytesIfExistsAsync(metadataPath, MaximumMetadataByteCount, cancellationToken);
        }
        catch (InvalidDataException ex)
        {
            throw new InstallationMetadataTooLargeException(
                $"Installation metadata '{metadataPath}' exceeds the {MaximumMetadataByteCount}-byte limit.",
                ex);
        }

        if (json is null)
        {
            throw new FileNotFoundException(
                $"Could not determine the installation method because metadata '{metadataPath}' was not found. " +
                $"See {Constants.DocsUrl} for installation options.",
                metadataPath);
        }

        InstallationMetadataDocument document;
        try
        {
            document = JsonSerializer.Deserialize(
                json,
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

        if (document.SchemaVersion != SupportedSchemaVersion)
        {
            throw new UnsupportedInstallationMetadataSchemaException(
                $"Installation metadata '{metadataPath}' declares unsupported schema version {document.SchemaVersion}. " +
                $"Only schema version {SupportedSchemaVersion} is supported.");
        }

        ValidateInstallMethod(document.InstallMethod, metadataPath);
        InstallationUpdatePolicy updatePolicy = ParseUpdatePolicy(document.UpdatePolicy, metadataPath);

        return new InstallationMetadata(document.InstallMethod, updatePolicy);
    }

    private string GetMetadataPath()
    {
        string processPath = _environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(processPath) || !Path.IsPathFullyQualified(processPath))
        {
            throw new InvalidOperationException("The running CLI executable path is unavailable or is not fully qualified.");
        }

        string physicalProcessPath = _fileSystem.ResolvePhysicalPath(processPath);
        string? installationDirectory = Path.GetDirectoryName(physicalProcessPath);
        if (string.IsNullOrEmpty(installationDirectory))
        {
            throw new InvalidOperationException("The running CLI installation directory could not be determined.");
        }

        return Path.Combine(installationDirectory, MetadataFileName);
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
