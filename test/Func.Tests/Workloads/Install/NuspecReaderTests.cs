// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Workloads.Install;
using Xunit;

namespace Azure.Functions.Cli.Tests.Workloads.Install;

public sealed class NuspecReaderTests : IDisposable
{
    private readonly string _tempDir = Directory.CreateTempSubdirectory("nuspec-reader-tests-").FullName;
    private readonly NuspecReader _reader = new();

    public void Dispose() => Directory.Delete(_tempDir, recursive: true);

    [Fact]
    public void Read_FullNuspec_ReturnsAllFields()
    {
        var path = WriteNuspec("""
            <?xml version="1.0"?>
            <package xmlns="http://schemas.microsoft.com/packaging/2010/07/nuspec.xsd">
              <metadata>
                <id>Azure.Functions.Cli.Workload.Dotnet</id>
                <version>1.2.3</version>
                <title>.NET Workload</title>
                <description>C# / F# support for func.</description>
                <tags>dotnet csharp fsharp</tags>
                <packageTypes>
                  <packageType name="FuncCliWorkload" />
                </packageTypes>
              </metadata>
            </package>
            """);

        var meta = _reader.Read(path);

        Assert.Equal("Azure.Functions.Cli.Workload.Dotnet", meta.PackageId);
        Assert.Equal("1.2.3", meta.Version);
        Assert.Equal(".NET Workload", meta.Title);
        Assert.Equal("C# / F# support for func.", meta.Description);
        Assert.Equal(new[] { "dotnet", "csharp", "fsharp" }, meta.Aliases);
        Assert.Equal(new[] { "FuncCliWorkload" }, meta.PackageTypes);
    }

    [Fact]
    public void Read_MinimalNuspec_OptionalFieldsAreEmpty()
    {
        var path = WriteNuspec("""
            <?xml version="1.0"?>
            <package>
              <metadata>
                <id>Foo</id>
                <version>0.1.0</version>
              </metadata>
            </package>
            """);

        var meta = _reader.Read(path);

        Assert.Equal("Foo", meta.PackageId);
        Assert.Equal("0.1.0", meta.Version);
        Assert.Equal(string.Empty, meta.Title);
        Assert.Equal(string.Empty, meta.Description);
        Assert.Empty(meta.Aliases);
        Assert.Empty(meta.PackageTypes);
    }

    [Fact]
    public void Read_MissingId_ThrowsGracefulException()
    {
        var path = WriteNuspec("""
            <?xml version="1.0"?>
            <package><metadata><version>1.0.0</version></metadata></package>
            """);

        var ex = Assert.Throws<GracefulException>(() => _reader.Read(path));
        Assert.True(ex.IsUserError);
        Assert.Contains("<id>", ex.Message);
    }

    [Fact]
    public void Read_MissingFile_ThrowsGracefulException()
    {
        var ex = Assert.Throws<GracefulException>(
            () => _reader.Read(Path.Combine(_tempDir, "missing.nuspec")));
        Assert.True(ex.IsUserError);
        Assert.Contains("does not exist", ex.Message);
    }

    [Fact]
    public void Read_MultiplePackageTypes_ReturnsAll()
    {
        var path = WriteNuspec("""
            <?xml version="1.0"?>
            <package>
              <metadata>
                <id>Foo</id>
                <version>1.0.0</version>
                <packageTypes>
                  <packageType name="Dependency" />
                  <packageType name="FuncCliWorkload" />
                </packageTypes>
              </metadata>
            </package>
            """);

        var meta = _reader.Read(path);

        Assert.Equal(new[] { "Dependency", "FuncCliWorkload" }, meta.PackageTypes);
    }

    private string WriteNuspec(string content)
    {
        var path = Path.Combine(_tempDir, $"{Guid.NewGuid():N}.nuspec");
        File.WriteAllText(path, content);
        return path;
    }
}
