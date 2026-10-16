// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Common;

namespace Azure.Functions.Cli.Tests.Common;

public sealed class PhysicalFileSystemEntriesTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "func-directory-entries-" + Guid.NewGuid().ToString("N"));
    private readonly IFileSystem _fileSystem = new PhysicalFileSystem();

    [Fact]
    public void GetFileSystemEntries_ReturnsImmediateFilesDirectoriesAndDotEntriesWithoutRecursion()
    {
        Directory.CreateDirectory(_root);
        string file = Path.Combine(_root, "source.txt");
        string dotFile = Path.Combine(_root, ".hidden");
        string directory = Path.Combine(_root, ".func");
        string empty = Path.Combine(_root, "empty");
        File.WriteAllText(file, "source");
        File.WriteAllText(dotFile, "dot");
        Directory.CreateDirectory(directory);
        Directory.CreateDirectory(empty);
        File.WriteAllText(Path.Combine(directory, "nested.txt"), "not a root entry");

        var entries = _fileSystem.GetFileSystemEntries(_root);

        entries.Should().BeEquivalentTo(file, dotFile, directory, empty);
    }

    [Fact]
    public void GetFileSystemEntries_EmptyDirectory_ReturnsNoEntries()
    {
        Directory.CreateDirectory(_root);

        var entries = _fileSystem.GetFileSystemEntries(_root);

        entries.Should().BeEmpty();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public void GetFileSystemEntries_InvalidPath_RejectsProgrammerError(string? path)
    {
        Action act = () => _fileSystem.GetFileSystemEntries(path!);

        act.Should().Throw<ArgumentException>().WithParameterName("directoryPath");
    }

    [Fact]
    public void GetFileSystemEntries_MissingDirectory_ThrowsInsteadOfReportingAnEmptyDirectory()
    {
        Action act = () => _fileSystem.GetFileSystemEntries(_root);

        act.Should().Throw<DirectoryNotFoundException>();
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }
}
