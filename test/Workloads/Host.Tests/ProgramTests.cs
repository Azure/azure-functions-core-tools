// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Workloads.Host.Tests;

public sealed class ProgramTests
{
    [Fact]
    public async Task StartStandardInputClosedMonitorAsync_DoesNotBlockCallerWhenReadLineBlocks()
    {
        using CancellationTokenSource shutdownTokenSource = new();
        var reader = new BlockingTextReader();

        Task monitorTask = Program.StartStandardInputClosedMonitorAsync(reader, shutdownTokenSource);

        await reader.WaitForReadAsync();
        monitorTask.IsCompleted.Should().BeFalse();

        reader.Release();
        await monitorTask.WaitAsync(TimeSpan.FromSeconds(5));
        shutdownTokenSource.IsCancellationRequested.Should().BeTrue();
    }

    [Fact]
    public void ReadDeferredWorkerEnvironment_ExtractsPrefixedVariables_StrippingPrefix()
    {
        var environment = new Dictionary<string, string>
        {
            ["PATH"] = "/usr/bin",
            ["FUNCTIONS_CORETOOLS_DEFER_ENV__DOTNET_STARTUP_HOOKS"] = "Microsoft.Azure.Functions.Worker.Core",
            ["FUNCTIONS_CORETOOLS_DEFER_ENV__DOTNET_gcServer"] = "0",
        };

        var deferred = Program.ReadDeferredWorkerEnvironment(environment).ToDictionary(x => x.Name, x => x.Value);

        deferred.Should().HaveCount(2);
        deferred["DOTNET_STARTUP_HOOKS"].Should().Be("Microsoft.Azure.Functions.Worker.Core");
        deferred["DOTNET_gcServer"].Should().Be("0");
    }

    [Fact]
    public void ReadDeferredWorkerEnvironment_WithoutPrefixedVariables_IsEmpty()
    {
        var environment = new Dictionary<string, string>
        {
            ["PATH"] = "/usr/bin",
            ["DOTNET_STARTUP_HOOKS"] = "already.set",
        };

        Program.ReadDeferredWorkerEnvironment(environment).Should().BeEmpty();
    }

    [Fact]
    public void ReadDeferredWorkerEnvironment_IgnoresBarePrefixWithNoTargetName()
    {
        var environment = new Dictionary<string, string>
        {
            ["FUNCTIONS_CORETOOLS_DEFER_ENV__"] = "orphaned",
        };

        Program.ReadDeferredWorkerEnvironment(environment).Should().BeEmpty();
    }

    private sealed class BlockingTextReader : TextReader
    {
        private readonly TaskCompletionSource _readStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task WaitForReadAsync()
            => _readStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        public void Release()
            => _release.SetResult();

        public override Task<string?> ReadLineAsync()
        {
            _readStarted.SetResult();
            _release.Task.GetAwaiter().GetResult();
            return Task.FromResult<string?>(null);
        }
    }
}
