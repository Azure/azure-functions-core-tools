// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.CommandLine;
using System.Text.Json;
using Azure.Functions.Cli.Bundles;
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

public sealed class NewExecutionJourneyTests : IDisposable
{
    private const string Low = "Func.Consumer.Low";
    private readonly string _root = Path.Combine(Path.GetTempPath(), "func-consumer-" + Guid.NewGuid().ToString("N"));
    private readonly TestInteractionService _interaction = new();
    private readonly List<ServiceProvider> _providers = [];

    private string Project => Path.Combine(_root, "project");
    private string Target => Path.Combine(Project, "functions", "nested");
    private string Home => Path.Combine(_root, "home");

    [Fact]
    public async Task Command_InstalledItemPreviewThenCreate_UsesCanonicalInputsAndDistinctNestedContext()
    {
        await InstallAsync();
        var harness = CreateHarness();
        string[] args = ["native-item", "--path", Target, "--name", "Generated", "--language", "js", "--output", "json",
            "--greeting=Hello from command", "--param:Auth", "function", "--feature", "true"];
        var before = Snapshot();

        int previewExit = await harness.InvokeAsync([.. args, "--dry-run"]);
        var preview = harness.Result!;

        previewExit.Should().Be(0);
        preview.Kind.Should().Be("previewed");
        preview.SelectedTemplate!.Identity.Should().Be(Low);
        preview.Context!.CommandDirectory.Info.FullName.Should().Be(Target);
        preview.Context.Project!.RootDirectory.Info.FullName.Should().Be(Project);
        preview.Context.Project.Language.Should().Be("JavaScript");
        preview.Context.Bundle.Should().Be(new TemplateEngineBundleContext(BundleHelpers.StableBundleId, "4.35.0"));
        preview.Invocation!.Changes.Should().ContainSingle(change => change.Path.EndsWith("Generated.txt", StringComparison.Ordinal));
        preview.Invocation.ProjectConfigurations.Should().BeEmpty();
        preview.Invocation.ConfiguredProjects.Should().BeEmpty();
        preview.SelectedTemplate.ProjectConfigurations.Should().BeNull();
        preview.SelectedTemplate.ProjectConfigurationDiagnostic.Should().BeNull();
        Snapshot().Should().BeEquivalentTo(before);
        Directory.Exists(Target).Should().BeFalse();
        var parsed = preview.ArgumentResults.Should().ContainSingle().Subject;
        parsed.ValuesByCanonicalName.Should().Contain("Greeting", "Hello from command").And.Contain("Auth", "function").And.Contain("Feature", "true");
        parsed.ValuesByCanonicalName.Should().NotContainKey("name").And.Contain("Optional", "leave");
        AssertJsonKind("previewed");

        int createExit = await harness.InvokeAsync(args);

        createExit.Should().Be(0);
        harness.Result!.Kind.Should().Be("created");
        File.ReadAllText(Path.Combine(Target, "Generated.txt"))
            .Should().Be($"Generated|Hello from command|function|True|leave|{Target}|{Project}|JavaScript");
        File.Exists(Path.Combine(Target, "High.txt")).Should().BeFalse();
        harness.Factory.Contexts.Should().HaveCount(2).And.OnlyHaveUniqueItems();
        foreach (Templater templater in harness.Factory.Templaters)
        {
            Func<Task> useDisposed = () => templater.ListAsync(TemplateType.Item);
            await useDisposed.Should().ThrowAsync<ObjectDisposedException>();
        }

        AssertJsonKind("created");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Command_RealJsonAdapter_PreservesOneStructuredPreviewOrFailure(bool invalidArguments)
    {
        await InstallAsync();
        using BufferedConsole stdout = new(true, true, noColor: true);
        using BufferedConsole stderr = new(true, true, noColor: true);
        var harness = CreateHarness(new SpectreInteractionService(new DefaultTheme(), stdout, stderr));
        var before = Snapshot();
        string[] tail = invalidArguments ? ["--unknown", "invalid"] : ["--greeting", "hello"];

        int exit = await harness.InvokeAsync([Low, "--path", Target, "--dry-run", "--output", "json", .. tail]);

        exit.Should().Be(invalidArguments ? 1 : 0);
        using var json = JsonDocument.Parse(stdout.Output);
        json.RootElement.GetProperty("kind").GetString().Should().Be(invalidArguments ? "invalidArguments" : "previewed");
        stderr.Reads.Should().Be(0);
        Directory.Exists(Target).Should().BeFalse();
        Snapshot().Should().BeEquivalentTo(before);
    }

    [Fact]
    public async Task Command_ExplicitLanguageAlias_OverridesProjectLanguageBeforeNativeBindingsAndSelection()
    {
        await InstallAsync();
        var harness = CreateHarness();

        int exit = await harness.InvokeAsync(["native-item", "--path", Target, "--language", "ts", "--name", "Typed", "--greeting", "hello"]);

        exit.Should().Be(0);
        harness.Result!.SelectedTemplate!.Identity.Should().Be("Func.Consumer.TypeScript");
        harness.Result.Context!.Project!.Language.Should().Be("TypeScript");
        File.ReadAllText(Path.Combine(Target, "Typed.txt")).Should().Contain("hello|TypeScript");
    }

    [Theory]
    [InlineData("--unknown", "value")]
    [InlineData("--greeting")]
    [InlineData("--greeting", "hello", "--param:Auth", "not-a-choice")]
    [InlineData("--greeting", "hello", "--feature", "not-a-bool")]
    [InlineData("--greeting", "hello", "--greeting", "again")]
    [InlineData("--greeting", "hello", "--", "tail")]
    [InlineData("--greeting", "hello", "@response-file")]
    public async Task Command_InvalidTail_HasProvenanceNoPromptNoWrites(params string[] tail)
    {
        await InstallAsync();
        var harness = CreateHarness();
        var before = Snapshot();

        int exit = await harness.InvokeAsync(["--template", Low, "--path", Target, "--force", "--output", "json", .. tail]);

        exit.Should().Be(1);
        harness.Result!.Kind.Should().Be("invalidArguments");
        harness.Result.ArgumentResults.Should().ContainSingle().Which.InvalidInputDiagnostics
            .Should().Contain(diagnostic => diagnostic.TokenIndices.Count > 0);
        Directory.Exists(Target).Should().BeFalse();
        Snapshot().Should().BeEquivalentTo(before);
        _interaction.AllOutput.Should().NotContain("INPUT:").And.NotContain("SELECT:");
        AssertJsonKind("invalidArguments");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Command_RequiredValuesAndAmbiguity_RefusesBothNoninteractiveModes(bool explicitSwitch)
    {
        await InstallAsync();
        IInteractionService interaction = explicitSwitch ? Interactive() : _interaction;
        var harness = CreateHarness(interaction);
        string[] noninteractive = explicitSwitch ? ["--non-interactive"] : [];

        int requiredExit = await harness.InvokeAsync(["--template", Low, "--path", Target, .. noninteractive]);
        var required = harness.Result!;
        int ambiguousExit = await harness.InvokeAsync(["native-item", "--path", Target, .. noninteractive]);
        var ambiguous = harness.Result!;
        int omittedExit = await harness.InvokeAsync(["--path", Target, .. noninteractive]);

        requiredExit.Should().Be(1);
        required.Kind.Should().Be("missingRequired");
        required.Diagnostics.Should().ContainSingle().Which.Should().Contain("--greeting");
        ambiguousExit.Should().Be(1);
        ambiguous.Kind.Should().Be("ambiguousCandidates");
        ambiguous.Templates.Select(entry => entry.Identity).Should().BeEquivalentTo("Func.Consumer.High", "Func.Consumer.High2");
        omittedExit.Should().Be(1);
        harness.Result!.Kind.Should().Be("templateRequired");
        Directory.Exists(Target).Should().BeFalse();
        if (explicitSwitch)
        {
            interaction.ReceivedCalls().Should().NotContain(call => call.GetMethodInfo().Name.StartsWith("Prompt", StringComparison.Ordinal));
        }
    }

    [Fact]
    public async Task Command_InteractiveFamilyAndRequiredCompletion_ReparsesOriginalArgumentsThenCreates()
    {
        await InstallAsync();
        var interaction = Interactive();
        interaction.PromptForSelectionAsync("Select an item-template family:", Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>())
            .Returns("Func.Consumer.Group");
        interaction.PromptForInputAsync("--greeting (string)", null, Arg.Any<CancellationToken>()).Returns("Prompted");
        var harness = CreateHarness(interaction);

        int exit = await harness.InvokeAsync(["--path", Target, "--name", "PromptedName", "--param:Auth", "anonymous", "--feature", "false"]);

        exit.Should().Be(0);
        File.ReadAllText(Path.Combine(Target, "PromptedName.txt")).Should().StartWith("PromptedName|Prompted|anonymous|False|leave|");
        await interaction.Received(1).PromptForInputAsync("--greeting (string)", null, Arg.Any<CancellationToken>());
        interaction.ReceivedCalls().Should().NotContain(call =>
            call.GetMethodInfo().Name == nameof(IInteractionService.PromptForInputAsync)
                && call.GetArguments()[0]!.ToString()!.Contains("Optional", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Command_InvalidPromptCompletion_IsRejectedByFinalCanonicalParse()
    {
        await InstallAsync();
        var interaction = Interactive();
        interaction.PromptForInputAsync(Arg.Any<string>(), null, Arg.Any<CancellationToken>()).Returns("not-an-integer");
        var harness = CreateHarness(interaction);
        var before = Snapshot();

        int exit = await harness.InvokeAsync(["Func.Consumer.Number", "--path", Target]);

        exit.Should().Be(1);
        harness.Result!.Kind.Should().Be("invalidCompletion");
        harness.Result.ArgumentResults.Single().InvalidInputDiagnostics.Should().Contain(diagnostic => diagnostic.TokenIndices.Count == 0);
        Snapshot().Should().BeEquivalentTo(before);
        Directory.Exists(Target).Should().BeFalse();
    }

    [Theory]
    [InlineData("missing", "NotFound")]
    [InlineData("Func.Consumer.Project", "WrongType")]
    [InlineData("Func.Consumer.Restricted", "Unavailable")]
    [InlineData("Func.Consumer.Untyped", "InvalidTemplateType")]
    [InlineData("collision", "ambiguousGroups")]
    public async Task Command_UnselectableItems_ReportsActualRuntimeOutcomeWithoutWrites(string reference, string kind)
    {
        await InstallAsync();
        var harness = CreateHarness();
        var before = Snapshot();

        int exit = await harness.InvokeAsync(["--template", reference, "--path", Target, "--greeting", "hello", "--force", "--output", "json"]);

        exit.Should().Be(1);
        harness.Result!.Kind.Should().Be(kind);
        Snapshot().Should().BeEquivalentTo(before);
        Directory.Exists(Target).Should().BeFalse();
        if (kind == "WrongType")
        {
            harness.Result.Diagnostics.Single().Should().Contain("func init");
        }

        AssertJsonKind(kind);
    }

    [Fact]
    public async Task Command_List_UsesItemLanguageConstraintsAndVisibilityBeforeRendering()
    {
        await InstallAsync();
        var harness = CreateHarness();
        var before = Snapshot();

        int exit = await harness.InvokeAsync(["--path", Target, "--list", "--output", "json"]);

        exit.Should().Be(0);
        harness.Result!.Templates.Should().Contain(entry => entry.Identity == Low);
        harness.Result.Templates.Should().NotContain(entry => entry.Type != TemplateType.Item
            || entry.Identity == "Func.Consumer.Restricted" || entry.Identity == "Func.Consumer.Hidden" || entry.Identity == "Func.Consumer.TypeScript");
        Snapshot().Should().BeEquivalentTo(before);
        Directory.Exists(Target).Should().BeFalse();
        AssertJsonKind("listed");
    }

    [Theory]
    [InlineData("--list", "--unknown")]
    [InlineData("--list", "--dry-run")]
    [InlineData("--list", "--force")]
    [InlineData("--list", "--name", "Ignored")]
    public async Task Command_ListCannotHideUnparsedTail_StopsBeforeEngine(params string[] tokens)
    {
        await InstallAsync();
        var harness = CreateHarness();

        int exit = await harness.InvokeAsync(["--path", Target, .. tokens]);

        exit.Should().Be(1);
        harness.Result!.Kind.Should().Be("invalidListArguments");
        harness.Factory.Contexts.Should().BeEmpty();
    }

    [Theory]
    [InlineData("list", false)]
    [InlineData("list", true)]
    [InlineData("preview", false)]
    [InlineData("preview", true)]
    [InlineData("forcePreview", false)]
    [InlineData("forcePreview", true)]
    public async Task Command_ReadOnlyPendingJournal_RefusesWithoutRecoveryOrBootstrap(string mode, bool corrupt)
    {
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
        File.WriteAllText(Path.Combine(store.Hive, "uncommitted.txt"), "Retain until an owning lifecycle recovers.");
        File.WriteAllText(store.Journal, corrupt ? "corrupt" : "present");
        var hive = StoreBytes(store.Hive);
        var backup = StoreBytes(store.Backup);
        byte[] journal = File.ReadAllBytes(store.Journal);
        var project = Snapshot();
        var harness = CreateHarness(templateFactory: new TemplaterFactory(store, files));
        string[] args = ReadOnlyArguments(mode);

        Exception? failure = await Record.ExceptionAsync(() => harness.InvokeAsync(
            [.. args, "--path", Target, "--output", "json"]));

        StoreBytes(store.Hive).Should().BeEquivalentTo(hive);
        StoreBytes(store.Backup).Should().BeEquivalentTo(backup);
        File.ReadAllBytes(store.Journal).Should().Equal(journal);
        Snapshot().Should().BeEquivalentTo(project);
        Directory.Exists(Target).Should().BeFalse();
        failure.Should().BeOfType<TemplateStoreRecoveryRequiredException>().Which.Message.Should().Contain("owning lifecycle");
        harness.Factory.Contexts.Should().ContainSingle().Which.RequireCleanStore.Should().BeTrue();
        harness.Factory.Templaters.Should().BeEmpty();
        loggers.DidNotReceive().CreateLogger(Arg.Any<string>());
        await storeLock.DidNotReceive().AcquireAsync(Arg.Any<string>(), TemplateStoreAccess.Write,
            Arg.Any<CancellationToken>(), Arg.Any<Action<TemplateStoreWaitReason>?>());
        _interaction.Lines.Should().NotContain(line => line.StartsWith("JSON:", StringComparison.Ordinal)
            || line.StartsWith("SUCCESS:", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("list")]
    [InlineData("preview")]
    [InlineData("forcePreview")]
    public async Task Command_ReadOnlyCleanStore_PreservesLiveHiveAndProject(string mode)
    {
        await InstallAsync();
        var beforeHive = StoreBytes(Path.Combine(Home, "templates"));
        var beforeProject = Snapshot();
        var harness = CreateHarness();

        int exit = await harness.InvokeAsync([.. ReadOnlyArguments(mode), "--path", Target, "--output", "json"]);

        exit.Should().Be(0);
        harness.Result!.Context!.RequireCleanStore.Should().BeTrue();
        harness.Result.Context.Project!.RootDirectory.Info.FullName.Should().Be(Project);
        StoreBytes(Path.Combine(Home, "templates")).Should().BeEquivalentTo(beforeHive);
        Snapshot().Should().BeEquivalentTo(beforeProject);
        Directory.Exists(Target).Should().BeFalse();
        harness.Factory.Templaters.Should().ContainSingle();
    }

    [Fact]
    public async Task Command_CreatePendingJournal_RetainsOrdinaryRecovery()
    {
        await InstallAsync();
        var files = new PhysicalFileSystem();
        var store = new TemplateStore(new(Home), files, new FileTemplateStoreLock(), NullLoggerFactory.Instance);
        files.CopyDirectory(store.Hive, store.Backup);
        var backup = StoreBytes(store.Backup);
        string uncommitted = Path.Combine(store.Hive, "uncommitted.txt");
        File.WriteAllText(uncommitted, "unfinished store mutation");
        File.WriteAllText(store.Journal, "present");
        var harness = CreateHarness();

        int exit = await harness.InvokeAsync([Low, "--path", Target, "--name", "Recovered", "--greeting", "recovery"]);

        exit.Should().Be(0);
        harness.Result!.Context!.RequireCleanStore.Should().BeFalse();
        harness.Result.Invocation!.Status.Should().Be(TemplateInvocationStatus.Success);
        File.ReadAllText(Path.Combine(Target, "Recovered.txt")).Should().StartWith("Recovered|recovery|");
        File.Exists(uncommitted).Should().BeFalse();
        File.Exists(store.Journal).Should().BeFalse();
        StoreBytes(store.Backup).Should().BeEquivalentTo(backup);
    }

    [Fact]
    public async Task Command_EmptyList_RendersUsefulHintWithoutCreatingOutput()
    {
        await InstallAsync();
        var harness = CreateHarness();
        var before = Snapshot();

        int exit = await harness.InvokeAsync(["missing", "--list", "--path", Target]);

        exit.Should().Be(0);
        harness.Result!.Kind.Should().Be("listed");
        harness.Result.Templates.Should().BeEmpty();
        _interaction.AllOutput.Should().Contain("No installed item templates match").And.Contain("func new --list");
        Snapshot().Should().BeEquivalentTo(before);
        Directory.Exists(Target).Should().BeFalse();
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-language")]
    public async Task Command_InvalidExplicitLanguage_FailsBeforeOpeningStore(string language)
    {
        await InstallAsync();
        var harness = CreateHarness();

        int exit = await harness.InvokeAsync(["native-item", "--path", Target, "--language", language]);

        exit.Should().Be(1);
        harness.Result!.Kind.Should().Be("invalidLanguage");
        harness.Factory.Contexts.Should().BeEmpty();
    }

    [Theory]
    [InlineData("[9.0.0,10.0.0)", "4.35.0")]
    [InlineData("[4.0.0,5.0.0)", null)]
    [InlineData("invalid-range", "4.35.0")]
    public async Task Command_UnresolvableBundle_GatesBothListAndCreationBeforeFactory(string range, string? installed)
    {
        await InstallAsync();
        WriteHost(range);
        var harness = CreateHarness(bundleVersion: installed);
        var before = Snapshot();

        int listExit = await harness.InvokeAsync(["--path", Target, "--list"]);
        int createExit = await harness.InvokeAsync(["native-item", "--path", Target, "--greeting", "hello"]);

        listExit.Should().Be(1);
        createExit.Should().Be(1);
        harness.Result!.Kind.Should().Be("bundleUnavailable");
        harness.Result.Diagnostics.Single().Should().NotBeNullOrWhiteSpace();
        harness.Factory.Contexts.Should().BeEmpty();
        Snapshot().Should().BeEquivalentTo(before);
    }

    [Fact]
    public async Task Command_NoContainingProject_DoesNotGuessFromNodeSourcesOrOpenEngine()
    {
        await InstallAsync();
        string outside = Path.Combine(_root, "outside");
        Directory.CreateDirectory(outside);
        File.WriteAllText(Path.Combine(outside, "package.json"), "{}");
        var harness = CreateHarness();

        int exit = await harness.InvokeAsync(["native-item", "--path", Path.Combine(outside, "missing")]);

        exit.Should().Be(1);
        harness.Result!.Kind.Should().Be("projectRequired");
        harness.Result.Diagnostics.Single().Should().Contain("func init");
        harness.Factory.Contexts.Should().BeEmpty();
    }

    [Fact]
    public async Task Command_NestedSources_DoNotBecomeProjectRootAndNoBundleRemainsAbsent()
    {
        await InstallAsync();
        File.WriteAllText(Path.Combine(Project, "host.json"), """{"version":"2.0"}""");
        Directory.CreateDirectory(Target);
        File.WriteAllText(Path.Combine(Target, "local.js"), "// not a separate project");
        var harness = CreateHarness();

        int exit = await harness.InvokeAsync([Low, "--path", Target, "--greeting", "hello", "--dry-run"]);

        exit.Should().Be(0);
        harness.Result!.Context!.Project!.RootDirectory.Info.FullName.Should().Be(Project);
        harness.Result.Context.Bundle.Should().BeNull();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Command_ComposedBundleRequirement_UsesResolvedContextWithWorkloadOnlyAvailability(bool declaredBundle)
    {
        WriteTemplate("bundle-context", """
            {
              "identity":"Func.Consumer.BundleContext","name":"Bundle context","shortName":"bundle-context",
              "tags":{"type":"item","language":"JavaScript"},
              "constraints":{"bundle":{"type":"func-bundle",
                "args":{"id":"Microsoft.Azure.Functions.ExtensionBundle","version":"[4.0.0,5.0.0)"}}},
              "symbols":{
                "BundleId":{"type":"bind","binding":"host:func:bundle-id","replaces":"BUNDLE_ID"},
                "BundleVersion":{"type":"bind","binding":"host:func:bundle-version","replaces":"BUNDLE_VERSION"}
              }
            }
            """, "bundle.txt", "BUNDLE_ID|BUNDLE_VERSION");
        await InstallAsync();
        if (!declaredBundle)
        {
            File.WriteAllText(Path.Combine(Project, "host.json"), """{"version":"2.0"}""");
        }

        var harness = CreateHarness();
        var before = Snapshot();

        int previewExit = await harness.InvokeAsync(["Func.Consumer.BundleContext", "--path", Target, "--dry-run"]);
        var preview = harness.Result!;

        previewExit.Should().Be(declaredBundle ? 0 : 1);
        Snapshot().Should().BeEquivalentTo(before);
        Directory.Exists(Target).Should().BeFalse();
        harness.Factory.Contexts.Should().ContainSingle().Which.Should().BeSameAs(preview.Context);
        if (declaredBundle)
        {
            preview.Context!.Bundle.Should().Be(new TemplateEngineBundleContext(BundleHelpers.StableBundleId, "4.35.0"));
            preview.Invocation!.Status.Should().Be(TemplateInvocationStatus.Success);

            int createExit = await harness.InvokeAsync(["Func.Consumer.BundleContext", "--path", Target]);

            createExit.Should().Be(0);
            File.ReadAllText(Path.Combine(Target, "bundle.txt")).Should().Be($"{BundleHelpers.StableBundleId}|4.35.0");
        }
        else
        {
            preview.Kind.Should().Be("Unavailable");
            preview.Context!.Bundle.Should().BeNull();
            preview.Templates.Should().ContainSingle().Which.ConstraintEvaluation.Diagnostics
                .Should().Contain(diagnostic => diagnostic.Type == "func-bundle");
        }
    }

    [Fact]
    public async Task Command_ConflictPreviewAndForce_KeepPreviewReadOnlyAndForceLimitedToWrites()
    {
        await InstallAsync();
        Directory.CreateDirectory(Target);
        string file = Path.Combine(Target, "Existing.txt");
        File.WriteAllText(file, "user-owned");
        var harness = CreateHarness();
        string[] args = [Low, "--path", Target, "--name", "Existing", "--greeting", "new content"];
        var before = Snapshot();

        int previewExit = await harness.InvokeAsync([.. args, "--dry-run"]);
        var preview = harness.Result!;
        int refusedExit = await harness.InvokeAsync(args);
        var refused = harness.Result!;
        int forcedPreviewExit = await harness.InvokeAsync([.. args, "--dry-run", "--force"]);
        var forcedPreview = harness.Result!;

        previewExit.Should().Be(0);
        preview.Kind.Should().Be("previewed");
        preview.Invocation!.Changes.Should().Contain(change => change.Kind != TemplateFileChangeKind.Create);
        preview.Invocation.Diagnostic.Should().Contain("force");
        refusedExit.Should().Be(1);
        refused.Invocation!.Status.Should().Be(TemplateInvocationStatus.FileConflict);
        forcedPreviewExit.Should().Be(0);
        forcedPreview.Kind.Should().Be("previewed");
        forcedPreview.Context!.RequireCleanStore.Should().BeTrue();
        forcedPreview.Invocation!.Changes.Should().Contain(change => change.Kind == TemplateFileChangeKind.Overwrite);
        Snapshot().Should().BeEquivalentTo(before);
        _interaction.AllOutput.Should().Contain("Re-run with --force");

        int forceExit = await harness.InvokeAsync([.. args, "--force"]);

        forceExit.Should().Be(0);
        File.ReadAllText(file).Should().StartWith("Existing|new content|anonymous|False|leave|");
    }

    [Fact]
    public async Task Command_EmptyItem_IsAnExplicitSuccessfulNoOp()
    {
        await InstallAsync();
        var harness = CreateHarness();
        var before = Snapshot();

        int exit = await harness.InvokeAsync(["Func.Consumer.Empty", "--path", Target]);

        exit.Should().Be(0);
        harness.Result!.Invocation!.Changes.Should().BeEmpty();
        Snapshot().Should().BeEquivalentTo(before);
    }

    [Fact]
    public async Task Command_ShortFallbackAliasAndNegativeValue_PreserveCanonicalMapping()
    {
        await InstallAsync();
        var harness = CreateHarness();

        int aliasExit = await harness.InvokeAsync([Low, "--path", Target, "--greeting", "hello", "-p:n", "function", "--dry-run"]);
        var aliasResult = harness.Result!;
        int numericExit = await harness.InvokeAsync(["Func.Consumer.Number", "--path", Target, "--Count", "-4", "--dry-run"]);

        aliasExit.Should().Be(0);
        aliasResult.ArgumentResults.Single().ValuesByCanonicalName.Should().Contain("Auth", "function");
        numericExit.Should().Be(0);
        harness.Result!.ArgumentResults.Single().ValuesByCanonicalName.Should().Contain("Count", "-4");
        Directory.Exists(Target).Should().BeFalse();
    }

    [Fact]
    public async Task Command_TypedRequiredValues_ReportsAllMissingAndPromptsOnlyRequired()
    {
        await InstallAsync();
        var harness = CreateHarness();

        int missingExit = await harness.InvokeAsync(["Func.Consumer.Required", "--path", Target]);

        missingExit.Should().Be(1);
        harness.Result!.Diagnostics.Single().Should().Contain("--Count").And.Contain("--Mode").And.Contain("--Enabled");
        harness.Result.ArgumentResults.Single().MissingRequiredParameters.Should().HaveCount(3);
        var interaction = Interactive();
        interaction.PromptForInputAsync("--Count (integer)", null, Arg.Any<CancellationToken>()).Returns("42");
        var interactive = CreateHarness(interaction);

        int exit = await interactive.InvokeAsync(["Func.Consumer.Required", "--path", Target, "--dry-run"]);

        exit.Should().Be(0);
        var values = interactive.Result!.ArgumentResults.Single().ValuesByCanonicalName;
        values.Should().Contain("Count", "42").And.Contain("Mode", "first").And.Contain("Enabled", "true");
        Directory.Exists(Target).Should().BeFalse();
    }

    [Fact]
    public async Task Command_HiddenRequiredValue_IsAnAuthoringDiagnosticNotAPrompt()
    {
        await InstallAsync();
        var interaction = Interactive();
        var harness = CreateHarness(interaction);

        int exit = await harness.InvokeAsync(["Func.Consumer.HiddenRequired", "--path", Target]);

        exit.Should().Be(1);
        harness.Result!.ArgumentResults.Single().DefinitionDiagnostics.Should().Contain(diagnostic => diagnostic.Contains("hidden", StringComparison.Ordinal));
        interaction.ReceivedCalls().Should().NotContain(call => call.GetMethodInfo().Name.StartsWith("Prompt", StringComparison.Ordinal));
        Directory.Exists(Target).Should().BeFalse();
    }

    [Fact]
    public async Task Command_DifferentProjectsUseSeparateNamedConfigurationAndBindings()
    {
        await InstallAsync();
        string second = Path.Combine(_root, "second-project");
        Directory.CreateDirectory(Path.Combine(second, ".func"));
        File.WriteAllText(Path.Combine(second, "host.json"), """{"version":"2.0"}""");
        File.WriteAllText(Path.Combine(second, "package.json"), "{}");
        File.WriteAllText(Path.Combine(second, ".func", "config.json"), """{"stack":{"runtime":"node","language":"ts"}}""");
        var harness = CreateHarness();

        await harness.InvokeAsync([Low, "--path", Target, "--greeting", "first", "--dry-run"]);
        var first = harness.Result!;
        int exit = await harness.InvokeAsync(["native-item", "--path", Path.Combine(second, "nested"), "--greeting", "second", "--dry-run"]);

        exit.Should().Be(0);
        first.Context!.Project!.Language.Should().Be("JavaScript");
        first.Context.Bundle.Should().NotBeNull();
        harness.Result!.Context!.Project!.Language.Should().Be("TypeScript");
        harness.Result.Context.Project.RootDirectory.Info.FullName.Should().Be(second);
        harness.Result.Context.Bundle.Should().BeNull();
        harness.Factory.Contexts.Should().HaveCount(2);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Command_FileTargetAndMalformedBundle_FailBeforeFactory(bool beneathFile)
    {
        await InstallAsync();
        var harness = CreateHarness();

        string filePath = Path.Combine(Project, "package.json");
        int fileExit = await harness.InvokeAsync([Low, "--path", beneathFile ? Path.Combine(filePath, "nested") : filePath]);
        var fileResult = harness.Result!;
        File.WriteAllText(Path.Combine(Project, "host.json"), """{"extensionBundle":{"id":"missing-version"}}""");
        int bundleExit = await harness.InvokeAsync(["--path", Target, "--list"]);

        fileExit.Should().Be(1);
        fileResult.Kind.Should().Be("invalidPath");
        bundleExit.Should().Be(1);
        harness.Result!.Kind.Should().Be("invalidBundle");
        harness.Factory.Contexts.Should().BeEmpty();
    }

    [Fact]
    public async Task Command_MalformedProjectConfiguration_ReturnsStructuredRepairFailureBeforeFactory()
    {
        await InstallAsync();
        File.WriteAllText(Path.Combine(Project, ".func", "config.json"), "{");
        var harness = CreateHarness();
        var before = Snapshot();

        int exit = await harness.InvokeAsync(["--path", Target, "--list", "--output", "json"]);

        exit.Should().Be(1);
        harness.Result!.Kind.Should().Be("invalidConfiguration");
        harness.Result.Diagnostics.Single().Should().Contain("config.json");
        harness.Factory.Contexts.Should().BeEmpty();
        Snapshot().Should().BeEquivalentTo(before);
        AssertJsonKind("invalidConfiguration");
    }

    [Theory]
    [InlineData("install")]
    [InlineData("update")]
    [InlineData("uninstall")]
    public async Task Command_LifecycleWordAsTemplateValue_ReachesActualInstalledCapability(string value)
    {
        await InstallAsync();
        var harness = CreateHarness();
        int childCalls = 0;
        Func<Command> lifecycle = () =>
        {
            Command child = new(value);
            child.SetAction(_ => childCalls++);
            return child;
        };

        int exit = await harness.InvokeAsync([Low, "--path", Target, "--name", "LifecycleValue", "--greeting", value],
            lifecycleFactories: [lifecycle]);

        exit.Should().Be(0);
        childCalls.Should().Be(0);
        File.ReadAllText(Path.Combine(Target, "LifecycleValue.txt")).Should().StartWith($"LifecycleValue|{value}|");
        harness.Result!.SelectedTemplate!.Identity.Should().Be(Low);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Command_NonInteractiveProfileSelection_FailsBeforeFactoryInsteadOfPrompting(bool explicitSwitch)
    {
        await InstallAsync();
        File.WriteAllText(Path.Combine(Project, ".func", "config.json"), """
            {"stack":{"runtime":"node","language":"js"},"profiles":["first","second"]}
            """);
        IInteractionService interaction = explicitSwitch ? Interactive() : _interaction;
        var harness = CreateHarness(interaction);
        var before = Snapshot();
        string[] flags = explicitSwitch ? ["--non-interactive"] : [];

        int exit = await harness.InvokeAsync(["--path", Target, "--list", .. flags, "--output", "json"]);

        exit.Should().Be(1);
        harness.Result!.Kind.Should().Be("invalidProfile");
        harness.Result.Diagnostics.Single().Should().NotContain("--profile").And.Contain("defaultProfile").And.Contain("func profile set");
        harness.Factory.Contexts.Should().BeEmpty();
        Snapshot().Should().BeEquivalentTo(before);
        if (explicitSwitch)
        {
            interaction.ReceivedCalls().Should().NotContain(call => call.GetMethodInfo().Name.StartsWith("Prompt", StringComparison.Ordinal));
        }
        else
        {
            AssertJsonKind("invalidProfile");
            _interaction.AllOutput.Should().NotContain("--profile");
        }
    }

    [Theory]
    [InlineData("bool")]
    [InlineData("Bool")]
    [InlineData("boolean")]
    [InlineData("Boolean")]
    [InlineData("BOOLEAN")]
    public async Task Command_RequiredBooleanCasing_UsesActualTrueFalseSelector(string dataType)
    {
        WritePromptTemplate(dataType);
        await InstallAsync();
        using BufferedConsole stdout = new(true, true, noColor: true);
        using BufferedConsole stderr = new(true, true, noColor: true);
        stderr.Enqueue(ConsoleKey.DownArrow);
        stderr.Enqueue(ConsoleKey.Enter);
        var harness = CreateHarness(new SpectreInteractionService(new DefaultTheme(), stdout, stderr));
        var before = Snapshot();

        int exit = await harness.InvokeAsync(["Func.Consumer.Prompt", "--path", Target, "--dry-run"]);

        exit.Should().Be(0);
        harness.Result!.ArgumentResults.Single().ValuesByCanonicalName.Should().Contain("Value", "false");
        stderr.Reads.Should().Be(2);
        stderr.Output.Should().Contain($"--Value ({dataType})").And.Contain("true").And.Contain("false");
        Snapshot().Should().BeEquivalentTo(before);
        Directory.Exists(Target).Should().BeFalse();
    }

    [Theory]
    [InlineData("string", "value[bad]", null)]
    [InlineData("string", "value[red]styled[/]", null)]
    [InlineData("choice", "value[bad]", "choice[unknown]")]
    [InlineData("choice", "value[red]styled[/]", "choice[red]styled[/]")]
    public async Task Command_RequiredPromptMetadata_RendersLiteralAndPreservesCanonicalValue(string dataType, string alias, string? choice)
    {
        WritePromptTemplate(dataType, alias, choice is null ? null : [choice]);
        await InstallAsync();
        using BufferedConsole stdout = new(true, true, noColor: true);
        using BufferedConsole stderr = new(true, true, noColor: true);
        if (choice is null)
        {
            stderr.Enqueue(ConsoleKey.X, 'x');
        }

        stderr.Enqueue(ConsoleKey.Enter);
        var harness = CreateHarness(new SpectreInteractionService(new DefaultTheme(), stdout, stderr));
        var before = Snapshot();

        int exit = await harness.InvokeAsync(["Func.Consumer.Prompt", "--path", Target, "--dry-run"]);

        exit.Should().Be(0);
        stderr.Output.Should().Contain($"--{alias} ({dataType})");
        if (choice is not null)
        {
            stderr.Output.Should().Contain(choice);
        }

        harness.Result!.ArgumentResults.Single().ValuesByCanonicalName.Should().Contain("Value", choice ?? "x");
        Snapshot().Should().BeEquivalentTo(before);
        Directory.Exists(Target).Should().BeFalse();
    }

    [Fact]
    public async Task Command_ProfileDefaultRemediation_UsesExistingConfigControlWithoutChangingUnknownArguments()
    {
        await InstallAsync();
        File.WriteAllText(Path.Combine(Project, ".func", "config.json"), """
            {"stack":{"runtime":"node","language":"js"},"profiles":["flex","linux-premium"]}
            """);
        var harness = CreateHarness();

        int missingExit = await harness.InvokeAsync(["--path", Target, "--list"]);

        missingExit.Should().Be(1);
        _interaction.AllOutput.Should().Contain("defaultProfile").And.Contain("func profile set").And.NotContain("--profile");
        await new ProjectProfileConfigStore(new PhysicalFileSystem()).SetDefaultProfileAsync(new(Project), "flex", CancellationToken.None);
        var repaired = CreateHarness();

        int repairedExit = await repaired.InvokeAsync(["--path", Target, "--list"]);
        int unknownExit = await repaired.InvokeAsync([Low, "--path", Target, "--greeting", "hello", "--profile", "flex", "--output", "json"]);

        repairedExit.Should().Be(0);
        unknownExit.Should().Be(1);
        repaired.Result!.Kind.Should().Be("invalidArguments");
        repaired.Result.ArgumentResults.Single().InvalidInputDiagnostics.Should().Contain(diagnostic =>
            diagnostic.Message.Contains("--profile", StringComparison.Ordinal));
        AssertJsonKind("invalidArguments");
        Directory.Exists(Target).Should().BeFalse();
    }

    [Fact]
    public async Task Command_UnknownConfiguredProfile_PreservesSpecificFailureInsteadOfReplacingItWithSelectionGuidance()
    {
        await InstallAsync();
        File.WriteAllText(Path.Combine(Project, ".func", "config.json"), """
            {"stack":{"runtime":"node","language":"js"},"defaultProfile":"not-installed"}
            """);
        var harness = CreateHarness();
        var before = Snapshot();

        int exit = await harness.InvokeAsync(["--path", Target, "--list", "--output", "json"]);

        exit.Should().Be(1);
        harness.Result!.Kind.Should().Be("invalidProfile");
        harness.Result.Diagnostics.Single().Should().Contain("not-installed").And.NotContain("func profile set");
        harness.Factory.Contexts.Should().BeEmpty();
        Snapshot().Should().BeEquivalentTo(before);
        AssertJsonKind("invalidProfile");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Command_FamilyAndCandidateLabels_AreLiteralAndSelectTheExactRawIdentity(bool sameFamily)
    {
        string? group = sameFamily ? "Group[bad]" : null;
        WritePromptTemplate("string", identity: "Func.Consumer.[bad]", group: group);
        WritePromptTemplate("string", identity: "Func.Consumer.[red]styled[/]", group: group, folder: "prompt-other");
        await InstallAsync();
        using BufferedConsole stdout = new(true, true, noColor: true);
        using BufferedConsole stderr = new(true, true, noColor: true);
        var harness = CreateHarness(LiteralChoiceInteraction(stdout, stderr));
        var before = Snapshot();

        int exit = await harness.InvokeAsync(["prompt", "--path", Target, "--dry-run"]);

        exit.Should().Be(0);
        stderr.Output.Should().Contain("Func.Consumer.[bad]").And.Contain("Func.Consumer.[red]styled[/]");
        harness.Result!.SelectedTemplate!.Identity.Should().Be("Func.Consumer.[red]styled[/]");
        harness.Result.ArgumentResults.Single().ValuesByCanonicalName.Should().Contain("Value", "x");
        Snapshot().Should().BeEquivalentTo(before);
        Directory.Exists(Target).Should().BeFalse();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Command_FamilyLabels_EitherOfferedOrder_SelectsExactRawIdentity(bool styledFirst)
    {
        WritePromptTemplate("string", identity: "Func.Consumer.[bad]");
        WritePromptTemplate("string", identity: "Func.Consumer.[red]styled[/]", folder: "prompt-other");
        await InstallAsync();
        using BufferedConsole stdout = new(true, true, noColor: true);
        using BufferedConsole stderr = new(true, true, noColor: true);
        List<string[]> offered = [];
        var harness = CreateHarness(LiteralChoiceInteraction(stdout, stderr, styledFirst, offered));
        var before = Snapshot();

        int exit = await harness.InvokeAsync(["prompt", "--path", Target, "--dry-run"]);

        exit.Should().Be(0);
        string[] actualChoices = offered.Should().ContainSingle().Subject;
        actualChoices.Should().Equal(styledFirst
            ? ["Func.Consumer.[red]styled[/]", "Func.Consumer.[bad]"]
            : ["Func.Consumer.[bad]", "Func.Consumer.[red]styled[/]"]);
        stderr.Reads.Should().Be(styledFirst ? 3 : 4);
        stderr.Output.Should().Contain("Func.Consumer.[bad]").And.Contain("Func.Consumer.[red]styled[/]");
        harness.Result!.Kind.Should().Be("previewed");
        harness.Result.SelectedTemplate!.Identity.Should().Be("Func.Consumer.[red]styled[/]");
        harness.Result.ArgumentResults.Single().ValuesByCanonicalName.Should().Contain("Value", "x");
        Snapshot().Should().BeEquivalentTo(before);
        Directory.Exists(Target).Should().BeFalse();
    }

    [Theory]
    [InlineData("string")]
    [InlineData("choice")]
    [InlineData("Bool")]
    public async Task Command_ActualLiteralPromptCancellation_ReleasesFlowAndDoesNotInvoke(string dataType)
    {
        WritePromptTemplate(dataType, "value[bad]", dataType == "choice" ? ["choice[bad]"] : null);
        await InstallAsync();
        using BufferedConsole stdout = new(true, true, noColor: true);
        using BufferedConsole stderr = new(true, true, noColor: true);
        stderr.Enqueue(ConsoleKey.C, '\u0003', control: true);
        var harness = CreateHarness(new SpectreInteractionService(new DefaultTheme(), stdout, stderr));
        var before = Snapshot();

        Func<Task> act = () => harness.InvokeAsync(["Func.Consumer.Prompt", "--path", Target]);

        await act.Should().ThrowAsync<OperationCanceledException>();
        stderr.Reads.Should().Be(1);
        harness.Result.Should().BeNull();
        Snapshot().Should().BeEquivalentTo(before);
        Directory.Exists(Target).Should().BeFalse();
        Func<Task> useDisposed = () => harness.Factory.Templaters.Single().ListAsync(TemplateType.Item);
        await useDisposed.Should().ThrowAsync<ObjectDisposedException>();
    }

    [Fact]
    public async Task Command_UnexpectedChoiceAnswer_FailsFinalCanonicalValidationWithoutUnescapingInvalidInput()
    {
        WritePromptTemplate("choice", choices: ["choice[bad]"]);
        await InstallAsync();
        var interaction = Interactive();
        interaction.PromptForSelectionAsync(Arg.Any<string>(), Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>())
            .Returns("not-offered[[bad]]");
        interaction.When(service => service.WriteJson(Arg.Any<object>())).Do(call => _interaction.WriteJson(call.Arg<object>()));
        var harness = CreateHarness(interaction);
        var before = Snapshot();

        int exit = await harness.InvokeAsync(["Func.Consumer.Prompt", "--path", Target, "--output", "json"]);

        exit.Should().Be(1);
        harness.Result!.Kind.Should().Be("invalidCompletion");
        harness.Result.ArgumentResults.Single().InvalidInputDiagnostics.Should().Contain(diagnostic =>
            diagnostic.Message.Contains("not-offered[[bad]]", StringComparison.Ordinal) && diagnostic.TokenIndices.Count == 0);
        Snapshot().Should().BeEquivalentTo(before);
        Directory.Exists(Target).Should().BeFalse();
        AssertJsonKind("invalidCompletion");
        _interaction.AllOutput.Should().Contain("not-offered[[bad]]");
    }

    [Theory]
    [InlineData("choice", "not-offered")]
    [InlineData("Boolean", "not-a-bool")]
    public async Task Command_InvalidExplicitPromptValue_FailsWithoutReadingInteractiveInput(string dataType, string value)
    {
        WritePromptTemplate(dataType, "value[bad]", dataType == "choice" ? ["choice[bad]"] : null);
        await InstallAsync();
        using BufferedConsole stdout = new(true, true, noColor: true);
        using BufferedConsole stderr = new(true, true, noColor: true);
        var harness = CreateHarness(new SpectreInteractionService(new DefaultTheme(), stdout, stderr));
        var before = Snapshot();

        int exit = await harness.InvokeAsync(["Func.Consumer.Prompt", "--path", Target, "--value[bad]", value]);

        exit.Should().Be(1);
        harness.Result!.Kind.Should().Be("invalidArguments");
        harness.Result.ArgumentResults.Single().InvalidInputDiagnostics.Should().Contain(diagnostic =>
            diagnostic.Message.Contains(value, StringComparison.Ordinal) && diagnostic.TokenIndices.Count > 0);
        stderr.Reads.Should().Be(0);
        Snapshot().Should().BeEquivalentTo(before);
        Directory.Exists(Target).Should().BeFalse();
    }

    [Fact]
    public async Task Command_CancellationDuringRequiredPrompt_ReleasesStoreWithoutInvocationOrOutput()
    {
        await InstallAsync();
        using CancellationTokenSource cancellation = new();
        var interaction = Interactive();
        interaction.PromptForInputAsync(Arg.Any<string>(), null, Arg.Any<CancellationToken>()).Returns(_ =>
        {
            cancellation.Cancel();
            return Task.FromResult("must not create");
        });
        var harness = CreateHarness(interaction);
        var before = Snapshot();

        Func<Task> invoke = () => harness.InvokeAsync([Low, "--path", Target], cancellation.Token);

        await invoke.Should().ThrowAsync<OperationCanceledException>();
        harness.Result.Should().BeNull();
        Snapshot().Should().BeEquivalentTo(before);
        Directory.Exists(Target).Should().BeFalse();
        Func<Task> disposed = () => harness.Factory.Templaters.Single().ListAsync(TemplateType.Item);
        await disposed.Should().ThrowAsync<ObjectDisposedException>();
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Command_NativeCreationCommitsThenCancels_PreservesCompletedResultAndRendersOnce(bool noOutputs, bool json)
    {
        await InstallAsync();
        using CancellationTokenSource cancellation = new();
        var native = new CancelAfterNativeReturnFactory(Path.Combine(Home, "templates"), cancellation, noOutputs: noOutputs);
        var harness = CreateHarness(templateFactory: native);
        string reference = noOutputs ? "Func.Consumer.Empty" : Low;
        string[] values = noOutputs ? [] : ["--greeting", "completed"];
        int? exit = null;

        Exception? failure = await Record.ExceptionAsync(async () => exit = await harness.InvokeAsync(
            [reference, "--path", Target, "--name", "Committed", .. values, "--output", json ? "json" : "plain"], cancellation.Token));

        // Observe the real native commit before asserting anything about cancellation or host presentation.
        if (!noOutputs)
        {
            File.ReadAllText(Path.Combine(Target, "Committed.txt"))
                .Should().Be($"Committed|completed|anonymous|False|leave|{Target}|{Project}|JavaScript");
        }

        native.NativeResult.Should().NotBeNull();
        native.NativeResult!.Status.Should().Be(CreationResultStatus.Success);
        native.NativeResult.CreationEffects!.FileChanges.Should().HaveCount(noOutputs ? 0 : 1);
        if (!noOutputs)
        {
            Path.GetFullPath(Path.Combine(Target, native.NativeResult.CreationEffects.FileChanges.Single().TargetRelativePath))
                .Should().Be(Path.Combine(Target, "Committed.txt"));
        }

        cancellation.IsCancellationRequested.Should().BeTrue();
        failure.Should().BeNull("native creation already returned validated successful effects");
        exit.Should().Be(0);
        harness.Result!.Kind.Should().Be("created");
        harness.Result.Invocation!.Status.Should().Be(TemplateInvocationStatus.Success);
        harness.Result.Invocation.Changes.Should().HaveCount(noOutputs ? 0 : 1);
        if (json)
        {
            string record = _interaction.Lines.Should().ContainSingle(line => line.StartsWith("JSON: ", StringComparison.Ordinal)).Subject;
            using var document = JsonDocument.Parse(record["JSON: ".Length..]);
            document.RootElement.GetProperty("kind").GetString().Should().Be("created");
            document.RootElement.GetProperty("succeeded").GetBoolean().Should().BeTrue();
            document.RootElement.GetProperty("invocation").GetProperty("status").GetString().Should().Be("Success");
            document.RootElement.GetProperty("invocation").GetProperty("changes").GetArrayLength().Should().Be(noOutputs ? 0 : 1);
            document.RootElement.TryGetProperty("hasCompletedCreation", out _).Should().BeFalse();
            if (!noOutputs)
            {
                JsonElement change = document.RootElement.GetProperty("invocation").GetProperty("changes")[0];
                Path.GetFullPath(Path.Combine(Target, change.GetProperty("path").GetString()!)).Should().Be(Path.Combine(Target, "Committed.txt"));
                change.GetProperty("kind").GetString().Should().Be("Create");
            }

            _interaction.Lines.Should().NotContain(line => line.StartsWith("SUCCESS:", StringComparison.Ordinal));
        }
        else
        {
            _interaction.Lines.Should().ContainSingle(line => line.StartsWith("SUCCESS:", StringComparison.Ordinal));
            _interaction.Lines.Should().NotContain(line => line.StartsWith("JSON:", StringComparison.Ordinal));
        }

        Func<Task> disposed = () => harness.Factory.Templaters.Single().ListAsync(TemplateType.Item);
        await disposed.Should().ThrowAsync<ObjectDisposedException>();
    }

    [Fact]
    public async Task Command_CancelsAfterRunnerReturnsActualCreation_PreservesCompletedJsonAtCommandBoundary()
    {
        await InstallAsync();
        using CancellationTokenSource cancellation = new();
        var harness = CreateHarness(afterResult: _ => cancellation.Cancel());
        int? exit = null;

        Exception? failure = await Record.ExceptionAsync(async () => exit = await harness.InvokeAsync(
            [Low, "--path", Target, "--name", "RunnerCommitted", "--greeting", "runner", "--output", "json"], cancellation.Token));

        File.ReadAllText(Path.Combine(Target, "RunnerCommitted.txt")).Should().StartWith("RunnerCommitted|runner|");
        harness.Result!.Invocation!.Status.Should().Be(TemplateInvocationStatus.Success);
        harness.Result.Invocation.Changes.Should().ContainSingle();
        cancellation.IsCancellationRequested.Should().BeTrue();
        failure.Should().BeNull("the real protected runner already returned creation success before cancellation");
        exit.Should().Be(0);
        _interaction.Lines.Should().ContainSingle(line => line.StartsWith("JSON: ", StringComparison.Ordinal));
        AssertJsonKind("created");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Command_NativePreviewCompletesThenCancels_StillThrowsWithoutCreatingOutput(bool dryRun)
    {
        await InstallAsync();
        using CancellationTokenSource cancellation = new();
        var native = new CancelAfterNativeReturnFactory(Path.Combine(Home, "templates"), cancellation, cancelPreview: true);
        var harness = CreateHarness(templateFactory: native);
        string[] preview = dryRun ? ["--dry-run"] : [];
        var before = Snapshot();

        Exception? failure = await Record.ExceptionAsync(() => harness.InvokeAsync(
            [Low, "--path", Target, "--greeting", "preview", .. preview, "--output", "json"], cancellation.Token));

        Snapshot().Should().BeEquivalentTo(before);
        Directory.Exists(Target).Should().BeFalse();
        native.NativeResult!.Status.Should().Be(CreationResultStatus.Success);
        failure.Should().BeOfType<OperationCanceledException>();
        harness.Result.Should().BeNull();
        _interaction.Lines.Should().NotContain(line => line.StartsWith("JSON:", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(CreationResultStatus.CreateFailed)]
    [InlineData(CreationResultStatus.Cancelled)]
    public async Task Command_NativeUnsuccessfulCreateAndCancellation_StillThrowsWithoutOutput(CreationResultStatus status)
    {
        await InstallAsync();
        using CancellationTokenSource cancellation = new();
        var native = new CancelAfterNativeReturnFactory(Path.Combine(Home, "templates"), cancellation, unsuccessfulStatus: status);
        var harness = CreateHarness(templateFactory: native);
        var before = Snapshot();

        Exception? failure = await Record.ExceptionAsync(() => harness.InvokeAsync(
            [Low, "--path", Target, "--greeting", "failed", "--output", "json"], cancellation.Token));

        Snapshot().Should().BeEquivalentTo(before);
        Directory.Exists(Target).Should().BeFalse();
        native.NativeResult!.Status.Should().Be(status);
        failure.Should().BeOfType<OperationCanceledException>();
        harness.Result.Should().BeNull();
        _interaction.Lines.Should().NotContain(line => line.StartsWith("JSON:", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Command_NativeCreationThrowsCancellation_DoesNotConvertItToSuccess()
    {
        await InstallAsync();
        using CancellationTokenSource cancellation = new();
        var native = new CancelAfterNativeReturnFactory(Path.Combine(Home, "templates"), cancellation, throwDuringCreate: true);
        var harness = CreateHarness(templateFactory: native);
        var before = Snapshot();

        Exception? failure = await Record.ExceptionAsync(() => harness.InvokeAsync(
            [Low, "--path", Target, "--greeting", "cancelled", "--output", "json"], cancellation.Token));

        Snapshot().Should().BeEquivalentTo(before);
        Directory.Exists(Target).Should().BeFalse();
        native.NativeResult.Should().BeNull();
        failure.Should().BeOfType<OperationCanceledException>();
        harness.Result.Should().BeNull();
        _interaction.Lines.Should().NotContain(line => line.StartsWith("JSON:", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("preview")]
    [InlineData("conflict")]
    [InlineData("unknown")]
    [InlineData("required")]
    [InlineData("list")]
    public async Task Command_CancelsAfterUncommittedOutcome_StillThrowsWithoutRenderingSuccess(string mode)
    {
        await InstallAsync();
        if (mode == "conflict")
        {
            Directory.CreateDirectory(Target);
            File.WriteAllText(Path.Combine(Target, "Existing.txt"), "user content");
        }

        using CancellationTokenSource cancellation = new();
        var harness = CreateHarness(afterResult: _ => cancellation.Cancel());
        string[] args = mode switch
        {
            "preview" => [Low, "--path", Target, "--greeting", "preview", "--dry-run"],
            "conflict" => [Low, "--path", Target, "--name", "Existing", "--greeting", "conflict"],
            "unknown" => ["missing", "--path", Target],
            "required" => [Low, "--path", Target],
            "list" => ["--path", Target, "--list"],
            _ => throw new InvalidOperationException("Unknown test mode."),
        };
        var before = Snapshot();

        Exception? failure = await Record.ExceptionAsync(() => harness.InvokeAsync([.. args, "--output", "json"], cancellation.Token));

        Snapshot().Should().BeEquivalentTo(before);
        failure.Should().BeOfType<OperationCanceledException>();
        harness.Result.Should().NotBeNull();
        if (mode is "preview" or "conflict")
        {
            harness.Result!.Invocation!.Changes.Should().NotBeEmpty();
        }

        _interaction.Lines.Should().NotContain(line => line.StartsWith("JSON:", StringComparison.Ordinal)
            || line.StartsWith("SUCCESS:", StringComparison.Ordinal));
    }

    public void Dispose()
    {
        foreach (ServiceProvider provider in _providers)
        {
            provider.Dispose();
        }

        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private Harness CreateHarness(IInteractionService? interaction = null, string? bundleVersion = "4.35.0",
        ITemplaterFactory? templateFactory = null, Action<NewExecutionResult>? afterResult = null)
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
        new NodeWorkload().Configure(new DefaultFunctionsCliBuilder(services, TestWorkloads.CreateInfo("Controlled.Node")));
        services.AddSingleton<IFunctionsProjectResolver, FunctionsProjectResolver>();
        services.AddSingleton<IHostJsonBundleSectionReader, HostJsonBundleSectionReader>();
        var installed = Substitute.For<IInstalledBundleWorkloads>();
        installed.ListInstalledAsync(Arg.Any<CancellationToken>()).Returns(bundleVersion is null ? [] :
            new InstalledBundleWorkload[] { new(bundleVersion, Path.Combine(_root, "bundle")) });
        services.AddSingleton<IExtensionBundleResolver>(new ExtensionBundleResolver(
            new InstalledBundleScanner(installed), NullBundleResolveTelemetry.Instance, NullLogger<ExtensionBundleResolver>.Instance));
        services.AddTemplatesOrchestrator();
        var factory = new RecordingFactory(templateFactory ?? new TemplaterFactory(new TemplateStore(
            new CliConfigurationPathsOptions(Home), new PhysicalFileSystem(), new FileTemplateStoreLock(), NullLoggerFactory.Instance),
            new PhysicalFileSystem()));
        services.AddSingleton<ITemplaterFactory>(factory);
        ServiceProvider provider = services.BuildServiceProvider();
        _providers.Add(provider);
        var runner = new RecordingRunner(provider.GetRequiredService<INewExecutionRunner>(), afterResult);
        NewExecutionCommand command = new(runner, provider.GetRequiredService<NewExecutionRenderer>(), new([
            new("Controlled.Node", NuGetVersion.Parse("1.0.0"), ["node"], true),
        ]));
        FuncRootCommand root = [command];
        return new(root, runner, factory);
    }

    private static IInteractionService Interactive()
    {
        var interaction = Substitute.For<IInteractionService>();
        interaction.IsInteractive.Returns(true);
        interaction.PromptForSelectionAsync(Arg.Any<string>(), Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>())
            .Returns(call => call.ArgAt<IEnumerable<string>>(1).First());
        return interaction;
    }

    private static IInteractionService LiteralChoiceInteraction(
        BufferedConsole stdout, BufferedConsole stderr, bool? styledFirst = null, List<string[]>? offered = null)
    {
        var spectre = new SpectreInteractionService(new DefaultTheme(), stdout, stderr);
        var interaction = Substitute.For<IInteractionService>();
        interaction.IsInteractive.Returns(spectre.IsInteractive);
        interaction.PromptForSelectionAsync(Arg.Any<string>(), Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                string[] choices = [.. call.ArgAt<IEnumerable<string>>(1)];
                if (styledFirst is not null)
                {
                    Array.Sort(choices, StringComparer.Ordinal);
                    if (styledFirst.Value)
                    {
                        Array.Reverse(choices);
                    }
                }

                offered?.Add(choices);
                choices.Should().ContainSingle(choice => choice == "Func.Consumer.[red]styled[/]");
                int index = Array.IndexOf(choices, "Func.Consumer.[red]styled[/]");
                for (int i = 0; i < index; i++)
                {
                    stderr.Enqueue(ConsoleKey.DownArrow);
                }

                stderr.Enqueue(ConsoleKey.Enter);
                return spectre.PromptForSelectionAsync(call.ArgAt<string>(0), choices, call.ArgAt<CancellationToken>(2));
            });
        interaction.PromptForInputAsync(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                stderr.Enqueue(ConsoleKey.X, 'x');
                stderr.Enqueue(ConsoleKey.Enter);
                return spectre.PromptForInputAsync(call.ArgAt<string>(0), call.ArgAt<string?>(1), call.ArgAt<CancellationToken>(2));
            });
        interaction.When(service => service.WriteSectionHeader(Arg.Any<string>()))
            .Do(call => spectre.WriteSectionHeader(call.Arg<string>()));
        interaction.When(service => service.WriteLine(Arg.Any<Action<InlineLine>>()))
            .Do(call => spectre.WriteLine(call.Arg<Action<InlineLine>>()));
        return interaction;
    }

    private async Task InstallAsync()
    {
        Directory.CreateDirectory(Project);
        Directory.CreateDirectory(Path.Combine(Project, ".func"));
        File.WriteAllText(Path.Combine(Project, "package.json"), """{"dependencies":{"@azure/functions":"4.0.0"}}""");
        File.WriteAllText(Path.Combine(Project, ".func", "config.json"), """{"stack":{"runtime":"node","language":"javascript"}}""");
        WriteHost("[4.0.0,5.0.0)");
        WriteTemplate("low", """
            {
              "identity":"Func.Consumer.Low","groupIdentity":"Func.Consumer.Group","name":"Low item",
              "shortName":["native-item","collision","install","update","uninstall"],"precedence":10,
              "tags":{"type":"item","language":"JavaScript"},"sourceName":"ItemFunction",
              "constraints":{"workload":{"type":"func-workload","args":"node"}},
              "symbols":{
                "Greeting":{"type":"parameter","datatype":"string","isRequired":true,"replaces":"GREETING"},
                "Auth":{"type":"parameter","datatype":"choice","isRequired":true,"defaultValue":"anonymous",
                  "choices":[{"choice":"anonymous"},{"choice":"function"}],"replaces":"AUTH"},
                "Feature":{"type":"parameter","datatype":"bool","defaultValue":"false","replaces":"FEATURE"},
                "Optional":{"type":"parameter","datatype":"string","defaultValue":"leave","replaces":"OPTIONAL"},
                "Execution":{"type":"bind","binding":"host:WorkingDirectory","replaces":"EXECUTION"},
                "Root":{"type":"bind","binding":"host:func:project-root","replaces":"ROOT"},
                "Language":{"type":"bind","binding":"host:func:language","replaces":"LANGUAGE"}
              }
            }
            """, "ItemFunction.txt", "ItemFunction|GREETING|AUTH|FEATURE|OPTIONAL|EXECUTION|ROOT|LANGUAGE",
            """{"symbolInfo":{"Greeting":{"longName":"greeting"},"Auth":{"longName":"output","shortName":"n"},"Feature":{"longName":"feature"}}}""");
        foreach (string identity in new[] { "High", "High2" })
        {
            WriteTemplate(identity, $$$"""
                {"identity":"Func.Consumer.{{{identity}}}","groupIdentity":"Func.Consumer.Group","name":"{{{identity}}}",
                 "shortName":"native-item","precedence":100,"tags":{"type":"item","language":"JavaScript"},
                 "symbols":{"Other":{"type":"parameter","datatype":"string","defaultValue":"other"}}
                }
                """, identity + ".txt", "high");
        }

        WriteTemplate("ts", """
            {"identity":"Func.Consumer.TypeScript","groupIdentity":"Func.Consumer.Group","name":"TypeScript item",
             "shortName":"native-item","tags":{"type":"item","language":"TypeScript"},"sourceName":"ItemFunction",
             "symbols":{"Greeting":{"type":"parameter","datatype":"string","isRequired":true,"replaces":"GREETING"},
               "Language":{"type":"bind","binding":"host:func:language","replaces":"LANGUAGE"}}}
            """, "ItemFunction.txt", "GREETING|LANGUAGE", """{"symbolInfo":{"Greeting":{"longName":"greeting"}}}""");
        WriteTemplate("other", """
            {"identity":"Func.Consumer.Other","groupIdentity":"Func.Consumer.OtherGroup","name":"Other family",
             "shortName":"collision","tags":{"type":"item","language":"JavaScript"},
             "symbols":{"Greeting":{"type":"parameter","datatype":"string"}}}
            """, "other.txt", "other", """{"symbolInfo":{"Greeting":{"longName":"greeting"}}}""");
        WriteTemplate("project", """
            {"identity":"Func.Consumer.Project","name":"Project control","shortName":"native-item","tags":{"type":"project"}}
            """, "host.json", """{"version":"2.0"}""");
        WriteTemplate("restricted", """
            {"identity":"Func.Consumer.Restricted","name":"Restricted","shortName":"native-item","precedence":10000,
             "tags":{"type":"item","language":"JavaScript"},"constraints":{"workload":{"type":"func-workload","args":"java"}}}
            """, "restricted.txt", "must not create");
        WriteTemplate("hidden", """
            {"identity":"Func.Consumer.Hidden","name":"Hidden","shortName":"hidden","tags":{"type":"item","language":"JavaScript"}}
            """, "hidden.txt", "hidden", """{"isHidden":true}""");
        WriteTemplate("untyped", """{"identity":"Func.Consumer.Untyped","name":"Untyped","shortName":"untyped"}""", "untyped.txt", "untyped");
        WriteTemplate("number", """
            {"identity":"Func.Consumer.Number","name":"Number","shortName":"number","tags":{"type":"item","language":"JavaScript"},
             "symbols":{"Count":{"type":"parameter","datatype":"integer","isRequired":true}}}
            """, "number.txt", "number");
        WriteTemplate("required", """
            {"identity":"Func.Consumer.Required","name":"Required values","shortName":"required","tags":{"type":"item","language":"JavaScript"},
             "symbols":{"Count":{"type":"parameter","datatype":"integer","isRequired":true},
               "Mode":{"type":"parameter","datatype":"choice","isRequired":true,"choices":[{"choice":"first"},{"choice":"second"}]},
               "Enabled":{"type":"parameter","datatype":"bool","isRequired":true}}}
            """, "required.txt", "required");
        WriteTemplate("hidden-required", """
            {"identity":"Func.Consumer.HiddenRequired","name":"Hidden required","shortName":"hidden-required",
             "tags":{"type":"item","language":"JavaScript"},"symbols":{"Secret":{"type":"parameter","datatype":"string","isRequired":true}}}
            """, "hidden-required.txt", "hidden", """{"symbolInfo":{"Secret":{"isHidden":true}}}""");
        WriteTemplate("empty", """
            {"identity":"Func.Consumer.Empty","name":"Empty","shortName":"empty","tags":{"type":"item","language":"JavaScript"}}
            """);
        await TemplateFixtures.InstallAsync(Path.Combine(_root, "package"), Path.Combine(Home, "templates"), CancellationToken.None);
    }

    private void WriteHost(string range) => File.WriteAllText(Path.Combine(Project, "host.json"), JsonSerializer.Serialize(new
    {
        version = "2.0",
        extensionBundle = new { id = BundleHelpers.StableBundleId, version = range },
    }));

    private void WritePromptTemplate(string dataType, string alias = "Value", string[]? choices = null,
        string identity = "Func.Consumer.Prompt", string? group = null, string folder = "prompt")
    {
        Dictionary<string, object> symbol = new() { ["type"] = "parameter", ["datatype"] = dataType, ["isRequired"] = true };
        if (choices is not null)
        {
            symbol["choices"] = choices.Select(choice => new { choice });
        }

        WriteTemplate(folder, JsonSerializer.Serialize(new
        {
            identity,
            groupIdentity = group,
            name = "Prompt",
            shortName = "prompt",
            tags = new { type = "item", language = "JavaScript" },
            symbols = new Dictionary<string, object> { ["Value"] = symbol },
        }), "prompt.txt", "prompt", JsonSerializer.Serialize(new
        {
            symbolInfo = new { Value = new { longName = alias } },
        }));
    }

    private void WriteTemplate(string folder, string configuration, string? file = null, string? content = null, string? host = null)
    {
        string directory = Path.Combine(_root, "package", folder);
        Directory.CreateDirectory(Path.Combine(directory, ".template.config"));
        File.WriteAllText(Path.Combine(directory, ".template.config", "template.json"), configuration);
        if (host is not null)
        {
            File.WriteAllText(Path.Combine(directory, ".template.config", "func.host.json"), host);
        }

        if (file is not null)
        {
            File.WriteAllText(Path.Combine(directory, file), content);
        }
    }

    private Dictionary<string, string> Snapshot() => Directory.GetFiles(Project, "*", SearchOption.AllDirectories)
        .ToDictionary(path => Path.GetRelativePath(Project, path), File.ReadAllText);

    private static Dictionary<string, byte[]> StoreBytes(string directory)
        => Directory.Exists(directory)
            ? Directory.GetFiles(directory, "*", SearchOption.AllDirectories)
                .ToDictionary(path => Path.GetRelativePath(directory, path), File.ReadAllBytes)
            : [];

    private static string[] ReadOnlyArguments(string mode) => mode switch
    {
        "list" => ["--list"],
        "preview" => [Low, "--greeting", "preview", "--dry-run"],
        "forcePreview" => [Low, "--greeting", "preview", "--dry-run", "--force"],
        _ => throw new ArgumentOutOfRangeException(nameof(mode)),
    };

    private void AssertJsonKind(string expected)
    {
        string line = _interaction.Lines.Last(line => line.StartsWith("JSON: ", StringComparison.Ordinal));
        using var json = JsonDocument.Parse(line["JSON: ".Length..]);
        json.RootElement.GetProperty("kind").GetString().Should().Be(expected);
    }

    private sealed class Harness(RootCommand root, RecordingRunner runner, RecordingFactory factory)
    {
        public NewExecutionResult? Result => runner.Result;
        public RecordingFactory Factory => factory;

        public Task<int> InvokeAsync(string[] args, CancellationToken cancellationToken = default,
            IReadOnlyList<Func<Command>>? lifecycleFactories = null)
            => root.Subcommands.OfType<NewExecutionCommand>().Single().ParseInvocation(args, lifecycleFactories).InvokeAsync(new InvocationConfiguration
            {
                EnableDefaultExceptionHandler = false,
            }, cancellationToken);
    }

    private sealed class RecordingRunner(INewExecutionRunner inner, Action<NewExecutionResult>? afterResult = null) : INewExecutionRunner
    {
        public NewExecutionResult? Result { get; private set; }

        public async Task<NewExecutionResult> ExecuteAsync(
            NewExecutionRequest request, TemplateConstraintEvaluationSnapshot availability, CancellationToken cancellationToken)
        {
            Result = await inner.ExecuteAsync(request, availability, cancellationToken);
            afterResult?.Invoke(Result);
            return Result;
        }
    }

    private sealed class CancelAfterNativeReturnFactory(string hive, CancellationTokenSource cancellation,
        bool cancelPreview = false, CreationResultStatus? unsuccessfulStatus = null, bool throwDuringCreate = false,
        bool noOutputs = false) : ITemplaterFactory
    {
        private CancelAfterNativeReturnSession? _session;

        public ITemplateCreationResult? NativeResult => _session?.NativeResult;

        public Task<Templater> CreateAsync(TemplateEngineContext context, CancellationToken cancellationToken,
            Action<TemplateStoreWaitReason>? waiting = null) => CreateAsync(context, new([]), cancellationToken, waiting);

        public Task<Templater> CreateAsync(TemplateEngineContext context, TemplateConstraintEvaluationSnapshot snapshot,
            CancellationToken cancellationToken, Action<TemplateStoreWaitReason>? waiting = null)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _session = new(context, hive, snapshot, cancellation, cancelPreview, unsuccessfulStatus, throwDuringCreate);
            var files = new RecordingCommitFileSystem(new PhysicalFileSystem())
            {
                AfterCreateDirectory = path =>
                {
                    if (noOutputs && path == context.CommandDirectory.Info.FullName)
                    {
                        cancellation.Cancel();
                    }
                },
                AfterMove = _ =>
                {
                    if (!noOutputs)
                    {
                        cancellation.Cancel();
                    }
                },
            };
            return Task.FromResult(TemplateFixtures.CreateTemplater(_session, files));
        }

        public Task<Templater> CreateProjectAsync(TemplateEngineContext context, TemplateConstraintEvaluationSnapshot snapshot,
            TemplateProjectSupport projectSupport, CancellationToken cancellationToken, Action<TemplateStoreWaitReason>? waiting = null)
            => throw new InvalidOperationException("Item command must not create a project-template environment.");
    }

    private sealed class CancelAfterNativeReturnSession(TemplateEngineContext context, string hive,
        TemplateConstraintEvaluationSnapshot snapshot, CancellationTokenSource cancellation, bool cancelPreview,
        CreationResultStatus? unsuccessfulStatus, bool throwDuringCreate) : TemplateEngineSession(context, hive, snapshot)
    {
        public ITemplateCreationResult? NativeResult { get; private set; }

        protected override async Task<ITemplateCreationResult> InstantiateAsync(ITemplateInfo template, TemplateInvocationRequest request,
            InputDataSet parameters, bool dryRun, CancellationToken cancellationToken)
        {
            if (!dryRun && throwDuringCreate)
            {
                await cancellation.CancelAsync();
                cancellationToken.ThrowIfCancellationRequested();
            }

            ITemplateCreationResult result;
            if (!dryRun && unsuccessfulStatus is { } status)
            {
                result = Substitute.For<ITemplateCreationResult>();
                result.Status.Returns(status);
            }
            else
            {
                result = await base.InstantiateAsync(template, request, parameters, dryRun, cancellationToken);
            }

            NativeResult = result;
            if (cancelPreview || unsuccessfulStatus is not null)
            {
                await cancellation.CancelAsync();
            }

            return result;
        }
    }

    private sealed class RecordingFactory(ITemplaterFactory inner) : ITemplaterFactory
    {
        public List<TemplateEngineContext> Contexts { get; } = [];
        public List<Templater> Templaters { get; } = [];

        public Task<Templater> CreateAsync(TemplateEngineContext context, CancellationToken cancellationToken, Action<TemplateStoreWaitReason>? waiting = null)
            => inner.CreateAsync(context, cancellationToken, waiting);

        public async Task<Templater> CreateAsync(TemplateEngineContext context, TemplateConstraintEvaluationSnapshot snapshot,
            CancellationToken cancellationToken, Action<TemplateStoreWaitReason>? waiting = null)
        {
            Contexts.Add(context);
            var templater = await inner.CreateAsync(context, snapshot, cancellationToken, waiting);
            Templaters.Add(templater);
            return templater;
        }

        public Task<Templater> CreateProjectAsync(TemplateEngineContext context, TemplateConstraintEvaluationSnapshot snapshot,
            TemplateProjectSupport projectSupport, CancellationToken cancellationToken, Action<TemplateStoreWaitReason>? waiting = null)
            => throw new InvalidOperationException("Item command must not create a project-template environment.");
    }
}
