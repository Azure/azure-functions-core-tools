// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Update;

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
    public void ResolvePhysicalPath_WindowsDirectoryReparsePoint_ResolvesWhenSupported()
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
        string directoryLink = Path.Combine(_tempDirectory, "linked");

        try
        {
            Directory.CreateSymbolicLink(directoryLink, physicalDirectory);
        }
        catch (UnauthorizedAccessException)
        {
            return;
        }
        catch (IOException)
        {
            return;
        }

        // Act
        string resolved = _fileSystem.ResolvePhysicalPath(Path.Combine(directoryLink, "func.exe"));

        // Assert
        resolved.Should().Be(physicalExecutable);
    }
}
