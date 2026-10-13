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
    public void ResolveFileLinkTarget_MissingFile_ThrowsFileNotFoundException()
    {
        // Arrange
        string missingPath = Path.Combine(_tempDirectory, "missing", "func");

        // Act
        Action act = () => _fileSystem.ResolveFileLinkTarget(missingPath);

        // Assert
        act.Should().Throw<FileNotFoundException>();
    }

    [Fact]
    public void ResolveFileLinkTarget_FileIsNotLink_ReturnsOriginalFullPath()
    {
        // Arrange
        string executable = Path.Combine(_tempDirectory, OperatingSystem.IsWindows() ? "func.exe" : "func");
        File.WriteAllText(executable, string.Empty);

        // Act
        string resolved = _fileSystem.ResolveFileLinkTarget(executable);

        // Assert
        resolved.Should().Be(executable);
    }

    [Fact]
    public void ResolveFileLinkTarget_UnixExecutableSymlinkChain_ReturnsFinalTarget()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        // Arrange
        string physicalDirectory = Path.Combine(_tempDirectory, "releases", "v1");
        Directory.CreateDirectory(physicalDirectory);
        string physicalExecutable = Path.Combine(physicalDirectory, "func");
        File.WriteAllText(physicalExecutable, string.Empty);

        string intermediateLink = Path.Combine(_tempDirectory, "intermediate");
        File.CreateSymbolicLink(intermediateLink, Path.Combine("releases", "v1", "func"));
        string executableLink = Path.Combine(_tempDirectory, "func");
        File.CreateSymbolicLink(executableLink, "intermediate");

        // Act
        string resolved = _fileSystem.ResolveFileLinkTarget(executableLink);

        // Assert
        resolved.Should().Be(physicalExecutable);
    }

    [Fact]
    public void ResolveFileLinkTarget_UnixExecutableSymlinkCycle_ThrowsIOException()
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
        Action act = () => _fileSystem.ResolveFileLinkTarget(firstLink);

        // Assert
        act.Should().Throw<IOException>().WithMessage("*cycle*");
    }

    [Fact]
    public async Task ResolveFileLinkTarget_UnixLinkedParent_PreservesAccessToSiblingMetadata()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        // Arrange
        string physicalDirectory = Path.Combine(_tempDirectory, "releases", "v1");
        Directory.CreateDirectory(physicalDirectory);
        string physicalExecutable = Path.Combine(physicalDirectory, "func");
        File.WriteAllText(physicalExecutable, string.Empty);
        string physicalMetadata = Path.Combine(physicalDirectory, InstallationMetadataReader.MetadataFileName);
        byte[] expected = [1, 2, 3];
        await File.WriteAllBytesAsync(physicalMetadata, expected, CancellationToken.None);

        string currentLink = Path.Combine(_tempDirectory, "current");
        Directory.CreateSymbolicLink(currentLink, physicalDirectory);
        string executablePath = Path.Combine(currentLink, "func");

        // Act
        string resolved = _fileSystem.ResolveFileLinkTarget(executablePath);
        byte[]? metadata = await _fileSystem.ReadAllBytesIfExistsAsync(
            Path.Combine(Path.GetDirectoryName(resolved)!, InstallationMetadataReader.MetadataFileName),
            InstallationMetadataReader.MaximumMetadataByteCount,
            CancellationToken.None);

        // Assert
        resolved.Should().Be(executablePath);
        metadata.Should().Equal(expected);
    }
}
