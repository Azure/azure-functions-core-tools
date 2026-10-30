// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.CommandLine;
using Azure.Functions.Cli.Commands;
using Azure.Functions.Cli.Common;
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

    private DotNetProjectInitializer CreateInitializer() => new(_dotnetCli, _hivePathProvider, new DotNetProjectStack());

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
        options.Should().OnlyContain(option =>
            option.Description != null &&
            !string.IsNullOrWhiteSpace(option.Description) &&
            !option.Description.Contains('\r') &&
            !option.Description.Contains('\n'));
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

    [Fact]
    public void SupportedFrameworks_ContainsNet10AndNet11()
    {
        DotNetProjectInitializer.SupportedFrameworks.Should().Equal("net10.0", "net11.0");
    }

    [Theory]
    [InlineData("net10.0", "C#", "--target-framework")]
    [InlineData("net10.0", "F#", "-tfm")]
    [InlineData("net11.0", "C#", "--target-framework")]
    [InlineData("net11.0", "C#", "-tfm")]
    [InlineData("NET11.0", "C#", "--target-framework")]
    public async Task InitializeAsync_SupportedFramework_PassesFrameworkAndLanguageToDotnet(string framework, string language, string option)
    {
        DotNetProjectInitializer initializer = CreateInitializer();
        RootCommand root = [];
        initializer.GetInitOptions(new InitOptionRegistry(root));
        string projectPath = Path.Combine(_hivePath, "project");
        InitContext context = new(WorkingDirectory.FromExplicit(projectPath), "MyApp", language, Force: false);
        using CancellationTokenSource cts = new();
        string[] expectedArguments =
        [
            "new", "func",
            "--name", "MyApp",
            "--output", projectPath,
            "--language", language,
            "--Framework", framework,
            "--debug:custom-hive", _hivePath
        ];

        await initializer.InitializeAsync(context, root.Parse([option, framework]), cts.Token);

        await _dotnetCli.Received(1).RunAsync(
            Arg.Is<IReadOnlyList<string>>(args => args.SequenceEqual(expectedArguments)),
            projectPath,
            Arg.Is<CancellationToken>(token => token.CanBeCanceled));
    }

    [Theory]
    [InlineData("C#")]
    [InlineData("F#")]
    [InlineData("fsharp")]
    public async Task InitializeAsync_NoFrameworkOption_DefaultsToNet10(string language)
    {
        DotNetProjectInitializer initializer = CreateInitializer();
        RootCommand root = [];
        initializer.GetInitOptions(new InitOptionRegistry(root));
        string projectPath = Path.Combine(_hivePath, "project");
        InitContext context = new(WorkingDirectory.FromExplicit(projectPath), "MyApp", language, Force: false);

        await initializer.InitializeAsync(context, root.Parse([]), CancellationToken.None);

        await _dotnetCli.Received(1).RunAsync(
            Arg.Is<IReadOnlyList<string>>(args => args.Contains("net10.0") && !args.Contains("net11.0")),
            projectPath,
            Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("fsharp", "net11.0", "--target-framework")]
    [InlineData("F#", "NET11.0", "--target-framework")]
    [InlineData("FSHARP", "net11.0", "-tfm")]
    [InlineData("f#", "NET11.0", "-tfm")]
    public async Task InitializeAsync_Net11WithFSharp_RejectsBeforeInstallingTemplates(string language, string framework, string option)
    {
        DotNetProjectInitializer initializer = CreateInitializer();
        RootCommand root = [];
        initializer.GetInitOptions(new InitOptionRegistry(root));
        InitContext context = new(WorkingDirectory.FromExplicit(Path.Combine(_hivePath, "project")), "MyApp", language, Force: false);

        GracefulException exception = (await FluentActions.Awaiting(() => initializer.InitializeAsync(context, root.Parse([option, framework]), CancellationToken.None))
            .Should().ThrowAsync<GracefulException>()
            .WithMessage(".NET 11 isolated project initialization is not yet supported for F#. Use C# or target .NET 10 instead.")).Which;

        exception.IsUserError.Should().BeTrue();
        await _dotnetCli.DidNotReceive().RunAsync(
            Arg.Any<IReadOnlyList<string>>(),
            Arg.Any<string?>(),
            Arg.Any<CancellationToken>());
        Directory.Exists(_hivePath).Should().BeFalse();
    }

    [Theory]
    [InlineData("net8.0")]
    [InlineData("net12.0")]
    public async Task InitializeAsync_UnsupportedFramework_RejectsBeforeInstallingTemplates(string framework)
    {
        DotNetProjectInitializer initializer = CreateInitializer();
        RootCommand root = [];
        initializer.GetInitOptions(new InitOptionRegistry(root));
        InitContext context = new(WorkingDirectory.FromExplicit(Path.Combine(_hivePath, "project")), "MyApp", "C#", Force: false);

        await FluentActions.Awaiting(() => initializer.InitializeAsync(context, root.Parse(["--target-framework", framework]), CancellationToken.None))
            .Should().ThrowAsync<ArgumentException>()
            .WithMessage($"Framework '{framework}' is not supported. Supported frameworks: net10.0, net11.0.*");

        await _dotnetCli.DidNotReceive().RunAsync(
            Arg.Any<IReadOnlyList<string>>(),
            Arg.Any<string?>(),
            Arg.Any<CancellationToken>());
        Directory.Exists(_hivePath).Should().BeFalse();
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
        FluentActions.Invoking(() => new DotNetProjectInitializer(null!, _hivePathProvider, new DotNetProjectStack()))
            .Should().ThrowExactly<ArgumentNullException>();
    }

    [Fact]
    public void Constructor_ThrowsOnNullHivePathProvider()
    {
        FluentActions.Invoking(() => new DotNetProjectInitializer(_dotnetCli, null!, new DotNetProjectStack()))
            .Should().ThrowExactly<ArgumentNullException>();
    }

    [Fact]
    public void Constructor_NullStack_Throws()
    {
        FluentActions.Invoking(() => new DotNetProjectInitializer(_dotnetCli, _hivePathProvider, null!))
            .Should().ThrowExactly<ArgumentNullException>().WithParameterName("stack");
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
