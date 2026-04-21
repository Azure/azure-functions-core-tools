// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Text.Json;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Console;

namespace Azure.Functions.Cli.Workloads;

/// <summary>
/// Discovers installed out-of-process workloads, spawns clients on demand,
/// and brokers high-level operations (init / templates / pack) by routing to
/// the right workload based on worker runtime.
/// </summary>
public interface IWorkloadHost
{
    /// <summary>Discovered workloads, manifest-only (no process spawned).</summary>
    public IReadOnlyList<DiscoveredWorkload> DiscoverWorkloads();

    /// <summary>Worker runtimes installed across all workloads.</summary>
    public IReadOnlyList<string> GetAvailableRuntimes();

    /// <summary>Spawns the workload that owns <paramref name="workerRuntime"/> and runs initialize.</summary>
    public Task<IWorkloadClient> StartForRuntimeAsync(string workerRuntime, CancellationToken cancellationToken = default);

    /// <summary>Spawns a workload by id (used by detect-then-route flows).</summary>
    public Task<IWorkloadClient> StartByIdAsync(string workloadId, CancellationToken cancellationToken = default);

    /// <summary>Asks each workload to detect a project in <paramref name="directory"/>; returns the best match.</summary>
    public Task<(DiscoveredWorkload Workload, ProjectDetectResult Detection)?> DetectProjectAsync(string directory, CancellationToken cancellationToken = default);
}

/// <summary>A workload found on disk along with its manifest and install path.</summary>
public sealed record DiscoveredWorkload(WorkloadManifestFile Manifest, string InstallDirectory);

public sealed class WorkloadHost : IWorkloadHost
{
    private readonly string _root;
    private readonly IInteractionService _interaction;
    private List<DiscoveredWorkload>? _cache;

    public WorkloadHost(IInteractionService interaction)
        : this(interaction, WorkloadPaths.DefaultRoot)
    {
    }

    internal WorkloadHost(IInteractionService interaction, string root)
    {
        _interaction = interaction;
        _root = root;
    }

    public IReadOnlyList<DiscoveredWorkload> DiscoverWorkloads()
    {
        if (_cache is not null)
        {
            return _cache;
        }

        var found = new List<DiscoveredWorkload>();
        if (!Directory.Exists(_root))
        {
            _cache = found;
            return _cache;
        }

        foreach (var manifestPath in Directory.EnumerateFiles(_root, "workload.json", SearchOption.AllDirectories))
        {
            try
            {
                var json = File.ReadAllText(manifestPath);
                var manifest = JsonSerializer.Deserialize(json, WorkloadJsonContext.Default.WorkloadManifestFile);
                if (manifest is null || string.IsNullOrEmpty(manifest.Id) || string.IsNullOrEmpty(manifest.Executable))
                {
                    continue;
                }

                if (manifest.ProtocolVersion != WorkloadProtocol.Version)
                {
                    _interaction.WriteWarning(
                        $"Skipping workload '{manifest.Id}': protocol {manifest.ProtocolVersion} ≠ host {WorkloadProtocol.Version}.");
                    continue;
                }

                found.Add(new DiscoveredWorkload(manifest, Path.GetDirectoryName(manifestPath)!));
            }
            catch (Exception ex)
            {
                _interaction.WriteWarning($"Skipping malformed workload manifest at '{manifestPath}': {ex.Message}");
            }
        }

        _cache = found;
        return _cache;
    }

    public IReadOnlyList<string> GetAvailableRuntimes() =>
        DiscoverWorkloads()
            .SelectMany(w => w.Manifest.WorkerRuntimes)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(r => r, StringComparer.OrdinalIgnoreCase)
            .ToList();

    public async Task<IWorkloadClient> StartForRuntimeAsync(string workerRuntime, CancellationToken cancellationToken = default)
    {
        var workload = DiscoverWorkloads()
            .FirstOrDefault(w => w.Manifest.WorkerRuntimes.Contains(workerRuntime, StringComparer.OrdinalIgnoreCase));

        if (workload is null)
        {
            throw new GracefulException(
                $"No installed workload supports runtime '{workerRuntime}'. " +
                $"Install one with: func workload install <name>",
                isUserError: true);
        }

        return await SpawnAsync(workload, cancellationToken).ConfigureAwait(false);
    }

    public async Task<IWorkloadClient> StartByIdAsync(string workloadId, CancellationToken cancellationToken = default)
    {
        var workload = DiscoverWorkloads()
            .FirstOrDefault(w => string.Equals(w.Manifest.Id, workloadId, StringComparison.OrdinalIgnoreCase))
            ?? throw new GracefulException($"Workload '{workloadId}' is not installed.", isUserError: true);

        return await SpawnAsync(workload, cancellationToken).ConfigureAwait(false);
    }

    public async Task<(DiscoveredWorkload Workload, ProjectDetectResult Detection)?> DetectProjectAsync(string directory, CancellationToken cancellationToken = default)
    {
        // Cheap pre-filter using projectMarkers from the manifest. Avoids
        // spawning every installed workload just to detect one project.
        var candidates = DiscoverWorkloads()
            .Where(w => w.Manifest.ProjectMarkers.Count == 0
                || w.Manifest.ProjectMarkers.Any(p => Directory.EnumerateFiles(directory, p, SearchOption.TopDirectoryOnly).Any()))
            .ToList();

        (DiscoveredWorkload, ProjectDetectResult)? best = null;
        foreach (var candidate in candidates)
        {
            try
            {
                await using var client = await SpawnAsync(candidate, cancellationToken).ConfigureAwait(false);
                var detection = await client.InvokeAsync(
                    WorkloadProtocol.Methods.ProjectDetect,
                    new ProjectDetectParams(directory),
                    WorkloadJsonContext.Default.ProjectDetectParams,
                    WorkloadJsonContext.Default.ProjectDetectResult,
                    cancellationToken).ConfigureAwait(false);

                if (!detection.Matched)
                {
                    continue;
                }

                if (best is null || detection.Confidence > best.Value.Item2.Confidence)
                {
                    best = (candidate, detection);
                }
            }
            catch (Exception ex)
            {
                _interaction.WriteWarning($"Workload '{candidate.Manifest.Id}' detect failed: {ex.Message}");
            }
        }

        return best;
    }

    private static async Task<IWorkloadClient> SpawnAsync(DiscoveredWorkload workload, CancellationToken cancellationToken)
    {
        var client = WorkloadClient.Spawn(workload.Manifest, workload.InstallDirectory);
        try
        {
            await client.InitializeAsync(Directory.GetCurrentDirectory(), cancellationToken).ConfigureAwait(false);
            return client;
        }
        catch
        {
            await client.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }
}
