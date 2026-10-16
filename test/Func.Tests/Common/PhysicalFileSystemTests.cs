// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Common;

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
}
