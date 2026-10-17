// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Templates.Engine;
using NSubstitute;
using Microsoft.TemplateEngine.Abstractions.PhysicalFileSystem;

namespace Azure.Functions.Cli.Tests.Templates;

public sealed class PlannedCleanupTemplateFileSystemTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("func-cleanup-preview-").FullName;

    [Theory]
    [InlineData(".func")]
    [InlineData("src")]
    public void FutureView_HidesScheduledRegularAncestorsButNeverMutatesThem(string name)
    {
        string path = Path.Combine(_root, name);
        File.WriteAllText(path, "preserve until qualified and confirmed");
        PlannedCleanupTemplateFileSystem view = new(new Microsoft.TemplateEngine.Utils.PhysicalFileSystem(), _root);

        view.FileExists(path).Should().BeFalse();
        view.DirectoryExists(path).Should().BeFalse();
        Action attributes = () => view.GetFileAttributes(path);
        attributes.Should().Throw<FileNotFoundException>();
        Action create = () => view.CreateFile(path);
        Action delete = () => view.FileDelete(path);
        Action directory = () => view.CreateDirectory(Path.Combine(_root, "new"));
        create.Should().Throw<IOException>();
        delete.Should().Throw<IOException>();
        directory.Should().Throw<IOException>();
        File.ReadAllText(path).Should().Be("preserve until qualified and confirmed");
        Directory.Exists(Path.Combine(_root, "new")).Should().BeFalse();
    }

    [Fact]
    public void FutureView_KeepsPreservedGitVisibleWithoutExposingOtherTargetContent()
    {
        string git = Directory.CreateDirectory(Path.Combine(_root, ".git")).FullName;
        File.WriteAllText(Path.Combine(git, "HEAD"), "preserved git data");
        Directory.CreateDirectory(Path.Combine(_root, "src"));
        File.WriteAllText(Path.Combine(_root, "src", "user.js"), "not part of the future empty target");
        PlannedCleanupTemplateFileSystem view = new(new Microsoft.TemplateEngine.Utils.PhysicalFileSystem(), _root);

        view.DirectoryExists(_root).Should().BeTrue();
        view.ReadAllText(Path.Combine(git, "HEAD")).Should().Be("preserved git data");
        view.EnumerateFileSystemEntries(_root, "*", SearchOption.AllDirectories).Should()
            .BeEquivalentTo(git, Path.Combine(git, "HEAD"));
        Action gitWrite = () => view.WriteAllText(Path.Combine(git, "HEAD"), "cannot modify even preserved entries");
        gitWrite.Should().Throw<IOException>();
        File.ReadAllText(Path.Combine(git, "HEAD")).Should().Be("preserved git data");
    }

    [Fact]
    public void Constructor_InvalidDependencies_AreRejected()
    {
        var basis = Substitute.For<IPhysicalFileSystem>();
        Action missingBasis = () => new PlannedCleanupTemplateFileSystem(null!, _root);
        Action missingRoot = () => new PlannedCleanupTemplateFileSystem(basis, null!);
        missingBasis.Should().Throw<ArgumentNullException>();
        missingRoot.Should().Throw<ArgumentNullException>();
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);
}
