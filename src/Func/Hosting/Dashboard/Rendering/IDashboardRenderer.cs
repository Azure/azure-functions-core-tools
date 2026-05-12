// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Hosting.Events;

namespace Azure.Functions.Cli.Hosting.Dashboard.Rendering;

/// <summary>
/// Surface every renderer (compact, plain, JSON) implements. The
/// <see cref="DashboardPipeline"/> drives the lifecycle.
/// </summary>
internal interface IDashboardRenderer : IAsyncDisposable
{
    /// <summary>
    /// Called once before any events are observed. Renderers should print
    /// any banner / header content here.
    /// </summary>
    public Task OnStartAsync(DashboardState state, CancellationToken cancellationToken);

    /// <summary>
    /// Called for every <see cref="HostLogEntry"/> observed, along with any
    /// synthetic events the state derived from it.
    /// </summary>
    public Task OnEventAsync(HostLogEntry entry, IReadOnlyList<DashboardEvent> events, CancellationToken cancellationToken);

    /// <summary>
    /// Called once after the stream completes (graceful shutdown, source
    /// disconnect, or cancellation). Renderers should print their final
    /// summary here.
    /// </summary>
    public Task OnSummaryAsync(SummaryEvent summary, CancellationToken cancellationToken);
}
