// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.ComponentModel;
using Azure.Functions.Cli.Console.Clipboard;
using NSubstitute;

namespace Azure.Functions.Cli.Tests.Console.Clipboard;

public class WindowsClipboardTests
{
    [Fact]
    public async Task SuccessfulWrite_TransfersMemoryOwnershipAndClosesWindow()
    {
        IWindowsClipboardNative native = Native();
        WindowsClipboard clipboard = new(native);

        var result = await clipboard.CopyAsync("界😀", CancellationToken.None);

        result.Status.Should().Be(ClipboardCopyStatus.Confirmed);
        native.Received(1).Allocate(8);
        native.Received(1).Write(new nint(43), "界😀");
        native.Received(1).TryOpen(new nint(42));
        native.Received(1).Empty();
        native.Received(1).SetUnicodeData(new nint(43));
        native.Received(1).Close();
        native.Received(1).DestroyOwnerWindow(new nint(42));
        native.DidNotReceiveWithAnyArgs().Free(default);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task FailedWrite_ReleasesUntransferredMemoryAndWindow(bool emptyFails)
    {
        IWindowsClipboardNative native = Native();
        if (emptyFails)
        {
            native.When(instance => instance.Empty()).Do(_ => throw new Win32Exception(5));
        }
        else
        {
            native.When(instance => instance.SetUnicodeData(Arg.Any<nint>())).Do(_ => throw new Win32Exception(5));
        }

        WindowsClipboard clipboard = new(native);
        var result = await clipboard.CopyAsync("data", CancellationToken.None);

        result.Status.Should().Be(ClipboardCopyStatus.Failed);
        native.Received(1).Close();
        native.Received(1).Free(new nint(43));
        native.Received(1).DestroyOwnerWindow(new nint(42));
    }

    [Fact]
    public async Task BusyClipboard_HasBoundedRetriesWithoutEmptying()
    {
        IWindowsClipboardNative native = Native();
        native.TryOpen(Arg.Any<nint>()).Returns(false);
        WindowsClipboard clipboard = new(native);

        var result = await clipboard.CopyAsync("data", CancellationToken.None);

        result.Status.Should().Be(ClipboardCopyStatus.Failed);
        result.Message.Should().Contain("busy");
        native.Received(10).TryOpen(new nint(42));
        native.Received(10).WaitBeforeRetry(Arg.Any<CancellationToken>());
        native.DidNotReceive().Empty();
        native.DidNotReceive().Close();
        native.Received(1).Free(new nint(43));
        native.Received(1).DestroyOwnerWindow(new nint(42));
    }

    [Fact]
    public async Task ContentionCanRecoverWithoutChangingOwnerWindow()
    {
        IWindowsClipboardNative native = Native();
        native.TryOpen(new nint(42)).Returns(false, false, true);
        WindowsClipboard clipboard = new(native);
        var result = await clipboard.CopyAsync("data", CancellationToken.None);
        result.Status.Should().Be(ClipboardCopyStatus.Confirmed);
        native.Received(3).TryOpen(new nint(42));
        native.Received(2).WaitBeforeRetry(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CancellationDuringContention_CleansUpWithoutChangingClipboard()
    {
        using CancellationTokenSource cancellation = new();
        IWindowsClipboardNative native = Native();
        native.TryOpen(Arg.Any<nint>()).Returns(false);
        native.When(instance => instance.WaitBeforeRetry(Arg.Any<CancellationToken>())).Do(_ => cancellation.Cancel());
        WindowsClipboard clipboard = new(native);

        Func<Task> copy = () => clipboard.CopyAsync("data", cancellation.Token);

        await copy.Should().ThrowAsync<OperationCanceledException>();
        native.DidNotReceive().Empty();
        native.Received(1).Free(new nint(43));
        native.Received(1).DestroyOwnerWindow(new nint(42));
    }

    [Fact]
    public async Task AllocationFailure_DoesNotOpenOrEmptyClipboard()
    {
        IWindowsClipboardNative native = Native();
        native.Allocate(Arg.Any<int>()).Returns<nint>(_ => throw new Win32Exception(8));
        WindowsClipboard clipboard = new(native);
        var result = await clipboard.CopyAsync("data", CancellationToken.None);
        result.Status.Should().Be(ClipboardCopyStatus.Failed);
        native.DidNotReceiveWithAnyArgs().TryOpen(default);
        native.DidNotReceive().Empty();
        native.Received(1).DestroyOwnerWindow(new nint(42));
    }

    private static IWindowsClipboardNative Native()
    {
        IWindowsClipboardNative native = Substitute.For<IWindowsClipboardNative>();
        native.CreateOwnerWindow().Returns(new nint(42));
        native.Allocate(Arg.Any<int>()).Returns(new nint(43));
        native.TryOpen(new nint(42)).Returns(true);
        return native;
    }
}
