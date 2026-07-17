// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.CommandLine;
using Azure.Functions.Cli.Projects;
using NSubstitute;

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
        CreateInitializer().Stack.Should().Be("dotnet");
    }

    [Fact]
    public void GetInitOptions_ContainsFrameworkOption()
    {
        DotNetProjectInitializer initializer = CreateInitializer();
        RootCommand root = [];
        IReadOnlyList<Option> options = initializer.GetInitOptions(new InitOptionRegistry(root));

        options.Should().ContainSingle();
        options[0].Should().BeSameAs(initializer.FrameworkOption);
    }

    [Fact]
    public void SupportedLanguages_ReturnsExpectedLanguages()
    {
        CreateInitializer().SupportedLanguages.Should().Equal(["C#", "F#"]);
    }

    [Fact]
    public void TemplatesPackageName_IsCorrect()
    {
        DotNetProjectInitializer.TemplatesPackageName.Should().Be("Microsoft.Azure.Functions.Worker.ProjectTemplates");
    }

    [Fact]
    public void DefaultFramework_IsNet10()
    {
        DotNetProjectInitializer.DefaultFramework.Should().Be("net10.0");
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
        CreateInitializer().NormalizeLanguage(input).Should().Be(expected);
    }

    [Fact]
    public void Constructor_ThrowsOnNullRunner()
    {
        FluentActions.Invoking(() => new DotNetProjectInitializer(null!, _hivePathProvider)).Should().ThrowExactly<ArgumentNullException>();
    }

    [Fact]
    public void Constructor_ThrowsOnNullHivePathProvider()
    {
        FluentActions.Invoking(() => new DotNetProjectInitializer(_dotnetCli, null!)).Should().ThrowExactly<ArgumentNullException>();
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
