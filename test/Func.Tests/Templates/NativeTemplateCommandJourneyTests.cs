// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.CommandLine;
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
using Azure.Functions.Cli.Templates.Engine;
using Azure.Functions.Cli.Workloads.Node;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using NuGet.Versioning;

namespace Azure.Functions.Cli.Tests.Templates;

public sealed class NativeTemplateCommandJourneyTests : IDisposable
{
    private const string LegacySource = """{"trigger-functionName":{"validator":"^[a-zA-Z][a-zA-Z0-9_]{0,126}[a-zA-Z0-9]$"}}""";
    private readonly string _root = Path.Combine(Path.GetTempPath(), "func-native-adapter-" + Guid.NewGuid().ToString("N"));
    private readonly TestInteractionService _interaction = new();
    private readonly List<ServiceProvider> _providers = [];

    private string Home => Path.Combine(_root, "home");
    private string Project => Path.Combine(_root, "project");
    private string Target => Path.Combine(Project, "nested");
    private string LegacySourcePath => Path.Combine(_root, "package", "legacy-source.json");

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

    [Fact]
    public async Task ProjectPreparation_NonzeroOwnedOutcome_PreservesCreatedProjectWithoutSuccess()
    {
        await InstallAsync();
        var preparation = Substitute.For<INativeProjectPreparation>();
        preparation.PreviewAsync(Arg.Any<NativeProjectPreparationContext>(), Arg.Any<CancellationToken>())
            .Returns(new NativeProjectPreparationPreview(true, [new(NativeProjectPreparationOperation.NodeNpmInstall, false)], []));
        preparation.CompleteAsync(Arg.Any<NativeProjectPreparationContext>(), Arg.Any<NativeProjectPreparationPreview>(), Arg.Any<CancellationToken>())
            .Returns(new NativeProjectPreparationResult(false, ["Owned preparation exited 7: original stderr"]));
        using var harness = CreateHarness(preparation: preparation);

        int exit = await InvokeAsync(harness.Adapter.ParseInitInvocation(harness.CreateInit(), [Project, "-s", "node", "-l", "js"],
            new("controlled-project", NonInteractive: true, JsonOutput: true)));

        exit.Should().Be(1);
        AssertJsonKind("partialPreparation");
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

    private Harness CreateHarness(bool itemCompatibility = false, INativeProjectPreparation? preparation = null)
    {
        ServiceCollection services = new();
        services.AddLogging();
        services.AddSingleton<Azure.Functions.Cli.Console.IInteractionService>(_interaction);
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
        services.AddSingleton<InstalledProjectStackCatalog>();
        services.AddSingleton<IFunctionsProjectResolver, FunctionsProjectResolver>();
        services.AddSingleton<IHostJsonBundleSectionReader, HostJsonBundleSectionReader>();
        var installed = Substitute.For<IInstalledBundleWorkloads>();
        installed.ListInstalledAsync(Arg.Any<CancellationToken>()).Returns([]);
        services.AddSingleton<IExtensionBundleResolver>(new ExtensionBundleResolver(
            new InstalledBundleScanner(installed), NullBundleResolveTelemetry.Instance, NullLogger<ExtensionBundleResolver>.Instance));
        services.AddTemplatesOrchestrator();
        services.AddSingleton<TemplateProjectSupport>();
        var provider = services.BuildServiceProvider();
        _providers.Add(provider);
        var scope = provider.CreateScope();
        var adapter = new NativeTemplateCommandAdapter(scope.ServiceProvider.GetRequiredService<IInitExecutionRunner>(),
            provider.GetRequiredService<INewExecutionRunner>(), provider.GetRequiredService<InitExecutionRenderer>(),
            provider.GetRequiredService<NewExecutionRenderer>(),
            new([new("Controlled.Node", NuGetVersion.Parse("1.0.0"), ["node"], true)]),
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
        return new(provider, scope, adapter, _interaction, installed);
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
        TestInteractionService interaction, IInstalledBundleWorkloads installed) : IDisposable
    {
        public NativeTemplateCommandAdapter Adapter { get; } = adapter;

        public INewCommandRunner LegacyNew { get; } = Substitute.For<INewCommandRunner>();

        public NewCommand CreateNew() => new(LegacyNew, Substitute.For<INewCommandTemplateOptionProvider>());

        public InitCommand CreateInit() => new(interaction, new RecordingWorkloadHintRenderer(),
            provider.GetRequiredService<ILocalSettingsProvider>(), provider.GetRequiredService<IFunctionsProjectResolver>(),
            provider.GetRequiredService<IHostJsonBundleSectionReader>(), new InstalledBundleScanner(installed),
            provider.GetRequiredService<IFileSystem>(), provider.GetServices<IProjectInitializer>());

        public void Dispose() => scope.Dispose();
    }
}
