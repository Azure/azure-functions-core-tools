// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Text.Json;
using Azure.Functions.Cli.Workloads.Storage;

namespace Azure.Functions.Cli.Workloads.Discovery;

/// <summary>
/// Default <see cref="IWorkloadMetadataReader"/>. Reads
/// <c>workload.json</c> from the package root and deserializes it through
/// the source-generated <see cref="WorkloadJsonContext"/>.
/// </summary>
internal sealed class WorkloadMetadataReader : IWorkloadMetadataReader
{
    /// <summary>
    /// Conventional file name for the per-workload manifest at the root of
    /// every workload's NuGet package.
    /// </summary>
    public const string MetadataFileName = "workload.json";

    /// <summary>
    /// Conventional sub-directory inside a workload's package that holds
    /// the workload's assemblies, dependencies, and runtime config. The
    /// per-workload manifest (<see cref="MetadataFileName"/>) sits at the
    /// package root and points into this directory.
    /// </summary>
    public const string ContentDirectoryName = "tools";

    /// <inheritdoc />
    public WorkloadMetadata Read(string packageDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packageDirectory);

        if (!Directory.Exists(packageDirectory))
        {
            throw new DirectoryNotFoundException(
                $"Workload package directory '{packageDirectory}' does not exist.");
        }

        string metadataPath = Path.Combine(packageDirectory, MetadataFileName);
        if (!File.Exists(metadataPath))
        {
            throw new InvalidWorkloadException(
                $"No {MetadataFileName} found in '{packageDirectory}'.");
        }

        WorkloadMetadata? metadata;
        try
        {
            using FileStream stream = File.OpenRead(metadataPath);
            metadata = JsonSerializer.Deserialize(
                stream,
                WorkloadJsonContext.Default.WorkloadMetadata);
        }
        catch (JsonException ex)
        {
            throw new InvalidWorkloadException(
                $"Failed to parse '{metadataPath}': {ex.Message}",
                ex);
        }

        if (metadata is null)
        {
            throw new InvalidWorkloadException(
                $"'{metadataPath}' deserialized to null.");
        }

        // System.Text.Json honours `required` on init-only properties, so a
        // missing entryPoint / assemblyPath / type already throws above. The
        // remaining failure mode is a JSON object that parses but contains
        // empty strings; surface that with the same exception type so callers
        // only have to handle one.
        if (string.IsNullOrWhiteSpace(metadata.EntryPoint.AssemblyPath))
        {
            throw new InvalidWorkloadException(
                $"'{metadataPath}' is missing entryPoint.assemblyPath.");
        }

        if (string.IsNullOrWhiteSpace(metadata.EntryPoint.Type))
        {
            throw new InvalidWorkloadException(
                $"'{metadataPath}' is missing entryPoint.type.");
        }

        return metadata;
    }
}
