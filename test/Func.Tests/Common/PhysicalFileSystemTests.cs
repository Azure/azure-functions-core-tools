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
        act.Should().Throw<IOException>();
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
        await using Stream? metadataStream = _fileSystem.OpenReadIfExists(
            Path.Combine(Path.GetDirectoryName(resolved)!, InstallationMetadataReader.MetadataFileName));
        using MemoryStream metadata = new();
        await metadataStream!.CopyToAsync(metadata, CancellationToken.None);

        // Assert
        resolved.Should().Be(executablePath);
        metadata.ToArray().Should().Equal(expected);
    }
}
