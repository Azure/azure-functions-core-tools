// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Threading.Channels;

namespace Azure.Functions.Cli.Commands.Start.Azurite.Launching;

/// <summary>
/// Wraps a launched <see cref="Process"/> and exposes line-buffered stdout and
/// stderr streams plus stop/dispose semantics. Used for both native Azurite
/// and the <c>docker run</c> parent process.
/// </summary>
internal sealed class AzuriteProcess : IAzuriteProcess
{
    private static readonly TimeSpan _dockerStopTimeout = TimeSpan.FromSeconds(10);

    private readonly Process _process;
    private readonly AzuriteLaunchMode _mode;
    private readonly string? _containerName;
    private readonly string _dockerCommand;
    private readonly Channel<string> _stdout;
    private readonly Channel<string> _stderr;
    private readonly object _stopLock = new();
    private Task? _stopTask;
    private int _disposed;

    public AzuriteProcess(
        Process process,
        AzuriteLaunchMode mode,
        string? containerName = null,
        string dockerCommand = "docker")
    {
        _process = process ?? throw new ArgumentNullException(nameof(process));
        _mode = mode;
        _containerName = containerName;
        _dockerCommand = dockerCommand;

        _stdout = Channel.CreateUnbounded<string>(new UnboundedChannelOptions
        {
            SingleReader = false,
            SingleWriter = true,
        });
        _stderr = Channel.CreateUnbounded<string>(new UnboundedChannelOptions
        {
            SingleReader = false,
            SingleWriter = true,
        });

        _process.OutputDataReceived += OnStdoutLine;
        _process.ErrorDataReceived += OnStderrLine;
        _process.Exited += OnExited;
        _process.EnableRaisingEvents = true;

        _process.BeginOutputReadLine();
        _process.BeginErrorReadLine();

        // If the process already exited between Start and event hookup, close
        // the channels so async readers don't hang.
        if (_process.HasExited)
        {
            CompleteChannels();
        }
    }

    public int ProcessId => _process.Id;

    public async Task<int> WaitForExitAsync(CancellationToken cancellationToken)
    {
        await _process.WaitForExitAsync(cancellationToken);
        CompleteChannels();
        return _process.ExitCode;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        lock (_stopLock)
        {
            _stopTask ??= StopCoreAsync(cancellationToken);
            return _stopTask;
        }
    }

    public async IAsyncEnumerable<string> ReadStdoutLinesAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await foreach (string line in _stdout.Reader.ReadAllAsync(cancellationToken))
        {
            yield return line;
        }
    }

    public async IAsyncEnumerable<string> ReadStderrLinesAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await foreach (string line in _stderr.Reader.ReadAllAsync(cancellationToken))
        {
            yield return line;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        try
        {
            await StopAsync(CancellationToken.None);
        }
        catch
        {
            // Best-effort cleanup: stop has already done what it can. Swallowing
            // here keeps disposal from masking earlier failures.
        }

        _process.OutputDataReceived -= OnStdoutLine;
        _process.ErrorDataReceived -= OnStderrLine;
        _process.Exited -= OnExited;
        CompleteChannels();
        _process.Dispose();
    }

    private async Task StopCoreAsync(CancellationToken cancellationToken)
    {
        if (_process.HasExited)
        {
            CompleteChannels();
            return;
        }

        if (_mode == AzuriteLaunchMode.Docker && !string.IsNullOrEmpty(_containerName))
        {
            await TryGracefulDockerStopAsync(cancellationToken);
        }

        if (!_process.HasExited)
        {
            // Native v1 behaviour: force-kill the process tree. SIGTERM is not
            // exposed by .NET's Process API without P/Invoke; we may add a
            // graceful Unix shutdown path later.
            TryKillTree(_process);
        }

        try
        {
            await _process.WaitForExitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            // Caller wanted to bail out; the process may still be exiting in
            // the background. Disposal will continue to clean up handles.
        }

        CompleteChannels();
    }

    private async Task TryGracefulDockerStopAsync(CancellationToken cancellationToken)
    {
        ProcessStartInfo psi = new()
        {
            FileName = _dockerCommand,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        psi.ArgumentList.Add("stop");
        psi.ArgumentList.Add("--time");
        psi.ArgumentList.Add(((int)_dockerStopTimeout.TotalSeconds).ToString(CultureInfo.InvariantCulture));
        psi.ArgumentList.Add(_containerName!);

        Process? stop = null;
        try
        {
            stop = Process.Start(psi);
        }
        catch
        {
            // If we cannot even start `docker stop`, the caller will fall back
            // to killing the parent docker process.
            return;
        }

        if (stop is null)
        {
            return;
        }

        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(_dockerStopTimeout);
            try
            {
                await stop.WaitForExitAsync(timeout.Token);
            }
            catch (OperationCanceledException)
            {
                // `docker stop` is taking too long; fall through to force kill.
            }
        }
        finally
        {
            TryKillTree(stop);
            stop.Dispose();
        }
    }

    private static void TryKillTree(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch
        {
            // Best-effort: the process may have exited between the check and
            // the kill, or we may not have permission. Either way there is
            // nothing more we can do here.
        }
    }

    private void OnStdoutLine(object sender, DataReceivedEventArgs e)
    {
        if (e.Data is not null)
        {
            _stdout.Writer.TryWrite(e.Data);
        }
    }

    private void OnStderrLine(object sender, DataReceivedEventArgs e)
    {
        if (e.Data is not null)
        {
            _stderr.Writer.TryWrite(e.Data);
        }
    }

    private void OnExited(object? sender, EventArgs e)
    {
        CompleteChannels();
    }

    private void CompleteChannels()
    {
        _stdout.Writer.TryComplete();
        _stderr.Writer.TryComplete();
    }
}
