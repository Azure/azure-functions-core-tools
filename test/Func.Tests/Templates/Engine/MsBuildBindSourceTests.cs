// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Templates.Engine;

namespace Azure.Functions.Cli.Tests.Templates.Engine;

/// <summary>
/// Unit tests for the <c>msbuild:</c> bind source and its project-file
/// property reader: it answers <c>msbuild:&lt;Property&gt;</c> from the project
/// in the current directory and falls back sanely when there is no project.
/// </summary>
public class MsBuildBindSourceTests
{
    private const string Csproj =
        "<Project Sdk=\"Microsoft.NET.Sdk\">\n  <PropertyGroup>\n    <TargetFramework>net8.0</TargetFramework>\n  </PropertyGroup>\n</Project>\n";

    private static readonly string _projectDir = Path.Combine(Path.GetTempPath(), "func-msbuild-project");

    [Fact]
    public async Task GetBoundValueAsync_ReturnsProjectTargetFramework()
    {
        (MsBuildBindSymbolSource source, FuncProjectDirectoryAccessor accessor) = CreateSource();
        accessor.Current = _projectDir;

        string? value = await source.GetBoundValueAsync(null!, "TargetFramework", CancellationToken.None);

        value.Should().Be("net8.0");
    }

    [Fact]
    public async Task GetBoundValueAsync_StripsMsBuildPrefix()
    {
        (MsBuildBindSymbolSource source, FuncProjectDirectoryAccessor accessor) = CreateSource();
        accessor.Current = _projectDir;

        string? value = await source.GetBoundValueAsync(null!, "msbuild:TargetFramework", CancellationToken.None);

        value.Should().Be("net8.0");
    }

    [Fact]
    public async Task GetBoundValueAsync_NoProjectDirectory_ReturnsNull()
    {
        (MsBuildBindSymbolSource source, FuncProjectDirectoryAccessor accessor) = CreateSource();
        accessor.Current = null;

        string? value = await source.GetBoundValueAsync(null!, "TargetFramework", CancellationToken.None);

        value.Should().BeNull();
    }

    [Fact]
    public async Task GetBoundValueAsync_ProjectWithoutMatchingProperty_ReturnsNull()
    {
        (MsBuildBindSymbolSource source, FuncProjectDirectoryAccessor accessor) = CreateSource();
        accessor.Current = _projectDir;

        string? value = await source.GetBoundValueAsync(null!, "NonExistentProperty", CancellationToken.None);

        value.Should().BeNull();
    }

    [Fact]
    public void SourceMetadata_UsesMsBuildPrefix()
    {
        (MsBuildBindSymbolSource source, _) = CreateSource();

        source.SourcePrefix.Should().Be("msbuild");
        source.RequiresPrefixMatch.Should().BeTrue();
    }

    private static (MsBuildBindSymbolSource Source, FuncProjectDirectoryAccessor Accessor) CreateSource()
    {
        var fs = new InMemoryFuncTemplateFileSystem();
        fs.Seed(Path.Combine(_projectDir, "MyApp.csproj"), Csproj);
        var accessor = new FuncProjectDirectoryAccessor();
        var reader = new MsBuildProjectFilePropertyReader(fs);
        return (new MsBuildBindSymbolSource(accessor, reader), accessor);
    }
}
