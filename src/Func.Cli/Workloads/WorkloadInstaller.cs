// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Text.Json;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Console;

namespace Azure.Functions.Cli.Workloads;

/// <summary>
/// Manages install / uninstall / list / search of OOP workloads.
///
/// Install sources, in priority order:
/// <list type="number">
///   <item>Local directory containing a <c>workload.json</c> + executable
///         (specified via <c>--from</c>).</item>
///   <item>Built-in catalog entries that resolve to a known in-tree project
///         (currently only the <c>sample</c> workload). This lets the demo
///         work end-to-end without a real package feed.</item>
/// </list>
///
/// Real NuGet acquisition is deferred — same scope as the in-process branch.
/// </summary>
public interface IWorkloadInstaller
{
    public IReadOnlyList<InstalledWorkloadInfo> GetInstalled();
    public Task<InstalledWorkloadInfo> InstallAsync(string idOrPackage, string? sourceDirectory, CancellationToken cancellationToken = default);
    public Task UninstallAsync(string id, CancellationToken cancellationToken = default);
    public IReadOnlyList<AvailableWorkload> GetAvailable();
}

public sealed class WorkloadInstaller : IWorkloadInstaller
{
    private readonly string _root;
    private readonly IInteractionService _interaction;

    public WorkloadInstaller(IInteractionService interaction)
        : this(interaction, WorkloadPaths.DefaultRoot)
    {
    }

    internal WorkloadInstaller(IInteractionService interaction, string root)
    {
        _interaction = interaction;
        _root = root;
    }

    public IReadOnlyList<InstalledWorkloadInfo> GetInstalled() => ReadManifest().Workloads.AsReadOnly();

    public async Task<InstalledWorkloadInfo> InstallAsync(string idOrPackage, string? sourceDirectory, CancellationToken cancellationToken = default)
    {
        var sourceManifest = ResolveSourceManifest(idOrPackage, sourceDirectory)
            ?? throw new GracefulException(
                $"Could not locate a workload to install for '{idOrPackage}'. " +
                $"Pass --from <path-to-workload-dir> to install from a local build.",
                isUserError: true);

        var (manifest, sourceDir) = sourceManifest;

        var installDir = WorkloadPaths.InstallDirectory(_root, manifest.Id, manifest.Version);
        if (Directory.Exists(installDir))
        {
            Directory.Delete(installDir, recursive: true);
        }
        Directory.CreateDirectory(installDir);

        // Copy the workload payload (workload.json + executable + any siblings)
        // into the install directory. This keeps the installed copy stable even
        // if the source directory moves.
        foreach (var path in Directory.EnumerateFileSystemEntries(sourceDir, "*", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(sourceDir, path);
            var dest = Path.Combine(installDir, rel);
            if (Directory.Exists(path))
            {
                Directory.CreateDirectory(dest);
            }
            else
            {
                Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                File.Copy(path, dest, overwrite: true);
            }
        }

        // Make sure the executable is actually executable (Unix bit drops on copy).
        if (!OperatingSystem.IsWindows())
        {
            var exePath = Path.Combine(installDir, manifest.Executable);
            if (File.Exists(exePath))
            {
                var mode = File.GetUnixFileMode(exePath);
                File.SetUnixFileMode(exePath, mode | UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute);
            }
        }

        var info = new InstalledWorkloadInfo
        {
            Id = manifest.Id,
            PackageId = $"Azure.Functions.Cli.Workload.{manifest.Id}",
            Version = manifest.Version,
            InstallPath = installDir,
            InstalledAt = DateTimeOffset.UtcNow,
        };

        var registry = ReadManifest();
        registry.Workloads.RemoveAll(w => string.Equals(w.Id, info.Id, StringComparison.OrdinalIgnoreCase));
        registry.Workloads.Add(info);
        await WriteManifestAsync(registry, cancellationToken).ConfigureAwait(false);

        _interaction.WriteSuccess($"Workload '{info.Id}' ({info.Version}) installed.");
        return info;
    }

    public async Task UninstallAsync(string id, CancellationToken cancellationToken = default)
    {
        var registry = ReadManifest();
        var existing = registry.Workloads.FirstOrDefault(w => string.Equals(w.Id, id, StringComparison.OrdinalIgnoreCase));
        if (existing is null)
        {
            _interaction.WriteWarning($"Workload '{id}' is not installed.");
            return;
        }

        if (Directory.Exists(existing.InstallPath))
        {
            Directory.Delete(existing.InstallPath, recursive: true);
        }

        var parent = Path.GetDirectoryName(existing.InstallPath);
        if (parent is not null && Directory.Exists(parent) && !Directory.EnumerateFileSystemEntries(parent).Any())
        {
            Directory.Delete(parent);
        }

        registry.Workloads.Remove(existing);
        await WriteManifestAsync(registry, cancellationToken).ConfigureAwait(false);
        _interaction.WriteSuccess($"Workload '{id}' uninstalled.");
    }

    public IReadOnlyList<AvailableWorkload> GetAvailable()
    {
        var installed = GetInstalled().ToDictionary(w => w.Id, w => w.Version, StringComparer.OrdinalIgnoreCase);
        return [.. WorkloadCatalog.Entries.Select(e => e with { InstalledVersion = installed.GetValueOrDefault(e.Id) })];
    }

    private (WorkloadManifestFile Manifest, string SourceDir)? ResolveSourceManifest(string idOrPackage, string? sourceDirectory)
    {
        // 1. Explicit --from path
        if (!string.IsNullOrEmpty(sourceDirectory))
        {
            var manifestPath = Path.Combine(sourceDirectory, "workload.json");
            if (!File.Exists(manifestPath))
            {
                throw new GracefulException(
                    $"No workload.json found at '{sourceDirectory}'.",
                    isUserError: true);
            }

            var json = File.ReadAllText(manifestPath);
            var manifest = JsonSerializer.Deserialize(json, WorkloadJsonContext.Default.WorkloadManifestFile)
                ?? throw new GracefulException($"Invalid workload.json at '{manifestPath}'.", isUserError: true);
            return (manifest, sourceDirectory);
        }

        // 2. Built-in scaffold: only the in-tree sample resolves to a buildable
        //    target. The other catalog entries are placeholders, same as the
        //    in-process branch — we surface a friendly "not yet available" error.
        var alias = WorkloadCatalog.FindByAlias(idOrPackage);
        if (alias is null)
        {
            return null;
        }

        if (!string.Equals(alias.Id, "sample", StringComparison.OrdinalIgnoreCase))
        {
            throw new GracefulException(
                $"Workload '{alias.Id}' is in the catalog but no install source is wired up yet. " +
                $"Build it locally and install with: func workload install {alias.Id} --from <path>",
                isUserError: true);
        }

        var sampleSource = LocateInTreeSample()
            ?? throw new GracefulException(
                "Could not locate the in-tree sample workload build output. " +
                "Build src/Func.Workload.Sample first, then retry.",
                isUserError: true);

        var sampleManifestPath = Path.Combine(sampleSource, "workload.json");
        if (!File.Exists(sampleManifestPath))
        {
            throw new GracefulException(
                $"Sample workload at '{sampleSource}' is missing workload.json.",
                isUserError: false);
        }

        var sampleJson = File.ReadAllText(sampleManifestPath);
        var sampleManifest = JsonSerializer.Deserialize(sampleJson, WorkloadJsonContext.Default.WorkloadManifestFile)!;
        return (sampleManifest, sampleSource);
    }

    private static string? LocateInTreeSample()
    {
        // Walk up from the host binary looking for the sample's build output.
        // This is a development convenience only — production install would
        // come from a NuGet package or an explicit --from path.
        var current = AppContext.BaseDirectory;
        for (var i = 0; i < 8; i++)
        {
            var probe = Path.Combine(current, "src", "Func.Workload.Sample", "bin");
            if (Directory.Exists(probe))
            {
                var candidate = Directory.EnumerateFiles(probe, "workload.json", SearchOption.AllDirectories)
                    .OrderByDescending(f => File.GetLastWriteTimeUtc(f))
                    .FirstOrDefault();
                if (candidate is not null)
                {
                    return Path.GetDirectoryName(candidate);
                }
            }
            current = Path.GetDirectoryName(current.TrimEnd(Path.DirectorySeparatorChar)) ?? current;
        }
        return null;
    }

    private InstalledWorkloadsManifest ReadManifest()
    {
        var path = WorkloadPaths.ManifestPath(_root);
        if (!File.Exists(path))
        {
            return new InstalledWorkloadsManifest();
        }

        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize(json, HostJsonContext.Default.InstalledWorkloadsManifest)
            ?? new InstalledWorkloadsManifest();
    }

    private async Task WriteManifestAsync(InstalledWorkloadsManifest manifest, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(_root);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(manifest, HostJsonContext.Default.InstalledWorkloadsManifest);
        await File.WriteAllBytesAsync(WorkloadPaths.ManifestPath(_root), bytes, cancellationToken).ConfigureAwait(false);
    }
}
