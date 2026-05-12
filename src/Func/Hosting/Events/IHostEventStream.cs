// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Hosting.Events;

/// <summary>
/// Source of <see cref="HostLogEntry"/> records consumed by the CLI dashboard.
/// Implementations include an in-memory fake for tests/prototyping and, in
/// the future, a transport-backed source bridging the real host (gRPC, OTLP,
/// or similar). The abstraction is intentionally transport-neutral.
/// </summary>
internal interface IHostEventStream
{
    /// <summary>
    /// Reads records as they arrive. Completes when the source signals end
    /// of stream or <paramref name="cancellationToken"/> is fired.
    /// </summary>
    public IAsyncEnumerable<HostLogEntry> ReadAsync(CancellationToken cancellationToken);
}
