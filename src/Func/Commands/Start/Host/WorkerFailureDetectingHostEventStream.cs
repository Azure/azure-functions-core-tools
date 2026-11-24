// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Runtime.CompilerServices;
using Azure.Functions.Cli.Hosting.Events;
using Microsoft.Extensions.Logging;

namespace Azure.Functions.Cli.Commands.Start.Host;

/// <summary>
/// Wraps the host event stream and records when the host gives up restarting 
/// a failing language worker. The host then exits 0, so its log is the only
/// place the failure shows.
/// Entries flow through untouched.
/// </summary>
internal sealed class WorkerFailureDetectingHostEventStream(IHostEventStream inner) : IHostEventStream, IHostEventStreamLifecycle
{
    private const string RetriesExhaustedPrefix = "Exceeded language worker restart retry count for runtime:";
    private const string WorkerExitedPrefix = "Language Worker Process exited";

    private readonly IHostEventStream _inner = inner ?? throw new ArgumentNullException(nameof(inner));
    private string? _lastExitCause;

    /// <summary>True once the host reports it stopped restarting a failing worker.</summary>
    public bool Detected => Runtime is not null;

    /// <summary>The failing worker's runtime, e.g. <c>python</c>; <see langword="null"/> until detected.</summary>
    public string? Runtime { get; private set; }

    /// <summary>The worker's reported error, when the host surfaced one.</summary>
    public string? Cause { get; private set; }

    public async IAsyncEnumerable<HostLogEntry> ReadAsync([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await foreach (HostLogEntry entry in _inner.ReadAsync(cancellationToken))
        {
            Observe(entry);
            yield return entry;
        }
    }

    public Task RequestShutdownAsync(CancellationToken cancellationToken)
        => _inner is IHostEventStreamLifecycle lifecycle ? lifecycle.RequestShutdownAsync(cancellationToken) : Task.CompletedTask;

    public Task<int> WaitForExitAsync(CancellationToken cancellationToken)
        => _inner is IHostEventStreamLifecycle lifecycle ? lifecycle.WaitForExitAsync(cancellationToken) : Task.FromResult(0);

    public ValueTask DisposeAsync() => _inner.DisposeAsync();

    private void Observe(HostLogEntry entry)
    {
        if (entry.Level < LogLevel.Error || Detected)
        {
            return;
        }

        if (entry.Message.StartsWith(WorkerExitedPrefix, StringComparison.Ordinal))
        {
            HostLogExceptionDetails? cause = entry.ExceptionDetails;
            while (cause?.InnerException is not null)
            {
                cause = cause.InnerException;
            }

            // The host repeats the worker's error once per restart attempt, joined with commas.
            _lastExitCause = cause is null ? null : string.Join(", ", cause.Message.Split(',', StringSplitOptions.TrimEntries).Distinct());
        }
        else if (entry.Message.StartsWith(RetriesExhaustedPrefix, StringComparison.Ordinal))
        {
            Runtime = entry.Message[RetriesExhaustedPrefix.Length..].Split(['.', ' '])[0];
            Cause = _lastExitCause;
        }
    }
}
