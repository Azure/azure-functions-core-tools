// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Common;

namespace Azure.Functions.Cli.Tests.Common;

public sealed class PhysicalFileSystemTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "func-create-only-" + Guid.NewGuid().ToString("N"));
    private readonly string _tempDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
    private readonly PhysicalFileSystem _fileSystem = new();

    public PhysicalFileSystemTests()
    {
        Directory.CreateDirectory(_tempDirectory);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }

        Directory.Delete(_tempDirectory, recursive: true);
    }

    [Fact]
    public async Task CreateAllTextAsync_AbsentDestination_CreatesCompleteFileWithoutTemporaryFiles()
    {
        PhysicalFileSystem fileSystem = new();
        var path = Path.Combine(_root, ".func", "config.json");

        await fileSystem.CreateAllTextAsync(path, "configuration", CancellationToken.None);

        File.ReadAllText(path).Should().Be("configuration");
        Directory.GetFiles(Path.GetDirectoryName(path)!).Should().Equal(path);
    }

    [Fact]
    public async Task CreateAllTextAsync_ExistingDestination_PreservesUserBytes()
    {
        Directory.CreateDirectory(_root);
        var path = Path.Combine(_root, "config.json");
        File.WriteAllBytes(path, [0, 1, 255, 42]);
        PhysicalFileSystem fileSystem = new();

        Func<Task> create = () => fileSystem.CreateAllTextAsync(path, "replacement", CancellationToken.None);

        await create.Should().ThrowAsync<IOException>();
        File.ReadAllBytes(path).Should().Equal(0, 1, 255, 42);
        Directory.GetFiles(_root).Should().Equal(path);
    }

    [Fact]
    public async Task CreateAllTextAsync_ExistingDirectory_PreservesDirectory()
    {
        var path = Path.Combine(_root, "config.json");
        Directory.CreateDirectory(path);
        PhysicalFileSystem fileSystem = new();

        Func<Task> create = () => fileSystem.CreateAllTextAsync(path, "replacement", CancellationToken.None);

        await create.Should().ThrowAsync<IOException>();
        Directory.Exists(path).Should().BeTrue();
        Directory.EnumerateFileSystemEntries(path).Should().BeEmpty();
        Directory.GetFiles(_root).Should().BeEmpty();
    }

    [Fact]
    public async Task CreateAllTextAsync_DestinationAppearsAtCommit_PreservesUserBytesAndCleansTemporaryFile()
    {
        var path = Path.Combine(_root, "config.json");
        CommitBoundaryFileSystem fileSystem = new(destination => File.WriteAllBytes(destination, [0, 1, 255, 42]));

        Func<Task> create = () => fileSystem.CreateAllTextAsync(path, "replacement", CancellationToken.None);

        await create.Should().ThrowAsync<IOException>();
        fileSystem.CommitCalls.Should().Be(1);
        File.ReadAllBytes(path).Should().Equal(0, 1, 255, 42);
        Directory.GetFiles(_root).Should().Equal(path);
    }

    [Fact]
    public async Task WriteAllTextAsync_DestinationAppearsAtCommit_PreservesLegacyOverwriteBehavior()
    {
        var path = Path.Combine(_root, "config.json");
        CommitBoundaryFileSystem fileSystem = new(destination => File.WriteAllText(destination, "previous"));

        await fileSystem.WriteAllTextAsync(path, "replacement", CancellationToken.None);

        File.ReadAllText(path).Should().Be("replacement");
        Directory.GetFiles(_root).Should().Equal(path);
    }

    [Fact]
    public async Task CreateAllTextAsync_PreCanceled_DoesNotCreateParentDirectories()
    {
        PhysicalFileSystem fileSystem = new();
        using CancellationTokenSource cancellation = new();
        await cancellation.CancelAsync();

        Func<Task> create = () => fileSystem.CreateAllTextAsync(Path.Combine(_root, "config.json"), "content", cancellation.Token);

        await create.Should().ThrowAsync<OperationCanceledException>();
        Directory.Exists(_root).Should().BeFalse();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public async Task CreateAllTextAsync_InvalidPath_RejectsWithoutOutput(string? path)
    {
        PhysicalFileSystem fileSystem = new();

        Func<Task> create = () => fileSystem.CreateAllTextAsync(path!, "content", CancellationToken.None);

        await create.Should().ThrowAsync<ArgumentException>().WithParameterName("path");
        Directory.Exists(_root).Should().BeFalse();
    }

    [Fact]
    public async Task CreateAllTextAsync_NullContents_RejectsWithoutOutput()
    {
        PhysicalFileSystem fileSystem = new();

        Func<Task> create = () => fileSystem.CreateAllTextAsync(Path.Combine(_root, "config.json"), null!, CancellationToken.None);

        await create.Should().ThrowAsync<ArgumentNullException>().WithParameterName("contents");
        Directory.Exists(_root).Should().BeFalse();
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
    public void OpenReadIfExists_MissingDirectory_ReturnsNull()
    {
        var stream = _fileSystem.OpenReadIfExists(Path.Combine(_tempDirectory, "missing", "metadata.json"));

        stream.Should().BeNull();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public void OpenReadIfExists_InvalidPath_Throws(string? path)
    {
        Action open = () => _fileSystem.OpenReadIfExists(path!);

        open.Should().Throw<ArgumentException>().WithParameterName("path");
    }

    [Fact]
    public void OpenReadIfExists_ExclusiveFile_PropagatesIOException()
    {
        string path = Path.Combine(_tempDirectory, "exclusive.json");
        using FileStream exclusive = new(path, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None);

        Action open = () => _fileSystem.OpenReadIfExists(path);

        open.Should().Throw<IOException>();
    }

    [Fact]
    public void OpenReadIfExists_Directory_PropagatesAccessFailure()
    {
        Action open = () => _fileSystem.OpenReadIfExists(_tempDirectory);

        open.Should().Throw<UnauthorizedAccessException>();
    }
}

internal sealed class CommitBoundaryFileSystem(Action<string> beforeCommit) : PhysicalFileSystem
{
    private readonly Action<string> _beforeCommit = beforeCommit ?? throw new ArgumentNullException(nameof(beforeCommit));

    public int CommitCalls { get; private set; }

    public IOException? CommitFailure { get; private set; }

    public override void MoveFile(string sourcePath, string destinationPath, bool overwrite = false)
    {
        CommitCalls++;
        _beforeCommit(destinationPath);
        try
        {
            base.MoveFile(sourcePath, destinationPath, overwrite);
        }
        catch (IOException exception)
        {
            CommitFailure = exception;
            throw;
        }
    }
}
