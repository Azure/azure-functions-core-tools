// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Workloads;
using Azure.Functions.Cli.Workloads.Discovery;

namespace Azure.Functions.Cli.Templates.Engine;

/// <summary>
/// Supplies template roots declared by the runtime workloads loaded at startup.
/// </summary>
internal sealed class WorkloadTemplateSource(IWorkloadProvider workloadProvider, IFileSystem fileSystem)
    : IWorkloadTemplateSource
{
    private readonly IWorkloadProvider _workloadProvider = workloadProvider
        ?? throw new ArgumentNullException(nameof(workloadProvider));
    private readonly IFileSystem _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));

    /// <inheritdoc />
    public Task<IReadOnlyList<WorkloadTemplateRoot>> GetRootsAsync(CancellationToken cancellation = default)
    {
        List<WorkloadTemplateRoot> roots = [];
        foreach (RuntimeWorkloadInfo workload in _workloadProvider.GetRuntimeWorkloads())
        {
            cancellation.ThrowIfCancellationRequested();
            if (string.IsNullOrEmpty(workload.TemplateRoot))
            {
                continue;
            }

            if (!_fileSystem.DirectoryExists(workload.TemplateRoot))
            {
                // TODO: Report a diagnostic for the missing template root when --verbose is set.
                continue;
            }

            roots.Add(new WorkloadTemplateRoot(
                workload.TemplateRoot,
                workload.PackageId,
                workload.PackageVersion,
                GetLastChangeTime(workload)));
        }

        return Task.FromResult<IReadOnlyList<WorkloadTemplateRoot>>(roots);
    }

    // Local builds can reuse a version, so include workload.json to trigger a template rescan on reinstall.
    private DateTime GetLastChangeTime(RuntimeWorkloadInfo workload)
    {
        DateTime lastChange = _fileSystem.GetLastWriteTimeUtc(workload.TemplateRoot!);
        string metadataPath = Path.Combine(workload.InstallDirectory, WorkloadMetadataReader.MetadataFileName);
        if (_fileSystem.FileExists(metadataPath))
        {
            DateTime metadataChange = _fileSystem.GetLastWriteTimeUtc(metadataPath);
            if (metadataChange > lastChange)
            {
                lastChange = metadataChange;
            }
        }

        return lastChange;
    }
}
