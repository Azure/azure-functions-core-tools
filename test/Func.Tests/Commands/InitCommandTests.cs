// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.CommandLine;
using System.Text.Json;
using Azure.Functions.Cli.Bundles;
using Azure.Functions.Cli.Commands;
using Azure.Functions.Cli.Commands.Init;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Configuration;
using Azure.Functions.Cli.Console;
using Azure.Functions.Cli.Console.Theme;
using Azure.Functions.Cli.Projects;
using Azure.Functions.Cli.Tests.Console;
using Azure.Functions.Cli.Tests.Templates;
using Azure.Functions.Cli.Templates.Engine;
using Azure.Functions.Cli.Workers;
using Azure.Functions.Cli.Workloads;
using Azure.Functions.Cli.Workloads.DotNet;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.TemplateEngine.Abstractions;
using Microsoft.TemplateEngine.Abstractions.Components;
using NSubstitute;

namespace Azure.Functions.Cli.Tests.Commands;

public class InitCommandTests
{
    private static readonly LanguageDescriptor _csharp = new("csharp", "C#", "c#");
    private static readonly LanguageDescriptor _fsharp = new("fsharp", "F#", "f#");
    private static readonly LanguageDescriptor _javascript = new("javascript", "JavaScript", "js");
    private static readonly LanguageDescriptor _typescript = new("typescript", "TypeScript", "ts");
    private static readonly LanguageDescriptor _python = new("python", "Python", "py");
    private readonly TestInteractionService _interaction;
    private readonly RecordingWorkloadHintRenderer _hintRenderer;
    private readonly ILocalSettingsProvider _localSettings;
    private readonly IFunctionsProjectResolver _projectResolver;
    private readonly IHostJsonBundleSectionReader _bundleReader;
    private readonly IInstalledBundleWorkloads _installedBundles;

    public InitCommandTests()
    {
        _interaction = new TestInteractionService();
        _hintRenderer = new RecordingWorkloadHintRenderer();
        _localSettings = Substitute.For<ILocalSettingsProvider>();
        _localSettings.Get(Arg.Any<DirectoryInfo>()).Returns(LocalSettingsSnapshot.Empty);
        _projectResolver = Substitute.For<IFunctionsProjectResolver>();
        _projectResolver
            .ResolveProjectAsync(Arg.Any<ProjectResolutionContext>(), Arg.Any<CancellationToken>())
            .Returns(ProjectResolutionResults.Unrecognized("no project"));
        _bundleReader = Substitute.For<IHostJsonBundleSectionReader>();
        _bundleReader
            .ReadAsync(Arg.Any<DirectoryInfo>(), Arg.Any<CancellationToken>())
            .Returns((HostJsonBundleSection?)null);
        _installedBundles = Substitute.For<IInstalledBundleWorkloads>();
        _installedBundles
            .ListInstalledAsync(Arg.Any<CancellationToken>())
            .Returns([]);
    }

    private InitCommand CreateCommand(
        IReadOnlyList<IProjectInitializer> initializers,
        IInteractionService? interaction = null,
        IEnumerable<IProjectStack>? stacks = null,
        ILocalSettingsProvider? localSettings = null,
        IFunctionsProjectResolver? projectResolver = null,
        ITemplaterFactory? templaterFactory = null,
        IProjectStackConfiguration? configuration = null) =>
        new(
            interaction ?? _interaction,
            _hintRenderer,
            configuration ?? new ProjectStackConfiguration(new PhysicalFileSystem(), localSettings ?? _localSettings),
            projectResolver ?? _projectResolver,
            _bundleReader,
            new InstalledBundleScanner(_installedBundles),
            new InstalledProjectStackCatalog(stacks ?? initializers.Select(i => i.Stack)),
            templaterFactory ?? TemplateFixtures.CreateEmptyTemplaterFactory(),
            initializers);

    private InitCommand CreateCommand(IReadOnlyList<IProjectInitializer> initializers,
        ITemplaterFactory factory, IInteractionService? interaction = null)
        => CreateCommand(initializers, interaction, templaterFactory: factory);

    [Fact]
    public void InitCommand_HasExpectedOptions()
    {
        var cmd = CreateCommand([]);
        var optionNames = cmd.Options.Select(o => o.Name).ToList();

        optionNames.Should().Contain("--stack");
        optionNames.Should().Contain("--name");
        optionNames.Should().Contain("--language");
        optionNames.Should().Contain("--force");
    }

    [Fact]
    public void InitCommand_StackOptionDescription_NoInitializers_PointsAtSetup()
    {
        var cmd = CreateCommand([]);
        string description = cmd.StackOption.Description ?? string.Empty;

        description.Should().Contain("Set up a stack");
        description.Should().Contain("func setup --features");
    }

    [Fact]
    public void InitCommand_StackOptionDescription_ListsCanonicalIdsSorted()
    {
        var cmd = CreateCommand(
        [
            new FakeProjectInitializer("Python"),
            new FakeProjectInitializer("dotnet"),
            new FakeProjectInitializer("node"),
        ]);
        string description = cmd.StackOption.Description ?? string.Empty;

        description.Should().Contain("Supported values: dotnet, node, Python.");
    }

    [Fact]
    public void InitCommand_HelpFooterHint_PointsAtWorkloadSearch()
    {
        var cmd = CreateCommand([]);

        (cmd.GetHelpFooterHint() ?? string.Empty).Should().Contain("func workload search --stack");
    }

    [Fact]
    public void InitCommand_RegisteredInParser()
    {
        var root = TestParser.CreateRoot(_interaction);
        var names = root.Subcommands.Select(c => c.Name).ToList();

        names.Should().Contain("init");
    }

    [Fact]
    public void InitCommand_HasPathArgument()
    {
        var cmd = CreateCommand([]);
        cmd.Arguments.Should().ContainSingle();
        cmd.Arguments[0].Name.Should().Be("path");
    }

    [Fact]
    public async Task InitCommand_NoInstalledStacks_DoesNotCreateDirectory()
    {
        var newDir = Path.Combine(Path.GetTempPath(), $"func-init-{Guid.NewGuid():N}");
        Directory.Exists(newDir).Should().BeFalse();

        try
        {
            var root = TestParser.CreateRoot(_interaction);
            var result = root.Parse($"init \"{newDir}\"");

            var exitCode = await result.InvokeAsync(cancellationToken: TestContext.Current.CancellationToken);

            exitCode.Should().Be(1);
            Directory.Exists(newDir).Should().BeFalse();
        }
        finally
        {
            if (Directory.Exists(newDir))
            {
                Directory.Delete(newDir, recursive: true);
            }
        }
    }

    [Fact]
    public async Task InitCommand_UsesDefaultTemplateAndBindsHostOptionAliases()
    {
        string root = Path.Combine(Path.GetTempPath(), $"func-init-template-{Guid.NewGuid():N}");
        string home = Path.Combine(root, "home");
        string output = Path.Combine(root, "App");
        try
        {
            string package = TemplateFixtures.WriteInitProjectPackage(root);
            await TemplateFixtures.InstallAsync(package, Path.Combine(home, "templates"));
            FakeProjectInitializer initializer = new("python", [_python]);

            int exitCode = await RunInitAsync(
                output,
                initializer,
                language: "python",
                stack: "python",
                extraArgs: ["-tfm", "net11.0", "--no-http"],
                templaterFactory: TemplateFixtures.CreateTemplaterFactory(home));

            exitCode.Should().Be(0, string.Join(Environment.NewLine, _interaction.Lines));
            initializer.WasInvoked.Should().BeFalse();
            File.ReadAllText(Path.Combine(output, "App.txt")).Should().Be("net11.0");
            File.Exists(Path.Combine(output, "Http.txt")).Should().BeFalse();
            AssertConfigJsonHasShape(output, expectedStack: "python", expectedLanguage: null);
        }
        finally
        {
            CleanupDirectory(root);
        }
    }

    [Fact]
    public async Task InitCommand_NoTelemetryAliasDisablesTelemetryInGeneratedContent()
    {
        string root = Path.Combine(Path.GetTempPath(), $"func-init-template-{Guid.NewGuid():N}");
        string home = Path.Combine(root, "home");
        string output = Path.Combine(root, "App");
        try
        {
            string package = TemplateFixtures.WriteInitProjectPackage(root);
            await TemplateFixtures.InstallAsync(package, Path.Combine(home, "templates"));
            FakeProjectInitializer initializer = new("python", [_python]);

            int exitCode = await RunInitAsync(
                output,
                initializer,
                language: "python",
                stack: "python",
                extraArgs: ["--no-telemetry"],
                templaterFactory: TemplateFixtures.CreateTemplaterFactory(home));

            exitCode.Should().Be(0);
            initializer.WasInvoked.Should().BeFalse();
            string generatedProgram = File.ReadAllText(Path.Combine(output, "Program.cs"));
            generatedProgram.Should().Contain("FunctionsApplication.CreateBuilder");
            generatedProgram.Should().NotContain("AddOpenTelemetry");
            AssertConfigJsonHasShape(output, expectedStack: "python", expectedLanguage: null);
        }
        finally
        {
            CleanupDirectory(root);
        }
    }

    [Fact]
    public async Task InitCommand_UnknownTemplateOptionPreservesExistingDirectory()
    {
        string root = Path.Combine(Path.GetTempPath(), $"func-init-template-{Guid.NewGuid():N}");
        string home = Path.Combine(root, "home");
        string output = Path.Combine(root, "App");
        string sentinel = Path.Combine(output, "keep.txt");
        Directory.CreateDirectory(output);
        File.WriteAllText(sentinel, "original content");
        try
        {
            string package = TemplateFixtures.WriteInitProjectPackage(root);
            await TemplateFixtures.InstallAsync(package, Path.Combine(home, "templates"));
            FakeProjectInitializer initializer = new("python", [_python]);

            int exitCode = await RunInitAsync(
                output,
                initializer,
                language: "python",
                stack: "python",
                extraArgs: ["--does-not-exist"],
                templaterFactory: TemplateFixtures.CreateTemplaterFactory(home));

            exitCode.Should().Be(1);
            initializer.WasInvoked.Should().BeFalse();
            File.ReadAllText(sentinel).Should().Be("original content");
            File.Exists(Path.Combine(output, ".func", "config.json")).Should().BeFalse();
            _interaction.Lines.Should().Contain(line => line.Contains("Unrecognized option '--does-not-exist'."));
        }
        finally
        {
            CleanupDirectory(root);
        }
    }

    [Fact]
    public async Task InitCommand_SelectsDefaultWhenAlternativeIsEnumeratedFirst()
    {
        string root = Path.Combine(Path.GetTempPath(), $"func-init-template-{Guid.NewGuid():N}");
        string home = Path.Combine(root, "home");
        string output = Path.Combine(root, "App");
        try
        {
            string package = TemplateFixtures.WriteInitProjectPackage(
                root,
                alternateShortName: "a-quickstart-alternative",
                alternateIdentity: "A.Func.Tests.InitProject.Alternative",
                alternateFirst: true);
            await TemplateFixtures.InstallAsync(package, Path.Combine(home, "templates"));
            ITemplaterFactory templaterFactory = TemplateFixtures.CreateTemplaterFactory(home);
            using (Templater templater = templaterFactory.Create(
                new TemplateEngineContext(WorkingDirectory.FromExplicit(root))))
            {
                TemplateResolutionResult candidates = await templater.ListProjectTemplatesAsync(
                    "python", TestContext.Current.CancellationToken);
                candidates.Templates.SelectMany(template => template.ShortNameList)
                    .Should().ContainInOrder("a-quickstart-alternative", "func-python");
            }
            FakeProjectInitializer initializer = new("python", [_python]);

            int exitCode = await RunInitAsync(
                output,
                initializer,
                language: "python",
                stack: "python",
                templaterFactory: templaterFactory);

            exitCode.Should().Be(0);
            initializer.WasInvoked.Should().BeFalse();
            File.ReadAllText(Path.Combine(output, "App.txt")).Should().Be("net10.0");
            File.Exists(Path.Combine(output, "Alternative.txt")).Should().BeFalse();
        }
        finally
        {
            CleanupDirectory(root);
        }
    }

    [Fact]
    public async Task InitCommand_LeavesUnspecifiedTemplateParametersToEngineDefaults()
    {
        string root = Path.Combine(Path.GetTempPath(), $"func-init-template-{Guid.NewGuid():N}");
        string home = Path.Combine(root, "home");
        string output = Path.Combine(root, "App");
        try
        {
            string package = TemplateFixtures.WriteInitProjectPackage(root);
            await TemplateFixtures.InstallAsync(package, Path.Combine(home, "templates"));
            FakeProjectInitializer initializer = new("python", [_python]);

            int exitCode = await RunInitAsync(
                output,
                initializer,
                language: "python",
                stack: "python",
                templaterFactory: TemplateFixtures.CreateTemplaterFactory(home));

            exitCode.Should().Be(0);
            File.ReadAllText(Path.Combine(output, "App.txt")).Should().Be("net10.0");
            File.Exists(Path.Combine(output, "Http.txt")).Should().BeTrue();
        }
        finally
        {
            CleanupDirectory(root);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task InitCommand_InvalidTemplateParameterDoesNotModifyTarget(bool force)
    {
        string root = Path.Combine(Path.GetTempPath(), $"func-init-template-{Guid.NewGuid():N}");
        string home = Path.Combine(root, "home");
        string output = Path.Combine(root, "App");
        string sentinel = Path.Combine(output, "keep.txt");
        if (force)
        {
            Directory.CreateDirectory(output);
            File.WriteAllText(sentinel, "original");
        }
        try
        {
            string package = TemplateFixtures.WriteInitProjectPackage(root);
            await TemplateFixtures.InstallAsync(package, Path.Combine(home, "templates"));
            FakeProjectInitializer initializer = new("python", [_python]);

            int exitCode = await RunInitAsync(
                output,
                initializer,
                language: "python",
                stack: "python",
                force: force,
                extraArgs: ["--target-framework", "invalid"],
                templaterFactory: TemplateFixtures.CreateTemplaterFactory(home));

            exitCode.Should().Be(1, string.Join(Environment.NewLine, _interaction.Lines));
            initializer.WasInvoked.Should().BeFalse();
            if (force)
            {
                File.ReadAllText(sentinel).Should().Be("original");
            }
            else
            {
                Directory.Exists(output).Should().BeFalse();
            }
            File.Exists(Path.Combine(output, ".func", "config.json")).Should().BeFalse();
        }
        finally
        {
            CleanupDirectory(root);
        }
    }

    [Fact]
    public async Task InitCommand_TemplateCandidatesWithoutUniqueDefaultDoNotFallBack()
    {
        string root = Path.Combine(Path.GetTempPath(), $"func-init-template-{Guid.NewGuid():N}");
        string home = Path.Combine(root, "home");
        string output = Path.Combine(root, "App");
        try
        {
            string package = TemplateFixtures.WriteInitProjectPackage(root, shortName: "func-python-other");
            await TemplateFixtures.InstallAsync(package, Path.Combine(home, "templates"));
            FakeProjectInitializer initializer = new("python", [_python]);

            int exitCode = await RunInitAsync(
                output,
                initializer,
                language: "python",
                stack: "python",
                templaterFactory: TemplateFixtures.CreateTemplaterFactory(home));

            exitCode.Should().Be(1);
            initializer.WasInvoked.Should().BeFalse();
            Directory.Exists(output).Should().BeFalse();
            _interaction.Lines.Should().Contain(line => line.Contains("Expected one default project template"));
        }
        finally
        {
            CleanupDirectory(root);
        }
    }

    [Fact]
    public async Task InitCommand_DuplicateDefaultTemplatesDoNotFallBack()
    {
        string root = Path.Combine(Path.GetTempPath(), $"func-init-template-{Guid.NewGuid():N}");
        string home = Path.Combine(root, "home");
        string output = Path.Combine(root, "App");
        try
        {
            string package = TemplateFixtures.WriteInitProjectPackage(
                root,
                alternateShortName: TemplateFixtures.InitProjectTemplateShortName);
            await TemplateFixtures.InstallAsync(package, Path.Combine(home, "templates"));
            FakeProjectInitializer initializer = new("python", [_python]);

            int exitCode = await RunInitAsync(
                output,
                initializer,
                language: "python",
                stack: "python",
                templaterFactory: TemplateFixtures.CreateTemplaterFactory(home));

            exitCode.Should().Be(1);
            initializer.WasInvoked.Should().BeFalse();
            Directory.Exists(output).Should().BeFalse();
            _interaction.Lines.Should().Contain(line => line.Contains("found 2"));
        }
        finally
        {
            CleanupDirectory(root);
        }
    }

    [Fact]
    public async Task InitCommand_TemplateAliasesCannotShadowRecursiveGlobalOptions()
    {
        string root = Path.Combine(Path.GetTempPath(), $"func-init-template-{Guid.NewGuid():N}");
        string home = Path.Combine(root, "home");
        string output = Path.Combine(root, "App");
        try
        {
            string package = TemplateFixtures.WriteInitProjectPackage(root, targetFrameworkAlias: "verbose");
            await TemplateFixtures.InstallAsync(package, Path.Combine(home, "templates"));
            FakeProjectInitializer initializer = new("python", [_python]);
            var command = CreateCommand([initializer], TemplateFixtures.CreateTemplaterFactory(home));
            RootCommand rootCommand = [command];
            rootCommand.Options.Add(new Option<bool>("--verbose") { Recursive = true });

            int exitCode = await rootCommand.Parse(
                ["init", output, "--language", "python", "--stack", "python", "--verbose"])
                .InvokeAsync(cancellationToken: TestContext.Current.CancellationToken);

            exitCode.Should().Be(1);
            initializer.WasInvoked.Should().BeFalse();
            Directory.Exists(output).Should().BeFalse();
            _interaction.Lines.Should().Contain(line => line.Contains("conflicts with a built-in option"));
        }
        finally
        {
            CleanupDirectory(root);
        }
    }

    [Theory]
    [InlineData("-fw")]
    [InlineData("--target-framework")]
    public async Task InitCommand_EveryTemplateAliasBindsWhenStackOptionPartiallyOverlaps(string alias)
    {
        string root = Path.Combine(Path.GetTempPath(), $"func-init-template-{Guid.NewGuid():N}");
        string home = Path.Combine(root, "home");
        string output = Path.Combine(root, "App");
        try
        {
            string package = TemplateFixtures.WriteInitProjectPackage(root, targetFrameworkShortName: "fw");
            await TemplateFixtures.InstallAsync(package, Path.Combine(home, "templates"));
            FakeProjectInitializer initializer = CreateInitializerWithTargetFrameworkOption();

            int exitCode = await RunInitAsync(
                output,
                initializer,
                language: "python",
                stack: "python",
                extraArgs: [alias, "net11.0"],
                templaterFactory: TemplateFixtures.CreateTemplaterFactory(home));

            exitCode.Should().Be(0, string.Join(Environment.NewLine, _interaction.Lines));
            initializer.WasInvoked.Should().BeFalse();
            File.ReadAllText(Path.Combine(output, "App.txt")).Should().Be("net11.0");
        }
        finally
        {
            CleanupDirectory(root);
        }
    }

    // '--target-framework' and '-tfm' are also registered by the stack; '-fw' and '--no-http' are template-only.
    [Theory]
    [InlineData("tfm", new[] { "--target-framework", "net11.0" })]
    [InlineData("tfm", new[] { "-tfm", "net11.0" })]
    [InlineData("fw", new[] { "-fw", "net11.0" })]
    [InlineData("tfm", new[] { "--no-http" })]
    public async Task InitCommand_PathBeforeTemplateOptionsCreatesProject(string templateShortName, string[] options)
    {
        string root = Path.Combine(Path.GetTempPath(), $"func-init-template-{Guid.NewGuid():N}");
        string home = Path.Combine(root, "home");
        string output = Path.Combine(root, "App");
        try
        {
            string package = TemplateFixtures.WriteInitProjectPackage(
                root, targetFrameworkShortName: templateShortName);
            await TemplateFixtures.InstallAsync(package, Path.Combine(home, "templates"));
            FakeProjectInitializer initializer = CreateInitializerWithTargetFrameworkOption();
            RootCommand rootCommand = [CreateCommand([initializer], TemplateFixtures.CreateTemplaterFactory(home))];

            int exitCode = await rootCommand
                .Parse(["init", output, .. options, "--stack", "python", "--language", "python"])
                .InvokeAsync(cancellationToken: TestContext.Current.CancellationToken);

            exitCode.Should().Be(0, string.Join(Environment.NewLine, _interaction.Lines));
            initializer.WasInvoked.Should().BeFalse();
            bool noHttp = options.Contains("--no-http");
            File.ReadAllText(Path.Combine(output, "App.txt")).Should().Be(noHttp ? "net10.0" : "net11.0");
            File.Exists(Path.Combine(output, "Http.txt")).Should().Be(!noHttp);
        }
        finally
        {
            CleanupDirectory(root);
        }
    }

    [Theory]
    [InlineData("tfm", new[] { "--target-framework", "net11.0" }, null)]
    [InlineData("tfm", new[] { "-tfm", "net11.0" }, null)]
    [InlineData("fw", new[] { "-fw", "net11.0" }, null)]
    [InlineData("tfm", new[] { "--no-http" }, "--no-http")]
    public async Task InitCommand_PathAfterTemplateOptionIsRejectedBeforeCreation(
        string templateShortName, string[] options, string? booleanOption)
    {
        string root = Path.Combine(Path.GetTempPath(), $"func-init-template-{Guid.NewGuid():N}");
        string home = Path.Combine(root, "home");
        string output = Path.Combine(root, "App");
        try
        {
            string package = TemplateFixtures.WriteInitProjectPackage(
                root, targetFrameworkShortName: templateShortName);
            await TemplateFixtures.InstallAsync(package, Path.Combine(home, "templates"));
            FakeProjectInitializer initializer = CreateInitializerWithTargetFrameworkOption();
            RootCommand rootCommand = [CreateCommand([initializer], TemplateFixtures.CreateTemplaterFactory(home))];

            int exitCode = await rootCommand
                .Parse(["init", .. options, output, "--stack", "python", "--language", "python"])
                .InvokeAsync(cancellationToken: TestContext.Current.CancellationToken);

            // A Boolean option may take an inline value, so the path after it is reported as that option's value.
            exitCode.Should().Be(1);
            _interaction.Lines.Should().Equal(booleanOption is null
                ? $"ERROR: Unrecognized argument '{output}'. Specify the project path before template or stack options."
                : $"ERROR: Invalid value '{output}' for option '{booleanOption}'. Expected 'true' or 'false'.");
            Directory.Exists(output).Should().BeFalse();
            initializer.WasInvoked.Should().BeFalse();
            initializer.ConfigureCount.Should().Be(0);
        }
        finally
        {
            CleanupDirectory(root);
        }
    }

    [Fact]
    public async Task InitCommand_LegacyScaffolding_PathAfterStackOptionIsRejectedBeforeCreation()
    {
        string root = Path.Combine(Path.GetTempPath(), $"func-init-{Guid.NewGuid():N}");
        string output = Path.Combine(root, "App");
        try
        {
            FakeProjectInitializer initializer = CreateInitializerWithTargetFrameworkOption();
            RootCommand rootCommand = [CreateCommand([initializer])];

            int exitCode = await rootCommand.Parse(
                ["init", "--target-framework", "net11.0", output, "--stack", "python", "--language", "python"])
                .InvokeAsync(cancellationToken: TestContext.Current.CancellationToken);

            exitCode.Should().Be(1);
            _interaction.Lines.Should().Equal(
                $"ERROR: Unrecognized argument '{output}'. Specify the project path before template or stack options.");
            Directory.Exists(output).Should().BeFalse();
            initializer.WasInvoked.Should().BeFalse();
        }
        finally
        {
            CleanupDirectory(root);
        }
    }

    [Fact]
    public async Task InitCommand_StackOptionArityDoesNotRejectTemplateBooleanAlias()
    {
        string root = Path.Combine(Path.GetTempPath(), $"func-init-template-{Guid.NewGuid():N}");
        string home = Path.Combine(root, "home");
        string output = Path.Combine(root, "App");
        try
        {
            string package = TemplateFixtures.WriteInitProjectPackage(root);
            await TemplateFixtures.InstallAsync(package, Path.Combine(home, "templates"));
            FakeProjectInitializer initializer = CreateInitializerWithRequiredStringNoHttpOption();

            int exitCode = await RunInitAsync(
                output,
                initializer,
                language: "python",
                stack: "python",
                extraArgs: ["--no-http"],
                templaterFactory: TemplateFixtures.CreateTemplaterFactory(home));

            exitCode.Should().Be(0, string.Join(Environment.NewLine, _interaction.Lines));
            initializer.WasInvoked.Should().BeFalse();
            File.Exists(Path.Combine(output, "App.txt")).Should().BeTrue();
            File.Exists(Path.Combine(output, "Http.txt")).Should().BeFalse();
        }
        finally
        {
            CleanupDirectory(root);
        }
    }

    [Fact]
    public async Task InitCommand_LegacyScaffolding_KeepsTypedStackOptionValidation()
    {
        string path = Path.Combine(Path.GetTempPath(), $"func-init-{Guid.NewGuid():N}");
        try
        {
            FakeProjectInitializer initializer = CreateInitializerWithRequiredStringNoHttpOption();

            int exitCode = await RunInitAsync(
                path, initializer, language: "python", stack: "python", extraArgs: ["--no-http"]);

            exitCode.Should().Be(1);
            _interaction.Lines.Should().ContainSingle().Which.Should().StartWith("ERROR:").And.Contain("'--no-http'");
            Directory.Exists(path).Should().BeFalse();
            initializer.WasInvoked.Should().BeFalse();
        }
        finally
        {
            CleanupDirectory(path);
        }
    }

    [Fact]
    public async Task InitCommand_LegacyScaffolding_BindsTypedStackOptionValue()
    {
        string path = Path.Combine(Path.GetTempPath(), $"func-init-{Guid.NewGuid():N}");
        try
        {
            string? observed = null;
            FakeProjectInitializer initializer = new(
                "python",
                [_python],
                optionFactory: registry => [registry.GetOrAdd(new Option<string>("--no-http") { Required = true })],
                onInitialize: (init, parseResult) =>
                    observed = parseResult.GetValue((Option<string>)init.ContributedOptions[0]));

            int exitCode = await RunInitAsync(
                path, initializer, language: "python", stack: "python", extraArgs: ["--no-http=disabled"]);

            exitCode.Should().Be(0, string.Join(Environment.NewLine, _interaction.Lines));
            observed.Should().Be("disabled");
        }
        finally
        {
            CleanupDirectory(path);
        }
    }

    [Fact]
    public async Task InitCommand_LegacyShortOptionRequiresSeparateChannelValue()
    {
        string output = Path.Combine(Path.GetTempPath(), $"func-init-{Guid.NewGuid():N}");
        try
        {
            BundleChannel? observed = null;
            FakeProjectInitializer initializer = new(
                "python", [_python],
                optionFactory: registry => [registry.GetOrAdd(CommonInitOptions.BundlesChannel())],
                onInitialize: (init, parse) =>
                    observed = parse.GetValue((Option<BundleChannel>)init.ContributedOptions.Single()));

            int combined = await RunInitAsync(
                output, initializer, language: "python", stack: "python", extraArgs: ["-cPreview"]);

            combined.Should().Be(1);
            _interaction.Lines.Should().Equal("ERROR: Unrecognized option '-cPreview'.");
            Directory.Exists(output).Should().BeFalse();
            initializer.WasInvoked.Should().BeFalse();

            int separate = await RunInitAsync(
                output, initializer, language: "python", stack: "python", extraArgs: ["-c", "Preview"]);

            separate.Should().Be(0);
            observed.Should().Be(BundleChannel.Preview);
        }
        finally
        {
            CleanupDirectory(output);
        }
    }

    [Fact]
    public async Task InitCommand_TemplateShortOptionsMustBePassedSeparately()
    {
        string root = Path.Combine(Path.GetTempPath(), $"func-init-template-{Guid.NewGuid():N}");
        string home = Path.Combine(root, "home");
        string output = Path.Combine(root, "App");
        try
        {
            await TemplateFixtures.InstallAsync(
                TemplateFixtures.WriteInitProjectPackage(root), Path.Combine(home, "templates"));
            ITemplaterFactory factory = TemplateFixtures.CreateTemplaterFactory(home);
            FakeProjectInitializer initializer = new("python", [_python]);

            int combined = await RunInitAsync(output, initializer, language: "python", stack: "python",
                extraArgs: ["-xy"], templaterFactory: factory);

            combined.Should().Be(1);
            _interaction.Lines.Should().Equal("ERROR: Unrecognized option '-xy'.");
            Directory.Exists(output).Should().BeFalse();
            initializer.WasInvoked.Should().BeFalse();

            int separate = await RunInitAsync(output, initializer, language: "python", stack: "python",
                extraArgs: ["-x", "-y"], templaterFactory: factory);

            separate.Should().Be(0);
            File.Exists(Path.Combine(output, "Http.txt")).Should().BeFalse();
            File.ReadAllText(Path.Combine(output, "Program.cs")).Should().NotContain("AddOpenTelemetry");
        }
        finally
        {
            CleanupDirectory(root);
        }
    }

    [Fact]
    public async Task InitCommand_TemplateOnlySiblingAliasBindsWhenStackRegistersOtherOptions()
    {
        string root = Path.Combine(Path.GetTempPath(), $"func-init-template-{Guid.NewGuid():N}");
        string home = Path.Combine(root, "home");
        string output = Path.Combine(root, "App");
        try
        {
            string package = TemplateFixtures.WriteInitProjectPackage(root);
            await TemplateFixtures.InstallAsync(package, Path.Combine(home, "templates"));
            FakeProjectInitializer initializer = CreateInitializerWithTargetFrameworkOption();

            int exitCode = await RunInitAsync(
                output,
                initializer,
                language: "python",
                stack: "python",
                extraArgs: ["--target-framework", "net11.0", "--no-telemetry"],
                templaterFactory: TemplateFixtures.CreateTemplaterFactory(home));

            exitCode.Should().Be(0, string.Join(Environment.NewLine, _interaction.Lines));
            initializer.WasInvoked.Should().BeFalse();
            File.ReadAllText(Path.Combine(output, "App.txt")).Should().Be("net11.0");
            File.ReadAllText(Path.Combine(output, "Program.cs")).Should().NotContain("AddOpenTelemetry");
        }
        finally
        {
            CleanupDirectory(root);
        }
    }

    [Theory]
    [InlineData("--no-http=garbage")]
    [InlineData("--no-telemetry=garbage")]
    public async Task InitCommand_InvalidInlineTemplateBooleanValueIsReportedBeforeCreation(string option)
    {
        string root = Path.Combine(Path.GetTempPath(), $"func-init-template-{Guid.NewGuid():N}");
        string home = Path.Combine(root, "home");
        string output = Path.Combine(root, "App");
        try
        {
            string package = TemplateFixtures.WriteInitProjectPackage(root);
            await TemplateFixtures.InstallAsync(package, Path.Combine(home, "templates"));
            FakeProjectInitializer initializer = new("python", [_python]);

            int exitCode = await RunInitAsync(
                output,
                initializer,
                language: "python",
                stack: "python",
                extraArgs: [option],
                templaterFactory: TemplateFixtures.CreateTemplaterFactory(home));

            exitCode.Should().Be(1);
            string name = option[..option.IndexOf('=')];
            _interaction.Lines.Should().Equal(
                $"ERROR: Invalid value 'garbage' for option '{name}'. Expected 'true' or 'false'.");
            Directory.Exists(output).Should().BeFalse();
            initializer.WasInvoked.Should().BeFalse();
            initializer.ConfigureCount.Should().Be(0);
        }
        finally
        {
            CleanupDirectory(root);
        }
    }

    [Fact]
    public async Task InitCommand_LegacyScaffolding_InvalidInlineStackBooleanValueIsReportedBeforeCreation()
    {
        string path = Path.Combine(Path.GetTempPath(), $"func-init-{Guid.NewGuid():N}");
        try
        {
            FakeProjectInitializer initializer = new(
                "python", [_python], optionFactory: registry => [registry.GetOrAdd(new Option<bool>("--no-bundles"))]);

            int exitCode = await RunInitAsync(
                path, initializer, language: "python", stack: "python", extraArgs: ["--no-bundles=garbage"]);

            exitCode.Should().Be(1);
            _interaction.Lines.Should().Equal(
                "ERROR: Invalid value 'garbage' for option '--no-bundles'. Expected 'true' or 'false'.");
            Directory.Exists(path).Should().BeFalse();
            initializer.WasInvoked.Should().BeFalse();
        }
        finally
        {
            CleanupDirectory(path);
        }
    }

    [Fact]
    public void InitCommand_StackOptionsAreDocumentedButNotParsedByPublicCommand()
    {
        var cmd = CreateCommand([CreateInitializerWithTargetFrameworkOption()]);

        cmd.Options.Should().NotContain(option => option.Name == "--target-framework");
        cmd.GetHelpOptions().Should().ContainSingle(option => option.Name == "--target-framework");
        cmd.GetHelpOptions().Select(option => option.Name)
            .Should().Contain(["--stack", "--language", "--name", "--force"]);
    }

    [Fact]
    public async Task InitCommand_StackOnlyAliasIsRejectedByTemplate()
    {
        string root = Path.Combine(Path.GetTempPath(), $"func-init-template-{Guid.NewGuid():N}");
        string home = Path.Combine(root, "home");
        string output = Path.Combine(root, "App");
        try
        {
            string package = TemplateFixtures.WriteInitProjectPackage(root, targetFrameworkShortName: "fw");
            await TemplateFixtures.InstallAsync(package, Path.Combine(home, "templates"));
            FakeProjectInitializer initializer = CreateInitializerWithTargetFrameworkOption();

            int exitCode = await RunInitAsync(
                output,
                initializer,
                language: "python",
                stack: "python",
                extraArgs: ["-tfm", "net11.0"],
                templaterFactory: TemplateFixtures.CreateTemplaterFactory(home));

            exitCode.Should().Be(1);
            initializer.WasInvoked.Should().BeFalse();
            Directory.Exists(output).Should().BeFalse();
            _interaction.Lines.Should().Equal("ERROR: Unrecognized option '-tfm'.");
        }
        finally
        {
            CleanupDirectory(root);
        }
    }

    [Theory]
    [InlineData("-fw", "--target-framework")]
    [InlineData("--target-framework", "-fw")]
    [InlineData("-fw", "-fw")]
    public async Task InitCommand_TemplateParameterAssignedTwiceIsRejected(string first, string second)
    {
        string root = Path.Combine(Path.GetTempPath(), $"func-init-template-{Guid.NewGuid():N}");
        string home = Path.Combine(root, "home");
        string output = Path.Combine(root, "App");
        try
        {
            string package = TemplateFixtures.WriteInitProjectPackage(root, targetFrameworkShortName: "fw");
            await TemplateFixtures.InstallAsync(package, Path.Combine(home, "templates"));
            FakeProjectInitializer initializer = CreateInitializerWithTargetFrameworkOption();

            int exitCode = await RunInitAsync(
                output,
                initializer,
                language: "python",
                stack: "python",
                extraArgs: [first, "net10.0", second, "net11.0"],
                templaterFactory: TemplateFixtures.CreateTemplaterFactory(home));

            exitCode.Should().Be(1);
            initializer.WasInvoked.Should().BeFalse();
            Directory.Exists(output).Should().BeFalse();
            _interaction.Lines.Should().ContainSingle(line => line.Contains("was specified more than once."));
        }
        finally
        {
            CleanupDirectory(root);
        }
    }

    [Fact]
    public async Task InitCommand_TemplateCreation_RunsStackSetupBeforeConfigurationAndSuccess()
    {
        string root = Path.Combine(Path.GetTempPath(), $"func-init-template-{Guid.NewGuid():N}");
        string home = Path.Combine(root, "home");
        string output = Path.Combine(root, "App");
        try
        {
            string package = TemplateFixtures.WriteInitProjectPackage(root);
            await TemplateFixtures.InstallAsync(package, Path.Combine(home, "templates"));
            bool? projectCreatedBeforeSetup = null;
            bool? configurationWrittenBeforeSetup = null;
            int linesBeforeSetup = -1;
            FakeProjectInitializer initializer = new("python", [_python], onConfigure: context =>
            {
                projectCreatedBeforeSetup = File.Exists(Path.Combine(output, "App.txt"));
                configurationWrittenBeforeSetup = File.Exists(Path.Combine(output, ".func", "config.json"));
                linesBeforeSetup = _interaction.Lines.Count;
                context.WorkingDirectory.Info.FullName.Should().Be(Path.GetFullPath(output));
                context.Language.Should().Be(_python);
            });

            int exitCode = await RunInitAsync(
                output,
                initializer,
                language: "python",
                stack: "python",
                templaterFactory: TemplateFixtures.CreateTemplaterFactory(home));

            exitCode.Should().Be(0, string.Join(Environment.NewLine, _interaction.Lines));
            initializer.WasInvoked.Should().BeFalse();
            initializer.ConfigureCount.Should().Be(1);
            projectCreatedBeforeSetup.Should().BeTrue();
            configurationWrittenBeforeSetup.Should().BeFalse();
            File.Exists(Path.Combine(output, ".func", "config.json")).Should().BeTrue();
            _interaction.Lines.Take(linesBeforeSetup)
                .Should().NotContain(line => line.Contains("Project initialized for"));
            _interaction.Lines.Skip(linesBeforeSetup)
                .Should().Contain(line => line.Contains("Project initialized for"));
        }
        finally
        {
            CleanupDirectory(root);
        }
    }

    [Fact]
    public async Task InitCommand_TemplateCreation_StackSetupFailureDoesNotReportSuccess()
    {
        string root = Path.Combine(Path.GetTempPath(), $"func-init-template-{Guid.NewGuid():N}");
        string home = Path.Combine(root, "home");
        string output = Path.Combine(root, "App");
        try
        {
            string package = TemplateFixtures.WriteInitProjectPackage(root);
            await TemplateFixtures.InstallAsync(package, Path.Combine(home, "templates"));
            FakeProjectInitializer initializer = new(
                "python", [_python], onConfigure: _ => throw new UnauthorizedAccessException("setup failed"));

            int exitCode = await RunInitAsync(
                output,
                initializer,
                language: "python",
                stack: "python",
                templaterFactory: TemplateFixtures.CreateTemplaterFactory(home));

            exitCode.Should().Be(1);
            initializer.ConfigureCount.Should().Be(1);
            _interaction.Lines.Should().NotContain(line => line.Contains("Project initialized for"));
        }
        finally
        {
            CleanupDirectory(root);
        }
    }

    [Fact]
    public async Task InitCommand_LegacyScaffolding_DoesNotRunTemplateStackSetup()
    {
        string path = Path.Combine(Path.GetTempPath(), $"func-init-{Guid.NewGuid():N}");
        try
        {
            FakeProjectInitializer python = new("python", [_python]);

            int exitCode = await RunInitAsync(path, python, language: "python", stack: "python");

            exitCode.Should().Be(0, string.Join(Environment.NewLine, _interaction.Lines));
            python.WasInvoked.Should().BeTrue();
            python.ConfigureCount.Should().Be(0);
        }
        finally
        {
            CleanupDirectory(path);
        }
    }

    [Fact]
    public async Task InitCommand_TemplateCreation_SetupThatRequestsCancellationDoesNotReportSuccess()
    {
        string root = Path.Combine(Path.GetTempPath(), $"func-init-template-{Guid.NewGuid():N}");
        string home = Path.Combine(root, "home");
        string output = Path.Combine(root, "App");
        try
        {
            string package = TemplateFixtures.WriteInitProjectPackage(root);
            await TemplateFixtures.InstallAsync(package, Path.Combine(home, "templates"));
            using CancellationTokenSource cancellation = new();
            FakeProjectInitializer initializer = new("python", [_python], onConfigure: _ => cancellation.Cancel());
            RootCommand rootCommand = [CreateCommand([initializer], TemplateFixtures.CreateTemplaterFactory(home))];

            Func<Task> invoke = () => rootCommand
                .Parse(["init", output, "--stack", "python", "--language", "python"])
                .InvokeAsync(new InvocationConfiguration { EnableDefaultExceptionHandler = false }, cancellation.Token);

            await invoke.Should().ThrowAsync<OperationCanceledException>();
            initializer.ConfigureCount.Should().Be(1);
            initializer.WasInvoked.Should().BeFalse();
            File.Exists(Path.Combine(output, "App.txt")).Should().BeTrue();
            File.Exists(Path.Combine(output, ".func", "config.json")).Should().BeFalse();
            _interaction.Lines.Should().NotContain(line => line.Contains("Project initialized for"));
        }
        finally
        {
            CleanupDirectory(root);
        }
    }

    [Theory]
    [InlineData("C#")]
    [InlineData("c#")]
    [InlineData("csharp")]
    public async Task InitCommand_DotNetStack_CreatesHttpProjectFromShippedTemplate(string language)
    {
        string root = Path.Combine(Path.GetTempPath(), $"func-init-dotnet-{Guid.NewGuid():N}");
        string output = Path.Combine(root, "MyFunctions");
        RuntimeWorkloadInfo dotnet = TestWorkloads.CreateInfo("Azure.Functions.Cli.Workloads.DotNet") with
        {
            Instance = new DotNetWorkload(new StackDescriptor("dotnet", "dotnet-isolated", ".NET")),
            TemplateRoot = Path.Combine(AppContext.BaseDirectory, "content", "templates"),
        };
        IWorkloadProvider workloads = Substitute.For<IWorkloadProvider>();
        workloads.GetRuntimeWorkloads().Returns([dotnet]);
        object? dotnetCli = null;
        ITemplaterFactory? templaterFactory = null;
        try
        {
            FuncRootCommand rootCommand = TestParser.CreateRootWithWorkload(_interaction, dotnet, builder =>
            {
                dotnet.Instance.Configure(builder);

                // The stack's legacy scaffolder shells out to 'dotnet new'; record any use instead.
                Type dotnetCliType = builder.Services
                    .Single(service => service.ServiceType.Name == "IDotnetCliRunner").ServiceType;
                dotnetCli = Substitute.For([dotnetCliType], []);
                builder.Services.AddSingleton(dotnetCliType, dotnetCli);
                builder.Services.AddSingleton(workloads);
                builder.Services.AddSingleton(_projectResolver);
                builder.Services.AddSingleton(new CliConfigurationPathsOptions(Path.Combine(root, "home")));
                builder.Services.AddTemplating();
                builder.Services.AddSingleton<ITemplaterFactory>(services =>
                    templaterFactory = ActivatorUtilities.CreateInstance<TemplaterFactory>(services));
            });

            int exitCode = await rootCommand
                .Parse(["init", output, "--stack", "dotnet", "--language", language, "--target-framework", "net11.0"])
                .InvokeAsync(cancellationToken: TestContext.Current.CancellationToken);

            exitCode.Should().Be(0, string.Join(Environment.NewLine, _interaction.Lines));
            dotnetCli!.ReceivedCalls().Should().BeEmpty();
            var workingDirectory = WorkingDirectory.FromExplicit(output);
            using (Templater templater = templaterFactory!.Create(new TemplateEngineContext(
                workingDirectory, new TemplateEngineProjectContext(workingDirectory, "dotnet", "C#"))))
            {
                IReadOnlyList<FuncTemplateInfo> candidates =
                    (await templater.ListProjectTemplatesAsync("C#", TestContext.Current.CancellationToken)).Templates;
                candidates.Should().ContainSingle(template => template.ShortNameList.Contains("func-dotnet"))
                    .Which.Identity.Should().Be("Azure.Functions.Cli.DotNet.Project.CSharp");
            }

            string project = File.ReadAllText(Path.Combine(output, "MyFunctions.csproj"));
            project.Should().Contain("<TargetFramework>net11.0</TargetFramework>");
            project.Should().Contain("Microsoft.Azure.Functions.Worker.Extensions.Http.AspNetCore");
            File.ReadAllText(Path.Combine(output, "HelloWorld.cs")).Should()
                .Contain("namespace MyFunctions;").And.Contain("[HttpTrigger(");
            File.Exists(Path.Combine(output, "Program.cs")).Should().BeTrue();
            File.Exists(Path.Combine(output, "host.json")).Should().BeTrue();
            File.Exists(Path.Combine(output, "local.settings.json")).Should().BeTrue();
            Directory.Exists(Path.Combine(output, ".template.config")).Should().BeFalse();
            AssertConfigJsonHasShape(output, "dotnet", "csharp");
            _interaction.Lines.Should().Contain(line => line.Contains("Project initialized for"));
        }
        finally
        {
            CleanupDirectory(root);
        }
    }

    private static FakeProjectInitializer CreateInitializerWithTargetFrameworkOption() =>
        new(
            "python",
            [_python],
            optionFactory: registry => [registry.GetOrAdd(new Option<string>("--target-framework", "-tfm"))]);

    private static FakeProjectInitializer CreateInitializerWithStackOptions(
        string stack, LanguageDescriptor[] languages) =>
        new(
            stack,
            languages,
            optionFactory: registry =>
            [
                registry.GetOrAdd(new Option<string>("--target-framework", "-tfm")),
                registry.GetOrAdd(new Option<bool>("--no-bundles")),
            ]);

    // Same spelling as the fixture template's Boolean '--no-http', but the stack requires a value.
    private static FakeProjectInitializer CreateInitializerWithRequiredStringNoHttpOption() =>
        new(
            "python",
            [_python],
            optionFactory: registry => [registry.GetOrAdd(new Option<string>("--no-http") { Required = true })]);

    [Fact]
    public async Task InitCommand_TemplateCreation_DeclinedForceDoesNotCreateProject()
    {
        string root = Path.Combine(Path.GetTempPath(), $"func-init-template-{Guid.NewGuid():N}");
        string home = Path.Combine(root, "home");
        string output = Path.Combine(root, "App");
        try
        {
            string package = TemplateFixtures.WriteInitProjectPackage(root);
            await TemplateFixtures.InstallAsync(package, Path.Combine(home, "templates"));
            Directory.CreateDirectory(output);
            string sentinel = Path.Combine(output, "keep.txt");
            File.WriteAllText(sentinel, "original content");
            InteractiveTestInteractionService interaction = new();
            FakeProjectInitializer initializer = new("python", [_python]);
            RootCommand rootCommand =
            [
                CreateCommand([initializer], TemplateFixtures.CreateTemplaterFactory(home), interaction),
            ];

            int exitCode = await rootCommand.Parse(
                ["init", output, "--stack", "python", "--language", "python", "--force"])
                .InvokeAsync(cancellationToken: TestContext.Current.CancellationToken);

            exitCode.Should().Be(1);
            interaction.Lines.Should().Contain(line => line.StartsWith("CONFIRM:"));
            Directory.GetFileSystemEntries(output).Should().Equal(sentinel);
            File.ReadAllText(sentinel).Should().Be("original content");
            initializer.WasInvoked.Should().BeFalse();
            initializer.ConfigureCount.Should().Be(0);
            interaction.Lines.Should().NotContain(line => line.Contains("Project initialized for"));
        }
        finally
        {
            CleanupDirectory(root);
        }
    }

    [Fact]
    public async Task InitCommand_TemplateCollisionDoesNotOverwriteExistingFile()
    {
        string root = Path.Combine(Path.GetTempPath(), $"func-init-template-{Guid.NewGuid():N}");
        string home = Path.Combine(root, "home");
        string output = Path.Combine(root, "App");
        Directory.CreateDirectory(output);
        string generatedFile = Path.Combine(output, "Http.txt");
        File.WriteAllText(generatedFile, "user content");
        try
        {
            string package = TemplateFixtures.WriteInitProjectPackage(root);
            await TemplateFixtures.InstallAsync(package, Path.Combine(home, "templates"));
            FakeProjectInitializer initializer = new("python", [_python]);

            int exitCode = await RunInitAsync(
                output,
                initializer,
                language: "python",
                stack: "python",
                templaterFactory: TemplateFixtures.CreateTemplaterFactory(home));

            exitCode.Should().Be(1);
            initializer.WasInvoked.Should().BeFalse();
            File.ReadAllText(generatedFile).Should().Be("user content");
            File.Exists(Path.Combine(output, ".func", "config.json")).Should().BeFalse();
        }
        finally
        {
            CleanupDirectory(root);
        }
    }

    [Fact]
    public async Task InitCommand_TemplateCreation_FileAddedAfterPreflightIsNotOverwritten()
    {
        string root = Path.Combine(Path.GetTempPath(), $"func-init-template-{Guid.NewGuid():N}");
        string home = Path.Combine(root, "home");
        string output = Path.Combine(root, "App");
        try
        {
            string package = TemplateFixtures.WriteInitProjectPackage(
                root, binding: $"{SentinelBindSymbolSource.Prefix}:value");
            await TemplateFixtures.InstallAsync(package, Path.Combine(home, "templates"));
            Directory.CreateDirectory(output);
            string sentinel = Path.Combine(output, "Http.txt");
            byte[] sentinelContent = "user content"u8.ToArray();
            SentinelBindSymbolSource source = new(() => File.WriteAllBytes(sentinel, sentinelContent));
            ICoreTemplateComponent component = Substitute.For<ICoreTemplateComponent>();
            component.InterfaceType.Returns(typeof(IBindSymbolSource));
            component.Instance.Returns(source);
            FakeProjectInitializer initializer = new("python", [_python]);

            int exitCode = await RunInitAsync(
                output,
                initializer,
                language: "python",
                stack: "python",
                templaterFactory: TemplateFixtures.CreateTemplaterFactory(home, [component]));

            exitCode.Should().Be(1);
            Directory.GetFileSystemEntries(output).Should().Equal(sentinel);
            File.ReadAllBytes(sentinel).Should().Equal(sentinelContent);
            File.Exists(Path.Combine(output, ".func", "config.json")).Should().BeFalse();
            _interaction.Lines.Should().Contain(
                "ERROR: Cannot initialize the project because these files already exist: ./Http.txt. "
                + "Use --force to replace the directory contents.");
            initializer.WasInvoked.Should().BeFalse();
            initializer.ConfigureCount.Should().Be(0);
            // Call 1 is the preflight dry run; the sentinel appears during call 2, before creation writes files.
            source.CallCount.Should().Be(2);
        }
        finally
        {
            CleanupDirectory(root);
        }
    }

    [Fact]
    public async Task InitCommand_TemplateCreation_ForceReplacesExistingFiles()
    {
        string root = Path.Combine(Path.GetTempPath(), $"func-init-template-{Guid.NewGuid():N}");
        string home = Path.Combine(root, "home");
        string output = Path.Combine(root, "App");
        try
        {
            string package = TemplateFixtures.WriteInitProjectPackage(root);
            await TemplateFixtures.InstallAsync(package, Path.Combine(home, "templates"));
            Directory.CreateDirectory(output);
            File.WriteAllText(Path.Combine(output, "Http.txt"), "user content");
            File.WriteAllText(Path.Combine(output, "leftover.txt"), "user content");
            FakeProjectInitializer initializer = new("python", [_python]);

            int exitCode = await RunInitAsync(
                output,
                initializer,
                language: "python",
                stack: "python",
                force: true,
                templaterFactory: TemplateFixtures.CreateTemplaterFactory(home));

            exitCode.Should().Be(0);
            File.ReadAllText(Path.Combine(output, "Http.txt")).Should().Be("HTTP function");
            File.ReadAllText(Path.Combine(output, "App.txt")).Should().Be("net10.0");
            File.Exists(Path.Combine(output, "leftover.txt")).Should().BeFalse();
            File.Exists(Path.Combine(output, ".func", "config.json")).Should().BeTrue();
            initializer.WasInvoked.Should().BeFalse();
            initializer.ConfigureCount.Should().Be(1);
        }
        finally
        {
            CleanupDirectory(root);
        }
    }

    [Fact]
    public async Task InitCommand_LegacySetupFailure_LeavesNoConfigAndForceRetrySucceeds()
    {
        using TempDirectory directory = new PhysicalFileSystem().CreateTempDirectory();
        int attempts = 0;
        FakeProjectInitializer python = new("python", [_python], onInitialize: (_, _) =>
        {
            File.WriteAllText(Path.Combine(directory.Path, "function_app.py"), "app");
            if (++attempts == 1)
            {
                throw new IOException("setup failed");
            }
        });

        int exitCode = await RunInitAsync(directory.Path, python, language: "python", stack: "python");

        exitCode.Should().Be(1);
        File.Exists(Path.Combine(directory.Path, "function_app.py")).Should().BeTrue();
        Directory.Exists(Path.Combine(directory.Path, ".func")).Should().BeFalse();
        AssertStartOverReported("ERROR: Couldn't create the python project: ", directory.Path, force: false)
            .Should().Be("ERROR: Couldn't create the python project: setup failed");
        _interaction.Lines.Should().NotContain(line => line.Contains("Project initialized for"));

        // The original command with --force added.
        int retryExitCode = await RunInitAsync(
            directory.Path, python, language: "python", stack: "python", force: true);

        retryExitCode.Should().Be(0, _interaction.AllOutput);
        attempts.Should().Be(2);
        AssertConfigJsonHasShape(directory.Path, expectedStack: "python", expectedLanguage: null);
        _interaction.Lines.Should().Contain(line => line.Contains("Project initialized for"));
    }

    [Fact]
    public async Task InitCommand_LegacySetupFailureWithForce_SaysToRunOriginalCommandAgain()
    {
        using TempDirectory parent = new PhysicalFileSystem().CreateTempDirectory();
        string project = Path.Combine(parent.Path, "cash$ bucket 'q'");
        FakeProjectInitializer node = new(
            "node",
            [_javascript, _typescript],
            optionFactory: r => [r.GetOrAdd(new Option<bool>("--no-bundles"))],
            onInitialize: (_, _) => throw new GracefulException("'dotnet new' failed."));

        int exitCode = await RunInitAsync(
            project, node, language: "ts", stack: "node", force: true, extraArgs: ["--no-bundles"]);

        exitCode.Should().Be(1);
        node.WasInvoked.Should().BeTrue();
        Directory.Exists(Path.Combine(project, ".func")).Should().BeFalse();
        AssertStartOverReported("ERROR: Couldn't create the node project: ", project, force: true)
            .Should().Be("ERROR: Couldn't create the node project: 'dotnet new' failed.");
        _interaction.Lines.Should().NotContain(line => line.Contains("--no-bundles"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task InitCommand_FailureOutput_KeepsLinesWholeAtNarrowConsoleWidth(bool configSaveFails)
    {
        using TempDirectory parent = new PhysicalFileSystem().CreateTempDirectory();
        string project = Path.Combine(parent.Path, "a-long-folder-name-that-would-wrap", "cash$ bucket 'q'");
        using BufferedConsole stdout = new(interactive: false, ansi: false, noColor: true);
        using BufferedConsole stderr = new(interactive: false, ansi: false, noColor: true);
        stdout.Profile.Width = 40;
        stderr.Profile.Width = 40;
        SpectreInteractionService interaction = new(new DefaultTheme(), stdout, stderr);
        FakeProjectInitializer node = new("node", [_javascript, _typescript], onInitialize: (_, _) =>
        {
            if (!configSaveFails)
            {
                throw new IOException("The disk is full.");
            }

            File.WriteAllText(Path.Combine(project, ".func"), "user content");
        });
        RootCommand root = [CreateCommand([node], interaction)];

        int exitCode = await root
            .Parse(["init", project, "--stack", "node", "--language", "js"])
            .InvokeAsync(cancellationToken: TestContext.Current.CancellationToken);

        exitCode.Should().Be(1);
        string[] lines = stdout.Output.Split(Environment.NewLine);
        if (configSaveFails)
        {
            lines.Should().Contain("  func init --stack node --language javascript")
                .And.Contain($"Project folder: {project}");
        }
        else
        {
            lines.Should().Contain($"The project folder may now hold a partly created project: {project}");
        }
    }

    [Fact]
    public async Task InitCommand_LegacySetupCancellation_PropagatesWithoutReportingFailure()
    {
        using TempDirectory directory = new PhysicalFileSystem().CreateTempDirectory();
        FakeProjectInitializer python = new(
            "python", [_python], onInitialize: (_, _) => throw new OperationCanceledException());
        RootCommand rootCommand = [CreateCommand([python])];

        Func<Task> invoke = () => rootCommand
            .Parse(["init", directory.Path, "--stack", "python"])
            .InvokeAsync(new InvocationConfiguration { EnableDefaultExceptionHandler = false });

        await invoke.Should().ThrowAsync<OperationCanceledException>();
        _interaction.Lines.Should().NotContain(line => line.Contains("partly created project"));
        Directory.Exists(Path.Combine(directory.Path, ".func")).Should().BeFalse();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task InitCommand_UnexpectedStackFailure_PropagatesWithoutStartOverAdvice(bool useTemplate)
    {
        string root = Path.Combine(Path.GetTempPath(), $"func-init-unexpected-{Guid.NewGuid():N}");
        string project = Path.Combine(root, "App");
        try
        {
            ITemplaterFactory? factory = await CreateInitTemplaterFactoryAsync(root, useTemplate);
            FakeProjectInitializer python = new(
                "python",
                [_python],
                onInitialize: (_, _) => throw new NullReferenceException(),
                onConfigure: _ => throw new NullReferenceException());
            RootCommand rootCommand = [CreateCommand([python], templaterFactory: factory)];

            Func<Task> invoke = () => rootCommand
                .Parse(["init", project, "--stack", "python"])
                .InvokeAsync(
                    new InvocationConfiguration { EnableDefaultExceptionHandler = false },
                    TestContext.Current.CancellationToken);

            await invoke.Should().ThrowAsync<NullReferenceException>();
            python.WasInvoked.Should().Be(!useTemplate);
            python.ConfigureCount.Should().Be(useTemplate ? 1 : 0);
            _interaction.Lines.Should().NotContain(line => line.Contains("--force") || line.Contains("partly created"));
            File.Exists(Path.Combine(project, ".func", "config.json")).Should().BeFalse();
        }
        finally
        {
            CleanupDirectory(root);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task InitCommand_ProjectFolderCannotBeCreated_ChangesNothing(bool useTemplate)
    {
        string root = Path.Combine(Path.GetTempPath(), $"func-init-blocked-{Guid.NewGuid():N}");
        string project = Path.Combine(root, "App");
        try
        {
            ITemplaterFactory? factory = await CreateInitTemplaterFactoryAsync(root, useTemplate);
            Directory.CreateDirectory(root);
            File.WriteAllText(project, "user file");
            FakeProjectInitializer python = new("python", [_python]);

            int exitCode = await RunInitAsync(
                project, python, language: "python", stack: "python", templaterFactory: factory);

            exitCode.Should().Be(1);
            File.ReadAllText(project).Should().Be("user file");
            python.WasInvoked.Should().BeFalse();
            python.ConfigureCount.Should().Be(0);
            AssertFolderFailureReported(
                $"ERROR: Couldn't create the project folder '{project}': ",
                "HINT: Nothing was changed. Fix the problem, then run the same command again.");

            // The same command succeeds once the problem is fixed.
            File.Delete(project);
            int retryExitCode = await RunInitAsync(
                project, python, language: "python", stack: "python", templaterFactory: factory);

            retryExitCode.Should().Be(0, _interaction.AllOutput);
            AssertConfigJsonHasShape(project, expectedStack: "python", expectedLanguage: null);
        }
        finally
        {
            CleanupDirectory(root);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task InitCommand_ProjectFolderCannotBeCleared_SameCommandSucceedsOnceFixed(bool useTemplate)
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Only Windows blocks deleting a file that is open.");
        string root = Path.Combine(Path.GetTempPath(), $"func-init-locked-{Guid.NewGuid():N}");
        string project = Path.Combine(root, "App");
        try
        {
            ITemplaterFactory? factory = await CreateInitTemplaterFactoryAsync(root, useTemplate);
            Directory.CreateDirectory(project);
            string locked = Path.Combine(project, "locked.txt");
            File.WriteAllText(locked, "in use");
            FakeProjectInitializer python = new("python", [_python]);

            int exitCode;
            using (FileStream stream = File.Open(locked, FileMode.Open, FileAccess.Read, FileShare.None))
            {
                exitCode = await RunInitAsync(
                    project, python, language: "python", stack: "python", force: true, templaterFactory: factory);
            }

            exitCode.Should().Be(1);
            python.WasInvoked.Should().BeFalse();
            python.ConfigureCount.Should().Be(0);
            Directory.Exists(Path.Combine(project, ".func")).Should().BeFalse();
            AssertFolderFailureReported(
                $"ERROR: Couldn't clear the project folder '{project}': ",
                "HINT: Some files may already be deleted. Fix the problem (for example, close programs that use "
                + "files in the folder), then run the same command again.");

            // The same command succeeds once the file is released.
            int retryExitCode = await RunInitAsync(
                project, python, language: "python", stack: "python", force: true, templaterFactory: factory);

            retryExitCode.Should().Be(0, _interaction.AllOutput);
            File.Exists(locked).Should().BeFalse();
            AssertConfigJsonHasShape(project, expectedStack: "python", expectedLanguage: null);
        }
        finally
        {
            CleanupDirectory(root);
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData("{")]
    [InlineData("not json")]
    [InlineData("""{"stack":null}""")]
    public async Task InitCommand_MalformedProjectConfig_FailsWithoutChanges(string content)
    {
        using TempDirectory directory = new PhysicalFileSystem().CreateTempDirectory();
        Directory.CreateDirectory(Path.Combine(directory.Path, ".func"));
        DirectoryInfo projectDirectory = new(directory.Path);
        string configPath = CliConfigurationPathsOptions.GetProjectConfigPath(projectDirectory);
        File.WriteAllText(configPath, content);
        FakeProjectInitializer python = new("python", [_python]);
        bool isValidJson = content.StartsWith("{\"stack\"", StringComparison.Ordinal);

        int exitCode = await RunInitAsync(directory.Path, python, language: null, stack: "python");

        exitCode.Should().Be(1);
        python.WasInvoked.Should().BeFalse();
        File.ReadAllText(configPath).Should().Be(content);
        string expectedError = isValidJson
            ? "ERROR: stack.runtime in .func/config.json '' does not identify an installed stack."
            : $"ERROR: Could not read project config '{configPath}': ";
        _interaction.Lines.Should().ContainSingle(
            line => line.StartsWith(expectedError, StringComparison.Ordinal), _interaction.AllOutput);

        // Today the CLI's startup configuration loader rejects invalid JSON before init runs, so even
        // func init --force can't recover it: https://github.com/Azure/azure-functions-core-tools/issues/6024
        Action loadStartupConfiguration = () => new CliConfigurationProvider(
            new LocalSettingsProvider(), new DirectoryInfo(Path.Combine(directory.Path, "user")))
            .GetProjectConfiguration(projectDirectory);
        if (isValidJson)
        {
            loadStartupConfiguration.Should().NotThrow();
        }
        else
        {
            loadStartupConfiguration.Should().Throw<InvalidDataException>()
                .WithMessage("Failed to load configuration from file*");
        }

        // Past the startup loader, init --force doesn't read the old config and replaces it.
        int forceExitCode = await RunInitAsync(directory.Path, python, language: null, stack: "python", force: true);

        forceExitCode.Should().Be(0, string.Join(Environment.NewLine, _interaction.Lines));
        python.WasInvoked.Should().BeTrue();
        AssertConfigJsonHasShape(directory.Path, expectedStack: "python", expectedLanguage: null);
    }

    [Fact]
    public async Task InitCommand_TemplateCreationFailure_LeavesNoConfigAndForceRetrySucceeds()
    {
        string root = Path.Combine(Path.GetTempPath(), $"func-init-template-{Guid.NewGuid():N}");
        string home = Path.Combine(root, "home");
        string output = Path.Combine(root, "App");
        try
        {
            string package = TemplateFixtures.WriteInitProjectPackage(
                root, binding: $"{SentinelBindSymbolSource.Prefix}:value");
            await TemplateFixtures.InstallAsync(package, Path.Combine(home, "templates"));

            // A folder where the template writes a file makes creation fail after the preflight passed.
            SentinelBindSymbolSource source = new(() => Directory.CreateDirectory(Path.Combine(output, "App.txt")));
            ICoreTemplateComponent component = Substitute.For<ICoreTemplateComponent>();
            component.InterfaceType.Returns(typeof(IBindSymbolSource));
            component.Instance.Returns(source);
            ITemplaterFactory factory = TemplateFixtures.CreateTemplaterFactory(home, [component]);
            FakeProjectInitializer initializer = new("python", [_python]);

            int exitCode = await RunInitAsync(
                output, initializer, language: "python", stack: "python", templaterFactory: factory);

            exitCode.Should().Be(1);
            source.CallCount.Should().BeGreaterThan(1, "creation fails only after the preflight passed");
            initializer.ConfigureCount.Should().Be(0);
            File.Exists(Path.Combine(output, ".func", "config.json")).Should().BeFalse();
            AssertStartOverReported(
                "ERROR: Couldn't create the python project from template "
                + $"'{TemplateFixtures.InitProjectTemplateIdentity}': ",
                output,
                force: false);

            // The original command with --force added.
            int retryExitCode = await RunInitAsync(
                output, initializer, language: "python", stack: "python", force: true, templaterFactory: factory);

            retryExitCode.Should().Be(0, _interaction.AllOutput);
            initializer.ConfigureCount.Should().Be(1);
            File.ReadAllText(Path.Combine(output, "App.txt")).Should().Be("net10.0");
            AssertConfigJsonHasShape(output, expectedStack: "python", expectedLanguage: null);
        }
        finally
        {
            CleanupDirectory(root);
        }
    }

    [Fact]
    public async Task InitCommand_TemplateSetupFailure_LeavesNoConfigAndForceRetrySucceeds()
    {
        string root = Path.Combine(Path.GetTempPath(), $"func-init-template-{Guid.NewGuid():N}");
        string home = Path.Combine(root, "home");
        string output = Path.Combine(root, "App");
        try
        {
            string package = TemplateFixtures.WriteInitProjectPackage(root);
            await TemplateFixtures.InstallAsync(package, Path.Combine(home, "templates"));
            int attempts = 0;
            FakeProjectInitializer initializer = new("python", [_python], onConfigure: _ =>
            {
                if (++attempts == 1)
                {
                    throw new IOException("setup failed");
                }
            });

            int exitCode = await RunInitAsync(
                output,
                initializer,
                language: "python",
                stack: "python",
                templaterFactory: TemplateFixtures.CreateTemplaterFactory(home));

            exitCode.Should().Be(1);
            File.Exists(Path.Combine(output, "App.txt")).Should().BeTrue();
            Directory.Exists(Path.Combine(output, ".func")).Should().BeFalse();
            AssertStartOverReported("ERROR: Couldn't set up the python project: ", output, force: false)
                .Should().Be("ERROR: Couldn't set up the python project: setup failed");

            // The original command with --force added.
            int retryExitCode = await RunInitAsync(
                output,
                initializer,
                language: "python",
                stack: "python",
                force: true,
                templaterFactory: TemplateFixtures.CreateTemplaterFactory(home));

            retryExitCode.Should().Be(0, _interaction.AllOutput);
            initializer.ConfigureCount.Should().Be(2);
            initializer.WasInvoked.Should().BeFalse();
            File.Exists(Path.Combine(output, "App.txt")).Should().BeTrue();
            AssertConfigJsonHasShape(output, expectedStack: "python", expectedLanguage: null);
            _interaction.Lines.Should().Contain(line => line.Contains("Project initialized for"));
        }
        finally
        {
            CleanupDirectory(root);
        }
    }

    [Theory]
    [InlineData("template", "unknown-stack")]
    [InlineData("template", "unsupported-language")]
    [InlineData("template", "missing-language")]
    [InlineData("legacy", "unknown-stack")]
    [InlineData("legacy", "unsupported-language")]
    [InlineData("legacy", "missing-language")]
    [InlineData("adopt", "unknown-stack")]
    [InlineData("adopt", "unsupported-language")]
    [InlineData("adopt", "missing-language")]
    [InlineData("complete", "unknown-stack")]
    [InlineData("complete", "unsupported-language")]
    [InlineData("complete", "missing-language")]
    public async Task InitCommand_InvalidStackOrLanguage_FailsBeforeAnyRouteWrites(string route, string failure)
    {
        string root = Directory.CreateTempSubdirectory("func-init-").FullName;
        string home = Path.Combine(root, "home");
        string project = Path.Combine(root, "App");
        try
        {
            ITemplaterFactory? templaterFactory = null;
            switch (route)
            {
                case "template":
                    await TemplateFixtures.InstallAsync(
                        TemplateFixtures.WriteInitProjectPackage(root), Path.Combine(home, "templates"));
                    templaterFactory = TemplateFixtures.CreateTemplaterFactory(home);
                    break;
                case "adopt":
                    Directory.CreateDirectory(project);
                    File.WriteAllText(Path.Combine(project, "host.json"), "{}");
                    StubLocalSettingsRuntime("python");
                    break;
                case "complete":
                    Directory.CreateDirectory(Path.Combine(project, ".func"));
                    File.WriteAllText(
                        Path.Combine(project, ".func", "config.json"),
                        """{ "stack": { "runtime": "python" }, "profiles": { "keep": true } }""");
                    break;
            }

            Dictionary<string, byte[]> before = SnapshotTree(project);

            // Two languages, so a missing --language is refused non-interactively on every route.
            FakeProjectInitializer initializer = new("python", [_python, _typescript]);
            string? stack = failure == "unknown-stack" ? "nope" : "python";
            string? language = failure switch
            {
                "unsupported-language" => "java",
                "missing-language" => null,
                _ => "python",
            };

            int exitCode = await RunInitAsync(
                project, initializer, language, stack, templaterFactory: templaterFactory);

            exitCode.Should().Be(1);
            initializer.WasInvoked.Should().BeFalse();
            initializer.ConfigureCount.Should().Be(0);
            Directory.Exists(project).Should().Be(route is "adopt" or "complete");
            SnapshotTree(project).Should().BeEquivalentTo(before);
            _interaction.Lines.Should().NotContain(line => line.Contains("Project initialized for"));
            switch (failure)
            {
                case "unknown-stack":
                    _hintRenderer.Hints.Should().ContainSingle()
                        .Which.Kind.Should().Be(WorkloadHintKind.NoMatchingStack);
                    break;
                case "unsupported-language":
                    _interaction.AllOutput.Should().Contain("Language 'java' from --language is not supported");
                    break;
                default:
                    _interaction.AllOutput.Should().Contain("--language <python|typescript>");
                    break;
            }
        }
        finally
        {
            CleanupDirectory(root);
        }
    }

    [Theory]
    [InlineData("template")]
    [InlineData("legacy")]
    [InlineData("adopt")]
    [InlineData("complete")]
    public async Task InitCommand_EveryRoute_WritesConfigOnlyThroughProjectStackConfiguration(string route)
    {
        string root = Directory.CreateTempSubdirectory("func-init-").FullName;
        string home = Path.Combine(root, "home");
        string project = Path.Combine(root, "App");
        try
        {
            ITemplaterFactory? templaterFactory = null;
            ProjectStackSettings settings = new();
            switch (route)
            {
                case "template":
                    await TemplateFixtures.InstallAsync(
                        TemplateFixtures.WriteInitProjectPackage(root), Path.Combine(home, "templates"));
                    templaterFactory = TemplateFixtures.CreateTemplaterFactory(home);
                    break;
                case "adopt":
                    Directory.CreateDirectory(project);
                    File.WriteAllText(Path.Combine(project, "host.json"), "{}");
                    settings = new(WorkerRuntime: "python", IsAdoptable: true);
                    break;
                case "complete":
                    Directory.CreateDirectory(project);
                    settings = new(HasProjectConfig: true, StackIdentifier: "python");
                    break;
            }

            IProjectStackConfiguration configuration = Substitute.For<IProjectStackConfiguration>();
            configuration.Read(Arg.Any<DirectoryInfo>()).Returns(settings);
            FakeProjectInitializer initializer = new("python", [_python, _typescript]);
            RootCommand rootCommand =
                [CreateCommand([initializer], templaterFactory: templaterFactory, configuration: configuration)];

            int exitCode = await rootCommand
                .Parse(["init", project, "--stack", "python", "--language", "python"])
                .InvokeAsync(cancellationToken: TestContext.Current.CancellationToken);

            exitCode.Should().Be(0, string.Join(Environment.NewLine, _interaction.Lines));
            int creates = route == "complete" ? 0 : 1;
            await configuration.Received(creates).CreateAsync(
                Arg.Is<DirectoryInfo>(directory => directory.FullName == Path.GetFullPath(project)),
                initializer.Stack,
                _python,
                Arg.Any<CancellationToken>());
            await configuration.Received(1 - creates).CompleteLanguageAsync(
                Arg.Any<DirectoryInfo>(), initializer.Stack, _python, Arg.Any<CancellationToken>());
            Directory.Exists(Path.Combine(project, ".func")).Should().BeFalse();
        }
        finally
        {
            CleanupDirectory(root);
        }
    }

    [Theory]
    [InlineData("null", "must contain a JSON object")]
    [InlineData("{}", "does not identify an installed stack")]
    [InlineData("""{"stack":5}""", "The 'stack' property in .func/config.json must be an object.")]
    [InlineData("""{"stack":{"runtime":5}}""", "The 'stack.runtime' property in .func/config.json must be a string.")]
    [InlineData(
        """{"stack":{"runtime":"python","language":5}}""",
        "The 'stack.language' property in .func/config.json must be a string.")]
    public async Task InitCommand_MalformedProjectConfig_FailsBeforeWritingAndForceRecovers(
        string config, string expectedError)
    {
        string project = Directory.CreateTempSubdirectory("func-init-").FullName;
        try
        {
            Directory.CreateDirectory(Path.Combine(project, ".func"));
            File.WriteAllText(Path.Combine(project, ".func", "config.json"), config);
            Dictionary<string, byte[]> before = SnapshotTree(project);
            FakeProjectInitializer initializer = new("python");

            int exitCode = await RunInitAsync(project, initializer, language: null, stack: "python");

            exitCode.Should().Be(1);
            initializer.WasInvoked.Should().BeFalse();
            SnapshotTree(project).Should().BeEquivalentTo(before);
            _interaction.AllOutput.Should().Contain(expectedError);

            int forceExitCode = await RunInitAsync(project, initializer, language: null, stack: "python", force: true);

            forceExitCode.Should().Be(0, string.Join(Environment.NewLine, _interaction.Lines));
            initializer.WasInvoked.Should().BeTrue();
            AssertConfigJsonHasShape(project, expectedStack: "python", expectedLanguage: null);
        }
        finally
        {
            CleanupDirectory(project);
        }
    }

    private static Dictionary<string, byte[]> SnapshotTree(string path) =>
        Directory.Exists(path)
            ? Directory.EnumerateFileSystemEntries(path, "*", SearchOption.AllDirectories).ToDictionary(
                entry => Path.GetRelativePath(path, entry),
                entry => File.Exists(entry) ? File.ReadAllBytes(entry) : [])
            : [];

    [Fact]
    public async Task InitCommand_ScaffoldsCliConfigurationFile_OnSuccess()
    {
        var newDir = Path.Combine(Path.GetTempPath(), $"func-init-{Guid.NewGuid():N}");

        try
        {
            var initializer = new FakeProjectInitializer("python");
            int exitCode = await RunInitAsync(newDir, initializer, language: "python", stack: "python");

            exitCode.Should().Be(0);
            initializer.WasInvoked.Should().BeTrue();

            string configPath = Path.Combine(newDir, ".func", "config.json");
            File.Exists(configPath).Should().BeTrue($"Expected .func/config.json at {configPath}.");

            using var doc = JsonDocument.Parse(File.ReadAllText(configPath));
            JsonElement stack = doc.RootElement.GetProperty("stack");
            stack.GetProperty("runtime").GetString().Should().Be("python");
            stack.TryGetProperty("language", out _).Should().BeFalse();
        }
        finally
        {
            if (Directory.Exists(newDir))
            {
                Directory.Delete(newDir, recursive: true);
            }
        }
    }

    [Fact]
    public async Task InitCommand_PreviewChannelBundle_NoInstalledBundle_WarnsWithSearchHint()
    {
        var newDir = Path.Combine(Path.GetTempPath(), $"func-init-{Guid.NewGuid():N}");

        try
        {
            _bundleReader.ReadAsync(Arg.Any<DirectoryInfo>(), Arg.Any<CancellationToken>())
                .Returns(new HostJsonBundleSection(BundleHelpers.PreviewBundleId, "[4.0.0, 5.0.0)"));

            var initializer = new FakeProjectInitializer("python");
            int exitCode = await RunInitAsync(newDir, initializer, language: "python", stack: "python");

            exitCode.Should().Be(0);
            _interaction.Lines.Should().Contain(line => line.Contains("func workload search bundles --prerelease", StringComparison.Ordinal));
        }
        finally
        {
            CleanupDirectory(newDir);
        }
    }

    [Fact]
    public async Task InitCommand_PreviewChannelBundle_InstalledBundle_DoesNotWarn()
    {
        var newDir = Path.Combine(Path.GetTempPath(), $"func-init-{Guid.NewGuid():N}");

        try
        {
            _bundleReader.ReadAsync(Arg.Any<DirectoryInfo>(), Arg.Any<CancellationToken>())
                .Returns(new HostJsonBundleSection(BundleHelpers.PreviewBundleId, "[4.0.0, 5.0.0)"));
            _installedBundles.ListInstalledAsync(Arg.Any<CancellationToken>())
                .Returns(new InstalledBundleWorkload[] { new("4.11.0-preview.1", newDir) });

            var initializer = new FakeProjectInitializer("python");
            int exitCode = await RunInitAsync(newDir, initializer, language: "python", stack: "python");

            exitCode.Should().Be(0);
            _interaction.Lines.Should().NotContain(line => line.Contains("func workload search bundles --prerelease", StringComparison.Ordinal));
        }
        finally
        {
            CleanupDirectory(newDir);
        }
    }

    [Fact]
    public async Task InitCommand_StableChannelBundle_DoesNotWarn()
    {
        var newDir = Path.Combine(Path.GetTempPath(), $"func-init-{Guid.NewGuid():N}");

        try
        {
            _bundleReader.ReadAsync(Arg.Any<DirectoryInfo>(), Arg.Any<CancellationToken>())
                .Returns(new HostJsonBundleSection(BundleHelpers.StableBundleId, "[4.0.0, 5.0.0)"));

            var initializer = new FakeProjectInitializer("python");
            int exitCode = await RunInitAsync(newDir, initializer, language: "python", stack: "python");

            exitCode.Should().Be(0);
            _interaction.Lines.Should().NotContain(line => line.Contains("func workload search bundles --prerelease", StringComparison.Ordinal));
        }
        finally
        {
            CleanupDirectory(newDir);
        }
    }

    [Fact]
    public async Task InitCommand_DoesNotOverwriteExistingCliConfigurationFile()
    {
        var newDir = Path.Combine(Path.GetTempPath(), $"func-init-{Guid.NewGuid():N}");

        try
        {
            Directory.CreateDirectory(Path.Combine(newDir, ".func"));
            string configPath = Path.Combine(newDir, ".func", "config.json");
            const string existingContent = """{"stack":"node","language":"typescript"}""";
            File.WriteAllText(configPath, existingContent);

            var initializer = new FakeProjectInitializer("python");
            int exitCode = await RunInitAsync(newDir, initializer, language: "python", stack: "python");

            // Folder is already a Functions project (.func/config.json present);
            // command refuses without --force, initializer never runs.
            exitCode.Should().Be(1);
            initializer.WasInvoked.Should().BeFalse();
            File.ReadAllText(configPath).Should().Be(existingContent);
        }
        finally
        {
            if (Directory.Exists(newDir))
            {
                Directory.Delete(newDir, recursive: true);
            }
        }
    }

    [Fact]
    public async Task InitCommand_OmitsLanguageWhenNotSpecified()
    {
        var newDir = Path.Combine(Path.GetTempPath(), $"func-init-{Guid.NewGuid():N}");

        try
        {
            var initializer = new FakeProjectInitializer("dotnet");
            int exitCode = await RunInitAsync(newDir, initializer, language: null, stack: "dotnet");

            exitCode.Should().Be(0);

            string configPath = Path.Combine(newDir, ".func", "config.json");
            using var doc = JsonDocument.Parse(File.ReadAllText(configPath));
            JsonElement stack = doc.RootElement.GetProperty("stack");
            stack.GetProperty("runtime").GetString().Should().Be("dotnet");
            stack.TryGetProperty("language", out _).Should().BeFalse();
        }
        finally
        {
            if (Directory.Exists(newDir))
            {
                Directory.Delete(newDir, recursive: true);
            }
        }
    }

    private async Task<int> RunInitAsync(
        string path,
        IProjectInitializer initializer,
        string? language,
        string? stack = null,
        bool force = false,
        IReadOnlyList<string>? extraArgs = null,
        ITemplaterFactory? templaterFactory = null)
    {
        return await RunInitAsync(path, [initializer], language, stack, force, extraArgs, templaterFactory);
    }

    private async Task<int> RunInitAsync(
        string path,
        IReadOnlyList<IProjectInitializer> initializers,
        string? language = null,
        string? stack = null,
        bool force = false,
        IReadOnlyList<string>? extraArgs = null,
        ITemplaterFactory? templaterFactory = null)
    {
        var cmd = CreateCommand(initializers, templaterFactory: templaterFactory);
        var root = new RootCommand { cmd };
        var args = new List<string> { "init", path };
        if (stack is not null)
        {
            args.Add("--stack");
            args.Add(stack);
        }
        if (language is not null)
        {
            args.Add("--language");
            args.Add(language);
        }
        if (force)
        {
            args.Add("--force");
        }
        if (extraArgs is not null)
        {
            args.AddRange(extraArgs);
        }
        ParseResult result = root.Parse(args.ToArray());
        return await result.InvokeAsync();
    }

    [Fact]
    public async Task InitCommand_FreshStack_RejectsUnknownOptionBeforeCreatingDestination()
    {
        string path = Path.Combine(Path.GetTempPath(), $"func-init-{Guid.NewGuid():N}");
        try
        {
            FakeProjectInitializer python = new("python", [_python]);
            int exitCode = await RunInitAsync(
                path, python, language: "python", stack: "python", extraArgs: ["--does-not-exist"]);

            exitCode.Should().Be(1);
            _interaction.Lines.Should().Equal("ERROR: Unrecognized option '--does-not-exist'.");
            Directory.Exists(path).Should().BeFalse();
            python.WasInvoked.Should().BeFalse();
        }
        finally
        {
            CleanupDirectory(path);
        }
    }

    [Fact]
    public async Task InitCommand_NoWorkloadsInstalled_ExitsWithHint()
    {
        var newDir = Path.Combine(Path.GetTempPath(), $"func-init-{Guid.NewGuid():N}");
        try
        {
            int exitCode = await RunInitAsync(newDir, initializers: []);

            exitCode.Should().Be(1);

            _hintRenderer.Hints.Should().ContainSingle().Which.Kind.Should().Be(WorkloadHintKind.NoWorkloadsInstalled);
        }
        finally
        {
            CleanupDirectory(newDir);
        }
    }

    [Fact]
    public async Task InitCommand_StackProvidedButNoMatch_ExitsWithHint()
    {
        var newDir = Path.Combine(Path.GetTempPath(), $"func-init-{Guid.NewGuid():N}");
        try
        {
            var initializer = new FakeProjectInitializer("python");
            int exitCode = await RunInitAsync(newDir, initializer, language: null, stack: "ruby");

            exitCode.Should().Be(1);
            initializer.WasInvoked.Should().BeFalse();

            WorkloadHint hint = _hintRenderer.Hints.Should().ContainSingle().Subject;
            hint.Kind.Should().Be(WorkloadHintKind.NoMatchingStack);
            hint.RequestedStack.Should().Be("ruby");
        }
        finally
        {
            CleanupDirectory(newDir);
        }
    }

    [Fact]
    public async Task InitCommand_MultipleWorkloads_NoStack_NonInteractive_ExitsWithHint()
    {
        var newDir = Path.Combine(Path.GetTempPath(), $"func-init-{Guid.NewGuid():N}");
        try
        {
            var python = new FakeProjectInitializer("python");
            var node = new FakeProjectInitializer("node");
            int exitCode = await RunInitAsync(newDir, [python, node]);

            exitCode.Should().Be(1);
            python.WasInvoked.Should().BeFalse();
            node.WasInvoked.Should().BeFalse();

            _hintRenderer.Hints.Should().ContainSingle().Which.Kind.Should().Be(WorkloadHintKind.AmbiguousStackChoice);
        }
        finally
        {
            CleanupDirectory(newDir);
        }
    }

    [Fact]
    public async Task InitCommand_ExplicitHostRuntime_IsNotAStackSelector()
    {
        var newDir = Path.Combine(Path.GetTempPath(), $"func-init-{Guid.NewGuid():N}");
        try
        {
            var dotnet = new FakeProjectInitializer("dotnet", workerRuntime: "dotnet-isolated");
            int exitCode = await RunInitAsync(newDir, dotnet, language: null, stack: "dotnet-isolated");

            exitCode.Should().Be(1);
            dotnet.WasInvoked.Should().BeFalse();
            _hintRenderer.Hints.Should().ContainSingle().Which.Kind.Should().Be(WorkloadHintKind.NoMatchingStack);
            Directory.Exists(newDir).Should().BeFalse();
        }
        finally
        {
            CleanupDirectory(newDir);
        }
    }

    [Fact]
    public async Task InitCommand_SingleWorkload_NoStack_AutoSelectsAndRuns()
    {
        var newDir = Path.Combine(Path.GetTempPath(), $"func-init-{Guid.NewGuid():N}");
        try
        {
            var initializer = new FakeProjectInitializer("python");
            int exitCode = await RunInitAsync(newDir, initializer, language: null);

            exitCode.Should().Be(0);
            initializer.WasInvoked.Should().BeTrue();
            AssertConfigJsonHasShape(newDir, expectedStack: "python", expectedLanguage: null);

            WorkloadHint hint = _hintRenderer.Hints.Should().ContainSingle().Subject;
            hint.Kind.Should().Be(WorkloadHintKind.AutoSelectedSoleWorkload);
            hint.RequestedStack.Should().Be("python");
        }
        finally
        {
            CleanupDirectory(newDir);
        }
    }

    [Fact]
    public async Task InitCommand_StackProvided_WritesConfigAndInvokesInitializer()
    {
        var newDir = Path.Combine(Path.GetTempPath(), $"func-init-{Guid.NewGuid():N}");
        try
        {
            var initializer = new FakeProjectInitializer("python");
            int exitCode = await RunInitAsync(newDir, initializer, language: "python", stack: "python");

            exitCode.Should().Be(0);
            initializer.WasInvoked.Should().BeTrue();
            AssertConfigJsonHasShape(newDir, expectedStack: "python", expectedLanguage: null);

            // No hint rendered when --stack matched directly.
            _hintRenderer.Hints.Should().BeEmpty();
        }
        finally
        {
            CleanupDirectory(newDir);
        }
    }

    [Fact]
    public async Task InitCommand_AdoptsExistingProject_WhenOnlyHostJsonPresent()
    {
        // host.json without .func/config.json means a pre-v5 project. `func init`
        // should adopt it: write .func/config.json, preserve host.json and any
        // user source, and skip scaffolding.
        var newDir = Path.Combine(Path.GetTempPath(), $"func-init-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(newDir);
            string hostPath = Path.Combine(newDir, "host.json");
            const string existingContent = "{\"version\":\"2.0\",\"customMarker\":true}";
            File.WriteAllText(hostPath, existingContent);
            string userFile = Path.Combine(newDir, "MyFunction.cs");
            File.WriteAllText(userFile, "// user code");

            var initializer = new FakeProjectInitializer("python");
            int exitCode = await RunInitAsync(newDir, initializer, language: null, stack: "python");

            exitCode.Should().Be(0);
            // Scaffolding is skipped: existing files survive untouched and the
            // initializer is not invoked.
            initializer.WasInvoked.Should().BeFalse();
            File.ReadAllText(hostPath).Should().Be(existingContent);
            File.Exists(userFile).Should().BeTrue();
            AssertConfigJsonHasShape(newDir, expectedStack: "python", expectedLanguage: null);
        }
        finally
        {
            CleanupDirectory(newDir);
        }
    }

    [Fact]
    public async Task InitCommand_RefusesEarly_WhenFullyInitialized()
    {
        // A directory that already has .func/config.json (a v5 project) is
        // refused without --force: no overwrite, no scaffolding, exit 1.
        var newDir = Path.Combine(Path.GetTempPath(), $"func-init-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(Path.Combine(newDir, ".func"));
            File.WriteAllText(Path.Combine(newDir, ".func", "config.json"), "{}");

            var initializer = new FakeProjectInitializer("python");
            int exitCode = await RunInitAsync(newDir, initializer, language: null, stack: "python");

            exitCode.Should().Be(1);
            initializer.WasInvoked.Should().BeFalse();
            File.ReadAllText(Path.Combine(newDir, ".func", "config.json")).Should().Be("{}");
        }
        finally
        {
            CleanupDirectory(newDir);
        }
    }

    [Fact]
    public async Task InitCommand_OverwritesExistingProject_WhenForceIsSet()
    {
        var newDir = Path.Combine(Path.GetTempPath(), $"func-init-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(newDir);
            string hostPath = Path.Combine(newDir, "host.json");
            File.WriteAllText(hostPath, "{\"version\":\"2.0\",\"customMarker\":true}");

            var initializer = new FakeProjectInitializer("python");
            int exitCode = await RunInitAsync(newDir, initializer, language: null, stack: "python", force: true);

            exitCode.Should().Be(0);
            initializer.WasInvoked.Should().BeTrue();
        }
        finally
        {
            CleanupDirectory(newDir);
        }
    }

    [Fact]
    public async Task InitCommand_Force_ClearsDirectoryButPreservesDotGit()
    {
        var newDir = Path.Combine(Path.GetTempPath(), $"func-init-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(newDir);
            // Pre-existing junk from a prior stack.
            File.WriteAllText(Path.Combine(newDir, "package.json"), "{}");
            File.WriteAllText(Path.Combine(newDir, "requirements.txt"), "azure-functions");
            Directory.CreateDirectory(Path.Combine(newDir, "node_modules"));
            File.WriteAllText(Path.Combine(newDir, "node_modules", "marker"), "x");

            // .git must survive.
            Directory.CreateDirectory(Path.Combine(newDir, ".git"));
            File.WriteAllText(Path.Combine(newDir, ".git", "HEAD"), "ref: refs/heads/main");

            var initializer = new FakeProjectInitializer("python");
            int exitCode = await RunInitAsync(newDir, initializer, language: null, stack: "python", force: true);

            exitCode.Should().Be(0);
            File.Exists(Path.Combine(newDir, "package.json")).Should().BeFalse();
            File.Exists(Path.Combine(newDir, "requirements.txt")).Should().BeFalse();
            Directory.Exists(Path.Combine(newDir, "node_modules")).Should().BeFalse();
            File.Exists(Path.Combine(newDir, ".git", "HEAD")).Should().BeTrue();
        }
        finally
        {
            CleanupDirectory(newDir);
        }
    }

    [Fact]
    public async Task InitCommand_Force_WritesWarningBeforeClearing()
    {
        var newDir = Path.Combine(Path.GetTempPath(), $"func-init-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(newDir);
            File.WriteAllText(Path.Combine(newDir, "leftover.txt"), "x");

            var initializer = new FakeProjectInitializer("python");
            int exitCode = await RunInitAsync(newDir, initializer, language: null, stack: "python", force: true);

            exitCode.Should().Be(0);
            _interaction.Lines.Should().Contain(l => l.StartsWith("WARNING:") && l.Contains("--force will delete"));
        }
        finally
        {
            CleanupDirectory(newDir);
        }
    }

    [Fact]
    public async Task InitCommand_Adopt_ConvertsHostRuntimeToCliId()
    {
        var newDir = Path.Combine(Path.GetTempPath(), $"func-init-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(newDir);
            File.WriteAllText(Path.Combine(newDir, "host.json"), "{}");
            StubLocalSettingsRuntime("dotnet-isolated");

            var dotnet = new FakeProjectInitializer("dotnet", workerRuntime: "dotnet-isolated");
            int exitCode = await RunInitAsync(newDir, [dotnet], language: null, stack: null);

            exitCode.Should().Be(0);
            dotnet.WasInvoked.Should().BeFalse();
            AssertConfigJsonHasShape(newDir, expectedStack: "dotnet", expectedLanguage: null);
        }
        finally
        {
            CleanupDirectory(newDir);
        }
    }

    [Fact]
    public async Task InitCommand_Adopt_ExplicitStackAgreesWithHostRuntime()
    {
        var newDir = Path.Combine(Path.GetTempPath(), $"func-init-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(newDir);
            File.WriteAllText(Path.Combine(newDir, "host.json"), "{}");
            StubLocalSettingsRuntime("dotnet-isolated");

            var dotnet = new FakeProjectInitializer("dotnet", workerRuntime: "dotnet-isolated");
            int exitCode = await RunInitAsync(newDir, dotnet, language: null, stack: "dotnet");

            exitCode.Should().Be(0);
            dotnet.WasInvoked.Should().BeFalse();
            AssertConfigJsonHasShape(newDir, expectedStack: "dotnet", expectedLanguage: null);
        }
        finally
        {
            CleanupDirectory(newDir);
        }
    }

    [Fact]
    public async Task InitCommand_Adopt_ExplicitRuntimeRejectedEvenWithMatchingLocalSignal()
    {
        var newDir = Path.Combine(Path.GetTempPath(), $"func-init-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(newDir);
            File.WriteAllText(Path.Combine(newDir, "host.json"), "{}");

            StubLocalSettingsRuntime("dotnet-isolated");
            var dotnet = new FakeProjectInitializer("dotnet", workerRuntime: "dotnet-isolated");
            var command = CreateCommand([dotnet], new InteractiveTestInteractionService());
            var root = new RootCommand { command };

            int exitCode = await root.Parse(["init", newDir, "--stack", "dotnet-isolated"]).InvokeAsync(
                cancellationToken: TestContext.Current.CancellationToken);

            exitCode.Should().Be(1);
            dotnet.WasInvoked.Should().BeFalse();
            Directory.Exists(Path.Combine(newDir, ".func")).Should().BeFalse();
        }
        finally
        {
            CleanupDirectory(newDir);
        }
    }

    [Fact]
    public async Task InitCommand_Adopt_SnapsStackFromLocalSettings()
    {
        // host.json + local.settings.json FUNCTIONS_WORKER_RUNTIME=python → snap
        // to python, initializer is not invoked (existing project), config
        // records python.
        var newDir = Path.Combine(Path.GetTempPath(), $"func-init-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(newDir);
            File.WriteAllText(Path.Combine(newDir, "host.json"), "{}");
            StubLocalSettingsRuntime("python");

            var python = new FakeProjectInitializer("python");
            int exitCode = await RunInitAsync(newDir, [python], language: null, stack: null);

            exitCode.Should().Be(0);
            python.WasInvoked.Should().BeFalse();
            AssertConfigJsonHasShape(newDir, expectedStack: "python", expectedLanguage: null);
        }
        finally
        {
            CleanupDirectory(newDir);
        }
    }

    [Fact]
    public async Task InitCommand_Adopt_UnknownHostRuntime_ReportsSourceAndDoesNotWrite()
    {
        var newDir = Path.Combine(Path.GetTempPath(), $"func-init-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(newDir);
            File.WriteAllText(Path.Combine(newDir, "host.json"), "{}");
            StubLocalSettingsRuntime("ruby");

            var python = new FakeProjectInitializer("python");
            int exitCode = await RunInitAsync(newDir, [python], language: null, stack: null);

            exitCode.Should().Be(1);
            python.WasInvoked.Should().BeFalse();
            File.Exists(Path.Combine(newDir, ".func", "config.json")).Should().BeFalse();
            _interaction.Lines.Should().Contain(l =>
                l.StartsWith("ERROR:")
                && l.Contains("'ruby'")
                && l.Contains("FUNCTIONS_WORKER_RUNTIME in local.settings.json"));
        }
        finally
        {
            CleanupDirectory(newDir);
        }
    }

    [Fact]
    public async Task InitCommand_Adopt_RefusesWhenStackOptionConflictsWithProjectRuntime()
    {
        var newDir = Path.Combine(Path.GetTempPath(), $"func-init-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(newDir);
            File.WriteAllText(Path.Combine(newDir, "host.json"), "{}");
            StubLocalSettingsRuntime("python");

            var python = new FakeProjectInitializer("python");
            var node = new FakeProjectInitializer("node");
            int exitCode = await RunInitAsync(newDir, [python, node], language: null, stack: "node");

            exitCode.Should().Be(1);
            python.WasInvoked.Should().BeFalse();
            node.WasInvoked.Should().BeFalse();
            File.Exists(Path.Combine(newDir, ".func", "config.json")).Should().BeFalse();
            _interaction.Lines.Should().Contain(l => l.Contains("conflicts") && l.Contains("python") && l.Contains("node"));
        }
        finally
        {
            CleanupDirectory(newDir);
        }
    }

    [Fact]
    public async Task InitCommand_Adopt_AllowsStackOptionWhenItMatchesProjectRuntime()
    {
        var newDir = Path.Combine(Path.GetTempPath(), $"func-init-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(newDir);
            File.WriteAllText(Path.Combine(newDir, "host.json"), "{}");
            StubLocalSettingsRuntime("python");

            var python = new FakeProjectInitializer("python");
            int exitCode = await RunInitAsync(newDir, python, language: null, stack: "python");

            exitCode.Should().Be(0);
            python.WasInvoked.Should().BeFalse();
            AssertConfigJsonHasShape(newDir, expectedStack: "python", expectedLanguage: null);
        }
        finally
        {
            CleanupDirectory(newDir);
        }
    }

    [Fact]
    public async Task InitCommand_Adopt_NoProjectSignal_NonInteractive_Refuses()
    {
        // In adopt mode with no --stack and no FUNCTIONS_WORKER_RUNTIME, we
        // don't auto-select even when only one initializer is installed:
        // writing the wrong stack into an existing project's config is worse
        // than failing fast. Non-interactive callers (CI, piped) get an
        // actionable error.
        var newDir = Path.Combine(Path.GetTempPath(), $"func-init-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(newDir);
            File.WriteAllText(Path.Combine(newDir, "host.json"), "{}");

            var python = new FakeProjectInitializer("python");
            int exitCode = await RunInitAsync(newDir, python, language: null);

            exitCode.Should().Be(1);
            python.WasInvoked.Should().BeFalse();
            File.Exists(Path.Combine(newDir, ".func", "config.json")).Should().BeFalse();
            _interaction.Lines.Should().Contain(l =>
                l.StartsWith("ERROR:") && l.Contains("Couldn't detect the project's stack"));
        }
        finally
        {
            CleanupDirectory(newDir);
        }
    }

    [Theory]
    [InlineData(new[] { "--does-not-exist" }, "ERROR: Unrecognized option '--does-not-exist'.")]
    [InlineData(
        new[] { "-tfm", "net11.0" },
        "ERROR: Option '-tfm' is not supported for an existing project "
            + "because no project content is created. Remove it, or pass --force to re-initialize.")]
    [InlineData(
        new[] { "--target-framework=net11.0" },
        "ERROR: Option '--target-framework' is not supported for an existing project "
            + "because no project content is created. Remove it, or pass --force to re-initialize.")]
    [InlineData(
        new[] { "--no-bundles" },
        "ERROR: Option '--no-bundles' is not supported for an existing project "
            + "because no project content is created. Remove it, or pass --force to re-initialize.")]
    public async Task InitCommand_Adopt_RejectsUnusedOptionBeforePromptingForStack(string[] extraArgs,
        string expectedError)
    {
        string path = Directory.CreateTempSubdirectory("func-init-").FullName;
        try
        {
            string host = Path.Combine(path, "host.json");
            File.WriteAllText(host, """{ "version": "2.0" }""");
            byte[] original = File.ReadAllBytes(host);
            InteractiveTestInteractionService interactive = new();
            FakeProjectInitializer python = CreateInitializerWithStackOptions("python", [_python]);
            RootCommand root = [CreateCommand([python], interaction: interactive)];

            int exitCode = await root.Parse(["init", path, .. extraArgs]).InvokeAsync(
                cancellationToken: TestContext.Current.CancellationToken);

            exitCode.Should().Be(1);
            interactive.Lines.Should().Equal(expectedError);
            Directory.GetFileSystemEntries(path).Should().Equal(host);
            File.ReadAllBytes(host).Should().Equal(original);
            python.WasInvoked.Should().BeFalse();
        }
        finally
        {
            CleanupDirectory(path);
        }
    }

    [Fact]
    public async Task InitCommand_Adopt_NoProjectSignal_Interactive_PromptsForStack()
    {
        // Interactive callers get a prompt that names adoption explicitly so
        // they know what they're answering for. The prompt's first choice is
        // selected by the test interaction service.
        var newDir = Path.Combine(Path.GetTempPath(), $"func-init-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(newDir);
            File.WriteAllText(Path.Combine(newDir, "host.json"), "{}");

            var interactive = new InteractiveTestInteractionService();
            var python = new FakeProjectInitializer("python");
            var command = CreateCommand([python], interactive);
            var root = new RootCommand { command };

            int exitCode = await root.Parse(["init", newDir]).InvokeAsync(
                cancellationToken: TestContext.Current.CancellationToken);

            exitCode.Should().Be(0);
            python.WasInvoked.Should().BeFalse();
            AssertConfigJsonHasShape(newDir, expectedStack: "python", expectedLanguage: null);
            interactive.Lines.Should().Contain(l =>
                l.StartsWith("SELECT:") && l.Contains("Adopting an existing project"));
        }
        finally
        {
            CleanupDirectory(newDir);
        }
    }

    [Fact]
    public async Task InitCommand_Adopt_StackOption_UninstalledStack_Refuses()
    {
        var newDir = Path.Combine(Path.GetTempPath(), $"func-init-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(newDir);
            File.WriteAllText(Path.Combine(newDir, "host.json"), "{}");

            var python = new FakeProjectInitializer("python");
            int exitCode = await RunInitAsync(newDir, [python], language: null, stack: "ruby");

            exitCode.Should().Be(1);
            python.WasInvoked.Should().BeFalse();
            File.Exists(Path.Combine(newDir, ".func", "config.json")).Should().BeFalse();
            WorkloadHint hint = _hintRenderer.Hints.Should().ContainSingle().Subject;
            hint.Kind.Should().Be(WorkloadHintKind.NoMatchingStack);
            hint.RequestedStack.Should().Be("ruby");
        }
        finally
        {
            CleanupDirectory(newDir);
        }
    }

    [Fact]
    public async Task InitCommand_Adopt_ForceOverridesConflict()
    {
        // --force is the documented escape hatch: it wipes the directory and
        // re-scaffolds with the requested stack, regardless of project signal.
        var newDir = Path.Combine(Path.GetTempPath(), $"func-init-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(newDir);
            File.WriteAllText(Path.Combine(newDir, "host.json"), "{}");
            StubLocalSettingsRuntime("python");

            var node = new FakeProjectInitializer("node");
            int exitCode = await RunInitAsync(newDir, node, language: null, stack: "node", force: true);

            exitCode.Should().Be(0);
            node.WasInvoked.Should().BeTrue();
            AssertConfigJsonHasShape(newDir, expectedStack: "node", expectedLanguage: null);
        }
        finally
        {
            CleanupDirectory(newDir);
        }
    }

    private void StubLocalSettingsRuntime(string runtime)
    {
        var snapshot = new LocalSettingsSnapshot
        {
            Values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["FUNCTIONS_WORKER_RUNTIME"] = runtime,
            },
        };
        _localSettings.Get(Arg.Any<DirectoryInfo>()).Returns(snapshot);
    }

    private static void AssertConfigJsonHasShape(string directory, string? expectedStack, string? expectedLanguage)
    {
        string configPath = Path.Combine(directory, ".func", "config.json");
        File.Exists(configPath).Should().BeTrue($"Expected .func/config.json at {configPath}.");

        using var doc = JsonDocument.Parse(File.ReadAllText(configPath));
        if (expectedStack is null)
        {
            doc.RootElement.TryGetProperty("stack", out _).Should().BeFalse();
        }
        else
        {
            doc.RootElement.GetProperty("stack").GetProperty("runtime").GetString().Should().Be(expectedStack);
        }
        if (expectedLanguage is null)
        {
            if (doc.RootElement.TryGetProperty("stack", out JsonElement stack))
            {
                stack.TryGetProperty("language", out _).Should().BeFalse();
            }
        }
        else
        {
            doc.RootElement.GetProperty("stack").GetProperty("language").GetString().Should().Be(expectedLanguage);
        }
    }

    private string AssertStartOverReported(string errorPrefix, string projectFolder, bool force)
    {
        List<string> lines = [.. _interaction.Lines];
        int index = lines.FindIndex(line => line.StartsWith(errorPrefix, StringComparison.Ordinal));
        index.Should().BeGreaterThanOrEqualTo(0, _interaction.AllOutput);
        lines.Should().HaveCountGreaterThan(index + 2);
        string rerun = force
            ? "run your original func init command again"
            : "run your original func init command again with --force added";
        lines[index + 1].Should().Be(
            $"The project folder may now hold a partly created project: {Path.GetFullPath(projectFolder)}");
        lines[index + 2].Should().Be(
            $"HINT: To start over, {rerun}. --force deletes everything in the folder except .git.");
        lines.Should().NotContain(line => line.Contains("unexpected error", StringComparison.OrdinalIgnoreCase));
        lines.Should().NotContain(line => line.TrimStart().StartsWith("func init", StringComparison.Ordinal));
        return lines[index];
    }

    private void AssertFolderFailureReported(string errorPrefix, string hint)
    {
        List<string> lines = [.. _interaction.Lines];
        int index = lines.FindIndex(line => line.StartsWith(errorPrefix, StringComparison.Ordinal));
        index.Should().BeGreaterThanOrEqualTo(0, _interaction.AllOutput);
        lines.Should().HaveCountGreaterThan(index + 1);
        lines[index + 1].Should().Be(hint);
        lines.Should().NotContain(line => line.Contains("unexpected error", StringComparison.OrdinalIgnoreCase));
        lines.Should().NotContain(line => line.Contains("partly created"));
        lines.Should().NotContain(line => line.Contains("Project initialized for"));
    }

    // Installs the init project template under root when useTemplate is set; otherwise init takes the legacy route.
    private static async Task<ITemplaterFactory?> CreateInitTemplaterFactoryAsync(string root, bool useTemplate)
    {
        if (!useTemplate)
        {
            return null;
        }

        string home = Path.Combine(root, "home");
        string package = TemplateFixtures.WriteInitProjectPackage(root);
        await TemplateFixtures.InstallAsync(package, Path.Combine(home, "templates"));
        return TemplateFixtures.CreateTemplaterFactory(home);
    }

    private static int CountOccurrences(string text, string value)
    {
        int count = 0;
        for (int index = text.IndexOf(value, StringComparison.Ordinal);
            index >= 0;
            index = text.IndexOf(value, index + value.Length, StringComparison.Ordinal))
        {
            count++;
        }

        return count;
    }

    private static void CleanupDirectory(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }

    [Fact]
    public async Task InitCommand_RejectsLanguageNotInSupportedList()
    {
        var newDir = Path.Combine(Path.GetTempPath(), $"func-init-{Guid.NewGuid():N}");
        try
        {
            var initializer = new FakeProjectInitializer("node", [_javascript, _typescript]);
            int exitCode = await RunInitAsync(newDir, initializer, language: "python", stack: "node");

            exitCode.Should().Be(1);
            initializer.WasInvoked.Should().BeFalse();
            _interaction.Lines.Should().Contain(l => l.Contains("not supported", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            CleanupDirectory(newDir);
        }
    }

    [Fact]
    public async Task InitCommand_OmitsLanguageWhenStackHasOnlySupportedLanguage()
    {
        var newDir = Path.Combine(Path.GetTempPath(), $"func-init-{Guid.NewGuid():N}");
        try
        {
            var initializer = new FakeProjectInitializer("python");
            int exitCode = await RunInitAsync(newDir, initializer, language: null, stack: "python");

            exitCode.Should().Be(0);

            // With a single supported language, the stack runtime implies the
            // language; the config skips the redundant 'language' field.
            AssertConfigJsonHasShape(newDir, expectedStack: "python", expectedLanguage: null);
        }
        finally
        {
            CleanupDirectory(newDir);
        }
    }

    [Fact]
    public async Task InitCommand_ErrorsWhenMultipleLanguagesAndNonInteractive()
    {
        var newDir = Path.Combine(Path.GetTempPath(), $"func-init-{Guid.NewGuid():N}");
        try
        {
            var initializer = new FakeProjectInitializer("node", [_javascript, _typescript]);
            int exitCode = await RunInitAsync(newDir, initializer, language: null, stack: "node");

            exitCode.Should().Be(1);
            initializer.WasInvoked.Should().BeFalse();
            _interaction.Lines.Should().Contain(l => l.Contains("--language", StringComparison.Ordinal));
        }
        finally
        {
            CleanupDirectory(newDir);
        }
    }

    [Fact]
    public void InitCommand_DedupesContributedOptionsByName()
    {
        // Two workloads contributing the same option name (e.g. --no-bundles) must
        // appear once in --help and resolve to a single canonical Option instance.
        var first = new FakeProjectInitializer(
            "node",
            optionFactory: r => [r.GetOrAdd(new Option<bool>("--no-bundles"))]);
        var second = new FakeProjectInitializer(
            "python",
            optionFactory: r => [r.GetOrAdd(new Option<bool>("--no-bundles"))]);

        var cmd = CreateCommand([first, second]);

        int matches = cmd.GetHelpOptions().Count(o => o.Name == "--no-bundles");
        matches.Should().Be(1);
        second.ContributedOptions[0].Should().BeSameAs(first.ContributedOptions[0]);
    }

    [Fact]
    public async Task InitCommand_SharedOption_IsReadCorrectlyByLaterWorkload()
    {
        // When `python` is selected but `node` was registered first, python must still see
        // the user-supplied value for the shared `--no-bundles` option.
        var newDir = Path.Combine(Path.GetTempPath(), $"func-init-{Guid.NewGuid():N}");
        try
        {
            bool? observed = null;
            var node = new FakeProjectInitializer(
                "node",
                optionFactory: r => [r.GetOrAdd(new Option<bool>("--no-bundles"))]);
            var python = new FakeProjectInitializer(
                "python",
                optionFactory: r => [r.GetOrAdd(new Option<bool>("--no-bundles"))],
                onInitialize: (init, parseResult) =>
                {
                    var opt = (Option<bool>)init.ContributedOptions[0];
                    observed = parseResult.GetValue(opt);
                });

            int exitCode = await RunInitAsync(
                newDir,
                [node, python],
                language: null,
                stack: "python",
                extraArgs: ["--no-bundles"]);

            exitCode.Should().Be(0);
            observed.Should().BeTrue();
        }
        finally
        {
            CleanupDirectory(newDir);
        }
    }

    [Fact]
    public void InitCommand_ThrowsWhenWorkloadsContributeSameNameDifferentType()
    {
        var first = new FakeProjectInitializer(
            "node",
            optionFactory: r => [r.GetOrAdd(new Option<bool>("--no-bundles"))]);
        var second = new FakeProjectInitializer(
            "python",
            optionFactory: r => [r.GetOrAdd(new Option<string>("--no-bundles"))]);

        var ex = FluentActions.Invoking(() =>
            CreateCommand([first, second])).Should().ThrowExactly<InvalidOperationException>().Which;

        ex.Message.Should().Contain("python");
        ex.Message.Should().Contain("node");
        ex.Message.Should().Contain("--no-bundles");
    }

    [Fact]
    public void InitCommand_ThrowsWhenAliasCollidesAcrossWorkloads()
    {
        // Two workloads both want -c as an alias, but for different options.
        var first = new FakeProjectInitializer(
            "node",
            optionFactory: r => [r.GetOrAdd(new Option<string>("--bundles-channel", "-c"))]);
        var second = new FakeProjectInitializer(
            "go",
            optionFactory: r => [r.GetOrAdd(new Option<string>("--clean", "-c"))]);

        var ex = FluentActions.Invoking(() =>
            CreateCommand([first, second])).Should().ThrowExactly<InvalidOperationException>().Which;

        ex.Message.Should().Contain("-c");
        ex.Message.Should().Contain("--clean");
        ex.Message.Should().Contain("--bundles-channel");
    }

    [Fact]
    public async Task InitCommand_Adopt_DetectsLanguageViaResolver_PersistsToConfig()
    {
        var newDir = Path.Combine(Path.GetTempPath(), $"func-init-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(newDir);
            File.WriteAllText(Path.Combine(newDir, "host.json"), "{}");

            _projectResolver
                .ResolveProjectAsync(Arg.Any<ProjectResolutionContext>(), Arg.Any<CancellationToken>())
                .Returns(ProjectResolutionResults.Resolved(new StubFunctionsProject("dotnet", _csharp), "stub"));

            var dotnet = new FakeProjectInitializer("dotnet", [_csharp, _fsharp]);

            int exitCode = await RunInitAsync(newDir, dotnet, language: null, stack: "dotnet");

            exitCode.Should().Be(0);
            dotnet.WasInvoked.Should().BeFalse();
            AssertConfigJsonHasShape(newDir, expectedStack: "dotnet", expectedLanguage: "csharp");
        }
        finally
        {
            CleanupDirectory(newDir);
        }
    }

    [Fact]
    public async Task InitCommand_Adopt_NoLanguageNonInteractive_RequiresLanguageBeforeWriting()
    {
        using TempDirectory directory = new PhysicalFileSystem().CreateTempDirectory();
        File.WriteAllText(Path.Combine(directory.Path, "host.json"), "{}");
        StubLocalSettingsRuntime("node");
        var node = new FakeProjectInitializer("node", [_javascript, _typescript]);

        int exitCode = await RunInitAsync(directory.Path, node, language: null);

        exitCode.Should().Be(1);
        _interaction.AllOutput.Should().Contain("--language <javascript|typescript>");
        Directory.Exists(Path.Combine(directory.Path, ".func")).Should().BeFalse();
        node.WasInvoked.Should().BeFalse();
    }

    [Fact]
    public async Task InitCommand_Adopt_CancelLanguagePicker_DoesNotWriteOrScaffold()
    {
        using TempDirectory directory = new PhysicalFileSystem().CreateTempDirectory();
        File.WriteAllText(Path.Combine(directory.Path, "host.json"), "{}");
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var node = new FakeProjectInitializer("node", [_javascript, _typescript]);
        var interaction = Substitute.For<IInteractionService>();
        interaction.IsInteractive.Returns(true);
        interaction.PromptForSelectionAsync(Arg.Any<string>(), Arg.Any<IEnumerable<LanguageDescriptor>>(), Arg.Any<Func<LanguageDescriptor, string>>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                cancellation.Cancel();
                return Task.FromCanceled<LanguageDescriptor>(call.Arg<CancellationToken>());
            });
        var root = new RootCommand { CreateCommand([node], interaction) };
        var result = root.Parse(["init", directory.Path, "--stack", "node"]);

        await FluentActions.Awaiting(() => result.InvokeAsync(new InvocationConfiguration { EnableDefaultExceptionHandler = false }, cancellation.Token))
            .Should().ThrowAsync<OperationCanceledException>();
        Directory.Exists(Path.Combine(directory.Path, ".func")).Should().BeFalse();
        node.WasInvoked.Should().BeFalse();
    }

    [Fact]
    public async Task InitCommand_Adopt_ExplicitLanguageConflictsWithResolvedProject_DoesNotWrite()
    {
        var newDir = Path.Combine(Path.GetTempPath(), $"func-init-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(newDir);
            File.WriteAllText(Path.Combine(newDir, "host.json"), "{}");

            _projectResolver
                .ResolveProjectAsync(Arg.Any<ProjectResolutionContext>(), Arg.Any<CancellationToken>())
                .Returns(ProjectResolutionResults.Resolved(new StubFunctionsProject("dotnet", _csharp), "stub"));

            var dotnet = new FakeProjectInitializer("dotnet", [_csharp, _fsharp]);

            int exitCode = await RunInitAsync(newDir, dotnet, language: "F#", stack: "dotnet");

            exitCode.Should().Be(1);
            _interaction.AllOutput.Should().Contain("--language 'F#'").And.Contain("detected project language 'csharp'");
            Directory.Exists(Path.Combine(newDir, ".func")).Should().BeFalse();
        }
        finally
        {
            CleanupDirectory(newDir);
        }
    }

    [Theory]
    [InlineData(new[] { "--does-not-exist" }, "ERROR: Unrecognized option '--does-not-exist'.")]
    [InlineData(
        new[] { "--target-framework", "net11.0" },
        "ERROR: Option '--target-framework' is not supported for an existing project "
            + "because no project content is created. Remove it, or pass --force to re-initialize.")]
    [InlineData(
        new[] { "-tfm=net11.0" },
        "ERROR: Option '-tfm' is not supported for an existing project "
            + "because no project content is created. Remove it, or pass --force to re-initialize.")]
    [InlineData(
        new[] { "--no-bundles" },
        "ERROR: Option '--no-bundles' is not supported for an existing project "
            + "because no project content is created. Remove it, or pass --force to re-initialize.")]
    public async Task InitCommand_Heal_RejectsUnusedOptionBeforeUpdatingConfig(string[] extraArgs, string expectedError)
    {
        string path = Directory.CreateTempSubdirectory("func-init-").FullName;
        try
        {
            Directory.CreateDirectory(Path.Combine(path, ".func"));
            string config = Path.Combine(path, ".func", "config.json");
            File.WriteAllText(config, """{ "stack": { "runtime": "dotnet" }, "profiles": { "keep": true } }""");
            byte[] original = File.ReadAllBytes(config);
            InteractiveTestInteractionService interactive = new();
            FakeProjectInitializer dotnet = CreateInitializerWithStackOptions("dotnet", [_csharp, _fsharp]);
            RootCommand root = [CreateCommand([dotnet], interaction: interactive)];

            int exitCode = await root.Parse(["init", path, .. extraArgs]).InvokeAsync(
                cancellationToken: TestContext.Current.CancellationToken);

            exitCode.Should().Be(1);
            interactive.Lines.Should().Equal(expectedError);
            File.ReadAllBytes(config).Should().Equal(original);
            dotnet.WasInvoked.Should().BeFalse();
        }
        finally
        {
            CleanupDirectory(path);
        }
    }

    [Fact]
    public async Task InitCommand_Heal_FillsMissingLanguageWithoutForce()
    {
        // Config with runtime but no language on a multi-language stack heals
        // in place: detector fills language, all other keys are preserved.
        var newDir = Path.Combine(Path.GetTempPath(), $"func-init-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(Path.Combine(newDir, ".func"));
            File.WriteAllText(
                Path.Combine(newDir, ".func", "config.json"),
                "{\"stack\":{\"runtime\":\"dotnet\"},\"profiles\":{\"keep\":true}}");

            _projectResolver
                .ResolveProjectAsync(Arg.Any<ProjectResolutionContext>(), Arg.Any<CancellationToken>())
                .Returns(ProjectResolutionResults.Resolved(new StubFunctionsProject("dotnet", _csharp), "stub"));

            var dotnet = new FakeProjectInitializer("dotnet", [_csharp, _fsharp]);

            int exitCode = await RunInitAsync(newDir, dotnet, language: null, stack: null);

            exitCode.Should().Be(0);
            dotnet.WasInvoked.Should().BeFalse();
            AssertConfigJsonHasShape(newDir, expectedStack: "dotnet", expectedLanguage: "csharp");

            using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(newDir, ".func", "config.json")));
            doc.RootElement.TryGetProperty("profiles", out JsonElement profiles).Should().BeTrue();
            profiles.GetProperty("keep").GetBoolean().Should().BeTrue();
        }
        finally
        {
            CleanupDirectory(newDir);
        }
    }

    [Fact]
    public async Task InitCommand_Heal_SingleLanguageStack_StillRefuses()
    {
        // Single-language stacks (e.g. python) don't persist 'language' even on
        // a fresh init, so a config with only 'runtime' is the correct shape.
        // Heal must not trigger; behaviour falls back to the existing refuse.
        var newDir = Path.Combine(Path.GetTempPath(), $"func-init-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(Path.Combine(newDir, ".func"));
            File.WriteAllText(
                Path.Combine(newDir, ".func", "config.json"),
                "{\"stack\":{\"runtime\":\"python\"}}");

            var python = new FakeProjectInitializer("python");

            int exitCode = await RunInitAsync(newDir, python, language: null, stack: null);

            exitCode.Should().Be(1);
            python.WasInvoked.Should().BeFalse();
            _interaction.Lines.Should().Contain(l => l.Contains("already contains a Functions project", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            CleanupDirectory(newDir);
        }
    }

    [Fact]
    public async Task InitCommand_Heal_ConfigAlreadyHasLanguage_StillRefuses()
    {
        // Nothing to heal: config already has both keys. The refuse path runs.
        var newDir = Path.Combine(Path.GetTempPath(), $"func-init-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(Path.Combine(newDir, ".func"));
            File.WriteAllText(
                Path.Combine(newDir, ".func", "config.json"),
                "{\"stack\":{\"runtime\":\"dotnet\",\"language\":\"C#\"}}");

            var dotnet = new FakeProjectInitializer("dotnet", [_csharp, _fsharp]);

            int exitCode = await RunInitAsync(newDir, dotnet, language: null, stack: null);

            exitCode.Should().Be(1);
            dotnet.WasInvoked.Should().BeFalse();
        }
        finally
        {
            CleanupDirectory(newDir);
        }
    }

    [Fact]
    public async Task Adopt_NativeFromDisk_PersistsGoAndLaterLocalChangesDoNotReadopt()
    {
        using TempDirectory directory = new PhysicalFileSystem().CreateTempDirectory();
        File.WriteAllText(Path.Combine(directory.Path, "host.json"), "{}");
        string settings = Path.Combine(directory.Path, "local.settings.json");
        File.WriteAllText(settings, """{"Values":{"FUNCTIONS_WORKER_RUNTIME":"native"}}""");
        var go = new FakeProjectInitializer("go", workerRuntime: "native");
        var other = new FakeProjectInitializer("native", workerRuntime: "other-host");
        var factory = Substitute.For<IFunctionsProjectFactory>();
        var project = Substitute.For<FunctionsProject>();
        project.Stack.Returns(go.Stack);
        factory.TryCreateProjectAsync(Arg.Any<ProjectCreationContext>(), Arg.Any<CancellationToken>()).Returns(ProjectCreationResults.Created(project, "Go project"));
        var owner = TestWorkloads.CreateInfo("Contoso.DevKit") with { Stack = go.Stack.Descriptor };
        var catalog = new InstalledProjectStackCatalog([other.Stack, go.Stack], [owner]);
        var local = new LocalSettingsProvider();
        var resolver = new FunctionsProjectResolver([new(owner, factory)], catalog, new ProjectStackConfiguration(new PhysicalFileSystem(), local));
        var root = new RootCommand { CreateCommand([other, go], localSettings: local, projectResolver: resolver) };

        int adopted = await root.Parse(["init", directory.Path]).InvokeAsync(cancellationToken: TestContext.Current.CancellationToken);

        adopted.Should().Be(0);
        AssertConfigJsonHasShape(directory.Path, "go", null);
        string configPath = Path.Combine(directory.Path, ".func", "config.json");
        byte[] original = File.ReadAllBytes(configPath);
        File.WriteAllText(settings, """{"Values":{"FUNCTIONS_WORKER_RUNTIME":"other-host"}}""");
        ProjectResolutionResult subsequentResolution = await resolver.ResolveProjectAsync(new(WorkingDirectory.FromExplicit(directory.Path)), TestContext.Current.CancellationToken);
        var subsequent = new RootCommand { CreateCommand([other, go], localSettings: new LocalSettingsProvider()) };
        int conflict = await subsequent.Parse(["init", directory.Path, "--stack", "native"]).InvokeAsync(cancellationToken: TestContext.Current.CancellationToken);

        subsequentResolution.Should().BeOfType<ProjectResolutionResult.Resolved>().Which.Project.Should().BeSameAs(project);
        conflict.Should().Be(1);
        _interaction.AllOutput.Should().Contain("--stack 'native'").And.Contain("stack.runtime in .func/config.json 'go'");
        File.ReadAllBytes(configPath).Should().Equal(original);
        go.WasInvoked.Should().BeFalse();
        other.WasInvoked.Should().BeFalse();
    }

    [Theory]
    [InlineData("unknown", "go", "FUNCTIONS_WORKER_RUNTIME")]
    [InlineData("go", "go", "FUNCTIONS_WORKER_RUNTIME")]
    [InlineData("native", "typo", "--stack")]
    [InlineData("native", "native", "--stack")]
    [InlineData("native", null, "multiple stacks")]
    [InlineData(" ", "go", "FUNCTIONS_WORKER_RUNTIME")]
    public async Task Adopt_InvalidOrAmbiguousSignals_DoNotCreateConfig(string runtime, string? requested, string expectedMessage)
    {
        using TempDirectory directory = new PhysicalFileSystem().CreateTempDirectory();
        File.WriteAllText(Path.Combine(directory.Path, "host.json"), "{}");
        StubLocalSettingsRuntime(runtime);
        var go = new FakeProjectInitializer("go", workerRuntime: "native");
        var comet = new FakeProjectInitializer("comet", workerRuntime: "native");

        int exitCode = await RunInitAsync(directory.Path, [go, comet], stack: requested);

        exitCode.Should().Be(1);
        if (expectedMessage == "--stack")
        {
            WorkloadHint hint = _hintRenderer.Hints.Should().ContainSingle().Subject;
            hint.Kind.Should().Be(WorkloadHintKind.NoMatchingStack);
            hint.RequestedStack.Should().Be(requested);
        }
        else
        {
            _interaction.AllOutput.Should().Contain(expectedMessage);
        }
        Directory.Exists(Path.Combine(directory.Path, ".func")).Should().BeFalse();
    }

    [Fact]
    public async Task Adopt_ExplicitIdDisambiguatesSharedRuntime_PersistsCanonicalLanguage()
    {
        using TempDirectory directory = new PhysicalFileSystem().CreateTempDirectory();
        File.WriteAllText(Path.Combine(directory.Path, "host.json"), "{}");
        StubLocalSettingsRuntime("native");
        var go = new FakeProjectInitializer("go", workerRuntime: "native");
        var comet = new FakeProjectInitializer("comet", [_javascript, _typescript], workerRuntime: "native");

        int exitCode = await RunInitAsync(directory.Path, [go, comet], language: " TS ", stack: "COMET");

        exitCode.Should().Be(0);
        AssertConfigJsonHasShape(directory.Path, "comet", "typescript");
    }

    [Theory]
    [InlineData("Fresh")]
    [InlineData("Adopt")]
    public async Task Picker_IdenticalLabels_ReturnsTheStackAndLanguageIdEvenIfLabelsAreNotAliases(string mode)
    {
        using TempDirectory directory = new PhysicalFileSystem().CreateTempDirectory();
        if (mode == "Adopt")
        {
            File.WriteAllText(Path.Combine(directory.Path, "host.json"), "{}");
        }

        var left = new FakeProjectInitializer("left", displayName: "Comet");
        var right = new FakeProjectInitializer("right", [new("type-a", "Script"), new("type-b", "Script")], displayName: "Comet");
        var interaction = Substitute.For<IInteractionService>();
        interaction.IsInteractive.Returns(true);
        string[] stackLabels = [];
        string[] languageLabels = [];
        interaction.PromptForSelectionAsync(Arg.Any<string>(), Arg.Any<IEnumerable<StackDescriptor>>(), Arg.Any<Func<StackDescriptor, string>>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var choices = call.Arg<IEnumerable<StackDescriptor>>().ToList();
                stackLabels = [.. choices.Select(call.Arg<Func<StackDescriptor, string>>())];
                return choices[1];
            });
        interaction.PromptForSelectionAsync(Arg.Any<string>(), Arg.Any<IEnumerable<LanguageDescriptor>>(), Arg.Any<Func<LanguageDescriptor, string>>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var choices = call.Arg<IEnumerable<LanguageDescriptor>>().ToList();
                languageLabels = [.. choices.Select(call.Arg<Func<LanguageDescriptor, string>>())];
                return choices[1];
            });
        var root = new RootCommand { CreateCommand([left, right], interaction) };

        int result = await root.Parse(["init", directory.Path]).InvokeAsync(cancellationToken: TestContext.Current.CancellationToken);

        result.Should().Be(0);
        stackLabels.Should().Equal("Comet (left)", "Comet (right)");
        languageLabels.Should().Equal("Script (type-a)", "Script (type-b)");
        AssertConfigJsonHasShape(directory.Path, "right", "type-b");
        if (mode == "Fresh")
        {
            right.SelectedLanguage.Should().BeSameAs(right.Stack.SupportedLanguages[1]);
        }
        else
        {
            right.WasInvoked.Should().BeFalse();
        }
    }

    [Fact]
    public async Task Init_DuplicateProviderEvenWithoutInitializer_RefusesBeforeWriting()
    {
        using TempDirectory directory = new PhysicalFileSystem().CreateTempDirectory();
        var go = new FakeProjectInitializer("go", workerRuntime: "native");
        var other = new TestProjectStack("GO", "Renamed", "native", new LanguageDescriptor("go", "Go"));
        var root = new RootCommand { CreateCommand([go], stacks: [go.Stack, other]) };

        int result = await root.Parse(["init", directory.Path, "--stack", "go"]).InvokeAsync(cancellationToken: TestContext.Current.CancellationToken);

        result.Should().Be(1);
        _interaction.AllOutput.Should().Contain("Multiple installed workloads");
        Directory.GetFileSystemEntries(directory.Path).Should().BeEmpty();
    }

    [Theory]
    [InlineData("script")]
    [InlineData("Display Only")]
    [InlineData(" ")]
    public async Task Init_ConflictingAliasesOrUnmatchedLanguage_ForceDoesNotModifyExistingConfig(string token)
    {
        using TempDirectory directory = new PhysicalFileSystem().CreateTempDirectory();
        string path = Path.Combine(directory.Path, ".func", "config.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        const string original = """{"stack":{"runtime":"go"},"keep":true}""";
        File.WriteAllText(path, original);
        var comet = new FakeProjectInitializer("comet", [new("left", "Display Only", "script"), new("right", "Right", "SCRIPT")]);

        int result = await RunInitAsync(directory.Path, comet, language: token, stack: "comet", force: true);

        result.Should().Be(1);
        comet.WasInvoked.Should().BeFalse();
        File.ReadAllText(path).Should().Be(original);
    }

    [Fact]
    public async Task Init_StackWithoutInitializer_IsNotOfferedOrSelectable()
    {
        using TempDirectory directory = new PhysicalFileSystem().CreateTempDirectory();
        var go = new FakeProjectInitializer("go");
        var other = new TestProjectStack("other", "Other", languages: [new("go", "Go")]);
        InitCommand command = CreateCommand([go], stacks: [other, go.Stack]);
        var root = new RootCommand { command };

        int result = await root.Parse(["init", directory.Path, "--stack", "other"]).InvokeAsync(cancellationToken: TestContext.Current.CancellationToken);

        result.Should().Be(1);
        command.StackOption.Description.Should().NotContain("other");
        _interaction.AllOutput.Should().Contain("does not provide an initializer");
        Directory.GetFileSystemEntries(directory.Path).Should().BeEmpty();
    }

    private sealed class StubFunctionsProject(string stackName, LanguageDescriptor? language) : FunctionsProject
    {
        private readonly WorkingDirectory _workingDirectory = WorkingDirectory.FromExplicit(Environment.CurrentDirectory);
        private readonly FunctionsWorkerReference _workerReference = FunctionsWorkerReference.FromWorkload(stackName);

        public override WorkingDirectory WorkingDirectory => _workingDirectory;

        public override IProjectStack Stack { get; } = new TestProjectStack(stackName, stackName, languages: language is null ? [] : [language]);

        public override LanguageDescriptor? Language { get; } = language;

        public override bool SupportsExtensionBundles => false;

        public override FunctionsWorkerReference WorkerReference => _workerReference;
    }

    private sealed class FakeProjectInitializer(
        string stack,
        LanguageDescriptor[]? supportedLanguages = null,
        Func<IInitOptionRegistry, IReadOnlyList<Option>>? optionFactory = null,
        Action<FakeProjectInitializer, ParseResult>? onInitialize = null,
        string? workerRuntime = null,
        string? displayName = null,
        Action<InitContext>? onConfigure = null) : IProjectInitializer
    {
        public IProjectStack Stack { get; } = new TestProjectStack(stack, displayName ?? stack, workerRuntime, supportedLanguages ?? [new(stack, stack)]);

        public bool WasInvoked { get; private set; }

        public LanguageDescriptor? SelectedLanguage { get; private set; }

        public IReadOnlyList<Option> ContributedOptions { get; private set; } = [];

        public IReadOnlyList<Option> GetInitOptions(IInitOptionRegistry registry)
        {
            ContributedOptions = optionFactory?.Invoke(registry) ?? [];
            return ContributedOptions;
        }

        public Task InitializeAsync(InitContext context, ParseResult parseResult, CancellationToken cancellationToken = default)
        {
            WasInvoked = true;
            SelectedLanguage = context.Language;
            onInitialize?.Invoke(this, parseResult);
            return Task.CompletedTask;
        }

        public int ConfigureCount { get; private set; }

        public Task ConfigureProjectAsync(InitContext context, CancellationToken cancellationToken = default)
        {
            ConfigureCount++;
            onConfigure?.Invoke(context);
            return Task.CompletedTask;
        }
    }

    private sealed class InteractiveTestInteractionService : TestInteractionService
    {
        public override bool IsInteractive => true;
    }

    /// <summary>
    /// Bind symbol source that runs <c>onCreation</c> on its second evaluation, which is the real creation's
    /// effects pass after the preflight dry run has passed.
    /// </summary>
    private sealed class SentinelBindSymbolSource(Action onCreation) : IBindSymbolSource
    {
        public const string Prefix = "funcTestsSentinel";

        public int CallCount { get; private set; }

        public string DisplayName => "Func tests sentinel";

        public string? SourcePrefix => Prefix;

        public bool RequiresPrefixMatch => true;

        public int Priority => 0;

        public Guid Id { get; } = Guid.NewGuid();

        public Task<string?> GetBoundValueAsync(
            IEngineEnvironmentSettings settings, string bindName, CancellationToken cancellationToken)
        {
            if (++CallCount == 2)
            {
                onCreation();
            }

            return Task.FromResult<string?>("value");
        }
    }

    /// <summary>
    /// Runs the command that a failed config save prints, from the project folder, exactly as printed.
    /// </summary>
    [Collection(ProcessCurrentDirectoryCollection.Name)]
    public sealed class PrintedFinishCommandTests
    {
        private const string FinishInstruction =
            "Fix the problem, then run this command in the project folder to finish the setup:";

        // A folder name with characters that cmd or PowerShell would change if the command echoed it.
        private const string ShellSpecialFolderName = "cash$ bucket 'q'";

        private readonly InitCommandTests _tests = new();

        [Fact]
        public async Task TemplateConfigurationWriteFailure_PrintedCommandFinishesSetup()
        {
            string root = Path.Combine(Path.GetTempPath(), $"func-init-template-{Guid.NewGuid():N}");
            string home = Path.Combine(root, "home");
            string output = Path.Combine(root, ShellSpecialFolderName, "App");
            try
            {
                string package = TemplateFixtures.WriteInitProjectPackage(root);

                // host.json makes the created project adoptable, which the printed command relies on.
                File.WriteAllText(Path.Combine(package, "project", "host.json"), "{}");
                await TemplateFixtures.InstallAsync(package, Path.Combine(home, "templates"));
                ITemplaterFactory factory = TemplateFixtures.CreateTemplaterFactory(home);
                Directory.CreateDirectory(output);
                string configFolder = Path.Combine(output, ".func");
                File.WriteAllText(configFolder, "user content");
                FakeProjectInitializer initializer = new("python", [_python]);

                int exitCode = await _tests.RunInitAsync(
                    output, initializer, language: "py", stack: "python", templaterFactory: factory);

                exitCode.Should().Be(1);
                initializer.WasInvoked.Should().BeFalse();
                initializer.ConfigureCount.Should().Be(1);
                File.ReadAllText(Path.Combine(output, "App.txt")).Should().Be("net10.0");
                File.ReadAllText(configFolder).Should().Be("user content");
                _tests._interaction.Lines.Should().ContainSingle(line =>
                    line.StartsWith(
                        "ERROR: The project was created, but "
                        + $"'{CliConfigurationPathsOptions.ProjectConfigDisplayPath}' couldn't be saved: ")
                    && line.EndsWith('.')
                    && !line.Contains("Could not create"));
                CountOccurrences(_tests._interaction.AllOutput, CliConfigurationPathsOptions.ProjectConfigDisplayPath)
                    .Should().Be(1);
                _tests._interaction.Lines.Should().NotContain(line => line.Contains("Project initialized for"));

                File.Delete(configFolder);
                FakeProjectInitializer retry = new("python", [_python]);
                int retryExitCode = await RunPrintedCommandAsync(output, "  func init --stack python", retry, factory);

                retryExitCode.Should().Be(0, _tests._interaction.AllOutput);
                retry.WasInvoked.Should().BeFalse();
                retry.ConfigureCount.Should().Be(0);
                AssertConfigJsonHasShape(output, expectedStack: "python", expectedLanguage: null);
                _tests._interaction.Lines.Should().Contain(line => line.Contains("Existing project adopted for"));
            }
            finally
            {
                CleanupDirectory(root);
            }
        }

        [Fact]
        public async Task LegacyConfigurationWriteFailure_PrintedCommandFinishesSetup()
        {
            using TempDirectory parent = new PhysicalFileSystem().CreateTempDirectory();
            string project = Path.Combine(parent.Path, ShellSpecialFolderName);
            Directory.CreateDirectory(project);
            string configFolder = Path.Combine(project, ".func");
            File.WriteAllText(configFolder, "user content");
            FakeProjectInitializer python = new("python", [_python], onInitialize: (_, _) =>
                File.WriteAllText(Path.Combine(project, "host.json"), "{}"));

            int exitCode = await _tests.RunInitAsync(project, python, language: "python", stack: "python");

            exitCode.Should().Be(1);
            python.WasInvoked.Should().BeTrue();
            File.Exists(Path.Combine(project, "host.json")).Should().BeTrue();
            File.ReadAllText(configFolder).Should().Be("user content");

            File.Delete(configFolder);
            FakeProjectInitializer retry = new("python", [_python]);
            int retryExitCode = await RunPrintedCommandAsync(project, "  func init --stack python", retry);

            retryExitCode.Should().Be(0, _tests._interaction.AllOutput);
            retry.WasInvoked.Should().BeFalse();
            AssertConfigJsonHasShape(project, expectedStack: "python", expectedLanguage: null);
            _tests._interaction.Lines.Should().Contain(line => line.Contains("Existing project adopted for"));
        }

        [Fact]
        public async Task LegacyConfigurationWriteFailure_PrintedCommandOmitsStackOptions()
        {
            using TempDirectory parent = new PhysicalFileSystem().CreateTempDirectory();
            string project = Path.Combine(parent.Path, ShellSpecialFolderName);
            Directory.CreateDirectory(project);
            string configFolder = Path.Combine(project, ".func");
            File.WriteAllText(configFolder, "user content");
            FakeProjectInitializer node = new(
                "node",
                [_javascript, _typescript],
                optionFactory: AddNoBundlesOption,
                onInitialize: (_, _) => File.WriteAllText(Path.Combine(project, "host.json"), "{}"));

            int exitCode = await _tests.RunInitAsync(
                project, node, language: "js", stack: "node", extraArgs: ["--no-bundles"]);

            exitCode.Should().Be(1);
            node.WasInvoked.Should().BeTrue();

            // Adopting the created project rejects stack options such as --no-bundles.
            File.Delete(configFolder);
            FakeProjectInitializer retry = new("node", [_javascript, _typescript], optionFactory: AddNoBundlesOption);
            int retryExitCode = await RunPrintedCommandAsync(
                project, "  func init --stack node --language javascript", retry);

            retryExitCode.Should().Be(0, _tests._interaction.AllOutput);
            retry.WasInvoked.Should().BeFalse();
            AssertConfigJsonHasShape(project, expectedStack: "node", expectedLanguage: "javascript");
        }

        private static IReadOnlyList<Option> AddNoBundlesOption(IInitOptionRegistry registry)
            => [registry.GetOrAdd(new Option<bool>("--no-bundles"))];

        // Checks the printed lines, then runs the printed command line itself from the project folder.
        private async Task<int> RunPrintedCommandAsync(string projectFolder, string expectedCommand,
            IProjectInitializer initializer, ITemplaterFactory? templaterFactory = null)
        {
            List<string> lines = [.. _tests._interaction.Lines];
            int index = lines.IndexOf(FinishInstruction);
            index.Should().BeGreaterThanOrEqualTo(0, _tests._interaction.AllOutput);
            lines.Should().HaveCountGreaterThan(index + 2);
            string command = lines[index + 1];
            command.Should().Be(expectedCommand);
            command.Should().NotContainAny(Path.DirectorySeparatorChar.ToString(), "$", "'", "--no-bundles");
            lines[index + 2].Should().Be($"Project folder: {Path.GetFullPath(projectFolder)}");

            string[] tokens = command.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            tokens[0].Should().Be("func");
            RootCommand root = [_tests.CreateCommand([initializer], templaterFactory: templaterFactory)];
            string currentDirectory = Directory.GetCurrentDirectory();
            Directory.SetCurrentDirectory(projectFolder);
            try
            {
                return await root.Parse(tokens[1..]).InvokeAsync(
                    cancellationToken: TestContext.Current.CancellationToken);
            }
            finally
            {
                Directory.SetCurrentDirectory(currentDirectory);
            }
        }
    }
}

/// <summary>
/// Serializes tests that change the current directory, which every test in the process shares.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class ProcessCurrentDirectoryCollection
{
    public const string Name = "Process current directory";
}
