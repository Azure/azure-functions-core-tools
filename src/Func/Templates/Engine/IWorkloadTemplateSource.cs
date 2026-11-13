// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Templates.Engine;

/// <summary>
/// Supplies template roots contributed by installed workloads.
/// </summary>
internal interface IWorkloadTemplateSource
{
    /// <summary>
    /// Gets the template roots contributed by active workloads.
    /// </summary>
    /// <param name="cancellation">A cancellation token.</param>
    /// <returns>The template roots contributed by active workloads.</returns>
    public Task<IReadOnlyList<WorkloadTemplateRoot>> GetRootsAsync(CancellationToken cancellation = default);
}
