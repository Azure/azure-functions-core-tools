// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Runtime.ExceptionServices;
using Azure.Functions.Cli.Common;

namespace Azure.Functions.Cli.Projects;

internal sealed class ProjectTemplateInputLease(IFileSystem fileSystem)
{
    private const int MaximumInputCount = 256;
    private readonly IFileSystem _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));

    public IDisposable Pin(IReadOnlyList<string> paths, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(paths);
        cancellationToken.ThrowIfCancellationRequested();
        if (paths.Count is 0 or > MaximumInputCount
            || paths.Any(path => !Path.IsPathFullyQualified(path))
            || paths.Distinct(StringComparer.OrdinalIgnoreCase).Count() != paths.Count)
        {
            throw new InvalidDataException("The evaluated project input set is invalid or exceeds its limit.");
        }

        List<Stream> streams = [];
        bool transferred = false;
        try
        {
            foreach (string path in paths)
            {
                cancellationToken.ThrowIfCancellationRequested();
                streams.Add(_fileSystem.OpenReadNoFollow(path, cancellationToken));
            }

            PinnedInputs lease = new(streams);
            transferred = true;
            return lease;
        }
        catch (Exception exception)
        {
            try
            {
                DisposeStreams(streams);
            }
            catch (Exception cleanupException)
            {
                // Preserve the opening failure while retaining the cleanup uncertainty for the caller.
                exception.Data["Project input lease cleanup"] = cleanupException;
            }

            ExceptionDispatchInfo.Capture(exception).Throw();
            throw;
        }
        finally
        {
            if (!transferred)
            {
                streams.Clear();
            }
        }
    }

    private static void DisposeStreams(IEnumerable<Stream> streams)
    {
        List<Exception> failures = [];
        foreach (Stream stream in streams)
        {
            try
            {
                stream.Dispose();
            }
            catch (Exception exception)
            {
                // Attempt to close every pinned input before reporting any cleanup failure.
                failures.Add(exception);
            }
        }

        if (failures.Count > 0)
        {
            throw new AggregateException("One or more project input handles could not be released.", failures);
        }
    }

    private sealed class PinnedInputs(List<Stream> streams) : IDisposable
    {
        private List<Stream>? _streams = streams;

        public void Dispose()
        {
            List<Stream>? owned = Interlocked.Exchange(ref _streams, null);
            if (owned is not null)
            {
                DisposeStreams(owned);
            }
        }
    }
}
