// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Buffers.Binary;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Update;
using Microsoft.Win32.SafeHandles;

namespace Azure.Functions.Cli.Tests.Common;

public sealed partial class PhysicalFileSystemTests : IDisposable
{
    private const uint IoReparseTagMountPoint = 0xA0000003;
    private const uint GenericWrite = 0x40000000;
    private const uint FileShareRead = 0x00000001;
    private const uint FileShareWrite = 0x00000002;
    private const uint FileShareDelete = 0x00000004;
    private const uint OpenExisting = 3;
    private const uint FileFlagOpenReparsePoint = 0x00200000;
    private const uint FileFlagBackupSemantics = 0x02000000;
    private const uint FsctlSetReparsePoint = 0x000900A4;

    private readonly string _tempDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
    private readonly PhysicalFileSystem _fileSystem = new();
    private readonly List<string> _junctions = [];

    public PhysicalFileSystemTests()
    {
        Directory.CreateDirectory(_tempDirectory);
    }

    public void Dispose()
    {
        for (int i = _junctions.Count - 1; i >= 0; i--)
        {
            try
            {
                Directory.Delete(_junctions[i]);
            }
            catch (DirectoryNotFoundException)
            {
                // A test may have already removed the junction.
            }
        }

        Directory.Delete(_tempDirectory, recursive: true);
    }

    [Fact]
    public async Task ReadAllBytesIfExistsAsync_FileAtLimit_ReturnsAllBytes()
    {
        // Arrange
        string path = Path.Combine(_tempDirectory, "metadata.json");
        byte[] expected = new byte[InstallationMetadataReader.MaximumMetadataByteCount];
        await File.WriteAllBytesAsync(path, expected, CancellationToken.None);

        // Act
        byte[]? actual = await _fileSystem.ReadAllBytesIfExistsAsync(
            path,
            InstallationMetadataReader.MaximumMetadataByteCount,
            CancellationToken.None);

        // Assert
        actual.Should().Equal(expected);
    }

    [Fact]
    public async Task ReadAllBytesIfExistsAsync_FileOverLimit_ThrowsInvalidDataException()
    {
        // Arrange
        string path = Path.Combine(_tempDirectory, "metadata.json");
        byte[] contents = new byte[InstallationMetadataReader.MaximumMetadataByteCount + 1];
        await File.WriteAllBytesAsync(path, contents, CancellationToken.None);

        // Act
        Func<Task> act = async () => await _fileSystem.ReadAllBytesIfExistsAsync(
            path,
            InstallationMetadataReader.MaximumMetadataByteCount,
            CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<InvalidDataException>();
    }

    [Fact]
    public async Task ReadAllBytesIfExistsAsync_PreCancelledMissingFile_ThrowsOperationCanceledException()
    {
        // Arrange
        string path = Path.Combine(_tempDirectory, "missing.json");
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();

        // Act
        Func<Task> act = async () => await _fileSystem.ReadAllBytesIfExistsAsync(
            path,
            InstallationMetadataReader.MaximumMetadataByteCount,
            cancellation.Token);

        // Assert
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public void ResolvePhysicalPath_MissingComponent_ThrowsFileNotFoundException()
    {
        // Arrange
        string missingPath = Path.Combine(_tempDirectory, "missing", "func");

        // Act
        Action act = () => _fileSystem.ResolvePhysicalPath(missingPath);

        // Assert
        act.Should().Throw<FileNotFoundException>();
    }

    [Fact]
    public void ResolvePhysicalPath_UnixSymlinkChain_ResolvesEveryComponent()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        // Arrange
        string physicalDirectory = Path.Combine(_tempDirectory, "releases", "v1", "bin");
        Directory.CreateDirectory(physicalDirectory);
        string physicalExecutable = Path.Combine(physicalDirectory, "func");
        File.WriteAllText(physicalExecutable, string.Empty);

        string currentLink = Path.Combine(_tempDirectory, "current");
        Directory.CreateSymbolicLink(currentLink, Path.Combine("releases", "v1"));
        string executableLink = Path.Combine(_tempDirectory, "func");
        File.CreateSymbolicLink(executableLink, Path.Combine("current", "bin", "func"));

        // Act
        string resolved = _fileSystem.ResolvePhysicalPath(executableLink);

        // Assert
        resolved.Should().Be(physicalExecutable);
    }

    [Fact]
    public void ResolvePhysicalPath_UnixSymlinkCycle_ThrowsIOException()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        // Arrange
        string firstLink = Path.Combine(_tempDirectory, "first");
        string secondLink = Path.Combine(_tempDirectory, "second");
        File.CreateSymbolicLink(firstLink, secondLink);
        File.CreateSymbolicLink(secondLink, firstLink);

        // Act
        Action act = () => _fileSystem.ResolvePhysicalPath(firstLink);

        // Assert
        act.Should().Throw<IOException>().WithMessage("*cycle*");
    }

    [Fact]
    public void ResolvePhysicalPath_WindowsJunction_ResolvesPhysicalTarget()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        // Arrange
        string physicalDirectory = Path.Combine(_tempDirectory, "physical");
        Directory.CreateDirectory(physicalDirectory);
        string physicalExecutable = Path.Combine(physicalDirectory, "func.exe");
        File.WriteAllText(physicalExecutable, string.Empty);
        string junction = Path.Combine(_tempDirectory, "junction");
        CreateJunction(junction, physicalDirectory);

        // Act
        string resolved = _fileSystem.ResolvePhysicalPath(Path.Combine(junction, "func.exe"));

        // Assert
        resolved.Should().Be(physicalExecutable);
        new DirectoryInfo(junction).LinkTarget.Should().NotBeNull();
    }

    [Fact]
    public void ResolvePhysicalPath_WindowsJunctionCycle_ThrowsIOException()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        // Arrange
        string firstJunction = Path.Combine(_tempDirectory, "first");
        string secondJunction = Path.Combine(_tempDirectory, "second");
        CreateJunction(firstJunction, secondJunction);
        CreateJunction(secondJunction, firstJunction);

        // Act
        Action act = () => _fileSystem.ResolvePhysicalPath(firstJunction);

        // Assert
        act.Should().Throw<IOException>().WithMessage("*cycle*");
    }

    private void CreateJunction(string junctionPath, string targetPath)
    {
        Directory.CreateDirectory(junctionPath);

        string substituteName = $@"\??\{Path.GetFullPath(targetPath).TrimEnd(Path.DirectorySeparatorChar)}";
        string printName = Path.GetFullPath(targetPath).TrimEnd(Path.DirectorySeparatorChar);
        byte[] substituteNameBytes = Encoding.Unicode.GetBytes(substituteName);
        byte[] printNameBytes = Encoding.Unicode.GetBytes(printName);
        int pathBufferLength = substituteNameBytes.Length + sizeof(char) + printNameBytes.Length + sizeof(char);
        ushort reparseDataLength = checked((ushort)(8 + pathBufferLength));
        byte[] reparseBuffer = new byte[8 + reparseDataLength];

        BinaryPrimitives.WriteUInt32LittleEndian(reparseBuffer.AsSpan(0, 4), IoReparseTagMountPoint);
        BinaryPrimitives.WriteUInt16LittleEndian(reparseBuffer.AsSpan(4, 2), reparseDataLength);
        BinaryPrimitives.WriteUInt16LittleEndian(reparseBuffer.AsSpan(8, 2), 0);
        BinaryPrimitives.WriteUInt16LittleEndian(reparseBuffer.AsSpan(10, 2), checked((ushort)substituteNameBytes.Length));
        BinaryPrimitives.WriteUInt16LittleEndian(
            reparseBuffer.AsSpan(12, 2),
            checked((ushort)(substituteNameBytes.Length + sizeof(char))));
        BinaryPrimitives.WriteUInt16LittleEndian(reparseBuffer.AsSpan(14, 2), checked((ushort)printNameBytes.Length));
        substituteNameBytes.CopyTo(reparseBuffer, 16);
        printNameBytes.CopyTo(reparseBuffer, 16 + substituteNameBytes.Length + sizeof(char));

        using SafeFileHandle handle = CreateFile(
            junctionPath,
            GenericWrite,
            FileShareRead | FileShareWrite | FileShareDelete,
            IntPtr.Zero,
            OpenExisting,
            FileFlagOpenReparsePoint | FileFlagBackupSemantics,
            IntPtr.Zero);
        if (handle.IsInvalid)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }

        if (!DeviceIoControl(
            handle,
            FsctlSetReparsePoint,
            reparseBuffer,
            checked((uint)reparseBuffer.Length),
            IntPtr.Zero,
            0,
            out _,
            IntPtr.Zero))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }

        _junctions.Add(junctionPath);
    }

    [LibraryImport("kernel32.dll", EntryPoint = "CreateFileW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    private static partial SafeFileHandle CreateFile(
        string fileName,
        uint desiredAccess,
        uint shareMode,
        IntPtr securityAttributes,
        uint creationDisposition,
        uint flagsAndAttributes,
        IntPtr templateFile);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DeviceIoControl(
        SafeFileHandle device,
        uint controlCode,
        byte[] inputBuffer,
        uint inputBufferSize,
        IntPtr outputBuffer,
        uint outputBufferSize,
        out uint bytesReturned,
        IntPtr overlapped);
}
