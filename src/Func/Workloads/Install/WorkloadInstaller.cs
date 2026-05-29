// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Commands.Start.Host;
using Azure.Functions.Cli.Workloads.Catalog;
using Azure.Functions.Cli.Workloads.Discovery;
using Azure.Functions.Cli.Workloads.Storage;
using Microsoft.Extensions.Options;
using NuGet.Packaging;
using NuGet.Packaging.Core;
using NuGet.Versioning;

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
    IWorkloadMetadataReader metadataReader,
    IWorkloadCatalog catalog,
    IOptions<WorkloadCatalogOptions> catalogOptions) : IWorkloadInstaller
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
    private readonly IWorkloadCatalog _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
    private readonly WorkloadCatalogOptions _catalogOptions = catalogOptions?.Value ?? throw new ArgumentNullException(nameof(catalogOptions));

    /// <inheritdoc />
    public async Task<WorkloadInstallResult> InstallFromPackageAsync(
        string nupkgPath,
        bool force = false,
        IProgress<WorkloadInstallProgress>? progress = null,
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
        string nuspecTitle = nuspec.GetTitle();
        string nuspecDescription = nuspec.GetDescription();

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
            progress?.Report(new WorkloadInstallProgress(
                WorkloadInstallPhase.Extracting,
                $"Extracting workload '{packageId}' {version}"));

            await ExtractPackageAsync(reader, installPath, cancellationToken);
            EnsureHostExecutableBit(installPath, packageId);

            WorkloadMetadata metadata = _metadataReader.Read(installPath);

            entry = new WorkloadEntry
            {
                PackageId = packageId,
                PackageVersion = version,
                Aliases = aliases,
                DisplayName = GetDisplayName(metadata, nuspecTitle, packageId),
                Description = GetDescription(metadata, nuspecDescription),
                EntryPoint = metadata.EntryPoint,
                Kind = metadata.Kind,
                Source = Path.GetFullPath(nupkgPath),
                InstallRefCount = 1,
            };

            progress?.Report(new WorkloadInstallProgress(
                WorkloadInstallPhase.Registering,
                $"Registering workload '{packageId}' {version}"));

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
    public async Task<WorkloadInstallResult> InstallFromCatalogAsync(
        string packageId,
        NuGetVersion? version,
        string? source,
        bool includePrerelease,
        bool exact,
        bool force,
        IProgress<WorkloadInstallProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packageId);
        bool effectiveIncludePrerelease = IncludePrerelease(includePrerelease);

        progress?.Report(new WorkloadInstallProgress(
            WorkloadInstallPhase.Resolving,
            $"Resolving workload '{packageId}'"));

        string resolvedId = exact
            ? packageId
            : await ResolveAliasOrIdAsync(packageId, source, effectiveIncludePrerelease, cancellationToken);

        ResolvedPackage resolved = await ResolveCatalogPackageAsync(resolvedId, version, source, effectiveIncludePrerelease, cancellationToken);

        string tempPath = Path.Combine(
            Path.GetTempPath(),
            $"func-workload-{Guid.NewGuid():N}.nupkg");

        try
        {
            progress?.Report(new WorkloadInstallProgress(
                WorkloadInstallPhase.Downloading,
                $"Downloading '{resolved.PackageId}' {resolved.Version.ToNormalizedString()}"));

            await using (Stream packageStream = await _catalog.DownloadAsync(resolved, cancellationToken))
            await using (FileStream tempStream = File.Create(tempPath))
            {
                await packageStream.CopyToAsync(tempStream, cancellationToken);
            }

            return await InstallFromPackageAsync(tempPath, force, progress, cancellationToken);
        }
        finally
        {
            try
            {
                if (File.Exists(tempPath))
                {
                    File.Delete(tempPath);
                }
            }
            catch
            {
                // Best-effort cleanup of the temp .nupkg; the OS reaps the
                // temp dir periodically and the registry / install dir are
                // already consistent at this point.
            }
        }
    }

    /// <inheritdoc />
    public async Task<WorkloadUpdateResult> UpdateAsync(
        string packageId,
        NuGetVersion? targetInstalledVersion,
        string? source,
        bool includePrerelease,
        bool allowMajor,
        IProgress<WorkloadInstallProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packageId);
        bool effectiveIncludePrerelease = IncludePrerelease(includePrerelease);

        progress?.Report(new WorkloadInstallProgress(
            WorkloadInstallPhase.Resolving,
            $"Resolving update for '{packageId}'"));

        IReadOnlyList<WorkloadEntry> installed = await _store.GetWorkloadsAsync(cancellationToken);
        List<WorkloadEntry> matches = [.. installed
            .Where(e => string.Equals(e.PackageId, packageId, StringComparison.OrdinalIgnoreCase))];

        if (matches.Count == 0)
        {
            throw new InvalidOperationException(
                $"Workload '{packageId}' is not installed.");
        }

        WorkloadEntry currentEntry = ResolveUpdateTarget(packageId, targetInstalledVersion, matches);
        var currentVersion = NuGetVersion.Parse(currentEntry.PackageVersion);

        ResolvedPackage? resolved = await _catalog.ResolveLatestVersionAsync(
            currentEntry.PackageId,
            effectiveIncludePrerelease,
            currentVersion,
            allowMajor,
            source,
            cancellationToken);

        if (resolved is null)
        {
            return new WorkloadUpdateResult(
                currentEntry,
                currentEntry.PackageVersion,
                NoUpdateAvailable: true,
                NoCandidateOnSource: true);
        }

        if (resolved.Version <= currentVersion)
        {
            return new WorkloadUpdateResult(currentEntry, currentEntry.PackageVersion, NoUpdateAvailable: true);
        }

        WorkloadEntry newEntry = await StageAndSwapAsync(currentEntry, resolved, progress, cancellationToken);

        return new WorkloadUpdateResult(newEntry, currentEntry.PackageVersion, NoUpdateAvailable: false);
    }

    /// <inheritdoc />
    public async Task<bool> UninstallAsync(string packageId, string version, CancellationToken cancellationToken = default)
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

    private async Task<string> ResolveAliasOrIdAsync(
        string aliasOrId,
        string? source,
        bool includePrerelease,
        CancellationToken cancellationToken)
    {
        // Spec §6.1 step 2 (default flow): query the catalog for packages
        // declaring `alias:<aliasOrId>` and pick the unique match. Zero
        // matches falls back to treating the input as a literal package id.
        // Multiple distinct package ids is an error; the user must re-run
        // with --exact <packageId>.
        const int aliasSearchTake = 50;

        var query = new CatalogSearchQuery
        {
            Filter = aliasOrId,
            IncludePrerelease = includePrerelease,
            Take = aliasSearchTake,
            Source = source,
        };

        IReadOnlyList<CatalogSearchResult> hits = await _catalog.SearchAsync(query, cancellationToken);
        IReadOnlyList<string> matchedIds = FilterByAlias(hits, aliasOrId);

        // Some feeds (BaGet, older NuGet implementations) tokenize the `q=`
        // term in ways that drop hyphenated aliases like `node-worker`. When
        // the targeted query returns nothing, retry with an empty filter:
        // the `packageType=FuncCliWorkload` constraint keeps the result set
        // bounded to workload packages, which we then filter client-side.
        if (matchedIds.Count == 0 && hits.Count == 0)
        {
            IReadOnlyList<CatalogSearchResult> all = await _catalog.SearchAsync(
                query with { Filter = null },
                cancellationToken);
            matchedIds = FilterByAlias(all, aliasOrId);
        }

        if (matchedIds.Count > 1)
        {
            throw new AmbiguousPackageMatchException(aliasOrId, matchedIds);
        }

        return matchedIds.Count == 1 ? matchedIds[0] : aliasOrId;
    }

    private static IReadOnlyList<string> FilterByAlias(IReadOnlyList<CatalogSearchResult> hits, string alias) =>
        [.. hits
            .Where(r => r.Aliases.Any(a => string.Equals(a, alias, StringComparison.OrdinalIgnoreCase)))
            .Select(r => r.PackageId)
            .Distinct(StringComparer.OrdinalIgnoreCase)];

    private async Task<ResolvedPackage> ResolveCatalogPackageAsync(
        string packageId,
        NuGetVersion? version,
        string? source,
        bool includePrerelease,
        CancellationToken cancellationToken)
    {
        ResolvedPackage? resolved = version is null
            ? await _catalog.ResolveLatestVersionAsync(
                packageId, includePrerelease, currentVersion: null, allowMajor: true, source: source, cancellationToken)
            : await _catalog.ResolveVersionAsync(packageId, version, source, cancellationToken);

        if (resolved is null)
        {
            string detail = version is null
                ? "No matching version was found on any configured source."
                : $"Version '{version.ToNormalizedString()}' was not found on any configured source.";

            string hint = includePrerelease
                ? string.Empty
                : " Pass --prerelease if it is a prerelease.";

            throw new Catalog.WorkloadPackageNotFoundException(
                $"Could not resolve workload '{packageId}'. {detail}{hint}");
        }

        return resolved;
    }

    private bool IncludePrerelease(bool includePrerelease) => includePrerelease || _catalogOptions.IncludePrerelease;

    private static WorkloadEntry ResolveUpdateTarget(
        string packageId,
        NuGetVersion? targetInstalledVersion,
        IReadOnlyList<WorkloadEntry> matches)
    {
        if (targetInstalledVersion is null)
        {
            return matches
                .OrderByDescending(e => NuGetVersion.Parse(e.PackageVersion))
                .First();
        }

        string requested = targetInstalledVersion.ToNormalizedString();
        WorkloadEntry? exact = matches.FirstOrDefault(e =>
            string.Equals(e.PackageVersion, requested, StringComparison.Ordinal));

        if (exact is null)
        {
            string available = string.Join(", ", matches.Select(m => m.PackageVersion));
            throw new InvalidOperationException(
                $"Workload '{packageId}' version '{requested}' is not installed. " +
                $"Installed versions: {available}.");
        }

        return exact;
    }

    private async Task<WorkloadEntry> StageAndSwapAsync(
        WorkloadEntry currentEntry,
        ResolvedPackage resolved,
        IProgress<WorkloadInstallProgress>? progress,
        CancellationToken cancellationToken)
    {
        string tempNupkg = Path.Combine(
            Path.GetTempPath(),
            $"func-workload-{Guid.NewGuid():N}.nupkg");

        string finalInstallPath = _paths.GetInstallDirectory(
            resolved.PackageId, resolved.Version.ToNormalizedString());
        string stagingPath = finalInstallPath + ".staging-" + Guid.NewGuid().ToString("N");

        try
        {
            progress?.Report(new WorkloadInstallProgress(
                WorkloadInstallPhase.Downloading,
                $"Downloading '{resolved.PackageId}' {resolved.Version.ToNormalizedString()}"));

            await using (Stream packageStream = await _catalog.DownloadAsync(resolved, cancellationToken))
            await using (FileStream tempStream = File.Create(tempNupkg))
            {
                await packageStream.CopyToAsync(tempStream, cancellationToken);
            }

            using PackageArchiveReader reader = OpenPackage(tempNupkg);
            NuspecReader nuspec = reader.NuspecReader;

            string newPackageId = nuspec.GetId().ToLowerInvariant();
            string newVersion = nuspec.GetVersion().ToNormalizedString();
            IReadOnlyList<string> aliases = ParseAliases(nuspec.GetTags());
            string nuspecTitle = nuspec.GetTitle();
            string nuspecDescription = nuspec.GetDescription();

            if (!nuspec.GetPackageTypes().Any(t => string.Equals(t.Name, FuncCliWorkloadPackageType, StringComparison.OrdinalIgnoreCase)))
            {
                throw new InvalidWorkloadException(
                    $"Package '{newPackageId}' is not a func CLI workload " +
                    $"(missing package type '{FuncCliWorkloadPackageType}' in its .nuspec).");
            }

            // Sanity check: the resolved id+version should match the package
            // we actually downloaded. If not, the source returned a wrong
            // bundle and we abort before touching the existing install.
            if (!string.Equals(newPackageId, resolved.PackageId, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(newVersion, resolved.Version.ToNormalizedString(), StringComparison.Ordinal))
            {
                throw new InvalidWorkloadException(
                    $"Resolved package '{resolved.PackageId}' {resolved.Version.ToNormalizedString()} but the source returned " +
                    $"'{newPackageId}' {newVersion}.");
            }

            Directory.CreateDirectory(Path.GetDirectoryName(stagingPath)!);
            Directory.CreateDirectory(stagingPath);

            progress?.Report(new WorkloadInstallProgress(
                WorkloadInstallPhase.Extracting,
                $"Extracting workload '{newPackageId}' {newVersion}"));

            await ExtractPackageAsync(reader, stagingPath, cancellationToken);
            EnsureHostExecutableBit(stagingPath, newPackageId);
            WorkloadMetadata metadata = _metadataReader.Read(stagingPath);

            WorkloadEntry newEntry = new()
            {
                PackageId = newPackageId,
                PackageVersion = newVersion,
                Aliases = aliases,
                DisplayName = GetDisplayName(metadata, nuspecTitle, newPackageId),
                Description = GetDescription(metadata, nuspecDescription),
                EntryPoint = metadata.EntryPoint,
                Kind = metadata.Kind,
                Source = resolved.Source.Source,
                InstallRefCount = currentEntry.InstallRefCount,
            };

            // Atomic swap (spec §6.4 step 4):
            //   1. Move staged dir into final location.
            //   2. Replace registry row in a single write (old removed,
            //      new added). The orphaned-dir case in §6.4 step 5 is
            //      recoverable via `func workload prune`.
            //   3. Delete the previous install directory.
            Directory.Move(stagingPath, finalInstallPath);

            progress?.Report(new WorkloadInstallProgress(
                WorkloadInstallPhase.Registering,
                $"Registering workload '{newPackageId}' {newVersion}"));

            await _store.ReplaceWorkloadAsync(currentEntry.PackageId, currentEntry.PackageVersion, newEntry, cancellationToken);

            string oldInstallPath = _paths.GetInstallDirectory(currentEntry.PackageId, currentEntry.PackageVersion);
            if (!string.Equals(oldInstallPath, finalInstallPath, StringComparison.Ordinal))
            {
                // Spec §6.4 step 5: a delete failure here leaves an orphan
                // directory, which `func workload prune` cleans up later.
                TryDeleteDirectory(oldInstallPath);
            }

            return newEntry;
        }
        catch
        {
            TryDeleteDirectory(stagingPath);
            throw;
        }
        finally
        {
            try
            {
                if (File.Exists(tempNupkg))
                {
                    File.Delete(tempNupkg);
                }
            }
            catch
            {
                // Best-effort temp cleanup.
            }
        }
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

    /// <summary>
    /// Targeted workaround: NuGet's zip extraction does not preserve Unix
    /// mode bits, so the host apphost lands without the execute bit and
    /// the launcher fails with "Permission denied". We narrowly chmod the
    /// single well-known host binary path, and only for the host workload
    /// package family (<c>Azure.Functions.Cli.Workloads.Host.*</c>), so no
    /// other package can use this code path to mark files executable.
    /// A general per-package executables mechanism can replace this later.
    /// </summary>
    private static void EnsureHostExecutableBit(string installPath, string packageId)
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        if (!packageId.StartsWith(HostWorkloadPackage.PackageIdPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        string hostBinary = Path.Combine(
            installPath,
            "tools",
            "any",
            HostProcessStartInfoFactory.ExecutableBaseName);

        if (!File.Exists(hostBinary))
        {
            return;
        }

        UnixFileMode mode = File.GetUnixFileMode(hostBinary);
        File.SetUnixFileMode(
            hostBinary,
            mode | UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute);
    }

    private static async Task ExtractPackageAsync(PackageArchiveReader reader, string destination, CancellationToken cancellationToken)
    {
        // PackageArchiveReader already filters OPC metadata and rejects
        // path-escape entries. We additionally restrict the on-disk layout
        // to the workload contract: workload.json at the root and the
        // tools/ payload. Other package-level files (the .nuspec, NuGet's
        // package icon, etc.) are pack-time artifacts the host never reads
        // and would just bloat the install directory.
        foreach (string packageFile in await reader.GetFilesAsync(cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!IsInstallablePackageFile(packageFile))
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

    /// <summary>
    /// Whether a file inside the .nupkg is part of the workload's on-disk
    /// contract. Only <c>workload.json</c> at the package root and entries
    /// under <c>tools/</c> are extracted; everything else (.nuspec, package
    /// icon, etc.) is pack-time metadata and stays inside the .nupkg.
    /// </summary>
    private static bool IsInstallablePackageFile(string packageFile)
    {
        if (string.Equals(packageFile, WorkloadMetadataReader.MetadataFileName, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return packageFile.StartsWith("tools/", StringComparison.OrdinalIgnoreCase);
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

    // Display name priority: workload.json (forward-compat), then nuspec
    // <title>, then the package id so the column is never blank.
    private static string GetDisplayName(WorkloadMetadata metadata, string? nuspecTitle, string packageId)
    {
        if (!string.IsNullOrWhiteSpace(metadata.DisplayName))
        {
            return metadata.DisplayName;
        }

        return string.IsNullOrWhiteSpace(nuspecTitle) ? packageId : nuspecTitle!;
    }

    // Description priority: workload.json (forward-compat), then the nuspec
    // <description> NuGet already requires. Empty string when neither is set.
    private static string GetDescription(WorkloadMetadata metadata, string? nuspecDescription)
    {
        if (!string.IsNullOrWhiteSpace(metadata.Description))
        {
            return metadata.Description;
        }

        return string.IsNullOrWhiteSpace(nuspecDescription) ? string.Empty : nuspecDescription!;
    }
}
