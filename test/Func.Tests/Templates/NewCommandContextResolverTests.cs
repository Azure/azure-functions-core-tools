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

public class NewCommandContextResolverTests
{
    [Fact]
    public async Task ResolveAsync_NullInvocation_ThrowsArgumentNullException()
    {
        ResolverFixture fixture = new();
        NewCommandContextResolver resolver = fixture.CreateResolver();

        await FluentActions.Awaiting(() => resolver.ResolveAsync(null!, CancellationToken.None))
            .Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task ResolveAsync_MissingLanguage_ReturnsFailureWithoutRendering()
    {
        ResolverFixture fixture = new();
        fixture.ResolveProject("dotnet", supportsExtensionBundles: false);
        fixture.StackOptions.Get(Arg.Any<string>()).Returns(new StackOptions { Runtime = "dotnet", Language = null });
        fixture.InstalledTemplates.ListInstalledAsync("dotnet", Arg.Any<CancellationToken>())
            .Returns([new InstalledTemplatesWorkload("dotnet", "1.0.0", "install")]);
        NewCommandContextResolver resolver = fixture.CreateResolver();

        NewCommandResolutionResult result = await resolver.ResolveAsync(fixture.Invocation, CancellationToken.None);

        result.Context.Should().BeNull();
        result.Failure.Should().NotBeNull();
        result.Failure!.Kind.Should().Be(NewCommandResolutionFailureKind.MissingLanguage);
        fixture.Interaction.Lines.Should().BeEmpty();
    }

    [Fact]
    public async Task ResolveAsync_MissingConfiguredLanguageOnSingleLanguageStack_UsesInitializerLanguage()
    {
        ResolverFixture fixture = new();
        fixture.ResolveProject("dotnet", supportsExtensionBundles: false);
        fixture.StackOptions.Get(Arg.Any<string>()).Returns(new StackOptions { Runtime = "dotnet", Language = null });
        fixture.InstalledTemplates.ListInstalledAsync("dotnet", Arg.Any<CancellationToken>())
            .Returns([new InstalledTemplatesWorkload("dotnet", "1.0.0", "install")]);
        IProjectInitializer initializer = Substitute.For<IProjectInitializer>();
        initializer.Stack.Returns("dotnet");
        initializer.SupportedLanguages.Returns(["csharp"]);
        NewCommandContextResolver resolver = fixture.CreateResolver([initializer]);

        NewCommandResolutionResult result = await resolver.ResolveAsync(fixture.Invocation, CancellationToken.None);

        result.Failure.Should().BeNull();
        result.Context.Should().NotBeNull();
        result.Context!.Language.Should().Be("csharp");
    }

    [Fact]
    public async Task ResolveAsync_PreviewChannelWithoutPreviewWorkload_UsesStableFallback()
    {
        ResolverFixture fixture = new();
        fixture.ResolveProject("node", supportsExtensionBundles: true);
        fixture.StackOptions.Get(Arg.Any<string>()).Returns(new StackOptions { Runtime = "node", Language = "javascript" });
        fixture.HostJsonReader.ReadAsync(fixture.WorkingDirectory.Info, Arg.Any<CancellationToken>())
            .Returns(new HostJsonBundleSection(BundleHelpers.PreviewBundleId, "[4.0.0, 5.0.0)"));
        InstalledTemplatesWorkload stableWorkload = new("node", "1.0.0", "stable-install");
        fixture.InstalledTemplates.ListInstalledAsync("node", Arg.Any<CancellationToken>())
            .Returns([stableWorkload]);
        NewCommandContextResolver resolver = fixture.CreateResolver();

        NewCommandResolutionResult result = await resolver.ResolveAsync(fixture.Invocation, CancellationToken.None);

        result.Failure.Should().BeNull();
        result.Context.Should().NotBeNull();
        result.Context!.Workload.Should().Be(stableWorkload);
        result.Context.BundleId.Should().Be(BundleHelpers.PreviewBundleId);
        result.Context.Channel.Should().Be(BundleChannel.Preview);
        result.Context.UsedStableFallback.Should().BeTrue();
    }

    [Fact]
    public async Task ResolveAsync_PreviewChannelWithoutPreviewOrStableWorkload_ReturnsChannelFailure()
    {
        ResolverFixture fixture = new();
        fixture.ResolveProject("node", supportsExtensionBundles: true);
        fixture.StackOptions.Get(Arg.Any<string>()).Returns(new StackOptions { Runtime = "node", Language = "javascript" });
        fixture.HostJsonReader.ReadAsync(fixture.WorkingDirectory.Info, Arg.Any<CancellationToken>())
            .Returns(new HostJsonBundleSection(BundleHelpers.PreviewBundleId, "[4.0.0, 5.0.0)"));
        fixture.InstalledTemplates.ListInstalledAsync("node", Arg.Any<CancellationToken>())
            .Returns([new InstalledTemplatesWorkload("node", "1.0.0-experimental.1", "experimental-install")]);
        NewCommandContextResolver resolver = fixture.CreateResolver();

        NewCommandResolutionResult result = await resolver.ResolveAsync(fixture.Invocation, CancellationToken.None);

        result.Context.Should().BeNull();
        result.Failure.Should().NotBeNull();
        result.Failure!.Kind.Should().Be(NewCommandResolutionFailureKind.NoTemplatesWorkloadForChannel);
        result.Failure.Channel.Should().Be(BundleChannel.Preview);
        result.Failure.BundleId.Should().Be(BundleHelpers.PreviewBundleId);
    }

    private sealed class ResolverFixture
    {
        public ResolverFixture()
        {
            WorkingDirectory = new WorkingDirectory(new DirectoryInfo(Path.GetTempPath()), WasExplicit: false);
            Invocation = new NewInvocation(
                WorkingDirectory,
                RequestedTemplate: null,
                RequestedFunctionName: null,
                Force: false,
                NonInteractive: true);
        }

        public TestInteractionService Interaction { get; } = new();

        public IFunctionsProjectResolver ProjectResolver { get; } = Substitute.For<IFunctionsProjectResolver>();

        public IProfileResolver ProfileResolver { get; } = Substitute.For<IProfileResolver>();

        public IOptionsMonitor<StackOptions> StackOptions { get; } = Substitute.For<IOptionsMonitor<StackOptions>>();

        public IInstalledTemplatesWorkloads InstalledTemplates { get; } = Substitute.For<IInstalledTemplatesWorkloads>();

        public IHostJsonBundleSectionReader HostJsonReader { get; } = Substitute.For<IHostJsonBundleSectionReader>();

        public WorkingDirectory WorkingDirectory { get; }

        public NewInvocation Invocation { get; }

        public NewCommandContextResolver CreateResolver(IEnumerable<IProjectInitializer>? projectInitializers = null)
        {
            return new NewCommandContextResolver(
                Interaction,
                ProjectResolver,
                ProfileResolver,
                StackOptions,
                projectInitializers ?? [],
                InstalledTemplates,
                HostJsonReader);
        }

        public void ResolveProject(string stack, bool supportsExtensionBundles)
        {
            ProjectResolver.ResolveProjectAsync(Arg.Any<ProjectResolutionContext>(), Arg.Any<CancellationToken>())
                .Returns(ProjectResolutionResults.Resolved(
                    new FakeProject(WorkingDirectory, stack, supportsExtensionBundles),
                    "fake"));
        }
    }

    private sealed class FakeProject(
        WorkingDirectory workingDirectory,
        string stack,
        bool supportsExtensionBundles) : FunctionsProject
    {
        private readonly FunctionsWorkerReference _workerReference =
            FunctionsWorkerReference.FromWorkerInfo(stack, stack, "/tmp/worker.config.json", "1.0.0");

        public override WorkingDirectory WorkingDirectory { get; } = workingDirectory;

        public override string StackName { get; } = stack;

        public override string StackDisplayName { get; } = stack;

        public override bool SupportsExtensionBundles { get; } = supportsExtensionBundles;

        public override FunctionsWorkerReference WorkerReference => _workerReference;
    }
}