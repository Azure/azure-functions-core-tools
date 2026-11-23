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
    private int _configurationWrites;
    private bool _generatedFileExistedWhenPrepared;
    private bool _generatedFileExistedWhenConfigured;

    private string Home => Path.Combine(_root, "home");

    private string Output => Path.Combine(_root, "App");

    private string ConfigurationPath => Path.Combine(Output, ".func", "config.json");

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [Fact]
    public async Task InitializeAsync_Success_PreparesDestinationThenCreatesProjectThenWritesConfiguration()
    {
        await InstallAsync();

        int exitCode = await InitializeAsync();

        exitCode.Should().Be(0, _interaction.AllOutput);
        _prepareCalls.Should().Be(1);
        _generatedFileExistedWhenPrepared.Should().BeFalse();
        _configurationWrites.Should().Be(1);
        _generatedFileExistedWhenConfigured.Should().BeTrue();
        File.ReadAllText(Path.Combine(Output, "App.txt")).Should().Be("net10.0");
        File.Exists(Path.Combine(Output, "Http.txt")).Should().BeTrue();
        File.ReadAllText(ConfigurationPath).Should().Be("configuration written");
    }

    [Fact]
    public async Task InitializeAsync_PreflightFailure_DoesNotPrepareDestinationCreateProjectOrWriteConfiguration()
    {
        await InstallAsync();
        Directory.CreateDirectory(Output);
        File.WriteAllText(Path.Combine(Output, "App.txt"), "existing");

        int exitCode = await InitializeAsync();

        exitCode.Should().Be(1);
        _prepareCalls.Should().Be(0);
        _configurationWrites.Should().Be(0);
        _interaction.AllOutput.Should().Contain("these files already exist").And.Contain("App.txt");
        File.ReadAllText(Path.Combine(Output, "App.txt")).Should().Be("existing");
        File.Exists(Path.Combine(Output, "Http.txt")).Should().BeFalse();
    }

    [Fact]
    public async Task InitializeAsync_DestinationNotPrepared_DoesNotCreateProjectOrWriteConfiguration()
    {
        await InstallAsync();

        int exitCode = await InitializeAsync(approveDestination: false);

        exitCode.Should().Be(1);
        _prepareCalls.Should().Be(1);
        _configurationWrites.Should().Be(0);
        File.Exists(Path.Combine(Output, "App.txt")).Should().BeFalse();
        File.Exists(Path.Combine(Output, "Http.txt")).Should().BeFalse();
    }

    [Fact]
    public async Task InitializeAsync_DestinationPreparationCancelled_DoesNotCreateProjectOrWriteConfiguration()
    {
        await InstallAsync();

        Func<Task> act = () => InitializeAsync(afterPreflight: () => throw new OperationCanceledException());

        await act.Should().ThrowAsync<OperationCanceledException>();
        _prepareCalls.Should().Be(1);
        _configurationWrites.Should().Be(0);
        Directory.Exists(Output).Should().BeFalse();
    }

    [Fact]
    public async Task InitializeAsync_CreationFailure_DoesNotWriteConfiguration()
    {
        await InstallAsync();

        int exitCode = await InitializeAsync(afterPreflight: () =>
        {
            Directory.CreateDirectory(Path.Combine(Output, "App.txt"));
        });

        exitCode.Should().Be(1, _interaction.AllOutput);
        _prepareCalls.Should().Be(1);
        _configurationWrites.Should().Be(0);
        _interaction.AllOutput.Should().Contain("Failed to initialize the project");
        Directory.Exists(Path.Combine(Output, "App.txt")).Should().BeTrue();
        File.Exists(ConfigurationPath).Should().BeFalse();
    }

    [Fact]
    public async Task InitializeAsync_UnknownTemplateArgument_DoesNotPrepareDestination()
    {
        await InstallAsync();

        int exitCode = await InitializeAsync(templateArguments: ["--unknown"]);

        exitCode.Should().Be(1);
        _prepareCalls.Should().Be(0);
        _configurationWrites.Should().Be(0);
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
        _configurationWrites.Should().Be(0);
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
        _configurationWrites.Should().Be(0);
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
        _configurationWrites.Should().Be(0);
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
        string[]? args = null,
        Action? afterPreflight = null)
    {
        var directory = WorkingDirectory.FromExplicit(Output);
        using Templater templater = CreateTemplater(directory);
        IReadOnlyList<FuncTemplateInfo> candidates = (await templater.ListProjectTemplatesAsync("python", CancellationToken.None)).Templates;
        candidates.Should().NotBeEmpty();
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
            _ => PrepareDestinationAsync(approveDestination, afterPreflight),
            WriteProjectConfiguration,
            CancellationToken.None);
    }

    private Task<bool> PrepareDestinationAsync(bool approve, Action? afterPreflight)
    {
        _prepareCalls++;
        _generatedFileExistedWhenPrepared = File.Exists(Path.Combine(Output, "App.txt"));
        afterPreflight?.Invoke();
        return Task.FromResult(approve);
    }

    private void WriteProjectConfiguration()
    {
        _configurationWrites++;
        _generatedFileExistedWhenConfigured = File.Exists(Path.Combine(Output, "App.txt"));
        Directory.CreateDirectory(Path.Combine(Output, ".func"));
        File.WriteAllText(ConfigurationPath, "configuration written");
    }
}
