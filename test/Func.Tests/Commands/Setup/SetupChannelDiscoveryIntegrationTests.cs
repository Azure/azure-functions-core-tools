// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Bundles;
using Azure.Functions.Cli.Commands.Setup;
using Azure.Functions.Cli.Configuration;
using Azure.Functions.Cli.Projects;
using Azure.Functions.Cli.Workloads;
using Azure.Functions.Cli.Workloads.Catalog;
using Azure.Functions.Cli.Workloads.Install;
using Azure.Functions.Cli.Workloads.Storage;
using NSubstitute;
using NSubstitute.Extensions;
using NuGet.Configuration;
using NuGet.Protocol.Core.Types;
using NuGet.Versioning;

namespace Azure.Functions.Cli.Tests.Commands.Setup;

public class SetupChannelDiscoveryIntegrationTests : IDisposable
{
    private const string Source = "https://channel-discovery.test/v3/index.json";
    private const string NodeStack = "contoso.stack.node";
    private const string NodeTemplates = "contoso.templates.node";

    private readonly IWorkloadCatalog _catalog = Substitute.For<IWorkloadCatalog>();
    private readonly IHostJsonBundleSectionReader _bundleReader = Substitute.For<IHostJsonBundleSectionReader>();
    private readonly CancellationTokenSource _cancellation = new();
    private readonly SetupStackCatalog _stacks;

    public SetupChannelDiscoveryIntegrationTests()
    {
        _stacks = new SetupStackCatalog(_catalog);
    }

    [Theory]
    [InlineData(BundleHelpers.PreviewBundleId, "1.0.0-preview.1")]
    [InlineData(BundleHelpers.ExperimentalBundleId, "1.0.0-experimental.1")]
    public async Task Plan_ExplicitBundleChannel_DiscoversPrereleaseOnlyTemplatesWithoutOptingStacksIn(string bundleId, string version)
    {
        WithBundle(bundleId);
        Publish(
            Result(NodeStack, "1.0.0", ["node", "nodejs"], canonical: "node"),
            Result(NodeStack, "2.0.0-preview.1", ["node", "nodejs"], canonical: "node"),
            Result(NodeTemplates, version, ["node-templates"], "content"));
        SetupCommandOptions options = Options("nodejs");
        var normal = await _stacks.GetStacksAsync(Source, false, _cancellation.Token);
        normal.StackPackageId("nodejs").Should().Be(NodeStack);
        normal.TemplatesPackageId("nodejs").Should().BeNull("a successful stable discovery must not use the built-in templates fallback");

        var plan = await BuildPlanAsync(options, _cancellation.Token);

        plan.Failures.Should().BeEmpty();
        var stack = plan.Dependencies.Should().ContainSingle(dependency => dependency.Kind == SetupDependencyKind.Stack).Subject;
        stack.PackageId.Should().Be(NodeStack);
        stack.Name.Should().Be("node");
        stack.Channel.Should().BeNull();
        var templates = plan.Dependencies.Should().ContainSingle(dependency => dependency.Kind == SetupDependencyKind.Templates).Subject;
        templates.PackageId.Should().Be(NodeTemplates);
        templates.Name.Should().Be("node");
        templates.Channel.Should().Be(BundleHelpers.GetBundleChannel(bundleId));
        plan.Dependencies.Should().ContainSingle(dependency => dependency.Kind == SetupDependencyKind.Worker)
            .Which.Name.Should().Be("node");
        options.IncludePrerelease.Should().BeFalse();
        AssertDiscoveryPolicies(false, true);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(BundleHelpers.StableBundleId)]
    [InlineData("Contoso.Unknown.Bundle")]
    public async Task Plan_NoExplicitNonstableBundle_DoesNotDiscoverPrereleaseTemplates(string? bundleId)
    {
        WithBundle(bundleId);
        Publish(Result(NodeStack, "1.0.0", ["node"]), Result(NodeTemplates, "1.0.0-preview.1", ["node-templates"], "content"));

        var plan = await BuildPlanAsync(Options("node"), _cancellation.Token);

        plan.Failures.Should().BeEmpty();
        plan.Dependencies.Should().ContainSingle(dependency => dependency.Kind == SetupDependencyKind.Stack).Which.PackageId.Should().Be(NodeStack);
        plan.Dependencies.Should().NotContain(dependency => dependency.Kind == SetupDependencyKind.Templates);
        plan.Dependencies.Should().ContainSingle(dependency => dependency.Kind == SetupDependencyKind.ExtensionBundle)
            .Which.Channel.Should().Be(BundleChannel.Stable);
        AssertDiscoveryPolicies(false);
    }

    [Theory]
    [InlineData("host")]
    [InlineData("runtime")]
    public async Task Plan_NoRuntimeFeatures_DoesNotDiscoverEvenWithPreviewBundle(string feature)
    {
        WithBundle(BundleHelpers.PreviewBundleId);

        var plan = await BuildPlanAsync(Options(feature), _cancellation.Token);

        plan.Failures.Should().BeEmpty();
        plan.Dependencies.Should().NotContain(dependency => dependency.Kind == SetupDependencyKind.Templates);
        AssertDiscoveryPolicies();
    }

    [Theory]
    [InlineData(BundleHelpers.PreviewBundleId)]
    [InlineData(BundleHelpers.ExperimentalBundleId)]
    public async Task Plan_DotNetOnly_DoesNotDiscoverOtherTemplateChannels(string bundleId)
    {
        WithBundle(bundleId);
        Publish(
            Result("contoso.stack.dotnet", "1.0.0", ["dotnet"]),
            Result("contoso.templates.dotnet", "1.0.0", ["dotnet-templates"], "content"),
            Result("contoso.other.templates.dotnet", "2.0.0-preview.1", ["dotnet-templates"], "content"));

        var plan = await BuildPlanAsync(Options("dotnet-isolated"), _cancellation.Token);

        plan.Failures.Should().BeEmpty();
        var templates = plan.Dependencies.Should().ContainSingle(dependency => dependency.Kind == SetupDependencyKind.Templates).Subject;
        templates.PackageId.Should().Be("contoso.templates.dotnet");
        templates.Channel.Should().BeNull();
        plan.Dependencies.Should().NotContain(dependency => dependency.Kind == SetupDependencyKind.Worker
            || dependency.Kind == SetupDependencyKind.ExtensionBundle);
        AssertDiscoveryPolicies(false);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Plan_MixedFeatures_UsesInclusiveTemplatesOnlyForScriptStacks(bool stableDotNetTemplates)
    {
        WithBundle(BundleHelpers.PreviewBundleId);
        List<CatalogSearchResult> packages =
        [
            Result(NodeStack, "1.0.0", ["node"]),
            Result("contoso.stack.dotnet", "1.0.0", ["dotnet"]),
            Result("contoso.stack.python", "1.0.0", ["python"]),
            Result(NodeTemplates, "1.0.0-preview.1", ["node-templates"], "content"),
            Result("contoso.templates.python", "1.0.0-preview.1", ["python-templates"], "content"),
            Result("contoso.preview.templates.dotnet", "1.0.0-preview.1", ["dotnet-templates"], "content"),
        ];
        if (stableDotNetTemplates)
        {
            packages.Add(Result("contoso.templates.dotnet", "1.0.0", ["dotnet-templates"], "content"));
        }

        Publish([.. packages]);

        var plan = await BuildPlanAsync(Options("node", "dotnet", "python"), _cancellation.Token);

        plan.Failures.Should().BeEmpty();
        plan.Dependencies.Where(dependency => dependency.Kind == SetupDependencyKind.Stack).Select(dependency => dependency.Name)
            .Should().BeEquivalentTo(["node", "dotnet", "python"]);
        plan.Dependencies.Where(dependency => dependency.Kind == SetupDependencyKind.Templates && dependency.Channel == BundleChannel.Preview)
            .Select(dependency => dependency.PackageId).Should().BeEquivalentTo([NodeTemplates, "contoso.templates.python"]);
        var dotnetTemplates = plan.Dependencies.Where(dependency => dependency.Kind == SetupDependencyKind.Templates && dependency.Name == "dotnet");
        if (stableDotNetTemplates)
        {
            var template = dotnetTemplates.Should().ContainSingle().Subject;
            template.PackageId.Should().Be("contoso.templates.dotnet");
            template.Channel.Should().BeNull();
        }
        else
        {
            dotnetTemplates.Should().BeEmpty();
        }

        AssertDiscoveryPolicies(false, true);
    }

    [Theory]
    [InlineData(BundleHelpers.StableBundleId)]
    [InlineData(BundleHelpers.PreviewBundleId)]
    [InlineData(BundleHelpers.ExperimentalBundleId)]
    public async Task Plan_GlobalPrerelease_UsesOneInclusiveDiscoveryAndKeepsTheExplicitTemplateChannel(string bundleId)
    {
        WithBundle(bundleId);
        Publish(Result(NodeStack, "1.0.0-preview.1", ["node"]), Result(NodeTemplates, "1.0.0-preview.1", ["node-templates"], "content"));

        var plan = await BuildPlanAsync(Options("node") with { IncludePrerelease = true }, _cancellation.Token);

        plan.Failures.Should().BeEmpty();
        plan.Dependencies.Should().ContainSingle(dependency => dependency.Kind == SetupDependencyKind.Stack).Which.PackageId.Should().Be(NodeStack);
        var templates = plan.Dependencies.Should().ContainSingle(dependency => dependency.Kind == SetupDependencyKind.Templates).Subject;
        templates.PackageId.Should().Be(NodeTemplates);
        templates.Channel.Should().Be(BundleHelpers.GetBundleChannel(bundleId));
        AssertDiscoveryPolicies(true);
    }

    [Fact]
    public async Task Plan_DotNetWithGlobalPrerelease_StillUsesChannelLessTemplates()
    {
        WithBundle(BundleHelpers.PreviewBundleId);
        Publish(Result("contoso.stack.dotnet", "1.0.0-preview.1", ["dotnet"]),
            Result("contoso.templates.dotnet", "1.0.0-preview.1", ["dotnet-templates"], "content"));

        var plan = await BuildPlanAsync(Options("dotnet") with { IncludePrerelease = true }, _cancellation.Token);

        plan.Failures.Should().BeEmpty();
        plan.Dependencies.Should().ContainSingle(dependency => dependency.Kind == SetupDependencyKind.Templates).Which.Channel.Should().BeNull();
        AssertDiscoveryPolicies(true);
    }

    [Fact]
    public async Task Plan_PreviewBundleWithStableTemplates_RetainsInstallerChannelFallback()
    {
        WithBundle(BundleHelpers.PreviewBundleId);
        Publish(Result(NodeStack, "1.0.0", ["node"]), Result(NodeTemplates, "1.0.0", ["node-templates"], "content"));
        var options = Options("node");
        var plan = await BuildPlanAsync(options, _cancellation.Token);
        plan.Failures.Should().BeEmpty();
        var templates = plan.Dependencies.Should().ContainSingle(dependency => dependency.Kind == SetupDependencyKind.Templates).Subject;
        templates.PackageId.Should().Be(NodeTemplates);
        templates.Channel.Should().Be(BundleChannel.Preview);
        _catalog.ResolveLatestVersionOnChannelAsync(NodeTemplates, "preview", null, Source, _cancellation.Token).Returns((ResolvedPackage?)null);
        _catalog.ResolveLatestVersionOnChannelAsync(NodeTemplates, null, null, Source, _cancellation.Token)
            .Returns(new ResolvedPackage(NodeTemplates, new NuGetVersion("1.0.0"), new PackageSource(Source)));
        var installer = InstallerWithInstalled(NodeTemplates, "1.0.0");

        var result = await installer.EnsureDependencyAsync(options, templates, _cancellation.Token);

        result.Status.Should().Be(SetupDependencyStatus.Satisfied);
        result.Version.Should().Be("1.0.0");
        result.Warning.Should().Contain("using stable instead").And.Contain("node-templates");
        await _catalog.Received(1).ResolveLatestVersionOnChannelAsync(NodeTemplates, "preview", null, Source, _cancellation.Token);
        await _catalog.Received(1).ResolveLatestVersionOnChannelAsync(NodeTemplates, null, null, Source, _cancellation.Token);
        AssertDiscoveryPolicies(false, true);
    }

    [Theory]
    [InlineData(BundleHelpers.PreviewBundleId, "http", false)]
    [InlineData(BundleHelpers.ExperimentalBundleId, "io", false)]
    [InlineData(BundleHelpers.PreviewBundleId, "protocol", false)]
    [InlineData(BundleHelpers.ExperimentalBundleId, "http", true)]
    [InlineData(BundleHelpers.PreviewBundleId, "empty", false)]
    public async Task Plan_InclusiveFallback_KeepsStableCustomOwnerAndInstalledChannelFallback(
        string bundleId, string failure, bool laterPage)
    {
        WithBundle(bundleId);
        Publish(Result(NodeStack, "1.0.0", ["node", "nodejs"], canonical: "node"),
            Result(NodeTemplates, "1.0.0", ["node-templates"], "content"));
        WithInclusiveFallback(failure, laterPage ? [Result("partial.stack", "2.0.0-preview.1", ["other"])] : []);
        var options = Options("nodejs");

        var plan = await BuildPlanAsync(options, _cancellation.Token);

        plan.Failures.Should().BeEmpty();
        plan.Dependencies.Should().ContainSingle(dependency => dependency.Kind == SetupDependencyKind.Stack)
            .Which.PackageId.Should().Be(NodeStack);
        var templates = plan.Dependencies.Should().ContainSingle(dependency => dependency.Kind == SetupDependencyKind.Templates).Subject;
        templates.Name.Should().Be("node");
        templates.PackageId.Should().Be(NodeTemplates);
        templates.Channel.Should().Be(BundleHelpers.GetBundleChannel(bundleId));
        _catalog.ResolveLatestVersionOnChannelAsync(NodeTemplates, null, null, Source, _cancellation.Token)
            .Returns(new ResolvedPackage(NodeTemplates, new NuGetVersion("1.0.0"), new PackageSource(Source)));

        var result = await InstallerWithInstalled(NodeTemplates, "1.0.0")
            .EnsureDependencyAsync(options, templates, _cancellation.Token);

        result.Status.Should().Be(SetupDependencyStatus.Satisfied);
        result.PackageId.Should().Be(NodeTemplates);
        result.Warning.Should().Contain("using stable instead");
        await _catalog.DidNotReceive().ResolveLatestVersionOnChannelAsync(
            Arg.Is<string>(id => id != NodeTemplates), Arg.Any<string?>(), Arg.Any<VersionRange?>(),
            Arg.Any<string?>(), Arg.Any<CancellationToken>());

        int requests = _catalog.ReceivedCalls().Count(call => call.GetMethodInfo().Name == nameof(IWorkloadCatalog.SearchPageAsync));
        var repeated = await BuildPlanAsync(options, _cancellation.Token);
        repeated.Should().BeEquivalentTo(plan);
        _catalog.ReceivedCalls().Count(call => call.GetMethodInfo().Name == nameof(IWorkloadCatalog.SearchPageAsync)).Should().Be(requests);
    }

    [Theory]
    [InlineData("http")]
    [InlineData("empty")]
    public async Task Plan_InclusiveFallback_DoesNotInventTemplatesMissingFromStableDiscovery(string failure)
    {
        WithBundle(BundleHelpers.PreviewBundleId);
        Publish(Result(NodeStack, "1.0.0", ["node"]));
        WithInclusiveFallback(failure, []);

        var plan = await BuildPlanAsync(Options("node"), _cancellation.Token);

        plan.Failures.Should().BeEmpty();
        plan.Dependencies.Should().NotContain(dependency => dependency.Kind == SetupDependencyKind.Templates);
        plan.Dependencies.Should().ContainSingle(dependency => dependency.Kind == SetupDependencyKind.Stack)
            .Which.PackageId.Should().Be(NodeStack);
    }

    [Theory]
    [InlineData("conflict")]
    [InlineData("pointer")]
    [InlineData("canonical-conflict")]
    public async Task Plan_InclusiveFallback_PreservesObservedRestrictionsOverKnownStableOwner(string restriction)
    {
        WithBundle(BundleHelpers.PreviewBundleId);
        Publish(Result(NodeStack, "1.0.0", ["node", "nodejs"], canonical: "node"),
            Result(NodeTemplates, "1.0.0", ["node-templates"], "content"));
        CatalogSearchResult[] partial = restriction switch
        {
            "conflict" => [Result(NodeTemplates, "1.0.0", ["node-templates"], "content"),
                Result("conflicting.templates", "2.0.0-preview.1", ["node-templates"], "meta")],
            "pointer" => [Result("unsupported.templates", "2.0.0-preview.1", ["node-templates"], "rid-pointer")],
            _ => [Result("renamed.stack", "2.0.0-preview.1", ["node", "future"], canonical: "future"),
                Result("conflicting.stack", "2.0.0-preview.1", ["future"])],
        };
        WithInclusiveFallback("http", partial);

        var plan = await BuildPlanAsync(Options("nodejs"), _cancellation.Token);

        plan.Failures.Should().ContainSingle();
        plan.Failures[0].Message.Should().Contain(restriction == "pointer" ? "RID-specific" : "alias collision");
        if (restriction == "conflict")
        {
            plan.Failures[0].Message.Should().Contain(NodeTemplates).And.Contain("conflicting.templates");
        }

        AssertNoRuntimeDependencies(plan);

        var cached = await _stacks.GetStacksAsync(Source, true, _cancellation.Token);
        cached.IsFallback.Should().BeTrue();
        int requests = _catalog.ReceivedCalls().Count(call => call.GetMethodInfo().Name == nameof(IWorkloadCatalog.SearchPageAsync));
        var repeated = await BuildPlanAsync(Options("nodejs"), _cancellation.Token);
        repeated.Should().BeEquivalentTo(plan);
        _catalog.ReceivedCalls().Count(call => call.GetMethodInfo().Name == nameof(IWorkloadCatalog.SearchPageAsync)).Should().Be(requests);
    }

    [Theory]
    [InlineData("http")]
    [InlineData("empty")]
    public async Task Discovery_FallbackProvenance_DistinguishesDefaultsFromCompletedOwnership(string failure)
    {
        Publish(Result(NodeStack, "1.0.0", ["node"]));
        WithInclusiveFallback(failure, []);

        var normal = await _stacks.GetStacksAsync(Source, false, _cancellation.Token);
        var inclusive = await _stacks.GetStacksAsync(Source, true, _cancellation.Token);

        normal.IsFallback.Should().BeFalse();
        inclusive.Should().BeSameAs(SetupDependency.BuiltInStackSnapshot);
        inclusive.IsFallback.Should().BeTrue();
        new SetupStackSnapshot(new Dictionary<string, string>(), new Dictionary<string, string>()).IsFallback.Should().BeFalse();
    }

    [Fact]
    public async Task Plan_BothScansFallBack_RetainsExistingBuiltInBehavior()
    {
        WithBundle(BundleHelpers.PreviewBundleId);
        Publish();
        WithInclusiveFallback("http", []);

        var plan = await BuildPlanAsync(Options("node"), _cancellation.Token);

        plan.Failures.Should().BeEmpty();
        plan.Dependencies.Should().ContainSingle(dependency => dependency.Kind == SetupDependencyKind.Stack)
            .Which.PackageId.Should().Be(SetupDependency.BuiltInStackSnapshot.StackPackageId("node"));
        plan.Dependencies.Should().ContainSingle(dependency => dependency.Kind == SetupDependencyKind.Templates)
            .Which.PackageId.Should().Be(SetupDependency.BuiltInStackSnapshot.TemplatesPackageId("node"));
    }

    [Fact]
    public async Task Plan_IncompleteInclusiveRemap_DoesNotRedefineKnownStableIdentity()
    {
        WithBundle(BundleHelpers.PreviewBundleId);
        Publish(Result(NodeStack, "1.0.0", ["node", "nodejs"], canonical: "node"),
            Result(NodeTemplates, "1.0.0", ["node-templates"], "content"));
        WithInclusiveFallback("http",
            [Result(NodeStack, "2.0.0-preview.1", ["node", "nodejs"], canonical: "nodejs")]);

        var plan = await BuildPlanAsync(Options("nodejs"), _cancellation.Token);

        plan.Failures.Should().BeEmpty();
        plan.Dependencies.Should().ContainSingle(dependency => dependency.Kind == SetupDependencyKind.Stack)
            .Which.PackageId.Should().Be(NodeStack);
        var templates = plan.Dependencies.Should().ContainSingle(dependency => dependency.Kind == SetupDependencyKind.Templates).Subject;
        templates.Name.Should().Be("node");
        templates.PackageId.Should().Be(NodeTemplates);
    }

    [Fact]
    public async Task Plan_InclusiveStackOwnerChanges_ResolvesTheNormalStackWithPrereleaseDisabled()
    {
        WithBundle(BundleHelpers.PreviewBundleId);
        Publish(
            Result(NodeStack, "1.0.0", ["node"]),
            Result(NodeStack, "2.0.0-preview.1", ["future-node"]),
            Result("contoso.preview.stack.node", "1.0.0-preview.1", ["node"]),
            Result(NodeTemplates, "1.0.0-preview.1", ["node-templates"], "content"));
        var options = Options("node");
        var plan = await BuildPlanAsync(options, _cancellation.Token);
        plan.Failures.Should().BeEmpty();
        var stack = plan.Dependencies.Should().ContainSingle(dependency => dependency.Kind == SetupDependencyKind.Stack).Subject;
        stack.PackageId.Should().Be(NodeStack);
        stack.Channel.Should().BeNull();
        var inclusive = await _stacks.GetStacksAsync(Source, true, _cancellation.Token);
        inclusive.StackPackageId("node").Should().Be("contoso.preview.stack.node");
        _catalog.ResolveLatestVersionAsync(NodeStack, false, null, true, Source, _cancellation.Token)
            .Returns(new ResolvedPackage(NodeStack, new NuGetVersion("1.0.0"), new PackageSource(Source)));
        var installer = InstallerWithInstalled(NodeStack, "1.0.0");

        var result = await installer.EnsureDependencyAsync(options, stack, _cancellation.Token);

        result.Status.Should().Be(SetupDependencyStatus.Satisfied);
        result.Version.Should().Be("1.0.0");
        await _catalog.Received(1).ResolveLatestVersionAsync(NodeStack, false, null, true, Source, _cancellation.Token);
        await _catalog.DidNotReceive().ResolveLatestVersionAsync(Arg.Any<string>(), true, Arg.Any<NuGetVersion?>(),
            Arg.Any<bool>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
        AssertDiscoveryPolicies(false, true);
    }

    [Fact]
    public async Task Plan_PrereleaseOnlyRequestedStack_IsNotAddedByTemplatesDiscovery()
    {
        WithBundle(BundleHelpers.PreviewBundleId);
        Publish(
            Result(NodeStack, "1.0.0", ["node"]),
            Result(NodeTemplates, "1.0.0-preview.1", ["node-templates"], "content"),
            Result("contoso.stack.python", "1.0.0-preview.1", ["python"]),
            Result("contoso.templates.python", "1.0.0-preview.1", ["python-templates"], "content"));

        var plan = await BuildPlanAsync(Options("node", "python"), _cancellation.Token);

        plan.Failures.Should().ContainSingle().Which.Message.Should().Contain("'python' stack is not available");
        plan.Dependencies.Should().NotContain(dependency => dependency.Name == "python");
        plan.Dependencies.Should().ContainSingle(dependency => dependency.Kind == SetupDependencyKind.Stack).Which.PackageId.Should().Be(NodeStack);
        plan.Dependencies.Should().ContainSingle(dependency => dependency.Kind == SetupDependencyKind.Templates).Which.PackageId.Should().Be(NodeTemplates);
        AssertDiscoveryPolicies(false, true);
    }

    [Theory]
    [InlineData("node")]
    [InlineData("nodejs")]
    public async Task Plan_InclusiveMetadataChangesCanonicalStack_FailsBeforePlanningRuntimeDependencies(string requested)
    {
        WithBundle(BundleHelpers.PreviewBundleId);
        Publish(
            Result(NodeStack, "1.0.0", ["node", "nodejs"], canonical: "node"),
            Result(NodeStack, "2.0.0-preview.1", ["node", "nodejs"], canonical: "nodejs"),
            Result("contoso.templates.nodejs", "1.0.0-preview.1", ["nodejs-templates"], "content"));

        var plan = await BuildPlanAsync(Options(requested), _cancellation.Token);

        plan.Failures.Should().ContainSingle().Which.Message.Should().Contain("'node' resolves to 'nodejs'")
            .And.Contain("prerelease-inclusive templates discovery").And.Contain("--source");
        AssertNoRuntimeDependencies(plan);
        AssertDiscoveryPolicies(false, true);
    }

    [Theory]
    [InlineData("content", false)]
    [InlineData("content", true)]
    [InlineData("workload", false)]
    [InlineData("workload", true)]
    public async Task Plan_InclusiveTemplateAliasConflict_FailsInsteadOfOmittingOrSelectingTemplates(string conflictingKind, bool stableTemplates)
    {
        WithBundle(BundleHelpers.PreviewBundleId);
        Publish(
            Result(NodeStack, "1.0.0", ["node"]),
            Result(NodeTemplates, stableTemplates ? "1.0.0" : "1.0.0-preview.1", ["node-templates"], "content"),
            Result("contoso.conflicting.templates", "2.0.0-preview.1", ["node-templates"], conflictingKind));

        var plan = await BuildPlanAsync(Options("node"), _cancellation.Token);

        plan.Failures.Should().ContainSingle().Which.Message.Should().Contain("workload-package alias collision")
            .And.Contain("node-templates").And.Contain(NodeTemplates).And.Contain("contoso.conflicting.templates").And.Contain("--exact");
        AssertNoRuntimeDependencies(plan);
        AssertDiscoveryPolicies(false, true);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Plan_InclusiveDiscoveryFallsBack_PreservesObservedTemplateConflict(bool laterPageFails)
    {
        WithBundle(BundleHelpers.PreviewBundleId);
        Publish(Result(NodeStack, "1.0.0", ["node"]));
        _catalog.Configure().SearchPageAsync(Arg.Is<CatalogSearchQuery>(query => query.IncludePrerelease == true && query.Skip == 0), _cancellation.Token)
            .Returns(new CatalogSearchPage([
                Result(NodeStack, "1.0.0", ["node"]),
                Result(NodeTemplates, "1.0.0-preview.1", ["node-templates"], "content"),
                Result("contoso.conflicting.templates", "2.0.0-preview.1", ["node-templates"], "content"),
            ], 100));
        _catalog.Configure().SearchPageAsync(Arg.Is<CatalogSearchQuery>(query => query.IncludePrerelease == true && query.Skip == 100), _cancellation.Token)
            .Returns<CatalogSearchPage>(_ => laterPageFails ? throw new HttpRequestException("offline") : new CatalogSearchPage([], 0));

        var plan = await BuildPlanAsync(Options("node"), _cancellation.Token);

        plan.Failures.Should().ContainSingle().Which.Message.Should().Contain("node-templates")
            .And.Contain(NodeTemplates).And.Contain("contoso.conflicting.templates");
        AssertNoRuntimeDependencies(plan);
        await _catalog.Received(3).SearchPageAsync(Arg.Any<CatalogSearchQuery>(), _cancellation.Token);
    }

    [Fact]
    public async Task Plan_InclusiveDiscoveryCancelled_PropagatesCancellation()
    {
        WithBundle(BundleHelpers.PreviewBundleId);
        Publish(Result(NodeStack, "1.0.0", ["node"]));
        _catalog.Configure().SearchPageAsync(Arg.Is<CatalogSearchQuery>(query => query.IncludePrerelease == true), _cancellation.Token)
            .Returns(_ =>
            {
                _cancellation.Cancel();
                return new CatalogSearchPage([], 0);
            });

        await FluentActions.Awaiting(() => BuildPlanAsync(Options("node"), _cancellation.Token))
            .Should().ThrowAsync<OperationCanceledException>();

        AssertDiscoveryPolicies(false, true);
    }

    [Theory]
    [InlineData("meta")]
    [InlineData("workload")]
    [InlineData("rid-pointer")]
    [InlineData("rid-content")]
    public async Task Plan_NormalRestrictionsHiddenByInclusiveMetadata_AreStillFailures(string kind)
    {
        WithBundle(BundleHelpers.PreviewBundleId);
        bool conflict = kind is "meta" or "workload";
        CatalogSearchResult normal = kind == "rid-content"
            ? Result(NodeTemplates, "1.0.0", ["node-templates"], "content") with { Rid = "win-x64" }
            : Result("contoso.restricted", "1.0.0", ["node-templates"], kind);
        Publish(Result(NodeStack, "1.0.0", ["node", "nodejs"], canonical: "node"), normal,
            Result(NodeTemplates, conflict ? "1.0.0" : "2.0.0-preview.1", ["node-templates"], "content"),
            Result("contoso.restricted", "2.0.0-preview.1", ["unrelated"], kind == "rid-content" ? "content" : kind));
        var normalSnapshot = await _stacks.GetStacksAsync(Source, false, _cancellation.Token);
        normalSnapshot.TemplatesPackageId("node").Should().BeNull();
        var inclusive = await _stacks.GetStacksAsync(Source, true, _cancellation.Token);
        inclusive.TemplatesPackageId("node").Should().Be(NodeTemplates);

        var plan = await BuildPlanAsync(Options("nodejs"), _cancellation.Token);

        plan.Failures.Should().ContainSingle().Which.Message.Should().Contain(conflict ? "alias collision" : "RID-specific");
        AssertNoRuntimeDependencies(plan);
    }

    public void Dispose() => _cancellation.Dispose();

    private void WithBundle(string? bundleId)
        => _bundleReader.ReadAsync(Arg.Any<DirectoryInfo>(), Arg.Any<CancellationToken>())
            .Returns(bundleId is null ? null : new HostJsonBundleSection(bundleId, "[4.0.0, 5.0.0)"));

    private void WithInclusiveFallback(string failure, CatalogSearchResult[] partial)
        => _catalog.Configure().SearchPageAsync(Arg.Is<CatalogSearchQuery>(query => query.IncludePrerelease == true), _cancellation.Token)
            .Returns(call =>
            {
                if (partial.Length > 0 && call.Arg<CatalogSearchQuery>().Skip == 0)
                {
                    return new CatalogSearchPage(partial, 100);
                }

                return failure switch
                {
                    "empty" => new CatalogSearchPage([], 0),
                    "http" => throw new HttpRequestException("inclusive feed unavailable"),
                    "io" => throw new IOException("inclusive feed unavailable"),
                    _ => throw new FatalProtocolException("inclusive feed unavailable"),
                };
            });

    private void Publish(params CatalogSearchResult[] packages)
        => _catalog.SearchPageAsync(Arg.Any<CatalogSearchQuery>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var query = call.Arg<CatalogSearchQuery>();
                // Model search metadata from the highest version allowed by the
                // query, including canonical-tag changes in newer prereleases.
                CatalogSearchResult[] visible = [.. packages.Where(package => query.IncludePrerelease == true || !package.LatestVersion.IsPrerelease)
                    .GroupBy(package => package.PackageId, StringComparer.OrdinalIgnoreCase)
                    .Select(group => group.OrderByDescending(package => package.LatestVersion).First())];
                CatalogSearchResult[] page = [.. visible.Skip(query.Skip).Take(query.Take ?? CatalogSearchQuery.DefaultTake)];
                return new CatalogSearchPage(page, page.Length, visible.Length);
            });

    private async Task<SetupDependencyPlan> BuildPlanAsync(SetupCommandOptions options, CancellationToken cancellationToken)
    {
        SetupFeatureResolver resolver = new(new TestInteractionService(), Substitute.For<IWorkloadStore>(),
            Substitute.For<ICliConfigurationProvider>(), _stacks);
        SetupDependencyPlanBuilder planner = new(_bundleReader, _stacks);
        var features = await resolver.ResolveFeaturesAsync(options, cancellationToken);
        features.Should().NotBeNull();
        return await planner.BuildDependencyPlanAsync(options, features!, SetupProfileScope.Unconstrained, cancellationToken);
    }

    private SetupDependencyInstaller InstallerWithInstalled(string packageId, string version)
    {
        var store = Substitute.For<IWorkloadStore>();
        store.GetWorkloadsAsync(_cancellation.Token).Returns([new WorkloadEntry
        {
            PackageId = packageId,
            PackageVersion = version,
            Kind = packageId == NodeTemplates ? WorkloadKind.Content : WorkloadKind.Workload,
            Aliases = packageId == NodeTemplates ? ["node-templates"] : ["node"],
        }]);
        return new SetupDependencyInstaller(new TestInteractionService(), store, _catalog, Substitute.For<IWorkloadInstaller>());
    }

    private void AssertDiscoveryPolicies(params bool[] policies)
    {
        NSubstitute.Core.ICall[] calls = [.. _catalog.ReceivedCalls()
            .Where(call => call.GetMethodInfo().Name == nameof(IWorkloadCatalog.SearchPageAsync))];
        calls.Select(call => ((CatalogSearchQuery)call.GetArguments()[0]!).IncludePrerelease)
            .Should().Equal(policies.Select(policy => (bool?)policy));
        foreach (var call in calls)
        {
            var query = (CatalogSearchQuery)call.GetArguments()[0]!;
            query.Source.Should().Be(Source);
            query.Skip.Should().Be(0);
            query.Take.Should().Be(100);
            call.GetArguments()[1].Should().Be(_cancellation.Token);
        }
    }

    private static void AssertNoRuntimeDependencies(SetupDependencyPlan plan)
        => plan.Dependencies.Should().NotContain(dependency => dependency.Kind == SetupDependencyKind.Worker
            || dependency.Kind == SetupDependencyKind.Stack || dependency.Kind == SetupDependencyKind.Templates);

    private static SetupCommandOptions Options(params string[] features)
        => new(new DirectoryInfo(Path.GetTempPath()), features, [], Source, SetupInstallPolicy.LatestCompatible,
            IncludePrerelease: false, NonInteractive: true, AssumeYes: true, Check: true, SetupOutputMode.Json);

    private static CatalogSearchResult Result(string id, string version, string[] aliases, string kind = "workload", string? canonical = null)
        => new(id, new NuGetVersion(version), null, null, aliases, new PackageSource(Source)) { Kind = kind, CanonicalStack = canonical };
}
