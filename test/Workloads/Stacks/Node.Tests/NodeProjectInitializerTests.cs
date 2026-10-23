// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.CommandLine;
using System.Text.Json;
using System.Text.Json.Nodes;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Commands;
using Azure.Functions.Cli.Projects;
using NSubstitute;

namespace Azure.Functions.Cli.Workloads.Node.Tests;

public class NodeProjectInitializerTests : IDisposable
{
    [Fact]
    public void Constructor_NullStack_Throws()
    {
        FluentActions.Invoking(() => new NodeProjectInitializer(null!, new NodeNpmInstaller(), _reporter))
            .Should().ThrowExactly<ArgumentNullException>().WithParameterName("stack");
    }

    [Fact]
    public void Constructor_NullNpmInstaller_Throws()
    {
        FluentActions.Invoking(() => new NodeProjectInitializer(new NodeProjectStack(), null!, _reporter))
            .Should().ThrowExactly<ArgumentNullException>().WithParameterName("npmInstaller");
    }

    [Fact]
    public void Constructor_NullReporter_Throws()
    {
        FluentActions.Invoking(() => new NodeProjectInitializer(new NodeProjectStack(), new NodeNpmInstaller(), null!))
            .Should().ThrowExactly<ArgumentNullException>().WithParameterName("reporter");
    }

    [Fact]
    public async Task RunNpmInstall_DefaultDelegate_UsesInjectedOwnedHelperWithoutScaffolding()
    {
        var npm = new RecordingNpmInstaller();
        NodeProjectInitializer initializer = new(new NodeProjectStack(), npm, _reporter);
        using var cancellation = new CancellationTokenSource();

        (int ExitCode, string Stderr) result = await initializer.RunNpmInstall(_projectDir.FullName, cancellation.Token);

        result.Should().Be((7, "owned helper stderr"));
        npm.Path.Should().Be(_projectDir.FullName);
        npm.Token.Should().Be(cancellation.Token);
        _projectDir.GetFileSystemInfos().Should().BeEmpty();
    }

    private readonly DirectoryInfo _projectDir;
    private readonly IProjectInitializationReporter _reporter = Substitute.For<IProjectInitializationReporter>();

    public NodeProjectInitializerTests()
    {
        _projectDir = Directory.CreateDirectory(
            Path.Combine(Path.GetTempPath(), "func-node-init-" + Guid.NewGuid().ToString("N")));
    }

    public void Dispose()
    {
        try
        {
            if (_projectDir.Exists)
            {
                _projectDir.Delete(recursive: true);
            }
        }
        catch (IOException)
        {
        }
    }

    [Fact]
    public void Stack_IsNode()
    {
        new NodeProjectInitializer(new NodeProjectStack(), new NodeNpmInstaller(), _reporter).Stack.Should().Be("node");
    }

    [Theory]
    [InlineData(null, "JavaScript")]
    [InlineData("", "JavaScript")]
    [InlineData("js", "JavaScript")]
    [InlineData("JS", "JavaScript")]
    [InlineData("JavaScript", "JavaScript")]
    [InlineData("ts", "TypeScript")]
    [InlineData("TS", "TypeScript")]
    [InlineData("TypeScript", "TypeScript")]
    [InlineData("unknown", "unknown")]
    public void NormalizeLanguage_ReturnsExpectedResult(string? input, string expected)
    {
        new NodeProjectInitializer(new NodeProjectStack(), new NodeNpmInstaller(), _reporter).NormalizeLanguage(input).Should().Be(expected);
    }

    [Fact]
    public async Task InitializeAsync_UnsupportedLanguage_Throws()
    {
        ArgumentException ex = (await FluentActions.Awaiting(() => RunAsync(language: "ruby", force: false)).Should().ThrowAsync<ArgumentException>()).Which;

        ex.Message.Should().Contain("ruby");
    }

    [Fact]
    public async Task InitializeAsync_AliasTs_ScaffoldsTypeScript()
    {
        await RunAsync(language: "ts", force: false);

        File.Exists(Path.Combine(_projectDir.FullName, "tsconfig.json")).Should().BeTrue();
    }

    [Fact]
    public async Task InitializeAsync_AliasJs_ScaffoldsJavaScript()
    {
        await RunAsync(language: "js", force: false);

        File.Exists(Path.Combine(_projectDir.FullName, "package.json")).Should().BeTrue();
        File.Exists(Path.Combine(_projectDir.FullName, "tsconfig.json")).Should().BeFalse();
    }

    [Fact]
    public void GetInitOptions_RegistersBundleAndNpmOptions()
    {
        RootCommand root = [];
        IReadOnlyList<Option> options = new NodeProjectInitializer(new NodeProjectStack(), new NodeNpmInstaller(), _reporter).GetInitOptions(new InitOptionRegistry(root));
        IReadOnlyList<string> names = [.. options.Select(o => o.Name)];

        names.Should().Contain("--no-bundles");
        names.Should().Contain("--bundles-channel");
        names.Should().Contain("--skip-npm-install");
        options.Should().OnlyContain(option =>
            option.Description != null &&
            !string.IsNullOrWhiteSpace(option.Description) &&
            !option.Description.Contains('\r') &&
            !option.Description.Contains('\n'));
    }

    [Fact]
    public async Task InitializeAsync_DefaultsToJavaScript()
    {
        await RunAsync(language: null, force: false);

        File.Exists(Path.Combine(_projectDir.FullName, "package.json")).Should().BeTrue();
        File.Exists(Path.Combine(_projectDir.FullName, "tsconfig.json")).Should().BeFalse();

        using var pkg = JsonDocument.Parse(File.ReadAllText(Path.Combine(_projectDir.FullName, "package.json")));
        pkg.RootElement.GetProperty("main").GetString().Should().Be("src/functions/*.js");
    }

    [Fact]
    public async Task InitializeAsync_TypeScript_WritesTsConfigAndTsPackageJson()
    {
        await RunAsync(language: "typescript", force: false);

        File.Exists(Path.Combine(_projectDir.FullName, "tsconfig.json")).Should().BeTrue();
        using var pkg = JsonDocument.Parse(File.ReadAllText(Path.Combine(_projectDir.FullName, "package.json")));
        pkg.RootElement.GetProperty("scripts").GetProperty("build").GetString().Should().Contain("tsc");
    }

    [Fact]
    public async Task InitializeAsync_SubstitutesProjectName()
    {
        await RunAsync(language: null, force: false, projectName: "My Cool App");

        using var pkg = JsonDocument.Parse(File.ReadAllText(Path.Combine(_projectDir.FullName, "package.json")));
        pkg.RootElement.GetProperty("name").GetString().Should().Be("my-cool-app");
    }

    [Fact]
    public async Task InitializeAsync_WritesCommonScaffolding()
    {
        await RunAsync(language: null, force: false);

        File.Exists(Path.Combine(_projectDir.FullName, ".funcignore")).Should().BeTrue();
        File.Exists(Path.Combine(_projectDir.FullName, ".gitignore")).Should().BeTrue();
        Directory.Exists(Path.Combine(_projectDir.FullName, "src", "functions")).Should().BeTrue();

        using var settings = JsonDocument.Parse(File.ReadAllText(Path.Combine(_projectDir.FullName, "local.settings.json")));
        settings.RootElement.GetProperty("Values").GetProperty("FUNCTIONS_WORKER_RUNTIME").GetString().Should().Be("node");
    }

    [Fact]
    public async Task InitializeAsync_AppendsExtensionBundle_IntoExistingHostJson()
    {
        File.WriteAllText(Path.Combine(_projectDir.FullName, "host.json"), "{ \"version\": \"2.0\" }");

        await RunAsync(language: null, force: false);

        JsonNode? bundle = JsonNode.Parse(File.ReadAllText(Path.Combine(_projectDir.FullName, "host.json")))!["extensionBundle"];
        bundle.Should().NotBeNull();
        bundle!["id"]!.GetValue<string>().Should().Be("Microsoft.Azure.Functions.ExtensionBundle");
    }

    [Fact]
    public async Task InitializeAsync_IsIdempotent_WithoutForce()
    {
        await RunAsync(language: null, force: false);
        const string userEdit = "{ \"name\": \"user-edited\" }";
        File.WriteAllText(Path.Combine(_projectDir.FullName, "package.json"), userEdit);

        await RunAsync(language: null, force: false);

        File.ReadAllText(Path.Combine(_projectDir.FullName, "package.json")).Should().Be(userEdit);
    }

    [Fact]
    public async Task InitializeAsync_ForceOverwrites()
    {
        await RunAsync(language: null, force: false);
        File.WriteAllText(Path.Combine(_projectDir.FullName, "package.json"), "stale");

        await RunAsync(language: null, force: true);

        File.ReadAllText(Path.Combine(_projectDir.FullName, "package.json")).Should().Contain("@azure/functions");
    }

    [Fact]
    public async Task InitializeAsync_NoBundle_WritesMinimalHostJsonWithoutExtensionBundle()
    {
        await RunAsync(language: null, force: false, args: ["--no-bundles"]);

        string hostJsonPath = Path.Combine(_projectDir.FullName, "host.json");
        File.Exists(hostJsonPath).Should().BeTrue("host.json should be created even with --no-bundles");
        string content = File.ReadAllText(hostJsonPath);
        content.Should().Contain("\"version\"");
        content.Should().NotContain("extensionBundle");
    }

    [Fact]
    public async Task InitializeAsync_BundlesChannel_Preview_WritesPreviewBundleId()
    {
        await RunAsync(language: null, force: false, args: ["--bundles-channel", "Preview"]);

        JsonNode? bundle = JsonNode.Parse(File.ReadAllText(Path.Combine(_projectDir.FullName, "host.json")))!["extensionBundle"];
        bundle!["id"]!.GetValue<string>().Should().Be("Microsoft.Azure.Functions.ExtensionBundle.Preview");
    }

    [Fact]
    public async Task InitializeAsync_RunsNpmInstallByDefault()
    {
        int npmCalls = 0;
        await RunAsync(language: null, force: false, configure: init =>
        {
            init.RunNpmInstall = (_, _) =>
            {
                npmCalls++;
                return Task.FromResult((0, string.Empty));
            };
        });

        npmCalls.Should().Be(1);
    }

    [Fact]
    public async Task InitializeAsync_SkipNpmInstall_DoesNotInvokeNpm()
    {
        int npmCalls = 0;
        await RunAsync(language: null, force: false, args: ["--skip-npm-install"], configure: init =>
        {
            init.RunNpmInstall = (_, _) =>
            {
                npmCalls++;
                return Task.FromResult((0, string.Empty));
            };
        });

        npmCalls.Should().Be(0);
        _reporter.DidNotReceive().WriteWarning(Arg.Any<string>());
    }

    [Fact]
    public async Task InitializeAsync_NpmInstallFailure_DoesNotThrow()
    {
        await RunAsync(language: null, force: false, configure: init =>
        {
            init.RunNpmInstall = (_, _) => Task.FromResult((1, "npm: command not found"));
        });

        File.Exists(Path.Combine(_projectDir.FullName, "package.json")).Should().BeTrue();
        _reporter.Received(1).WriteWarning(Arg.Is<string>(message => message.Contains("not installed") && message.Contains("npm install")));
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, true)]
    [InlineData(-1, true)]
    public async Task InitializeAsync_NpmOutcome_WarnsOnlyForFailureWithoutDumpingStderr(int exitCode, bool warning)
    {
        await RunAsync(language: null, force: false, configure: init =>
            init.RunNpmInstall = (_, _) => Task.FromResult((exitCode, "[red]private dependency output/token[/]")));

        if (warning)
        {
            _reporter.Received(1).WriteWarning("Node dependencies were not installed. The generated project files were retained. Run 'npm install' in the project directory.");
        }
        else
        {
            _reporter.DidNotReceive().WriteWarning(Arg.Any<string>());
        }
        File.Exists(Path.Combine(_projectDir.FullName, "package.json")).Should().BeTrue();
        File.Exists(Path.Combine(_projectDir.FullName, "host.json")).Should().BeTrue();
        Directory.Exists(Path.Combine(_projectDir.FullName, "src", "functions")).Should().BeTrue();
    }

    [Fact]
    public async Task InitializeAsync_LegacyCancellationResult_WarnsAndPreservesScaffoldSuccess()
    {
        using var cancellation = new CancellationTokenSource();
        NodeNpmInstaller npm = Substitute.For<NodeNpmInstaller>();
        npm.InstallAsync(_projectDir.FullName, cancellation.Token).Returns(_ =>
        {
            cancellation.Cancel();
            return (-1, "legacy cancellation");
        });
        NodeProjectInitializer initializer = new(new NodeProjectStack(), npm, _reporter);
        RootCommand root = [];
        initializer.GetInitOptions(new InitOptionRegistry(root));

        await initializer.InitializeAsync(new(WorkingDirectory.FromExplicit(_projectDir.FullName), "test", null, false),
            root.Parse([]), cancellation.Token);

        File.Exists(Path.Combine(_projectDir.FullName, "package.json")).Should().BeTrue();
        _reporter.Received(1).WriteWarning(Arg.Is<string>(message => message.Contains("npm install")));
        await npm.DidNotReceive().InstallRequiredAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    private Task RunAsync(
        string? language,
        bool force,
        string? projectName = "test",
        string[]? args = null,
        Action<NodeProjectInitializer>? configure = null)
    {
        NodeProjectInitializer initializer = new(new NodeProjectStack(), new NodeNpmInstaller(), _reporter)
        {
            // Stub by default so tests don't spawn real npm.
            RunNpmInstall = (_, _) => Task.FromResult((0, string.Empty)),
        };
        configure?.Invoke(initializer);

        InitContext context = new(
            WorkingDirectory.FromExplicit(_projectDir.FullName),
            ProjectName: projectName,
            Language: language,
            Force: force);

        RootCommand root = [];
        var registry = new InitOptionRegistry(root);
        initializer.GetInitOptions(registry);

        ParseResult parseResult = root.Parse(args ?? []);
        return initializer.InitializeAsync(context, parseResult);
    }

    private sealed class RecordingNpmInstaller : NodeNpmInstaller
    {
        public string? Path { get; private set; }
        public CancellationToken Token { get; private set; }

        public override Task<(int ExitCode, string Stderr)> InstallAsync(string workingDirectory, CancellationToken cancellationToken)
        {
            Path = workingDirectory;
            Token = cancellationToken;
            return Task.FromResult((7, "owned helper stderr"));
        }
    }
}
