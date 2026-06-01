// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Azure.Functions.Cli.Hosting.Events;
using Microsoft.Extensions.Logging;

namespace Azure.Functions.Cli.Commands.Start.Host;

internal sealed class HostProcessEventStream : IHostEventStream, IHostEventStreamLifecycle
{
    private readonly IHostProcess _process;
    private readonly IHostProcessOutputParser _parser;
    private readonly TimeProvider _timeProvider;
    private readonly TimeSpan _shutdownTimeout;
    private readonly Channel<HostLogEntry> _channel;
    private readonly Task<int> _exitTask;
    private readonly Task _stdoutTask;
    private readonly Task _stderrTask;
    private int _shutdownRequested;
    private int _disposed;

    public HostProcessEventStream(
        IHostProcess process,
        IHostProcessOutputParser parser,
        HostProcessLaunchInfo launchInfo,
        TimeSpan shutdownTimeout,
        TimeProvider? timeProvider = null)
    {
        _process = process ?? throw new ArgumentNullException(nameof(process));
        _parser = parser ?? throw new ArgumentNullException(nameof(parser));
        ArgumentNullException.ThrowIfNull(launchInfo);
        _timeProvider = timeProvider ?? TimeProvider.System;
        _shutdownTimeout = shutdownTimeout;
        _channel = Channel.CreateUnbounded<HostLogEntry>(
            new UnboundedChannelOptions
            {
                SingleReader = true,
                SingleWriter = false,
            });

        WriteProcessStarted(launchInfo);
        _stdoutTask = ReadLinesAsync(_process.StandardOutput, HostProcessStreamNames.StandardOutput);
        _stderrTask = ReadLinesAsync(_process.StandardError, HostProcessStreamNames.StandardError);
        _exitTask = CompleteWhenProcessExitsAsync();
    }

    public async IAsyncEnumerable<HostLogEntry> ReadAsync([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        while (await _channel.Reader.WaitToReadAsync(cancellationToken))
        {
            while (_channel.Reader.TryRead(out HostLogEntry? entry))
            {
                yield return entry;
            }
        }
    }

    public async Task RequestShutdownAsync(CancellationToken cancellationToken)
    {
        if (Interlocked.Exchange(ref _shutdownRequested, 1) == 0 && !_process.HasExited)
        {
            try
            {
                await _process.StandardInput.DisposeAsync();
            }
            catch (InvalidOperationException) when (_process.HasExited)
            {
            }
        }

        Task completed = await Task.WhenAny(_exitTask, Task.Delay(_shutdownTimeout, cancellationToken));
        if (!ReferenceEquals(completed, _exitTask))
        {
            _process.KillTree();
        }

        await _exitTask.WaitAsync(cancellationToken);
    }

    public Task<int> WaitForExitAsync(CancellationToken cancellationToken)
        => _exitTask.WaitAsync(cancellationToken);

    /// <summary>
    /// Ensures the host process is shut down (gracefully if it cooperates,
    /// killed otherwise) before returning. Idempotent and best-effort, so
    /// it's safe to call from <c>await using</c> on every code path,
    /// including ones that already drove the pipeline to completion.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        try
        {
            await RequestShutdownAsync(CancellationToken.None);
        }
        catch
        {
            // RequestShutdownAsync is best-effort during disposal; the
            // process is killed below regardless. Swallowing here keeps the
            // outer failure (which is why we're disposing) primary.
            try
            {
                _process.KillTree();
            }
            catch
            {
                // Process may already be gone; nothing more we can do.
            }

            await _process.DisposeAsync();
        }
    }

    private void WriteProcessStarted(HostProcessLaunchInfo launchInfo)
    {
        var attributes = new Dictionary<string, object?>
        {
            [HostLogAttributeKeys.Stream] = HostProcessStreamNames.Cli,
            [HostLogAttributeKeys.HostVersion] = launchInfo.HostVersion,
            [HostLogAttributeKeys.HostListenUri] = launchInfo.LocalBaseUri.ToString(),
        };

        _channel.Writer.TryWrite(new HostLogEntry(
            _timeProvider.GetUtcNow(),
            "Host.Process",
            LogLevel.Information,
            default,
            $"Host process started. Listening on {launchInfo.LocalBaseUri}",
            Exception: null,
            attributes));
    }

    private async Task ReadLinesAsync(TextReader reader, string streamName)
    {
        string? line;
        while ((line = await reader.ReadLineAsync()) is not null)
        {
            HostLogEntry entry = _parser.ParseLine(streamName, line, _timeProvider.GetUtcNow());
            await _channel.Writer.WriteAsync(entry);
        }
    }

    private async Task<int> CompleteWhenProcessExitsAsync()
    {
        try
        {
            await _process.WaitForExitAsync(CancellationToken.None);
            await Task.WhenAll(_stdoutTask, _stderrTask);
            int exitCode = _process.ExitCode;
            _channel.Writer.TryComplete();
            return exitCode;
        }
        catch (Exception ex)
        {
            _channel.Writer.TryComplete(ex);
            throw;
        }
        finally
        {
            await _process.DisposeAsync();
        }
    }
}
