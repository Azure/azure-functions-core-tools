// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Bundles;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Configuration;
using Azure.Functions.Cli.Profiles;
using Azure.Functions.Cli.Projects;
using Azure.Functions.Cli.Templates;
using Azure.Functions.Cli.Workers;
using Azure.Functions.Cli.Workloads;
using Azure.Functions.Cli.Workloads.Storage;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Azure.Functions.Cli.Tests.Templates;

public sealed class NewCommandContextResolverIntegrationTests : IDisposable
{
    private readonly CancellationTokenSource _cancellation = new();

    public void Dispose() => _cancellation.Dispose();

    [Theory]
    [InlineData("node", false, null)]
    [InlineData("node", true, null)]
    [InlineData("node", false, "")]
    [InlineData("node", true, "")]
    [InlineData("node", false, "any")]
    [InlineData("node", true, "any")]
    [InlineData("node", false, "AnY")]
    [InlineData("node", true, "AnY")]
    [InlineData("DOTNET", false, null)]
    [InlineData("DOTNET", true, null)]
    [InlineData("DOTNET", false, "")]
    [InlineData("DOTNET", true, "")]
    [InlineData("DOTNET", false, "any")]
    [InlineData("DOTNET", true, "any")]
    [InlineData("DOTNET", false, "AnY")]
    [InlineData("DOTNET", true, "AnY")]
    public async Task ResolveAsync_CustomPackageAlias_PreservesPhysicalRootInContext(
        string stack, bool hasLogicalOwner, string? runtimeIdentifier)
    {
        ResolverFixture fixture = new(stack);
        const string logicalId = "Contoso.FunctionTemplates";
        string physicalId = hasLogicalOwner ? "Contoso.TemplatePayload" : logicalId;
        string alias = $"{stack.ToUpperInvariant()}-TEMPLATES";
        fixture.SetEntries(Content(
            physicalId,
            aliases: hasLogicalOwner ? ["unrelated-templates"] : [alias],
            logicalId: hasLogicalOwner ? logicalId : null,
            logicalAliases: [alias],
            runtimeIdentifier: runtimeIdentifier));
        NewCommandContextResolver resolver = fixture.CreateResolver();

        NewCommandResolutionResult result = await resolver.ResolveAsync(fixture.Invocation, _cancellation.Token);

        result.Failure.Should().BeNull();
        result.Context.Should().NotBeNull();
        NewCommandResolvedContext context = result.Context!;
        context.Workload.Should().Be(new InstalledTemplatesWorkload(
            stack.ToLowerInvariant(), "1.0.0", Path.Combine(fixture.InstallRoot, physicalId, "1.0.0")));
        context.WorkingDirectory.Should().BeSameAs(fixture.WorkingDirectory);
        context.Stack.Should().Be(stack);
        context.Language.Should().Be(fixture.Language);
        context.UsedStableFallback.Should().BeFalse();
        fixture.Paths.Received(1).GetInstallDirectory(physicalId, "1.0.0");
        await fixture.Store.Received(1).GetWorkloadsAsync(_cancellation.Token);
        if (hasLogicalOwner)
        {
            fixture.Paths.DidNotReceive().GetInstallDirectory(logicalId, Arg.Any<string>());
        }

        if (string.Equals(stack, "dotnet", StringComparison.OrdinalIgnoreCase))
        {
            context.Channel.Should().Be(BundleChannel.Unknown);
            context.BundleId.Should().BeNull();
            await fixture.HostJsonReader.DidNotReceive().ReadAsync(Arg.Any<DirectoryInfo>(), Arg.Any<CancellationToken>());
        }
        else
        {
            context.Channel.Should().Be(BundleChannel.Stable);
            context.BundleId.Should().Be(BundleHelpers.StableBundleId);
            await fixture.HostJsonReader.Received(1).ReadAsync(fixture.WorkingDirectory.Info, _cancellation.Token);
        }
    }

    [Theory]
    [InlineData(false, "2.0.0-preview.1", "Payload.Preview")]
    [InlineData(true, "1.10.0", "Payload.Newer")]
    public async Task ResolveAsync_LogicalAliasVersions_SelectsPreviewOrStableFallback(
        bool omitPreview, string expectedVersion, string expectedPhysicalId)
    {
        ResolverFixture fixture = new("node");
        fixture.HostJsonReader.ReadAsync(fixture.WorkingDirectory.Info, Arg.Any<CancellationToken>())
            .Returns(new HostJsonBundleSection(BundleHelpers.PreviewBundleId, "[4.0.0, 5.0.0)"));
        List<WorkloadEntry> entries =
        [
            Content("Payload.Older", "1.9.0", logicalId: "Contoso.Templates", logicalAliases: ["node-templates"]),
            Content("Payload.Newer", "1.10.0", logicalId: "CONTOSO.TEMPLATES", logicalAliases: ["node-templates"]),
        ];
        if (!omitPreview)
        {
            entries.Add(Content("Payload.Preview", "2.0.0-preview.1", logicalId: "Contoso.Templates", logicalAliases: ["node-templates"]));
        }

        fixture.SetEntries([.. entries]);
        NewCommandContextResolver resolver = fixture.CreateResolver();

        NewCommandResolutionResult result = await resolver.ResolveAsync(fixture.Invocation, _cancellation.Token);

        result.Failure.Should().BeNull();
        result.Context.Should().NotBeNull();
        NewCommandResolvedContext context = result.Context!;
        context.Workload.Should().Be(new InstalledTemplatesWorkload(
            "node", expectedVersion, Path.Combine(fixture.InstallRoot, expectedPhysicalId, expectedVersion)));
        context.Channel.Should().Be(BundleChannel.Preview);
        context.BundleId.Should().Be(BundleHelpers.PreviewBundleId);
        context.UsedStableFallback.Should().Be(omitPreview);
        await fixture.Store.Received(omitPreview ? 2 : 1).GetWorkloadsAsync(_cancellation.Token);
    }

    [Theory]
    [InlineData("node", false)]
    [InlineData("node", true)]
    [InlineData("dotnet", false)]
    [InlineData("dotnet", true)]
    public async Task ResolveAsync_ConflictingClaims_ThrowsGracefulErrorPreservingAdapterCause(string stack, bool reverseOrder)
    {
        ResolverFixture fixture = new(stack);
        string alias = $"{stack}-templates";
        WorkloadEntry[] entries =
        [
            Content("Payload.First", logicalId: "Contoso.Templates", logicalAliases: [alias]),
            Content("Payload.Second", logicalId: "Fabrikam.Templates", logicalAliases: [alias.ToUpperInvariant()]),
        ];
        fixture.SetEntries(reverseOrder ? [.. entries.Reverse()] : entries);
        NewCommandContextResolver resolver = fixture.CreateResolver();

        Func<Task> act = () => resolver.ResolveAsync(fixture.Invocation, _cancellation.Token);

        GracefulException exception = (await act.Should().ThrowExactlyAsync<GracefulException>()).Which;
        exception.IsUserError.Should().BeTrue();
        InvalidOperationException cause = exception.InnerException.Should().BeOfType<InvalidOperationException>().Which;
        cause.Message.Should().Be(
            $"Multiple installed content packages claim templates alias '{alias}': Contoso.Templates, Fabrikam.Templates.");
        exception.Message.Should().Be(
            $"{cause.Message} Use 'func workload uninstall' to remove conflicting or unsupported templates packages, then retry.");
        cause.StackTrace.Should().Contain(nameof(InstalledTemplatesWorkloads.ListInstalledAsync));
        fixture.Paths.DidNotReceive().GetInstallDirectory(Arg.Any<string>(), Arg.Any<string>());
        fixture.StackOptions.DidNotReceive().Get(Arg.Any<string>());
        await fixture.Store.Received(1).GetWorkloadsAsync(_cancellation.Token);
        fixture.Interaction.Lines.Should().BeEmpty();
    }

    [Theory]
    [InlineData("node", "win-x64", false)]
    [InlineData("node", "win-x64", true)]
    [InlineData("node", "linux-x64", false)]
    [InlineData("node", "linux-x64", true)]
    [InlineData("node", "osx-arm64", false)]
    [InlineData("node", "osx-arm64", true)]
    [InlineData("dotnet", "win-x64", false)]
    [InlineData("dotnet", "win-x64", true)]
    [InlineData("dotnet", "linux-x64", false)]
    [InlineData("dotnet", "linux-x64", true)]
    [InlineData("dotnet", "osx-arm64", false)]
    [InlineData("dotnet", "osx-arm64", true)]
    public async Task ResolveAsync_NonPortableMatchingVersion_DoesNotPoisonPortableVersion(
        string stack, string runtimeIdentifier, bool hasLogicalOwner)
    {
        ResolverFixture fixture = new(stack);
        const string logicalId = "Contoso.Templates";
        string physicalId = hasLogicalOwner ? "Contoso.TemplatePayload" : logicalId;
        string alias = $"{stack}-templates";
        fixture.SetEntries(
            Content(logicalId, "2.0.0", aliases: [alias]),
            Content(physicalId, "1.0.0-experimental.1",
                aliases: hasLogicalOwner ? ["unrelated-templates"] : [alias],
                logicalId: hasLogicalOwner ? logicalId : null,
                logicalAliases: [alias],
                runtimeIdentifier: runtimeIdentifier));
        NewCommandContextResolver resolver = fixture.CreateResolver();

        NewCommandResolutionResult result = await resolver.ResolveAsync(fixture.Invocation, _cancellation.Token);

        result.Failure.Should().BeNull();
        result.Context!.Workload.PackageVersion.Should().Be("2.0.0");
        fixture.Paths.Received(1).GetInstallDirectory(logicalId, "2.0.0");
        await fixture.Store.Received(1).GetWorkloadsAsync(_cancellation.Token);
        fixture.Interaction.Lines.Should().BeEmpty();
    }

    [Theory]
    [InlineData(null, nameof(NewCommandResolutionFailureKind.HostJsonBundleMissing))]
    [InlineData("Contoso.UnknownBundle", nameof(NewCommandResolutionFailureKind.UnrecognisedBundleId))]
    public async Task ResolveAsync_InvalidBundle_StopsBeforeConflictingTemplatesLookup(
        string? bundleId, string expectedFailure)
    {
        ResolverFixture fixture = new("node");
        fixture.HostJsonReader.ReadAsync(fixture.WorkingDirectory.Info, Arg.Any<CancellationToken>())
            .Returns(bundleId is null ? null : new HostJsonBundleSection(bundleId, "[4.0.0, 5.0.0)"));
        fixture.SetEntries(
            Content("Contoso.Templates", aliases: ["node-templates"]),
            Content("Fabrikam.Templates", aliases: ["node-templates"]));
        NewCommandContextResolver resolver = fixture.CreateResolver();

        NewCommandResolutionResult result = await resolver.ResolveAsync(fixture.Invocation, _cancellation.Token);

        result.Context.Should().BeNull();
        result.Failure.Should().NotBeNull();
        result.Failure!.Kind.ToString().Should().Be(expectedFailure);
        await fixture.Store.DidNotReceive().GetWorkloadsAsync(Arg.Any<CancellationToken>());
        fixture.Paths.DidNotReceive().GetInstallDirectory(Arg.Any<string>(), Arg.Any<string>());
    }

    [Theory]
    [InlineData("profile")]
    [InlineData("project")]
    [InlineData("host")]
    [InlineData("options")]
    public async Task ResolveAsync_InvalidOperationOutsideTemplatesLookup_PropagatesUnchanged(string stage)
    {
        ResolverFixture fixture = new("node");
        fixture.SetEntries(Content("Contoso.Templates", aliases: ["node-templates"]));
        InvalidOperationException failure = new($"{stage} failed.");
        switch (stage)
        {
            case "profile":
                fixture.ProfileResolver.ResolveAsync(Arg.Any<ProfileResolutionContext>(), Arg.Any<CancellationToken>())
                    .Returns(Task.FromException<ProfileResolution>(failure));
                break;
            case "project":
                fixture.ProjectResolver.ResolveProjectAsync(Arg.Any<ProjectResolutionContext>(), Arg.Any<CancellationToken>())
                    .Returns(Task.FromException<ProjectResolutionResult>(failure));
                break;
            case "host":
                fixture.HostJsonReader.ReadAsync(Arg.Any<DirectoryInfo>(), Arg.Any<CancellationToken>())
                    .Returns(Task.FromException<HostJsonBundleSection?>(failure));
                break;
            case "options":
                fixture.StackOptions.Get(Arg.Any<string>()).Returns(_ => throw failure);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(stage));
        }

        NewCommandContextResolver resolver = fixture.CreateResolver();

        Func<Task> act = () => resolver.ResolveAsync(fixture.Invocation, _cancellation.Token);

        (await act.Should().ThrowExactlyAsync<InvalidOperationException>()).Which.Should().BeSameAs(failure);
    }

    [Theory]
    [InlineData("node")]
    [InlineData("dotnet")]
    public async Task ResolveAsync_StoreIoFailure_PropagatesUnchanged(string stack)
    {
        ResolverFixture fixture = new(stack);
        IOException failure = new("Registry unavailable.");
        fixture.Store.GetWorkloadsAsync(_cancellation.Token)
            .Returns(Task.FromException<IReadOnlyList<WorkloadEntry>>(failure));
        NewCommandContextResolver resolver = fixture.CreateResolver();

        Func<Task> act = () => resolver.ResolveAsync(fixture.Invocation, _cancellation.Token);

        (await act.Should().ThrowExactlyAsync<IOException>()).Which.Should().BeSameAs(failure);
        await fixture.Store.Received(1).GetWorkloadsAsync(_cancellation.Token);
        fixture.Paths.DidNotReceive().GetInstallDirectory(Arg.Any<string>(), Arg.Any<string>());
    }

    [Theory]
    [InlineData("node")]
    [InlineData("dotnet")]
    public async Task ResolveAsync_CanceledLookup_PropagatesCancellationWithoutReadingStore(string stack)
    {
        ResolverFixture fixture = new(stack);
        NewCommandContextResolver resolver = fixture.CreateResolver();
        _cancellation.Cancel();

        Func<Task> act = () => resolver.ResolveAsync(fixture.Invocation, _cancellation.Token);

        OperationCanceledException exception = (await act.Should().ThrowAsync<OperationCanceledException>()).Which;
        exception.CancellationToken.Should().Be(_cancellation.Token);
        await fixture.Store.DidNotReceive().GetWorkloadsAsync(Arg.Any<CancellationToken>());
        fixture.Paths.DidNotReceive().GetInstallDirectory(Arg.Any<string>(), Arg.Any<string>());
    }

    [Theory]
    [InlineData(false, null)]
    [InlineData(true, null)]
    [InlineData(false, "win-x64")]
    [InlineData(true, "win-x64")]
    public async Task ResolveAsync_StableWithHistoricalExperimentalOwner_SelectsStableBeforeAmbiguity(bool fallback, string? rid)
    {
        ResolverFixture fixture = new("node");
        fixture.HostJsonReader.ReadAsync(fixture.WorkingDirectory.Info, Arg.Any<CancellationToken>())
            .Returns(new HostJsonBundleSection(fallback ? BundleHelpers.PreviewBundleId : BundleHelpers.StableBundleId, "[4.0.0, 5.0.0)"));
        fixture.SetEntries(
            Content("Current.Templates", "1.10.0", aliases: ["node-templates"]),
            Content("Old.Templates", "9.0.0-experimental.1", aliases: ["node-templates"], runtimeIdentifier: rid));

        NewCommandResolutionResult result = await fixture.CreateResolver().ResolveAsync(fixture.Invocation, _cancellation.Token);

        result.Failure.Should().BeNull();
        result.Context!.Workload.InstallDirectory.Should().Be(Path.Combine(fixture.InstallRoot, "Current.Templates", "1.10.0"));
        result.Context.UsedStableFallback.Should().Be(fallback);
    }

    [Theory]
    [InlineData("node", false)]
    [InlineData("node", true)]
    [InlineData("dotnet", false)]
    [InlineData("dotnet", true)]
    public async Task ResolveAsync_ConventionalAndCustomSameChannel_PreservesLegacyIncumbent(string stack, bool reverse)
    {
        ResolverFixture fixture = new(stack);
        string id = TemplatesWorkloadConstants.GetPackageId(stack);
        WorkloadEntry[] entries =
        [
            Content(id, "1.0.0", aliases: ["unrelated-templates"]),
            Content("Custom.Templates", "9.0.0", aliases: [$"{stack}-templates"]),
        ];
        fixture.SetEntries(reverse ? [.. entries.Reverse()] : entries);

        NewCommandResolutionResult result = await fixture.CreateResolver().ResolveAsync(fixture.Invocation, _cancellation.Token);

        result.Failure.Should().BeNull();
        result.Context!.Workload.InstallDirectory.Should().Be(Path.Combine(fixture.InstallRoot, id, "1.0.0"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ResolveAsync_ConventionalOnOtherChannel_DoesNotDisplaceCustomRequestedChannel(bool fallback)
    {
        ResolverFixture fixture = new("node");
        fixture.HostJsonReader.ReadAsync(fixture.WorkingDirectory.Info, Arg.Any<CancellationToken>())
            .Returns(new HostJsonBundleSection(fallback ? BundleHelpers.PreviewBundleId : BundleHelpers.StableBundleId, "[4.0.0, 5.0.0)"));
        fixture.SetEntries(
            Content("Custom.Templates", aliases: ["node-templates"]),
            Content(TemplatesWorkloadConstants.GetPackageId("node"), "9.0.0-experimental.1"));

        NewCommandResolutionResult result = await fixture.CreateResolver().ResolveAsync(fixture.Invocation, _cancellation.Token);

        result.Context!.Workload.InstallDirectory.Should().Be(Path.Combine(fixture.InstallRoot, "Custom.Templates", "1.0.0"));
        result.Context.UsedStableFallback.Should().Be(fallback);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ResolveAsync_OnlyRequestedChannelCandidateHasBadRid_Refuses(bool fallback)
    {
        ResolverFixture fixture = new("node");
        fixture.HostJsonReader.ReadAsync(fixture.WorkingDirectory.Info, Arg.Any<CancellationToken>())
            .Returns(new HostJsonBundleSection(fallback ? BundleHelpers.PreviewBundleId : BundleHelpers.StableBundleId, "[4.0.0, 5.0.0)"));
        fixture.SetEntries(Content("Invalid.Templates", aliases: ["node-templates"], runtimeIdentifier: "win-x64"));

        Func<Task> act = () => fixture.CreateResolver().ResolveAsync(fixture.Invocation, _cancellation.Token);

        (await act.Should().ThrowExactlyAsync<GracefulException>()).Which.InnerException.Should().BeOfType<InvalidOperationException>()
            .Which.Message.Should().Contain("unsupported runtime identifier 'win-x64'");
    }

    [Fact]
    public async Task ResolveAsync_DotnetWithoutChannel_PreservesOrdinalVersionSelection()
    {
        ResolverFixture fixture = new("dotnet");
        fixture.SetEntries(
            Content("Custom.Templates", "1.9.0", aliases: ["dotnet-templates"]),
            Content("Custom.Templates", "1.10.0", aliases: ["dotnet-templates"]));

        NewCommandResolutionResult result = await fixture.CreateResolver().ResolveAsync(fixture.Invocation, _cancellation.Token);

        result.Context!.Workload.PackageVersion.Should().Be("1.9.0");
    }

    [Theory]
    [InlineData("9.0.0-beta.1", false)]
    [InlineData("9.0.0-beta.1", true)]
    [InlineData("9.0.0-rc.1", false)]
    [InlineData("9.0.0-rc.1", true)]
    [InlineData("invalid-version", false)]
    [InlineData("invalid-version", true)]
    public async Task ResolveAsync_IneligibleOwnerCannotShadowStableOrStableFallback(string version, bool fallback)
    {
        ResolverFixture fixture = new("node");
        fixture.HostJsonReader.ReadAsync(fixture.WorkingDirectory.Info, Arg.Any<CancellationToken>())
            .Returns(new HostJsonBundleSection(fallback ? BundleHelpers.PreviewBundleId : BundleHelpers.StableBundleId, "[4.0.0, 5.0.0)"));
        fixture.SetEntries(
            Content(TemplatesWorkloadConstants.GetPackageId("node"), version, aliases: ["node-templates"]),
            Content("Stable.Custom", "1.0.0", aliases: ["node-templates"]));

        NewCommandResolutionResult result = await fixture.CreateResolver().ResolveAsync(fixture.Invocation, _cancellation.Token);

        result.Failure.Should().BeNull();
        result.Context!.Workload.InstallDirectory.Should().Be(Path.Combine(fixture.InstallRoot, "Stable.Custom", "1.0.0"));
        result.Context.UsedStableFallback.Should().Be(fallback);
        fixture.Paths.DidNotReceive().GetInstallDirectory(TemplatesWorkloadConstants.GetPackageId("node"), version);
    }

    private static WorkloadEntry Content(
        string packageId,
        string version = "1.0.0",
        IReadOnlyList<string>? aliases = null,
        string? logicalId = null,
        IReadOnlyList<string>? logicalAliases = null,
        string? runtimeIdentifier = null)
        => new()
        {
            PackageId = packageId,
            PackageVersion = version,
            Kind = WorkloadKind.Content,
            Aliases = aliases ?? [],
            RuntimeIdentifier = runtimeIdentifier,
            LogicalPackage = logicalId is null ? null : new LogicalPackage
            {
                PackageId = logicalId,
                PackageVersion = version,
                Aliases = logicalAliases ?? [],
            },
        };

    private sealed class ResolverFixture
    {
        public ResolverFixture(string stack)
        {
            WorkingDirectory = new WorkingDirectory(new DirectoryInfo(Path.Combine(AppContext.BaseDirectory, "template-consumer-project")), false);
            Invocation = new NewInvocation(WorkingDirectory, null, null, Force: false, NonInteractive: true);
            InstallRoot = Path.Combine(WorkingDirectory.Info.FullName, "installed");
            Language = string.Equals(stack, "dotnet", StringComparison.OrdinalIgnoreCase) ? "csharp" : "javascript";
            ProjectResolver.ResolveProjectAsync(Arg.Any<ProjectResolutionContext>(), Arg.Any<CancellationToken>())
                .Returns(ProjectResolutionResults.Resolved(new FakeProject(WorkingDirectory, stack), "test"));
            ProfileResolver.ResolveAsync(Arg.Any<ProfileResolutionContext>(), Arg.Any<CancellationToken>())
                .Returns(new ProfileResolution.None([]));
            StackOptions.Get(Arg.Any<string>()).Returns(new StackOptions { Runtime = stack, Language = Language });
            HostJsonReader.ReadAsync(WorkingDirectory.Info, Arg.Any<CancellationToken>())
                .Returns(new HostJsonBundleSection(BundleHelpers.StableBundleId, "[4.0.0, 5.0.0)"));
            Paths.GetInstallDirectory(Arg.Any<string>(), Arg.Any<string>())
                .Returns(call => Path.Combine(InstallRoot, call.ArgAt<string>(0), call.ArgAt<string>(1)));
            SetEntries();
        }

        public TestInteractionService Interaction { get; } = new();

        public IFunctionsProjectResolver ProjectResolver { get; } = Substitute.For<IFunctionsProjectResolver>();

        public IProfileResolver ProfileResolver { get; } = Substitute.For<IProfileResolver>();

        public IOptionsMonitor<StackOptions> StackOptions { get; } = Substitute.For<IOptionsMonitor<StackOptions>>();

        public IHostJsonBundleSectionReader HostJsonReader { get; } = Substitute.For<IHostJsonBundleSectionReader>();

        public IWorkloadStore Store { get; } = Substitute.For<IWorkloadStore>();

        public IWorkloadPaths Paths { get; } = Substitute.For<IWorkloadPaths>();

        public WorkingDirectory WorkingDirectory { get; }

        public NewInvocation Invocation { get; }

        public string InstallRoot { get; }

        public string Language { get; }

        public NewCommandContextResolver CreateResolver()
            => new(Interaction, ProjectResolver, ProfileResolver, StackOptions, [], new InstalledTemplatesWorkloads(Store, Paths), HostJsonReader);

        public void SetEntries(params WorkloadEntry[] entries)
            => Store.GetWorkloadsAsync(Arg.Any<CancellationToken>()).Returns<IReadOnlyList<WorkloadEntry>>(entries);
    }

    private sealed class FakeProject(WorkingDirectory workingDirectory, string stack) : FunctionsProject
    {
        public override WorkingDirectory WorkingDirectory { get; } = workingDirectory;

        public override string StackName { get; } = stack;

        public override string StackDisplayName { get; } = stack;

        public override bool SupportsExtensionBundles => !string.Equals(StackName, "dotnet", StringComparison.OrdinalIgnoreCase);

        public override FunctionsWorkerReference WorkerReference { get; } =
            FunctionsWorkerReference.FromWorkerInfo(stack, stack, "worker.config.json", "1.0.0");
    }
}