// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Common;
using Microsoft.Win32.SafeHandles;
using System.Runtime.InteropServices;

namespace Azure.Functions.Cli.Tests.Common;

public sealed partial class PlatformFileIdentityReaderTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("func-file-identity-").FullName;

    [Fact]
    public void Read_ActualHeldHandleAndPath_IdentifyTheSameIndependentRegularFile()
    {
        string path = Path.Combine(_root, "app.py");
        File.WriteAllText(path, "unchanged app");
        PlatformFileIdentityReader reader = new();
        using SafeFileHandle handle = File.OpenHandle(path);

        FileIdentity byPath = reader.Read(path);
        FileIdentity byHandle = reader.Read(handle);

        byPath.Identifies(byHandle).Should().BeTrue();
        byHandle.IsRegular.Should().BeTrue();
        byHandle.LinkCount.Should().Be(1);
        File.ReadAllText(path).Should().Be("unchanged app");
    }

    [Fact]
    public void Read_DistinctFilesWithIdenticalContent_DoNotShareFilesystemIdentity()
    {
        string first = Path.Combine(_root, "first.py");
        string second = Path.Combine(_root, "second.py");
        File.WriteAllText(first, "same app bytes");
        File.WriteAllText(second, "same app bytes");
        PlatformFileIdentityReader reader = new();

        FileIdentity left = reader.Read(first);
        FileIdentity right = reader.Read(second);

        left.Identifies(right).Should().BeFalse("content equality cannot establish a hard-link identity");
        left.LinkCount.Should().Be(1);
        right.LinkCount.Should().Be(1);
    }

    [Fact]
    public void Read_MissingInvalidOrDisposedInputs_FailExplicitly()
    {
        PlatformFileIdentityReader reader = new();
        Action nullHandle = () => reader.Read((SafeFileHandle)null!);
        Action relative = () => reader.Read("relative.py");
        Action missing = () => reader.Read(Path.Combine(_root, "missing.py"));
        using SafeFileHandle invalid = new(new IntPtr(-1), ownsHandle: false);
        Action invalidHandle = () => reader.Read(invalid);
        string path = Path.Combine(_root, "disposed.py");
        File.WriteAllText(path, "closed");
        SafeFileHandle disposed = File.OpenHandle(path);
        disposed.Dispose();
        Action disposedHandle = () => reader.Read(disposed);

        nullHandle.Should().Throw<ArgumentNullException>();
        relative.Should().Throw<ArgumentException>();
        missing.Should().Throw<FileNotFoundException>();
        invalidHandle.Should().Throw<ArgumentException>();
        disposedHandle.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(0x101)]
    [InlineData(0x104)]
    public void QualifyLinux_MissingRequiredReturnedFields_RefusesInsteadOfInventingIdentity(uint mask)
    {
        Action qualify = () => PlatformFileIdentityReader.QualifyLinux(mask, 1, 0x8000, 42, 8, 1);

        qualify.Should().Throw<InvalidDataException>();
    }

    [Theory]
    [InlineData(40, 0x0200000a, 1)]
    [InlineData(44, 0, 1)]
    [InlineData(44, 0x02000002, 1)]
    [InlineData(44, 0x0200000a, 0)]
    public void QualifyMac_WrongLengthOrMissingFields_RefusesInsteadOfUsingPackedDefaults(uint length, uint common, uint file)
    {
        Action qualify = () => PlatformFileIdentityReader.QualifyMac(length, common, file, 8, 1, 42, 1);

        qualify.Should().Throw<InvalidDataException>();
    }

    [Fact]
    public void QualifyNativeFields_KeepIdentityLinkCountAndRegularTypeSeparate()
    {
        FileIdentity linux = PlatformFileIdentityReader.QualifyLinux(0x105, 2, 0x8000, 42, 8, 1);
        FileIdentity mac = PlatformFileIdentityReader.QualifyMac(44, 0x8200000a, 1, 8, 1, 42, 2);

        linux.LinkCount.Should().Be(2);
        mac.LinkCount.Should().Be(2);
        linux.IsRegular.Should().BeTrue();
        mac.IsRegular.Should().BeTrue();
        PlatformFileIdentityReader.QualifyLinux(0x105, 1, 0x4000, 42, 8, 1).IsRegular.Should().BeFalse();
        PlatformFileIdentityReader.QualifyMac(44, 0x8200000a, 1, 8, 2, 42, 1).IsRegular.Should().BeFalse();
    }

    [LinuxFifoFact]
    public async Task Read_RealLinuxFifoWithoutPeer_ReturnsNonregularMetadataWithoutAContentOpen()
    {
        string path = Path.Combine(_root, "selected_app.py");
        CreateLinuxFifo(path, 0x180).Should().Be(0);
        PlatformFileIdentityReader reader = new();
        Task<FileIdentity> read = Task.Run(() => reader.Read(path));
        try
        {
            FileIdentity identity = await read.WaitAsync(TimeSpan.FromSeconds(10));
            identity.IsRegular.Should().BeFalse();
        }
        finally
        {
            if (!read.IsCompleted)
            {
                // A failing blocking-open implementation needs a peer to finish naturally, not a process signal.
                int descriptor = OpenLinuxFifoPeer(path, 2 | 0x800 | 0x80000);
                descriptor.Should().BeGreaterThanOrEqualTo(0);
                using SafeFileHandle peer = new((IntPtr)descriptor, ownsHandle: true);
                await read.WaitAsync(TimeSpan.FromSeconds(5));
            }
        }
    }

    [LibraryImport("libc", EntryPoint = "mkfifo", StringMarshalling = StringMarshalling.Utf8, SetLastError = true)]
    private static partial int CreateLinuxFifo(string path, uint mode);

    [LibraryImport("libc", EntryPoint = "open", StringMarshalling = StringMarshalling.Utf8, SetLastError = true)]
    private static partial int OpenLinuxFifoPeer(string path, int flags);

    public void Dispose() => Directory.Delete(_root, recursive: true);
}
