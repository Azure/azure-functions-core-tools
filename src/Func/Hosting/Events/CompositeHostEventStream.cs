// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Runtime.CompilerServices;

namespace Azure.Functions.Cli.Hosting.Events;

/// <summary>
/// Reads multiple host event streams sequentially as one stream.
/// </summary>
internal sealed class CompositeHostEventStream : IHostEventStream, IHostEventStreamLifecycle
{
    private readonly IHostEventStream[] _sources;
    private readonly IHostEventStreamLifecycle? _lifecycle;

    public CompositeHostEventStream(IEnumerable<IHostEventStream> sources)
    {
        _sources = CopySources(sources);
        _lifecycle = GetLifecycle(_sources);
    }

    public async IAsyncEnumerable<HostLogEntry> ReadAsync([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        foreach (IHostEventStream source in _sources)
        {
            await foreach (HostLogEntry entry in source.ReadAsync(cancellationToken))
            {
                yield return entry;
            }
        }
    }

    public Task RequestShutdownAsync(CancellationToken cancellationToken)
        => _lifecycle?.RequestShutdownAsync(cancellationToken) ?? Task.CompletedTask;

    public Task<int> WaitForExitAsync(CancellationToken cancellationToken)
        => _lifecycle?.WaitForExitAsync(cancellationToken) ?? Task.FromResult(0);

    private static IHostEventStream[] CopySources(IEnumerable<IHostEventStream> sources)
    {
        ArgumentNullException.ThrowIfNull(sources);

        List<IHostEventStream> copied = [];
        foreach (IHostEventStream source in sources)
        {
            if (source is null)
            {
                throw new ArgumentException("Sources cannot contain null entries.", nameof(sources));
            }

            copied.Add(source);
        }

        return [.. copied];
    }

    private static IHostEventStreamLifecycle? GetLifecycle(IEnumerable<IHostEventStream> sources)
    {
        IHostEventStreamLifecycle? lifecycle = null;
        foreach (IHostEventStream source in sources)
        {
            if (source is IHostEventStreamLifecycle candidate)
            {
                lifecycle = candidate;
            }
        }

        return lifecycle;
    }
}
