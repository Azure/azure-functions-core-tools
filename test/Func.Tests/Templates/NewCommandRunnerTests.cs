// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Bundles;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Configuration;
using Azure.Functions.Cli.Profiles;
using Azure.Functions.Cli.Projects;
using Azure.Functions.Cli.Templates;
using Azure.Functions.Cli.Workers;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Azure.Functions.Cli.Tests.Templates;

public class NewCommandRunnerTests
{
    [Fact]
    public async Task ExecuteAsync_Success_InvokesPipelineInOrderAndForwardsCancellationToken()
    {
        List<string> calls = [];
        using CancellationTokenSource cancellation = new();
        WorkingDirectory workingDirectory = new(new DirectoryInfo(Path.GetTempPath()), WasExplicit: false);
        NewInvocation invocation = new(
            workingDirectory,
            RequestedTemplate: "HttpTrigger",
            RequestedFunctionName: "MyFunction",
            Force: false,
            NonInteractive: true);
        NewCommandResolvedContext context = new(
            workingDirectory,
            "node",
            "javascript",
            new InstalledTemplatesWorkload("node", "1.0.0", "install"),
            BundleId: null,
            BundleChannel.Unknown,
            UsedStableFallback: false);
        FunctionTemplateInfo template = new(
            "HttpTrigger",
            "node",
            EngineIds.V2,
            "HTTP trigger",
            Description: null,
            DefaultFunctionName: "HttpTrigger",
            Languages: ["javascript"],
            new TemplateMetadata([], RequiresExtensionBundle: false, MinBundleVersion: null));
        TemplateApplicationResult applyResult = new TemplateApplicationResult.Created(["Function.js"]);

        INewCommandContextResolver contextResolver = Substitute.For<INewCommandContextResolver>();
        contextResolver.ResolveAsync(invocation, cancellation.Token).Returns(_ =>
        {
            calls.Add("resolve");
            return Task.FromResult(NewCommandResolutionResult.Succeed(context));
        });
        INewCommandBundleValidator bundleValidator = Substitute.For<INewCommandBundleValidator>();
        bundleValidator.ValidateAsync(context, cancellation.Token).Returns(_ =>
        {
            calls.Add("validate");
            return Task.FromResult(0);
        });
        INewCommandTemplateCatalog templateCatalog = Substitute.For<INewCommandTemplateCatalog>();
        templateCatalog.ListAsync(context, cancellation.Token).Returns(_ =>
        {
            calls.Add("catalog");
            return Task.FromResult<IReadOnlyList<FunctionTemplateInfo>>([template]);
        });
        INewCommandTemplateSelector templateSelector = Substitute.For<INewCommandTemplateSelector>();
        templateSelector.SelectAsync(invocation, Arg.Any<IReadOnlyList<FunctionTemplateInfo>>(), cancellation.Token).Returns(_ =>
        {
            calls.Add("select");
            return Task.FromResult<FunctionTemplateInfo?>(template);
        });
        INewCommandTemplateApplicator templateApplicator = Substitute.For<INewCommandTemplateApplicator>();
        templateApplicator.ApplyAsync(invocation, context, template, cancellation.Token).Returns(_ =>
        {
            calls.Add("apply");
            return Task.FromResult(applyResult);
        });
        INewCommandResultRenderer resultRenderer = Substitute.For<INewCommandResultRenderer>();
        resultRenderer.RenderApplyResult(template, applyResult).Returns(_ =>
        {
            calls.Add("render");
            return 0;
        });
        TestInteractionService interaction = new();
        NewCommandRunner runner = new(
            contextResolver,
            bundleValidator,
            templateCatalog,
            templateSelector,
            templateApplicator,
            new NewCommandRenderer(interaction),
            resultRenderer);

        int exitCode = await runner.ExecuteAsync(invocation, cancellation.Token);

        exitCode.Should().Be(0);
        calls.Should().Equal("resolve", "validate", "catalog", "select", "apply", "render");
    }
    [Fact]
    public async Task ExecuteAsync_MissingLanguageOnMultiLanguageStack_RendersErrorExactlyOnce()
    {
        // Repro of https://github.com/Azure/azure-functions-core-tools/issues/5304:
        // when `.func/config.json` is missing `stack.language` on a
        // multi-language stack (dotnet), the missing-language error should
        // surface exactly once for one `func new -t ...` invocation.
        TestInteractionService interaction = new();
        NewCommandRunner runner = BuildRunnerForMissingLanguage(interaction);

        WorkingDirectory wd = new(new DirectoryInfo(Path.GetTempPath()), WasExplicit: false);
        NewInvocation invocation = new(
            WorkingDirectory: wd,
            RequestedTemplate: "HttpTrigger",
            RequestedFunctionName: null,
            Force: false,
            NonInteractive: true);

        int exitCode = await runner.ExecuteAsync(invocation, CancellationToken.None);

        exitCode.Should().Be(1);
        int hits = interaction.Lines.Count(l => l.Contains("Cannot determine language for stack"));
        (hits == 1).Should().BeTrue($"expected the missing-language error to be rendered exactly once, but it was rendered {hits} times. Output:\n{interaction.AllOutput}");
    }

    [Fact]
    public async Task ListAsync_MissingLanguageOnMultiLanguageStack_RendersErrorExactlyOnce()
    {
        TestInteractionService interaction = new();
        NewCommandRunner runner = BuildRunnerForMissingLanguage(interaction);

        WorkingDirectory wd = new(new DirectoryInfo(Path.GetTempPath()), WasExplicit: false);
        NewInvocation invocation = new(
            WorkingDirectory: wd,
            RequestedTemplate: null,
            RequestedFunctionName: null,
            Force: false,
            NonInteractive: true);

        int exitCode = await runner.ListAsync(invocation, CancellationToken.None);

        exitCode.Should().Be(1);
        int hits = interaction.Lines.Count(l => l.Contains("Cannot determine language for stack"));
        (hits == 1).Should().BeTrue($"expected the missing-language error to be rendered exactly once, but it was rendered {hits} times. Output:\n{interaction.AllOutput}");
    }

    [Fact]
    public async Task ListAsync_ProjectChannelMissingWorkload_FallsBackToStableAndWarns()
    {
        // Repro of https://github.com/Azure/azure-functions-core-tools/issues/5369:
        // project declares the preview extension bundle but only a stable
        // templates workload is installed. The runner should fall back to the
        // stable workload, emit a warning, and not hard-fail.
        TestInteractionService interaction = new();
        NewCommandRunner runner = BuildRunnerForChannelFallback(
            interaction,
            bundleId: BundleHelpers.PreviewBundleId,
            installedVersions: ["1.0.0"]);

        WorkingDirectory wd = new(new DirectoryInfo(Path.GetTempPath()), WasExplicit: false);
        NewInvocation invocation = new(
            WorkingDirectory: wd,
            RequestedTemplate: null,
            RequestedFunctionName: null,
            Force: false,
            NonInteractive: true);

        int exitCode = await runner.ListAsync(invocation, CancellationToken.None);

        exitCode.Should().Be(1);
        interaction.Lines.Should().Contain(l =>
            l.Contains("WARNING") && l.Contains("preview") && l.Contains(BundleHelpers.PreviewBundleId));
        interaction.Lines.Should().NotContain(l =>
            l.Contains("No installed templates workload matches"));
    }

    [Fact]
    public async Task ListAsync_ProjectChannelMissingAndNoStable_StillHardFails()
    {
        // Counterpart to the fallback test: when neither the project's
        // channel nor stable is installed, the original hard-fail message
        // still surfaces.
        TestInteractionService interaction = new();
        NewCommandRunner runner = BuildRunnerForChannelFallback(
            interaction,
            bundleId: BundleHelpers.PreviewBundleId,
            installedVersions: ["1.0.0-experimental.1"]);

        WorkingDirectory wd = new(new DirectoryInfo(Path.GetTempPath()), WasExplicit: false);
        NewInvocation invocation = new(
            WorkingDirectory: wd,
            RequestedTemplate: null,
            RequestedFunctionName: null,
            Force: false,
            NonInteractive: true);

        int exitCode = await runner.ListAsync(invocation, CancellationToken.None);

        exitCode.Should().Be(1);
        interaction.Lines.Should().Contain(l => l.Contains("No installed templates workload matches"));
    }

    private static NewCommandRunner BuildRunnerForMissingLanguage(TestInteractionService interaction)
    {
        // Project resolver returns a multi-language dotnet project.
        IFunctionsProjectResolver projectResolver = Substitute.For<IFunctionsProjectResolver>();
        WorkingDirectory wd = new(new DirectoryInfo(Path.GetTempPath()), WasExplicit: false);
        projectResolver
            .ResolveProjectAsync(Arg.Any<ProjectResolutionContext>(), Arg.Any<CancellationToken>())
            .Returns(ProjectResolutionResults.Resolved(new FakeDotNetProject(wd), "fake"));

        // Profile resolver: no-op (returns whatever default Substitute provides).
        IProfileResolver profileResolver = Substitute.For<IProfileResolver>();

        // StackOptions: Language is null/empty, simulating the missing
        // `stack.language` config we want the runner to surface.
        IOptionsMonitor<StackOptions> stackOptions = Substitute.For<IOptionsMonitor<StackOptions>>();
        stackOptions.Get(Arg.Any<string>()).Returns(new StackOptions { Runtime = "dotnet", Language = null });

        // Installed templates workloads: return one row so the orchestrator
        // gets past the "no workload installed" gate and reaches the
        // language-resolution step that emits the error under test.
        IInstalledTemplatesWorkloads installedTemplates = Substitute.For<IInstalledTemplatesWorkloads>();
        installedTemplates
            .ListInstalledAsync("dotnet", Arg.Any<CancellationToken>())
            .Returns(new List<InstalledTemplatesWorkload>
            {
                new("dotnet", "1.0.0", Path.GetTempPath()),
            });

        // No project initializers registered: multi-language stack
        // (initializer has > 1 SupportedLanguages or no entry), so the
        // single-language fallback in ResolveLanguage cannot fire and the
        // missing-language branch is taken.
        ITemplateEngineProviderRegistry engineProviders = new TemplateEngineProviderRegistry([]);
        TemplateOptionHydrator optionHydrator = new([]);
        NewCommandRenderer renderer = new(interaction);
        return new NewCommandRunner(
            new NewCommandContextResolver(
                interaction,
                projectResolver,
                profileResolver,
                stackOptions,
                projectInitializers: [],
                installedTemplates,
                Substitute.For<IHostJsonBundleSectionReader>()),
            Substitute.For<INewCommandBundleValidator>(),
            new NewCommandTemplateCatalog(engineProviders),
            new NewCommandTemplateSelector(interaction, new TemplatePicker(interaction)),
            new NewCommandTemplateApplicator(engineProviders, optionHydrator),
            renderer,
            new NewCommandResultRenderer(interaction, renderer));
    }

    private static NewCommandRunner BuildRunnerForChannelFallback(
        TestInteractionService interaction,
        string bundleId,
        IReadOnlyList<string> installedVersions)
    {
        IFunctionsProjectResolver projectResolver = Substitute.For<IFunctionsProjectResolver>();
        WorkingDirectory wd = new(new DirectoryInfo(Path.GetTempPath()), WasExplicit: false);
        projectResolver
            .ResolveProjectAsync(Arg.Any<ProjectResolutionContext>(), Arg.Any<CancellationToken>())
            .Returns(ProjectResolutionResults.Resolved(new FakeNodeProject(wd), "fake"));

        IProfileResolver profileResolver = Substitute.For<IProfileResolver>();

        IOptionsMonitor<StackOptions> stackOptions = Substitute.For<IOptionsMonitor<StackOptions>>();
        stackOptions.Get(Arg.Any<string>()).Returns(new StackOptions { Runtime = "node", Language = "javascript" });

        IInstalledTemplatesWorkloads installedTemplates = Substitute.For<IInstalledTemplatesWorkloads>();
        installedTemplates
            .ListInstalledAsync("node", Arg.Any<CancellationToken>())
            .Returns(installedVersions.Select(v => new InstalledTemplatesWorkload("node", v, Path.GetTempPath())).ToList());

        IHostJsonBundleSectionReader hostJsonReader = Substitute.For<IHostJsonBundleSectionReader>();
        hostJsonReader
            .ReadAsync(Arg.Any<DirectoryInfo>(), Arg.Any<CancellationToken>())
            .Returns(new HostJsonBundleSection(bundleId, "[4.*, 5.0.0)"));

        ITemplateEngineProviderRegistry engineProviders = new TemplateEngineProviderRegistry([]);
        TemplateOptionHydrator optionHydrator = new([]);
        NewCommandRenderer renderer = new(interaction);
        return new NewCommandRunner(
            new NewCommandContextResolver(
                interaction,
                projectResolver,
                profileResolver,
                stackOptions,
                projectInitializers: [],
                installedTemplates,
                hostJsonReader),
            Substitute.For<INewCommandBundleValidator>(),
            new NewCommandTemplateCatalog(engineProviders),
            new NewCommandTemplateSelector(interaction, new TemplatePicker(interaction)),
            new NewCommandTemplateApplicator(engineProviders, optionHydrator),
            renderer,
            new NewCommandResultRenderer(interaction, renderer));
    }

    private sealed class FakeDotNetProject(WorkingDirectory workingDirectory) : FunctionsProject
    {
        private readonly WorkingDirectory _workingDirectory = workingDirectory;
        private readonly FunctionsWorkerReference _workerReference =
            FunctionsWorkerReference.FromWorkerInfo(".NET", "dotnet-isolated", "/tmp/worker.config.json", "1.0.0");

        public override WorkingDirectory WorkingDirectory => _workingDirectory;

        public override string StackName => "dotnet";

        public override string StackDisplayName => ".NET";

        public override bool SupportsExtensionBundles => false;

        public override FunctionsWorkerReference WorkerReference => _workerReference;
    }

    private sealed class FakeNodeProject(WorkingDirectory workingDirectory) : FunctionsProject
    {
        private readonly WorkingDirectory _workingDirectory = workingDirectory;
        private readonly FunctionsWorkerReference _workerReference =
            FunctionsWorkerReference.FromWorkerInfo("Node.js", "node", "/tmp/worker.config.json", "1.0.0");

        public override WorkingDirectory WorkingDirectory => _workingDirectory;

        public override string StackName => "node";

        public override string StackDisplayName => "Node.js";

        public override bool SupportsExtensionBundles => true;

        public override FunctionsWorkerReference WorkerReference => _workerReference;
    }
}
