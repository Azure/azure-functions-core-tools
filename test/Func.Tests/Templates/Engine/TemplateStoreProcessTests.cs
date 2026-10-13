// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Collections.Concurrent;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Threading.Channels;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Templates.Engine;
using Microsoft.Extensions.Logging.Abstractions;

namespace Azure.Functions.Cli.Tests.Templates.Engine;

public sealed class TemplateStoreProcessTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "func-protected-store-process-tests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [Fact]
    public async Task CrossProcess_ReadersOverlapAndQueuedWriterPrecedesReaderChurn()
    {
        await TemplateFixtures.InstallAsync(TemplateFixtures.WriteBasicPackage(_root), Path.Combine(_root, "templates"));
        await using var first = OwnedProcess.Start("store-read", _root);
        await first.WaitForAsync("READY:1");
        await using var second = OwnedProcess.Start("store-read", _root);
        await second.WaitForAsync("READY:1");
        await using var writer = OwnedProcess.Start("store-write", _root);
        await writer.WaitForAsync("WAITING:ActiveReaders");
        await using var churn = OwnedProcess.Start("churn", _root);
        await churn.WaitForAsync("WAITING:LifecycleOperation");

        await first.ReleaseAsync();
        writer.Lines.Should().NotContain("READY");
        churn.Lines.Should().NotContain("READ");
        await second.ReleaseAsync();
        await writer.WaitForAsync("READY");
        churn.Lines.Should().NotContain("READ");
        await writer.ReleaseAsync();
        await churn.WaitForExitAsync();

        churn.Lines.Count(line => line == "READ").Should().Be(20);
    }

    [Fact]
    public async Task CrossProcess_CanceledQueuedWriterReleasesAdmissionWhileReaderRemainsAlive()
    {
        await using var reader = OwnedProcess.Start("read", _root);
        await reader.WaitForAsync("READY");
        await using var writer = OwnedProcess.Start("cancelable-write", _root);
        await writer.WaitForAsync("WAITING:ActiveReaders");

        await writer.CancelAsync();
        await writer.WaitForAsync("CANCELED");
        await writer.WaitForExitAsync();
        await using var later = OwnedProcess.Start("read", _root);
        await later.WaitForAsync("READY");

        reader.HasExited.Should().BeFalse();
        Directory.GetFiles(Path.Combine(_root, ".template-store"), "*.writer").Should().BeEmpty();
    }

    [Fact]
    public async Task CrossProcess_TerminatedReaderReleasesOsOwnershipWithoutDisposal()
    {
        await using var reader = OwnedProcess.Start("read", _root);
        await reader.WaitForAsync("READY");
        await using var writer = OwnedProcess.Start("write", _root);
        await writer.WaitForAsync("WAITING:ActiveReaders");

        await reader.TerminateAsync();
        await writer.WaitForAsync("READY");

        reader.HasExited.Should().BeTrue();
    }

    [Fact]
    public async Task CrossProcess_RepeatedWriterCancellationDoesNotFailConcurrentReaderAdmission()
    {
        await using var incumbent = OwnedProcess.Start("read", _root);
        await incumbent.WaitForAsync("READY");
        await using var cancellations = OwnedProcess.Start("cancel-churn", _root);
        await cancellations.WaitForAsync("READY");
        await using var readers = OwnedProcess.Start("churn", _root);
        await readers.WaitForAsync("READ");

        await cancellations.ReleaseAsync();
        await readers.WaitForExitAsync();

        cancellations.Lines.Count(line => line == "CANCELED").Should().Be(128);
        readers.Lines.Count(line => line == "READ").Should().Be(20);
        Directory.GetFiles(Path.Combine(_root, ".template-store"), "*.writer").Should().BeEmpty();
        Directory.GetFiles(Path.Combine(_root, ".template-store"), "*.intent").Should().BeEmpty();
    }

    [Fact]
    public async Task CrossProcess_LeftoverMarkersAreRemovedOnlyAfterExclusiveInspection()
    {
        var control = Path.Combine(_root, ".template-store");
        Directory.CreateDirectory(control);
        File.WriteAllText(Path.Combine(control, $"{Guid.NewGuid():N}.intent"), string.Empty);
        File.WriteAllText(Path.Combine(control, $"00000000000000000001-{Guid.NewGuid():N}.writer"), string.Empty);

        await using var reader = OwnedProcess.Start("read", _root);
        await reader.WaitForAsync("READY");

        Directory.GetFiles(control, "*.intent").Should().BeEmpty();
        Directory.GetFiles(control, "*.writer").Should().BeEmpty();
    }

    [Fact]
    public async Task CrossProcess_TerminatedMutationIsRestoredBeforeAnotherProcessCanList()
    {
        var store = new TemplateStore(new(_root), new PhysicalFileSystem(), new FileTemplateStoreLock(), NullLoggerFactory.Instance);
        await TemplateFixtures.InstallAsync(TemplateFixtures.WriteBasicPackage(_root), store.Hive);
        using (var warm = new TemplateEngineSession(new(WorkingDirectory.FromExplicit(_root)), store.Hive))
        {
            await warm.PackageManager.GetTemplatesAsync(CancellationToken.None);
        }

        var before = Bytes(store.Hive);
        await using var writer = OwnedProcess.Start("mutate", _root);
        await writer.WaitForAsync("MUTATING");
        File.Exists(Path.Combine(store.Hive, "interrupted.txt")).Should().BeTrue();
        File.Exists(store.Journal).Should().BeTrue();

        await writer.TerminateAsync();
        await using var reader = OwnedProcess.Start("store-read", _root);
        await reader.WaitForAsync("READY:1");

        Bytes(store.Hive).Should().BeEquivalentTo(before);
        File.Exists(store.Journal).Should().BeFalse();
        Directory.GetFiles(store.ControlDirectory, "*.writer").Should().BeEmpty();
    }

    private static Dictionary<string, string> Bytes(string path)
        => Directory.GetFiles(path, "*", SearchOption.AllDirectories)
            .ToDictionary(file => Path.GetRelativePath(path, file), file => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(file))));

    private sealed class OwnedProcess : IAsyncDisposable
    {
        private readonly Process _process;
        private readonly Task _output;
        private readonly Task<string> _errors;
        private readonly Channel<string> _signals = Channel.CreateUnbounded<string>();
        private readonly ConcurrentQueue<string> _lines = new();

        private OwnedProcess(Process process)
        {
            _process = process;
            _output = CaptureAsync();
            _errors = process.StandardError.ReadToEndAsync();
        }

        public IReadOnlyList<string> Lines => [.. _lines];

        public bool HasExited => _process.HasExited;

        public static OwnedProcess Start(string mode, string home)
        {
            string directory = AppContext.BaseDirectory;
            ProcessStartInfo start = new("dotnet")
            {
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            foreach (string argument in new[]
            {
                "exec", "--runtimeconfig", Path.Combine(directory, "Azure.Functions.Cli.Tests.runtimeconfig.json"),
                "--depsfile", Path.Combine(directory, "Azure.Functions.Cli.Tests.deps.json"),
                Path.Combine(directory, "Azure.Functions.Cli.Templates.Tests.StoreProcess.dll"), mode, home,
            })
            {
                start.ArgumentList.Add(argument);
            }

            return new(Process.Start(start) ?? throw new InvalidOperationException("The test helper could not be started."));
        }

        public async Task WaitForAsync(string expected)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            try
            {
                await foreach (var line in _signals.Reader.ReadAllAsync(timeout.Token))
                {
                    if (line == expected)
                    {
                        return;
                    }
                }
            }
            catch (OperationCanceledException)
            {
                throw new InvalidOperationException($"The owned helper did not report '{expected}'. Output: {string.Join("; ", Lines)}");
            }

            throw new InvalidOperationException($"The owned helper exited before '{expected}'. Error: {await _errors}");
        }

        public async Task ReleaseAsync()
        {
            await _process.StandardInput.WriteLineAsync("release");
            await _process.StandardInput.FlushAsync();
            await WaitForExitAsync();
        }

        public async Task CancelAsync()
        {
            await _process.StandardInput.WriteLineAsync("cancel");
            await _process.StandardInput.FlushAsync();
        }

        public async Task WaitForExitAsync()
        {
            await _process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(20));
            await _output;
            _process.ExitCode.Should().Be(0, await _errors);
        }

        public async Task TerminateAsync()
        {
            if (!_process.HasExited)
            {
                _process.Kill(entireProcessTree: true);
                await _process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(20));
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
}
