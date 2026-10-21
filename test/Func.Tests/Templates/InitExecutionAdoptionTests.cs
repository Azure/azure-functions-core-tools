// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.CommandLine;
using System.Text.Json;
using System.Text.Json.Nodes;
using Azure.Functions.Cli.Commands;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Configuration;
using Azure.Functions.Cli.Console;
using Azure.Functions.Cli.Console.Theme;
using Azure.Functions.Cli.Hosting;
using Azure.Functions.Cli.Profiles;
using Azure.Functions.Cli.Projects;
using Azure.Functions.Cli.Templates;
using Azure.Functions.Cli.Templates.Engine;
using Azure.Functions.Cli.Tests.Common;
using Azure.Functions.Cli.Tests.Console;
using Azure.Functions.Cli.Workers;
using Azure.Functions.Cli.Workloads.Node;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Azure.Functions.Cli.Tests.Templates;

public sealed class InitExecutionAdoptionTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "func-init-adoption-" + Guid.NewGuid().ToString("N"));
    private readonly List<ServiceProvider> _providers = [];
    private readonly List<IServiceScope> _scopes = [];
    private readonly ITemplaterFactory _factory = Substitute.For<ITemplaterFactory>();

    private string Target => Path.Combine(_root, "project");
    private string Home => Path.Combine(_root, "home");
    private string Config => Path.Combine(Target, ".func", "config.json");

    [Fact]
    public async Task Command_ExistingRealNodeProject_AdoptsWithoutTemplatesAndRefusesRepeat()
    {
        WriteProject();
        var before = Snapshot();
        using BufferedConsole stdout = new(false, false, noColor: true);
        using BufferedConsole stderr = new(false, false, noColor: true);
        var harness = CreateHarness(new SpectreInteractionService(new DefaultTheme(), stdout, stderr));

        int exit = await harness.InvokeAsync([Target, "--output", "json"]);

        exit.Should().Be(0);
        harness.Result!.Kind.Should().Be("adopted");
        using var result = JsonDocument.Parse(stdout.Output);
        result.RootElement.GetProperty("configuration").GetProperty("stack").GetString().Should().Be("node");
        result.RootElement.GetProperty("configuration").GetProperty("language").GetString().Should().Be("JavaScript");
        result.RootElement.GetProperty("configuration").GetProperty("written").GetBoolean().Should().BeTrue();
        Snapshot().Where(pair => pair.Key != Path.GetRelativePath(_root, Config)).Should().BeEquivalentTo(before);
        AssertCanonicalConfiguration(harness, "JavaScript");
        var resolution = await harness.Provider.GetRequiredService<IFunctionsProjectResolver>()
            .ResolveProjectAsync(new(WorkingDirectory.FromExplicit(Target)), CancellationToken.None);
        resolution.Should().BeOfType<ProjectResolutionResult.Resolved>().Which.Project.Language.Should().Be("JavaScript");
        var profile = await harness.Provider.GetRequiredService<IProfileResolver>().ResolveAsync(new(new(Target), null, false), CancellationToken.None);
        profile.Should().BeOfType<ProfileResolution.None>();
        _factory.ReceivedCalls().Should().BeEmpty();
        Directory.Exists(Home).Should().BeFalse();
        var adopted = Snapshot();

        int repeated = await harness.InvokeAsync([Target]);

        repeated.Should().Be(1);
        harness.Result!.Kind.Should().Be("alreadyInitialized");
        Snapshot().Should().BeEquivalentTo(adopted);
        _factory.ReceivedCalls().Should().BeEmpty();
    }

    [Fact]
    public async Task Command_RealTypeScriptProject_RepairsOnlyMissingLanguageAndPreservesProfilesAndUnknownKeys()
    {
        WriteProject(typeScript: true);
        WriteConfig("""{"stack":{"runtime":"node","customStack":"keep"},"profiles":["flex","linux-premium"],"defaultProfile":"flex","custom":{"keep":[1,true]}}""");
        var original = JsonNode.Parse(File.ReadAllText(Config))!.AsObject();
        var before = Snapshot();
        using BufferedConsole stdout = new(false, false, noColor: true);
        using BufferedConsole stderr = new(false, false, noColor: true);
        var harness = CreateHarness(new SpectreInteractionService(new DefaultTheme(), stdout, stderr));

        int exit = await harness.InvokeAsync([Target]);

        exit.Should().Be(0);
        harness.Result!.Kind.Should().Be("repaired");
        stdout.Output.Should().Contain("Existing project repaired").And.Contain("TypeScript");
        stderr.Output.Should().BeEmpty();
        var repaired = JsonNode.Parse(File.ReadAllText(Config))!.AsObject();
        repaired["stack"]!["language"]!.GetValue<string>().Should().Be("TypeScript");
        repaired["stack"]!.AsObject().Remove("language");
        JsonNode.DeepEquals(original, repaired).Should().BeTrue();
        Snapshot().Where(pair => pair.Key != Path.GetRelativePath(_root, Config))
            .Should().BeEquivalentTo(before.Where(pair => pair.Key != Path.GetRelativePath(_root, Config)));
        AssertCanonicalConfiguration(harness, "TypeScript");
        var profile = await harness.Provider.GetRequiredService<IProfileResolver>().ResolveAsync(new(new(Target), null, false), CancellationToken.None);
        profile.Should().BeOfType<ProfileResolution.Resolved>().Which.Profile.Name.Should().Be("flex");
        _factory.ReceivedCalls().Should().BeEmpty();
        Directory.Exists(Home).Should().BeFalse();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Command_DryRunAdoptionOrRepair_ReportsOnlyPlannedConfigurationAndPhysicallyWritesNothing(bool repair)
    {
        WriteProject(typeScript: true);
        if (repair)
        {
            WriteConfig("""{"stack":{"runtime":"node","language":null},"profiles":["flex"],"custom":17}""");
        }

        var before = Snapshot();
        var directories = Directory.GetDirectories(_root, "*", SearchOption.AllDirectories);
        using BufferedConsole stdout = new(false, false, noColor: true);
        using BufferedConsole stderr = new(false, false, noColor: true);
        var harness = CreateHarness(new SpectreInteractionService(new DefaultTheme(), stdout, stderr));

        int exit = await harness.InvokeAsync([Target, "--dry-run", "--output", "json"]);

        exit.Should().Be(0);
        harness.Result!.Kind.Should().Be(repair ? "repairPreviewed" : "adoptionPreviewed");
        Snapshot().Should().BeEquivalentTo(before);
        Directory.GetDirectories(_root, "*", SearchOption.AllDirectories).Should().BeEquivalentTo(directories);
        using var json = JsonDocument.Parse(stdout.Output);
        var configuration = json.RootElement.GetProperty("configuration");
        configuration.GetProperty("path").GetString().Should().Be(Config);
        configuration.GetProperty("repair").GetBoolean().Should().Be(repair);
        configuration.GetProperty("written").GetBoolean().Should().BeFalse();
        configuration.GetProperty("language").GetString().Should().Be("TypeScript");
        json.RootElement.TryGetProperty("invocation", out _).Should().BeFalse();
        _factory.ReceivedCalls().Should().BeEmpty();
        Directory.Exists(Home).Should().BeFalse();
    }

    [Theory]
    [InlineData("""{"stack":{"runtime":"node","language":"JavaScript"}}""")]
    [InlineData("""{"stack":{"runtime":"single"}}""")]
    [InlineData("{}")]
    [InlineData("""{"stack":{"language":"JavaScript"}}""")]
    public async Task Command_InitializedOrUnsupportedPartialShape_RefusesWithoutChangingConfiguration(string config)
    {
        WriteProject();
        WriteConfig(config);
        var single = Metadata("single", ["Only"]);
        var node = Metadata("node", ["JavaScript", "TypeScript"]);
        var before = Snapshot();
        var interaction = new TestInteractionService();
        var harness = CreateHarness(interaction, initializers: [single, node]);

        int exit = await harness.InvokeAsync([Target]);

        exit.Should().Be(1);
        harness.Result!.Kind.Should().Be("alreadyInitialized");
        Snapshot().Should().BeEquivalentTo(before);
        interaction.AllOutput.Should().NotContain("SUCCESS:");
        _factory.ReceivedCalls().Should().BeEmpty();
    }

    [Theory]
    [InlineData("invalid JSON")]
    [InlineData("[]")]
    [InlineData("""{"stack":[]}""")]
    [InlineData("""{"stack":{"runtime":false}}""")]
    [InlineData("""{"stack":{"runtime":"node","language":[]}}""")]
    public async Task Command_MalformedConfiguration_IsExplicitFailureNotAdoptionOrSuccess(string config)
    {
        WriteProject();
        WriteConfig(config);
        var before = Snapshot();
        using BufferedConsole stdout = new(true, true, noColor: true);
        using BufferedConsole stderr = new(true, true, noColor: true);
        var harness = CreateHarness(new SpectreInteractionService(new DefaultTheme(), stdout, stderr));

        int exit = await harness.InvokeAsync([Target, "--output", "json"]);

        exit.Should().Be(1);
        using var json = JsonDocument.Parse(stdout.Output);
        json.RootElement.GetProperty("kind").GetString().Should().Be("invalidConfiguration");
        json.RootElement.GetProperty("succeeded").GetBoolean().Should().BeFalse();
        json.RootElement.GetProperty("diagnostics")[0].GetString().Should().Contain(".func/config.json");
        stderr.Reads.Should().Be(0);
        Snapshot().Should().BeEquivalentTo(before);
        _factory.ReceivedCalls().Should().BeEmpty();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Command_ConfigurationReadFails_IsStructuredAndLeavesInputUntouched(bool denied)
    {
        WriteProject();
        WriteConfig("""{"stack":{"runtime":"node"}}""");
        var before = Snapshot();
        var fileSystem = FileSystem();
        Exception error = denied ? new UnauthorizedAccessException("controlled read failure") : new IOException("controlled read failure");
        fileSystem.ReadAllTextIfExistsAsync(Config, Arg.Any<CancellationToken>()).Returns(Task.FromException<string?>(error));
        using BufferedConsole stdout = new(false, false, noColor: true);
        using BufferedConsole stderr = new(false, false, noColor: true);
        var harness = CreateHarness(new SpectreInteractionService(new DefaultTheme(), stdout, stderr), fileSystem);

        int exit = await harness.InvokeAsync([Target, "--output", "json"]);

        exit.Should().Be(1);
        using var json = JsonDocument.Parse(stdout.Output);
        json.RootElement.GetProperty("kind").GetString().Should().Be("invalidTarget");
        json.RootElement.GetProperty("diagnostics")[0].GetString().Should().Contain("controlled read failure");
        Snapshot().Should().BeEquivalentTo(before);
        await fileSystem.DidNotReceiveWithAnyArgs().WriteAllTextAsync(default!, default!, default);
        _factory.ReceivedCalls().Should().BeEmpty();
    }

    [Theory]
    [InlineData(false, "--template", "not-installed", "templateOnExistingProject")]
    [InlineData(true, "--template", "not-installed", "templateOnExistingProject")]
    [InlineData(false, "--unknown", "value", "invalidExistingProjectArguments")]
    [InlineData(true, "--name", "Ignored", "invalidExistingProjectArguments")]
    [InlineData(false, "--stack", "not-installed", "invalidStack")]
    [InlineData(true, "--language", "not-supported", "invalidLanguage")]
    [InlineData(false, "--stack", "", "invalidStack")]
    [InlineData(false, "--language", "", "invalidLanguage")]
    public async Task Command_ExplicitInvalidOrUnusedInput_NeverPromptsOrFallsBack(bool repair, string option, string value, string kind)
    {
        WriteProject();
        if (repair)
        {
            WriteConfig("""{"stack":{"runtime":"node"}}""");
        }

        var before = Snapshot();
        using BufferedConsole stdout = new(true, true, noColor: true);
        using BufferedConsole stderr = new(true, true, noColor: true);
        var harness = CreateHarness(new SpectreInteractionService(new DefaultTheme(), stdout, stderr));

        int exit = await harness.InvokeAsync([Target, option, value, "--output", "json"]);

        exit.Should().Be(1);
        using var json = JsonDocument.Parse(stdout.Output);
        json.RootElement.GetProperty("kind").GetString().Should().Be(kind);
        stderr.Reads.Should().Be(0);
        Snapshot().Should().BeEquivalentTo(before);
        _factory.ReceivedCalls().Should().BeEmpty();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Command_ForceExistingProject_RemainsUnsupportedWithoutDeletionEvenForDryRun(bool dryRun)
    {
        WriteProject();
        WriteConfig("""{"stack":{"runtime":"node"}}""");
        var before = Snapshot();
        var harness = CreateHarness(new TestInteractionService());
        string[] flags = dryRun ? ["--dry-run"] : [];

        int exit = await harness.InvokeAsync([Target, "--force", .. flags]);

        exit.Should().Be(1);
        harness.Result!.Kind.Should().Be("forceNotQualified");
        Snapshot().Should().BeEquivalentTo(before);
        _factory.ReceivedCalls().Should().BeEmpty();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Command_MissingStack_NeverAutoSelectsSoleInstalledStack(bool explicitNonInteractive)
    {
        WriteProject(runtime: false);
        var before = Snapshot();
        using BufferedConsole stdout = new(explicitNonInteractive, explicitNonInteractive, noColor: true);
        using BufferedConsole stderr = new(explicitNonInteractive, explicitNonInteractive, noColor: true);
        var harness = CreateHarness(new SpectreInteractionService(new DefaultTheme(), stdout, stderr));
        string[] flags = explicitNonInteractive ? ["--non-interactive"] : [];

        int exit = await harness.InvokeAsync([Target, "--language", "js", "--output", "json", .. flags]);

        exit.Should().Be(1);
        using var json = JsonDocument.Parse(stdout.Output);
        json.RootElement.GetProperty("kind").GetString().Should().Be("missingProjectStack");
        stderr.Reads.Should().Be(0);
        Snapshot().Should().BeEquivalentTo(before);
        _factory.ReceivedCalls().Should().BeEmpty();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Command_UnresolvedMultiLanguageProject_RequiresLanguageInsteadOfWritingIncompleteConfiguration(bool repair)
    {
        WriteProject(fingerprint: false);
        if (repair)
        {
            WriteConfig("""{"stack":{"runtime":"node"}}""");
        }

        var before = Snapshot();
        var harness = CreateHarness(new TestInteractionService());

        int exit = await harness.InvokeAsync([Target]);

        exit.Should().Be(1);
        harness.Result!.Kind.Should().Be("missingProjectLanguage");
        harness.Result.Diagnostics.Single().Should().Contain("--language");
        Snapshot().Should().BeEquivalentTo(before);
        _factory.ReceivedCalls().Should().BeEmpty();
    }

    [Fact]
    public async Task Command_RealSpectreAdoptionPrompts_KeepLiteralMetadataOnStderrAndOneJsonResult()
    {
        WriteProject(runtime: false, fingerprint: false);
        var metadata = Metadata("node", ["Language[bad]", "Other"]);
        metadata.DisplayName.Returns("Node[bad]");
        using BufferedConsole stdout = new(true, true, noColor: true);
        using BufferedConsole stderr = new(true, true, noColor: true);
        stderr.Enqueue(ConsoleKey.Enter);
        stderr.Enqueue(ConsoleKey.Enter);
        var harness = CreateHarness(new SpectreInteractionService(new DefaultTheme(), stdout, stderr), initializers: [metadata]);

        int exit = await harness.InvokeAsync([Target, "--output", "json"]);

        exit.Should().Be(0);
        using var json = JsonDocument.Parse(stdout.Output);
        json.RootElement.GetProperty("configuration").GetProperty("language").GetString().Should().Be("Language[bad]");
        stderr.Output.Should().Contain("Node[bad]").And.Contain("Language[bad]").And.Contain("Select the existing project's language:");
        harness.Provider.GetRequiredService<IEnumerable<IProjectInitializer>>().Single().ReceivedCalls().Should().BeEmpty();
        _factory.ReceivedCalls().Should().BeEmpty();
    }

    [Fact]
    public async Task Command_AmbiguousRuntimeAlias_DoesNotPickFirstInstalledOwner()
    {
        WriteProject();
        var first = Metadata("first", ["JavaScript"]);
        var second = Metadata("second", ["JavaScript"]);
        first.WorkerRuntimeAliases.Returns(["node"]);
        second.WorkerRuntimeAliases.Returns(["node"]);
        var before = Snapshot();
        var harness = CreateHarness(new TestInteractionService(), initializers: [first, second]);

        int exit = await harness.InvokeAsync([Target]);

        exit.Should().Be(1);
        harness.Result!.Kind.Should().Be("duplicateStacks");
        harness.Result.Diagnostics.Single().Should().Contain("first").And.Contain("second");
        Snapshot().Should().BeEquivalentTo(before);
        _factory.ReceivedCalls().Should().BeEmpty();
    }

    [Fact]
    public async Task Command_NoInstalledMetadata_ReturnsSetupGuidanceAndNeverLoadsTemplates()
    {
        WriteProject();
        var harness = CreateHarness(new TestInteractionService(), initializers: []);

        int exit = await harness.InvokeAsync([Target]);

        exit.Should().Be(1);
        harness.Result!.Kind.Should().Be("noInstalledStacks");
        harness.Result.Diagnostics.Single().Should().Contain("func setup");
        File.Exists(Config).Should().BeFalse();
        _factory.ReceivedCalls().Should().BeEmpty();
    }

    [Fact]
    public async Task Command_UninstalledProjectRuntime_RefusesInsteadOfUsingTheSoleInstalledStack()
    {
        WriteProject();
        File.WriteAllText(Path.Combine(Target, "local.settings.json"), """{"Values":{"FUNCTIONS_WORKER_RUNTIME":"java"}}""");
        var before = Snapshot();
        var harness = CreateHarness(new TestInteractionService());

        int exit = await harness.InvokeAsync([Target]);

        exit.Should().Be(1);
        harness.Result!.Kind.Should().Be("uninstalledProjectStack");
        harness.Result.Diagnostics.Single().Should().Contain("java").And.Contain("func setup");
        Snapshot().Should().BeEquivalentTo(before);
        _factory.ReceivedCalls().Should().BeEmpty();
    }

    [Fact]
    public async Task Command_ExplicitStackConflictsWithExistingRuntime_DoesNotRewriteProjectIdentity()
    {
        WriteProject();
        var before = Snapshot();
        var node = Metadata("node", ["JavaScript", "TypeScript"]);
        var other = Metadata("other", ["Other"]);
        var harness = CreateHarness(new TestInteractionService(), initializers: [node, other]);

        int exit = await harness.InvokeAsync([Target, "--stack", "other"]);

        exit.Should().Be(1);
        harness.Result!.Kind.Should().Be("projectStackConflict");
        Snapshot().Should().BeEquivalentTo(before);
        _factory.ReceivedCalls().Should().BeEmpty();
    }

    [Fact]
    public async Task Command_RepairRuntimeAndLanguageAliases_PersistCanonicalMetadataAndKeepUnknownFields()
    {
        WriteProject();
        WriteConfig("""{"stack":{"runtime":"node-alias","other":23},"extra":"keep"}""");
        var metadata = Metadata("node", ["JavaScript", "TypeScript"]);
        metadata.WorkerRuntimeAliases.Returns(["node-alias"]);
        metadata.SupportedLanguageAliases.Returns(new Dictionary<string, IReadOnlyList<string>> { ["TypeScript"] = ["ts"] });
        var harness = CreateHarness(new TestInteractionService(), initializers: [metadata]);

        int exit = await harness.InvokeAsync([Target, "--stack", "NODE-ALIAS", "--language", "TS"]);

        exit.Should().Be(0);
        AssertCanonicalConfiguration(harness, "TypeScript");
        var json = JsonNode.Parse(File.ReadAllText(Config))!;
        json["stack"]!["other"]!.GetValue<int>().Should().Be(23);
        json["extra"]!.GetValue<string>().Should().Be("keep");
        _factory.ReceivedCalls().Should().BeEmpty();
    }

    [Fact]
    public async Task Command_AmbiguousLanguageAlias_DoesNotSelectTheFirstLanguage()
    {
        WriteProject();
        var metadata = Metadata("node", ["JavaScript", "TypeScript"]);
        metadata.SupportedLanguageAliases.Returns(new Dictionary<string, IReadOnlyList<string>>
        {
            ["JavaScript"] = ["ambiguous"],
            ["TypeScript"] = ["ambiguous"],
        });
        var harness = CreateHarness(new TestInteractionService(), initializers: [metadata]);

        int exit = await harness.InvokeAsync([Target, "--language", "ambiguous"]);

        exit.Should().Be(1);
        harness.Result!.Kind.Should().Be("duplicateStacks");
        File.Exists(Config).Should().BeFalse();
        _factory.ReceivedCalls().Should().BeEmpty();
    }

    [Fact]
    public async Task Command_DuplicateStackOwnership_StopsBeforeProjectMetadataOrConfiguration()
    {
        WriteProject();
        var harness = CreateHarness(new TestInteractionService(), initializers: [Metadata("node", ["JavaScript"]), Metadata("NODE", ["JavaScript"])]);

        int exit = await harness.InvokeAsync([Target]);

        exit.Should().Be(1);
        harness.Result!.Kind.Should().Be("duplicateStacks");
        File.Exists(Config).Should().BeFalse();
        _factory.ReceivedCalls().Should().BeEmpty();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Command_PlainStructuredInputFailure_RendersErrorWithoutPromptOrSuccess(bool interactive)
    {
        WriteProject();
        using BufferedConsole stdout = new(interactive, interactive, noColor: true);
        using BufferedConsole stderr = new(interactive, interactive, noColor: true);
        var harness = CreateHarness(new SpectreInteractionService(new DefaultTheme(), stdout, stderr));

        int exit = await harness.InvokeAsync([Target, "--language", "invalid"]);

        exit.Should().Be(1);
        stdout.Output.Should().BeEmpty();
        stderr.Output.Replace(Environment.NewLine, " ").Should().Contain("Language 'invalid'");
        stderr.Reads.Should().Be(0);
        File.Exists(Config).Should().BeFalse();
        _factory.ReceivedCalls().Should().BeEmpty();
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Command_PersistenceFailure_PreservesExistingContentAndReportsNoSuccess(bool repair, bool denied)
    {
        WriteProject();
        if (repair)
        {
            WriteConfig("""{"stack":{"runtime":"node"},"custom":"keep"}""");
        }

        var before = Snapshot();
        var fileSystem = FileSystem();
        Exception failure = denied ? new UnauthorizedAccessException("controlled write failure") : new IOException("controlled write failure");
        if (repair)
        {
            fileSystem.WriteAllTextAsync(Config, Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(Task.FromException(failure));
        }
        else
        {
            fileSystem.CreateAllTextAsync(Config, Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(Task.FromException(failure));
        }
        using BufferedConsole stdout = new(false, false, noColor: true);
        using BufferedConsole stderr = new(false, false, noColor: true);
        var harness = CreateHarness(new SpectreInteractionService(new DefaultTheme(), stdout, stderr), fileSystem);

        int exit = await harness.InvokeAsync([Target, "--output", "json"]);

        exit.Should().Be(1);
        using var json = JsonDocument.Parse(stdout.Output);
        json.RootElement.GetProperty("kind").GetString().Should().Be("configurationFailed");
        json.RootElement.GetProperty("configuration").GetProperty("written").GetBoolean().Should().BeFalse();
        json.RootElement.GetProperty("diagnostics")[0].GetString().Should().Contain("controlled write failure");
        Snapshot().Should().BeEquivalentTo(before);
        if (repair)
        {
            await fileSystem.Received(1).WriteAllTextAsync(Config, Arg.Any<string>(), Arg.Any<CancellationToken>());
            await fileSystem.DidNotReceiveWithAnyArgs().CreateAllTextAsync(default!, default!, default);
        }
        else
        {
            await fileSystem.Received(1).CreateAllTextAsync(Config, Arg.Any<string>(), Arg.Any<CancellationToken>());
            await fileSystem.DidNotReceiveWithAnyArgs().WriteAllTextAsync(default!, default!, default);
        }
        _factory.ReceivedCalls().Should().BeEmpty();
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Command_CancellationDuringOrAfterWrite_StopsWithoutRenderingSuccessAndPreservesCommittedState(bool repair, bool afterCommit)
    {
        WriteProject();
        if (repair)
        {
            WriteConfig("""{"stack":{"runtime":"node"},"custom":"keep"}""");
        }

        var before = Snapshot();
        using CancellationTokenSource cancellation = new();
        var fileSystem = FileSystem();
        using BufferedConsole stdout = new(false, false, noColor: true);
        using BufferedConsole stderr = new(false, false, noColor: true);
        var harness = CreateHarness(new SpectreInteractionService(new DefaultTheme(), stdout, stderr), fileSystem);
        async Task WriteAsync(NSubstitute.Core.CallInfo call)
        {
            var token = call.ArgAt<CancellationToken>(2);
            token.Should().Be(harness.CommandToken);
            token.CanBeCanceled.Should().BeTrue();
            if (afterCommit)
            {
                var physical = new PhysicalFileSystem();
                if (repair)
                {
                    await physical.WriteAllTextAsync(Config, call.ArgAt<string>(1), token);
                }
                else
                {
                    await physical.CreateAllTextAsync(Config, call.ArgAt<string>(1), token);
                }
            }

            cancellation.Cancel();
            token.IsCancellationRequested.Should().BeTrue();
            if (!afterCommit)
            {
                token.ThrowIfCancellationRequested();
            }
        }

        if (repair)
        {
            fileSystem.WriteAllTextAsync(Config, Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(WriteAsync);
        }
        else
        {
            fileSystem.CreateAllTextAsync(Config, Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(WriteAsync);
        }
        Func<Task> act = () => harness.InvokeAsync([Target, "--output", "json"], cancellation.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        stdout.Output.Should().BeEmpty();
        harness.Result.Should().BeNull();
        if (afterCommit)
        {
            AssertCanonicalConfiguration(harness, "JavaScript");
            if (repair)
            {
                JsonNode.Parse(File.ReadAllText(Config))!["custom"]!.GetValue<string>().Should().Be("keep");
            }
        }
        else
        {
            Snapshot().Should().BeEquivalentTo(before);
        }

        if (repair)
        {
            await fileSystem.Received(1).WriteAllTextAsync(Config, Arg.Any<string>(), Arg.Any<CancellationToken>());
            await fileSystem.DidNotReceiveWithAnyArgs().CreateAllTextAsync(default!, default!, default);
        }
        else
        {
            await fileSystem.Received(1).CreateAllTextAsync(Config, Arg.Any<string>(), Arg.Any<CancellationToken>());
            await fileSystem.DidNotReceiveWithAnyArgs().WriteAllTextAsync(default!, default!, default);
        }
        _factory.ReceivedCalls().Should().BeEmpty();
    }

    [Fact]
    public async Task Command_AdoptionConfigurationAppearsAtCommit_PreservesUserBytesAndReportsFailure()
    {
        WriteProject();
        byte[] sentinel = [0, 1, 255, 42];
        var fileSystem = new CommitBoundaryFileSystem(destination => File.WriteAllBytes(destination, sentinel));
        using BufferedConsole stdout = new(false, false, noColor: true);
        using BufferedConsole stderr = new(false, false, noColor: true);
        var harness = CreateHarness(new SpectreInteractionService(new DefaultTheme(), stdout, stderr), fileSystem);

        int exit = await harness.InvokeAsync([Target, "--output", "json"]);

        exit.Should().Be(1);
        using var json = JsonDocument.Parse(stdout.Output);
        json.RootElement.GetProperty("kind").GetString().Should().Be("configurationFailed");
        json.RootElement.GetProperty("configuration").GetProperty("written").GetBoolean().Should().BeFalse();
        File.ReadAllBytes(Config).Should().Equal(sentinel);
        Directory.GetFiles(Path.GetDirectoryName(Config)!).Should().Equal(Config);
        File.ReadAllText(Path.Combine(Target, "user.txt")).Should().Be("user content must not change");
        _factory.ReceivedCalls().Should().BeEmpty();
    }

    [Fact]
    public async Task Command_PreCanceled_DoesNotInspectMetadataOrWrite()
    {
        WriteProject();
        var fileSystem = FileSystem();
        var harness = CreateHarness(new TestInteractionService(), fileSystem);
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();
        fileSystem.ClearReceivedCalls();

        Func<Task> act = () => harness.InvokeAsync([Target], cancellation.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        fileSystem.ReceivedCalls().Should().BeEmpty();
        _factory.ReceivedCalls().Should().BeEmpty();
    }

    [Fact]
    public async Task Command_RealSpectrePromptCancellation_LeavesNoConfigurationOrJsonResult()
    {
        WriteProject(runtime: false);
        var before = Snapshot();
        using BufferedConsole stdout = new(true, true, noColor: true);
        using BufferedConsole stderr = new(true, true, noColor: true);
        stderr.Enqueue(ConsoleKey.C, '\u0003', control: true);
        var harness = CreateHarness(new SpectreInteractionService(new DefaultTheme(), stdout, stderr));

        Func<Task> act = () => harness.InvokeAsync([Target, "--output", "json"]);

        await act.Should().ThrowAsync<OperationCanceledException>();
        stdout.Output.Should().BeEmpty();
        Snapshot().Should().BeEquivalentTo(before);
        _factory.ReceivedCalls().Should().BeEmpty();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("invalid")]
    [InlineData("{}")]
    [InlineData("""{"stack":{"runtime":"different"}}""")]
    [InlineData("""{"stack":{"runtime":"node","language":"TypeScript"}}""")]
    public async Task Command_RepairInputChangesDuringDetection_DoesNotReplaceMissingMalformedOrCompletedInput(string? current)
    {
        WriteProject();
        WriteConfig("""{"stack":{"runtime":"node"},"custom":"keep"}""");
        var fileSystem = FileSystem();
        string original = File.ReadAllText(Config);
        fileSystem.ReadAllTextIfExistsAsync(Config, Arg.Any<CancellationToken>()).Returns(Task.FromResult<string?>(original), Task.FromResult(current));
        var harness = CreateHarness(new TestInteractionService(), fileSystem);

        int exit = await harness.InvokeAsync([Target]);

        exit.Should().Be(1);
        harness.Result!.Kind.Should().Be(current is null or "invalid" ? "configurationFailed" : "targetChanged");
        File.ReadAllText(Config).Should().Be(original);
        await fileSystem.DidNotReceiveWithAnyArgs().WriteAllTextAsync(default!, default!, default);
        _factory.ReceivedCalls().Should().BeEmpty();
    }

    [Fact]
    public async Task Command_RepairReadCancelsAfterInspection_DoesNotStartWrite()
    {
        WriteProject();
        WriteConfig("""{"stack":{"runtime":"node"}}""");
        var fileSystem = FileSystem();
        string original = File.ReadAllText(Config);
        using CancellationTokenSource cancellation = new();
        int reads = 0;
        fileSystem.ReadAllTextIfExistsAsync(Config, Arg.Any<CancellationToken>()).Returns(_ =>
        {
            if (++reads == 2)
            {
                cancellation.Cancel();
            }

            return Task.FromResult<string?>(original);
        });
        var harness = CreateHarness(new TestInteractionService(), fileSystem);

        Func<Task> act = () => harness.InvokeAsync([Target, "--output", "json"], cancellation.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        await fileSystem.DidNotReceiveWithAnyArgs().WriteAllTextAsync(default!, default!, default);
        File.ReadAllText(Config).Should().Be(original);
        _factory.ReceivedCalls().Should().BeEmpty();
    }

    [Fact]
    public async Task Command_RepairRuntimeChangesToSharedAlias_RefusesEvenWhenSelectedStackRemainsAnOwner()
    {
        WriteProject();
        const string original = """{"stack":{"runtime":"node"},"custom":"keep"}""";
        WriteConfig(original);
        var node = Metadata("node", ["JavaScript", "TypeScript"]);
        var other = Metadata("other", ["Other"]);
        node.WorkerRuntimeAliases.Returns(["shared"]);
        other.WorkerRuntimeAliases.Returns(["shared"]);
        var fileSystem = FileSystem();
        fileSystem.ReadAllTextIfExistsAsync(Config, Arg.Any<CancellationToken>()).Returns(Task.FromResult<string?>(original),
            Task.FromResult<string?>("""{"stack":{"runtime":"shared"},"custom":"keep"}"""));
        using BufferedConsole stdout = new(false, false, noColor: true);
        using BufferedConsole stderr = new(false, false, noColor: true);
        var harness = CreateHarness(new SpectreInteractionService(new DefaultTheme(), stdout, stderr), fileSystem, [node, other]);

        int exit = await harness.InvokeAsync([Target, "--language", "JavaScript", "--non-interactive", "--output", "json"]);

        exit.Should().Be(1);
        using var json = JsonDocument.Parse(stdout.Output);
        json.RootElement.GetProperty("kind").GetString().Should().Be("duplicateStacks");
        json.RootElement.TryGetProperty("configuration", out _).Should().BeFalse();
        File.ReadAllText(Config).Should().Be(original);
        await fileSystem.DidNotReceiveWithAnyArgs().WriteAllTextAsync(default!, default!, default);
        _factory.ReceivedCalls().Should().BeEmpty();
    }

    [Fact]
    public async Task Command_AdoptionConfigAppearsBeforeWrite_DoesNotOverwriteUserConfiguration()
    {
        WriteProject();
        var fileSystem = FileSystem();
        fileSystem.FileExists(Config).Returns(false, true);
        var harness = CreateHarness(new TestInteractionService(), fileSystem);

        int exit = await harness.InvokeAsync([Target]);

        exit.Should().Be(1);
        harness.Result!.Kind.Should().Be("targetChanged");
        await fileSystem.DidNotReceiveWithAnyArgs().WriteAllTextAsync(default!, default!, default);
        File.Exists(Config).Should().BeFalse();
        _factory.ReceivedCalls().Should().BeEmpty();
    }

    [Fact]
    public async Task Command_UnexpectedWriterDefect_IsNotHiddenAsStructuredUserFailure()
    {
        WriteProject();
        var fileSystem = FileSystem();
        var failure = new NotSupportedException("controlled unexpected defect");
        fileSystem.CreateAllTextAsync(Config, Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(Task.FromException(failure));
        var harness = CreateHarness(new TestInteractionService(), fileSystem);

        Func<Task> act = () => harness.InvokeAsync([Target]);

        (await act.Should().ThrowExactlyAsync<NotSupportedException>()).Which.Should().BeSameAs(failure);
        _factory.ReceivedCalls().Should().BeEmpty();
    }

    [Fact]
    public async Task Command_RepairedCustomProfiles_RemainReadableWithoutResolvingAnAmbiguousProfileSilently()
    {
        WriteProject();
        WriteConfig("""{"stack":{"runtime":"node"},"profiles":["custom","flex"],"customKey":"keep"}""");
        File.WriteAllText(Path.Combine(Target, ".func", "profiles.json"), """{"custom":{"host":{"version":"[4.0.0,5.0.0)"}}}""");
        var before = Snapshot();
        var harness = CreateHarness(new TestInteractionService());

        int exit = await harness.InvokeAsync([Target]);

        exit.Should().Be(0);
        var resolver = harness.Provider.GetRequiredService<IProfileResolver>();
        Func<Task> ambiguous = () => resolver.ResolveAsync(new(new(Target), null, false), CancellationToken.None);
        await ambiguous.Should().ThrowAsync<ProfileConfigurationException>().WithMessage("*multiple profiles*");
        var profile = await resolver.ResolveAsync(new(new(Target), "custom", false), CancellationToken.None);
        profile.Should().BeOfType<ProfileResolution.Resolved>().Which.Profile.Name.Should().Be("custom");
        Snapshot().Where(pair => pair.Key != Path.GetRelativePath(_root, Config))
            .Should().BeEquivalentTo(before.Where(pair => pair.Key != Path.GetRelativePath(_root, Config)));
        _factory.ReceivedCalls().Should().BeEmpty();
    }

    [Fact]
    public async Task Command_SingleLanguageAdoption_AlwaysWritesCanonicalLanguageWhileLegacyConfigStillRefuses()
    {
        WriteProject();
        var metadata = Metadata("node", ["JavaScript"]);
        var harness = CreateHarness(new TestInteractionService(), initializers: [metadata]);

        int exit = await harness.InvokeAsync([Target]);

        exit.Should().Be(0);
        AssertCanonicalConfiguration(harness, "JavaScript");
        harness.Provider.GetRequiredService<IEnumerable<IProjectInitializer>>().Single().ReceivedCalls().Should().BeEmpty();
        _factory.ReceivedCalls().Should().BeEmpty();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Command_ExplicitLanguageWithConflictingRecognizedStack_RefusesBeforeAdoptionOrRepair(bool repair)
    {
        WriteProject();
        File.WriteAllText(Path.Combine(Target, "local.settings.json"), """{"Values":{"FUNCTIONS_WORKER_RUNTIME":"other"}}""");
        if (repair)
        {
            WriteConfig("""{"stack":{"runtime":"other"},"custom":"keep"}""");
        }

        var before = Snapshot();
        var other = Metadata("other", ["JavaScript", "TypeScript"]);
        var harness = CreateHarness(new TestInteractionService(), additionalInitializers: [other]);
        var recognized = await harness.Provider.GetRequiredService<IFunctionsProjectResolver>()
            .ResolveProjectAsync(new(WorkingDirectory.FromExplicit(Target)), CancellationToken.None);
        recognized.Should().BeOfType<ProjectResolutionResult.Resolved>().Which.Project.StackName.Should().Be("node");

        int exit = await harness.InvokeAsync([Target, "--stack", "other", "--language", "JavaScript", "--non-interactive"]);

        exit.Should().Be(1);
        harness.Result!.Kind.Should().Be("projectStackConflict");
        Snapshot().Should().BeEquivalentTo(before);
        _factory.ReceivedCalls().Should().BeEmpty();
    }

    [Fact]
    public async Task Command_AlternateFilesystemOnlyProject_AdoptsWithoutConsultingHostDirectoryContents()
    {
        Directory.Exists(Target).Should().BeFalse();
        var fileSystem = Substitute.For<IFileSystem>();
        fileSystem.DirectoryExists(Target).Returns(true);
        fileSystem.FileExists(Path.Combine(Target, "host.json")).Returns(true);
        fileSystem.GetFileSystemEntries(Target).Returns([Path.Combine(Target, "host.json")]);
        string? written = null;
        fileSystem.CreateAllTextAsync(Config, Arg.Do<string>(json => written = json), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        var harness = CreateHarness(new TestInteractionService(), fileSystem);

        int exit = await harness.InvokeAsync([Target, "--stack", "node", "--language", "js"]);

        exit.Should().Be(0);
        harness.Result!.Kind.Should().Be("adopted");
        JsonNode.Parse(written!)!["stack"]!["language"]!.GetValue<string>().Should().Be("JavaScript");
        Directory.Exists(Target).Should().BeFalse();
        _factory.ReceivedCalls().Should().BeEmpty();
    }

    [Fact]
    public async Task Inspector_AlternateEmptyDirectory_IgnoresUnrelatedPhysicalContents()
    {
        WriteProject();
        var fileSystem = Substitute.For<IFileSystem>();
        fileSystem.DirectoryExists(Target).Returns(true);
        fileSystem.GetFileSystemEntries(Target).Returns([]);
        var inspector = new InitExecutionTargetInspector(fileSystem);

        var observed = await inspector.InspectAsync(WorkingDirectory.FromExplicit(Target), CancellationToken.None);

        observed.State.Should().Be(InitExecutionTargetState.Empty);
        File.Exists(Path.Combine(Target, "host.json")).Should().BeTrue();
    }

    [Fact]
    public async Task Command_MatchingRealStack_ExplicitLanguageOverridesDetectedLanguageWithoutPrompt()
    {
        WriteProject();
        var interaction = new TestInteractionService();
        var harness = CreateHarness(interaction);
        var detected = await harness.Provider.GetRequiredService<IFunctionsProjectResolver>()
            .ResolveProjectAsync(new(WorkingDirectory.FromExplicit(Target)), CancellationToken.None);
        detected.Should().BeOfType<ProjectResolutionResult.Resolved>().Which.Project.Language.Should().Be("JavaScript");

        int exit = await harness.InvokeAsync([Target, "--stack", "node", "--language", "ts"]);

        exit.Should().Be(0);
        AssertCanonicalConfiguration(harness, "TypeScript");
        interaction.AllOutput.Should().NotContain("SELECT:").And.NotContain("INPUT:");
        _factory.ReceivedCalls().Should().BeEmpty();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Command_RegisteredFactoryHasNoLanguage_ExplicitLanguageDoesNotAddAPrompt(bool recognizesProject)
    {
        WriteProject(fingerprint: false);
        var factory = Substitute.For<IFunctionsProjectFactory>();
        var project = Substitute.For<FunctionsProject>();
        project.StackName.Returns("node");
        project.Language.Returns((string?)null);
        factory.TryCreateProjectAsync(Arg.Any<ProjectCreationContext>(), Arg.Any<CancellationToken>()).Returns(recognizesProject
            ? ProjectCreationResults.Created(project, "controlled registered factory has no language")
            : ProjectCreationResults.NotCreated("controlled factory does not recognize this root"));
        var interaction = new TestInteractionService();
        var harness = CreateHarness(interaction, projectFactory: factory);

        int exit = await harness.InvokeAsync([Target, "--language", "js"]);

        exit.Should().Be(0);
        AssertCanonicalConfiguration(harness, "JavaScript");
        await factory.Received(1).TryCreateProjectAsync(Arg.Any<ProjectCreationContext>(), Arg.Any<CancellationToken>());
        interaction.AllOutput.Should().NotContain("SELECT:").And.NotContain("INPUT:");
        _factory.ReceivedCalls().Should().BeEmpty();
    }

    [Fact]
    public async Task Command_RegisteredFactoryFailure_ExplicitLanguageDoesNotHideRecognitionFailure()
    {
        WriteProject();
        var factory = Substitute.For<IFunctionsProjectFactory>();
        var failure = new ProjectCreationFailure.WorkerNotResolved(
            new FunctionsWorkerResolutionFailure.NotInstalled(new("node"), "controlled worker is missing"), "controlled recognition failure");
        factory.TryCreateProjectAsync(Arg.Any<ProjectCreationContext>(), Arg.Any<CancellationToken>())
            .Returns(ProjectCreationResults.Failed(failure));
        var before = Snapshot();
        var harness = CreateHarness(new TestInteractionService(), projectFactory: factory);

        int exit = await harness.InvokeAsync([Target, "--language", "js"]);

        exit.Should().Be(1);
        harness.Result!.Kind.Should().Be("invalidProjectMetadata");
        harness.Result.Diagnostics.Single().Should().Be("controlled recognition failure");
        Snapshot().Should().BeEquivalentTo(before);
        _factory.ReceivedCalls().Should().BeEmpty();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Command_RegisteredFactoryReadError_ExplicitLanguageDoesNotHideIt(bool denied)
    {
        WriteProject();
        var factory = Substitute.For<IFunctionsProjectFactory>();
        Exception failure = denied ? new UnauthorizedAccessException("controlled metadata read failure") : new IOException("controlled metadata read failure");
        factory.TryCreateProjectAsync(Arg.Any<ProjectCreationContext>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException<ProjectCreationResult>(failure));
        var before = Snapshot();
        var harness = CreateHarness(new TestInteractionService(), projectFactory: factory);

        int exit = await harness.InvokeAsync([Target, "--language", "js"]);

        exit.Should().Be(1);
        harness.Result!.Kind.Should().Be("invalidProjectMetadata");
        harness.Result.Diagnostics.Single().Should().Contain("controlled metadata read failure");
        Snapshot().Should().BeEquivalentTo(before);
        _factory.ReceivedCalls().Should().BeEmpty();
    }

    [Fact]
    public async Task Command_RegisteredFactoryCancels_ExplicitLanguageDoesNotStartConfigurationWrite()
    {
        WriteProject();
        var factory = Substitute.For<IFunctionsProjectFactory>();
        using CancellationTokenSource cancellation = new();
        factory.TryCreateProjectAsync(Arg.Any<ProjectCreationContext>(), Arg.Any<CancellationToken>()).Returns(_ =>
        {
            cancellation.Cancel();
            return Task.FromResult(ProjectCreationResults.NotCreated("controlled cancellation"));
        });
        var fileSystem = FileSystem();
        var harness = CreateHarness(new TestInteractionService(), fileSystem, projectFactory: factory);

        Func<Task> act = () => harness.InvokeAsync([Target, "--language", "js"], cancellation.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        await fileSystem.DidNotReceiveWithAnyArgs().WriteAllTextAsync(default!, default!, default);
        File.Exists(Config).Should().BeFalse();
        _factory.ReceivedCalls().Should().BeEmpty();
    }

    [Theory]
    [InlineData(".hidden")]
    [InlineData("empty-subdirectory")]
    public async Task Inspector_PhysicalAndAlternateEntries_AreBothNonEmptyWithoutProjectMarkers(string entryName)
    {
        Directory.CreateDirectory(Target);
        string entry = Path.Combine(Target, entryName);
        if (entryName == ".hidden")
        {
            File.WriteAllText(entry, "keep");
        }
        else
        {
            Directory.CreateDirectory(entry);
        }

        var alternate = Substitute.For<IFileSystem>();
        alternate.DirectoryExists(Target).Returns(true);
        alternate.GetFileSystemEntries(Target).Returns([entry]);
        var physical = new InitExecutionTargetInspector(new PhysicalFileSystem());
        var injected = new InitExecutionTargetInspector(alternate);

        var physicalResult = await physical.InspectAsync(WorkingDirectory.FromExplicit(Target), CancellationToken.None);
        var alternateResult = await injected.InspectAsync(WorkingDirectory.FromExplicit(Target), CancellationToken.None);

        physicalResult.State.Should().Be(InitExecutionTargetState.Unsupported);
        alternateResult.Should().Be(physicalResult);
        alternate.Received(1).GetFileSystemEntries(Target);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Command_DirectoryEntryFailure_IsNotTreatedAsAnEmptyTarget(bool denied)
    {
        var fileSystem = Substitute.For<IFileSystem>();
        fileSystem.DirectoryExists(Target).Returns(true);
        Exception failure = denied ? new UnauthorizedAccessException("controlled entry read failure") : new IOException("controlled entry read failure");
        fileSystem.GetFileSystemEntries(Target).Returns(_ => throw failure);
        var harness = CreateHarness(new TestInteractionService(), fileSystem);

        int exit = await harness.InvokeAsync([Target, "--stack", "node", "--language", "js"]);

        exit.Should().Be(1);
        harness.Result!.Kind.Should().Be("invalidTarget");
        harness.Result.Diagnostics.Single().Should().Contain("controlled entry read failure");
        await fileSystem.DidNotReceiveWithAnyArgs().WriteAllTextAsync(default!, default!, default);
        _factory.ReceivedCalls().Should().BeEmpty();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Command_ExistingProject_ResolvesSharedCatalogOnceWithoutSupportOrInitializerMetadata(bool repair)
    {
        WriteProject(typeScript: true);
        if (repair)
        {
            WriteConfig("""{"stack":{"runtime":"node"},"profiles":["flex"],"unknown":"keep"}""");
        }

        var harness = CreateHarness(new TestInteractionService());

        int exit = await harness.InvokeAsync([Target, "--language", "ts"]);

        exit.Should().Be(0);
        harness.CatalogRequests.Should().Be(1);
        harness.SupportRequests.Should().Be(0);
        harness.Provider.GetRequiredService<IEnumerable<IProjectInitializer>>().Single().ReceivedCalls().Should().BeEmpty();
        AssertCanonicalConfiguration(harness, "TypeScript");
        if (repair)
        {
            JsonNode.Parse(File.ReadAllText(Config))!["unknown"]!.GetValue<string>().Should().Be("keep");
        }

        _factory.ReceivedCalls().Should().BeEmpty();
    }

    public void Dispose()
    {
        foreach (var scope in _scopes)
        {
            scope.Dispose();
        }

        foreach (var provider in _providers)
        {
            provider.Dispose();
        }

        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private Harness CreateHarness(IInteractionService interaction, IFileSystem? fileSystem = null,
        IReadOnlyList<IProjectStack>? initializers = null, IFunctionsProjectResolver? resolver = null,
        IReadOnlyList<IProjectStack>? additionalInitializers = null, IFunctionsProjectFactory? projectFactory = null)
    {
        ServiceCollection services = new();
        services.AddLogging();
        services.AddSingleton(interaction);
        services.AddSingleton<IFileSystem>(fileSystem ?? new PhysicalFileSystem());
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddSingleton(new CliConfigurationPathsOptions(Home));
        services.AddSingleton<ILocalSettingsProvider, LocalSettingsProvider>();
        services.AddSingleton<ICliConfigurationProvider, CliConfigurationProvider>();
        services.AddOptions();
        services.AddSingleton<IConfigureOptions<StackOptions>, StackOptionsSetup>();
        services.AddSingleton<IConfigureOptions<ProjectProfileOptions>, ProjectProfileOptionsSetup>();
        services.AddSingleton<ProfileDocumentParser>();
        services.AddSingleton<IProfileSource, BuiltInProfileSource>();
        services.AddSingleton<IProfileSource, ProjectProfileSource>();
        services.AddSingleton<IProfileCatalog, ProfileCatalog>();
        services.AddSingleton<IProfileResolver, ProfileResolver>();
        var builder = new DefaultFunctionsCliBuilder(services, TestWorkloads.CreateInfo("Controlled.Node"));
        if (projectFactory is not null)
        {
            builder.AddProjectFactory(projectFactory);
        }

        new NodeWorkload().Configure(builder);
        foreach (var initializer in additionalInitializers ?? [])
        {
            services.AddSingleton(initializer);
        }

        if (initializers is not null)
        {
            foreach (var registration in services.Where(descriptor => descriptor.ServiceType == typeof(IProjectStack)).ToArray())
            {
                services.Remove(registration);
            }

            foreach (var initializer in initializers)
            {
                services.AddSingleton(initializer);
            }
        }

        services.AddSingleton<IFunctionsProjectResolver, FunctionsProjectResolver>();
        if (resolver is not null)
        {
            services.AddSingleton(resolver);
        }

        foreach (var registration in services.Where(descriptor => descriptor.ServiceType == typeof(IProjectInitializer)).ToArray())
        {
            services.Remove(registration);
        }

        services.AddSingleton(Substitute.For<IProjectInitializer>());
        services.AddSingleton<InstalledProjectStackCatalog>();
        services.AddTemplatesOrchestrator();
        int catalogRequests = 0;
        int supportRequests = 0;
        services.AddSingleton<Func<InstalledProjectStackCatalog>>(provider => () =>
        {
            catalogRequests++;
            return provider.GetRequiredService<InstalledProjectStackCatalog>();
        });
        services.AddSingleton<Func<TemplateProjectSupport>>(_ => () =>
        {
            supportRequests++;
            throw new InvalidOperationException("Existing-project adoption and repair must not resolve project support.");
        });
        services.AddSingleton(_factory);
        var provider = services.BuildServiceProvider();
        _providers.Add(provider);
        var scope = provider.CreateScope();
        _scopes.Add(scope);
        var runner = new RecordingRunner(scope.ServiceProvider.GetRequiredService<IInitExecutionRunner>());
        return new(provider, new(runner, provider.GetRequiredService<InitExecutionRenderer>(), new([])), runner,
            () => catalogRequests, () => supportRequests);
    }

    private void WriteProject(bool typeScript = false, bool runtime = true, bool fingerprint = true)
    {
        Directory.CreateDirectory(Target);
        File.WriteAllText(Path.Combine(Target, "host.json"), """{"version":"2.0"}""");
        File.WriteAllText(Path.Combine(Target, "user.txt"), "user content must not change");
        if (runtime)
        {
            File.WriteAllText(Path.Combine(Target, "local.settings.json"), """{"Values":{"FUNCTIONS_WORKER_RUNTIME":"node"}}""");
        }

        if (fingerprint)
        {
            File.WriteAllText(Path.Combine(Target, "package.json"), """{"dependencies":{"@azure/functions":"4.0.0"}}""");
            if (typeScript)
            {
                File.WriteAllText(Path.Combine(Target, "tsconfig.json"), "{}");
            }
        }
    }

    private void WriteConfig(string json)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Config)!);
        File.WriteAllText(Config, json);
    }

    private Dictionary<string, string> Snapshot() => Directory.GetFiles(_root, "*", SearchOption.AllDirectories)
        .ToDictionary(path => Path.GetRelativePath(_root, path), File.ReadAllText);

    private void AssertCanonicalConfiguration(Harness harness, string language)
    {
        using var json = JsonDocument.Parse(File.ReadAllText(Config));
        json.RootElement.GetProperty("stack").GetProperty("runtime").GetString().Should().Be("node");
        json.RootElement.GetProperty("stack").GetProperty("language").GetString().Should().Be(language);
        Directory.GetFiles(Path.GetDirectoryName(Config)!, "config.json.tmp.*").Should().BeEmpty();
        harness.Provider.GetRequiredService<IOptionsMonitor<StackOptions>>().Get(Target).Language.Should().Be(language);
    }

    private static IProjectStack Metadata(string stack, IReadOnlyList<string> languages)
    {
        var metadata = Substitute.For<IProjectStack>();
        metadata.Stack.Returns(stack);
        metadata.DisplayName.Returns(stack);
        metadata.SupportedLanguages.Returns(languages);
        metadata.SupportedLanguageAliases.Returns(new Dictionary<string, IReadOnlyList<string>>());
        metadata.WorkerRuntimeAliases.Returns([]);
        return metadata;
    }

    private static IFileSystem FileSystem()
    {
        var physical = new PhysicalFileSystem();
        var fileSystem = Substitute.For<IFileSystem>();
        fileSystem.FileExists(Arg.Any<string>()).Returns(call => physical.FileExists(call.Arg<string>()));
        fileSystem.DirectoryExists(Arg.Any<string>()).Returns(call => physical.DirectoryExists(call.Arg<string>()));
        fileSystem.GetFileSystemEntries(Arg.Any<string>()).Returns(call => physical.GetFileSystemEntries(call.Arg<string>()));
        fileSystem.ReadAllTextIfExistsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(call => physical.ReadAllTextIfExistsAsync(call.ArgAt<string>(0), call.ArgAt<CancellationToken>(1)));
        fileSystem.WriteAllTextAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(call => physical.WriteAllTextAsync(call.ArgAt<string>(0), call.ArgAt<string>(1), call.ArgAt<CancellationToken>(2)));
        fileSystem.CreateAllTextAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(call => physical.CreateAllTextAsync(call.ArgAt<string>(0), call.ArgAt<string>(1), call.ArgAt<CancellationToken>(2)));
        return fileSystem;
    }

    private sealed class Harness(ServiceProvider provider, InitExecutionCommand command, RecordingRunner runner,
        Func<int> catalogRequests, Func<int> supportRequests)
    {
        public ServiceProvider Provider => provider;
        public InitExecutionResult? Result => runner.Result;
        public CancellationToken CommandToken => runner.CommandToken;
        public int CatalogRequests => catalogRequests();
        public int SupportRequests => supportRequests();

        public Task<int> InvokeAsync(string[] args, CancellationToken cancellationToken = default) =>
            command.ParseInvocation(args).InvokeAsync(new InvocationConfiguration { EnableDefaultExceptionHandler = false }, cancellationToken);
    }

    private sealed class RecordingRunner(IInitExecutionRunner inner) : IInitExecutionRunner
    {
        public InitExecutionResult? Result { get; private set; }
        public CancellationToken CommandToken { get; private set; }

        public async Task<InitExecutionResult> ExecuteAsync(
            InitExecutionRequest request, TemplateConstraintEvaluationSnapshot availability, CancellationToken cancellationToken)
        {
            Result = null;
            CommandToken = cancellationToken;
            Result = await inner.ExecuteAsync(request, availability, cancellationToken);
            return Result;
        }
    }
}
