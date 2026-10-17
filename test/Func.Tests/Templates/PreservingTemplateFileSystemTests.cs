// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Templates.Engine;
using Microsoft.TemplateEngine.Abstractions.PhysicalFileSystem;
using NSubstitute;

namespace Azure.Functions.Cli.Tests.Templates;

public sealed class PreservingTemplateFileSystemTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("func-preserved-output-").FullName;

    [Fact]
    public void Constructor_NullInputs_RejectsInvalidDependencies()
    {
        var basis = Substitute.For<IPhysicalFileSystem>();
        Action missingBasis = () => new PreservingTemplateFileSystem(null!, _root, new HashSet<string>());
        Action missingRoot = () => new PreservingTemplateFileSystem(basis, null!, new HashSet<string>());
        Action missingPaths = () => new PreservingTemplateFileSystem(basis, _root, null!);
        missingBasis.Should().Throw<ArgumentNullException>();
        missingRoot.Should().Throw<ArgumentNullException>();
        missingPaths.Should().Throw<ArgumentNullException>();
    }

    [Theory]
    [InlineData("create")]
    [InlineData("write")]
    [InlineData("copy")]
    [InlineData("attributes")]
    [InlineData("timestamp")]
    public void NativeWrite_ExistingUserFile_PreservesBytesAndMetadata(string operation)
    {
        string path = Path.Combine(_root, "existing.txt");
        File.WriteAllText(path, "user-owned\n");
        DateTime timestamp = File.GetLastWriteTimeUtc(path);
        FileAttributes attributes = File.GetAttributes(path);
        var basis = new Microsoft.TemplateEngine.Utils.PhysicalFileSystem();
        PreservingTemplateFileSystem files = new(basis, _root, new HashSet<string>(StringComparer.OrdinalIgnoreCase) { path });
        switch (operation)
        {
            case "create":
                using (Stream stream = files.CreateFile(path))
                {
                    stream.Write([1, 2, 3]);
                    stream.Position = 0;
                    stream.ReadByte().Should().Be(1);
                }
                break;
            case "write":
                files.WriteAllText(path, "replacement");
                break;
            case "copy":
                string source = Path.Combine(_root, "source.txt");
                File.WriteAllText(source, "replacement");
                files.FileCopy(source, path, true);
                break;
            case "attributes":
                files.SetFileAttributes(path, FileAttributes.ReadOnly);
                break;
            default:
                files.SetLastWriteTimeUtc(path, timestamp.AddDays(-1));
                break;
        }

        File.ReadAllText(path).Should().Be("user-owned\n");
        File.GetLastWriteTimeUtc(path).Should().Be(timestamp);
        File.GetAttributes(path).Should().Be(attributes);
    }

    [Fact]
    public void NativeWrite_FileAppearingAfterPreview_IsNeverOverwritten()
    {
        string path = Path.Combine(_root, "appeared.txt");
        var basis = new Microsoft.TemplateEngine.Utils.PhysicalFileSystem();
        PreservingTemplateFileSystem files = new(basis, _root, new HashSet<string>());
        File.WriteAllText(path, "appeared after preview");

        Action write = () => files.CreateFile(path);

        write.Should().Throw<IOException>();
        File.ReadAllText(path).Should().Be("appeared after preview");
    }

    [Fact]
    public void NativeWrite_NewOutput_IsReadableSeekableAndCreatedExactlyOnce()
    {
        string path = Path.Combine(_root, "new.txt");
        PreservingTemplateFileSystem files = new(new Microsoft.TemplateEngine.Utils.PhysicalFileSystem(), _root, new HashSet<string>());
        using (Stream stream = files.CreateFile(path))
        {
            stream.Write([10, 20]);
            stream.Position = 0;
            stream.ReadByte().Should().Be(10);
        }
        File.ReadAllBytes(path).Should().Equal([10, 20]);
        Action repeated = () => files.CreateFile(path);
        repeated.Should().Throw<IOException>();
        Action delete = () => files.FileDelete(path);
        delete.Should().Throw<IOException>();
        File.ReadAllBytes(path).Should().Equal([10, 20]);
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);
}
