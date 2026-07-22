// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Diagnostics;
using Azure.Functions.Cli.Commands.Start.Host;
using Azure.Functions.Cli.Hosting.Events;
using Microsoft.Extensions.Logging;

namespace Azure.Functions.Cli.Tests.Commands;

public class HostProcessEventStreamTests
{
    [Fact]
    public async Task ReadAsync_BuffersProcessStartedAndOutputLinesUntilExit()
    {
        var process = new CompletedHostProcess(stdout: "stdout line", stderr: "stderr line", exitCode: 23);
        var launchInfo = new HostProcessLaunchInfo(
            new ProcessStartInfo("host"),
            Port: 7071,
            ListenUri: new Uri("http://0.0.0.0:7071"),
            LocalBaseUri: new Uri("http://localhost:7071"),
            HostVersion: "4.1000.0");
        var stream = new HostProcessEventStream(
            process,
            new LineHostProcessOutputParser(),
            launchInfo,
            TimeSpan.FromMilliseconds(1));

        HostLogEntry[] entries = await ReadAllAsync(stream);
        int exitCode = await stream.WaitForExitAsync(CancellationToken.None);

        exitCode.Should().Be(23);
        process.Disposed.Should().BeTrue();
        entries.Length.Should().Be(3);
        entries[0].Category.Should().Be("Host.Process");
        entries[0].Level.Should().Be(LogLevel.Information);
        entries[0].Message.Should().Contain("http://localhost:7071");
        entries[0].GetAttribute<string>(HostLogAttributeKeys.Stream).Should().Be(HostProcessStreamNames.Cli);
        entries.Should().Contain(entry =>
            entry.Message == "stdout line"
            && entry.Level == LogLevel.Information
            && entry.GetAttribute<string>(HostLogAttributeKeys.Stream) == HostProcessStreamNames.StandardOutput);
        entries.Should().Contain(entry =>
            entry.Message == "stderr line"
            && entry.Level == LogLevel.Error
            && entry.GetAttribute<string>(HostLogAttributeKeys.Stream) == HostProcessStreamNames.StandardError);
    }

    [Fact]
    public async Task RequestShutdownAsync_KillsProcessTreeAfterTimeout()
    {
        var process = new BlockingHostProcess();
        var stream = new HostProcessEventStream(
            process,
            new LineHostProcessOutputParser(),
            CreateLaunchInfo(),
            TimeSpan.Zero);

        await stream.RequestShutdownAsync(CancellationToken.None);

        process.StandardInputDisposed.Should().BeTrue();
        process.KillTreeCalled.Should().BeTrue();
        process.Disposed.Should().BeTrue();
    }

    [Fact]
    public async Task DisposeAsync_KillsBlockingProcessTree()
    {
        var process = new BlockingHostProcess();
        var stream = new HostProcessEventStream(
            process,
            new LineHostProcessOutputParser(),
            CreateLaunchInfo(),
            TimeSpan.Zero);

        await stream.DisposeAsync();

        process.StandardInputDisposed.Should().BeTrue();
        process.KillTreeCalled.Should().BeTrue();
        process.Disposed.Should().BeTrue();
    }

    [Fact]
    public async Task DisposeAsync_IsIdempotent()
    {
        var process = new BlockingHostProcess();
        var stream = new HostProcessEventStream(
            process,
            new LineHostProcessOutputParser(),
            CreateLaunchInfo(),
            TimeSpan.Zero);

        await stream.DisposeAsync();
        await stream.DisposeAsync();

        process.KillTreeCallCount.Should().Be(1);
    }

    private static async Task<HostLogEntry[]> ReadAllAsync(IHostEventStream stream)
    {
        List<HostLogEntry> entries = [];
        await foreach (HostLogEntry entry in stream.ReadAsync(CancellationToken.None))
        {
            entries.Add(entry);
        }

        return [.. entries];
    }

    private static HostProcessLaunchInfo CreateLaunchInfo()
        => new(
            new ProcessStartInfo("host"),
            Port: 7071,
            ListenUri: new Uri("http://0.0.0.0:7071"),
            LocalBaseUri: new Uri("http://localhost:7071"),
            HostVersion: "4.1000.0");

    private sealed class CompletedHostProcess(string stdout, string stderr, int exitCode) : IHostProcess
    {
        public TextReader StandardOutput { get; } = new StringReader(stdout);

        public TextReader StandardError { get; } = new StringReader(stderr);

        public TextWriter StandardInput { get; } = new StringWriter();

        public bool HasExited => true;

        public int ExitCode { get; } = exitCode;

        public bool Disposed { get; private set; }

        public void Start()
        {
        }

        public Task WaitForExitAsync(CancellationToken cancellationToken)
            => Task.CompletedTask;

        public void KillTree()
        {
        }

        public ValueTask DisposeAsync()
        {
            Disposed = true;
            StandardOutput.Dispose();
            StandardError.Dispose();
            StandardInput.Dispose();
            return ValueTask.CompletedTask;
        }
    }

    private sealed class BlockingHostProcess : IHostProcess
    {
        private readonly TaskCompletionSource _exit = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TrackingTextWriter _standardInput = new();

        public TextReader StandardOutput { get; } = new StringReader(string.Empty);

        public TextReader StandardError { get; } = new StringReader(string.Empty);

        public TextWriter StandardInput => _standardInput;

        public bool HasExited => _exit.Task.IsCompleted;

        public int ExitCode => 0;

        public bool StandardInputDisposed => _standardInput.Disposed;

        public bool KillTreeCalled { get; private set; }

        public int KillTreeCallCount { get; private set; }

        public bool Disposed { get; private set; }

        public void Start()
        {
        }

        public Task WaitForExitAsync(CancellationToken cancellationToken)
            => _exit.Task.WaitAsync(cancellationToken);

        public void KillTree()
        {
            KillTreeCalled = true;
            KillTreeCallCount++;
            _exit.TrySetResult();
        }

        public ValueTask DisposeAsync()
        {
            Disposed = true;
            StandardOutput.Dispose();
            StandardError.Dispose();
            StandardInput.Dispose();
            return ValueTask.CompletedTask;
        }
    }

    private sealed class TrackingTextWriter : StringWriter
    {
        public bool Disposed { get; private set; }

        protected override void Dispose(bool disposing)
        {
            Disposed = true;
            base.Dispose(disposing);
        }

        public override ValueTask DisposeAsync()
        {
            Disposed = true;
            return base.DisposeAsync();
        }
    }
}
