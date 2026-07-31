// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Bundles;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Configuration;
using Azure.Functions.Cli.Profiles;
using Azure.Functions.Cli.Projects;
using Azure.Functions.Cli.Templates;
using Azure.Functions.Cli.Templates.Engine;
using Azure.Functions.Cli.Templates.Search;
using Azure.Functions.Cli.Workers;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Azure.Functions.Cli.Tests.Templates;

public class NewCommandRunnerTests
{
    private const string NodeStack = "node";
    private const string NodeLanguage = "javascript";

    [Fact]
    public async Task ExecuteAsync_MissingLanguageOnMultiLanguageStack_RendersErrorExactlyOnce()
    {
        // Repro of https://github.com/Azure/azure-functions-core-tools/issues/5304:
        // when `.func/config.json` is missing `stack.language` on a
        // multi-language stack (dotnet), the missing-language error should
        // surface exactly once for one `func new -t ...` invocation.
        var harness = new RunnerHarness(stack: "dotnet", language: null);

        int exitCode = await harness.Runner.ExecuteAsync(
            Invocation(requestedTemplate: "HttpTrigger"), CancellationToken.None);

        exitCode.Should().Be(1);
        harness.Interaction.Lines.Count(l => l.Contains("Cannot determine language for stack"))
            .Should().Be(1);
    }

    [Fact]
    public async Task ListAsync_MissingLanguageOnMultiLanguageStack_RendersErrorExactlyOnce()
    {
        var harness = new RunnerHarness(stack: "dotnet", language: null);

        int exitCode = await harness.Runner.ListAsync(Invocation(), CancellationToken.None);

        exitCode.Should().Be(1);
        harness.Interaction.Lines.Count(l => l.Contains("Cannot determine language for stack"))
            .Should().Be(1);
    }

    [Fact]
    public async Task ListAsync_RendersCatalogueAndPassesResolvedStackAndLanguage()
    {
        var harness = new RunnerHarness();
        FunctionTemplateInfo http = Template("HttpTrigger", displayName: "HTTP trigger");
        harness.SetTemplates(http);

        int exitCode = await harness.Runner.ListAsync(Invocation(), CancellationToken.None);

        exitCode.Should().Be(0);
        await harness.Catalog.Received(1).ListAsync(
            Arg.Is<TemplateListContext>(c => c.Stack == NodeStack && c.Language == NodeLanguage),
            Arg.Any<CancellationToken>());
        harness.Interaction.AllOutput.Should().Contain("HttpTrigger");
    }

    [Fact]
    public async Task ListAsync_NoTemplates_RendersInstallHint()
    {
        var harness = new RunnerHarness();
        harness.SetTemplates();

        int exitCode = await harness.Runner.ListAsync(Invocation(), CancellationToken.None);

        exitCode.Should().Be(0);
        harness.Interaction.AllOutput.Should().Contain("func new --install");
    }

    [Fact]
    public async Task ExecuteAsync_ResolvesTemplateByLegacyShortNameAlias_AndScaffolds()
    {
        var harness = new RunnerHarness();
        FunctionTemplateInfo http = Template(
            "HttpTrigger",
            shortNames: ["HttpTrigger", "http", "HttpTrigger-TypeScript"]);
        harness.SetTemplates(http);
        harness.SetScaffoldResult(new TemplateApplicationResult.Created(["index.ts"]));

        int exitCode = await harness.Runner.ExecuteAsync(
            Invocation(requestedTemplate: "http", functionName: "MyFunc"), CancellationToken.None);

        exitCode.Should().Be(0);
        await harness.Scaffolder.Received(1).ApplyAsync(
            Arg.Is<NewContext>(c => c.Template.Id == "HttpTrigger" && c.FunctionName == "MyFunc"),
            Arg.Any<System.CommandLine.ParseResult>(),
            Arg.Any<CancellationToken>());
        harness.Interaction.AllOutput.Should().Contain("Created function 'MyFunc'");
    }

    [Fact]
    public async Task ExecuteAsync_UnknownTemplate_RendersUnknownTemplateErrorAndHint()
    {
        var harness = new RunnerHarness();
        harness.SetTemplates(Template("HttpTrigger"));

        int exitCode = await harness.Runner.ExecuteAsync(
            Invocation(requestedTemplate: "DoesNotExist"), CancellationToken.None);

        exitCode.Should().Be(1);
        harness.Interaction.AllOutput.Should().Contain("Template 'DoesNotExist' was not found");
        harness.Interaction.AllOutput.Should().Contain("func new --list");
        await harness.Scaffolder.DidNotReceive().ApplyAsync(
            Arg.Any<NewContext>(), Arg.Any<System.CommandLine.ParseResult>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_ConstraintRestrictedTemplate_ExplicitRequestRendersCallToAction()
    {
        // A constraint-restricted template is hidden from the selectable set,
        // but an explicit request for it must surface the restriction reason
        // and call-to-action (not a bare "unknown template").
        var harness = new RunnerHarness();
        harness.SetTemplates(Template("HttpTrigger"));
        harness.Catalog
            .FindRestrictedAsync(Arg.Any<TemplateListContext>(), "DurableFunctionsOrchestrator", Arg.Any<CancellationToken>())
            .Returns(new RestrictedTemplateInfo(
                Template("DurableFunctionsOrchestrator"),
                "This template requires extension bundle 'X' [4.0.0, ), but this project has version 3.99.0. Update your host.json extensionBundle version to a value within [4.0.0, )."));

        int exitCode = await harness.Runner.ExecuteAsync(
            Invocation(requestedTemplate: "DurableFunctionsOrchestrator"), CancellationToken.None);

        exitCode.Should().Be(1);
        harness.Interaction.AllOutput.Should().Contain("is not available for this project");
        harness.Interaction.AllOutput.Should().Contain("Update your host.json");
        harness.Interaction.AllOutput.Should().NotContain("was not found");
        await harness.Scaffolder.DidNotReceive().ApplyAsync(
            Arg.Any<NewContext>(), Arg.Any<System.CommandLine.ParseResult>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_BundleDeclaredButUnresolvable_HardFailsWithMissingExtensionBundle()
    {
        var harness = new RunnerHarness();
        harness.HostJson.ReadAsync(Arg.Any<DirectoryInfo>(), Arg.Any<CancellationToken>())
            .Returns(new HostJsonBundleSection("Microsoft.Azure.Functions.ExtensionBundle", "[4.*, 5.0.0)"));
        harness.BundleResolver.ResolveAsync(Arg.Any<ExtensionBundleProjectContext>(), Arg.Any<CancellationToken>())
            .Returns(new ExtensionBundleResolution.WorkloadMissing("no bundle installed"));
        harness.SetTemplates(Template("HttpTrigger"));

        int exitCode = await harness.Runner.ExecuteAsync(
            Invocation(requestedTemplate: "HttpTrigger"), CancellationToken.None);

        exitCode.Should().Be(1);
        harness.Interaction.AllOutput.Should().Contain("requires an extension bundle");
        harness.BundleAccessor.Current.Should().BeNull();
        await harness.Scaffolder.DidNotReceive().ApplyAsync(
            Arg.Any<NewContext>(), Arg.Any<System.CommandLine.ParseResult>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_BundleResolves_PublishesConstraintContextBeforeScaffold()
    {
        var harness = new RunnerHarness();
        harness.HostJson.ReadAsync(Arg.Any<DirectoryInfo>(), Arg.Any<CancellationToken>())
            .Returns(new HostJsonBundleSection("Microsoft.Azure.Functions.ExtensionBundle", "[4.*, 5.0.0)"));
        harness.BundleResolver.ResolveAsync(Arg.Any<ExtensionBundleProjectContext>(), Arg.Any<CancellationToken>())
            .Returns(new ExtensionBundleResolution.Resolved(
                "Microsoft.Azure.Functions.ExtensionBundle", "4.20.0", "/hive/bundle", null));
        harness.SetTemplates(Template("HttpTrigger"));
        harness.SetScaffoldResult(new TemplateApplicationResult.Created(["index.js"]));

        int exitCode = await harness.Runner.ExecuteAsync(
            Invocation(requestedTemplate: "HttpTrigger"), CancellationToken.None);

        exitCode.Should().Be(0);
        harness.BundleAccessor.Current.Should().NotBeNull();
        harness.BundleAccessor.Current!.BundleId.Should().Be("Microsoft.Azure.Functions.ExtensionBundle");
        harness.BundleAccessor.Current!.BundleVersion.Should().Be("4.20.0");
    }

    [Fact]
    public async Task ExecuteAsync_ScaffoldReportsModifiedFiles_RendersCreatedAndModified()
    {
        var harness = new RunnerHarness();
        harness.SetTemplates(Template("HttpTrigger"));
        harness.SetScaffoldResult(new TemplateApplicationResult.Created(["index.js"])
        {
            Modified = ["host.json"],
        });

        int exitCode = await harness.Runner.ExecuteAsync(
            Invocation(requestedTemplate: "HttpTrigger"), CancellationToken.None);

        exitCode.Should().Be(0);
        harness.Interaction.AllOutput.Should().Contain("Created:");
        harness.Interaction.AllOutput.Should().Contain("index.js");
        harness.Interaction.AllOutput.Should().Contain("Modified:");
        harness.Interaction.AllOutput.Should().Contain("host.json");
    }

    [Fact]
    public async Task ExecuteAsync_ScaffoldAlreadyExists_RendersForceHintAndFails()
    {
        var harness = new RunnerHarness();
        harness.SetTemplates(Template("HttpTrigger"));
        harness.SetScaffoldResult(new TemplateApplicationResult.AlreadyExists(["index.js"]));

        int exitCode = await harness.Runner.ExecuteAsync(
            Invocation(requestedTemplate: "HttpTrigger"), CancellationToken.None);

        exitCode.Should().Be(1);
        harness.Interaction.AllOutput.Should().Contain("--force");
    }

    [Fact]
    public async Task ExecuteAsync_JsonOutput_EmitsCreatedAndModifiedEnvelope()
    {
        var harness = new RunnerHarness();
        harness.SetTemplates(Template("HttpTrigger"));
        harness.SetScaffoldResult(new TemplateApplicationResult.Created(["index.js"])
        {
            Modified = ["host.json"],
        });

        int exitCode = await harness.Runner.ExecuteAsync(
            Invocation(requestedTemplate: "HttpTrigger", jsonOutput: true), CancellationToken.None);

        exitCode.Should().Be(0);
        string json = harness.Interaction.Lines.Single(l => l.StartsWith("JSON:"));
        json.Should().Contain("\"created\":[\"index.js\"]");
        json.Should().Contain("\"modified\":[\"host.json\"]");
    }

    [Fact]
    public async Task ExecuteAsync_NonInteractiveWithoutTemplate_RendersTemplateRequired()
    {
        var harness = new RunnerHarness();
        harness.SetTemplates(Template("HttpTrigger"));

        int exitCode = await harness.Runner.ExecuteAsync(
            Invocation(requestedTemplate: null, nonInteractive: true), CancellationToken.None);

        exitCode.Should().Be(1);
        harness.Interaction.AllOutput.Should().Contain("--template");
    }

    [Fact]
    public async Task ExecuteAsync_InteractiveWithoutTemplate_PromptsWithPicker()
    {
        var harness = new RunnerHarness(interactive: true);
        harness.SetTemplates(Template("HttpTrigger", displayName: "HTTP trigger"));
        harness.SetScaffoldResult(new TemplateApplicationResult.Created(["index.js"]));

        int exitCode = await harness.Runner.ExecuteAsync(
            Invocation(requestedTemplate: null, nonInteractive: false), CancellationToken.None);

        exitCode.Should().Be(0);
        harness.Interaction.Lines.Should().Contain(l => l.StartsWith("SELECT:"));
    }

    [Fact]
    public async Task HydrateOptionsForTemplateWithIdsAsync_ResolvesByAlias()
    {
        var harness = new RunnerHarness();
        var prompt = new TemplateUserPrompt(
            Id: "authLevel",
            Description: "Auth level",
            DataType: "string",
            DefaultValue: "function",
            Choices: null,
            IsRequired: false,
            ValidatorRegex: null,
            ShortAlias: null,
            LongAlias: null);
        harness.SetTemplates(Template("HttpTrigger", shortNames: ["HttpTrigger", "http"], prompts: [prompt]));

        IReadOnlyList<HydratedTemplateOption>? hydrated =
            await harness.Runner.HydrateOptionsForTemplateWithIdsAsync(
                Invocation(requestedTemplate: "http"), "http", CancellationToken.None);

        hydrated.Should().NotBeNull();
        hydrated!.Select(h => h.PromptId).Should().Contain("authLevel");
    }

    [Fact]
    public async Task InstallPackageAsync_BypassesProjectGate_AndRendersResult()
    {
        var harness = new RunnerHarness(projectResolved: false);
        var package = new InstalledTemplatePackage("Microsoft.Azure.Functions.Templates.Node", "4.0.0", "feed", null);
        harness.Packages.InstallAsync(Arg.Any<TemplatePackageInstallRequest>(), Arg.Any<CancellationToken>())
            .Returns(new TemplatePackageInstallResult.Installed(package));

        int exitCode = await harness.Runner.InstallPackageAsync(
            new TemplatePackageInstallRequest("Microsoft.Azure.Functions.Templates.Node", "4.0.0", "feed"),
            CancellationToken.None);

        exitCode.Should().Be(0);
        harness.Interaction.AllOutput.Should().Contain("Installed template package");
        await harness.ProjectResolver.DidNotReceive()
            .ResolveProjectAsync(Arg.Any<ProjectResolutionContext>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task InstallPackageAsync_NotFound_ReturnsNonZero()
    {
        var harness = new RunnerHarness();
        harness.Packages.InstallAsync(Arg.Any<TemplatePackageInstallRequest>(), Arg.Any<CancellationToken>())
            .Returns(new TemplatePackageInstallResult.NotFound("Missing.Pkg", "9.9.9"));

        int exitCode = await harness.Runner.InstallPackageAsync(
            new TemplatePackageInstallRequest("Missing.Pkg", "9.9.9"), CancellationToken.None);

        exitCode.Should().Be(1);
        harness.Interaction.AllOutput.Should().Contain("No template package 'Missing.Pkg'");
    }

    [Fact]
    public async Task UninstallPackageAsync_NotInstalled_IsIdempotentSuccess()
    {
        var harness = new RunnerHarness();
        harness.Packages.UninstallAsync("Some.Pkg", Arg.Any<CancellationToken>())
            .Returns(new TemplatePackageUninstallResult.NotInstalled("Some.Pkg"));

        int exitCode = await harness.Runner.UninstallPackageAsync("Some.Pkg", CancellationToken.None);

        exitCode.Should().Be(0);
        harness.Interaction.AllOutput.Should().Contain("nothing to do");
    }

    [Fact]
    public async Task UpdatePackagesAsync_NoUpdates_ReturnsZero()
    {
        var harness = new RunnerHarness();
        harness.Packages.UpdateAsync(Arg.Any<TemplatePackageUpdateRequest>(), Arg.Any<CancellationToken>())
            .Returns(new TemplatePackageUpdateResult.NoUpdatesAvailable());

        int exitCode = await harness.Runner.UpdatePackagesAsync(
            new TemplatePackageUpdateRequest(All: true), CancellationToken.None);

        exitCode.Should().Be(0);
        harness.Interaction.AllOutput.Should().Contain("up to date");
    }

    [Fact]
    public async Task UpdatePackagesAsync_Updated_RendersVersionChanges()
    {
        var harness = new RunnerHarness();
        harness.Packages.UpdateAsync(Arg.Any<TemplatePackageUpdateRequest>(), Arg.Any<CancellationToken>())
            .Returns(new TemplatePackageUpdateResult.Updated(
                [new TemplatePackageUpdate("Pkg", "1.0.0", "2.0.0")]));

        int exitCode = await harness.Runner.UpdatePackagesAsync(
            new TemplatePackageUpdateRequest(All: true), CancellationToken.None);

        exitCode.Should().Be(0);
        harness.Interaction.AllOutput.Should().Contain("1.0.0 -> 2.0.0");
    }

    [Fact]
    public async Task SearchTemplatesAsync_RendersResultsFromSearchService()
    {
        var harness = new RunnerHarness();
        harness.Search
            .SearchAsync(Arg.Any<FuncSearchRequest>(), Arg.Any<CancellationToken>())
            .Returns(new FuncSearchResults(
                "queue",
                Source: null,
                [
                    new FuncSearchPackageResult(
                        "Contoso.Templates",
                        "1.0.0",
                        [new FuncSearchTemplateResult("Queue trigger", ["queue"], "node", "javascript")],
                        new FuncTemplateInstalledState.NotInstalled()),
                ]));

        int exitCode = await harness.Runner.SearchTemplatesAsync("queue", source: null, CancellationToken.None);

        exitCode.Should().Be(0);
        harness.Interaction.AllOutput.Should().Contain("Contoso.Templates");
    }

    [Fact]
    public async Task SearchTemplatesAsync_WrapsUnreachableIndexAsGracefulUserError()
    {
        var harness = new RunnerHarness();
        harness.Search
            .SearchAsync(Arg.Any<FuncSearchRequest>(), Arg.Any<CancellationToken>())
            .Returns<FuncSearchResults>(_ => throw new InvalidOperationException("Unable to download the template search index."));

        Func<Task> act = () => harness.Runner.SearchTemplatesAsync("queue", source: null, CancellationToken.None);

        (await act.Should().ThrowAsync<GracefulException>())
            .Which.IsUserError.Should().BeTrue();
    }

    [Fact]
    public async Task SearchTemplatesAsync_WithNoProject_BypassesGateAndRenders()
    {
        // D30: search must work in an empty directory with no Functions project resolved.
        var harness = new RunnerHarness(projectResolved: false);
        harness.Search
            .SearchAsync(Arg.Any<FuncSearchRequest>(), Arg.Any<CancellationToken>())
            .Returns(new FuncSearchResults(
                Term: null,
                Source: null,
                [
                    new FuncSearchPackageResult(
                        "Contoso.Templates",
                        "1.0.0",
                        [new FuncSearchTemplateResult("Queue trigger", ["queue"], "node", "javascript")],
                        new FuncTemplateInstalledState.NotInstalled()),
                ]));

        int exitCode = await harness.Runner.SearchTemplatesAsync(term: null, source: null, CancellationToken.None);

        exitCode.Should().Be(0);
        harness.Interaction.AllOutput.Should().Contain("Contoso.Templates");
        await harness.ProjectResolver.DidNotReceiveWithAnyArgs()
            .ResolveProjectAsync(default!, default);
    }

    private static NewInvocation Invocation(
        string? requestedTemplate = null,
        string? functionName = null,
        bool nonInteractive = false,
        bool jsonOutput = false)
        => new(
            WorkingDirectory: new WorkingDirectory(new DirectoryInfo(Path.GetTempPath()), WasExplicit: false),
            RequestedTemplate: requestedTemplate,
            RequestedFunctionName: functionName,
            Force: false,
            NonInteractive: nonInteractive,
            JsonOutput: jsonOutput);

    private static FunctionTemplateInfo Template(
        string id,
        string stack = NodeStack,
        string? displayName = null,
        IReadOnlyList<string>? shortNames = null,
        IReadOnlyList<TemplateUserPrompt>? prompts = null)
        => new(
            Id: id,
            Stack: stack,
            DisplayName: displayName ?? id,
            Description: null,
            DefaultFunctionName: id,
            Languages: [NodeLanguage],
            Metadata: new TemplateMetadata(prompts ?? [], RequiresExtensionBundle: false, MinBundleVersion: null))
        {
            ShortNames = shortNames ?? [id],
        };

    private sealed class RunnerHarness
    {
        public RunnerHarness(
            string stack = NodeStack,
            string? language = NodeLanguage,
            bool interactive = false,
            bool projectResolved = true)
        {
            Interaction = interactive ? new InteractiveTestInteractionService() : new TestInteractionService();

            ProjectResolver = Substitute.For<IFunctionsProjectResolver>();
            WorkingDirectory wd = new(new DirectoryInfo(Path.GetTempPath()), WasExplicit: false);
            ProjectResolver
                .ResolveProjectAsync(Arg.Any<ProjectResolutionContext>(), Arg.Any<CancellationToken>())
                .Returns(projectResolved
                    ? ProjectResolutionResults.Resolved(new FakeProject(wd, stack), "fake")
                    : ProjectResolutionResults.NotResolved("no project"));

            IProfileResolver profileResolver = Substitute.For<IProfileResolver>();

            IOptionsMonitor<StackOptions> stackOptions = Substitute.For<IOptionsMonitor<StackOptions>>();
            stackOptions.Get(Arg.Any<string>()).Returns(new StackOptions { Runtime = stack, Language = language });

            Catalog = Substitute.For<IFuncTemplateCatalog>();
            Catalog.ListAsync(Arg.Any<TemplateListContext>(), Arg.Any<CancellationToken>())
                .Returns(Array.Empty<FunctionTemplateInfo>());
            Scaffolder = Substitute.For<IFuncTemplateScaffolder>();
            Packages = Substitute.For<IFuncTemplatePackageService>();
            BundleAccessor = new FakeBundleContextAccessor();
            HostJson = Substitute.For<IHostJsonBundleSectionReader>();
            BundleResolver = Substitute.For<IExtensionBundleResolver>();
            Search = Substitute.For<IFuncTemplateSearchService>();

            Runner = new NewCommandRunner(
                Interaction,
                ProjectResolver,
                profileResolver,
                stackOptions,
                projectInitializers: Array.Empty<IProjectInitializer>(),
                new TemplateOptionHydrator(Array.Empty<IProjectInitializer>()),
                new TemplatePicker(Interaction),
                new NewCommandRenderer(Interaction),
                Catalog,
                Scaffolder,
                Packages,
                BundleAccessor,
                HostJson,
                BundleResolver,
                Search);
        }

        public NewCommandRunner Runner { get; }

        public TestInteractionService Interaction { get; }

        public IFunctionsProjectResolver ProjectResolver { get; }

        public IFuncTemplateCatalog Catalog { get; }

        public IFuncTemplateScaffolder Scaffolder { get; }

        public IFuncTemplatePackageService Packages { get; }

        public IFuncExtensionBundleContextAccessor BundleAccessor { get; }

        public IHostJsonBundleSectionReader HostJson { get; }

        public IExtensionBundleResolver BundleResolver { get; }

        public IFuncTemplateSearchService Search { get; }

        public void SetTemplates(params FunctionTemplateInfo[] templates)
            => Catalog.ListAsync(Arg.Any<TemplateListContext>(), Arg.Any<CancellationToken>())
                .Returns(templates);

        public void SetScaffoldResult(TemplateApplicationResult result)
            => Scaffolder.ApplyAsync(
                    Arg.Any<NewContext>(), Arg.Any<System.CommandLine.ParseResult>(), Arg.Any<CancellationToken>())
                .Returns(result);
    }

    private sealed class InteractiveTestInteractionService : TestInteractionService
    {
        public override bool IsInteractive => true;
    }

    private sealed class FakeBundleContextAccessor : IFuncExtensionBundleContextAccessor
    {
        public FuncExtensionBundleContext? Current { get; set; }
    }

    private sealed class FakeProject(WorkingDirectory workingDirectory, string stack) : FunctionsProject
    {
        private readonly WorkingDirectory _workingDirectory = workingDirectory;
        private readonly string _stack = stack;
        private readonly FunctionsWorkerReference _workerReference =
            FunctionsWorkerReference.FromWorkerInfo(".NET", "dotnet-isolated", "worker.config.json", "1.0.0");

        public override WorkingDirectory WorkingDirectory => _workingDirectory;

        public override string StackName => _stack;

        public override string StackDisplayName => _stack;

        public override bool SupportsExtensionBundles => !string.Equals(_stack, "dotnet", StringComparison.OrdinalIgnoreCase);

        public override FunctionsWorkerReference WorkerReference => _workerReference;
    }
}
