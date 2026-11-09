// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Common;
using System.Reflection;
using System.Runtime.InteropServices;

namespace Azure.Functions.Cli.Tests.Common;

public sealed class PhysicalFileSystemTests : IDisposable
{
    private readonly string _tempDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
    private readonly PhysicalFileSystem _fileSystem = new();

    public PhysicalFileSystemTests()
    {
        Directory.CreateDirectory(_tempDirectory);
    }

    public void Dispose()
    {
        Directory.Delete(_tempDirectory, recursive: true);
    }

    [Fact]
    public void DirectoryHasEntries_EmptyDirectory_ReturnsFalse()
    {
        _fileSystem.DirectoryHasEntries(_tempDirectory).Should().BeFalse();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DirectoryHasEntries_ImmediateFileOrDirectory_ReturnsTrue(bool directory)
    {
        string path = Path.Combine(_tempDirectory, directory ? ".git" : "item.txt");
        if (directory)
        {
            Directory.CreateDirectory(path);
        }
        else
        {
            File.WriteAllText(path, string.Empty);
        }

        _fileSystem.DirectoryHasEntries(_tempDirectory).Should().BeTrue();
    }

    [Fact]
    public void DirectoryHasEntries_HiddenFile_ReturnsTrue()
    {
        string path = Path.Combine(_tempDirectory, ".hidden");
        File.WriteAllText(path, string.Empty);
        if (OperatingSystem.IsWindows())
        {
            File.SetAttributes(path, File.GetAttributes(path) | FileAttributes.Hidden);
        }

        _fileSystem.DirectoryHasEntries(_tempDirectory).Should().BeTrue();
    }

    [Fact]
    public void DirectoryHasEntries_MissingDirectory_Throws()
    {
        Action act = () => _fileSystem.DirectoryHasEntries(Path.Combine(_tempDirectory, "missing"));

        act.Should().Throw<DirectoryNotFoundException>();
    }

    [Fact]
    public async Task OpenReadIfExists_ExistingFile_ReturnsReadableStream()
    {
        // Arrange
        string path = Path.Combine(_tempDirectory, "metadata.json");
        byte[] expected = [1, 2, 3];
        await File.WriteAllBytesAsync(path, expected, CancellationToken.None);

        // Act
        await using Stream? stream = _fileSystem.OpenReadIfExists(path);

        // Assert
        stream.Should().NotBeNull();
        using MemoryStream actual = new();
        await stream!.CopyToAsync(actual, CancellationToken.None);
        actual.ToArray().Should().Equal(expected);
    }

    [Fact]
    public void OpenReadIfExists_MissingFile_ReturnsNull()
    {
        // Arrange
        string path = Path.Combine(_tempDirectory, "missing.json");

        // Act
        Stream? stream = _fileSystem.OpenReadIfExists(path);

        // Assert
        stream.Should().BeNull();
    }

    [Theory]
    [InlineData(true, Architecture.X64, Architecture.X64, true)]
    [InlineData(true, Architecture.X86, Architecture.X64, false)]
    [InlineData(true, Architecture.Arm64, Architecture.Arm64, false)]
    [InlineData(true, Architecture.X64, Architecture.Arm64, false)]
    [InlineData(true, Architecture.Arm, Architecture.Arm64, false)]
    [InlineData(false, Architecture.X64, Architecture.X64, false)]
    public void IsTemplateSourcePlatformSupported_RequiresWindowsX64(
        bool isWindows,
        Architecture processArchitecture,
        Architecture operatingSystemArchitecture,
        bool expected)
    {
        PhysicalFileSystem.IsTemplateSourcePlatformSupported(isWindows, processArchitecture, operatingSystemArchitecture)
            .Should().Be(expected);
    }

    [WindowsX64TemplateSourceFact]
    public async Task OpenReadNoFollow_ExistingFilePinsItsFileAndAncestryUntilDisposal()
    {
        string directory = Path.Combine(_tempDirectory, "source");
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "package.nupkg");
        string replacementPath = Path.Combine(directory, "replacement.nupkg");
        byte[] expected = [1, 2, 3];
        await File.WriteAllBytesAsync(path, expected);
        await File.WriteAllBytesAsync(replacementPath, [4, 5, 6]);

        await using Stream input = _fileSystem.OpenReadNoFollow(path, CancellationToken.None);
        Action replaceFile = () => File.Move(replacementPath, path, overwrite: true);
        Action renameAncestor = () => Directory.Move(directory, Path.Combine(_tempDirectory, "moved-source"));

        replaceFile.Should().Throw<IOException>();
        renameAncestor.Should().Throw<IOException>();
        using MemoryStream actual = new();
        await input.CopyToAsync(actual, CancellationToken.None);
        actual.ToArray().Should().Equal(expected);

        await input.DisposeAsync();
        File.Move(replacementPath, path, overwrite: true);
        Directory.Move(directory, Path.Combine(_tempDirectory, "moved-source"));
    }

    [WindowsX64TemplateSourceFact]
    public void OpenReadNoFollow_RejectsReparsePointAtLeaf()
    {
        string target = Path.Combine(_tempDirectory, "target.nupkg");
        string link = Path.Combine(_tempDirectory, "link.nupkg");
        File.WriteAllBytes(target, [1]);
        File.CreateSymbolicLink(link, target);

        Stream? stream = null;
        Exception? exception = Record.Exception(() => stream = _fileSystem.OpenReadNoFollow(link, CancellationToken.None));
        stream?.Dispose();

        exception.Should().NotBeNull();
        (exception is IOException or NotSupportedException).Should().BeTrue();
    }

    [WindowsX64TemplateSourceFact]
    public void OpenReadNoFollow_RejectsReparsePointInAncestry()
    {
        string target = Path.Combine(_tempDirectory, "target");
        string link = Path.Combine(_tempDirectory, "link");
        Directory.CreateDirectory(target);
        File.WriteAllBytes(Path.Combine(target, "package.nupkg"), [1]);
        Directory.CreateSymbolicLink(link, target);

        Stream? stream = null;
        Exception? exception = Record.Exception(() =>
            stream = _fileSystem.OpenReadNoFollow(Path.Combine(link, "package.nupkg"), CancellationToken.None));
        stream?.Dispose();

        exception.Should().NotBeNull();
        (exception is IOException or NotSupportedException).Should().BeTrue();
    }

    [Fact]
    public void OpenReadNoFollow_PreCanceled_ThrowsBeforeOpening()
    {
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();

        Action act = () => _fileSystem.OpenReadNoFollow(Path.Combine(_tempDirectory, "missing.nupkg"), cancellation.Token);

        act.Should().Throw<OperationCanceledException>();
    }

    [WindowsX64TemplateSourceFact]
    public void OpenReadNoFollow_MissingFileFailsClosed()
    {
        Action act = () => _fileSystem.OpenReadNoFollow(Path.Combine(_tempDirectory, "missing.nupkg"), CancellationToken.None);

        act.Should().Throw<IOException>();
    }

    [Fact]
    public void IoStatusBlock_MatchesNativeLayout()
    {
        Type ioStatusBlock = typeof(PhysicalFileSystem).GetNestedType("IoStatusBlock", BindingFlags.NonPublic)!;

        Marshal.SizeOf(ioStatusBlock).Should().Be(IntPtr.Size * 2);
        Marshal.OffsetOf(ioStatusBlock, "Information").ToInt32().Should().Be(IntPtr.Size);
    }

    [UnsupportedTemplateSourceFact]
    public void SafeSourceOperations_UnsupportedPlatformFailClosed()
    {
        Action open = () => _fileSystem.OpenReadNoFollow(
            Path.Combine(_tempDirectory, "source.nupkg"),
            CancellationToken.None);
        open.Should().Throw<PlatformNotSupportedException>().WithMessage("*Windows x64*");
    }
}
