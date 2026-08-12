// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Workloads.Discovery;
using NuGet.Packaging;
using NuGet.Packaging.Core;

namespace Azure.Functions.Cli.Workloads.Install;

internal sealed class WorkloadPackageInspector(IWorkloadMetadataReader metadataReader, WorkloadRidPackageSelector ridPackageSelector)
    : IWorkloadPackageInspector
{
    private readonly IWorkloadMetadataReader _metadataReader = metadataReader ?? throw new ArgumentNullException(nameof(metadataReader));
    private readonly WorkloadRidPackageSelector _ridPackageSelector = ridPackageSelector ?? throw new ArgumentNullException(nameof(ridPackageSelector));

    public async Task<InspectedWorkloadPackage> InspectAsync(string path, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        using PackageArchiveReader reader = OpenPackage(path);
        NuspecReader nuspec = reader.NuspecReader;
        WorkloadPackageIdentity identity = new(
            nuspec.GetId().ToLowerInvariant(),
            nuspec.GetVersion().ToNormalizedString(),
            [.. nuspec.GetPackageTypes().Select(t => t.Name)],
            WorkloadPackageTags.ParseValues(nuspec.GetTags(), WorkloadPackageTags.AliasPrefix),
            WorkloadPackageTags.ParseValues(nuspec.GetTags(), WorkloadPackageTags.RuntimeIdentifierPrefix),
            nuspec.GetTitle(),
            nuspec.GetDescription());

        string inspectionDirectory = Path.Combine(Path.GetTempPath(), $"func-workload-inspect-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(inspectionDirectory);
            await ExtractMetadataAsync(reader, inspectionDirectory, cancellationToken);
            WorkloadMetadata metadata = _metadataReader.Read(inspectionDirectory);
            IReadOnlyList<string> files = [.. await reader.GetFilesAsync(cancellationToken)];
            WorkloadPackageRole role = ValidatePackage(identity, metadata, files);
            return new InspectedWorkloadPackage(path, identity, metadata, role);
        }
        finally
        {
            TryDeleteDirectory(inspectionDirectory);
        }
    }

    public bool MatchesIdentity(string path, string packageId, string version)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentException.ThrowIfNullOrWhiteSpace(packageId);
        ArgumentException.ThrowIfNullOrWhiteSpace(version);

        try
        {
            using PackageArchiveReader reader = OpenPackage(path);
            NuspecReader nuspec = reader.NuspecReader;
            return string.Equals(nuspec.GetId(), packageId, StringComparison.OrdinalIgnoreCase)
                && string.Equals(nuspec.GetVersion().ToNormalizedString(), version, StringComparison.Ordinal);
        }
        catch (Exception ex) when (ex is InvalidWorkloadException or IOException or UnauthorizedAccessException)
        {
            // An unreadable sibling cannot be the implementation being located.
            return false;
        }
    }

    public void ValidateIdentity(WorkloadPackageIdentity identity, string expectedPackageId, string expectedVersion)
    {
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedPackageId);
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedVersion);

        if (!string.Equals(identity.PackageId, expectedPackageId, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(identity.Version, expectedVersion, StringComparison.Ordinal))
        {
            throw new InvalidWorkloadException(
                $"Resolved package '{expectedPackageId}' {expectedVersion} but the source returned " +
                $"'{identity.PackageId}' {identity.Version}.");
        }
    }

    private WorkloadPackageRole ValidatePackage(WorkloadPackageIdentity identity, WorkloadMetadata metadata, IReadOnlyList<string> files)
    {
        bool standardType = identity.PackageTypes.Any(t =>
            string.Equals(t, WorkloadPackageTypes.Workload, StringComparison.OrdinalIgnoreCase));

        bool runtimeIdentifierType = identity.PackageTypes.Any(t =>
            string.Equals(t, WorkloadPackageTypes.RuntimeIdentifierPackage, StringComparison.OrdinalIgnoreCase));

        if (standardType && runtimeIdentifierType)
        {
            throw new InvalidWorkloadException(
                $"Package '{identity.PackageId}' declares both '{WorkloadPackageTypes.Workload}' and " +
                $"'{WorkloadPackageTypes.RuntimeIdentifierPackage}' package types.");
        }

        if (metadata.Kind == WorkloadKind.RidPointer)
        {
            RequirePackageType(identity, standardType, WorkloadPackageTypes.Workload);
            if (identity.RuntimeIdentifiers.Count > 0)
            {
                throw new InvalidWorkloadException($"RID pointer package '{identity.PackageId}' cannot declare a rid: tag.");
            }

            if (files.Any(IsPayloadFile))
            {
                throw new InvalidWorkloadException($"RID pointer package '{identity.PackageId}' cannot contain a tools/ payload.");
            }

            ValidatePointerMap(identity, metadata);
            return WorkloadPackageRole.Pointer;
        }

        if (runtimeIdentifierType)
        {
            if (metadata.Kind is not (WorkloadKind.Workload or WorkloadKind.Content))
            {
                throw new InvalidWorkloadException(
                    $"RID implementation package '{identity.PackageId}' must declare kind 'workload' or 'content'.");
            }

            if (string.IsNullOrWhiteSpace(metadata.RuntimeIdentifier))
            {
                throw new InvalidWorkloadException(
                    $"RID implementation package '{identity.PackageId}' is missing runtimeIdentifier.");
            }

            _ridPackageSelector.ValidateImplementation(identity, metadata.RuntimeIdentifier);
            return WorkloadPackageRole.RuntimeIdentifierImplementation;
        }

        RequirePackageType(identity, standardType, WorkloadPackageTypes.Workload);
        if (metadata.RuntimeIdentifier is not null || identity.RuntimeIdentifiers.Count > 0)
        {
            throw new InvalidWorkloadException(
                $"Package '{identity.PackageId}' uses RID implementation metadata but does not declare package type " +
                $"'{WorkloadPackageTypes.RuntimeIdentifierPackage}'.");
        }

        return WorkloadPackageRole.Ordinary;
    }

    private static void ValidatePointerMap(WorkloadPackageIdentity identity, WorkloadMetadata metadata)
    {
        foreach ((string runtimeIdentifier, string implementationId) in metadata.Packages!)
        {
            string expected = $"{identity.PackageId}.{runtimeIdentifier}";
            if (!string.Equals(implementationId, expected, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidWorkloadException(
                    $"RID pointer package '{identity.PackageId}' maps runtime identifier '{runtimeIdentifier}' to '{implementationId}', " +
                    $"but the required implementation id is '{expected}'.");
            }
        }
    }

    private static void RequirePackageType(WorkloadPackageIdentity identity, bool hasType, string expectedType)
    {
        if (!hasType)
        {
            throw new InvalidWorkloadException(
                $"Package '{identity.PackageId}' is missing required package type '{expectedType}' in its .nuspec.");
        }
    }

    private static PackageArchiveReader OpenPackage(string path)
    {
        FileStream stream = File.OpenRead(path);
        try
        {
            return new PackageArchiveReader(stream);
        }
        catch (Exception ex) when (ex is InvalidDataException or PackagingException)
        {
            stream.Dispose();
            throw new InvalidWorkloadException($"Failed to read .nupkg at '{path}': {ex.Message}", ex);
        }
        catch
        {
            stream.Dispose();
            throw;
        }
    }

    private static Task ExtractMetadataAsync(PackageArchiveReader reader, string destination, CancellationToken cancellationToken)
        => ExtractPackageFilesAsync(reader, destination, IsMetadataFile, cancellationToken);

    private static async Task ExtractPackageFilesAsync(PackageArchiveReader reader, string destination, Func<string, bool> include,
        CancellationToken cancellationToken)
    {
        foreach (string packageFile in await reader.GetFilesAsync(cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!include(packageFile))
            {
                continue;
            }

            string targetPath = Path.Combine(destination, packageFile.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
            using Stream entryStream = await reader.GetStreamAsync(packageFile, cancellationToken);
            using FileStream output = File.Create(targetPath);
            await entryStream.CopyToAsync(output, cancellationToken);
        }
    }

    private static bool IsMetadataFile(string packageFile)
        => string.Equals(packageFile, WorkloadMetadataReader.MetadataFileName, StringComparison.OrdinalIgnoreCase);

    private static bool IsPayloadFile(string packageFile)
        => packageFile.StartsWith("tools/", StringComparison.OrdinalIgnoreCase);

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch
        {
            // Inspection cleanup is best-effort; the operating system reaps temporary directories.
        }
    }
}
