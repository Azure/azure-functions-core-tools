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
using Azure.Functions.Cli.Tests.Console;
using Azure.Functions.Cli.Tests.Templates.Engine;
using Azure.Functions.Cli.Workloads.Node;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.TemplateEngine.Abstractions;
using Microsoft.TemplateEngine.Edge.Template;
using NSubstitute;
using NuGet.Versioning;

namespace Azure.Functions.Cli.Tests.Templates;

public sealed class InitExecutionJourneyTests : IDisposable
{
    private const string Low = "Func.Init.Low";
    private const string Parameters = """
        {
          "Greeting":{"type":"parameter","datatype":"string","isRequired":true,"replaces":"GREETING"},
          "Auth":{"type":"parameter","datatype":"choice","isRequired":true,"defaultValue":"anonymous",
            "choices":[{"choice":"anonymous"},{"choice":"function"}],"replaces":"AUTH"},
          "Enabled":{"type":"parameter","datatype":"Boolean","defaultValue":"false","replaces":"ENABLED"},
          "Optional":{"type":"parameter","datatype":"string","defaultValue":"leave","replaces":"OPTIONAL"},
          "Execution":{"type":"bind","binding":"host:WorkingDirectory","replaces":"EXECUTION"},
          "Root":{"type":"bind","binding":"host:func:project-root","replaces":"ROOT"},
          "Stack":{"type":"bind","binding":"host:func:stack","defaultValue":"no-stack","replaces":"STACK"},
          "Language":{"type":"bind","binding":"host:func:language","defaultValue":"no-language","replaces":"LANGUAGE"}
        }
        """;
    private readonly string _root = Path.Combine(Path.GetTempPath(), "func-init-consumer-" + Guid.NewGuid().ToString("N"));
    private readonly TestInteractionService _interaction = new();
    private readonly List<ServiceProvider> _providers = [];
    private readonly List<IServiceScope> _scopes = [];

    private string Target => Path.Combine(_root, "target");
    private string Home => Path.Combine(_root, "home");
    private string Package => Path.Combine(_root, "package");

    [Fact]
    public async Task Command_RealInstalledProject_PreviewsThenCreatesCanonicalConfigurationAndReadableProject()
    {
        WriteVariants();
        await InstallAsync();
        var harness = CreateHarness();
        string[] args = [Target, "--template", "project", "--stack", "NODE", "--language", "js", "--name", "NamedProject",
            "--greeting", "Hello", "--param:Auth", "function", "--Enabled", "true", "--output", "json"];

        int previewExit = await harness.InvokeAsync([.. args, "--dry-run"]);
        var preview = harness.Result!;

        previewExit.Should().Be(0);
        preview.SelectedTemplate!.Identity.Should().Be(Low);
        preview.SelectedTemplate.ProjectConfigurations.Should().ContainSingle().Which
            .Should().Be(new TemplateProjectConfigurationDeclaration(0, "host.json", "node", "JavaScript"));
        preview.Context!.CommandDirectory.Info.FullName.Should().Be(Target);
        preview.Context.Project!.RootDirectory.Info.FullName.Should().Be(Target);
        preview.Context.Project.Stack.Should().BeNull();
        preview.Context.Project.Language.Should().BeNull();
        preview.Context.Bundle.Should().BeNull();
        preview.Preview!.ProjectConfigurations.Should().ContainSingle().Which.Should()
            .Be(new TemplateProjectConfigurationPlan(Path.Combine(Target, "host.json"), "node", "JavaScript"));
        preview.Preview.ConfiguredProjects.Should().BeEmpty();
        preview.Preview.Changes.Should().Contain(change => change.Path.Replace('\\', '/') == ".func/config.json");
        preview.Invocation.Should().BeNull();
        Directory.Exists(Target).Should().BeFalse();
        AssertJson("previewed", planned: 1, completed: 0);

        int createExit = await harness.InvokeAsync(args);

        createExit.Should().Be(0);
        harness.Result!.Kind.Should().Be("created");
        File.ReadAllText(Path.Combine(Target, "context.txt")).Should()
            .Be($"NamedProject|Hello|function|True|leave|{Target}|{Target}|no-stack|no-language");
        string config = Path.Combine(Target, ".func", "config.json");
        using var json = JsonDocument.Parse(File.ReadAllText(config));
        json.RootElement.EnumerateObject().Select(property => property.Name).Should().Equal("stack");
        json.RootElement.GetProperty("stack").EnumerateObject().Select(property => property.Name).Should().Equal("runtime", "language");
        json.RootElement.GetProperty("stack").GetProperty("runtime").GetString().Should().Be("node");
        json.RootElement.GetProperty("stack").GetProperty("language").GetString().Should().Be("JavaScript");
        Directory.GetFiles(Path.GetDirectoryName(config)!).Should().Equal(config);
        harness.Provider.GetRequiredService<IOptionsMonitor<StackOptions>>().Get(Target).Language.Should().Be("JavaScript");
        var resolution = await harness.Provider.GetRequiredService<IFunctionsProjectResolver>()
            .ResolveProjectAsync(new(WorkingDirectory.FromExplicit(Target)), CancellationToken.None);
        resolution.Should().BeOfType<ProjectResolutionResult.Resolved>().Which.Project.StackName.Should().Be("node");
        var profile = await harness.Provider.GetRequiredService<IProfileResolver>().ResolveAsync(new(new(Target), null, false), CancellationToken.None);
        profile.Should().BeOfType<ProfileResolution.None>();
        AssertJson("created", planned: 1, completed: 1);
        harness.Factory.Contexts.Should().HaveCount(2).And.OnlyHaveUniqueItems();
        await AssertDisposedAsync(harness.Factory);
    }

    [Fact]
    public async Task Command_SoleGroupAndVariant_AutoSelectWithoutStackLanguageOrNameDefaults()
    {
        WriteProject("sole", Low);
        await InstallAsync();
        var harness = CreateHarness();

        int exit = await harness.InvokeAsync([Target, "--non-interactive", "--dry-run"]);

        exit.Should().Be(0);
        harness.Result!.SelectedTemplate!.Identity.Should().Be(Low);
        harness.Result.Context!.Project!.Stack.Should().BeNull();
        harness.Result.Context.Project.Language.Should().BeNull();
        _interaction.AllOutput.Should().NotContain("SELECT:").And.NotContain("INPUT:");
        Directory.Exists(Target).Should().BeFalse();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Command_DryRunPendingJournal_RefusesWithoutRecoveryOrNativeBootstrap(bool corrupt)
    {
        WriteProject("valid", Low);
        await InstallAsync();
        var files = new PhysicalFileSystem();
        var storeLock = Substitute.For<ITemplateStoreLock>();
        storeLock.AcquireAsync(Arg.Any<string>(), Arg.Any<TemplateStoreAccess>(), Arg.Any<CancellationToken>(),
            Arg.Any<Action<TemplateStoreWaitReason>?>()).Returns(call => new FileTemplateStoreLock().AcquireAsync(
                call.Arg<string>(), call.Arg<TemplateStoreAccess>(), call.Arg<CancellationToken>(),
                call.Arg<Action<TemplateStoreWaitReason>?>()));
        var loggers = Substitute.For<ILoggerFactory>();
        var store = new TemplateStore(new(Home), files, storeLock, loggers);
        files.CopyDirectory(store.Hive, store.Backup);
        File.WriteAllText(Path.Combine(store.Hive, "uncommitted.txt"), "Preserve until an owning lifecycle recovers.");
        File.WriteAllText(store.Journal, corrupt ? "corrupt" : "present");
        var hive = StoreBytes(store.Hive);
        var backup = StoreBytes(store.Backup);
        byte[] journal = File.ReadAllBytes(store.Journal);
        var harness = CreateHarness(templateFactory: new TemplaterFactory(store, files));

        var failure = await Record.ExceptionAsync(() =>
            harness.InvokeAsync([Target, "--template", Low, "--dry-run", "--output", "json"]));

        StoreBytes(store.Hive).Should().BeEquivalentTo(hive);
        StoreBytes(store.Backup).Should().BeEquivalentTo(backup);
        File.ReadAllBytes(store.Journal).Should().Equal(journal);
        Directory.Exists(Target).Should().BeFalse();
        failure.Should().BeOfType<TemplateStoreRecoveryRequiredException>().Which.Message.Should().Contain("owning lifecycle");
        harness.Factory.Contexts.Should().ContainSingle().Which.RequireCleanStore.Should().BeTrue();
        harness.Factory.Templaters.Should().BeEmpty();
        loggers.DidNotReceive().CreateLogger(Arg.Any<string>());
        await storeLock.DidNotReceive().AcquireAsync(Arg.Any<string>(), TemplateStoreAccess.Write,
            Arg.Any<CancellationToken>(), Arg.Any<Action<TemplateStoreWaitReason>?>());
        harness.Result.Should().BeNull();
        _interaction.AllOutput.Should().NotContain("JSON:").And.NotContain("SUCCESS:");
    }

    [Fact]
    public async Task Command_DryRunCleanStore_PreservesLiveHiveAndTarget()
    {
        WriteProject("valid", Low);
        await InstallAsync();
        var before = StoreBytes(Path.Combine(Home, "templates"));
        var harness = CreateHarness();

        int exit = await harness.InvokeAsync([Target, "--template", Low, "--dry-run", "--output", "json"]);

        exit.Should().Be(0);
        harness.Result!.Context!.RequireCleanStore.Should().BeTrue();
        StoreBytes(Path.Combine(Home, "templates")).Should().BeEquivalentTo(before);
        Directory.Exists(Target).Should().BeFalse();
        AssertJson("previewed", planned: 1, completed: 0);
        await AssertDisposedAsync(harness.Factory);
    }

    [Fact]
    public async Task Command_CreatePendingJournal_RetainsOrdinaryRecoveryAndConfiguration()
    {
        WriteProject("valid", Low);
        await InstallAsync();
        var files = new PhysicalFileSystem();
        var store = new TemplateStore(new(Home), files, new FileTemplateStoreLock(), NullLoggerFactory.Instance);
        files.CopyDirectory(store.Hive, store.Backup);
        var backup = StoreBytes(store.Backup);
        string uncommitted = Path.Combine(store.Hive, "uncommitted.txt");
        File.WriteAllText(uncommitted, "Unfinished store mutation.");
        File.WriteAllText(store.Journal, "present");
        var harness = CreateHarness();

        int exit = await harness.InvokeAsync([Target, "--template", Low, "--output", "json"]);

        exit.Should().Be(0);
        harness.Result!.Context!.RequireCleanStore.Should().BeFalse();
        harness.Result.Invocation!.HasCommittedOutput.Should().BeTrue();
        File.Exists(uncommitted).Should().BeFalse();
        File.Exists(store.Journal).Should().BeFalse();
        StoreBytes(store.Backup).Should().BeEquivalentTo(backup);
        File.Exists(Path.Combine(Target, "host.json")).Should().BeTrue();
        File.Exists(Path.Combine(Target, ".func", "config.json")).Should().BeTrue();
        AssertJson("created", planned: 1, completed: 1);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Command_PrivateNativeSuccessThenCancellation_DoesNotClaimRealCreation(bool dryRun)
    {
        WriteProject("valid", Low);
        await InstallAsync();
        using CancellationTokenSource cancellation = new();
        var factory = new CancelAfterCaptureFactory(Path.Combine(Home, "templates"), cancellation);
        var harness = CreateHarness(templateFactory: factory);
        string[] flags = dryRun ? ["--dry-run"] : [];

        var failure = await Record.ExceptionAsync(() =>
            harness.InvokeAsync([Target, "--template", Low, "--output", "json", .. flags], cancellation.Token));

        failure.Should().BeAssignableTo<OperationCanceledException>();
        factory.NativeStatus.Should().Be(CreationResultStatus.Success);
        factory.NativeCalls.Should().Be(1);
        Directory.Exists(Target).Should().BeFalse();
        harness.Result.Should().BeNull();
        _interaction.AllOutput.Should().NotContain("JSON:").And.NotContain("SUCCESS:");
        await AssertDisposedAsync(harness.Factory);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Command_RealNativeMoveThenCancellation_RetainsPartialOutputWithoutConfiguration(bool json)
    {
        WriteProject("valid", Low);
        await InstallAsync();
        using CancellationTokenSource cancellation = new();
        List<string> committed = [];
        var files = new PhysicalFileSystem();
        var installation = new RecordingCommitFileSystem(files)
        {
            AfterMove = path =>
            {
                committed.Add(path);
                cancellation.Cancel();
            },
        };
        var store = new TemplateStore(new(Home), files, new FileTemplateStoreLock(), NullLoggerFactory.Instance);
        var writer = WriterFileSystem();
        var harness = CreateHarness(writerFileSystem: writer, templateFactory: new TemplaterFactory(store, installation));
        int? exit = null;

        var failure = await Record.ExceptionAsync(async () => exit = await harness.InvokeAsync(
            [Target, "--template", Low, "--output", json ? "json" : "plain"], cancellation.Token));

        committed.Should().ContainSingle();
        File.Exists(committed[0]).Should().BeTrue();
        failure.Should().BeNull("the current producer reports a non-successful result with already committed real output");
        exit.Should().Be(1);
        harness.Result!.Kind.Should().Be("invocationFailed");
        harness.Result.Succeeded.Should().BeFalse();
        harness.Result.Invocation!.Status.Should().Be(TemplateInvocationStatus.Failed);
        harness.Result.Invocation.HasCommittedOutput.Should().BeTrue();
        harness.Result.Invocation.Changes.Should().Contain(change =>
            Path.GetFullPath(Path.Combine(Target, change.Path)) == committed[0]);
        harness.Result.Invocation.ConfiguredProjects.Should().BeEmpty();
        harness.Result.Preview!.ProjectConfigurations.Should().ContainSingle();
        File.Exists(Path.Combine(Target, ".func", "config.json")).Should().BeFalse();
        await writer.DidNotReceive().CreateAllTextAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        _interaction.AllOutput.Should().NotContain("SUCCESS:");
        if (json)
        {
            AssertJson("invocationFailed", planned: 1, completed: 0);
            using var document = JsonDocument.Parse(_interaction.Lines.Single(line => line.StartsWith("JSON: ", StringComparison.Ordinal))["JSON: ".Length..]);
            document.RootElement.GetProperty("invocation").GetProperty("completedChanges").GetArrayLength().Should().BeGreaterThan(0);
        }
        else
        {
            _interaction.AllOutput.Should().Contain("Completed effects");
        }

        await AssertDisposedAsync(harness.Factory);
    }

    [Fact]
    public async Task Command_MetadataFiltersPrecedeArgumentCompatibilityAndPrecedence()
    {
        WriteVariants();
        await InstallAsync();
        var harness = CreateHarness();

        int exit = await harness.InvokeAsync([Target, "--language", "TS", "--template", "project", "--dry-run"]);

        exit.Should().Be(0);
        harness.Result!.SelectedTemplate!.Identity.Should().Be("Func.Init.TypeScript");
        harness.Result.SelectedTemplate.ProjectConfigurations!.Single().Language.Should().Be("TypeScript");
        harness.Result.ArgumentResults.Should().ContainSingle().Which.TemplateIdentity.Should().Be("Func.Init.TypeScript");
        Directory.Exists(Target).Should().BeFalse();
    }

    [Fact]
    public async Task Command_LanguageWithMultipleInstalledOwners_DoesNotChooseTheFirstStack()
    {
        WriteProject("valid", Low);
        await InstallAsync();
        var other = Substitute.For<IProjectStack>();
        other.Stack.Returns("other");
        other.DisplayName.Returns("Other");
        other.WorkerRuntimeAliases.Returns([]);
        other.SupportedLanguages.Returns(["JavaScript"]);
        other.SupportedLanguageAliases.Returns(new Dictionary<string, IReadOnlyList<string>> { ["JavaScript"] = ["js"] });
        var harness = CreateHarness(additionalStacks: [other]);

        int exit = await harness.InvokeAsync([Target, "--language", "js", "--dry-run"]);

        exit.Should().Be(0);
        harness.Result!.Preview!.ProjectConfigurations.Single().Stack.Should().Be("node");
        typeof(IProjectStack).GetMethods().Should().NotContain(method => method.Name == nameof(IProjectInitializer.InitializeAsync)
            || method.Name == nameof(IProjectInitializer.GetInitOptions));
        Directory.Exists(Target).Should().BeFalse();
    }

    [Fact]
    public async Task Command_NameAndCollidingShortAlias_AreSeparateFromCanonicalTemplateValues()
    {
        WriteVariants();
        await InstallAsync();
        var harness = CreateHarness();

        int exit = await harness.InvokeAsync([Target, "--template", Low, "--name", "Named", "--greeting", "Hello", "-p:n", "function"]);

        exit.Should().Be(0);
        harness.Result!.ArgumentResults.Single().ValuesByCanonicalName.Should().Contain("Auth", "function").And.NotContainKey("name");
        File.ReadAllText(Path.Combine(Target, "context.txt")).Should().StartWith("Named|Hello|function|");
    }

    [Theory]
    [InlineData("--unknown", "value")]
    [InlineData("--greeting")]
    [InlineData("--greeting", "hello", "--param:Auth", "invalid")]
    [InlineData("--greeting", "hello", "--Enabled", "not-a-bool")]
    [InlineData("--greeting", "hello", "--greeting", "again")]
    [InlineData("--greeting", "hello", "--", "tail")]
    [InlineData("--greeting", "hello", "@response-file")]
    public async Task Command_InvalidExplicitInput_NeverPromptsOrCreates(params string[] tail)
    {
        WriteVariants();
        await InstallAsync();
        var interaction = Interactive();
        var harness = CreateHarness(interaction);

        int exit = await harness.InvokeAsync([Target, "--template", Low, .. tail]);

        exit.Should().Be(1);
        harness.Result!.Kind.Should().Be("invalidArguments");
        harness.Result.ArgumentResults.Single().InvalidInputDiagnostics.Should().Contain(diagnostic => diagnostic.TokenIndices.Count > 0);
        interaction.ReceivedCalls().Should().NotContain(call => call.GetMethodInfo().Name.StartsWith("Prompt", StringComparison.Ordinal));
        Directory.Exists(Target).Should().BeFalse();
        await AssertDisposedAsync(harness.Factory);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Command_NonInteractive_ReportsFamiliesVariantsAndRequiredValues(bool explicitSwitch)
    {
        WriteVariants();
        WriteProject("other", "Func.Init.Other", shortName: "other");
        await InstallAsync();
        IInteractionService interaction = explicitSwitch ? Interactive() : _interaction;
        var harness = CreateHarness(interaction);
        string[] flags = explicitSwitch ? ["--non-interactive"] : [];

        int families = await harness.InvokeAsync([Target, .. flags]);
        var familyResult = harness.Result!;
        int variants = await harness.InvokeAsync([Target, "--template", "project", "--language", "js", .. flags]);
        var variantResult = harness.Result!;
        int required = await harness.InvokeAsync([Target, "--template", Low, .. flags]);

        families.Should().Be(1);
        familyResult.Kind.Should().Be("ambiguousGroups");
        familyResult.Templates.Should().Contain(entry => entry.Identity == "Func.Init.Other");
        variants.Should().Be(1);
        variantResult.Kind.Should().Be("ambiguousCandidates");
        variantResult.Templates.Select(entry => entry.Identity).Should().BeEquivalentTo("Func.Init.High", "Func.Init.High2");
        required.Should().Be(1);
        harness.Result!.Kind.Should().Be("missingRequired");
        harness.Result.Diagnostics.Single().Should().Contain("--greeting");
        Directory.Exists(Target).Should().BeFalse();
    }

    [Fact]
    public async Task Command_Interactive_TemplateFamilyFirstThenVisibleRequiredOnly()
    {
        WriteVariants();
        WriteProject("other", "Func.Init.Other", shortName: "other");
        await InstallAsync();
        var interaction = Interactive();
        interaction.PromptForSelectionAsync("Select a project-template family:", Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>())
            .Returns("Func.Init.Group");
        interaction.PromptForInputAsync("--greeting (string)", null, Arg.Any<CancellationToken>()).Returns("Prompted");
        var harness = CreateHarness(interaction);

        int exit = await harness.InvokeAsync([Target, "--param:Auth", "anonymous", "--Enabled", "false", "--dry-run"]);

        exit.Should().Be(0);
        var prompts = interaction.ReceivedCalls().Where(call => call.GetMethodInfo().Name.StartsWith("Prompt", StringComparison.Ordinal)).ToArray();
        prompts.Select(call => call.GetArguments()[0]).Should().Equal("Select a project-template family:", "--greeting (string)");
        harness.Result!.ArgumentResults.Single().ValuesByCanonicalName.Should()
            .Contain("Greeting", "Prompted").And.Contain("Auth", "anonymous").And.Contain("Optional", "leave");
        harness.Result.ArgumentResults.Single().ValuesByCanonicalName.Should().NotContainKey("name");
        Directory.Exists(Target).Should().BeFalse();
    }

    [Theory]
    [InlineData("missing", "NotFound")]
    [InlineData("Func.Init.Item", "WrongType")]
    [InlineData("Func.Init.Restricted", "Unavailable")]
    [InlineData("Func.Init.Untyped", "InvalidTemplateType")]
    public async Task Command_UnselectableTemplate_ReturnsStructuredFailureWithoutFallback(string reference, string kind)
    {
        WriteProject("valid", Low);
        WriteProject("restricted", "Func.Init.Restricted", extraConstraint: """ "missing":{"type":"func-workload","args":"java"} """);
        WriteOther("item", "Func.Init.Item", "item");
        WriteOther("untyped", "Func.Init.Untyped", null);
        await InstallAsync();
        var before = SnapshotHome();
        var harness = CreateHarness();

        int exit = await harness.InvokeAsync([Target, "--template", reference, "--output", "json"]);

        exit.Should().Be(1);
        harness.Result!.Kind.Should().Be(kind);
        if (kind == "NotFound")
        {
            harness.Result.Diagnostics.Single().Should().Contain("func new install <package>").And.Contain("not installed");
        }
        else if (kind == "WrongType")
        {
            harness.Result.Diagnostics.Single().Should().Contain("func new");
        }

        SnapshotHome().Should().BeEquivalentTo(before);
        Directory.Exists(Target).Should().BeFalse();
    }

    [Theory]
    [InlineData("--stack", "missing", "invalidStack")]
    [InlineData("--stack", "", "invalidStack")]
    [InlineData("--language", "missing", "invalidLanguage")]
    [InlineData("--language", "", "invalidLanguage")]
    public async Task Command_InvalidExplicitFilter_FailsBeforeFactory(string option, string value, string kind)
    {
        WriteProject("valid", Low);
        await InstallAsync();
        var harness = CreateHarness(Interactive());

        int exit = await harness.InvokeAsync([Target, "--template", Low, option, value]);

        exit.Should().Be(1);
        harness.Result!.Kind.Should().Be(kind);
        harness.Factory.Contexts.Should().BeEmpty();
        Directory.Exists(Target).Should().BeFalse();
    }

    [Fact]
    public async Task Command_SupportedMultipleRoots_PreviewsAndConfiguresEachInDeclarationOrder()
    {
        WriteProject("multi", "Func.Init.Multi", roots: ["api", "worker"]);
        await InstallAsync();
        var harness = CreateHarness();

        int previewExit = await harness.InvokeAsync([Target, "--template", "Func.Init.Multi", "--dry-run", "--output", "json"]);
        var preview = harness.Result!;

        previewExit.Should().Be(0);
        preview.Preview!.ProjectConfigurations.Select(plan => plan.Root).Should().Equal(Path.Combine(Target, "api"), Path.Combine(Target, "worker"));
        preview.Invocation.Should().BeNull();
        Directory.Exists(Target).Should().BeFalse();
        AssertJson("previewed", planned: 2, completed: 0);

        int createdExit = await harness.InvokeAsync([Target, "--template", "Func.Init.Multi", "--output", "json"]);

        createdExit.Should().Be(0);
        harness.Result!.Invocation!.ConfiguredProjects.Should().Equal(Path.Combine(Target, "api"), Path.Combine(Target, "worker"));
        File.Exists(Path.Combine(Target, ".func", "config.json")).Should().BeFalse();
        foreach (string root in harness.Result.Invocation.ConfiguredProjects)
        {
            harness.Provider.GetRequiredService<IOptionsMonitor<StackOptions>>().Get(root).Language.Should().Be("JavaScript");
        }

        AssertJson("created", planned: 2, completed: 2);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Command_WholeTemplateLanguageFilter_RejectsMixedRootsBeforeWrites(bool explicitReference)
    {
        WriteProject("mixed", "Func.Init.Mixed", roots: ["api", "worker"], languages: ["JavaScript", "TypeScript"]);
        await InstallAsync();
        var harness = CreateHarness();
        string[] selector = explicitReference ? ["--template", "Func.Init.Mixed"] : [];

        int rejected = await harness.InvokeAsync([Target, .. selector, "--language", "js", "--dry-run"]);
        var rejectedResult = harness.Result!;
        int accepted = await harness.InvokeAsync([Target, .. selector, "--dry-run"]);

        rejected.Should().Be(1);
        rejectedResult.Kind.Should().Be("noCompatibleTemplates");
        accepted.Should().Be(0);
        harness.Result!.Preview!.ProjectConfigurations.Select(plan => plan.Language).Should().Equal("JavaScript", "TypeScript");
        Directory.Exists(Target).Should().BeFalse();
    }

    [Theory]
    [InlineData("missing-actions")]
    [InlineData("conditional")]
    [InlineData("ordinary-action")]
    [InlineData("renaming")]
    public async Task Command_UnsupportedMetadata_IsVisibleAndRejectedBeforeWrites(string shape)
    {
        var configuration = WriteProject("invalid", "Func.Init.Invalid");
        if (shape == "missing-actions")
        {
            configuration.Remove("postActions");
        }
        else if (shape == "conditional")
        {
            configuration["primaryOutputs"]![0]!["condition"] = "true";
        }
        else if (shape == "ordinary-action")
        {
            configuration["postActions"]![0]!["actionId"] = Guid.NewGuid().ToString();
        }
        else
        {
            configuration["symbols"] = JsonNode.Parse("""{"Rename":{"type":"parameter","datatype":"string","fileRename":"old"}}""");
        }

        SaveConfiguration("invalid", configuration);
        await InstallAsync();
        var harness = CreateHarness();

        int exit = await harness.InvokeAsync([Target, "--template", "Func.Init.Invalid", "--output", "json"]);

        exit.Should().Be(1);
        harness.Result!.Kind.Should().Be("Unavailable");
        harness.Result.Templates.Should().ContainSingle().Which.ProjectConfigurationDiagnostic.Should().NotBeNullOrWhiteSpace();
        Directory.Exists(Target).Should().BeFalse();
        _interaction.AllOutput.Should().Contain("projectConfigurationDiagnostic");
    }

    [Fact]
    public async Task Command_RestrictedMalformedMetadata_RetainsBothDiagnosticsAndNoPrompt()
    {
        var configuration = WriteProject("invalid", "Func.Init.Invalid", extraConstraint: """ "missing":{"type":"func-workload","args":"java"} """);
        configuration.Remove("postActions");
        SaveConfiguration("invalid", configuration);
        await InstallAsync();
        var interaction = Interactive();
        var harness = CreateHarness(interaction);

        int exit = await harness.InvokeAsync([Target]);

        exit.Should().Be(1);
        var entry = harness.Result!.Templates.Should().ContainSingle().Subject;
        entry.ConstraintEvaluation.Status.Should().Be(TemplateConstraintEvaluationStatus.Restricted);
        entry.ConstraintEvaluation.Diagnostics.Should().NotBeEmpty();
        entry.ProjectConfigurationDiagnostic.Should().NotBeNullOrWhiteSpace();
        interaction.ReceivedCalls().Should().NotContain(call => call.GetMethodInfo().Name.StartsWith("Prompt", StringComparison.Ordinal));
        Directory.Exists(Target).Should().BeFalse();
    }

    [Fact]
    public async Task Command_SoleAvailableWithRestrictedGroup_ShowsPickerButNeverOffersUnavailableChoice()
    {
        WriteProject("valid", Low);
        WriteProject("restricted", "Func.Init.Restricted", extraConstraint: """ "missing":{"type":"func-workload","args":"java"} """);
        await InstallAsync();
        var interaction = Interactive();
        var harness = CreateHarness(interaction);

        int exit = await harness.InvokeAsync([Target, "--dry-run"]);

        exit.Should().Be(0);
        await interaction.Received(1).PromptForSelectionAsync("Select a project-template family:",
            Arg.Is<IEnumerable<string>>(choices => choices.SequenceEqual(new[] { Low })), Arg.Any<CancellationToken>());
        interaction.Received(1).WriteTable(Arg.Any<string[]>(), Arg.Any<IEnumerable<string[]>>());
        harness.Result!.SelectedTemplate!.Identity.Should().Be(Low);
        Directory.Exists(Target).Should().BeFalse();
    }

    [Fact]
    public async Task Command_NoEligibleGroups_RendersDistinctConstraintRemediationOnce()
    {
        WriteProject("restricted", "Func.Init.Restricted", extraConstraint: """ "missing":{"type":"func-workload","args":"java"} """);
        WriteProject("restricted2", "Func.Init.Restricted2", extraConstraint: """ "missing":{"type":"func-workload","args":"java"} """);
        await InstallAsync();
        var harness = CreateHarness();

        int exit = await harness.InvokeAsync([Target]);

        exit.Should().Be(1);
        harness.Result!.Kind.Should().Be("noCompatibleTemplates");
        var actions = harness.Result.Templates.SelectMany(entry => entry.ConstraintEvaluation.Diagnostics)
            .Select(diagnostic => diagnostic.CallToAction).OfType<string>().Where(action => !string.IsNullOrWhiteSpace(action))
            .Distinct(StringComparer.Ordinal).ToArray();
        actions.Should().NotBeEmpty();
        foreach (string action in actions)
        {
            _interaction.Lines.Count(line => line == "HINT: " + action).Should().Be(1);
        }

        Directory.Exists(Target).Should().BeFalse();
    }

    [Fact]
    public async Task Command_ConfigurationFailure_PreservesGeneratedFilesAndReportsOnlyCompletedSubset()
    {
        WriteProject("multi", "Func.Init.Multi", roots: ["api", "worker", "later"]);
        await InstallAsync();
        string workerConfig = Path.Combine(Target, "worker", ".func", "config.json");
        var fileSystem = WriterFileSystem();
        fileSystem.CreateAllTextAsync(workerConfig, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new IOException("controlled finalization failure")));
        var harness = CreateHarness(writerFileSystem: fileSystem);

        int exit = await harness.InvokeAsync([Target, "--template", "Func.Init.Multi", "--output", "json"]);

        exit.Should().Be(1);
        harness.Result!.Kind.Should().Be("partialInitialization");
        harness.Result.Invocation!.ProjectConfigurations.Should().HaveCount(3);
        harness.Result.Invocation.ConfiguredProjects.Should().Equal(Path.Combine(Target, "api"));
        File.Exists(Path.Combine(Target, "api", ".func", "config.json")).Should().BeTrue();
        File.Exists(workerConfig).Should().BeFalse();
        File.Exists(Path.Combine(Target, "later", "host.json")).Should().BeTrue();
        await fileSystem.DidNotReceive().CreateAllTextAsync(Path.Combine(Target, "later", ".func", "config.json"),
            Arg.Any<string>(), Arg.Any<CancellationToken>());
        AssertJson("partialInitialization", planned: 3, completed: 1);
        using var json = JsonDocument.Parse(_interaction.Lines.Last(line => line.StartsWith("JSON: ", StringComparison.Ordinal))["JSON: ".Length..]);
        var invocation = json.RootElement.GetProperty("invocation");
        invocation.GetProperty("plannedChanges").EnumerateArray().Select(change => change.GetProperty("path").GetString()!.Replace('\\', '/'))
            .Where(path => path.EndsWith("/.func/config.json", StringComparison.Ordinal))
            .Should().BeEquivalentTo("api/.func/config.json", "worker/.func/config.json", "later/.func/config.json");
        invocation.GetProperty("completedChanges").EnumerateArray().Select(change => change.GetProperty("path").GetString()!.Replace('\\', '/'))
            .Where(path => path.EndsWith("/.func/config.json", StringComparison.Ordinal)).Should().Equal("api/.func/config.json");
        _interaction.AllOutput.Should().NotContain("SUCCESS:");
        await AssertDisposedAsync(harness.Factory);
    }

    [Fact]
    public async Task Command_CancellationDuringFinalization_DoesNotWriteNextRootOrReportSuccess()
    {
        WriteProject("multi", "Func.Init.Multi", roots: ["api", "worker"]);
        await InstallAsync();
        using CancellationTokenSource cancellation = new();
        var fileSystem = WriterFileSystem();
        string config = Path.Combine(Target, "api", ".func", "config.json");
        fileSystem.CreateAllTextAsync(config, Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(_ =>
        {
            cancellation.Cancel();
            return Task.FromCanceled(cancellation.Token);
        });
        var harness = CreateHarness(writerFileSystem: fileSystem);

        int? exit = null;
        var failure = await Record.ExceptionAsync(async () =>
            exit = await harness.InvokeAsync([Target, "--template", "Func.Init.Multi", "--output", "json"], cancellation.Token));

        failure.Should().BeNull("native output is already committed, so cancellation must retain the partial result");
        exit.Should().Be(1);
        harness.Result!.Kind.Should().Be("partialInitialization");
        harness.Result.Succeeded.Should().BeFalse();
        harness.Result.Invocation!.HasCommittedOutput.Should().BeTrue();
        harness.Result.Invocation.ConfiguredProjects.Should().BeEmpty();
        harness.Result.Invocation.Changes.Should().Contain(change =>
            Path.GetFullPath(Path.Combine(Target, change.Path)) == Path.Combine(Target, "worker", "host.json"));
        File.Exists(config).Should().BeFalse();
        await fileSystem.DidNotReceive().CreateAllTextAsync(Path.Combine(Target, "worker", ".func", "config.json"),
            Arg.Any<string>(), Arg.Any<CancellationToken>());
        File.Exists(Path.Combine(Target, "worker", "host.json")).Should().BeTrue();
        AssertJson("partialInitialization", planned: 2, completed: 0);
        _interaction.AllOutput.Should().NotContain("SUCCESS:");
        await AssertDisposedAsync(harness.Factory);
    }

    [Theory]
    [InlineData("runner")]
    [InlineData("plain")]
    [InlineData("json")]
    public async Task Execute_FinalConfigurationCommitsBeforeCancellation_PreservesCompletedCreation(string boundary)
    {
        const string identity = "Func.Init.Committed";
        WriteProject("committed", identity);
        await InstallAsync();
        using CancellationTokenSource producerCancellation = new();
        CancelAfterConfigurationCommitFileSystem producerFileSystem = new(producerCancellation, cancelOnCommit: 1);
        var producerHarness = CreateHarness(writerFileSystem: producerFileSystem);
        string producerTarget = Path.Combine(_root, "producer");
        using (var flow = await OpenProjectFlowAsync(producerHarness, producerTarget))
        {
            var selected = (await flow.ResolveGroupAsync(identity, TemplateType.Project)).Group![0];
            var completed = await selected.InvokeAsync(
                new(WorkingDirectory.FromExplicit(producerTarget), new Dictionary<string, string?>()), producerCancellation.Token);

            producerCancellation.IsCancellationRequested.Should().BeTrue();
            selected.Entry.Identity.Should().Be(identity);
            completed.Status.Should().Be(TemplateInvocationStatus.Success);
            completed.ConfiguredProjects.Should().Equal(producerTarget);
            completed.Changes.Select(change => Path.GetFullPath(Path.Combine(producerTarget, change.Path))).Should().BeEquivalentTo(
                Path.Combine(producerTarget, "host.json"), Path.Combine(producerTarget, "package.json"),
                Path.Combine(producerTarget, "context.txt"), Path.Combine(producerTarget, ".func", "config.json"));
            File.ReadAllText(Path.Combine(producerTarget, "host.json")).Should().Be("""{"version":"2.0"}""");
            using var configuration = JsonDocument.Parse(File.ReadAllText(Path.Combine(producerTarget, ".func", "config.json")));
            configuration.RootElement.GetProperty("stack").GetProperty("runtime").GetString().Should().Be("node");
            configuration.RootElement.GetProperty("stack").GetProperty("language").GetString().Should().Be("JavaScript");
        }

        using CancellationTokenSource cancellation = new();
        CancelAfterConfigurationCommitFileSystem fileSystem = new(cancellation, cancelOnCommit: 1);
        var harness = CreateHarness(writerFileSystem: fileSystem);
        int? exit = null;
        var failure = await Record.ExceptionAsync(async () =>
        {
            if (boundary == "runner")
            {
                await harness.Runner.ExecuteAsync(new(WorkingDirectory.FromExplicit(Target), identity, null, null, null,
                    false, true, false, false, [], new HashSet<string>(StringComparer.Ordinal)),
                    new([new("Controlled.Node", NuGetVersion.Parse("1.0.0"), ["node"], true)]), cancellation.Token);
            }
            else
            {
                exit = await harness.InvokeAsync([Target, "--template", identity, "--output", boundary], cancellation.Token);
            }
        });

        cancellation.IsCancellationRequested.Should().BeTrue();
        fileSystem.CommittedConfigurations.Should().Equal(Path.Combine(Target, ".func", "config.json"));
        File.ReadAllText(Path.Combine(Target, "host.json")).Should().Be("""{"version":"2.0"}""");
        File.Exists(Path.Combine(Target, ".func", "config.json")).Should().BeTrue();
        failure.Should().BeNull("the producer committed the final configuration before cancellation");
        harness.Result!.Kind.Should().Be("created");
        harness.Result.Succeeded.Should().BeTrue();
        harness.Result.SelectedTemplate!.Identity.Should().Be(identity);
        harness.Result.Preview!.ConfiguredProjects.Should().BeEmpty();
        harness.Result.Invocation!.Status.Should().Be(TemplateInvocationStatus.Success);
        harness.Result.Invocation.ConfiguredProjects.Should().Equal(Target);
        harness.Result.Invocation.Changes.Should().Contain(change => change.Path.Replace('\\', '/') == ".func/config.json");
        if (boundary != "runner")
        {
            exit.Should().Be(0);
            if (boundary == "json")
            {
                AssertJson("created", planned: 1, completed: 1);
            }
            else
            {
                _interaction.AllOutput.Should().Contain("SUCCESS:").And.Contain("Completed effects").And.Contain("Configured project:");
            }
        }

        await AssertDisposedAsync(harness.Factory);
        await AssertDisposedAsync(producerHarness.Factory);
    }

    [Fact]
    public async Task Command_ConfirmedPartialResultReturnsBeforeCancellation_PreservesFailureAndCompletedRoots()
    {
        WriteProject("multi", "Func.Init.Multi", roots: ["api", "worker", "later"]);
        await InstallAsync();
        using CancellationTokenSource cancellation = new();
        var fileSystem = WriterFileSystem();
        string workerConfiguration = Path.Combine(Target, "worker", ".func", "config.json");
        fileSystem.CreateAllTextAsync(workerConfiguration, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new IOException("controlled finalization failure")));
        var harness = CreateHarness(writerFileSystem: fileSystem, afterRunnerReturn: result =>
        {
            result.Kind.Should().Be("partialInitialization");
            result.Invocation!.Status.Should().Be(TemplateInvocationStatus.ConfigurationFailure);
            result.Invocation.ConfiguredProjects.Should().Equal(Path.Combine(Target, "api"));
            File.Exists(Path.Combine(Target, "api", ".func", "config.json")).Should().BeTrue();
            cancellation.Cancel();
        });
        int? exit = null;

        var failure = await Record.ExceptionAsync(async () =>
            exit = await harness.InvokeAsync([Target, "--template", "Func.Init.Multi", "--output", "json"], cancellation.Token));

        cancellation.IsCancellationRequested.Should().BeTrue();
        harness.Result!.Succeeded.Should().BeFalse();
        harness.Result.Invocation!.Changes.Should().Contain(change => change.Path.Replace('\\', '/') == "api/.func/config.json");
        File.Exists(workerConfiguration).Should().BeFalse();
        await fileSystem.DidNotReceive().CreateAllTextAsync(Path.Combine(Target, "later", ".func", "config.json"),
            Arg.Any<string>(), Arg.Any<CancellationToken>());
        failure.Should().BeNull("the returned producer-confirmed partial result must not be replaced by late cancellation");
        exit.Should().Be(1);
        AssertJson("partialInitialization", planned: 3, completed: 1);
        _interaction.AllOutput.Should().NotContain("SUCCESS:");
        await AssertDisposedAsync(harness.Factory);
    }

    [Fact]
    public async Task Command_CancellationAfterFirstConfigurationCommit_DoesNotStartRemainingPlanOrRenderSuccess()
    {
        WriteProject("multi", "Func.Init.Multi", roots: ["api", "worker"]);
        await InstallAsync();
        using CancellationTokenSource cancellation = new();
        CancelAfterConfigurationCommitFileSystem fileSystem = new(cancellation, cancelOnCommit: 1);
        var harness = CreateHarness(writerFileSystem: fileSystem);

        int? exit = null;
        var failure = await Record.ExceptionAsync(async () =>
            exit = await harness.InvokeAsync([Target, "--template", "Func.Init.Multi", "--output", "json"], cancellation.Token));

        fileSystem.CommittedConfigurations.Should().Equal(Path.Combine(Target, "api", ".func", "config.json"));
        File.Exists(Path.Combine(Target, "api", ".func", "config.json")).Should().BeTrue();
        File.ReadAllText(Path.Combine(Target, "worker", "host.json")).Should().Be("""{"version":"2.0"}""");
        Directory.Exists(Path.Combine(Target, "worker", ".func")).Should().BeFalse();
        failure.Should().BeNull("the current native producer preserves committed output and finalized roots on cancellation");
        exit.Should().Be(1);
        harness.Result!.Kind.Should().Be("partialInitialization");
        harness.Result.Succeeded.Should().BeFalse();
        harness.Result.Invocation!.HasCommittedOutput.Should().BeTrue();
        harness.Result.Invocation.ConfiguredProjects.Should().Equal(Path.Combine(Target, "api"));
        AssertJson("partialInitialization", planned: 2, completed: 1);
        _interaction.Lines.Count(line => line.StartsWith("JSON: ", StringComparison.Ordinal)).Should().Be(1);
        _interaction.AllOutput.Should().NotContain("SUCCESS:");
        await AssertDisposedAsync(harness.Factory);
    }

    [Theory]
    [InlineData("Boolean")]
    [InlineData("choice")]
    [InlineData("string")]
    public async Task Command_RequiredLiteralPrompts_ReuseSharedCompletionWithRealRenderer(string dataType)
    {
        JsonObject symbol = new() { ["type"] = "parameter", ["datatype"] = dataType, ["isRequired"] = true };
        if (dataType == "choice")
        {
            symbol["choices"] = new JsonArray(new JsonObject { ["choice"] = "choice[bad]" });
        }

        WriteProject("prompt", Low, parameters: new JsonObject { ["Value"] = symbol }.ToJsonString(),
            hostMetadata: """{"symbolInfo":{"Value":{"longName":"value[bad]"}}}""");
        await InstallAsync();
        using BufferedConsole stdout = new(true, true, noColor: true);
        using BufferedConsole stderr = new(true, true, noColor: true);
        if (dataType == "Boolean")
        {
            stderr.Enqueue(ConsoleKey.DownArrow);
        }
        else if (dataType == "string")
        {
            stderr.Enqueue(ConsoleKey.X, 'x');
        }

        stderr.Enqueue(ConsoleKey.Enter);
        var harness = CreateHarness(new SpectreInteractionService(new DefaultTheme(), stdout, stderr));

        int exit = await harness.InvokeAsync([Target, "--template", Low, "--dry-run"]);

        exit.Should().Be(0);
        harness.Result!.ArgumentResults.Single().ValuesByCanonicalName.Should()
            .Contain("Value", dataType == "Boolean" ? "false" : dataType == "choice" ? "choice[bad]" : "x");
        stderr.Output.Should().Contain($"--value[bad] ({dataType})");
        Directory.Exists(Target).Should().BeFalse();
    }

    [Fact]
    public async Task Command_HiddenRequiredParameter_IsAuthoringFailureNotPrompted()
    {
        WriteProject("hidden", Low, parameters: """{"Secret":{"type":"parameter","datatype":"string","isRequired":true}}""",
            hostMetadata: """{"symbolInfo":{"Secret":{"isHidden":true}}}""");
        await InstallAsync();
        var interaction = Interactive();
        var harness = CreateHarness(interaction);

        int exit = await harness.InvokeAsync([Target, "--template", Low]);

        exit.Should().Be(1);
        harness.Result!.ArgumentResults.Single().DefinitionDiagnostics.Should().Contain(message => message.Contains("hidden", StringComparison.Ordinal));
        interaction.ReceivedCalls().Should().NotContain(call => call.GetMethodInfo().Name.StartsWith("Prompt", StringComparison.Ordinal));
        Directory.Exists(Target).Should().BeFalse();
    }

    [Fact]
    public async Task Command_InvalidCompletion_IsReparsedWithoutOverwritingExplicitValues()
    {
        WriteProject("required", Low, parameters: """
            {"Count":{"type":"parameter","datatype":"integer","isRequired":true},
             "Explicit":{"type":"parameter","datatype":"string","isRequired":true}}
            """);
        await InstallAsync();
        var interaction = Interactive();
        interaction.PromptForInputAsync(Arg.Any<string>(), null, Arg.Any<CancellationToken>()).Returns("not-integer");
        var harness = CreateHarness(interaction);

        int exit = await harness.InvokeAsync([Target, "--template", Low, "--Explicit", "keep"]);

        exit.Should().Be(1);
        harness.Result!.Kind.Should().Be("invalidCompletion");
        harness.Result.ArgumentResults.Single().ValuesByCanonicalName.Should().Contain("Explicit", "keep");
        harness.Result.ArgumentResults.Single().InvalidInputDiagnostics.Should().Contain(diagnostic => diagnostic.TokenIndices.Count == 0);
        Directory.Exists(Target).Should().BeFalse();
    }

    [Fact]
    public async Task Command_CancellationDuringRequiredCompletion_ReleasesFlowAndDoesNotCreate()
    {
        WriteProject("required", Low, parameters: Parameters);
        await InstallAsync();
        using CancellationTokenSource cancellation = new();
        var interaction = Interactive();
        interaction.PromptForInputAsync(Arg.Any<string>(), null, Arg.Any<CancellationToken>()).Returns(_ =>
        {
            cancellation.Cancel();
            return Task.FromResult("must not create");
        });
        var harness = CreateHarness(interaction);

        Func<Task> act = () => harness.InvokeAsync([Target, "--template", Low], cancellation.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        Directory.Exists(Target).Should().BeFalse();
        harness.Result.Should().BeNull();
        await AssertDisposedAsync(harness.Factory);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Command_ExistingContentOrForce_IsExplicitlyUnqualifiedWithoutDeletion(bool force)
    {
        WriteProject("valid", Low);
        await InstallAsync();
        Directory.CreateDirectory(Target);
        string file = Path.Combine(Target, "user.txt");
        File.WriteAllText(file, "keep");
        var harness = CreateHarness();
        string[] flags = force ? ["--force"] : [];

        int exit = await harness.InvokeAsync([Target, "--template", Low, .. flags, "--dry-run"]);

        exit.Should().Be(1);
        harness.Result!.Kind.Should().Be(force ? "forceNotQualified" : "unsupportedTarget");
        harness.Factory.Contexts.Should().BeEmpty();
        File.ReadAllText(file).Should().Be("keep");
        Directory.GetFiles(Target).Should().Equal(file);
    }

    [Fact]
    public async Task Command_EmptyDirectoryWithOnlyEmptySubdirectory_IsStillNotAnEmptyTarget()
    {
        WriteProject("valid", Low);
        await InstallAsync();
        Directory.CreateDirectory(Path.Combine(Target, "user-empty"));
        var harness = CreateHarness();

        int exit = await harness.InvokeAsync([Target, "--template", Low]);

        exit.Should().Be(1);
        harness.Result!.Kind.Should().Be("unsupportedTarget");
        harness.Factory.Contexts.Should().BeEmpty();
        Directory.Exists(Path.Combine(Target, "user-empty")).Should().BeTrue();
    }

    [Fact]
    public async Task Command_ContentAppearingDuringPrompt_IsRefusedBeforeNativeCreation()
    {
        WriteProject("required", Low, parameters: Parameters);
        await InstallAsync();
        var interaction = Interactive();
        interaction.PromptForInputAsync(Arg.Any<string>(), null, Arg.Any<CancellationToken>()).Returns(_ =>
        {
            Directory.CreateDirectory(Target);
            File.WriteAllText(Path.Combine(Target, "appeared.txt"), "keep");
            return Task.FromResult("hello");
        });
        var harness = CreateHarness(interaction);

        int exit = await harness.InvokeAsync([Target, "--template", Low]);

        exit.Should().Be(1);
        harness.Result!.Kind.Should().Be("unsupportedTarget");
        File.ReadAllText(Path.Combine(Target, "appeared.txt")).Should().Be("keep");
        File.Exists(Path.Combine(Target, "host.json")).Should().BeFalse();
        harness.Result.Preview.Should().NotBeNull();
        harness.Result.Preview!.ProjectConfigurations.Should().ContainSingle();
        harness.Result.Preview.ConfiguredProjects.Should().BeEmpty();
        harness.Result.Invocation.Should().BeNull();
        using BufferedConsole stdout = new(true, true, noColor: true);
        using BufferedConsole stderr = new(true, true, noColor: true);

        int rendered = new InitExecutionRenderer(new SpectreInteractionService(new DefaultTheme(), stdout, stderr))
            .Render(harness.Result, jsonOutput: true);

        rendered.Should().Be(1);
        using var json = JsonDocument.Parse(stdout.Output);
        json.RootElement.GetProperty("succeeded").GetBoolean().Should().BeFalse();
        json.RootElement.GetProperty("invocation").GetProperty("plannedConfigurations").GetArrayLength().Should().Be(1);
        json.RootElement.GetProperty("invocation").GetProperty("configuredProjects").GetArrayLength().Should().Be(0);
        json.RootElement.GetProperty("invocation").GetProperty("completedChanges").GetArrayLength().Should().Be(0);
        await AssertDisposedAsync(harness.Factory);
    }

    [Fact]
    public async Task Command_EmptyInstalledCatalog_ReturnsPackageGuidanceWithoutInstallingOrCreating()
    {
        var harness = CreateHarness();

        int exit = await harness.InvokeAsync([Target]);

        exit.Should().Be(1);
        harness.Result!.Kind.Should().Be("noCompatibleTemplates");
        harness.Result.Diagnostics.Single().Should().Contain("func new install <package>");
        Directory.Exists(Target).Should().BeFalse();
    }

    [Fact]
    public async Task Command_ProjectBundleConstraint_StaysItemOnlyWithoutOpeningExistingProjectContext()
    {
        var configuration = WriteProject("bundle", Low, extraConstraint: """
            "bundle":{"type":"func-bundle","args":{"id":"Microsoft.Azure.Functions.ExtensionBundle","version":"[4.0.0,5.0.0)"}}
            """);
        SaveConfiguration("bundle", configuration);
        await InstallAsync();
        var harness = CreateHarness();
        var before = SnapshotHome();

        int exit = await harness.InvokeAsync([Target, "--template", Low, "--dry-run", "--output", "json"]);

        exit.Should().Be(1);
        harness.Result!.Kind.Should().Be("Unavailable");
        harness.Result.Context!.Project!.Stack.Should().BeNull();
        harness.Result.Context.Project.Language.Should().BeNull();
        harness.Result.Context.Bundle.Should().BeNull();
        harness.Result.Templates.Should().ContainSingle().Which.ConstraintEvaluation.Diagnostics
            .Should().Contain(diagnostic => diagnostic.Message.Contains("func-bundle", StringComparison.Ordinal));
        SnapshotHome().Should().BeEquivalentTo(before);
        Directory.Exists(Target).Should().BeFalse();
        await AssertDisposedAsync(harness.Factory);
    }

    [Fact]
    public async Task Command_CurrentSupportPair_IsRetainedWithOneAvailabilityOnlyProtectedFlow()
    {
        WriteProject("valid", Low);
        await InstallAsync();
        var harness = CreateHarness();
        var support = harness.Provider.GetRequiredService<TemplateProjectSupport>();

        int exit = await harness.InvokeAsync([Target, "--template", Low, "--dry-run"]);

        exit.Should().Be(0);
        harness.Factory.ProjectSupports.Should().ContainSingle().Which.Should().BeSameAs(support);
        harness.SupportRequests.Should().Be(1);
        harness.CatalogRequests.Should().Be(1);
        harness.Provider.GetRequiredService<Func<InstalledProjectStackCatalog>>()()
            .Should().BeSameAs(harness.Provider.GetRequiredService<InstalledProjectStackCatalog>());
        support.Planner.Should().BeSameAs(harness.Provider.GetRequiredService<TemplateProjectConfigurationPlanner>());
        support.Writer.Should().BeSameAs(harness.Provider.GetRequiredService<TemplateProjectConfigurationWriter>());
        harness.Factory.Contexts.Should().ContainSingle().Which.Should().BeSameAs(harness.Result!.Context);
        harness.Factory.Availability.Should().ContainSingle().Which.Workloads.Should().ContainSingle()
            .Which.Aliases.Should().Equal("node");
        harness.Result.Context!.Bundle.Should().BeNull();
        Directory.Exists(Target).Should().BeFalse();
        await AssertDisposedAsync(harness.Factory);
    }

    [Fact]
    public async Task Command_ConflictingInstalledMetadata_ReturnsStructuredFailureWithoutSupportStoreOrFiles()
    {
        WriteProject("valid", Low);
        await InstallAsync();
        var duplicate = Substitute.For<IProjectStack>();
        duplicate.Stack.Returns("node");
        duplicate.DisplayName.Returns("Conflicting Node");
        duplicate.WorkerRuntimeAliases.Returns([]);
        duplicate.SupportedLanguages.Returns(["JavaScript"]);
        duplicate.SupportedLanguageAliases.Returns(new Dictionary<string, IReadOnlyList<string>>());
        var harness = CreateHarness(additionalStacks: [duplicate]);
        var before = SnapshotHome();

        int exit = await harness.InvokeAsync([Target, "--template", Low, "--output", "json"]);

        exit.Should().Be(1);
        harness.Result!.Kind.Should().Be("duplicateStacks");
        harness.Result.Succeeded.Should().BeFalse();
        harness.SupportRequests.Should().Be(0);
        harness.CatalogRequests.Should().Be(1);
        harness.Factory.Contexts.Should().BeEmpty();
        harness.Factory.ProjectSupports.Should().BeEmpty();
        Directory.Exists(Target).Should().BeFalse();
        SnapshotHome().Should().BeEquivalentTo(before);
        _interaction.AllOutput.Should().Contain("\"kind\":\"duplicateStacks\"").And.NotContain("SUCCESS:");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ProjectMetadataFailure_PreservesClassificationWithoutHostMetadataConflation(bool restricted)
    {
        var configuration = WriteProject("invalid", Low,
            parameters: """{"Value":{"type":"parameter","datatype":"string","defaultValue":"keep"}}""",
            extraConstraint: restricted ? """ "missing":{"type":"func-workload","args":"java"} """ : null);
        configuration.Remove("postActions");
        SaveConfiguration("invalid", configuration);
        await InstallAsync();
        var harness = CreateHarness();
        using var templater = await OpenProjectFlowAsync(harness);

        var entries = await templater.ListAsync(TemplateType.Project);
        var entry = entries.Should().ContainSingle().Subject;
        var resolution = await templater.CreateGroupAsync(entries);

        entry.ProjectConfigurationDiagnostic.Should().NotBeNullOrWhiteSpace();
        entry.MetadataDiagnostic.Should().BeNull();
        entry.Parameters.Should().ContainSingle(parameter => parameter.CanonicalName == "Value");
        entry.ConstraintEvaluation.Status.Should().Be(restricted
            ? TemplateConstraintEvaluationStatus.Restricted : TemplateConstraintEvaluationStatus.Failed);
        if (restricted)
        {
            entry.ConstraintEvaluation.Diagnostics.Should().Contain(diagnostic => diagnostic.Type == "func-workload");
        }

        resolution.Status.Should().Be(TemplateResolutionStatus.Unavailable);
        Directory.Exists(Target).Should().BeFalse();
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(true, false)]
    [InlineData(false, false)]
    public async Task ProjectConflictResult_RetainsComputedPlansButNeverClaimsCompletion(bool existingConfiguration, bool dryRun)
    {
        WriteProject("valid", Low);
        await InstallAsync();
        var harness = CreateHarness();
        using var templater = await OpenProjectFlowAsync(harness);
        var selected = (await templater.ResolveGroupAsync(Low, TemplateType.Project)).Group![0];
        string existing = existingConfiguration ? Path.Combine(Target, ".func", "config.json") : Path.Combine(Target, "host.json");
        Directory.CreateDirectory(Path.GetDirectoryName(existing)!);
        File.WriteAllText(existing, existingConfiguration ? """{"user":"keep"}""" : """{"version":"user-owned"}""");
        string before = File.ReadAllText(existing);

        var result = await selected.InvokeAsync(new(WorkingDirectory.FromExplicit(Target), new Dictionary<string, string?>(), DryRun: dryRun));

        result.Status.Should().Be(TemplateInvocationStatus.FileConflict);
        result.ProjectConfigurations.Should().ContainSingle().Which.Should()
            .Be(new TemplateProjectConfigurationPlan(Path.Combine(Target, "host.json"), "node", "JavaScript"));
        result.ConfiguredProjects.Should().BeEmpty();
        File.ReadAllText(existing).Should().Be(before);
        File.Exists(Path.Combine(Target, "package.json")).Should().BeFalse();
    }

    [Fact]
    public async Task ProjectResult_BeforePlanning_DoesNotInventConfigurationPlans()
    {
        WriteProject("valid", Low);
        await InstallAsync();
        var harness = CreateHarness();
        using var templater = await OpenProjectFlowAsync(harness);
        var selected = (await templater.ResolveGroupAsync(Low, TemplateType.Project)).Group![0];

        var result = await selected.InvokeAsync(new(WorkingDirectory.FromExplicit(Target),
            new Dictionary<string, string?> { ["NotDeclared"] = "invalid" }));

        result.Status.Should().Be(TemplateInvocationStatus.InvalidArguments);
        result.ProjectConfigurations.Should().BeEmpty();
        result.ConfiguredProjects.Should().BeEmpty();
        Directory.Exists(Target).Should().BeFalse();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Command_InteractiveJsonUnavailableDiscovery_WritesOnlyOneStructuredResult(bool invalidArguments)
    {
        WriteProject("valid", Low);
        WriteProject("restricted", "Func.Init.Restricted", extraConstraint: """ "missing":{"type":"func-workload","args":"java"} """);
        await InstallAsync();
        using BufferedConsole stdout = new(true, true, noColor: true);
        using BufferedConsole stderr = new(true, true, noColor: true);
        stderr.Enqueue(ConsoleKey.Enter);
        var harness = CreateHarness(new SpectreInteractionService(new DefaultTheme(), stdout, stderr));
        string[] tail = invalidArguments ? ["--unknown", "invalid"] : [];

        int exit = await harness.InvokeAsync([Target, "--output", "json", "--dry-run", .. tail]);

        exit.Should().Be(invalidArguments ? 1 : 0);
        using var json = JsonDocument.Parse(stdout.Output);
        json.RootElement.GetProperty("kind").GetString().Should().Be(invalidArguments ? "invalidArguments" : "previewed");
        var unavailable = json.RootElement.GetProperty("unavailableTemplates").EnumerateArray().Should().ContainSingle().Subject;
        unavailable.GetProperty("identity").GetString().Should().Be("Func.Init.Restricted");
        unavailable.GetProperty("constraints").GetArrayLength().Should().BeGreaterThan(0);
        stderr.Output.Should().Contain("Select a project-template family:");
        Directory.Exists(Target).Should().BeFalse();
    }

    [Fact]
    public async Task Command_JsonNoEligibleTemplates_RemainsOneStructuredFailureWithoutPrompt()
    {
        WriteProject("restricted", Low, extraConstraint: """ "missing":{"type":"func-workload","args":"java"} """);
        await InstallAsync();
        using BufferedConsole stdout = new(true, true, noColor: true);
        using BufferedConsole stderr = new(true, true, noColor: true);
        var harness = CreateHarness(new SpectreInteractionService(new DefaultTheme(), stdout, stderr));

        int exit = await harness.InvokeAsync([Target, "--output", "json"]);

        exit.Should().Be(1);
        using var json = JsonDocument.Parse(stdout.Output);
        json.RootElement.GetProperty("kind").GetString().Should().Be("noCompatibleTemplates");
        json.RootElement.GetProperty("templates").GetArrayLength().Should().Be(1);
        stderr.Reads.Should().Be(0);
        Directory.Exists(Target).Should().BeFalse();
    }

    [Fact]
    public async Task Command_InteractiveJsonCancellation_DoesNotWriteDiscoveryOrSuccessToStdout()
    {
        WriteProject("valid", Low);
        WriteProject("restricted", "Func.Init.Restricted", extraConstraint: """ "missing":{"type":"func-workload","args":"java"} """);
        await InstallAsync();
        using BufferedConsole stdout = new(true, true, noColor: true);
        using BufferedConsole stderr = new(true, true, noColor: true);
        stderr.Enqueue(ConsoleKey.C, '\u0003', control: true);
        var harness = CreateHarness(new SpectreInteractionService(new DefaultTheme(), stdout, stderr));

        Func<Task> act = () => harness.InvokeAsync([Target, "--output", "json", "--dry-run"]);

        await act.Should().ThrowAsync<OperationCanceledException>();
        stdout.Output.Should().BeEmpty();
        harness.Result.Should().BeNull();
        Directory.Exists(Target).Should().BeFalse();
        await AssertDisposedAsync(harness.Factory);
    }

    public void Dispose()
    {
        foreach (IServiceScope scope in _scopes)
        {
            scope.Dispose();
        }

        foreach (ServiceProvider provider in _providers)
        {
            provider.Dispose();
        }

        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private Harness CreateHarness(IInteractionService? interaction = null, IFileSystem? writerFileSystem = null,
        IReadOnlyList<IProjectStack>? additionalStacks = null, Action<InitExecutionResult>? afterRunnerReturn = null,
        ITemplaterFactory? templateFactory = null)
    {
        interaction ??= _interaction;
        ServiceCollection services = new();
        services.AddLogging();
        services.AddSingleton(interaction);
        services.AddSingleton<IFileSystem, PhysicalFileSystem>();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddSingleton(new CliConfigurationPathsOptions(Home));
        services.AddSingleton<ILocalSettingsProvider, LocalSettingsProvider>();
        services.AddSingleton<ICliConfigurationProvider, CliConfigurationProvider>();
        services.AddOptions();
        services.AddSingleton<IConfigureOptions<StackOptions>, StackOptionsSetup>();
        services.AddSingleton<IConfigureOptions<ProjectProfileOptions>, ProjectProfileOptionsSetup>();
        services.AddSingleton<ProfileDocumentParser>();
        services.AddSingleton<IProfileSource, BuiltInProfileSource>();
        services.AddSingleton<IProfileCatalog, ProfileCatalog>();
        services.AddSingleton<IProfileResolver, ProfileResolver>();
        foreach (IProjectStack stack in additionalStacks ?? [])
        {
            services.AddSingleton(stack);
        }

        new NodeWorkload().Configure(new DefaultFunctionsCliBuilder(services, TestWorkloads.CreateInfo("Controlled.Node")));
        services.AddSingleton<InstalledProjectStackCatalog>();
        services.AddSingleton<IFunctionsProjectResolver, FunctionsProjectResolver>();
        services.AddTemplatesOrchestrator();
        var factory = new RecordingFactory(templateFactory ?? new TemplaterFactory(new TemplateStore(
            new CliConfigurationPathsOptions(Home), new PhysicalFileSystem(), new FileTemplateStoreLock(), NullLoggerFactory.Instance),
            new PhysicalFileSystem()));
        services.AddSingleton<ITemplaterFactory>(factory);
        if (writerFileSystem is not null)
        {
            services.AddSingleton(new TemplateProjectConfigurationWriter(writerFileSystem));
        }

        services.AddSingleton<TemplateProjectSupport>();
        int supportRequests = 0;
        int catalogRequests = 0;
        services.AddSingleton<Func<InstalledProjectStackCatalog>>(provider => () =>
        {
            catalogRequests++;
            return provider.GetRequiredService<InstalledProjectStackCatalog>();
        });
        services.AddSingleton<Func<TemplateProjectSupport>>(provider => () =>
        {
            supportRequests++;
            return provider.GetRequiredService<TemplateProjectSupport>();
        });
        ServiceProvider provider = services.BuildServiceProvider();
        _providers.Add(provider);
        var scope = provider.CreateScope();
        _scopes.Add(scope);
        var runner = new RecordingRunner(scope.ServiceProvider.GetRequiredService<IInitExecutionRunner>(), afterRunnerReturn);
        InitExecutionCommand command = new(runner, provider.GetRequiredService<InitExecutionRenderer>(), new([
            new("Controlled.Node", NuGetVersion.Parse("1.0.0"), ["node"], true),
        ]));
        return new(provider, command, runner, factory, () => supportRequests, () => catalogRequests);
    }

    private void WriteVariants()
    {
        WriteProject("low", Low, group: "Func.Init.Group", precedence: 10, parameters: Parameters,
            hostMetadata: """{"symbolInfo":{"Greeting":{"longName":"greeting"},"Auth":{"longName":"output","shortName":"n"}}}""");
        WriteProject("high", "Func.Init.High", group: "Func.Init.Group", precedence: 100);
        WriteProject("high2", "Func.Init.High2", group: "Func.Init.Group", precedence: 100);
        WriteProject("ts", "Func.Init.TypeScript", group: "Func.Init.Group", languages: ["TypeScript"]);
    }

    private JsonObject WriteProject(string folder, string identity, string? group = null, int precedence = 0, string? parameters = null,
        string[]? roots = null, string[]? languages = null, string? extraConstraint = null, string? hostMetadata = null, string shortName = "project")
    {
        roots ??= [""];
        languages ??= [.. roots.Select(_ => "JavaScript")];
        var outputs = new JsonArray();
        var actions = new JsonArray();
        for (int index = 0; index < roots.Length; index++)
        {
            string path = string.IsNullOrEmpty(roots[index]) ? "host.json" : roots[index] + "/host.json";
            outputs.Add(new JsonObject { ["path"] = path });
            actions.Add(new JsonObject
            {
                ["actionId"] = TemplateProjectConfigurationPlanner.ConfigurationActionId,
                ["args"] = new JsonObject { ["primaryOutputIndex"] = index.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    ["stack"] = "node", ["language"] = languages[index] },
            });
            string directory = Path.Combine(Package, folder, roots[index]);
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, "host.json"), """{"version":"2.0"}""");
            File.WriteAllText(Path.Combine(directory, "package.json"), """{"name":"ProjectName","dependencies":{"@azure/functions":"4.0.0"}}""");
            File.WriteAllText(Path.Combine(directory, "context.txt"), "ProjectName|GREETING|AUTH|ENABLED|OPTIONAL|EXECUTION|ROOT|STACK|LANGUAGE");
        }

        var constraints = JsonNode.Parse("""
            {"func":{"type":"host","args":[{"hostname":"func"}]},"node":{"type":"func-workload","args":"node"}}
            """)!.AsObject();
        if (extraConstraint is not null)
        {
            foreach (var pair in JsonNode.Parse("{" + extraConstraint + "}")!.AsObject())
            {
                constraints.Add(pair.Key, pair.Value!.DeepClone());
            }
        }

        JsonObject configuration = new()
        {
            ["identity"] = identity, ["name"] = identity, ["shortName"] = shortName, ["groupIdentity"] = group, ["precedence"] = precedence,
            ["tags"] = new JsonObject { ["type"] = "project" }, ["sourceName"] = "ProjectName",
            ["constraints"] = constraints, ["primaryOutputs"] = outputs, ["postActions"] = actions,
            ["symbols"] = JsonNode.Parse(parameters ?? "{}"),
        };
        SaveConfiguration(folder, configuration);
        if (hostMetadata is not null)
        {
            File.WriteAllText(Path.Combine(Package, folder, ".template.config", "func.host.json"), hostMetadata);
        }

        return configuration;
    }

    private void WriteOther(string folder, string identity, string? type)
    {
        JsonObject configuration = new() { ["identity"] = identity, ["name"] = identity, ["shortName"] = "project" };
        if (type is not null)
        {
            configuration["tags"] = new JsonObject { ["type"] = type };
        }

        SaveConfiguration(folder, configuration);
    }

    private void SaveConfiguration(string folder, JsonObject configuration)
    {
        string directory = Path.Combine(Package, folder, ".template.config");
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "template.json"), configuration.ToJsonString());
    }

    private Task InstallAsync() => TemplateFixtures.InstallAsync(Package, Path.Combine(Home, "templates"), CancellationToken.None);

    private Task<Templater> OpenProjectFlowAsync(Harness harness, string? target = null) => harness.Factory.CreateProjectAsync(
        new(WorkingDirectory.FromExplicit(target ?? Target), new(WorkingDirectory.FromExplicit(target ?? Target), stack: null)),
        new([new("Controlled.Node", NuGetVersion.Parse("1.0.0"), ["node"], true)]),
        harness.Provider.GetRequiredService<TemplateProjectSupport>(), CancellationToken.None);

    private Dictionary<string, string> SnapshotHome() => Directory.GetFiles(Path.Combine(Home, "templates"), "*", SearchOption.AllDirectories)
        .ToDictionary(path => Path.GetRelativePath(Home, path), File.ReadAllText);

    private static Dictionary<string, string> StoreBytes(string path) => Directory.GetFiles(path, "*", SearchOption.AllDirectories)
        .ToDictionary(file => Path.GetRelativePath(path, file), file => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(file))));

    private void AssertJson(string kind, int planned, int completed)
    {
        using var json = JsonDocument.Parse(_interaction.Lines.Last(line => line.StartsWith("JSON: ", StringComparison.Ordinal))["JSON: ".Length..]);
        json.RootElement.GetProperty("kind").GetString().Should().Be(kind);
        var invocation = json.RootElement.GetProperty("invocation");
        invocation.GetProperty("plannedConfigurations").GetArrayLength().Should().Be(planned);
        invocation.GetProperty("configuredProjects").GetArrayLength().Should().Be(completed);
        if (kind == "previewed")
        {
            invocation.GetProperty("completedChanges").GetArrayLength().Should().Be(0);
        }
    }

    private static IInteractionService Interactive()
    {
        var interaction = Substitute.For<IInteractionService>();
        interaction.IsInteractive.Returns(true);
        interaction.PromptForSelectionAsync(Arg.Any<string>(), Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>())
            .Returns(call => call.ArgAt<IEnumerable<string>>(1).First());
        return interaction;
    }

    private static IFileSystem WriterFileSystem()
    {
        var fileSystem = Substitute.For<IFileSystem>();
        fileSystem.FileExists(Arg.Any<string>()).Returns(call => File.Exists(call.Arg<string>()));
        fileSystem.DirectoryExists(Arg.Any<string>()).Returns(call => Directory.Exists(call.Arg<string>()));
        fileSystem.CreateAllTextAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(call => new PhysicalFileSystem().CreateAllTextAsync(call.ArgAt<string>(0), call.ArgAt<string>(1), call.ArgAt<CancellationToken>(2)));
        return fileSystem;
    }

    private static async Task AssertDisposedAsync(RecordingFactory factory)
    {
        foreach (var templater in factory.Templaters)
        {
            Func<Task> act = () => templater.ListAsync(TemplateType.Project);
            await act.Should().ThrowAsync<ObjectDisposedException>();
        }
    }

    private sealed class Harness(ServiceProvider provider, InitExecutionCommand command, RecordingRunner runner,
        RecordingFactory factory, Func<int> supportRequests, Func<int> catalogRequests)
    {
        public ServiceProvider Provider => provider;
        public InitExecutionResult? Result => runner.Result;
        public IInitExecutionRunner Runner => runner;
        public RecordingFactory Factory => factory;
        public int SupportRequests => supportRequests();
        public int CatalogRequests => catalogRequests();

        public Task<int> InvokeAsync(string[] args, CancellationToken cancellationToken = default) =>
            command.ParseInvocation(args).InvokeAsync(new InvocationConfiguration { EnableDefaultExceptionHandler = false }, cancellationToken);
    }

    private sealed class RecordingRunner(IInitExecutionRunner inner, Action<InitExecutionResult>? afterReturn = null) : IInitExecutionRunner
    {
        public InitExecutionResult? Result { get; private set; }

        public async Task<InitExecutionResult> ExecuteAsync(
            InitExecutionRequest request, TemplateConstraintEvaluationSnapshot availability, CancellationToken cancellationToken)
        {
            Result = null;
            Result = await inner.ExecuteAsync(request, availability, cancellationToken);
            afterReturn?.Invoke(Result);
            return Result;
        }
    }

    private sealed class CancelAfterConfigurationCommitFileSystem(CancellationTokenSource cancellation, int cancelOnCommit) : PhysicalFileSystem
    {
        private readonly CancellationTokenSource _cancellation = cancellation ?? throw new ArgumentNullException(nameof(cancellation));
        private readonly List<string> _committedConfigurations = [];

        public IReadOnlyList<string> CommittedConfigurations => _committedConfigurations.AsReadOnly();

        public override void MoveFile(string sourcePath, string destinationPath, bool overwrite = false)
        {
            base.MoveFile(sourcePath, destinationPath, overwrite);
            _committedConfigurations.Add(destinationPath);
            if (_committedConfigurations.Count == cancelOnCommit)
            {
                _cancellation.Cancel();
            }
        }
    }

    private sealed class RecordingFactory(ITemplaterFactory inner) : ITemplaterFactory
    {
        public List<TemplateEngineContext> Contexts { get; } = [];
        public List<Templater> Templaters { get; } = [];
        public List<TemplateProjectSupport> ProjectSupports { get; } = [];
        public List<TemplateConstraintEvaluationSnapshot> Availability { get; } = [];

        public Task<Templater> CreateAsync(TemplateEngineContext context, CancellationToken cancellationToken, Action<TemplateStoreWaitReason>? waiting = null)
            => throw new InvalidOperationException("INIT must use the project-capable flow.");

        public Task<Templater> CreateAsync(TemplateEngineContext context, TemplateConstraintEvaluationSnapshot snapshot,
            CancellationToken cancellationToken, Action<TemplateStoreWaitReason>? waiting = null)
            => throw new InvalidOperationException("INIT must use the project-capable flow.");

        public async Task<Templater> CreateProjectAsync(TemplateEngineContext context, TemplateConstraintEvaluationSnapshot snapshot,
            TemplateProjectSupport projectSupport, CancellationToken cancellationToken, Action<TemplateStoreWaitReason>? waiting = null)
        {
            Contexts.Add(context);
            ProjectSupports.Add(projectSupport);
            Availability.Add(snapshot);
            var templater = await inner.CreateProjectAsync(context, snapshot, projectSupport, cancellationToken, waiting);
            Templaters.Add(templater);
            return templater;
        }

    }

    private sealed class CancelAfterCaptureFactory(string hive, CancellationTokenSource cancellation) : ITemplaterFactory
    {
        private CancelAfterCaptureSession? _session;

        public CreationResultStatus? NativeStatus => _session?.NativeStatus;
        public int NativeCalls => _session?.NativeCalls ?? 0;

        public Task<Templater> CreateAsync(TemplateEngineContext context, CancellationToken cancellationToken,
            Action<TemplateStoreWaitReason>? waiting = null)
            => throw new InvalidOperationException("INIT requires a project-capable flow.");

        public Task<Templater> CreateAsync(TemplateEngineContext context, TemplateConstraintEvaluationSnapshot snapshot,
            CancellationToken cancellationToken, Action<TemplateStoreWaitReason>? waiting = null)
            => throw new InvalidOperationException("INIT requires a project-capable flow.");

        public Task<Templater> CreateProjectAsync(TemplateEngineContext context, TemplateConstraintEvaluationSnapshot snapshot,
            TemplateProjectSupport projectSupport, CancellationToken cancellationToken, Action<TemplateStoreWaitReason>? waiting = null)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _session = new(context, hive, snapshot, cancellation);
            return Task.FromResult(TemplateFixtures.CreateTemplater(_session, projectSupport: projectSupport));
        }
    }

    private sealed class CancelAfterCaptureSession(TemplateEngineContext context, string hive,
        TemplateConstraintEvaluationSnapshot snapshot, CancellationTokenSource cancellation) : TemplateEngineSession(context, hive, snapshot)
    {
        public CreationResultStatus? NativeStatus { get; private set; }
        public int NativeCalls { get; private set; }

        protected override async Task<ITemplateCreationResult> InstantiateAsync(ITemplateInfo template, TemplateInvocationRequest request,
            InputDataSet parameters, bool dryRun, CancellationToken cancellationToken)
        {
            NativeCalls++;
            var result = await base.InstantiateAsync(template, request, parameters, dryRun, cancellationToken);
            NativeStatus = result.Status;
            await cancellation.CancelAsync();
            return result;
        }
    }
}
