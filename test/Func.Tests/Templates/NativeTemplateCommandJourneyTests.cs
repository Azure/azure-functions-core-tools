// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.CommandLine;
using System.IO.Compression;
using System.Runtime.Loader;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Azure.Functions.Cli.Bundles;
using Azure.Functions.Cli.Commands;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Configuration;
using Azure.Functions.Cli.Hosting;
using Azure.Functions.Cli.Profiles;
using Azure.Functions.Cli.Projects;
using Azure.Functions.Cli.Templates;
using Azure.Functions.Cli.Templates.Acquisition;
using Azure.Functions.Cli.Templates.Engine;
using Azure.Functions.Cli.Templates.Native;
using Azure.Functions.Cli.Tests.Templates.Engine;
using Azure.Functions.Cli.Workloads.Node;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.TemplateEngine.Abstractions;
using Microsoft.TemplateEngine.Edge.Template;
using NSubstitute;
using NuGet.Packaging;
using NuGet.Versioning;

namespace Azure.Functions.Cli.Tests.Templates;

public sealed class NativeTemplateCommandJourneyTests : IDisposable
{
    private const string LegacySource = """{"trigger-functionName":{"validator":"^[a-zA-Z][a-zA-Z0-9_]{0,126}[a-zA-Z0-9]$"}}""";
    private readonly string _root = Path.Combine(Path.GetTempPath(), "func-native-adapter-" + Guid.NewGuid().ToString("N"));
    private readonly TestInteractionService _interaction = new();
    private readonly List<ServiceProvider> _providers = [];
    private BundledNodeTemplateTests? _ownedNode;
    private TemplateConstraintEvaluationSnapshot? _ownedNodeAvailability;

    private string Home => Path.Combine(_root, "home");
    private string Project => Path.Combine(_root, "project");
    private string Target => Path.Combine(Project, "nested");
    private string LegacySourcePath => Path.Combine(_root, "package", "legacy-source.json");

    [Fact]
    public async Task Fixture_CurrentArtifactsAndQualifiedOwner_PreserveHistoricalPackagesAndSplitInventories()
    {
        var artifacts = BundledNodeTemplateTests.CurrentNodeJourneyArtifacts;
        Path.GetFileName(artifacts.Workload).Should().Be($"Azure.Functions.Cli.Workloads.Node.{artifacts.Version}.nupkg");
        Path.GetFileName(artifacts.Functions).Should().Be($"Azure.Functions.Templates.Node.Functions.{artifacts.Version}.nupkg");
        Dictionary<string, string> archives = NodeArtifactHashes();
        using (var workload = new PackageArchiveReader(artifacts.Workload))
        {
            workload.NuspecReader.GetId().Should().Be("Azure.Functions.Cli.Workloads.Node");
            workload.NuspecReader.GetVersion().Should().Be(NuGetVersion.Parse(artifacts.Version));
            using var declaration = JsonDocument.Parse(workload.GetStream("bundled-templates.json"));
            JsonElement asset = declaration.RootElement.GetProperty("assets").EnumerateArray().Should().ContainSingle().Subject;
            asset.GetProperty("packageId").GetString().Should().Be("Azure.Functions.Templates.Node.Basic");
            asset.GetProperty("version").GetString().Should().Be(artifacts.Version);
            asset.GetProperty("templates").GetArrayLength().Should().Be(2);
            using var input = workload.GetStream(asset.GetProperty("path").GetString()!);
            using var bytes = new MemoryStream();
            input.CopyTo(bytes);
            Convert.ToHexStringLower(SHA256.HashData(bytes.ToArray())).Should().Be(asset.GetProperty("sha256").GetString());
            bytes.Position = 0;
            using var basic = new PackageArchiveReader(bytes);
            basic.GetFiles().Should().NotContain(path => path.StartsWith("content/http-", StringComparison.Ordinal)
                || path.StartsWith("content/items/", StringComparison.Ordinal));
        }
        using (var functions = new PackageArchiveReader(artifacts.Functions))
        {
            functions.NuspecReader.GetId().Should().Be("Azure.Functions.Templates.Node.Functions");
            functions.NuspecReader.GetVersion().Should().Be(NuGetVersion.Parse(artifacts.Version));
            functions.GetFiles().Where(path => path.EndsWith("/.template.config/template.json", StringComparison.Ordinal))
                .Should().HaveCount(33).And.Contain("content/http-js/.template.config/template.json", "content/http-ts/.template.config/template.json");
            functions.GetFiles().Should().NotContain(path => path.StartsWith("content/project-", StringComparison.Ordinal));
        }

        await InstallBasicNodeAsync(owned: true);
        using var harness = await CreateOwnedHarnessAsync();
        var registration = harness.Services.GetServices<WorkloadNativeTemplatePolicyRegistration>()
            .Single(value => value.Policy.Stack == "node");
        var initializer = harness.Services.GetServices<IProjectInitializer>().Single(value => value.Stack == "node");
        initializer.GetType().Assembly.Should().BeSameAs(registration.Policy.GetType().Assembly);
        AssemblyLoadContext.GetLoadContext(initializer.GetType().Assembly).Should().BeSameAs(registration.Owner.LoadContext);
        harness.Services.GetRequiredService<IProjectInitializationReporter>().Should().BeOfType<ProjectInitializationReporter>();
        (await harness.Services.GetRequiredService<IBasicCompanionCatalog>().LoadAsync(CancellationToken.None))
            .Revision.Should().Be("explicit-local-node-function-fixture");
        var support = harness.Services.GetRequiredService<TemplateProjectSupport>();
        support.Planner.Should().BeSameAs(harness.Services.GetRequiredService<TemplateProjectConfigurationPlanner>());
        support.Writer.Should().BeSameAs(harness.Services.GetRequiredService<TemplateProjectConfigurationWriter>());
        harness.Services.GetRequiredService<Func<TemplateProjectSupport>>()().Should().BeSameAs(support);
        await CreateBasicProjectAsync(harness);
        harness.InitResult!.Preparation!.Preview.OwnedPlan!.Skipped.Should().BeTrue();
        harness.InitResult.Preparation.Completion!.OwnedCompletion!.TargetMayHaveChanged.Should().BeFalse();
        NodeArtifactHashes().OrderBy(pair => pair.Key, StringComparer.Ordinal).Should()
            .Equal(archives.OrderBy(pair => pair.Key, StringComparer.Ordinal));
    }

    [Fact]
    public async Task PublicDefinitions_InstalledNativePreviewCreateAndItem_PreserveTargetContextAndConfiguration()
    {
        await InstallAsync();
        using var harness = CreateHarness();
        string[] projectTokens = [Project, "-s", "node", "-l", "js", "-n", "Named", "--no-bundles"];

        int preview = await InvokeAsync(harness.Adapter.ParseInitInvocation(harness.CreateInit(), projectTokens,
            new("controlled-project", NonInteractive: true, DryRun: true, JsonOutput: true)));

        preview.Should().Be(0);
        AssertJsonKind("previewed");
        Directory.Exists(Project).Should().BeFalse();

        int created = await InvokeAsync(harness.Adapter.ParseInitInvocation(harness.CreateInit(), projectTokens,
            new("controlled-project", NonInteractive: true, JsonOutput: true)));

        created.Should().Be(0);
        AssertJsonKind("created");
        File.ReadAllText(Path.Combine(Project, "project.txt")).Should().Be("Named|True");
        string configuration = File.ReadAllText(Path.Combine(Project, ".func", "config.json"));
        using (var json = JsonDocument.Parse(configuration))
        {
            json.RootElement.GetProperty("stack").GetProperty("runtime").GetString().Should().Be("node");
            json.RootElement.GetProperty("stack").GetProperty("language").GetString().Should().Be("JavaScript");
        }

        string[] itemTokens = [Target, "-t", "controlled-item", "-n", "Item", "--greeting=original", "--param:Auth", "anonymous",
            "--non-interactive", "--output", "json"];
        int itemPreview = await InvokeAsync(harness.Adapter.ParseNewInvocation(harness.CreateNew(), itemTokens, dryRun: true));

        itemPreview.Should().Be(0);
        AssertJsonKind("previewed");
        Directory.Exists(Target).Should().BeFalse();
        using (var json = LastJson())
        {
            json.RootElement.GetProperty("projectRoot").GetString().Should().Be(Project);
            json.RootElement.GetProperty("executionDirectory").GetString().Should().Be(Target);
        }

        int item = await InvokeAsync(harness.Adapter.ParseNewInvocation(harness.CreateNew(), itemTokens));

        item.Should().Be(0);
        AssertJsonKind("created");
        File.ReadAllText(Path.Combine(Target, "Item.txt")).Should().Be("Item|original|anonymous");
        File.ReadAllText(Path.Combine(Project, ".func", "config.json")).Should().Be(configuration);
        harness.LegacyNew.ReceivedCalls().Should().BeEmpty();
    }

    [Theory]
    [InlineData("--greeting=one", "--greeting=two")]
    [InlineData("--unknown=explicit")]
    [InlineData("--greeting")]
    [InlineData("--greeting=one", "@literal-file")]
    [InlineData("--greeting=one", "--", "tail")]
    public async Task PublicNew_InvalidRawArguments_ReachStrictNativeParserWithoutWrites(params string[] tail)
    {
        await InstallAsync();
        using var harness = CreateHarness();
        await CreateProjectAsync(harness);
        string before = File.ReadAllText(Path.Combine(Project, ".func", "config.json"));

        int exit = await InvokeAsync(harness.Adapter.ParseNewInvocation(harness.CreateNew(),
            [Target, "-t", "controlled-item", "--output=json", "--non-interactive", .. tail]));

        exit.Should().Be(1);
        AssertJsonKind("invalidArguments");
        using var json = LastJson();
        json.RootElement.GetProperty("argumentResults")[0].GetProperty("invalidInputDiagnostics")
            .EnumerateArray().Should().Contain(diagnostic => diagnostic.GetProperty("tokenIndices").GetArrayLength() > 0);
        Directory.Exists(Target).Should().BeFalse();
        File.ReadAllText(Path.Combine(Project, ".func", "config.json")).Should().Be(before);
        _interaction.AllOutput.Should().NotContain("INPUT:").And.NotContain("SELECT:");
    }

    [Fact]
    public async Task PublicInit_UnqualifiedPreparationFlag_FailsRatherThanPretendingTheRequestedEffectRan()
    {
        await InstallAsync();
        using var harness = CreateHarness();

        int exit = await InvokeAsync(harness.Adapter.ParseInitInvocation(harness.CreateInit(),
            [Project, "--stack=node", "--language=js", "--skip-npm-install"],
            new("controlled-project", NonInteractive: true, JsonOutput: true)));

        exit.Should().Be(1);
        AssertJsonKind("invalidArguments");
        Directory.Exists(Project).Should().BeFalse();
        _interaction.AllOutput.Should().Contain("--skip-npm-install").And.NotContain("SUCCESS:");
    }

    [Fact]
    public async Task PublicInit_AdoptionAndForceRefusal_PreserveUserFilesAndNeverRegenerate()
    {
        await InstallAsync();
        Directory.CreateDirectory(Project);
        File.WriteAllText(Path.Combine(Project, "host.json"), """{"version":"2.0"}""");
        File.WriteAllText(Path.Combine(Project, "package.json"), """{"dependencies":{"@azure/functions":"4.0.0"}}""");
        File.WriteAllText(Path.Combine(Project, "local.settings.json"), """{"Values":{"FUNCTIONS_WORKER_RUNTIME":"node"}}""");
        File.WriteAllText(Path.Combine(Project, "user.js"), "user-owned");
        using var harness = CreateHarness();

        int adopted = await InvokeAsync(harness.Adapter.ParseInitInvocation(harness.CreateInit(), [Project, "-l", "js"],
            new(NonInteractive: true, JsonOutput: true)));

        adopted.Should().Be(0);
        AssertJsonKind("adopted");
        File.Exists(Path.Combine(Project, "project.txt")).Should().BeFalse();
        string configuration = File.ReadAllText(Path.Combine(Project, ".func", "config.json"));

        int refused = await InvokeAsync(harness.Adapter.ParseInitInvocation(harness.CreateInit(), [Project, "--force"],
            new(NonInteractive: true, JsonOutput: true)));

        refused.Should().Be(1);
        AssertJsonKind("forceNotQualified");
        File.ReadAllText(Path.Combine(Project, "user.js")).Should().Be("user-owned");
        File.ReadAllText(Path.Combine(Project, ".func", "config.json")).Should().Be(configuration);
    }

    [Fact]
    public async Task PublicNew_ActualBundleUnavailable_FailsBeforeItemGeneration()
    {
        await InstallAsync();
        using var harness = CreateHarness();
        await CreateProjectAsync(harness);
        File.WriteAllText(Path.Combine(Project, "host.json"),
            """{"version":"2.0","extensionBundle":{"id":"Microsoft.Azure.Functions.ExtensionBundle","version":"[4.0.0,5.0.0)"}}""");

        int exit = await InvokeAsync(harness.Adapter.ParseNewInvocation(harness.CreateNew(),
            [Target, "-t", "controlled-item", "--greeting", "original", "--output", "json"]));

        exit.Should().Be(1);
        AssertJsonKind("bundleUnavailable");
        Directory.Exists(Target).Should().BeFalse();
    }

    [Theory]
    [InlineData(null, "AuthoredFunction")]
    [InlineData("ExplicitOverride", "ExplicitOverride")]
    public async Task Compatibility_ActualNativeItem_UsesAuthoredDefaultOrExplicitNameWithoutChangingProjectDefaults(
        string? explicitName, string expected)
    {
        await InstallAsync();
        using var harness = CreateHarness(itemCompatibility: true);
        await CreateProjectAsync(harness);
        string[] name = explicitName is null ? [] : ["--name", explicitName];

        int exit = await InvokeAsync(harness.Adapter.ParseNewInvocation(harness.CreateNew(),
            [Target, "-t", "controlled-item", "--greeting", "original", "--non-interactive", "--output", "json", .. name]));

        exit.Should().Be(0);
        File.ReadAllText(Path.Combine(Target, expected + ".txt")).Should().StartWith(expected + "|original|");
        File.Exists(Path.Combine(Target, "nested.txt")).Should().BeFalse();
        File.ReadAllText(Path.Combine(Project, "project.txt")).Should().StartWith("project|");
    }

    [Fact]
    public async Task Compatibility_OriginalNameRegex_RejectsUnsanitizedExplicitInputBeforeNativeMutation()
    {
        await InstallAsync();
        using var harness = CreateHarness(itemCompatibility: true);
        await CreateProjectAsync(harness);

        int exit = await InvokeAsync(harness.Adapter.ParseNewInvocation(harness.CreateNew(),
            [Target, "-t", "controlled-item", "--name", "Original-Invalid", "--greeting", "original", "--output", "json"]));

        exit.Should().Be(1);
        AssertJsonKind("invalidLegacyInput");
        _interaction.AllOutput.Should().Contain("Original-Invalid").And.Contain("legacy-source.json");
        Directory.Exists(Target).Should().BeFalse();
    }

    [Fact]
    public async Task NativeRunner_WithoutCompatibility_KeepsExistingDirectoryNameFallback()
    {
        await InstallAsync();
        using var harness = CreateHarness();
        await CreateProjectAsync(harness);

        int exit = await InvokeAsync(harness.Adapter.ParseNewInvocation(harness.CreateNew(),
            [Target, "-t", "controlled-item", "--greeting", "original", "--non-interactive", "--output", "json"]));

        exit.Should().Be(0);
        File.ReadAllText(Path.Combine(Target, "nested.txt")).Should().StartWith("nested|original|");
        File.Exists(Path.Combine(Target, "AuthoredFunction.txt")).Should().BeFalse();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ProjectPreparation_ActualNativePreviewAndCreate_OnlyCompletesAfterFilesAndConfiguration(bool dryRun)
    {
        await InstallAsync();
        var preparation = Substitute.For<INativeProjectPreparation>();
        preparation.PreviewAsync(Arg.Any<NativeProjectPreparationContext>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var context = call.Arg<NativeProjectPreparationContext>();
                context.SelectedTemplate.Entry.Identity.Should().Be("Controlled.Project");
                context.SelectedTemplate.ConfigurationSha256.Should().NotBeNullOrWhiteSpace();
                context.Parameters.Should().Contain("NoBundles", "false");
                context.NativePreview.ProjectConfigurations.Should().ContainSingle().Which.Stack.Should().Be("node");
                Directory.Exists(Project).Should().BeFalse();
                return new NativeProjectPreparationPreview(true, [new(NativeProjectPreparationOperation.NodeNpmInstall, false)], []);
            });
        preparation.CompleteAsync(Arg.Any<NativeProjectPreparationContext>(), Arg.Any<NativeProjectPreparationPreview>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                File.Exists(Path.Combine(Project, "host.json")).Should().BeTrue();
                File.Exists(Path.Combine(Project, ".func", "config.json")).Should().BeTrue();
                return new NativeProjectPreparationResult(true, []);
            });
        using var harness = CreateHarness(preparation: preparation);

        int exit = await InvokeAsync(harness.Adapter.ParseInitInvocation(harness.CreateInit(), [Project, "-s", "node", "-l", "js"],
            new("controlled-project", NonInteractive: true, DryRun: dryRun, JsonOutput: true)));

        exit.Should().Be(0);
        AssertJsonKind(dryRun ? "previewed" : "created");
        _interaction.AllOutput.Should().Contain("\"operation\":\"NodeNpmInstall\"");
        if (dryRun)
        {
            Directory.Exists(Project).Should().BeFalse();
            await preparation.DidNotReceive().CompleteAsync(Arg.Any<NativeProjectPreparationContext>(),
                Arg.Any<NativeProjectPreparationPreview>(), Arg.Any<CancellationToken>());
        }
        else
        {
            await preparation.Received(1).CompleteAsync(Arg.Any<NativeProjectPreparationContext>(),
                Arg.Any<NativeProjectPreparationPreview>(), Arg.Any<CancellationToken>());
        }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task ProjectPreparation_ReturnedOutcome_PreservesDiagnosticsAndLedgerThroughLateCancellation(bool succeeded, bool lateCancellation)
    {
        await InstallAsync();
        using CancellationTokenSource cancellation = new();
        var preparation = Substitute.For<INativeProjectPreparation>();
        preparation.PreviewAsync(Arg.Any<NativeProjectPreparationContext>(), Arg.Any<CancellationToken>())
            .Returns(new NativeProjectPreparationPreview(true, [new(NativeProjectPreparationOperation.NodeNpmInstall, false)], []));
        TemplateEditorNoOpEffect editor = new(new("Controlled.Project", "owned-helper", new('A', 64)));
        NativeTemplateEffectResult ledger = new("applied", true, new(WorkingDirectory.FromExplicit(Project), [editor]), [editor], [], ["Owned helper result"]);
        NativeProjectPreparationResult returned = new(succeeded, ["Owned preparation diagnostic: original stderr"], ledger);
        preparation.CompleteAsync(Arg.Any<NativeProjectPreparationContext>(), Arg.Any<NativeProjectPreparationPreview>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                if (lateCancellation) cancellation.Cancel();
                return returned;
            });
        using var harness = CreateHarness(preparation: preparation);

        var parsed = harness.Adapter.ParseInitInvocation(harness.CreateInit(), [Project, "-s", "node", "-l", "js"],
            new("controlled-project", NonInteractive: true, JsonOutput: true));
        int exit = await parsed.InvokeAsync(new() { EnableDefaultExceptionHandler = false }, cancellation.Token);

        exit.Should().Be(succeeded ? 0 : 1);
        AssertJsonKind(succeeded ? "created" : "partialPreparation");
        harness.InitResult!.Preparation!.Completion.Should().BeSameAs(returned);
        harness.InitResult.Preparation.Completion!.Effects.Should().BeSameAs(ledger);
        File.Exists(Path.Combine(Project, ".func", "config.json")).Should().BeTrue();
        File.Exists(Path.Combine(Project, "host.json")).Should().BeTrue();
        _interaction.AllOutput.Should().Contain("original stderr").And.NotContain("SUCCESS:");
    }

    [Fact]
    public async Task ProjectPreparation_WrongStackOperation_FailsBeforeNativeWrites()
    {
        await InstallAsync();
        var preparation = Substitute.For<INativeProjectPreparation>();
        preparation.PreviewAsync(Arg.Any<NativeProjectPreparationContext>(), Arg.Any<CancellationToken>())
            .Returns(new NativeProjectPreparationPreview(true, [new(NativeProjectPreparationOperation.GoModTidy, false)], []));
        using var harness = CreateHarness(preparation: preparation);

        int exit = await InvokeAsync(harness.Adapter.ParseInitInvocation(harness.CreateInit(), [Project, "-s", "node", "-l", "js"],
            new("controlled-project", NonInteractive: true, JsonOutput: true)));

        exit.Should().Be(1);
        AssertJsonKind("preparationUnavailable");
        Directory.Exists(Project).Should().BeFalse();
        await preparation.DidNotReceive().CompleteAsync(Arg.Any<NativeProjectPreparationContext>(),
            Arg.Any<NativeProjectPreparationPreview>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("body")]
    [InlineData("configuration")]
    public async Task ProjectPreparation_SourceChangesAfterPreview_RefusesBeforeCurrentCreateAndPreparation(string changed)
    {
        await InstallAsync();
        var preparation = Substitute.For<INativeProjectPreparation>();
        preparation.PreviewAsync(Arg.Any<NativeProjectPreparationContext>(), Arg.Any<CancellationToken>()).Returns(_ =>
        {
            string file = changed == "body" ? Path.Combine(_root, "package", "project", "project.txt")
                : Path.Combine(_root, "package", "project", ".template.config", "template.json");
            File.AppendAllText(file, changed == "body" ? "changed" : "\n ");
            return new NativeProjectPreparationPreview(true, [new(NativeProjectPreparationOperation.NodeNpmInstall, false)], []);
        });
        using var harness = CreateHarness(preparation: preparation);

        int exit = await InvokeAsync(harness.Adapter.ParseInitInvocation(harness.CreateInit(), [Project],
            new("controlled-project", NonInteractive: true, JsonOutput: true)));

        exit.Should().Be(1, _interaction.AllOutput);
        harness.InitResult!.Invocation!.Status.Should().Be(TemplateInvocationStatus.TemplateChanged);
        harness.InitResult.Invocation.HasCommittedOutput.Should().BeFalse();
        Directory.Exists(Project).Should().BeFalse();
        await preparation.DidNotReceive().CompleteAsync(Arg.Any<NativeProjectPreparationContext>(),
            Arg.Any<NativeProjectPreparationPreview>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ProjectPreparation_SkippedOperation_IsReportedWithoutClaimingItRan()
    {
        await InstallAsync();
        var preparation = Substitute.For<INativeProjectPreparation>();
        preparation.PreviewAsync(Arg.Any<NativeProjectPreparationContext>(), Arg.Any<CancellationToken>())
            .Returns(new NativeProjectPreparationPreview(true, [new(NativeProjectPreparationOperation.NodeNpmInstall, true)], []));
        preparation.CompleteAsync(Arg.Any<NativeProjectPreparationContext>(), Arg.Any<NativeProjectPreparationPreview>(), Arg.Any<CancellationToken>())
            .Returns(new NativeProjectPreparationResult(true, ["Skipped npm installation."]));
        using var harness = CreateHarness(preparation: preparation);

        int exit = await InvokeAsync(harness.Adapter.ParseInitInvocation(harness.CreateInit(), [Project, "-s", "node", "-l", "js"],
            new("controlled-project", NonInteractive: true, JsonOutput: true)));

        exit.Should().Be(0);
        _interaction.AllOutput.Should().Contain("\"skipped\":true").And.Contain("Skipped npm installation.");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ProjectPreparation_Cancellation_PreservesExceptionAndWarnsOnlyAfterNativeConfiguration(bool duringCompletion)
    {
        await InstallAsync();
        using CancellationTokenSource cancellation = new();
        var original = new OperationCanceledException("Owned preparation cancellation", cancellation.Token);
        var preparation = Substitute.For<INativeProjectPreparation>();
        preparation.PreviewAsync(Arg.Any<NativeProjectPreparationContext>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                if (!duringCompletion)
                {
                    cancellation.Cancel();
                    return Task.FromException<NativeProjectPreparationPreview>(original);
                }

                return Task.FromResult(new NativeProjectPreparationPreview(true, [new(NativeProjectPreparationOperation.NodeNpmInstall, false)], []));
            });
        preparation.CompleteAsync(Arg.Any<NativeProjectPreparationContext>(), Arg.Any<NativeProjectPreparationPreview>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                cancellation.Cancel();
                return Task.FromException<NativeProjectPreparationResult>(original);
            });
        using var harness = CreateHarness(preparation: preparation);
        var parsed = harness.Adapter.ParseInitInvocation(harness.CreateInit(), [Project, "-s", "node", "-l", "js"],
            new("controlled-project", NonInteractive: true, JsonOutput: true));

        Func<Task> act = () => parsed.InvokeAsync(new() { EnableDefaultExceptionHandler = false }, cancellation.Token);

        var thrown = await act.Should().ThrowAsync<OperationCanceledException>();
        thrown.Which.Should().BeSameAs(original);
        thrown.Which.CancellationToken.Should().Be(cancellation.Token);
        File.Exists(Path.Combine(Project, "host.json")).Should().Be(duringCompletion);
        File.Exists(Path.Combine(Project, ".func", "config.json")).Should().Be(duringCompletion);
        _interaction.AllOutput.Should().NotContain("SUCCESS:").And.NotContain("\"succeeded\":true");
        if (duringCompletion)
        {
            _interaction.AllOutput.Should().Contain("were created before").And.Contain("may be incomplete").And.Contain("No rollback");
        }
        else
        {
            _interaction.AllOutput.Should().NotContain("were created before");
        }
    }

    [Fact]
    public async Task ProjectPreparation_PartialNativeInstallation_DoesNotStartPreparationOrInventConfiguration()
    {
        await InstallAsync();
        using CancellationTokenSource cancellation = new();
        List<string> committed = [];
        var files = new RecordingCommitFileSystem(new PhysicalFileSystem())
        {
            AfterMove = path =>
            {
                committed.Add(path);
                cancellation.Cancel();
            },
        };
        var preparation = Substitute.For<INativeProjectPreparation>();
        preparation.PreviewAsync(Arg.Any<NativeProjectPreparationContext>(), Arg.Any<CancellationToken>())
            .Returns(new NativeProjectPreparationPreview(true, [new(NativeProjectPreparationOperation.NodeNpmInstall, false)], []));
        using var harness = CreateHarness(preparation: preparation, fileSystem: files);
        var parsed = harness.Adapter.ParseInitInvocation(harness.CreateInit(), [Project],
            new("controlled-project", NonInteractive: true, JsonOutput: true));

        int exit = await parsed.InvokeAsync(new() { EnableDefaultExceptionHandler = false }, cancellation.Token);

        exit.Should().Be(1);
        harness.InitResult!.Invocation!.Status.Should().Be(TemplateInvocationStatus.Failed);
        harness.InitResult.Invocation.HasCommittedOutput.Should().BeTrue();
        harness.InitResult.Invocation.ConfiguredProjects.Should().BeEmpty();
        harness.InitResult.Invocation.Changes.Select(change => Path.GetFullPath(Path.Combine(Project, change.Path)))
            .Should().Equal(Project, committed.Should().ContainSingle().Subject);
        harness.InitResult.Invocation.Changes.Should().OnlyContain(change => change.Kind == TemplateFileChangeKind.Create);
        File.Exists(committed[0]).Should().BeTrue();
        File.Exists(Path.Combine(Project, ".func", "config.json")).Should().BeFalse();
        harness.InitResult.Preparation!.Completion.Should().BeNull();
        await preparation.DidNotReceive().CompleteAsync(Arg.Any<NativeProjectPreparationContext>(),
            Arg.Any<NativeProjectPreparationPreview>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(false, true, false)]
    [InlineData(true, false, false)]
    [InlineData(true, true, false)]
    [InlineData(false, false, true)]
    [InlineData(false, true, true)]
    [InlineData(true, false, true, true)]
    [InlineData(true, true, true, true)]
    public async Task Adapter_NewCommittedCreationThenCancellation_RendersNativeResult(bool noOutputs, bool json, bool afterRunner, bool existingTarget = false)
    {
        await InstallBasicNodeAsync();
        if (noOutputs)
        {
            WriteTemplate("empty", """
                {"identity":"Controlled.Empty","name":"Empty","shortName":"empty","tags":{"type":"item","language":"JavaScript"}}
                """, "{}");
            await TemplateFixtures.InstallAsync(Path.Combine(_root, "package"), Path.Combine(Home, "templates"), CancellationToken.None);
        }

        using CancellationTokenSource cancellation = new();
        var files = new RecordingCommitFileSystem(new PhysicalFileSystem())
        {
            AfterMove = path => { if (!afterRunner && !noOutputs && path == Path.Combine(Target, "Committed.js")) cancellation.Cancel(); },
            AfterCreateDirectory = path => { if (!afterRunner && noOutputs && path == Target) cancellation.Cancel(); },
        };
        using var harness = CreateHarness(fileSystem: files,
            afterNew: afterRunner ? _ => cancellation.Cancel() : null);
        await CreateBasicProjectAsync(harness);
        if (existingTarget)
        {
            Directory.CreateDirectory(Target);
        }
        int beforeOutput = _interaction.Lines.Count;
        int? exit = null;
        var parsed = harness.Adapter.ParseNewInvocation(harness.CreateNew(),
            [Target, "-t", noOutputs ? "Controlled.Empty" : "Azure.Functions.Node.Http.JavaScript",
                "-n", "Committed", "--non-interactive", "--output", json ? "json" : "plain"]);

        Exception? failure = await Record.ExceptionAsync(async () =>
            exit = await parsed.InvokeAsync(new() { EnableDefaultExceptionHandler = false }, cancellation.Token));

        if (!noOutputs)
        {
            File.ReadAllText(Path.Combine(Target, "Committed.js")).Should().Contain("app.http('Committed'");
        }
        harness.NewResult!.Invocation!.Status.Should().Be(TemplateInvocationStatus.Success);
        harness.NewResult.Invocation.Changes.Should().HaveCount(noOutputs ? 0 : 1);
        harness.NewResult.Invocation.HasCommittedOutput.Should().Be(!existingTarget);
        cancellation.IsCancellationRequested.Should().BeTrue();
        failure.Should().BeNull("a native committed result must reach the adapter renderer even for zero outputs");
        exit.Should().Be(0);
        AssertCommittedRendering(beforeOutput, "created", json, succeeded: true);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task Adapter_InitCommittedConfigurationThenCancellation_RendersNativeResult(bool json, bool afterRunner)
    {
        await InstallBasicNodeAsync();
        using CancellationTokenSource cancellation = new();
        var files = new ConfigurationBoundaryFileSystem(afterRunner ? null : cancellation);
        using var harness = CreateHarness(fileSystem: files, afterInit: afterRunner ? _ => cancellation.Cancel() : null);
        int? exit = null;
        var parsed = harness.Adapter.ParseInitInvocation(harness.CreateInit(),
            [Project, "-s", "node", "-l", "js", "--no-bundles"],
            new("Azure.Functions.Node.Basic.JavaScript", NonInteractive: true, JsonOutput: json));

        Exception? failure = await Record.ExceptionAsync(async () =>
            exit = await parsed.InvokeAsync(new() { EnableDefaultExceptionHandler = false }, cancellation.Token));

        File.Exists(Path.Combine(Project, "host.json")).Should().BeTrue();
        using var configuration = JsonDocument.Parse(File.ReadAllText(Path.Combine(Project, ".func", "config.json")));
        configuration.RootElement.GetProperty("stack").GetProperty("language").GetString().Should().Be("JavaScript");
        files.CommittedConfigurations.Should().Equal(Path.Combine(Project, ".func", "config.json"));
        harness.InitResult!.Invocation!.Status.Should().Be(TemplateInvocationStatus.Success);
        harness.InitResult.Invocation.ConfiguredProjects.Should().Equal(Project);
        cancellation.IsCancellationRequested.Should().BeTrue();
        failure.Should().BeNull("the final native configuration was committed before cancellation");
        exit.Should().Be(0);
        AssertCommittedRendering(0, "created", json, succeeded: true);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Adapter_InitConfirmedPartialResultThenCancellation_RendersFailureAndCommittedSubset(bool json)
    {
        WriteTemplate("multi", $$$"""
            {"identity":"Controlled.Multi","name":"Multi","shortName":"multi","tags":{"type":"project"},
             "constraints":{"func":{"type":"host","args":[{"hostname":"func"}]}},
             "primaryOutputs":[{"path":"api/host.json"},{"path":"worker/host.json"}],"postActions":[
               {"actionId":"{{{TemplateProjectConfigurationPlanner.ConfigurationActionId}}}",
                "args":{"primaryOutputIndex":"0","stack":"node","language":"JavaScript"}},
               {"actionId":"{{{TemplateProjectConfigurationPlanner.ConfigurationActionId}}}",
                "args":{"primaryOutputIndex":"1","stack":"node","language":"JavaScript"}}]}
            """, "{}", ("api/host.json", """{"version":"2.0"}"""), ("worker/host.json", """{"version":"2.0"}"""));
        await TemplateFixtures.InstallAsync(Path.Combine(_root, "package"), Path.Combine(Home, "templates"), CancellationToken.None);
        using CancellationTokenSource cancellation = new();
        string failedConfiguration = Path.Combine(Project, "worker", ".func", "config.json");
        var files = new ConfigurationBoundaryFileSystem(failedPath: failedConfiguration);
        using var harness = CreateHarness(fileSystem: files, afterInit: _ => cancellation.Cancel());
        int? exit = null;
        var parsed = harness.Adapter.ParseInitInvocation(harness.CreateInit(), [Project],
            new("Controlled.Multi", NonInteractive: true, JsonOutput: json));

        Exception? failure = await Record.ExceptionAsync(async () =>
            exit = await parsed.InvokeAsync(new() { EnableDefaultExceptionHandler = false }, cancellation.Token));

        File.Exists(Path.Combine(Project, "worker", "host.json")).Should().BeTrue();
        File.Exists(Path.Combine(Project, "api", ".func", "config.json")).Should().BeTrue();
        File.Exists(failedConfiguration).Should().BeFalse();
        harness.InitResult!.Kind.Should().Be("partialInitialization");
        harness.InitResult.Invocation!.Status.Should().Be(TemplateInvocationStatus.ConfigurationFailure);
        harness.InitResult.Invocation.ConfiguredProjects.Should().Equal(Path.Combine(Project, "api"));
        cancellation.IsCancellationRequested.Should().BeTrue();
        failure.Should().BeNull("the authoritative partial configuration outcome must not be masked");
        exit.Should().Be(1);
        AssertCommittedRendering(0, "partialInitialization", json, succeeded: false);
        if (json)
        {
            using var result = LastJson();
            result.RootElement.GetProperty("invocation").GetProperty("configuredProjects").GetArrayLength().Should().Be(1);
        }
        else
        {
            _interaction.AllOutput.Should().Contain("controlled configuration failure").And.Contain("Configured project:");
        }
    }

    [Theory]
    [InlineData("preview")]
    [InlineData("missing")]
    [InlineData("list")]
    public async Task Adapter_NewUncommittedResultThenCancellation_DoesNotRender(string outcome)
    {
        await InstallBasicNodeAsync(owned: true);
        using CancellationTokenSource cancellation = new();
        using var harness = await CreateOwnedHarnessAsync(afterNew: _ => cancellation.Cancel());
        await CreateBasicProjectAsync(harness);
        int beforeOutput = _interaction.Lines.Count;
        string[] selection = outcome == "list" ? ["--list"] : ["-t", outcome == "missing" ? "missing" : "Azure.Functions.Node.Http.JavaScript"];
        var parsed = harness.Adapter.ParseNewInvocation(harness.CreateNew(), [Target, .. selection, "--output", "json"],
            dryRun: outcome == "preview");

        Func<Task> act = () => parsed.InvokeAsync(new() { EnableDefaultExceptionHandler = false }, cancellation.Token);

        (await act.Should().ThrowAsync<OperationCanceledException>()).Which.CancellationToken.Should().Be(harness.NewToken);
        harness.NewResult!.Kind.Should().Be(outcome switch { "preview" => "previewed", "missing" => "NotFound", _ => "listed" });
        Directory.Exists(Target).Should().BeFalse();
        _interaction.Lines.Skip(beforeOutput).Should().BeEmpty();
    }

    [Theory]
    [InlineData("preview")]
    [InlineData("createFailed")]
    [InlineData("nativeCancelled")]
    [InlineData("throwBeforeCreate")]
    public async Task Adapter_NewNativeCancellationBeforeCommit_PropagatesWithoutRendering(string boundary)
    {
        await InstallBasicNodeAsync(owned: true);
        using CancellationTokenSource cancellation = new();
        var native = new CancelingNativeFactory(Path.Combine(Home, "templates"), cancellation, boundary);
        using var harness = await CreateOwnedHarnessAsync(templateFactory: native);
        await CreateBasicProjectAsync(harness);
        int beforeOutput = _interaction.Lines.Count;
        var parsed = harness.Adapter.ParseNewInvocation(harness.CreateNew(),
            [Target, "-t", "Azure.Functions.Node.Http.JavaScript", "--output", "json"]);

        Func<Task> act = () => parsed.InvokeAsync(new() { EnableDefaultExceptionHandler = false }, cancellation.Token);

        (await act.Should().ThrowAsync<OperationCanceledException>()).Which.CancellationToken.Should().Be(harness.NewToken);
        harness.NewResult.Should().BeNull();
        Directory.Exists(Target).Should().BeFalse();
        _interaction.Lines.Skip(beforeOutput).Should().BeEmpty();
        if (boundary != "throwBeforeCreate")
        {
            native.NativeResult!.Status.Should().Be(boundary switch
            {
                "createFailed" => CreationResultStatus.CreateFailed,
                "nativeCancelled" => CreationResultStatus.Cancelled,
                _ => CreationResultStatus.Success,
            });
        }
    }

    [Theory]
    [InlineData("preview")]
    [InlineData("missing")]
    [InlineData("adopted")]
    [InlineData("repaired")]
    public async Task Adapter_InitUnconfirmedResultThenCancellation_DoesNotRender(string outcome)
    {
        await InstallBasicNodeAsync();
        bool existing = outcome is "adopted" or "repaired";
        if (existing)
        {
            WriteExistingProject(outcome == "repaired");
        }
        using CancellationTokenSource cancellation = new();
        using var harness = CreateHarness(afterInit: _ => cancellation.Cancel());
        string? template = existing ? null : outcome == "missing" ? "missing" : "Azure.Functions.Node.Basic.JavaScript";
        var parsed = harness.Adapter.ParseInitInvocation(harness.CreateInit(), [Project, "-s", "node", "-l", "js"],
            new(template, NonInteractive: true, DryRun: outcome == "preview", JsonOutput: true));

        Func<Task> act = () => parsed.InvokeAsync(new() { EnableDefaultExceptionHandler = false }, cancellation.Token);

        (await act.Should().ThrowAsync<OperationCanceledException>()).Which.CancellationToken.Should().Be(harness.InitToken);
        harness.InitResult!.Kind.Should().Be(outcome switch { "preview" => "previewed", "missing" => "NotFound", _ => outcome });
        File.Exists(Path.Combine(Project, ".func", "config.json")).Should().Be(existing);
        if (existing)
        {
            harness.InitResult.Configuration!.Written.Should().BeTrue();
            File.ReadAllText(Path.Combine(Project, "user.js")).Should().Be("user-owned");
        }
        _interaction.AllOutput.Should().NotContain("JSON:").And.NotContain("SUCCESS:");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Adapter_ExistingConfigurationCommitThenCancellation_IsNotNativeCreation(bool repair)
    {
        WriteExistingProject(repair);
        using CancellationTokenSource cancellation = new();
        var files = new ConfigurationBoundaryFileSystem(cancellation);
        using var harness = CreateHarness(fileSystem: files);
        var parsed = harness.Adapter.ParseInitInvocation(harness.CreateInit(), [Project, "-s", "node", "-l", "js"],
            new(NonInteractive: true, JsonOutput: true));

        Func<Task> act = () => parsed.InvokeAsync(new() { EnableDefaultExceptionHandler = false }, cancellation.Token);

        (await act.Should().ThrowAsync<OperationCanceledException>()).Which.CancellationToken.Should().Be(harness.InitToken);
        files.CommittedConfigurations.Should().Equal(Path.Combine(Project, ".func", "config.json"));
        using var configuration = JsonDocument.Parse(File.ReadAllText(Path.Combine(Project, ".func", "config.json")));
        configuration.RootElement.GetProperty("stack").GetProperty("language").GetString().Should().Be("JavaScript");
        harness.InitResult.Should().BeNull();
        _interaction.AllOutput.Should().NotContain("JSON:").And.NotContain("SUCCESS:");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Adapter_PreCanceledInvocation_DoesNotRunOrRender(bool init)
    {
        using CancellationTokenSource cancellation = new();
        using var harness = CreateHarness();
        var parsed = init ? harness.Adapter.ParseInitInvocation(harness.CreateInit(), [Project])
            : harness.Adapter.ParseNewInvocation(harness.CreateNew(), [Target]);
        await cancellation.CancelAsync();

        Func<Task> act = () => parsed.InvokeAsync(new() { EnableDefaultExceptionHandler = false }, cancellation.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        harness.InitResult.Should().BeNull();
        harness.NewResult.Should().BeNull();
        Directory.Exists(Project).Should().BeFalse();
        _interaction.Lines.Should().BeEmpty();
    }

    [Fact]
    public async Task Adapter_InitCancellationBeforeConfigurationCommit_PreservesAndRendersActualNativeSubset()
    {
        await InstallBasicNodeAsync();
        using CancellationTokenSource cancellation = new();
        var files = new ConfigurationBoundaryFileSystem(cancellation, cancelBeforeCommit: true);
        using var harness = CreateHarness(fileSystem: files);
        var parsed = harness.Adapter.ParseInitInvocation(harness.CreateInit(), [Project, "--no-bundles"],
            new("Azure.Functions.Node.Basic.JavaScript", NonInteractive: true, JsonOutput: true));

        int exit = await parsed.InvokeAsync(new() { EnableDefaultExceptionHandler = false }, cancellation.Token);

        exit.Should().Be(1);
        File.Exists(Path.Combine(Project, "host.json")).Should().BeTrue();
        File.Exists(Path.Combine(Project, ".func", "config.json")).Should().BeFalse();
        files.CommittedConfigurations.Should().BeEmpty();
        harness.InitResult!.Invocation!.HasCommittedOutput.Should().BeTrue();
        harness.InitResult.Invocation.Status.Should().Be(TemplateInvocationStatus.ConfigurationFailure);
        harness.InitResult.Invocation.ConfiguredProjects.Should().BeEmpty();
        _interaction.AllOutput.Should().Contain("JSON:").And.NotContain("SUCCESS:");
    }

    [Theory]
    [InlineData(true, false, false, false, false)]
    [InlineData(true, false, false, true, false)]
    [InlineData(true, false, true, false, false)]
    [InlineData(true, false, true, true, false)]
    [InlineData(false, false, false, false, false)]
    [InlineData(false, false, false, true, false)]
    [InlineData(false, false, true, false, false)]
    [InlineData(false, false, true, true, false)]
    [InlineData(false, true, false, false, false)]
    [InlineData(false, true, false, true, false)]
    [InlineData(false, true, true, false, false)]
    [InlineData(false, true, true, true, false)]
    [InlineData(false, false, false, false, true)]
    [InlineData(false, false, false, true, true)]
    [InlineData(false, false, true, false, true)]
    [InlineData(false, false, true, true, true)]
    public async Task Renderer_QualifiedProjectEffects_PreservesPlansAndActualOutcomes(
        bool dryRun, bool restoreFails, bool preparation, bool json, bool configurationFails)
    {
        const string identity = "Controlled.DependencyProject";
        WriteTemplate("dependencies", $$$"""
            {"identity":"{{{identity}}}","name":"Dependencies","shortName":"dependencies",
             "tags":{"type":"project","language":"C#"},"sourceName":"Company.FunctionApp",
             "constraints":{"func":{"type":"host","args":[{"hostname":"func"}]}},
             "sources":[{"modifiers":[{"rename":{"_gitignore":".gitignore"}}]}],
             "symbols":{"Framework":{"type":"parameter","datatype":"choice","defaultValue":"net10.0",
               "replaces":"TargetFrameworkValue","choices":[{"choice":"net10.0"}]}},
             "primaryOutputs":[{"path":"Company.FunctionApp.csproj"}],"postActions":[
               {"actionId":"{{{NativeTemplateEffectPlanner.PackageReferenceActionId}}}",
                "args":{"referenceType":"package","reference":"Controlled.Package","version":"2.0.0-preview.3",
                  "projectFileExtensions":".csproj"}},
               {"actionId":"{{{NativeTemplateEffectPlanner.RestoreActionId}}}","args":{}}]}
            """, "{}", ("host.json", """{"version":"2.0"}"""), ("_gitignore", "bin/"),
            ("Company.FunctionApp.csproj", "<Project><PropertyGroup><TargetFramework>TargetFrameworkValue</TargetFramework></PropertyGroup></Project>"));
        string fingerprint = Convert.ToHexString(SHA256.HashData(
            File.ReadAllBytes(Path.Combine(_root, "package", "dependencies", ".template.config", "template.json"))));
        await TemplateFixtures.InstallAsync(Path.Combine(_root, "package"), Path.Combine(Home, "templates"), CancellationToken.None);
        var stack = Substitute.For<IProjectStack>();
        stack.Stack.Returns("dotnet");
        stack.DisplayName.Returns(".NET");
        stack.WorkerRuntimeAliases.Returns([]);
        stack.SupportedLanguages.Returns(["C#"]);
        stack.SupportedLanguageAliases.Returns(new Dictionary<string, IReadOnlyList<string>>());
        var capability = Substitute.For<INativeTemplateEffectCapability>();
        capability.ValidateAsync(Arg.Any<NativeTemplateEffectPlan>(), Arg.Any<CancellationToken>()).Returns([]);
        capability.ExecuteAsync(Arg.Any<TemplatePackageReferenceEffect>(), Arg.Any<CancellationToken>())
            .Returns(new TemplateEffectExecutionResult(true, ["Controlled package work completed."]));
        capability.ExecuteAsync(Arg.Any<TemplateRestoreEffect>(), Arg.Any<CancellationToken>())
            .Returns(new TemplateEffectExecutionResult(!restoreFails,
                [restoreFails ? "Controlled restore failed." : "Controlled restore completed."], TargetMayHaveChanged: restoreFails));
        var workflow = Substitute.For<INativeProjectWorkflowProvider>();
        workflow.Stack.Returns("dotnet");
        workflow.Handles(Arg.Any<TemplateCatalogEntry>()).Returns(call => call.Arg<TemplateCatalogEntry>().Identity == identity);
        workflow.QualifyLayoutAsync(Arg.Any<NativeProjectTemplateSource>(), Arg.Any<CancellationToken>())
            .Returns(new NativeProjectLayoutQualification(identity, fingerprint, new(0, "dotnet", "C#")));
        workflow.PrepareAsync(Arg.Any<NativeProjectWorkflowContext>(), Arg.Any<CancellationToken>())
            .Returns(call => NativeTemplateInvocationPreparation.Ready(call.Arg<NativeProjectWorkflowContext>().Invocation with
            {
                EffectSupport = new(WorkingDirectory.FromExplicit(Project), Path.Combine(Project, "Committed.csproj"), capability, Confirmed: true),
            }));
        var support = Substitute.For<INativeProjectPreparation>();
        support.PreviewAsync(Arg.Any<NativeProjectPreparationContext>(), Arg.Any<CancellationToken>())
            .Returns(new NativeProjectPreparationPreview(true, [], ["Preparation planned independently."]));
        support.CompleteAsync(Arg.Any<NativeProjectPreparationContext>(), Arg.Any<NativeProjectPreparationPreview>(), Arg.Any<CancellationToken>())
            .Returns(new NativeProjectPreparationResult(true, ["Preparation completed independently."]));
        workflow.Preparation.Returns(preparation ? support : null);
        var files = new ConfigurationBoundaryFileSystem(failedPath: configurationFails ? Path.Combine(Project, ".func", "config.json") : null);
        using var harness = CreateHarness(fileSystem: files, additionalStack: stack, projectWorkflow: workflow);

        int exit = await InvokeAsync(harness.Adapter.ParseInitInvocation(harness.CreateInit(),
            [Project, "-s", "dotnet", "-l", "C#", "-n", "Committed", "--Framework", "net10.0"],
            new(identity, NonInteractive: true, DryRun: dryRun, JsonOutput: json)));

        bool succeeded = dryRun || (!restoreFails && !configurationFails);
        exit.Should().Be(succeeded ? 0 : 1, _interaction.AllOutput);
        var effects = (harness.InitResult!.Invocation?.Effects ?? harness.InitResult.Preview!.Effects)!;
        effects.Plan.Effects.Should().HaveCount(2);
        effects.Plan.Effects.Should().OnlyContain(effect => effect.Source.Sha256 == fingerprint && effect.Source.TemplateIdentity == identity);
        effects.CompletedEffects.Should().HaveCount(dryRun || configurationFails ? 0 : restoreFails ? 1 : 2);
        effects.FailedEffects.Should().HaveCount(!dryRun && restoreFails ? 1 : 0);
        File.Exists(Path.Combine(Project, ".func", "config.json")).Should().Be(!dryRun && !configurationFails);
        File.Exists(Path.Combine(Project, "Committed.csproj")).Should().Be(!dryRun);
        if (!dryRun && !configurationFails)
        {
            File.ReadAllText(Path.Combine(Project, "Committed.csproj")).Should().Contain("net10.0");
            harness.InitResult.Invocation!.ConfiguredProjects.Should().Equal(Project);
            await capability.Received(1).ExecuteAsync(Arg.Any<TemplatePackageReferenceEffect>(), Arg.Any<CancellationToken>());
            await capability.Received(1).ExecuteAsync(Arg.Any<TemplateRestoreEffect>(), Arg.Any<CancellationToken>());
        }
        else
        {
            await capability.DidNotReceive().ExecuteAsync(Arg.Any<TemplateEffect>(), Arg.Any<CancellationToken>());
        }
        if (preparation)
        {
            harness.InitResult.Preparation.Should().NotBeNull();
            if (dryRun || restoreFails || configurationFails)
            {
                await support.DidNotReceive().CompleteAsync(Arg.Any<NativeProjectPreparationContext>(),
                    Arg.Any<NativeProjectPreparationPreview>(), Arg.Any<CancellationToken>());
            }
            else
            {
                await support.Received(1).CompleteAsync(Arg.Any<NativeProjectPreparationContext>(),
                    Arg.Any<NativeProjectPreparationPreview>(), Arg.Any<CancellationToken>());
            }
        }

        if (json)
        {
            using var document = LastJson();
            var invocation = document.RootElement.GetProperty("invocation");
            invocation.TryGetProperty("preparation", out _).Should().Be(preparation);
            var projected = invocation.GetProperty("effects");
            projected.GetProperty("kind").GetString().Should().Be(dryRun || configurationFails ? "previewed" : restoreFails ? "partialFailure" : "applied");
            projected.GetProperty("succeeded").GetBoolean().Should().Be(dryRun || configurationFails || !restoreFails);
            projected.GetProperty("planned").GetArrayLength().Should().Be(2);
            projected.GetProperty("planned")[0].GetProperty("packageId").GetString().Should().Be("Controlled.Package");
            projected.GetProperty("planned")[0].GetProperty("packageVersion").GetString().Should().Be("2.0.0-preview.3");
            projected.GetProperty("completed").GetArrayLength().Should().Be(dryRun || configurationFails ? 0 : restoreFails ? 1 : 2);
            projected.GetProperty("failed").GetArrayLength().Should().Be(!dryRun && restoreFails ? 1 : 0);
            if (!dryRun && restoreFails)
            {
                projected.GetProperty("completed")[0].GetProperty("kind").GetString().Should().Be(nameof(TemplatePackageReferenceEffect));
                projected.GetProperty("failed")[0].GetProperty("kind").GetString().Should().Be(nameof(TemplateRestoreEffect));
                projected.GetProperty("diagnostics").ToString().Should().Contain("Controlled restore failed.");
            }
            invocation.GetProperty("completedChanges").GetArrayLength().Should().Be(dryRun ? 0 : configurationFails ? 3 : 4);
            if (configurationFails)
            {
                invocation.GetProperty("status").GetString().Should().Be("ConfigurationFailure");
                invocation.GetProperty("configuredProjects").GetArrayLength().Should().Be(0);
            }
        }
        else
        {
            _interaction.AllOutput.Should().Contain("Planned dependency effects").And.Contain("Controlled.Package")
                .And.Contain("2.0.0-preview.3").And.Contain("Committed.csproj");
            if (dryRun || configurationFails)
            {
                _interaction.AllOutput.Should().NotContain("Completed dependency effects").And.NotContain("Failed dependency effects");
            }
            else
            {
                _interaction.AllOutput.Should().Contain("Completed dependency effects").And.Contain("Controlled package work completed.");
                if (restoreFails)
                {
                    _interaction.AllOutput.Should().Contain("Failed dependency effects").And.Contain("Controlled restore failed.");
                }
            }
        }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Renderer_NoDependencyEffects_RetainsExistingJsonShape(bool preparation, bool dryRun)
    {
        await InstallBasicNodeAsync();
        var support = Substitute.For<INativeProjectPreparation>();
        support.PreviewAsync(Arg.Any<NativeProjectPreparationContext>(), Arg.Any<CancellationToken>())
            .Returns(new NativeProjectPreparationPreview(true, [], []));
        support.CompleteAsync(Arg.Any<NativeProjectPreparationContext>(), Arg.Any<NativeProjectPreparationPreview>(), Arg.Any<CancellationToken>())
            .Returns(new NativeProjectPreparationResult(true, []));
        using var harness = CreateHarness(preparation: preparation ? support : null);

        int exit = await InvokeAsync(harness.Adapter.ParseInitInvocation(harness.CreateInit(),
            [Project, "-s", "node", "-l", "js", "--no-bundles"],
            new("Azure.Functions.Node.Basic.JavaScript", NonInteractive: true, DryRun: dryRun, JsonOutput: true)));

        exit.Should().Be(0);
        using var document = LastJson();
        string[] properties = [.. document.RootElement.GetProperty("invocation").EnumerateObject().Select(property => property.Name)];
        string[] expected = preparation
            ? ["status", "plannedChanges", "completedChanges", "plannedConfigurations", "configuredProjects", "preparation"]
            : ["status", "plannedChanges", "completedChanges", "plannedConfigurations", "configuredProjects"];
        properties.Should().BeEquivalentTo(expected);
        _interaction.AllOutput.Should().NotContain("\"effects\":");
    }

    [Fact]
    public void Renderer_NullTypedEffectProjection_RejectsProgrammerError()
    {
        Action act = () => NewExecutionRenderer.ProjectEffects(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    public void Dispose()
    {
        IReadOnlyList<WeakReference> owners = DisposeProvidersAndUnloadOwnedWorkloads();
        for (int pass = 0; pass < 3 && owners.Any(owner => owner.IsAlive); pass++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }
        owners.Should().OnlyContain(owner => !owner.IsAlive, "the opt-in fixture must release every collectible workload before deleting its files");

        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private IReadOnlyList<WeakReference> DisposeProvidersAndUnloadOwnedWorkloads()
    {
        foreach (ServiceProvider provider in _providers)
        {
            provider.Dispose();
        }
        _providers.Clear();
        IReadOnlyList<WeakReference> owners = _ownedNode?.UnloadOwnedWorkloads() ?? [];
        _ownedNode = null;
        return owners;
    }

    private Harness CreateHarness(bool itemCompatibility = false, INativeProjectPreparation? preparation = null,
        IFileSystem? fileSystem = null, ITemplaterFactory? templateFactory = null,
        Action<InitExecutionResult>? afterInit = null, Action<NewExecutionResult>? afterNew = null,
        IProjectStack? additionalStack = null, INativeProjectWorkflowProvider? projectWorkflow = null)
    {
        ServiceCollection services = new();
        services.AddLogging();
        services.AddSingleton<Azure.Functions.Cli.Console.IInteractionService>(_interaction);
        services.AddSingleton<IProjectInitializationReporter, ProjectInitializationReporter>();
        services.AddSingleton<IFileSystem>(fileSystem ?? new PhysicalFileSystem());
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddSingleton(new CliConfigurationPathsOptions(Home));
        services.AddSingleton<ICliVersionProvider>(AssemblyCliVersionProvider.Instance);
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
        if (additionalStack is not null)
        {
            services.AddSingleton(additionalStack);
        }
        services.AddSingleton<InstalledProjectStackCatalog>();
        if (projectWorkflow is not null)
        {
            services.AddSingleton(provider => new NativeTemplateWorkflowRegistry(
                provider.GetRequiredService<InstalledProjectStackCatalog>(), [], [projectWorkflow]));
        }
        services.AddSingleton<IFunctionsProjectResolver, FunctionsProjectResolver>();
        services.AddSingleton<IHostJsonBundleSectionReader, HostJsonBundleSectionReader>();
        var installed = Substitute.For<IInstalledBundleWorkloads>();
        installed.ListInstalledAsync(Arg.Any<CancellationToken>()).Returns([]);
        services.AddSingleton(installed);
        services.AddSingleton<IExtensionBundleResolver>(new ExtensionBundleResolver(
            new InstalledBundleScanner(installed), NullBundleResolveTelemetry.Instance, NullLogger<ExtensionBundleResolver>.Instance));
        services.AddTemplatesOrchestrator();
        if (templateFactory is not null)
        {
            services.AddSingleton(templateFactory);
        }
        services.AddSingleton<TemplateProjectSupport>();
        var provider = services.BuildServiceProvider();
        return CompleteHarness(provider, new([new("Controlled.Node", NuGetVersion.Parse("1.0.0"), ["node"], true)]),
            itemCompatibility, preparation, afterInit, afterNew);
    }

    private async Task<Harness> CreateOwnedHarnessAsync(
        ITemplaterFactory? templateFactory = null, Action<NewExecutionResult>? afterNew = null)
    {
        BundledNodeTemplateTests fixture = _ownedNode ?? throw new InvalidOperationException("Install the owned Node fixture first.");
        ServiceProvider provider = await fixture.CreateOwnedNodeJourneyServicesAsync(services =>
        {
            services.AddSingleton<Azure.Functions.Cli.Console.IInteractionService>(_interaction);
            if (templateFactory is not null) services.AddSingleton(templateFactory);
            services.AddScoped<NativeTemplateWorkflowRegistry>();
        });
        return CompleteHarness(provider, _ownedNodeAvailability!, false, null, null, afterNew);
    }

    private Harness CompleteHarness(ServiceProvider provider, TemplateConstraintEvaluationSnapshot availability,
        bool itemCompatibility, INativeProjectPreparation? preparation,
        Action<InitExecutionResult>? afterInit, Action<NewExecutionResult>? afterNew)
    {
        _providers.Add(provider);
        IInstalledBundleWorkloads installed = provider.GetRequiredService<IInstalledBundleWorkloads>();
        var scope = provider.CreateScope();
        var init = new RecordingInitRunner(scope.ServiceProvider.GetRequiredService<IInitExecutionRunner>(), afterInit);
        var items = new RecordingNewRunner(scope.ServiceProvider.GetRequiredService<INewExecutionRunner>(), afterNew);
        var adapter = new NativeTemplateCommandAdapter(init, items, provider.GetRequiredService<InitExecutionRenderer>(),
            provider.GetRequiredService<NewExecutionRenderer>(),
            availability,
            itemCompatibility ? new NativeItemCompatibility(_interaction, entry => new(
                new(entry.Entry.Identity, LegacySourcePath, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(LegacySource)))), entry.Entry.Package, null,
                new("trigger-functionName", null, "string", null, null, true,
                    "^[a-zA-Z][a-zA-Z0-9_]{0,126}[a-zA-Z0-9]$", null, null), null, []),
                async (entry, identity, cancellationToken) =>
                {
                    entry.Entry.Package!.Identifier.Should().Be(Path.Combine(_root, "package"));
                    identity.Should().Be(LegacySourcePath);
                    return await File.ReadAllBytesAsync(identity, cancellationToken);
                }) : null, projectPreparation: preparation);
        return new(provider, scope, adapter, _interaction, installed, init, items);
    }

    private async Task InstallBasicNodeAsync(bool owned = false)
    {
        Directory.CreateDirectory(_root);
        var artifacts = BundledNodeTemplateTests.CurrentNodeJourneyArtifacts;
        if (owned)
        {
            _ownedNode = new(_root, Home);
            _ownedNodeAvailability = await _ownedNode.InstallOwnedNodeJourneyAsync();
            return;
        }
        using var outer = ZipFile.OpenRead(artifacts.Workload);
        string innerPath = Path.Combine(_root, "node-basic.nupkg");
        outer.GetEntry("tools/any/templates/node-basic.nupkg")!.ExtractToFile(innerPath);
        await TemplateFixtures.InstallAsync(innerPath, Path.Combine(Home, "templates"), CancellationToken.None);
    }

    private static Dictionary<string, string> NodeArtifactHashes()
        => new[] { "bundled-node-artifacts", "node-function-artifacts" }
            .SelectMany(folder => Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, folder), "*.nupkg"))
            .ToDictionary(path => path, path => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))), StringComparer.Ordinal);

    private async Task CreateBasicProjectAsync(Harness harness)
    {
        int exit = await InvokeAsync(harness.Adapter.ParseInitInvocation(harness.CreateInit(),
            [Project, "-s", "node", "-l", "js", "--no-bundles", "--skip-npm-install"],
            new("Azure.Functions.Node.Basic.JavaScript", NonInteractive: true, JsonOutput: true)));
        exit.Should().Be(0, _interaction.AllOutput);
    }

    private void WriteExistingProject(bool repair)
    {
        Directory.CreateDirectory(Project);
        File.WriteAllText(Path.Combine(Project, "host.json"), """{"version":"2.0"}""");
        File.WriteAllText(Path.Combine(Project, "package.json"), """{"dependencies":{"@azure/functions":"4.0.0"}}""");
        File.WriteAllText(Path.Combine(Project, "local.settings.json"), """{"Values":{"FUNCTIONS_WORKER_RUNTIME":"node"}}""");
        File.WriteAllText(Path.Combine(Project, "user.js"), "user-owned");
        if (repair)
        {
            Directory.CreateDirectory(Path.Combine(Project, ".func"));
            File.WriteAllText(Path.Combine(Project, ".func", "config.json"), """{"stack":{"runtime":"node"}}""");
        }
    }

    private void AssertCommittedRendering(int beforeOutput, string kind, bool json, bool succeeded)
    {
        string[] output = [.. _interaction.Lines.Skip(beforeOutput)];
        if (json)
        {
            string record = output.Should().ContainSingle(line => line.StartsWith("JSON: ", StringComparison.Ordinal)).Subject;
            using var document = JsonDocument.Parse(record["JSON: ".Length..]);
            document.RootElement.GetProperty("kind").GetString().Should().Be(kind);
            document.RootElement.GetProperty("succeeded").GetBoolean().Should().Be(succeeded);
            output.Should().NotContain(line => line.StartsWith("SUCCESS:", StringComparison.Ordinal));
        }
        else
        {
            output.Should().NotContain(line => line.StartsWith("JSON:", StringComparison.Ordinal));
            output.Count(line => line.StartsWith("SUCCESS:", StringComparison.Ordinal)).Should().Be(succeeded ? 1 : 0);
        }
    }

    private async Task InstallAsync()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(LegacySourcePath)!);
        File.WriteAllText(LegacySourcePath, LegacySource, new UTF8Encoding(false));
        WriteTemplate("project", $$$"""
            {"identity":"Controlled.Project","name":"Controlled project","shortName":"controlled-project",
             "tags":{"type":"project"},"sourceName":"ProjectName",
             "constraints":{"func":{"type":"host","args":[{"hostname":"func"}]}},
             "symbols":{"NoBundles":{"type":"parameter","datatype":"bool","defaultValue":"false","replaces":"NO_BUNDLES"}},
             "primaryOutputs":[{"path":"host.json"}],"postActions":[
               {"actionId":"{{{TemplateProjectConfigurationPlanner.ConfigurationActionId}}}",
                "args":{"primaryOutputIndex":"0","stack":"node","language":"JavaScript"}}]}
            """, """{"symbolInfo":{"NoBundles":{"longName":"no-bundles"}}}""",
            ("host.json", """{"version":"2.0"}"""), ("package.json", """{"dependencies":{"@azure/functions":"4.0.0"}}"""),
            ("project.txt", "ProjectName|NO_BUNDLES"));
        WriteTemplate("item", """
            {"identity":"Controlled.Item","name":"Controlled item","shortName":"controlled-item",
             "tags":{"type":"item","language":"JavaScript"},"sourceName":"ItemName","defaultName":"AuthoredFunction",
             "symbols":{"Greeting":{"type":"parameter","datatype":"string","isRequired":true,"replaces":"GREETING"},
               "Auth":{"type":"parameter","datatype":"choice","defaultValue":"function","replaces":"AUTH",
                 "choices":[{"choice":"function"},{"choice":"anonymous"}]}}}
            """, """{"symbolInfo":{"Greeting":{"longName":"greeting"},"Auth":{"longName":"output","shortName":"n"}}}""",
            ("ItemName.txt", "ItemName|GREETING|AUTH"));
        await TemplateFixtures.InstallAsync(Path.Combine(_root, "package"), Path.Combine(Home, "templates"), CancellationToken.None);
    }

    private void WriteTemplate(string folder, string configuration, string host, params (string Path, string Content)[] files)
    {
        string directory = Path.Combine(_root, "package", folder);
        Directory.CreateDirectory(Path.Combine(directory, ".template.config"));
        File.WriteAllText(Path.Combine(directory, ".template.config", "template.json"), configuration);
        File.WriteAllText(Path.Combine(directory, ".template.config", "func.host.json"), host);
        foreach (var file in files)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.Combine(directory, file.Path))!);
            File.WriteAllText(Path.Combine(directory, file.Path), file.Content);
        }
    }

    private async Task CreateProjectAsync(Harness harness)
    {
        int exit = await InvokeAsync(harness.Adapter.ParseInitInvocation(harness.CreateInit(),
            [Project, "-s", "node", "-l", "js"], new("controlled-project", NonInteractive: true, JsonOutput: true)));
        exit.Should().Be(0, _interaction.AllOutput);
    }

    private static Task<int> InvokeAsync(ParseResult parsed) => parsed.InvokeAsync(new() { EnableDefaultExceptionHandler = false });

    private JsonDocument LastJson() => JsonDocument.Parse(
        _interaction.Lines.Last(line => line.StartsWith("JSON: ", StringComparison.Ordinal))["JSON: ".Length..]);

    private void AssertJsonKind(string kind)
    {
        using var json = LastJson();
        json.RootElement.GetProperty("kind").GetString().Should().Be(kind, _interaction.AllOutput);
    }

    private sealed class Harness(ServiceProvider provider, IServiceScope scope, NativeTemplateCommandAdapter adapter,
        TestInteractionService interaction, IInstalledBundleWorkloads installed, RecordingInitRunner init, RecordingNewRunner items) : IDisposable
    {
        private ServiceProvider? _provider = provider ?? throw new ArgumentNullException(nameof(provider));
        private IServiceScope? _scope = scope ?? throw new ArgumentNullException(nameof(scope));

        private ServiceProvider Provider => _provider ?? throw new ObjectDisposedException(nameof(Harness));
        public NativeTemplateCommandAdapter Adapter { get; } = adapter;
        public IServiceProvider Services => (_scope ?? throw new ObjectDisposedException(nameof(Harness))).ServiceProvider;

        public InitExecutionResult? InitResult => init.Result;

        public NewExecutionResult? NewResult => items.Result;

        public CancellationToken InitToken => init.Token;

        public CancellationToken NewToken => items.Token;

        public INewCommandRunner LegacyNew { get; } = Substitute.For<INewCommandRunner>();

        public NewCommand CreateNew() => new(LegacyNew, Substitute.For<INewCommandTemplateOptionProvider>());

        public InitCommand CreateInit() => new(interaction, new RecordingWorkloadHintRenderer(),
            Provider.GetRequiredService<ILocalSettingsProvider>(), Provider.GetRequiredService<IFunctionsProjectResolver>(),
            Provider.GetRequiredService<IHostJsonBundleSectionReader>(), new InstalledBundleScanner(installed),
            Provider.GetRequiredService<IFileSystem>(), Provider.GetServices<IProjectInitializer>());

        public void Dispose()
        {
            _scope?.Dispose();
            _scope = null;
            _provider = null;
            init.Release();
            items.Release();
        }
    }

    private sealed class RecordingInitRunner(IInitExecutionRunner inner, Action<InitExecutionResult>? afterReturn) : IInitExecutionRunner
    {
        private IInitExecutionRunner? _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        public InitExecutionResult? Result { get; private set; }

        public CancellationToken Token { get; private set; }

        public async Task<InitExecutionResult> ExecuteAsync(
            InitExecutionRequest request, TemplateConstraintEvaluationSnapshot availability, CancellationToken cancellationToken)
        {
            Result = null;
            Token = cancellationToken;
            Result = await (_inner ?? throw new ObjectDisposedException(nameof(RecordingInitRunner))).ExecuteAsync(request, availability, cancellationToken);
            afterReturn?.Invoke(Result);
            return Result;
        }

        public void Release()
        {
            Result = null;
            _inner = null;
            afterReturn = null;
        }
    }

    private sealed class RecordingNewRunner(INewExecutionRunner inner, Action<NewExecutionResult>? afterReturn) : INewExecutionRunner
    {
        private INewExecutionRunner? _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        public NewExecutionResult? Result { get; private set; }

        public CancellationToken Token { get; private set; }

        public async Task<NewExecutionResult> ExecuteAsync(
            NewExecutionRequest request, TemplateConstraintEvaluationSnapshot availability, CancellationToken cancellationToken)
        {
            Result = null;
            Token = cancellationToken;
            Result = await (_inner ?? throw new ObjectDisposedException(nameof(RecordingNewRunner))).ExecuteAsync(request, availability, cancellationToken);
            afterReturn?.Invoke(Result);
            return Result;
        }

        public void Release()
        {
            Result = null;
            _inner = null;
            afterReturn = null;
        }
    }

    private sealed class ConfigurationBoundaryFileSystem(
        CancellationTokenSource? cancellation = null, string? failedPath = null, bool cancelBeforeCommit = false) : PhysicalFileSystem
    {
        public List<string> CommittedConfigurations { get; } = [];

        public override void MoveFile(string sourcePath, string destinationPath, bool overwrite = false)
        {
            bool configuration = Path.GetFileName(destinationPath) == "config.json"
                && Path.GetFileName(Path.GetDirectoryName(destinationPath)) == ".func";
            if (!configuration)
            {
                base.MoveFile(sourcePath, destinationPath, overwrite);
                return;
            }
            if (cancelBeforeCommit && cancellation is not null)
            {
                cancellation.Cancel();
                cancellation.Token.ThrowIfCancellationRequested();
            }
            if (destinationPath == failedPath)
            {
                throw new IOException("controlled configuration failure");
            }
            base.MoveFile(sourcePath, destinationPath, overwrite);
            CommittedConfigurations.Add(destinationPath);
            cancellation?.Cancel();
        }
    }

    private sealed class CancelingNativeFactory(string hive, CancellationTokenSource cancellation, string boundary) : ITemplaterFactory
    {
        public ITemplateCreationResult? NativeResult { get; private set; }

        public Task<Templater> CreateAsync(TemplateEngineContext context, CancellationToken cancellationToken,
            Action<TemplateStoreWaitReason>? waiting = null) => CreateAsync(context, new([]), cancellationToken, waiting);

        public Task<Templater> CreateAsync(TemplateEngineContext context, TemplateConstraintEvaluationSnapshot snapshot,
            CancellationToken cancellationToken, Action<TemplateStoreWaitReason>? waiting = null)
            => Task.FromResult(TemplateFixtures.CreateTemplater(new CancelingNativeSession(context, hive, snapshot, cancellation, boundary,
                result => NativeResult = result)));

        public Task<Templater> CreateProjectAsync(TemplateEngineContext context, TemplateConstraintEvaluationSnapshot snapshot,
            TemplateProjectSupport projectSupport, CancellationToken cancellationToken, Action<TemplateStoreWaitReason>? waiting = null)
            => Task.FromResult(TemplateFixtures.CreateTemplater(new(context, hive, snapshot), projectSupport: projectSupport));
    }

    private sealed class CancelingNativeSession(TemplateEngineContext context, string hive,
        TemplateConstraintEvaluationSnapshot availability,
        CancellationTokenSource cancellation, string boundary, Action<ITemplateCreationResult> observe)
        : TemplateEngineSession(context, hive, availability)
    {
        protected override async Task<ITemplateCreationResult> InstantiateAsync(ITemplateInfo template, TemplateInvocationRequest request,
            InputDataSet parameters, bool dryRun, CancellationToken cancellationToken)
        {
            if (!dryRun && boundary == "throwBeforeCreate")
            {
                await cancellation.CancelAsync();
                cancellationToken.ThrowIfCancellationRequested();
            }
            ITemplateCreationResult result;
            if (!dryRun && boundary is "createFailed" or "nativeCancelled")
            {
                result = Substitute.For<ITemplateCreationResult>();
                result.Status.Returns(boundary == "createFailed" ? CreationResultStatus.CreateFailed : CreationResultStatus.Cancelled);
            }
            else
            {
                result = await base.InstantiateAsync(template, request, parameters, dryRun, cancellationToken);
            }
            if (!dryRun)
            {
                observe(result);
                await cancellation.CancelAsync();
            }
            return result;
        }
    }
}
