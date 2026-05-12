// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Workloads.Discovery;
using Azure.Functions.Cli.Workloads.Storage;
using NuGet.Packaging;
using NuGet.Packaging.Core;

namespace Azure.Functions.Cli.Workloads.Install;

/// <summary>
/// Default <see cref="IWorkloadInstaller"/>. Extracts a <c>.nupkg</c> into
/// the install directory, validates its <c>workload.json</c>, then writes
/// the registry entry. On any failure after the install directory is
/// created, the directory is rolled back so disk and registry stay in sync.
/// </summary>
internal sealed class WorkloadInstaller(
    IWorkloadPaths paths,
    IWorkloadStore store,
    IWorkloadMetadataReader metadataReader) : IWorkloadInstaller
{
    /// <summary>
    /// Prefix on nuspec tags that marks the tag as a CLI-facing alias, so
    /// workloads can keep using other tags for search/categories without
    /// leaking them into alias resolution.
    /// </summary>
    public const string AliasTagPrefix = "alias:";

    /// <summary>
    /// Package-type name a workload package must declare in its .nuspec so
    /// the installer can refuse arbitrary .nupkgs. Mirrors dotnet's
    /// <c>DotnetTool</c> convention.
    /// </summary>
    public const string FuncCliWorkloadPackageType = "FuncCliWorkload";

    private readonly IWorkloadPaths _paths = paths ?? throw new ArgumentNullException(nameof(paths));
    private readonly IWorkloadStore _store = store ?? throw new ArgumentNullException(nameof(store));
    private readonly IWorkloadMetadataReader _metadataReader = metadataReader ?? throw new ArgumentNullException(nameof(metadataReader));

    /// <inheritdoc />
    public async Task<WorkloadInstallResult> InstallFromPackageAsync(
        string nupkgPath,
        bool force = false,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(nupkgPath);

        if (!File.Exists(nupkgPath))
        {
            throw new FileNotFoundException(
                $"Package file '{nupkgPath}' does not exist.",
                nupkgPath);
        }

        using PackageArchiveReader reader = OpenPackage(nupkgPath);
        NuspecReader nuspec = reader.NuspecReader;

        // NuGet package ids are case-insensitive; lowercase so the install
        // path, registry key, and later lookups all agree.
        string packageId = nuspec.GetId().ToLowerInvariant();
        string version = nuspec.GetVersion().ToNormalizedString();
        IReadOnlyList<string> aliases = ParseAliases(nuspec.GetTags());

        if (!nuspec.GetPackageTypes().Any(t => string.Equals(t.Name, FuncCliWorkloadPackageType, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidWorkloadException(
                $"Package '{packageId}' is not a func CLI workload " +
                $"(missing package type '{FuncCliWorkloadPackageType}' in its .nuspec).");
        }

        string installPath = _paths.GetInstallDirectory(packageId, version);

        // Idempotent re-install: same (id, version) already on disk and in
        // the registry returns success without re-extracting. Force still
        // re-extracts so users can repair a broken install.
        if (!force)
        {
            IReadOnlyList<WorkloadEntry> existing = await _store.GetWorkloadsAsync(cancellationToken);
            WorkloadEntry? prior = existing.FirstOrDefault(e =>
                string.Equals(e.PackageId, packageId, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(e.PackageVersion, version, StringComparison.Ordinal));

            if (prior is not null && Directory.Exists(installPath))
            {
                return new WorkloadInstallResult(prior, AlreadyInstalled: true);
            }
        }

        if (Directory.Exists(installPath))
        {
            if (!force)
            {
                throw new InvalidOperationException(
                    $"Workload '{packageId}' version '{version}' is already installed at '{installPath}' " +
                    "but is missing from the registry.");
            }

            // Drop registry and on-disk directory before extracting fresh
            // so files dropped by the new package don't linger.
            await _store.RemoveWorkloadAsync(packageId, version, cancellationToken);
            Directory.Delete(installPath, recursive: true);
        }

        Directory.CreateDirectory(Path.GetDirectoryName(installPath)!);
        Directory.CreateDirectory(installPath);

        WorkloadEntry entry;
        try
        {
            await ExtractPackageAsync(reader, installPath, cancellationToken);

            WorkloadMetadata metadata = _metadataReader.Read(installPath);

            entry = new WorkloadEntry
            {
                PackageId = packageId,
                PackageVersion = version,
                Aliases = aliases,
                EntryPoint = metadata.EntryPoint,
                Kind = metadata.Kind,
                Source = Path.GetFullPath(nupkgPath),
                InstallRefCount = 1,
            };

            await _store.SaveWorkloadAsync(entry, cancellationToken);
        }
        catch
        {
            TryDeleteDirectory(installPath);
            throw;
        }

        return new WorkloadInstallResult(entry, AlreadyInstalled: false);
    }

    /// <inheritdoc />
    public async Task<bool> UninstallAsync(
        string packageId,
        string version,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packageId);
        ArgumentException.ThrowIfNullOrWhiteSpace(version);

        bool removed = await _store.RemoveWorkloadAsync(packageId, version, cancellationToken);
        if (!removed)
        {
            return false;
        }

        string installPath = _paths.GetInstallDirectory(packageId, version);
        TryDeleteDirectory(installPath);
        return true;
    }

    private static PackageArchiveReader OpenPackage(string nupkgPath)
    {
        try
        {
            return new PackageArchiveReader(File.OpenRead(nupkgPath));
        }
        catch (Exception ex) when (ex is InvalidDataException or PackagingException)
        {
            throw new InvalidWorkloadException(
                $"Failed to read .nupkg at '{nupkgPath}': {ex.Message}",
                ex);
        }
    }

    private static async Task ExtractPackageAsync(
        PackageArchiveReader reader,
        string destination,
        CancellationToken cancellationToken)
    {
        // PackageArchiveReader already filters OPC metadata and rejects
        // path-escape entries, so we don't re-filter here.
        foreach (string packageFile in await reader.GetFilesAsync(cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();

            string targetPath = Path.Combine(destination, packageFile.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);

            using Stream entryStream = await reader.GetStreamAsync(packageFile, cancellationToken);
            using FileStream output = File.Create(targetPath);
            await entryStream.CopyToAsync(output, cancellationToken);
        }
    }

    /// <summary>
    /// Extracts CLI aliases from nuspec tags. Only tags prefixed with
    /// <see cref="AliasTagPrefix"/> are surfaced.
    /// </summary>
    private static IReadOnlyList<string> ParseAliases(string? tags)
    {
        if (string.IsNullOrWhiteSpace(tags))
        {
            return [];
        }

        List<string> aliases = [];
        foreach (string tag in tags.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (tag.StartsWith(AliasTagPrefix, StringComparison.OrdinalIgnoreCase))
            {
                string alias = tag[AliasTagPrefix.Length..].Trim();
                if (alias.Length > 0)
                {
                    aliases.Add(alias);
                }
            }
        }

        return aliases;
    }

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
            // Best-effort: if the directory can't be removed (e.g. AV holds
            // an assembly open), the registry is already consistent and the
            // user can clean up manually.
        }
    }
}
