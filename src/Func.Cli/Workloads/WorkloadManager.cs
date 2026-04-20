// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json;
using Azure.Functions.Cli.Console;

namespace Azure.Functions.Cli.Workloads;

/// <summary>
/// Manages workload lifecycle — install, uninstall, update, and runtime loading.
/// Workloads are stored under ~/.azure-functions/workloads/{id}/{version}/ and
/// tracked via a workloads.json manifest file.
/// </summary>
public class WorkloadManager : IWorkloadManager
{
    private readonly string _workloadsDirectory;
    private readonly string _manifestPath;
    private readonly IInteractionService _interaction;
    private readonly WorkloadUpdateChecker _updateChecker;

    private WorkloadManifest? _manifest;
    private List<IWorkload>? _loadedWorkloads;
    private Task? _updateCheckTask;

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    /// <summary>
    /// Well-known short aliases for workload package IDs.
    /// Allows <c>func workload install dotnet</c> instead of the full package name.
    /// </summary>
    private static readonly Dictionary<string, string> _wellKnownAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["dotnet"] = "Azure.Functions.Cli.Workload.Dotnet",
        ["node"] = "Azure.Functions.Cli.Workload.Node",
        ["python"] = "Azure.Functions.Cli.Workload.Python",
        ["java"] = "Azure.Functions.Cli.Workload.Java",
        ["powershell"] = "Azure.Functions.Cli.Workload.PowerShell",
    };

    /// <summary>
    /// Catalog of all known workloads with descriptions and language info.
    /// </summary>
    private static readonly AvailableWorkload[] _workloadCatalog =
    [
        new("dotnet", "Azure.Functions.Cli.Workload.Dotnet", ".NET (Isolated Worker)", "C#, F#"),
        new("node", "Azure.Functions.Cli.Workload.Node", "Node.js", "JavaScript, TypeScript"),
        new("python", "Azure.Functions.Cli.Workload.Python", "Python", "Python"),
        new("java", "Azure.Functions.Cli.Workload.Java", "Java", "Java"),
        new("powershell", "Azure.Functions.Cli.Workload.PowerShell", "PowerShell", "PowerShell"),
    ];

    public WorkloadManager(IInteractionService interaction)
        : this(interaction, WorkloadManifest.DefaultWorkloadsDirectory, new WorkloadUpdateChecker())
    {
    }

    internal WorkloadManager(IInteractionService interaction, string workloadsDirectory, WorkloadUpdateChecker? updateChecker = null)
    {
        _interaction = interaction;
        _workloadsDirectory = workloadsDirectory;
        _manifestPath = Path.Combine(workloadsDirectory, "workloads.json");
        _updateChecker = updateChecker ?? new WorkloadUpdateChecker();
    }

    public IReadOnlyList<WorkloadInfo> GetInstalledWorkloads()
    {
        var manifest = ReadManifest();
        return manifest.Workloads.AsReadOnly();
    }

    public async Task<WorkloadInfo> InstallWorkloadAsync(
        string packageIdOrAlias,
        string? version = null,
        CancellationToken cancellationToken = default)
    {
        // TODO: Implement NuGet package download and extraction
        // For now, this creates the directory structure and manifest entry
        // as a scaffold for the real implementation.

        var packageId = ResolvePackageId(packageIdOrAlias);
        var workloadId = ExtractWorkloadId(packageId);
        var resolvedVersion = version ?? "0.0.0-placeholder";
        var installPath = Path.Combine(_workloadsDirectory, workloadId, resolvedVersion);

        Directory.CreateDirectory(installPath);

        var info = new WorkloadInfo(
            Id: workloadId,
            PackageId: packageId,
            Version: resolvedVersion,
            InstallPath: installPath,
            AssemblyName: $"{packageId}.dll",
            InstalledAt: DateTimeOffset.UtcNow);

        var manifest = ReadManifest();
        manifest.Workloads.RemoveAll(w => w.Id == workloadId);
        manifest.Workloads.Add(info);
        await WriteManifestAsync(manifest, cancellationToken);

        _loadedWorkloads = null; // Invalidate cache
        _interaction.WriteSuccess($"Workload '{workloadId}' ({resolvedVersion}) installed.");
        return info;
    }

    public async Task UninstallWorkloadAsync(string workloadId, CancellationToken cancellationToken = default)
    {
        var manifest = ReadManifest();
        var existing = manifest.Workloads.Find(w => w.Id == workloadId);

        if (existing is null)
        {
            _interaction.WriteWarning($"Workload '{workloadId}' is not installed.");
            return;
        }

        // Remove the workload files
        if (Directory.Exists(existing.InstallPath))
        {
            Directory.Delete(existing.InstallPath, recursive: true);
        }

        // Clean up empty parent directory
        var parentDir = Path.GetDirectoryName(existing.InstallPath);
        if (parentDir is not null && Directory.Exists(parentDir)
            && !Directory.EnumerateFileSystemEntries(parentDir).Any())
        {
            Directory.Delete(parentDir);
        }

        manifest.Workloads.Remove(existing);
        await WriteManifestAsync(manifest, cancellationToken);

        _loadedWorkloads = null; // Invalidate cache
        _interaction.WriteSuccess($"Workload '{workloadId}' uninstalled.");
    }

    public async Task<WorkloadInfo> UpdateWorkloadAsync(
        string workloadId,
        string? version = null,
        CancellationToken cancellationToken = default)
    {
        var manifest = ReadManifest();
        var existing = manifest.Workloads.Find(w => w.Id == workloadId);

        if (existing is null)
        {
            throw new InvalidOperationException($"Workload '{workloadId}' is not installed.");
        }

        // Uninstall old, install new
        await UninstallWorkloadAsync(workloadId, cancellationToken);
        return await InstallWorkloadAsync(existing.PackageId, version, cancellationToken);
    }

    public IReadOnlyList<IWorkload> LoadWorkloads()
    {
        if (_loadedWorkloads is not null)
        {
            return _loadedWorkloads.AsReadOnly();
        }

        _loadedWorkloads = [];
        var manifest = ReadManifest();

        foreach (var info in manifest.Workloads)
        {
            try
            {
                var workload = LoadWorkloadAssembly(info);
                if (workload is not null)
                {
                    _loadedWorkloads.Add(workload);
                }
            }
            catch (Exception ex)
            {
                _interaction.WriteWarning(
                    $"Failed to load workload '{info.Id}': {ex.Message}");
            }
        }

        // Fire-and-forget background check for workload updates
        if (manifest.Workloads.Count > 0)
        {
            _updateCheckTask = Task.Run(() => CheckAndNotifyUpdatesAsync(manifest.Workloads));
        }

        return _loadedWorkloads.AsReadOnly();
    }

    /// <summary>
    /// Waits for the background update check to complete and prints any
    /// available update notices. Call this near the end of command execution
    /// so the notice appears after the main output.
    /// </summary>
    public async Task PrintUpdateNoticesAsync()
    {
        if (_updateCheckTask is null)
        {
            return;
        }

        try
        {
            // Wait briefly — the check should already be done or nearly done
            await _updateCheckTask.WaitAsync(TimeSpan.FromSeconds(2));
        }
        catch
        {
            // Timeout or failure — skip notices
        }
    }

    private async Task CheckAndNotifyUpdatesAsync(List<WorkloadInfo> workloads)
    {
        try
        {
            var updates = await _updateChecker.CheckForUpdatesAsync(workloads);
            foreach (var update in updates)
            {
                _interaction.WriteMarkupLine(
                    $"[dim]Update available: workload '{update.WorkloadId}' {update.InstalledVersion} → {update.LatestVersion}. " +
                    $"Run 'func workload update {update.WorkloadId}' to update.[/]");
            }
        }
        catch
        {
            // Never let update checks break the command
        }
    }

    public IReadOnlyList<ITemplateProvider> GetAllTemplateProviders()
    {
        return LoadWorkloads()
            .SelectMany(w => w.GetTemplateProviders())
            .ToList()
            .AsReadOnly();
    }

    public IReadOnlyList<IProjectInitializer> GetAllProjectInitializers()
    {
        return LoadWorkloads()
            .Select(w => w.GetProjectInitializer())
            .Where(p => p is not null)
            .Cast<IProjectInitializer>()
            .ToList()
            .AsReadOnly();
    }

    public IReadOnlyList<IPackProvider> GetAllPackProviders()
    {
        return LoadWorkloads()
            .Select(w => w.GetPackProvider())
            .Where(p => p is not null)
            .Cast<IPackProvider>()
            .ToList()
            .AsReadOnly();
    }

    public IReadOnlyList<string> GetAvailableRuntimes()
    {
        return GetAllProjectInitializers()
            .Select(p => p.WorkerRuntime)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(r => r)
            .ToList()
            .AsReadOnly();
    }

    /// <summary>
    /// Returns all known workloads with their install status. Queries NuGet
    /// for packages matching the workload prefix to discover third-party
    /// workloads, and merges with the built-in catalog. Falls back to the
    /// catalog alone when offline.
    /// </summary>
    public async Task<IReadOnlyList<AvailableWorkload>> GetAvailableWorkloadsAsync(
        CancellationToken cancellationToken = default)
    {
        var installed = GetInstalledWorkloads()
            .ToDictionary(w => w.PackageId, w => w.Version, StringComparer.OrdinalIgnoreCase);

        // Start with the built-in catalog
        var workloads = new Dictionary<string, AvailableWorkload>(StringComparer.OrdinalIgnoreCase);
        foreach (var w in _workloadCatalog)
        {
            installed.TryGetValue(w.PackageId, out var version);
            workloads[w.PackageId] = w with { InstalledVersion = version };
        }

        // Discover additional workloads from NuGet
        try
        {
            var discovered = await NuGetWorkloadSearch.SearchAsync(cancellationToken);
            foreach (var d in discovered)
            {
                if (!workloads.ContainsKey(d.PackageId))
                {
                    installed.TryGetValue(d.PackageId, out var version);
                    workloads[d.PackageId] = d with { InstalledVersion = version };
                }
            }
        }
        catch
        {
            // Offline or NuGet error — just use the catalog
        }

        // Also include any installed workloads not in catalog or NuGet
        foreach (var w in GetInstalledWorkloads())
        {
            if (!workloads.ContainsKey(w.PackageId))
            {
                workloads[w.PackageId] = new AvailableWorkload(
                    w.Id, w.PackageId, w.Id, "", InstalledVersion: w.Version);
            }
        }

        return workloads.Values.OrderBy(w => w.Id).ToList().AsReadOnly();
    }

    private IWorkload? LoadWorkloadAssembly(WorkloadInfo info)
    {
        var assemblyPath = Path.Combine(info.InstallPath, info.AssemblyName);
        if (!File.Exists(assemblyPath))
        {
            _interaction.WriteWarning(
                $"Workload assembly not found: {assemblyPath}");
            return null;
        }

        var loadContext = new AssemblyLoadContext(
            name: $"Workload-{info.Id}",
            isCollectible: false);

        var assembly = loadContext.LoadFromAssemblyPath(assemblyPath);

        // Find the IWorkload implementation in the assembly
        var workloadType = assembly.GetTypes()
            .FirstOrDefault(t => typeof(IWorkload).IsAssignableFrom(t)
                && !t.IsInterface
                && !t.IsAbstract);

        if (workloadType is null)
        {
            _interaction.WriteWarning(
                $"No IWorkload implementation found in {info.AssemblyName}");
            return null;
        }

        return (IWorkload?)Activator.CreateInstance(workloadType);
    }

    private WorkloadManifest ReadManifest()
    {
        if (_manifest is not null)
        {
            return _manifest;
        }

        if (!File.Exists(_manifestPath))
        {
            _manifest = new WorkloadManifest();
            return _manifest;
        }

        var json = File.ReadAllText(_manifestPath);
        _manifest = JsonSerializer.Deserialize<WorkloadManifest>(json, _jsonOptions)
            ?? new WorkloadManifest();
        return _manifest;
    }

    private async Task WriteManifestAsync(WorkloadManifest manifest, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(_workloadsDirectory);
        var json = JsonSerializer.Serialize(manifest, _jsonOptions);
        await File.WriteAllTextAsync(_manifestPath, json, cancellationToken);
        _manifest = manifest;
    }

    /// <summary>
    /// Extracts a short workload ID from a package ID.
    /// E.g., "Azure.Functions.Cli.Workload.Dotnet" → "dotnet"
    /// </summary>
    internal static string ExtractWorkloadId(string packageId)
    {
        var parts = packageId.Split('.');
        return parts[^1].ToLowerInvariant();
    }

    /// <summary>
    /// Resolves a short alias (e.g., "dotnet") to its full NuGet package ID.
    /// If the input is already a full package ID, returns it unchanged.
    /// </summary>
    public static string ResolvePackageId(string packageIdOrAlias)
    {
        if (_wellKnownAliases.TryGetValue(packageIdOrAlias, out var fullPackageId))
        {
            return fullPackageId;
        }

        return packageIdOrAlias;
    }
}
