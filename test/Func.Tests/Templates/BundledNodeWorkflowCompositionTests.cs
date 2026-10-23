// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Text.Json;
using Azure.Functions.Cli.Bundles;
using Azure.Functions.Cli.Commands;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Configuration;
using Azure.Functions.Cli.Console;
using Azure.Functions.Cli.Hosting;
using Azure.Functions.Cli.Profiles;
using Azure.Functions.Cli.Projects;
using Azure.Functions.Cli.Templates;
using Azure.Functions.Cli.Templates.Acquisition;
using Azure.Functions.Cli.Templates.Engine;
using Azure.Functions.Cli.Workloads;
using Azure.Functions.Cli.Workloads.Install;
using Azure.Functions.Cli.Workloads.Node;
using Azure.Functions.Cli.Workloads.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Azure.Functions.Cli.Tests.Templates;

public sealed partial class BundledNodeTemplateTests
{
    [Theory]
    [InlineData("js", "JavaScript", ".js")]
    [InlineData("ts", "TypeScript", ".ts")]
    public async Task Composition_ProductionFactoryAndRunners_ApplyNodeProvidersWithoutRequestCapabilities(
        string language, string canonicalLanguage, string extension)
    {
        await InstallOwnedNodeJourneyAsync();
        var npm = Substitute.For<NodeNpmInstaller>();
        using var services = await CreateNodeWorkflowServicesAsync(npm);
        using var scope = services.CreateScope();
        var composer = scope.ServiceProvider.GetRequiredService<NativeTemplateWorkflowComposer>();
        var availability = scope.ServiceProvider.GetRequiredService<INativeTemplateAvailability>();
        availability.Should().BeOfType<NativeTemplateAvailability>();
        var captured = await availability.CaptureAsync(CancellationToken.None);
        captured.Workloads.Should().ContainSingle().Which.Aliases.Should().Contain("node");
        captured.Workloads.Single().IsUsable.Should().BeTrue();
        captured.Workloads.Single().PackageId.Should().Be((await _workloads.GetWorkloadsAsync()).Single().PackageId);
        scope.ServiceProvider.GetRequiredService<INewExecutionContextResolver>().Should().BeOfType<NativeTemplateContextResolver>();
        string project = Path.Combine(_root, "composed-project");
        var init = NodeComposedInit(project, language, ["--skip-npm-install", "true"]);
        init.ProjectPreparation.Should().BeNull();

        var preview = await composer.ExecuteProjectAsync(init with { DryRun = true }, CancellationToken.None);
        Directory.Exists(project).Should().BeFalse();
        var created = await composer.ExecuteProjectAsync(init, CancellationToken.None);

        preview.Succeeded.Should().BeTrue(string.Join("; ", preview.Diagnostics));
        created.Succeeded.Should().BeTrue(string.Join("; ", created.Diagnostics));
        created.Preparation!.Preview.Operations.Should().BeEmpty();
        created.Preparation.Preview.OwnedPlan!.Skipped.Should().BeTrue();
        created.Preparation.Completion!.OwnedCompletion!.Succeeded.Should().BeTrue();
        created.Preparation.Completion!.Diagnostics.Should().Contain("Skipped npm installation.");
        await npm.DidNotReceive().InstallRequiredAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        var registered = scope.ServiceProvider.GetRequiredService<WorkloadNativeTemplatePolicies>();
        scope.ServiceProvider.GetRequiredService<WorkloadNativeTemplatePolicies>().Should().BeSameAs(registered);
        scope.ServiceProvider.GetServices<INativeItemWorkflowProvider>().Should().NotContain(provider => provider.Stack == "node");
        scope.ServiceProvider.GetServices<INativeProjectWorkflowProvider>().Should().NotContain(provider => provider.Stack == "node");
        created.Preview!.ProjectPreparationCapture!.Owner.Capability.GetType().Assembly.Should()
            .BeSameAs(scope.ServiceProvider.GetServices<WorkloadNativeTemplatePolicyRegistration>().Single(value => value.Policy.Stack == "node")
                .Policy.GetType().Assembly);
        using (var config = JsonDocument.Parse(File.ReadAllText(Path.Combine(project, ".func", "config.json"))))
        {
            config.RootElement.GetProperty("stack").GetProperty("language").GetString().Should().Be(canonicalLanguage);
        }

        string target = Path.Combine(project, "src", "functions");
        string userFile = Path.Combine(target, "existing-user" + extension);
        File.WriteAllText(userFile, "// preserve user source");
        var item = NodeComposedItem(target, "HTTP trigger");
        item.ItemCompatibility.Should().BeNull();
        item.SelectedItemEffects.Should().BeNull();

        var itemPreview = await composer.ExecuteItemAsync(item with { DryRun = true }, CancellationToken.None);
        File.Exists(Path.Combine(target, "httpTrigger" + extension)).Should().BeFalse();
        var itemCreated = await composer.ExecuteItemAsync(item, CancellationToken.None);
        var invalid = await composer.ExecuteItemAsync(item with { Name = "Invalid-Original" }, CancellationToken.None);
        var explicitName = await composer.ExecuteItemAsync(item with { Name = "ExplicitOriginal" }, CancellationToken.None);

        itemPreview.Succeeded.Should().BeTrue(string.Join("; ", itemPreview.Diagnostics));
        itemCreated.Succeeded.Should().BeTrue(string.Join("; ", itemCreated.Diagnostics));
        itemCreated.ItemName!.Name.Should().Be("httpTrigger");
        itemCreated.ItemName.Origin.Should().Be("authoredDefault");
        itemCreated.Invocation!.Changes.Should().BeEquivalentTo(itemPreview.Invocation!.Changes);
        File.ReadAllText(Path.Combine(target, "httpTrigger" + extension)).Should().Contain("app.http('httpTrigger'");
        invalid.Kind.Should().Be("invalidLegacyInput");
        invalid.ItemName!.Name.Should().Be("Invalid-Original");
        invalid.Invocation.Should().BeNull();
        explicitName.ItemName!.Origin.Should().Be("explicit");
        File.ReadAllText(Path.Combine(target, "ExplicitOriginal" + extension)).Should().Contain("app.http('ExplicitOriginal'");
        File.ReadAllText(userFile).Should().Be("// preserve user source");
    }

    [Theory]
    [InlineData(0, "controlled npm warning", "created")]
    [InlineData(7, "controlled npm failure", "partialPreparation")]
    public async Task Composition_ProductionPreparation_UsesOwnedHelperAfterConfigurationAndPreservesStderr(
        int exitCode, string stderr, string kind)
    {
        await InstallOwnedNodeJourneyAsync();
        string project = Path.Combine(_root, "composed-preparation");
        var npm = Substitute.For<NodeNpmInstaller>();
        npm.InstallRequiredAsync(project, Arg.Any<CancellationToken>()).Returns(_ =>
        {
            File.Exists(Path.Combine(project, ".func", "config.json")).Should().BeTrue();
            File.Exists(Path.Combine(project, "package.json")).Should().BeTrue();
            return (exitCode, stderr);
        });
        using var services = await CreateNodeWorkflowServicesAsync(npm);
        using var scope = services.CreateScope();
        var composer = scope.ServiceProvider.GetRequiredService<NativeTemplateWorkflowComposer>();
        var request = NodeComposedInit(project, "ts", []);

        var preview = await composer.ExecuteProjectAsync(request with { DryRun = true }, CancellationToken.None);
        preview.Succeeded.Should().BeTrue(string.Join("; ", preview.Diagnostics));
        await npm.DidNotReceive().InstallRequiredAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        var result = await composer.ExecuteProjectAsync(request, CancellationToken.None);

        preview.Preparation!.Preview.Operations.Should().BeEmpty();
        preview.Preparation.Preview.OwnedPlan!.Skipped.Should().BeFalse();
        result.Kind.Should().Be(kind);
        result.Succeeded.Should().Be(exitCode == 0);
        result.Preparation!.Completion!.Diagnostics.Should().Contain(stderr);
        File.Exists(Path.Combine(project, "host.json")).Should().BeTrue();
        File.Exists(Path.Combine(project, ".func", "config.json")).Should().BeTrue();
        await npm.Received(1).InstallRequiredAsync(project, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Composition_ProductionCancellation_RetainsOutputsWarnsAndPropagatesSameException()
    {
        await InstallOwnedNodeJourneyAsync();
        using var cancellation = new CancellationTokenSource();
        string project = Path.Combine(_root, "composed-canceled");
        var original = new OperationCanceledException(cancellation.Token);
        var npm = Substitute.For<NodeNpmInstaller>();
        npm.InstallRequiredAsync(project, Arg.Any<CancellationToken>()).Returns(_ =>
        {
            File.Exists(Path.Combine(project, ".func", "config.json")).Should().BeTrue();
            cancellation.Cancel();
            return Task.FromException<(int ExitCode, string Stderr)>(original);
        });
        using var services = await CreateNodeWorkflowServicesAsync(npm);
        using var scope = services.CreateScope();
        var composer = scope.ServiceProvider.GetRequiredService<NativeTemplateWorkflowComposer>();

        Func<Task> invoke = () => composer.ExecuteProjectAsync(NodeComposedInit(project, "js", []), cancellation.Token);
        var thrown = (await invoke.Should().ThrowAsync<OperationCanceledException>()).Which;

        thrown.Should().BeSameAs(original);
        File.Exists(Path.Combine(project, "package.json")).Should().BeTrue();
        File.Exists(Path.Combine(project, ".func", "config.json")).Should().BeTrue();
        _interaction.AllOutput.Should().Contain("Native project files and CLI configuration were created before stack preparation was canceled.")
            .And.NotContain("SUCCESS:");
    }

    [Fact]
    public async Task Composition_ProductionItemSelection_UsesActualBundleCapabilitiesAndHiddenPolicy()
    {
        await InstallOwnedNodeJourneyAsync();
        using var services = await CreateNodeWorkflowServicesAsync(Substitute.For<NodeNpmInstaller>());
        using var scope = services.CreateScope();
        var composer = scope.ServiceProvider.GetRequiredService<NativeTemplateWorkflowComposer>();
        string project = Path.Combine(_root, "composed-bindings");
        (await composer.ExecuteProjectAsync(NodeComposedInit(project, "ts", ["--skip-npm-install", "true"]), CancellationToken.None)).Succeeded.Should().BeTrue();
        string target = Path.Combine(project, "src", "functions");
        string metadata = Path.Combine(_root, "composed-bundle", "tools", "any", "bin", "extensions.json");

        var listed = await composer.ExecuteItemAsync(NodeComposedItem(target, null) with { List = true }, CancellationToken.None);
        listed.Templates.Should().NotContain(template => template.IsHidden);
        var hidden = await composer.ExecuteItemAsync(NodeComposedItem(target, "DurableFunctionsEntity-TypeScript") with { DryRun = true }, CancellationToken.None);
        hidden.Succeeded.Should().BeTrue(string.Join("; ", hidden.Diagnostics));
        hidden.SelectedTemplate!.IsHidden.Should().BeTrue();
        hidden.ItemName!.Name.Should().Be("counter");

        File.WriteAllText(metadata, """{"extensions":[{"bindings":["blobTrigger"]}]}""");
        var missing = await composer.ExecuteItemAsync(NodeComposedItem(target, "EventGridBlobTrigger-TypeScript") with { DryRun = true }, CancellationToken.None);
        missing.Kind.Should().Be("Unavailable");
        File.WriteAllText(metadata, """{"extensions":[{"bindings":["BLOBTRIGGER","eventgridtrigger"]}]}""");
        var all = await composer.ExecuteItemAsync(NodeComposedItem(target, "EventGridBlobTrigger-TypeScript") with { DryRun = true }, CancellationToken.None);
        all.Succeeded.Should().BeTrue(string.Join("; ", all.Diagnostics));
        all.ItemName!.Name.Should().Be("eventGridBlobTrigger");
        Directory.GetFiles(target).Should().BeEmpty();
    }

    [Fact]
    public async Task Composition_FactoryCreatedCommandAdapter_PreservesExistingFlagsAndSelectsNodeProviders()
    {
        await InstallOwnedNodeJourneyAsync();
        var npm = Substitute.For<NodeNpmInstaller>();
        using var services = await CreateNodeWorkflowServicesAsync(npm);
        using var scope = services.CreateScope();
        var composer = scope.ServiceProvider.GetRequiredService<NativeTemplateWorkflowComposer>();
        var adapter = await composer.CreateCommandAdapterAsync(CancellationToken.None);
        string project = Path.Combine(_root, "factory-command");
        var init = new InitCommand(_interaction, new RecordingWorkloadHintRenderer(),
            services.GetRequiredService<ILocalSettingsProvider>(), services.GetRequiredService<IFunctionsProjectResolver>(),
            services.GetRequiredService<IHostJsonBundleSectionReader>(), new InstalledBundleScanner(services.GetRequiredService<IInstalledBundleWorkloads>()),
            _files, services.GetServices<IProjectInitializer>());

        int initialized = await InvokeNodeBridgeAsync(adapter.ParseInitInvocation(init,
            [project, "--stack", "node", "--language", "js", "--skip-npm-install"],
            new("basic", NonInteractive: true, JsonOutput: true)));
        var legacy = Substitute.For<INewCommandRunner>();
        var command = new NewCommand(legacy, Substitute.For<INewCommandTemplateOptionProvider>());
        int created = await InvokeNodeBridgeAsync(adapter.ParseNewInvocation(command,
            [Path.Combine(project, "src", "functions"), "--template", "HttpTrigger-JavaScript", "--non-interactive", "--format", "json"]));

        initialized.Should().Be(0, _interaction.AllOutput);
        created.Should().Be(0, _interaction.AllOutput);
        File.ReadAllText(Path.Combine(project, "src", "functions", "httpTrigger.js")).Should().Contain("app.http('httpTrigger'");
        File.Exists(Path.Combine(project, "src", "functions", "functions.js")).Should().BeFalse();
        _interaction.AllOutput.Should().Contain("Skipped npm installation.");
        await npm.DidNotReceive().InstallRequiredAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        legacy.ReceivedCalls().Should().BeEmpty();
        using var second = services.CreateScope();
        second.ServiceProvider.GetRequiredService<WorkloadNativeTemplatePolicies>()
            .Should().NotBeSameAs(scope.ServiceProvider.GetRequiredService<WorkloadNativeTemplatePolicies>());
    }

    private static InitExecutionRequest NodeComposedInit(string project, string language, IReadOnlyList<string> tokens) =>
        new(WorkingDirectory.FromExplicit(project), "basic", null, "node", language, false, true, false, false, tokens, new HashSet<string>());

    private static NewExecutionRequest NodeComposedItem(string target, string? reference) =>
        new(WorkingDirectory.FromExplicit(target), reference, null, null, false, true, false, false, false, [], new HashSet<string>());

    private async Task<ServiceProvider> CreateNodeWorkflowServicesAsync(NodeNpmInstaller npm)
    {
        ServiceCollection services = new();
        services.AddLogging();
        services.AddSingleton<IInteractionService>(_interaction);
        services.AddSingleton<IFileSystem>(_files);
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddSingleton(new CliConfigurationPathsOptions(Home));
        services.AddSingleton<ICliVersionProvider>(AssemblyCliVersionProvider.Instance);
        services.AddSingleton<IWorkloadStore>(_workloads);
        services.AddSingleton<IWorkloadPaths>(_paths);
        services.AddSingleton<IBundledTemplateInstaller>(new BundledTemplateInstaller(_templates, _inspector, _files, new(Home)));
        services.AddSingleton<IWorkloadDeploymentService>(AdmissionDeployment(new FileWorkloadOperationCoordinator(_paths)));
        services.AddSingleton<IBasicCompanionCatalog>(_ownedJourneyCatalog
            ?? throw new InvalidOperationException("Install the qualified Node function association before composing its services."));
        services.AddSingleton<BasicAcquisitionPlanner>();
        services.AddSingleton<ILocalSettingsProvider, LocalSettingsProvider>();
        services.AddSingleton<ICliConfigurationProvider, CliConfigurationProvider>();
        services.AddOptions();
        services.AddSingleton<IConfigureOptions<StackOptions>, StackOptionsSetup>();
        services.AddSingleton<IConfigureOptions<ProjectProfileOptions>, ProjectProfileOptionsSetup>();
        services.AddSingleton<ProfileDocumentParser>();
        services.AddSingleton<IProfileSource, BuiltInProfileSource>();
        services.AddSingleton<IProfileCatalog, ProfileCatalog>();
        services.AddSingleton<IProfileResolver, ProfileResolver>();
        var capture = new NativeTemplateWorkloadCapture();
        await WorkloadRegistration.RegisterWorkloadsAsync(services, _paths, _interaction, capture, CancellationToken.None);
        var loaded = services.Select(service => service.ImplementationInstance).OfType<RuntimeWorkloadInfo>().ToArray();
        _loadContexts.AddRange(loaded.Select(workload => workload.LoadContext));
        services.AddSingleton<IFunctionsProjectResolver, FunctionsProjectResolver>();
        services.AddSingleton<IHostJsonBundleSectionReader, HostJsonBundleSectionReader>();
        string bundleRoot = Path.Combine(_root, "composed-bundle");
        Directory.CreateDirectory(Path.Combine(bundleRoot, "tools", "any", "bin"));
        File.WriteAllText(Path.Combine(bundleRoot, "tools", "any", "bin", "extensions.json"), NodeBridgeExtensions);
        var bundles = Substitute.For<IInstalledBundleWorkloads>();
        bundles.ListInstalledAsync(Arg.Any<CancellationToken>()).Returns([new InstalledBundleWorkload("4.35.0", bundleRoot)]);
        services.AddSingleton(bundles);
        services.AddSingleton<IExtensionBundleResolver>(new ExtensionBundleResolver(new InstalledBundleScanner(bundles),
            NullBundleResolveTelemetry.Instance, NullLogger<ExtensionBundleResolver>.Instance));
        services.AddTemplatesOrchestrator();
        services.AddSingleton<InstalledProjectStackCatalog>();
        services.AddSingleton<ITemplateStore>(_templates);
        ConfigureNodePreparationHelper(services, npm.InstallRequiredAsync);
        services.AddSingleton(capture);
        services.AddScoped<INativeTemplateAvailability, NativeTemplateAvailability>();
        services.AddSingleton(new TemplateEngineBundleContextReader(_files));
        services.AddScoped<INewExecutionContextResolver>(provider => new NativeTemplateContextResolver(
            _files, provider.GetRequiredService<IFunctionsProjectResolver>(), provider.GetRequiredService<IProfileResolver>(),
            provider.GetRequiredService<IOptionsMonitor<StackOptions>>(), provider.GetRequiredService<InstalledProjectStackCatalog>(),
            provider.GetRequiredService<IHostJsonBundleSectionReader>(), provider.GetRequiredService<IExtensionBundleResolver>(),
            provider.GetRequiredService<TemplateEngineBundleContextReader>(), enableBundleBindingConstraints: true));
        services.AddNativeTemplateWorkflowComposition();
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
    }
}
