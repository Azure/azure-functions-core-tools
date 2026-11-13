// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Reflection;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Workloads.Discovery;
using Azure.Functions.Cli.Workloads.Storage;

namespace Azure.Functions.Cli.Workloads.Loading;

/// <summary>
/// Default <see cref="IWorkloadLoader"/>. Loads each entry into its own
/// collectible <see cref="WorkloadLoadContext"/> so dependency versions
/// don't collide across workloads.
/// </summary>
internal sealed class WorkloadLoader(IWorkloadRuntimeIdentifierProvider runtimeIdentifierProvider) : IWorkloadLoader
{
    private readonly IWorkloadRuntimeIdentifierProvider _runtimeIdentifierProvider = runtimeIdentifierProvider
        ?? throw new ArgumentNullException(nameof(runtimeIdentifierProvider));

    public WorkloadLoader() : this(new WorkloadRuntimeIdentifierProvider())
    {
    }

    /// <inheritdoc />
    public IReadOnlyList<RuntimeWorkloadInfo> Load(IReadOnlyList<WorkloadEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);

        var results = new List<RuntimeWorkloadInfo>(entries.Count);
        foreach (WorkloadEntry entry in entries)
        {
            try
            {
                results.Add(LoadEntry(entry));
            }
            catch (InvalidWorkloadException ex) when (HasRuntimeIdentifierMismatch(entry))
            {
                string logicalPackageId = entry.LogicalPackage?.PackageId ?? entry.PackageId;
                throw new InvalidWorkloadException(
                    $"{ex.Message} The installed implementation targets runtime identifier '{entry.RuntimeIdentifier}', " +
                    $"but this machine uses '{_runtimeIdentifierProvider.Current}'. " +
                    $"Run 'func workload update {logicalPackageId}' to install the matching implementation.",
                    ex);
            }
        }

        return results;
    }

    private bool HasRuntimeIdentifierMismatch(WorkloadEntry entry)
        => entry.RuntimeIdentifier is not null
            && !string.Equals(
                entry.RuntimeIdentifier,
                _runtimeIdentifierProvider.Current,
                StringComparison.OrdinalIgnoreCase);

    private RuntimeWorkloadInfo LoadEntry(WorkloadEntry entry)
    {
        if (entry.Kind != WorkloadKind.Workload)
        {
            throw new InvalidOperationException(
                $"[{entry.PackageId}] WorkloadLoader was invoked for a non-runtime entry (kind={entry.Kind}). " +
                "Only kind=workload entries should reach the loader. This is a CLI bug.");
        }

        EntryPointSpec entryPoint = entry.EntryPoint
            ?? throw new InvalidOperationException(
                $"[{entry.PackageId}] WorkloadLoader was invoked for a runtime entry without an EntryPoint. " +
                "Only kind=workload entries should reach the loader. This is a CLI bug.");

        string installPath = entry.PackageRoot;
        if (string.IsNullOrWhiteSpace(installPath))
        {
            throw new InvalidWorkloadException($"[{entry.PackageId}] Could not load workload: package root is missing.");
        }

        string assemblyPath = ResolveAssemblyPath(installPath, entryPoint.AssemblyPath, entry.PackageId);
        string contentRoot = Path.GetDirectoryName(assemblyPath)!;

        string? templateRoot = ResolveTemplateRoot(entry, installPath);

        if (!File.Exists(assemblyPath))
        {
            throw new InvalidWorkloadException(
                $"[{entry.PackageId}] Could not load workload: assembly '{entryPoint.AssemblyPath}' was not found at '{installPath}'.");
        }

        WorkloadLoadContext loadContext = new(entry.PackageId, assemblyPath);
        Assembly assembly = loadContext.LoadFromAssemblyPath(assemblyPath);

        Type? type = assembly.GetType(entryPoint.Type, throwOnError: false) ?? throw new InvalidWorkloadException(
                $"[{entry.PackageId}] Could not load workload: type '{entryPoint.Type}' was not found in '{entryPoint.AssemblyPath}' (install path: '{installPath}').");

        if (!typeof(Workload).IsAssignableFrom(type))
        {
            throw new InvalidWorkloadException(
                $"[{entry.PackageId}] Could not load workload: type '{entryPoint.Type}' does not derive from {nameof(Workload)} (install path: '{installPath}').");
        }

        var instance = (Workload)Activator.CreateInstance(type)!;
        return new RuntimeWorkloadInfo(
            Instance: instance,
            PackageId: entry.PackageId,
            PackageVersion: entry.PackageVersion,
            Aliases: entry.Aliases,
            InstallDirectory: installPath,
            ContentRoot: contentRoot,
            DisplayName: instance.DisplayName,
            Description: instance.Description,
            LoadContext: loadContext,
            TemplateRoot: templateRoot);
    }

    /// <summary>
    /// Resolves the registry's package-relative template root to an absolute path inside the install directory.
    /// </summary>
    /// <remarks>
    /// Existence is not checked here; a missing template root must not prevent the workload from loading.
    /// </remarks>
    private static string? ResolveTemplateRoot(WorkloadEntry entry, string installPath)
    {
        if (entry.TemplateRoot is null)
        {
            return null;
        }

        return ResolvePackagePath(installPath, entry.TemplateRoot, entry.PackageId, "template root");
    }

    /// <summary>
    /// Resolves the entry-point assembly path, first relative to the install directory
    /// (where workload.json lives), then falling back to the legacy tools/any/ subdirectory.
    /// </summary>
    private static string ResolveAssemblyPath(string installPath, string relativeAssemblyPath, string packageId)
    {
        // Try resolving directly relative to the install root (workload.json location).
        string candidate = ResolvePackagePath(installPath, relativeAssemblyPath, packageId, "assembly path");
        if (File.Exists(candidate))
        {
            return candidate;
        }

        // Fall back to legacy tools/any/ layout.
        string legacyCandidate = ResolvePackagePath(installPath, Path.Combine("tools", "any", relativeAssemblyPath), packageId, "assembly path");
        if (File.Exists(legacyCandidate))
        {
            return legacyCandidate;
        }

        // Return the primary candidate so the caller produces a clear error message.
        return candidate;
    }

    private static string ResolvePackagePath(string installPath, string relativePath, string packageId, string pathDescription)
    {
        // The on-disk registry can be tampered with after install-time validation.
        try
        {
            return Path.CombineNormalized(installPath, relativePath);
        }
        catch (ArgumentException ex)
        {
            throw new InvalidWorkloadException(
                $"[{packageId}] Could not load workload: {pathDescription} '{relativePath}' is invalid or escapes its base directory. {ex.Message}",
                ex);
        }
    }
}
