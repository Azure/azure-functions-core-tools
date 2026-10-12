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
}
