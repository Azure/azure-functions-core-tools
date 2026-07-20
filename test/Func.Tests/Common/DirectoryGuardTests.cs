// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Common;

namespace Azure.Functions.Cli.Tests.Common;

public class DirectoryGuardTests : IDisposable
{
    private readonly string _tempDir;

    public DirectoryGuardTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"func-dg-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, recursive: true);
        }
    }

    [Fact]
    public void HasNonGitContent_EmptyDirectory_ReturnsFalse()
    {
        bool result = DirectoryGuard.HasNonGitContent(new DirectoryInfo(_tempDir));

        result.Should().BeFalse();
    }

    [Fact]
    public void HasNonGitContent_OnlyGitDirectory_ReturnsFalse()
    {
        Directory.CreateDirectory(Path.Combine(_tempDir, ".git"));

        bool result = DirectoryGuard.HasNonGitContent(new DirectoryInfo(_tempDir));

        result.Should().BeFalse();
    }

    [Fact]
    public void HasNonGitContent_WithFile_ReturnsTrue()
    {
        File.WriteAllText(Path.Combine(_tempDir, "README.md"), "hello");

        bool result = DirectoryGuard.HasNonGitContent(new DirectoryInfo(_tempDir));

        result.Should().BeTrue();
    }

    [Fact]
    public void HasNonGitContent_WithNonGitDirectory_ReturnsTrue()
    {
        Directory.CreateDirectory(Path.Combine(_tempDir, "src"));

        bool result = DirectoryGuard.HasNonGitContent(new DirectoryInfo(_tempDir));

        result.Should().BeTrue();
    }

    [Fact]
    public void ClearExceptGit_RemovesFilesAndDirs_PreservesGit()
    {
        // Arrange
        File.WriteAllText(Path.Combine(_tempDir, "host.json"), "{}");
        File.WriteAllText(Path.Combine(_tempDir, "local.settings.json"), "{}");
        Directory.CreateDirectory(Path.Combine(_tempDir, "src"));
        File.WriteAllText(Path.Combine(_tempDir, "src", "app.cs"), "code");
        string gitDir = Path.Combine(_tempDir, ".git");
        Directory.CreateDirectory(gitDir);
        File.WriteAllText(Path.Combine(gitDir, "HEAD"), "ref: refs/heads/main");

        // Act
        DirectoryGuard.ClearExceptGit(new DirectoryInfo(_tempDir));

        // Assert
        Directory.Exists(gitDir).Should().BeTrue();
        File.Exists(Path.Combine(gitDir, "HEAD")).Should().BeTrue();
        File.Exists(Path.Combine(_tempDir, "host.json")).Should().BeFalse();
        File.Exists(Path.Combine(_tempDir, "local.settings.json")).Should().BeFalse();
        Directory.Exists(Path.Combine(_tempDir, "src")).Should().BeFalse();
    }

    [Fact]
    public void ClearExceptGit_ClearsReadOnlyFiles()
    {
        string filePath = Path.Combine(_tempDir, "readonly.txt");
        File.WriteAllText(filePath, "content");
        File.SetAttributes(filePath, FileAttributes.ReadOnly);

        DirectoryGuard.ClearExceptGit(new DirectoryInfo(_tempDir));

        File.Exists(filePath).Should().BeFalse();
    }

    [Fact]
    public void HasNonGitContent_ThrowsOnNull()
    {
        FluentActions.Invoking(() => DirectoryGuard.HasNonGitContent(null!)).Should().ThrowExactly<ArgumentNullException>();
    }

    [Fact]
    public void ClearExceptGit_ThrowsOnNull()
    {
        FluentActions.Invoking(() => DirectoryGuard.ClearExceptGit(null!)).Should().ThrowExactly<ArgumentNullException>();
    }
}
