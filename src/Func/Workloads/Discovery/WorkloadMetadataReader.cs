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
    /// Sub-directory inside a workload's package that holds its assemblies,
    /// dependencies, and runtime config. Mirrors NuGet's <c>tools/</c>
    /// convention; <c>any</c> denotes a TFM-agnostic payload.
    /// </summary>
    public const string ContentDirectoryName = "tools/any";

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

        // Reject unknown $schema values strictly rather than partially
        // interpret a future schema. Wording mirrors WorkloadStore's registry
        // schema rejection (in WorkloadStore.ReadRegistryAsync) so users see
        // consistent guidance for both manifests.
        if (!WorkloadManifestSchema.IsPackageManifestSupported(metadata.Schema))
        {
            string supported = string.Join(
                Environment.NewLine,
                WorkloadManifestSchema.SupportedPackageManifestSchemas.Select(s => $"  - {s}"));

            throw new InvalidWorkloadException(
                $"The schema '{metadata.Schema}' declared by manifest '{metadataPath}' is not supported."
                + Environment.NewLine
                + "Supported schemas are:"
                + Environment.NewLine
                + supported
                + Environment.NewLine
                + Environment.NewLine
                + "Check for spelling or try updating the CLI to the latest version.");
        }

        switch (metadata.Kind)
        {
            case WorkloadKind.Workload:
                ValidateWorkloadEntryPoint(metadata, metadataPath);
                ValidateNoPackages(metadata, metadataPath);
                ValidateRuntimeIdentifier(metadata, metadataPath);
                return metadata;

            case WorkloadKind.Content:
                ValidateNoEntryPoint(metadata, metadataPath);
                ValidateNoPackages(metadata, metadataPath);
                ValidateRuntimeIdentifier(metadata, metadataPath);
                return metadata;

            case WorkloadKind.Meta:
                ValidateNoEntryPoint(metadata, metadataPath);
                ValidateNoPackages(metadata, metadataPath);
                if (metadata.RuntimeIdentifier is not null)
                {
                    throw new InvalidWorkloadException(
                        $"'{metadataPath}' declares kind 'meta' but also defines runtimeIdentifier.");
                }

                return metadata;

            case WorkloadKind.RidPointer:
                ValidateNoEntryPoint(metadata, metadataPath);
                if (metadata.RuntimeIdentifier is not null)
                {
                    throw new InvalidWorkloadException(
                        $"'{metadataPath}' declares kind 'rid-pointer' but also defines runtimeIdentifier.");
                }

                ValidatePointerPackages(metadata, metadataPath);
                return metadata;

            default:
                throw new InvalidWorkloadException(
                    $"'{metadataPath}' has unrecognized kind '{metadata.Kind}'.");
        }
    }

    private static void ValidateNoEntryPoint(WorkloadMetadata metadata, string metadataPath)
    {
        if (metadata.EntryPoint is not null)
        {
            throw new InvalidWorkloadException(
                $"'{metadataPath}' declares kind '{metadata.Kind.ToWireValue()}' " +
                "but also defines an entryPoint. Remove the entryPoint or change the kind to 'workload'.");
        }
    }

    private static void ValidateNoPackages(WorkloadMetadata metadata, string metadataPath)
    {
        if (metadata.Packages is not null)
        {
            throw new InvalidWorkloadException(
                $"'{metadataPath}' declares kind '{metadata.Kind.ToWireValue()}' but also defines packages.");
        }
    }

    private static void ValidateRuntimeIdentifier(WorkloadMetadata metadata, string metadataPath)
    {
        if (metadata.RuntimeIdentifier is not null && !IsNormalizedRuntimeIdentifier(metadata.RuntimeIdentifier))
        {
            throw new InvalidWorkloadException(
                $"'{metadataPath}' has invalid runtimeIdentifier '{metadata.RuntimeIdentifier}'. Runtime identifiers must be lowercase.");
        }
    }

    private static void ValidatePointerPackages(WorkloadMetadata metadata, string metadataPath)
    {
        if (metadata.Packages is null || metadata.Packages.Count == 0)
        {
            throw new InvalidWorkloadException(
                $"'{metadataPath}' declares kind 'rid-pointer' but does not define a non-empty packages map.");
        }

        HashSet<string> runtimeIdentifiers = new(StringComparer.OrdinalIgnoreCase);
        foreach ((string runtimeIdentifier, string packageId) in metadata.Packages)
        {
            if (!IsNormalizedRuntimeIdentifier(runtimeIdentifier))
            {
                throw new InvalidWorkloadException(
                    $"'{metadataPath}' has invalid packages key '{runtimeIdentifier}'. Runtime identifiers must be lowercase.");
            }

            if (!runtimeIdentifiers.Add(runtimeIdentifier))
            {
                throw new InvalidWorkloadException(
                    $"'{metadataPath}' defines runtime identifier '{runtimeIdentifier}' more than once.");
            }

            if (string.IsNullOrWhiteSpace(packageId))
            {
                throw new InvalidWorkloadException(
                    $"'{metadataPath}' has an empty implementation package id for runtime identifier '{runtimeIdentifier}'.");
            }
        }
    }

    private static bool IsNormalizedRuntimeIdentifier(string value)
        => !string.IsNullOrWhiteSpace(value)
            && string.Equals(value, value.Trim(), StringComparison.Ordinal)
            && string.Equals(value, value.ToLowerInvariant(), StringComparison.Ordinal);

    private static void ValidateWorkloadEntryPoint(WorkloadMetadata metadata, string metadataPath)
    {
        if (metadata.EntryPoint is null)
        {
            // The kind=workload branch is the only caller, so the kind context
            // is implicit. Keep the message scoped to the missing field.
            throw new InvalidWorkloadException(
                $"'{metadataPath}' is missing entryPoint.");
        }

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

        // Reject anything that would let a package escape its content root:
        // absolute paths or `..` segments. Defense-in-depth — the loader
        // also checks.
        ValidateRelativePathStaysWithinPackage(
            metadata.EntryPoint.AssemblyPath,
            metadataPath,
            "entryPoint.assemblyPath");
    }

    private static void ValidateRelativePathStaysWithinPackage(string value, string metadataPath, string fieldName)
    {
        if (Path.IsPathRooted(value))
        {
            throw new InvalidWorkloadException(
                $"'{metadataPath}' has invalid {fieldName} '{value}': absolute paths are not allowed.");
        }

        // workload.json is authored cross-platform, so a Windows host may
        // see forward slashes. Split on both separators.
        string[] segments = value.Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries);
        foreach (string segment in segments)
        {
            if (segment == "..")
            {
                throw new InvalidWorkloadException(
                    $"'{metadataPath}' has invalid {fieldName} '{value}': parent-directory ('..') segments are not allowed.");
            }
        }
    }
}
