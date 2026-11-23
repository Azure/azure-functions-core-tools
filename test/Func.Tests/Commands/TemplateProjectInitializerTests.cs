// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.CommandLine;
using Azure.Functions.Cli.Commands;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Tests.Templates;
using Azure.Functions.Cli.Templates.Engine;

namespace Azure.Functions.Cli.Tests.Commands;

public sealed class TemplateProjectInitializerTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"func-template-initializer-{Guid.NewGuid():N}");
    private readonly TestInteractionService _interaction = new();
    private int _prepareCalls;
    private bool _generatedFileExistedWhenPrepared;

    private string Home => Path.Combine(_root, "home");

    private string Output => Path.Combine(_root, "App");

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [Fact]
    public async Task InitializeAsync_NoCandidates_Throws()
    {
        await InstallAsync();
        var directory = WorkingDirectory.FromExplicit(Output);
        using Templater templater = CreateTemplater(directory);

        Func<Task> act = () => new TemplateProjectInitializer(_interaction).InitializeAsync(
            templater,
            [],
            new InitContext(directory, ProjectName: null, Language: "python", Force: false),
            "python",
            new Command("init").Parse(Array.Empty<string>()),
            [],
            [],
            _ => PrepareDestinationAsync(approve: true),
            CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentException>().WithParameterName("candidates");
        _prepareCalls.Should().Be(0);
    }

    [Fact]
    public async Task InitializeAsync_Success_PreparesDestinationBeforeCreatingProject()
    {
        await InstallAsync();

        int exitCode = await InitializeAsync();

        exitCode.Should().Be(0, _interaction.AllOutput);
        _prepareCalls.Should().Be(1);
        _generatedFileExistedWhenPrepared.Should().BeFalse();
        File.ReadAllText(Path.Combine(Output, "App.txt")).Should().Be("net10.0");
        File.Exists(Path.Combine(Output, "Http.txt")).Should().BeTrue();
        File.Exists(Path.Combine(Output, ".func", "config.json")).Should().BeFalse();
    }

    [Fact]
    public async Task InitializeAsync_PreflightFailure_DoesNotPrepareDestinationOrCreateProject()
    {
        await InstallAsync();
        Directory.CreateDirectory(Output);
        File.WriteAllText(Path.Combine(Output, "App.txt"), "existing");

        int exitCode = await InitializeAsync();

        exitCode.Should().Be(1);
        _prepareCalls.Should().Be(0);
        _interaction.AllOutput.Should().Contain("these files already exist").And.Contain("App.txt");
        File.ReadAllText(Path.Combine(Output, "App.txt")).Should().Be("existing");
        File.Exists(Path.Combine(Output, "Http.txt")).Should().BeFalse();
    }

    [Fact]
    public async Task InitializeAsync_DestinationNotPrepared_DoesNotCreateProject()
    {
        await InstallAsync();

        int exitCode = await InitializeAsync(approveDestination: false);

        exitCode.Should().Be(1);
        _prepareCalls.Should().Be(1);
        File.Exists(Path.Combine(Output, "App.txt")).Should().BeFalse();
        File.Exists(Path.Combine(Output, "Http.txt")).Should().BeFalse();
    }

    [Fact]
    public async Task InitializeAsync_UnknownTemplateArgument_DoesNotPrepareDestination()
    {
        await InstallAsync();

        int exitCode = await InitializeAsync(templateArguments: ["--unknown"]);

        exitCode.Should().Be(1);
        _prepareCalls.Should().Be(0);
        _interaction.AllOutput.Should().Contain("Unrecognized option '--unknown'.");
        Directory.Exists(Output).Should().BeFalse();
    }

    [Fact]
    public async Task InitializeAsync_MissingDefaultTemplate_DoesNotPrepareDestination()
    {
        await InstallAsync(shortName: "func-python-other");

        int exitCode = await InitializeAsync();

        exitCode.Should().Be(1);
        _prepareCalls.Should().Be(0);
        _interaction.AllOutput.Should().Contain("Expected one default project template named 'func-python'")
            .And.Contain("but found 0");
        Directory.Exists(Output).Should().BeFalse();
    }

    [Fact]
    public async Task InitializeAsync_DuplicateDefaultTemplate_DoesNotPrepareDestination()
    {
        await InstallAsync(alternateShortName: TemplateFixtures.InitProjectTemplateShortName);

        int exitCode = await InitializeAsync();

        exitCode.Should().Be(1);
        _prepareCalls.Should().Be(0);
        _interaction.AllOutput.Should().Contain("but found 2");
        Directory.Exists(Output).Should().BeFalse();
    }

    [Fact]
    public async Task InitializeAsync_StackOptionWithMatchingTemplateAlias_BindsTemplateParameter()
    {
        await InstallAsync();
        var targetFramework = new Option<string>("--target-framework", "-tfm");

        int exitCode = await InitializeAsync(stackOptions: [targetFramework], args: ["-tfm", "net11.0"]);

        exitCode.Should().Be(0, _interaction.AllOutput);
        File.ReadAllText(Path.Combine(Output, "App.txt")).Should().Be("net11.0");
    }

    [Fact]
    public async Task InitializeAsync_StackOptionWithoutTemplateAlias_DoesNotPrepareDestination()
    {
        await InstallAsync();
        var noBundles = new Option<bool>("--no-bundles");

        int exitCode = await InitializeAsync(stackOptions: [noBundles], args: ["--no-bundles"]);

        exitCode.Should().Be(1);
        _prepareCalls.Should().Be(0);
        _interaction.AllOutput.Should().Contain("Option '--no-bundles' is not supported by template");
        Directory.Exists(Output).Should().BeFalse();
    }

    private async Task InstallAsync(
        string shortName = TemplateFixtures.InitProjectTemplateShortName,
        string alternateShortName = "func-python-alternative")
    {
        string package = TemplateFixtures.WriteInitProjectPackage(_root, shortName: shortName, alternateShortName: alternateShortName);
        await TemplateFixtures.InstallAsync(package, Path.Combine(Home, "templates"));
    }

    private Templater CreateTemplater(WorkingDirectory directory)
        => TemplateFixtures.CreateTemplaterFactory(Home).Create(
            new TemplateEngineContext(directory, new TemplateEngineProjectContext(directory, "python", "python")));

    private async Task<int> InitializeAsync(
        bool approveDestination = true,
        IReadOnlyList<string>? templateArguments = null,
        IReadOnlyList<Option>? stackOptions = null,
        string[]? args = null)
    {
        var directory = WorkingDirectory.FromExplicit(Output);
        using Templater templater = CreateTemplater(directory);
        IReadOnlyList<FuncTemplateInfo> candidates = (await templater.ListProjectTemplatesAsync("python", CancellationToken.None)).Templates;
        var command = new Command("init");
        foreach (Option option in stackOptions ?? [])
        {
            command.Options.Add(option);
        }

        return await new TemplateProjectInitializer(_interaction).InitializeAsync(
            templater,
            candidates,
            new InitContext(directory, ProjectName: null, Language: "python", Force: false),
            "python",
            command.Parse(args ?? []),
            stackOptions ?? [],
            templateArguments ?? [],
            _ => PrepareDestinationAsync(approveDestination),
            CancellationToken.None);
    }

    private Task<bool> PrepareDestinationAsync(bool approve)
    {
        _prepareCalls++;
        _generatedFileExistedWhenPrepared = File.Exists(Path.Combine(Output, "App.txt"));
        return Task.FromResult(approve);
    }
}
