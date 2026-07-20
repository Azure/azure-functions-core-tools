// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Runtime.CompilerServices;
using Azure.Functions.Cli.Hosting.Events;
using Microsoft.Extensions.Logging;

namespace Azure.Functions.Cli.Commands.Start.Azurite;

/// <summary>
/// Wraps the host event stream and watches for a 500 from the CLI's managed
/// Azurite instance. Host entries flow through untouched. On the first
/// detection it injects a single warning entry with reset guidance so the
/// failure is visible live, and flips <see cref="Detected"/> so the caller can
/// repeat the guidance in the end-of-run summary. Only installed when the CLI
/// launched Azurite, so it stays inert for user-managed or disabled emulators.
/// </summary>
internal sealed class AzuriteFailureDetectingHostEventStream : IHostEventStream, IHostEventStreamLifecycle
{
    private const string GuidanceCategory = "Azurite";

    private readonly IHostEventStream _inner;
    private readonly string _guidanceMessage;
    private readonly TimeProvider _timeProvider;
    private readonly AzuriteBlobFailureDetector _detector = new();

    public AzuriteFailureDetectingHostEventStream(IHostEventStream inner, string guidanceMessage, TimeProvider? timeProvider = null)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _guidanceMessage = guidanceMessage ?? throw new ArgumentNullException(nameof(guidanceMessage));
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <summary>True once a managed-Azurite 500 has been detected in host output.</summary>
    public bool Detected => _detector.Detected;

    public async IAsyncEnumerable<HostLogEntry> ReadAsync([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await foreach (HostLogEntry entry in _inner.ReadAsync(cancellationToken))
        {
            yield return entry;

            if (!_detector.Detected && Observe(entry))
            {
                yield return CreateGuidanceEntry();
            }
        }
    }

    public Task RequestShutdownAsync(CancellationToken cancellationToken)
        => _inner is IHostEventStreamLifecycle lifecycle ? lifecycle.RequestShutdownAsync(cancellationToken) : Task.CompletedTask;

    public Task<int> WaitForExitAsync(CancellationToken cancellationToken)
        => _inner is IHostEventStreamLifecycle lifecycle ? lifecycle.WaitForExitAsync(cancellationToken) : Task.FromResult(0);

    public ValueTask DisposeAsync() => _inner.DisposeAsync();

    private bool Observe(HostLogEntry entry)
    {
        // Markers can be in the formatted message or, when the host attaches
        // the RequestFailedException, in its dump.
        bool detected = _detector.Observe(entry.Message);
        if (!detected && entry.Exception is { } exception)
        {
            detected = _detector.Observe(exception.ToString());
        }

        return detected;
    }

    private HostLogEntry CreateGuidanceEntry()
        => new(
            _timeProvider.GetUtcNow(),
            GuidanceCategory,
            LogLevel.Warning,
            default,
            _guidanceMessage,
            Exception: null,
            HostLogEntry.EmptyAttributes);
}
