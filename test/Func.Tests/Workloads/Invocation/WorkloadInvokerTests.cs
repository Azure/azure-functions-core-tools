// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Workloads;
using Azure.Functions.Cli.Workloads.Invocation;
using Xunit;

namespace Azure.Functions.Cli.Tests.Workloads.Invocation;

public sealed class WorkloadInvokerTests
{
    private readonly WorkloadInvoker _invoker = new();
    private readonly WorkloadInfo _workload = TestWorkloads.CreateInfo("Pkg.Acme");

    [Fact]
    public async Task InvokeAsync_Success_RunsAndReturns()
    {
        var calls = 0;

        await _invoker.InvokeAsync(_workload, _ =>
        {
            calls++;
            return Task.CompletedTask;
        }, CancellationToken.None);

        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task InvokeAsyncGeneric_Success_ReturnsValue()
    {
        int result = await _invoker.InvokeAsync(_workload, _ => Task.FromResult(42), CancellationToken.None);

        Assert.Equal(42, result);
    }

    [Fact]
    public async Task InvokeAsync_GracefulException_RethrownWithPrefix_PreservesUserError()
    {
        GracefulException original = new("boom", isUserError: true, verboseMessage: "v");

        GracefulException thrown = await Assert.ThrowsAsync<GracefulException>(() =>
            _invoker.InvokeAsync(_workload, _ => throw original, CancellationToken.None));

        // Must remain a plain GracefulException (not the protocol subclass).
        Assert.IsType<GracefulException>(thrown);
        Assert.Equal("[Pkg.Acme] boom", thrown.Message);
        Assert.True(thrown.IsUserError);
        Assert.Equal("v", thrown.VerboseMessage);
    }

    [Fact]
    public async Task InvokeAsync_ArbitraryException_WrappedAsProtocol()
    {
        InvalidOperationException original = new("oops");

        WorkloadProtocolException thrown = await Assert.ThrowsAsync<WorkloadProtocolException>(() =>
            _invoker.InvokeAsync(_workload, _ => throw original, CancellationToken.None));

        Assert.Contains("[Pkg.Acme] error: oops", thrown.Message);
        Assert.Contains("Please file an issue against the workload.", thrown.Message);
        Assert.Same(original, thrown.OriginalException);
        Assert.Same(_workload, thrown.Workload);
        Assert.False(thrown.IsUserError);
    }

    [Fact]
    public async Task InvokeAsync_OperationCanceled_RethrownUnchanged()
    {
        using CancellationTokenSource cts = new();
        cts.Cancel();

        OperationCanceledException thrown = await Assert.ThrowsAsync<OperationCanceledException>(() =>
            _invoker.InvokeAsync(_workload, ct =>
            {
                ct.ThrowIfCancellationRequested();
                return Task.CompletedTask;
            }, cts.Token));

        Assert.IsAssignableFrom<OperationCanceledException>(thrown);
        // Cancellation is not a workload error: not wrapped, not graceful.
    }

    [Fact]
    public async Task InvokeAsyncGeneric_GracefulException_RethrownWithPrefix()
    {
        GracefulException original = new("nope");

        GracefulException thrown = await Assert.ThrowsAsync<GracefulException>(() =>
            _invoker.InvokeAsync<int>(_workload, _ => throw original, CancellationToken.None));

        Assert.Equal("[Pkg.Acme] nope", thrown.Message);
    }

    [Fact]
    public async Task InvokeAsyncGeneric_ArbitraryException_WrappedAsProtocol()
    {
        InvalidOperationException original = new("kaboom");

        WorkloadProtocolException thrown = await Assert.ThrowsAsync<WorkloadProtocolException>(() =>
            _invoker.InvokeAsync<int>(_workload, _ => throw original, CancellationToken.None));

        Assert.Contains("[Pkg.Acme] error: kaboom", thrown.Message);
        Assert.Same(original, thrown.OriginalException);
    }

    [Fact]
    public async Task InvokeAsync_NullWorkload_Throws()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            _invoker.InvokeAsync(null!, _ => Task.CompletedTask, CancellationToken.None));
    }

    [Fact]
    public async Task InvokeAsync_NullOperation_Throws()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            _invoker.InvokeAsync(_workload, null!, CancellationToken.None));
    }
}
