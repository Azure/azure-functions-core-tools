// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Projects;
using Azure.Functions.Cli.Workloads.Storage;

namespace Azure.Functions.Cli.Templates;

/// <summary>
/// Finds installed templates content by declared stack alias or legacy package identity.
/// </summary>
internal sealed class InstalledTemplatesWorkloads(IWorkloadStore store, IWorkloadPaths paths)
    : IInstalledTemplatesWorkloads
{
    private readonly IWorkloadStore _store = store ?? throw new ArgumentNullException(nameof(store));
    private readonly IWorkloadPaths _paths = paths ?? throw new ArgumentNullException(nameof(paths));

    public Task<IReadOnlyList<InstalledTemplatesWorkload>> ListInstalledAsync(
        string stack, CancellationToken cancellationToken = default)
        => ListInstalledAsync(stack, cancellationToken, channel: null);

    /// <summary>
    /// Returns every matching version of one logical package, retaining its physical install directory.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// <paramref name="stack"/> is null, empty, or whitespace.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// Competing custom owners claim the selected channel, or only unsupported templates rows match.
    /// </exception>
    public async Task<IReadOnlyList<InstalledTemplatesWorkload>> ListInstalledAsync(
        string stack,
        CancellationToken cancellationToken,
        BundleChannel? channel)
    {
        if (string.IsNullOrWhiteSpace(stack))
        {
            throw new ArgumentException("Stack must be non-empty.", nameof(stack));
        }

        cancellationToken.ThrowIfCancellationRequested();

        string normalizedStack = stack.Trim().ToLowerInvariant();
        IReadOnlyList<WorkloadEntry> entries = await _store.GetWorkloadsAsync(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

        IReadOnlyList<WorkloadEntry> matches = TemplatesWorkloadPolicy.Select(entries, normalizedStack, channel);
        List<InstalledTemplatesWorkload> result = [];
        foreach (WorkloadEntry entry in matches)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Template readers append tools/any/content themselves, so this must remain the extracted package root.
            string installDir = _paths.GetInstallDirectory(entry.PackageId, entry.PackageVersion);
            result.Add(new InstalledTemplatesWorkload(normalizedStack, entry.PackageVersion, installDir));
        }

        return result;
    }
}
