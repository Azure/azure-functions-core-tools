// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Templates.Engine;

/// <summary>
/// Supplies no template roots until workload template discovery is implemented.
/// </summary>
internal sealed class WorkloadTemplateSource : IWorkloadTemplateSource
{
    /// <inheritdoc />
    public Task<IReadOnlyList<WorkloadTemplateRoot>> GetRootsAsync(CancellationToken cancellation = default)
    {
        // TODO: Discover template roots from installed workloads.
        return Task.FromResult<IReadOnlyList<WorkloadTemplateRoot>>([]);
    }
}
