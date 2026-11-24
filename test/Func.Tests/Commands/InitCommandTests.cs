// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.CommandLine;
using System.Text.Json;
using Azure.Functions.Cli.Bundles;
using Azure.Functions.Cli.Commands;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Configuration;
using Azure.Functions.Cli.Console;
using Azure.Functions.Cli.Projects;
using Azure.Functions.Cli.Workers;
using Azure.Functions.Cli.Workloads;
using NSubstitute;

namespace Azure.Functions.Cli.Tests.Commands;

public class InitCommandTests
{
    private static readonly LanguageDescriptor _csharp = new("csharp", "C#", "c#");
    private static readonly LanguageDescriptor _fsharp = new("fsharp", "F#", "f#");
    private static readonly LanguageDescriptor _javascript = new("javascript", "JavaScript", "js");
    private static readonly LanguageDescriptor _typescript = new("typescript", "TypeScript", "ts");
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

    private InitCommand CreateCommand(IReadOnlyList<IProjectInitializer> initializers, IInteractionService? interaction = null,
        IEnumerable<IProjectStack>? stacks = null, ILocalSettingsProvider? localSettings = null, IFunctionsProjectResolver? projectResolver = null) =>
        new(interaction ?? _interaction, _hintRenderer, new ProjectStackConfiguration(new PhysicalFileSystem(), localSettings ?? _localSettings),
            projectResolver ?? _projectResolver, _bundleReader, new InstalledBundleScanner(_installedBundles),
            new InstalledProjectStackCatalog(stacks ?? initializers.Select(i => i.Stack)), initializers);

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
            [new FakeProjectInitializer("Python"), new FakeProjectInitializer("dotnet"), new FakeProjectInitializer("node")]);
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
        bool force = false)
    {
        return await RunInitAsync(path, [initializer], language, stack, force);
    }

    private async Task<int> RunInitAsync(
        string path,
        IReadOnlyList<IProjectInitializer> initializers,
        string? language = null,
        string? stack = null,
        bool force = false,
        IReadOnlyList<string>? extraArgs = null)
    {
        var cmd = CreateCommand(initializers);
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

        int matches = cmd.Options.Count(o => o.Name == "--no-bundles");
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
    public async Task InitCommand_Adopt_ResolverReturnsNullLanguage_LeavesLanguageUnset()
    {
        var newDir = Path.Combine(Path.GetTempPath(), $"func-init-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(newDir);
            File.WriteAllText(Path.Combine(newDir, "host.json"), "{}");

            _projectResolver
                .ResolveProjectAsync(Arg.Any<ProjectResolutionContext>(), Arg.Any<CancellationToken>())
                .Returns(ProjectResolutionResults.Resolved(new StubFunctionsProject("dotnet", language: null), "stub"));

            var dotnet = new FakeProjectInitializer("dotnet", [_csharp, _fsharp]);

            int exitCode = await RunInitAsync(newDir, dotnet, language: null, stack: "dotnet");

            exitCode.Should().Be(0);
            AssertConfigJsonHasShape(newDir, expectedStack: "dotnet", expectedLanguage: null);
        }
        finally
        {
            CleanupDirectory(newDir);
        }
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
        if (mode == "Fresh")
        {
            languageLabels.Should().Equal("Script (type-a)", "Script (type-b)");
            AssertConfigJsonHasShape(directory.Path, "right", "type-b");
            right.SelectedLanguage.Should().BeSameAs(right.Stack.SupportedLanguages[1]);
        }
        else
        {
            AssertConfigJsonHasShape(directory.Path, "right", null);
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
        string? displayName = null) : IProjectInitializer
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
    }

    private sealed class InteractiveTestInteractionService : TestInteractionService
    {
        public override bool IsInteractive => true;
    }
}
