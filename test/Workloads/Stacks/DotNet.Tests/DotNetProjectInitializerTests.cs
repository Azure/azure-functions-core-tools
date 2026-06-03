// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.CommandLine;
using Azure.Functions.Cli.Projects;
using NSubstitute;
using Xunit;

namespace Azure.Functions.Cli.Workloads.DotNet.Tests;

public class DotNetProjectInitializerTests : IDisposable
{
    private readonly IDotnetCliRunner _dotnetCli = Substitute.For<IDotnetCliRunner>();
    private readonly ITemplateHivePathProvider _hivePathProvider = Substitute.For<ITemplateHivePathProvider>();
    private readonly string _hivePath;

    public DotNetProjectInitializerTests()
    {
        _hivePath = Path.Combine(Path.GetTempPath(), "func-test-hive-" + Guid.NewGuid().ToString("N"));
        _hivePathProvider.HivePath.Returns(_hivePath);
        _hivePathProvider.TimestampPath.Returns(Path.Combine(_hivePath, ".installed"));
    }

    public void Dispose()
    {
        if (Directory.Exists(_hivePath))
        {
            Directory.Delete(_hivePath, recursive: true);
        }
    }

    private DotNetProjectInitializer CreateInitializer() => new(_dotnetCli, _hivePathProvider);

    [Fact]
    public void Stack_IsDotNet()
    {
        Assert.Equal("dotnet", CreateInitializer().Stack);
    }

    [Fact]
    public void GetInitOptions_ContainsFrameworkOption()
    {
        DotNetProjectInitializer initializer = CreateInitializer();
        RootCommand root = [];
        IReadOnlyList<Option> options = initializer.GetInitOptions(new InitOptionRegistry(root));

        Assert.Single(options);
        Assert.Same(initializer.FrameworkOption, options[0]);
    }

    [Fact]
    public void SupportedLanguages_ReturnsExpectedLanguages()
    {
        Assert.Equal(["C#", "F#"], CreateInitializer().SupportedLanguages);
    }

    [Fact]
    public void DetectAdoptedLanguage_CsprojAtRoot_ReturnsCSharp()
    {
        string dir = Path.Combine(Path.GetTempPath(), "func-test-detect-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "MyApp.csproj"), "<Project/>");

            Assert.Equal("C#", CreateInitializer().DetectAdoptedLanguage(new DirectoryInfo(dir)));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void DetectAdoptedLanguage_FsprojAtRoot_ReturnsFSharp()
    {
        string dir = Path.Combine(Path.GetTempPath(), "func-test-detect-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "MyApp.fsproj"), "<Project/>");
            // .csproj at root would normally win; the .fsproj check fires first
            // so an F# project with a sibling .csproj sample folder still maps to F#.
            Directory.CreateDirectory(Path.Combine(dir, "samples"));
            File.WriteAllText(Path.Combine(dir, "samples", "Sample.csproj"), "<Project/>");

            Assert.Equal("F#", CreateInitializer().DetectAdoptedLanguage(new DirectoryInfo(dir)));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void DetectAdoptedLanguage_NoProjectFile_ReturnsNull()
    {
        string dir = Path.Combine(Path.GetTempPath(), "func-test-detect-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "host.json"), "{}");

            Assert.Null(CreateInitializer().DetectAdoptedLanguage(new DirectoryInfo(dir)));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void DetectAdoptedLanguage_MissingDirectory_ReturnsNull()
    {
        var dir = new DirectoryInfo(Path.Combine(Path.GetTempPath(), "func-test-detect-missing-" + Guid.NewGuid().ToString("N")));
        Assert.Null(CreateInitializer().DetectAdoptedLanguage(dir));
    }

    [Fact]
    public void TemplatesPackageName_IsCorrect()
    {
        Assert.Equal("Microsoft.Azure.Functions.Worker.ProjectTemplates", DotNetProjectInitializer.TemplatesPackageName);
    }

    [Fact]
    public void DefaultFramework_IsNet10()
    {
        Assert.Equal("net10.0", DotNetProjectInitializer.DefaultFramework);
    }

    [Theory]
    [InlineData(null, "C#")]
    [InlineData("", "C#")]
    [InlineData("  ", "C#")]
    [InlineData("csharp", "C#")]
    [InlineData("CSHARP", "C#")]
    [InlineData("C#", "C#")]
    [InlineData("fsharp", "F#")]
    [InlineData("FSHARP", "F#")]
    [InlineData("F#", "F#")]
    [InlineData("unknown", "unknown")]
    public void NormalizeLanguage_ReturnsExpectedResult(string? input, string expected)
    {
        Assert.Equal(expected, CreateInitializer().NormalizeLanguage(input));
    }

    [Fact]
    public void Constructor_ThrowsOnNullRunner()
    {
        Assert.Throws<ArgumentNullException>(() => new DotNetProjectInitializer(null!, _hivePathProvider));
    }

    [Fact]
    public void Constructor_ThrowsOnNullHivePathProvider()
    {
        Assert.Throws<ArgumentNullException>(() => new DotNetProjectInitializer(_dotnetCli, null!));
    }

    [Fact]
    public async Task EnsureTemplatesInstalledAsync_CallsInstallWhenTimestampMissing()
    {
        DotNetProjectInitializer initializer = CreateInitializer();

        // Timestamp doesn't exist (temp path with unique GUID), so install should run.
        await initializer.EnsureTemplatesInstalledAsync(CancellationToken.None);

        await _dotnetCli.Received(1).RunAsync(
            Arg.Is<IReadOnlyList<string>>(args =>
                args.Contains(DotNetProjectInitializer.TemplatesPackageName)),
            null,
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task EnsureTemplatesInstalledAsync_SkipsInstallWhenTimestampIsFresh()
    {
        Directory.CreateDirectory(_hivePath);
        File.WriteAllText(_hivePathProvider.TimestampPath, string.Empty);
        // Create a real template entry (not just .installed) to satisfy the freshness check.
        File.WriteAllText(Path.Combine(_hivePath, "dummy-package"), string.Empty);

        DotNetProjectInitializer initializer = CreateInitializer();

        await initializer.EnsureTemplatesInstalledAsync(CancellationToken.None);

        await _dotnetCli.DidNotReceive().RunAsync(
            Arg.Any<IReadOnlyList<string>>(),
            Arg.Any<string?>(),
            Arg.Any<CancellationToken>());
    }
}
