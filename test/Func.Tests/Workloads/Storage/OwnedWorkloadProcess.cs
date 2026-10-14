// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading.Channels;

namespace Azure.Functions.Cli.Tests.Workloads.Storage;

internal sealed class OwnedWorkloadProcess : IAsyncDisposable
{
    private readonly Process _process;
    private readonly Channel<string> _signals = Channel.CreateUnbounded<string>();
    private readonly ConcurrentQueue<string> _lines = new();
    private readonly Task _output;
    private readonly Task<string> _errors;

    private OwnedWorkloadProcess(Process process)
    {
        _process = process;
        _output = CaptureAsync();
        _errors = process.StandardError.ReadToEndAsync();
    }

    public IReadOnlyList<string> Lines => [.. _lines];

    public static OwnedWorkloadProcess Start(params string[] args)
    {
        string directory = AppContext.BaseDirectory;
        ProcessStartInfo start = new("dotnet")
        {
            RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
            UseShellExecute = false, CreateNoWindow = true,
        };
        foreach (string argument in new[]
        {
            "exec", "--runtimeconfig", Path.Combine(directory, "Azure.Functions.Cli.Tests.runtimeconfig.json"),
            "--depsfile", Path.Combine(directory, "Azure.Functions.Cli.Tests.deps.json"),
            Path.Combine(directory, "Azure.Functions.Cli.Workloads.Tests.OperationProcess.dll"),
        }.Concat(args))
        {
            start.ArgumentList.Add(argument);
        }

        return new(Process.Start(start) ?? throw new InvalidOperationException("The owned workload helper did not start."));
    }

    public async Task WaitForAsync(string expected)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        try
        {
            await foreach (string line in _signals.Reader.ReadAllAsync(timeout.Token))
            {
                if (line == expected)
                {
                    return;
                }
            }
        }
        catch (OperationCanceledException)
        {
            throw new InvalidOperationException($"No '{expected}' signal. Output: {string.Join("; ", Lines)}");
        }

        throw new InvalidOperationException($"Exited before '{expected}': {await _errors}");
    }

    public async Task SignalAsync()
    {
        await _process.StandardInput.WriteLineAsync("continue");
        await _process.StandardInput.FlushAsync();
    }

    public async Task WaitForExitAsync()
    {
        await _process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(30));
        await _output;
        _process.ExitCode.Should().Be(0, await _errors);
    }

    public async Task TerminateAsync()
    {
        if (!_process.HasExited)
        {
            _process.Kill(entireProcessTree: true);
            await _process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(30));
            await _output;
        }
    }

    public async ValueTask DisposeAsync()
    {
        await TerminateAsync();
        _process.Dispose();
    }

    private async Task CaptureAsync()
    {
        while (await _process.StandardOutput.ReadLineAsync() is { } line)
        {
            _lines.Enqueue(line);
            await _signals.Writer.WriteAsync(line);
        }

        _signals.Writer.Complete();
    }
}
