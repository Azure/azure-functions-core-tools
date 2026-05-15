// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Workloads.Resolution;
using Xunit;

namespace Azure.Functions.Cli.Tests.Workloads.Resolution;

public sealed class FuncProjectConfigReaderTests : IDisposable
{
    private readonly DirectoryInfo _dir = Directory.CreateTempSubdirectory("func-project-config-reader-");
    private readonly FuncProjectConfigReader _reader = new();

    public void Dispose() => _dir.Delete(recursive: true);

    [Fact]
    public void Read_FolderMissing_ReturnsNull()
    {
        Assert.Null(_reader.Read(_dir));
    }

    [Fact]
    public void Read_FileMissing_ReturnsNull()
    {
        Directory.CreateDirectory(Path.Combine(_dir.FullName, ".func"));

        Assert.Null(_reader.Read(_dir));
    }

    [Fact]
    public void Read_StackOnly_ReturnsConfigWithStack()
    {
        WriteConfig("""{"stack":"python"}""");

        FuncProjectConfig? config = _reader.Read(_dir);

        Assert.NotNull(config);
        Assert.Equal("python", config!.Stack);
        Assert.Null(config.Language);
    }

    [Fact]
    public void Read_BothFields_ReturnsConfigWithBoth()
    {
        WriteConfig("""{"stack":"node","language":"typescript"}""");

        FuncProjectConfig? config = _reader.Read(_dir);

        Assert.NotNull(config);
        Assert.Equal("node", config!.Stack);
        Assert.Equal("typescript", config.Language);
    }

    [Fact]
    public void Read_UnknownPropertiesIgnored()
    {
        WriteConfig("""{"$schema":"https://example.com/schema.json","stack":"dotnet","azurite":{"port":10000}}""");

        FuncProjectConfig? config = _reader.Read(_dir);

        Assert.NotNull(config);
        Assert.Equal("dotnet", config!.Stack);
    }

    [Fact]
    public void Read_EmptyStack_TreatsAsNotSet()
    {
        WriteConfig("""{"stack":"  ","language":"python"}""");

        FuncProjectConfig? config = _reader.Read(_dir);

        Assert.NotNull(config);
        Assert.Null(config!.Stack);
        Assert.Equal("python", config.Language);
    }

    [Fact]
    public void Read_NeitherFieldSet_ReturnsNull()
    {
        WriteConfig("""{"$schema":"https://example.com/schema.json"}""");

        Assert.Null(_reader.Read(_dir));
    }

    [Fact]
    public void Read_NonStringStack_TreatsAsNotSet()
    {
        WriteConfig("""{"stack":42,"language":"python"}""");

        FuncProjectConfig? config = _reader.Read(_dir);

        Assert.NotNull(config);
        Assert.Null(config!.Stack);
        Assert.Equal("python", config.Language);
    }

    [Fact]
    public void Read_RootIsArray_ReturnsNull()
    {
        WriteConfig("""["stack","python"]""");

        Assert.Null(_reader.Read(_dir));
    }

    [Fact]
    public void Read_MalformedJson_ReturnsNullDoesNotThrow()
    {
        WriteConfig("not json {");

        Assert.Null(_reader.Read(_dir));
    }

    [Fact]
    public void Read_NullDirectory_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => _reader.Read(null!));
    }

    private void WriteConfig(string contents)
    {
        string folder = Path.Combine(_dir.FullName, ".func");
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "config.json"), contents);
    }
}
