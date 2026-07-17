// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Workloads;
using Azure.Functions.Cli.Workloads.Invocation;

namespace Azure.Functions.Cli.Tests.Workloads.Invocation;

public sealed class WorkloadInvokerTests
{
    private readonly WorkloadInvoker _invoker = new();
    private readonly RuntimeWorkloadInfo _workload = TestWorkloads.CreateInfo("Pkg.Acme");

    [Fact]
    public async Task InvokeAsync_Success_RunsAndReturns()
    {
        var calls = 0;

        await _invoker.InvokeAsync(_workload, _ =>
        {
            calls++;
            return Task.CompletedTask;
        }, CancellationToken.None);

        calls.Should().Be(1);
    }

    [Fact]
    public async Task InvokeAsyncGeneric_Success_ReturnsValue()
    {
        int result = await _invoker.InvokeAsync(_workload, _ => Task.FromResult(42), CancellationToken.None);

        result.Should().Be(42);
    }

    [Fact]
    public async Task InvokeAsync_GracefulException_RethrownWithPrefix_PreservesUserError()
    {
        GracefulException original = new("boom", isUserError: true, verboseMessage: "v");

        GracefulException thrown = (await FluentActions.Awaiting(() =>
            _invoker.InvokeAsync(_workload, _ => throw original, CancellationToken.None)).Should().ThrowAsync<GracefulException>()).Which;

        // Must remain a plain GracefulException (not the protocol subclass).
        thrown.Should().BeOfType<GracefulException>();
        thrown.Message.Should().Be("[Pkg.Acme] boom");
        thrown.IsUserError.Should().BeTrue();
        thrown.VerboseMessage.Should().Be("v");
    }

    [Fact]
    public async Task InvokeAsync_ArbitraryException_WrappedAsProtocol()
    {
        InvalidOperationException original = new("oops");

        WorkloadProtocolException thrown = (await FluentActions.Awaiting(() =>
            _invoker.InvokeAsync(_workload, _ => throw original, CancellationToken.None)).Should().ThrowAsync<WorkloadProtocolException>()).Which;

        thrown.Message.Should().Contain("[Pkg.Acme] error: oops");
        thrown.Message.Should().Contain("Please file an issue against the workload.");
        thrown.OriginalException.Should().BeSameAs(original);
        thrown.Workload.Should().BeSameAs(_workload);
        thrown.IsUserError.Should().BeFalse();
    }

    [Fact]
    public async Task InvokeAsync_OperationCanceled_RethrownUnchanged()
    {
        using CancellationTokenSource cts = new();
        cts.Cancel();

        OperationCanceledException thrown = (await FluentActions.Awaiting(() =>
            _invoker.InvokeAsync(_workload, ct =>
            {
                ct.ThrowIfCancellationRequested();
                return Task.CompletedTask;
            }, cts.Token)).Should().ThrowAsync<OperationCanceledException>()).Which;

        thrown.Should().BeAssignableTo<OperationCanceledException>();
        // Cancellation is not a workload error: not wrapped, not graceful.
    }

    [Fact]
    public async Task InvokeAsyncGeneric_GracefulException_RethrownWithPrefix()
    {
        GracefulException original = new("nope");

        GracefulException thrown = (await FluentActions.Awaiting(() =>
            _invoker.InvokeAsync<int>(_workload, _ => throw original, CancellationToken.None)).Should().ThrowAsync<GracefulException>()).Which;

        thrown.Message.Should().Be("[Pkg.Acme] nope");
    }

    [Fact]
    public async Task InvokeAsyncGeneric_ArbitraryException_WrappedAsProtocol()
    {
        InvalidOperationException original = new("kaboom");

        WorkloadProtocolException thrown = (await FluentActions.Awaiting(() =>
            _invoker.InvokeAsync<int>(_workload, _ => throw original, CancellationToken.None)).Should().ThrowAsync<WorkloadProtocolException>()).Which;

        thrown.Message.Should().Contain("[Pkg.Acme] error: kaboom");
        thrown.OriginalException.Should().BeSameAs(original);
    }

    [Fact]
    public async Task InvokeAsync_NullWorkload_Throws()
    {
        await FluentActions.Awaiting(() =>
            _invoker.InvokeAsync(null!, _ => Task.CompletedTask, CancellationToken.None)).Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task InvokeAsync_NullOperation_Throws()
    {
        await FluentActions.Awaiting(() =>
            _invoker.InvokeAsync(_workload, null!, CancellationToken.None)).Should().ThrowAsync<ArgumentNullException>();
    }
}
