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
using Xunit;

namespace Azure.Functions.Cli.Tests.Templates;

public class NewCommandRunnerTests
{
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

        Assert.Equal(1, exitCode);
        int hits = interaction.Lines.Count(l => l.Contains("Cannot determine language for stack"));
        Assert.True(hits == 1, $"expected the missing-language error to be rendered exactly once, but it was rendered {hits} times. Output:\n{interaction.AllOutput}");
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

        Assert.Equal(1, exitCode);
        int hits = interaction.Lines.Count(l => l.Contains("Cannot determine language for stack"));
        Assert.True(hits == 1, $"expected the missing-language error to be rendered exactly once, but it was rendered {hits} times. Output:\n{interaction.AllOutput}");
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
        return new NewCommandRunner(
            interaction,
            projectResolver,
            profileResolver,
            stackOptions,
            projectInitializers: Array.Empty<IProjectInitializer>(),
            installedTemplates,
            new TemplateEngineProviderRegistry([]),
            new TemplateOptionHydrator(Array.Empty<IProjectInitializer>()),
            new TemplatePicker(interaction),
            new NewCommandRenderer(interaction),
            Substitute.For<IHostJsonBundleSectionReader>(),
            Substitute.For<IExtensionBundleResolver>());
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
}
