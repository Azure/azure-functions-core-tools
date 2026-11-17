// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Buffers.Binary;
using System.Runtime.InteropServices;
using Azure.Functions.Cli.Console.Terminal;
using NSubstitute;

namespace Azure.Functions.Cli.Tests.Console.Terminal;

public class UnixTerminalTests
{
    [Theory]
    [InlineData(false, 60, 17, 0, 6, 5)]
    [InlineData(true, 72, 32, 8, 16, 17)]
    public void ModeLayout_PreservesSignalsSpeedsAndUnrelatedControlCharacters(
        bool mac, int size, int controlOffset, int intr, int min, int time)
    {
        byte[] original = [.. Enumerable.Range(0, size).Select(value => (byte)value)];
        if (mac)
        {
            BinaryPrimitives.WriteUInt64LittleEndian(original.AsSpan(24), 0xffffffffffffffff);
        }
        else
        {
            BinaryPrimitives.WriteUInt32LittleEndian(original.AsSpan(12), 0xffffffff);
        }

        byte[] copy = (byte[])original.Clone();
        byte[] mode = UnixTerminalMode.CreateInputMode(original, mac);
        original.Should().Equal(copy);
        mode.Length.Should().Be(size);
        mode[controlOffset + intr].Should().Be(mac ? (byte)255 : (byte)0);
        mode[controlOffset + min].Should().Be(0);
        mode[controlOffset + time].Should().Be(0);

        int controlCount = mac ? 20 : 32;
        for (int i = 0; i < controlCount; i++)
        {
            if (i != intr && i != min && i != time)
            {
                mode[controlOffset + i].Should().Be(original[controlOffset + i]);
            }
        }

        mode.AsSpan(mac ? 56 : 52).ToArray().Should().Equal(original.AsSpan(mac ? 56 : 52).ToArray());
        ulong local = mac
            ? BinaryPrimitives.ReadUInt64LittleEndian(mode.AsSpan(24))
            : BinaryPrimitives.ReadUInt32LittleEndian(mode.AsSpan(12));
        (local & (mac ? 0x80UL : 1UL)).Should().NotBe(0, "ISIG and job-control signals must remain enabled");
        (local & (mac ? 0x118UL : 0x4aUL)).Should().Be(0, "canonical input and all echo must be disabled");
    }

    [Fact]
    public void PollAndWindowStructures_MatchBothSupportedUnixAbis()
    {
        Marshal.SizeOf<UnixPollDescriptor>().Should().Be(8);
        Marshal.OffsetOf<UnixPollDescriptor>(nameof(UnixPollDescriptor.ReturnedEvents)).ToInt32().Should().Be(6);
        Marshal.SizeOf<UnixWindowSize>().Should().Be(8);
        Marshal.OffsetOf<UnixWindowSize>(nameof(UnixWindowSize.Columns)).ToInt32().Should().Be(2);
        Marshal.SizeOf<MacSignalAction>().Should().Be(16);
        Marshal.OffsetOf<MacSignalAction>(nameof(MacSignalAction.Mask)).ToInt32().Should().Be(8);
        Marshal.OffsetOf<MacSignalAction>(nameof(MacSignalAction.Flags)).ToInt32().Should().Be(12);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Restore_UsesOriginalBytesEvenAfterPartialActivation(bool mac)
    {
        IUnixTerminalNative native = Substitute.For<IUnixTerminalNative>();
        byte[] original = new byte[mac ? UnixTerminalMode.DarwinSize : UnixTerminalMode.LinuxSize];
        native.GetAttributes().Returns(original);
        native.GetSize().Returns(new TerminalSize(80, 24));
        native.When(instance => instance.SetAttributes(Arg.Is<byte[]>(bytes => !ReferenceEquals(bytes, original))))
            .Do(_ => throw new IOException("injected mode failure"));
        UnixTerminalDriver driver = new(native, mac);

        Action activate = driver.Activate;
        activate.Should().Throw<IOException>();
        driver.Restore();
        driver.Dispose();
        native.Received(1).SetAttributes(original);
    }

    [Fact]
    public void Read_ReportsResizeAndBoundsPoll()
    {
        IUnixTerminalNative native = Native();
        UnixTerminalDriver driver = new(native, false);
        driver.Activate();
        native.GetSize().Returns(new TerminalSize(100, 30));

        driver.Read(CancellationToken.None).Should().Equal(new TerminalInputEvent(TerminalInputKind.Resize));
        native.Received(1).Poll(50);
        driver.Read(CancellationToken.None).Should().BeEmpty();
        driver.Dispose();
    }

    [Fact]
    public void Read_HangupIsNotAnEmptyReadSpin()
    {
        IUnixTerminalNative native = Native();
        native.Poll(50).Returns((short)16);
        native.Read(Arg.Any<byte[]>()).Returns(0);
        UnixTerminalDriver driver = new(native, false);
        driver.Activate();
        Action read = () => driver.Read(CancellationToken.None);
        read.Should().Throw<EndOfStreamException>();
        driver.Dispose();
    }

    [Fact]
    public void Read_CancelledInputDoesNotPoll()
    {
        IUnixTerminalNative native = Native();
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();
        UnixTerminalDriver driver = new(native, false);
        Action read = () => driver.Read(cancellation.Token);
        read.Should().Throw<OperationCanceledException>();
        native.DidNotReceive().Poll(Arg.Any<int>());
    }

    [Fact]
    public void SuspendResume_RestoresOriginalThenReappliesOnlyOwnedModes()
    {
        IUnixTerminalNative native = Native();
        byte[] original = native.GetAttributes();
        UnixTerminalDriver driver = new(native, false);
        driver.Activate();
        driver.Suspend();
        Received.InOrder(() =>
        {
            native.SetAttributes(original);
            native.SuspendProcess();
        });
        native.Received(1).SuspendProcess();
        driver.Read(CancellationToken.None).Should().BeEmpty();
        native.DidNotReceive().Poll(Arg.Any<int>());
        native.Received(1).SetAttributes(original);
        driver.Resume();
        native.Received(2).SetAttributes(Arg.Is<byte[]>(bytes => !ReferenceEquals(bytes, original)));
        driver.Dispose();
        native.Received(2).SetAttributes(original);
    }

    [Fact]
    public void InvalidSnapshotLength_IsRejectedBeforeNativeWrite()
    {
        Action create = () => UnixTerminalMode.CreateInputMode(new byte[59], false);
        create.Should().Throw<ArgumentException>();
    }

    private static IUnixTerminalNative Native()
    {
        IUnixTerminalNative native = Substitute.For<IUnixTerminalNative>();
        native.GetAttributes().Returns(new byte[UnixTerminalMode.LinuxSize]);
        native.GetSize().Returns(new TerminalSize(80, 24));
        return native;
    }
}
