// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Runtime.CompilerServices;

namespace Azure.Functions.Cli.Hosting.Events;

/// <summary>
/// Reads multiple host event streams sequentially as one stream.
/// </summary>
internal sealed class CompositeHostEventStream(IEnumerable<IHostEventStream> sources) : IHostEventStream
{
    private readonly IHostEventStream[] _sources = CopySources(sources);

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
}
