// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.CommandLine;
using System.Text.Json;
using Azure.Functions.Cli.Bundles;
using Azure.Functions.Cli.Commands;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Configuration;
using Azure.Functions.Cli.Projects;
using Azure.Functions.Cli.Workers;
using Azure.Functions.Cli.Workloads;
using NSubstitute;

namespace Azure.Functions.Cli.Tests.Commands;

public class InitCommandTests
{
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
            .Returns(ProjectResolutionResults.NotResolved("no project"));
        _bundleReader = Substitute.For<IHostJsonBundleSectionReader>();
        _bundleReader
            .ReadAsync(Arg.Any<DirectoryInfo>(), Arg.Any<CancellationToken>())
            .Returns((HostJsonBundleSection?)null);
        _installedBundles = Substitute.For<IInstalledBundleWorkloads>();
        _installedBundles
            .ListInstalledAsync(Arg.Any<CancellationToken>())
            .Returns([]);
    }

    private InitCommand CreateCommand(IEnumerable<IProjectInitializer> initializers) =>
        new(_interaction, _hintRenderer, _localSettings, _projectResolver, _bundleReader, new InstalledBundleScanner(_installedBundles), initializers);

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
    public void InitCommand_StackOptionDescription_ListsInstalledStacks_SortedAndLowercased()
    {
        var cmd = CreateCommand(
            [new FakeProjectInitializer("Python"), new FakeProjectInitializer("dotnet"), new FakeProjectInitializer("node")]);
        string description = cmd.StackOption.Description ?? string.Empty;

        description.Should().Contain("Supported values: dotnet, node, python.");
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
    public async Task InitCommand_CreatesMissingDirectoryBeforeDispatch()
    {
        // Even when --stack is missing, InitCommand creates the target
        // directory before validating (directory creation is unconditional).
        var newDir = Path.Combine(Path.GetTempPath(), $"func-init-{Guid.NewGuid():N}");
        Directory.Exists(newDir).Should().BeFalse();

        try
        {
            var root = TestParser.CreateRoot(_interaction);
            var result = root.Parse($"init \"{newDir}\"");

            var exitCode = await result.InvokeAsync();

            // No --stack provided → exit 1, but directory was still created.
            exitCode.Should().Be(1);
            Directory.Exists(newDir).Should().BeTrue();
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
            stack.GetProperty("language").GetString().Should().Be("python");
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

            WorkloadHint hint = _hintRenderer.Hints.Should().ContainSingle().Subject;
            hint.Kind.Should().Be(WorkloadHintKind.NoWorkloadsInstalled);
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

            WorkloadHint hint = _hintRenderer.Hints.Should().ContainSingle().Subject;
            hint.Kind.Should().Be(WorkloadHintKind.AmbiguousStackChoice);
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
            AssertConfigJsonHasShape(newDir, expectedStack: "python", expectedLanguage: "python");

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
    public async Task InitCommand_Adopt_SnapsViaRuntimeAlias()
    {
        // local.settings.json declares "dotnet-isolated" but the dotnet
        // initializer owns that runtime as an alias. We should snap to the
        // initializer's canonical stack id ("dotnet"), not error.
        var newDir = Path.Combine(Path.GetTempPath(), $"func-init-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(newDir);
            File.WriteAllText(Path.Combine(newDir, "host.json"), "{}");
            StubLocalSettingsRuntime("dotnet-isolated");

            var dotnet = new FakeProjectInitializer("dotnet", workerRuntimeAliases: ["dotnet-isolated"]);
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
    public async Task InitCommand_Adopt_StackOptionAgreesViaRuntimeAlias()
    {
        // --stack dotnet against a project whose local.settings.json says
        // "dotnet-isolated" should resolve via the alias and adopt
        // (not flag a phantom conflict).
        var newDir = Path.Combine(Path.GetTempPath(), $"func-init-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(newDir);
            File.WriteAllText(Path.Combine(newDir, "host.json"), "{}");
            StubLocalSettingsRuntime("dotnet-isolated");

            var dotnet = new FakeProjectInitializer("dotnet", workerRuntimeAliases: ["dotnet-isolated"]);
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
    public async Task InitCommand_Adopt_StackOptionAlias_Recognized()
    {
        // The reverse direction: --stack dotnet-isolated (an alias) should
        // resolve to the dotnet initializer when adopting an empty
        // local.settings.json project.
        var newDir = Path.Combine(Path.GetTempPath(), $"func-init-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(newDir);
            File.WriteAllText(Path.Combine(newDir, "host.json"), "{}");

            var interactive = new InteractiveTestInteractionService();
            var dotnet = new FakeProjectInitializer("dotnet", workerRuntimeAliases: ["dotnet-isolated"]);
            var command = new InitCommand(interactive, _hintRenderer, _localSettings, _projectResolver, _bundleReader, new InstalledBundleScanner(_installedBundles), [dotnet]);
            var root = new RootCommand { command };

            int exitCode = await root.Parse(["init", newDir, "--stack", "dotnet-isolated"]).InvokeAsync();

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
    public async Task InitCommand_Adopt_UninstalledStack_RefusesAndSuggestsSetup()
    {
        // project_runtime points at a stack with no installed initializer:
        // refuse rather than write a config we can't validate. The setup
        // hint uses a `<stack>` placeholder (not the raw runtime value),
        // because the runtime string may itself not be a valid feature id.
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
                && l.Contains("'ruby' stack is not installed")
                && l.Contains("func setup --features <stack>"));
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
            var command = new InitCommand(interactive, _hintRenderer, _localSettings, _projectResolver, _bundleReader, new InstalledBundleScanner(_installedBundles), [python]);
            var root = new RootCommand { command };

            int exitCode = await root.Parse(["init", newDir]).InvokeAsync();

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
        // --stack X where X isn't installed should be refused just like the
        // uninstalled local.settings.json case. Same hint, same exit code,
        // no config written.
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
            _interaction.Lines.Should().Contain(l =>
                l.StartsWith("ERROR:")
                && l.Contains("'ruby' stack is not installed")
                && l.Contains("func setup --features <stack>"));
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
            var initializer = new FakeProjectInitializer("node", ["JavaScript", "TypeScript"]);
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
            var initializer = new FakeProjectInitializer("python", ["Python"]);
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
            var initializer = new FakeProjectInitializer("node", ["JavaScript", "TypeScript"]);
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
                .Returns(ProjectResolutionResults.Resolved(new StubFunctionsProject("dotnet", language: "C#"), "stub"));

            var dotnet = new FakeProjectInitializer(
                "dotnet",
                supportedLanguages: ["C#", "F#"],
                aliases: new Dictionary<string, IReadOnlyList<string>>
                {
                    ["C#"] = ["csharp"],
                    ["F#"] = ["fsharp"],
                });

            int exitCode = await RunInitAsync(newDir, dotnet, language: null, stack: "dotnet");

            exitCode.Should().Be(0);
            dotnet.WasInvoked.Should().BeFalse();
            AssertConfigJsonHasShape(newDir, expectedStack: "dotnet", expectedLanguage: "c#");
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

            var dotnet = new FakeProjectInitializer("dotnet", supportedLanguages: ["C#", "F#"]);

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
    public async Task InitCommand_Adopt_ExplicitLanguage_WinsOverResolver()
    {
        var newDir = Path.Combine(Path.GetTempPath(), $"func-init-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(newDir);
            File.WriteAllText(Path.Combine(newDir, "host.json"), "{}");

            _projectResolver
                .ResolveProjectAsync(Arg.Any<ProjectResolutionContext>(), Arg.Any<CancellationToken>())
                .Returns(ProjectResolutionResults.Resolved(new StubFunctionsProject("dotnet", language: "C#"), "stub"));

            var dotnet = new FakeProjectInitializer(
                "dotnet",
                supportedLanguages: ["C#", "F#"],
                aliases: new Dictionary<string, IReadOnlyList<string>>
                {
                    ["C#"] = ["csharp"],
                    ["F#"] = ["fsharp"],
                });

            int exitCode = await RunInitAsync(newDir, dotnet, language: "F#", stack: "dotnet");

            exitCode.Should().Be(0);
            AssertConfigJsonHasShape(newDir, expectedStack: "dotnet", expectedLanguage: "f#");
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
                .Returns(ProjectResolutionResults.Resolved(new StubFunctionsProject("dotnet", language: "C#"), "stub"));

            var dotnet = new FakeProjectInitializer(
                "dotnet",
                supportedLanguages: ["C#", "F#"],
                aliases: new Dictionary<string, IReadOnlyList<string>>
                {
                    ["C#"] = ["csharp"],
                    ["F#"] = ["fsharp"],
                });

            int exitCode = await RunInitAsync(newDir, dotnet, language: null, stack: null);

            exitCode.Should().Be(0);
            dotnet.WasInvoked.Should().BeFalse();
            AssertConfigJsonHasShape(newDir, expectedStack: "dotnet", expectedLanguage: "c#");

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

            var python = new FakeProjectInitializer("python", supportedLanguages: ["python"]);

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

            var dotnet = new FakeProjectInitializer("dotnet", supportedLanguages: ["C#", "F#"]);

            int exitCode = await RunInitAsync(newDir, dotnet, language: null, stack: null);

            exitCode.Should().Be(1);
            dotnet.WasInvoked.Should().BeFalse();
        }
        finally
        {
            CleanupDirectory(newDir);
        }
    }

    private sealed class StubFunctionsProject(string stackName, string? language) : FunctionsProject
    {
        private readonly WorkingDirectory _workingDirectory = WorkingDirectory.FromExplicit(Environment.CurrentDirectory);
        private readonly FunctionsWorkerReference _workerReference = FunctionsWorkerReference.FromWorkload(stackName);

        public override WorkingDirectory WorkingDirectory => _workingDirectory;

        public override string StackName { get; } = stackName;

        public override string StackDisplayName => StackName;

        public override string? Language { get; } = language;

        public override bool SupportsExtensionBundles => false;

        public override FunctionsWorkerReference WorkerReference => _workerReference;
    }

    private sealed class FakeProjectInitializer(
        string stack,
        IReadOnlyList<string>? supportedLanguages = null,
        IReadOnlyDictionary<string, IReadOnlyList<string>>? aliases = null,
        Func<IInitOptionRegistry, IReadOnlyList<Option>>? optionFactory = null,
        Action<FakeProjectInitializer, ParseResult>? onInitialize = null,
        IReadOnlyList<string>? workerRuntimeAliases = null) : IProjectInitializer
    {
        public string Stack { get; } = stack;

        public IReadOnlyList<string> SupportedLanguages { get; } = supportedLanguages ?? [];

        public IReadOnlyDictionary<string, IReadOnlyList<string>> SupportedLanguageAliases { get; } =
            aliases ?? new Dictionary<string, IReadOnlyList<string>>();

        public IReadOnlyList<string> WorkerRuntimeAliases { get; } = workerRuntimeAliases ?? [];

        public bool WasInvoked { get; private set; }

        public IReadOnlyList<Option> ContributedOptions { get; private set; } = [];

        public IReadOnlyList<Option> GetInitOptions(IInitOptionRegistry registry)
        {
            ContributedOptions = optionFactory?.Invoke(registry) ?? [];
            return ContributedOptions;
        }

        public Task InitializeAsync(InitContext context, ParseResult parseResult, CancellationToken cancellationToken = default)
        {
            WasInvoked = true;
            onInitialize?.Invoke(this, parseResult);
            return Task.CompletedTask;
        }
    }

    private sealed class InteractiveTestInteractionService : TestInteractionService
    {
        public override bool IsInteractive => true;
    }
}
