// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Hosting.Events;

namespace Azure.Functions.Cli.Hosting.Dashboard;

/// <summary>
/// Observes every dashboard event that flows through the pipeline.
/// </summary>
internal interface IDashboardEventSink : IAsyncDisposable
{
    public Task OnEventAsync(HostLogEntry entry, IReadOnlyList<DashboardEvent> events, CancellationToken cancellationToken);
}
