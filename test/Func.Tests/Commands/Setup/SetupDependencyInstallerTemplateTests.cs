// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Bundles;
using Azure.Functions.Cli.Commands.Setup;
using Azure.Functions.Cli.Projects;
using Azure.Functions.Cli.Templates;
using Azure.Functions.Cli.Workloads;
using Azure.Functions.Cli.Workloads.Catalog;
using Azure.Functions.Cli.Workloads.Install;
using Azure.Functions.Cli.Workloads.Storage;
using NSubstitute;
using NuGet.Configuration;
using NuGet.Versioning;

namespace Azure.Functions.Cli.Tests.Commands.Setup;

public sealed class SetupDependencyInstallerTemplateTests : IDisposable
{
    private const string Source = "https://template-validation.test/v3/index.json";
    private const string TemplateId = "contoso.templates.shared";
    private const string PhysicalId = "contoso.templates.payload";
    private static readonly string _legacyId = TemplatesWorkloadConstants.GetPackageId("node");
    private readonly WorkloadRegistry _registry = new();
    private readonly IWorkloadStore _store = Substitute.For<IWorkloadStore>();
    private readonly IWorkloadCatalog _catalog = Substitute.For<IWorkloadCatalog>();
    private readonly IWorkloadInstaller _installer = Substitute.For<IWorkloadInstaller>();
    private readonly CancellationTokenSource _cancellation = new();

    public SetupDependencyInstallerTemplateTests()
    {
        _store.GetWorkloadsAsync(_cancellation.Token).Returns(_ => _registry.Workloads.ToArray());
    }

    public static IEnumerable<object?[]> TemplateCases()
    {
        IEnumerable<string?> channels = Enum.GetValues<BundleChannel>()
            .Where(channel => channel != BundleChannel.Unknown)
            .Select(channel => (string?)channel.ToString().ToLowerInvariant()).Prepend(null);
        bool[] installedStates = [false, true];
        foreach ((string metadata, bool accepted) in MetadataCases())
        {
            foreach (string? channel in channels)
            {
                foreach (bool alreadyInstalled in installedStates)
                {
                    yield return [metadata, accepted, channel, alreadyInstalled];
                }
            }
        }
    }

    public static IEnumerable<object[]> InvalidResolutionCases()
    {
        bool[] states = [false, true];
        foreach ((string metadata, bool accepted) in MetadataCases())
        {
            if (accepted)
            {
                continue;
            }

            foreach (bool offline in states)
            {
                foreach (bool validOlderVersion in states)
                {
                    yield return [metadata, offline, validOlderVersion];
                }
            }
        }
    }

    [Theory]
    [MemberData(nameof(TemplateCases))]
    public async Task EnsureDependencyAsync_ReturnedEntry_ValidatesMetadataBeforeReportingSuccess(
        string metadata, bool accepted, string? channel, bool alreadyInstalled)
    {
        WorkloadEntry entry = CreateEntry(metadata, VersionFor(channel));
        SetupDependency dependency = Dependency(entry, channel);
        Resolve(dependency, entry.PackageVersion);
        ReturnInstalled(entry, alreadyInstalled);
        SetupCommandOptions options = Options() with { OutputMode = alreadyInstalled ? SetupOutputMode.Json : SetupOutputMode.Plain };

        SetupDependencyResult result = await CreateInstaller().EnsureDependencyAsync(options, dependency, _cancellation.Token);

        result.Status.Should().Be(accepted
            ? alreadyInstalled ? SetupDependencyStatus.Satisfied : SetupDependencyStatus.Installed
            : SetupDependencyStatus.Failed);
        result.PackageId.Should().Be(entry.PackageId);
        result.Version.Should().Be(entry.PackageVersion);
        if (!accepted)
        {
            AssertMismatch(result, entry);
            result.Message.Should().Contain("Failure detected after install").And.Contain("No rollback was performed");
            if (alreadyInstalled)
            {
                result.Message.Should().Contain("was already installed");
            }
        }

        await AssertOneInstallAsync(dependency.PackageId, entry.PackageVersion);
        _registry.Workloads.Should().ContainSingle().Which.Should().BeSameAs(entry);
        await AssertAdapterAcceptanceAsync(entry, accepted);
    }

    [Theory]
    [MemberData(nameof(TemplateCases))]
    public async Task EnsureDependencyAsync_InstalledEntry_RequiresMatchingMetadataForIfNeededAndExactShortcuts(
        string metadata, bool accepted, string? channel, bool ifNeeded)
    {
        WorkloadEntry entry = CreateEntry(metadata, VersionFor(channel));
        SetupDependency dependency = Dependency(entry, channel);
        _registry.Workloads.Add(entry);
        Resolve(dependency, entry.PackageVersion);
        SetupCommandOptions options = Options() with
        {
            Check = true,
            InstallPolicy = ifNeeded ? SetupInstallPolicy.IfNeeded : SetupInstallPolicy.LatestCompatible,
        };

        SetupDependencyResult result = await CreateInstaller().EnsureDependencyAsync(options, dependency, _cancellation.Token);

        result.Status.Should().Be(accepted ? SetupDependencyStatus.Satisfied : SetupDependencyStatus.Failed);
        if (accepted)
        {
            result.PackageId.Should().Be(ifNeeded ? entry.PackageId : dependency.PackageId);
            result.Version.Should().Be(entry.PackageVersion);
        }
        else
        {
            result.Message.Should().Contain("not installed");
        }

        _installer.ReceivedCalls().Should().BeEmpty();
        _catalog.ReceivedCalls().Count().Should().Be(ifNeeded && accepted ? 0 : 1);
        _registry.Workloads.Should().ContainSingle().Which.Should().BeSameAs(entry);
    }

    [Theory]
    [MemberData(nameof(InvalidResolutionCases))]
    public async Task EnsureDependencyAsync_InvalidMatchingEntryAndResolutionFailure_OnlyConsumerBlockersPreventValidFallback(
        string metadata, bool offline, bool validOlderVersion)
    {
        WorkloadEntry invalid = CreateEntry(metadata, "2.0.0-preview.1");
        SetupDependency dependency = Dependency(invalid, "preview");
        _registry.Workloads.Add(invalid);
        if (validOlderVersion)
        {
            _registry.Workloads.Add(Content(dependency.PackageId, "1.0.0-preview.1", ["node-templates"]));
        }

        FailResolution(dependency, offline);

        SetupDependencyResult result = await CreateInstaller().EnsureDependencyAsync(Options(), dependency, _cancellation.Token);

        if (metadata.StartsWith("rid-", StringComparison.Ordinal) && !validOlderVersion)
        {
            AssertReadinessBlocker(result, invalid);
            result.Message.Should().Contain("Catalog resolution failed").And.Contain("No installed workload was removed");
        }
        else if (validOlderVersion)
        {
            result.Status.Should().Be(SetupDependencyStatus.SatisfiedFallback);
            result.PackageId.Should().Be(dependency.PackageId);
            result.Version.Should().Be("1.0.0-preview.1");
            (await new InstalledTemplatesWorkloads(_store, Substitute.For<IWorkloadPaths>())
                .ListInstalledAsync("node", _cancellation.Token)).Should().ContainSingle();
        }
        else
        {
            AssertMismatch(result, invalid);
            result.Message.Should().Contain("Catalog resolution failed").And.Contain("No installed workload was removed");
        }

        result.Message.Should().Contain(offline ? "offline" : "No node templates workload version");
        _installer.ReceivedCalls().Should().BeEmpty();
        _registry.Workloads.Should().HaveCount(validOlderVersion ? 2 : 1).And.Contain(invalid);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task EnsureDependencyAsync_InvalidPriorVersion_AllowsValidResolvedVersionWithoutRemovingPriorEntry(bool ifNeeded)
    {
        WorkloadEntry prior = CreateEntry("wrong-alias", "1.0.0-preview.1");
        WorkloadEntry repaired = CreateEntry("alias", "2.0.0-preview.1");
        SetupDependency dependency = Dependency(prior, "preview");
        _registry.Workloads.Add(prior);
        Resolve(dependency, repaired.PackageVersion);
        ReturnInstalled(repaired, alreadyInstalled: false);
        SetupCommandOptions options = Options() with
        {
            InstallPolicy = ifNeeded ? SetupInstallPolicy.IfNeeded : SetupInstallPolicy.LatestCompatible,
        };

        SetupDependencyResult result = await CreateInstaller().EnsureDependencyAsync(options, dependency, _cancellation.Token);

        result.Status.Should().Be(SetupDependencyStatus.Installed);
        result.Version.Should().Be(repaired.PackageVersion);
        await AssertOneInstallAsync(dependency.PackageId, repaired.PackageVersion);
        _registry.Workloads.Should().Equal(prior, repaired);
        var adapter = new InstalledTemplatesWorkloads(_store, Substitute.For<IWorkloadPaths>());
        (await adapter.ListInstalledAsync("node", _cancellation.Token)).Should().ContainSingle()
            .Which.PackageVersion.Should().Be(repaired.PackageVersion);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task EnsureDependencyAsync_InvalidExactVersion_ValidatesAlreadyInstalledResultWithoutRollback(bool ifNeeded)
    {
        WorkloadEntry entry = CreateEntry("logical-wrong-alias", "1.0.0-preview.1");
        SetupDependency dependency = Dependency(entry, "preview");
        _registry.Workloads.Add(entry);
        Resolve(dependency, entry.PackageVersion);
        ReturnInstalled(entry, alreadyInstalled: true);

        SetupDependencyResult result = await CreateInstaller().EnsureDependencyAsync(
            Options() with { InstallPolicy = ifNeeded ? SetupInstallPolicy.IfNeeded : SetupInstallPolicy.LatestCompatible },
            dependency, _cancellation.Token);

        AssertMismatch(result, entry);
        result.Message.Should().Contain("was already installed").And.Contain("No rollback was performed");
        await AssertOneInstallAsync(dependency.PackageId, entry.PackageVersion);
        _registry.Workloads.Should().ContainSingle().Which.Should().BeSameAs(entry);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task EnsureDependencyAsync_ValidInstalledEntry_StillSupportsCatalogFailureFallback(bool offline)
    {
        WorkloadEntry entry = CreateEntry("logical-alias", "1.0.0-preview.1");
        SetupDependency dependency = Dependency(entry, "preview");
        _registry.Workloads.Add(entry);
        FailResolution(dependency, offline);

        SetupDependencyResult result = await CreateInstaller().EnsureDependencyAsync(Options(), dependency, _cancellation.Token);

        result.Status.Should().Be(SetupDependencyStatus.SatisfiedFallback);
        result.Version.Should().Be(entry.PackageVersion);
        _installer.ReceivedCalls().Should().BeEmpty();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("java-templates")]
    public async Task EnsureDependencyAsync_PhysicalConventionalId_DoesNotOverrideLogicalOwner(string? logicalAlias)
    {
        WorkloadEntry entry = Content(_legacyId, "1.0.0", ["node-templates"],
            logical: Logical(TemplateId, "1.0.0", logicalAlias is null ? [] : [logicalAlias]));
        _registry.Workloads.Add(entry);
        var dependency = SetupDependency.Templates("node", _legacyId, BundleChannel.Stable);

        SetupDependencyResult result = await CreateInstaller().EnsureDependencyAsync(
            Options() with { InstallPolicy = SetupInstallPolicy.IfNeeded }, dependency, _cancellation.Token);

        AssertMismatch(result, entry);
        result.Message.Should().Contain($"Effective package '{TemplateId}'");
        _installer.ReceivedCalls().Should().BeEmpty();
        await AssertAdapterAcceptanceAsync(entry, accepted: false);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task EnsureDependencyAsync_ChannelLessPrerelease_RespectsGlobalPolicyWithoutBypassingMetadata(bool accepted)
    {
        WorkloadEntry entry = CreateEntry(accepted ? "alias" : "wrong-alias", "2.0.0-preview.1");
        SetupDependency dependency = Dependency(entry, channel: null);
        _catalog.ResolveLatestVersionAsync(dependency.PackageId, true, null, true, Source, _cancellation.Token)
            .Returns(new ResolvedPackage(dependency.PackageId, new NuGetVersion(entry.PackageVersion), new PackageSource(Source)));
        ReturnInstalled(entry, alreadyInstalled: false);

        SetupDependencyResult result = await CreateInstaller().EnsureDependencyAsync(
            Options() with { IncludePrerelease = true }, dependency, _cancellation.Token);

        result.Status.Should().Be(accepted ? SetupDependencyStatus.Installed : SetupDependencyStatus.Failed);
        await _catalog.Received(1).ResolveLatestVersionAsync(dependency.PackageId, true, null, true, Source, _cancellation.Token);
        await _installer.Received(1).InstallFromCatalogAsync(dependency.PackageId, new NuGetVersion(entry.PackageVersion), Source,
            true, true, false, Arg.Any<IProgress<WorkloadInstallProgress>?>(), _cancellation.Token);
        _installer.ReceivedCalls().Should().ContainSingle();
        await AssertAdapterAcceptanceAsync(entry, accepted);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task EnsureDependencyAsync_DifferentOwnerWithExpectedRole_BlocksTargetInsteadOfSatisfyingOrSkipping(bool resolves)
    {
        WorkloadEntry blocker = Content("contoso.other.templates", "1.0.0", ["node-templates"]);
        _registry.Workloads.Add(blocker);
        var dependency = SetupDependency.Templates("node", TemplateId, BundleChannel.Stable);
        if (resolves)
        {
            Resolve(dependency, "1.0.0");
        }

        SetupDependencyResult result = await CreateInstaller().EnsureDependencyAsync(
            Options() with { Check = true, InstallPolicy = SetupInstallPolicy.IfNeeded }, dependency, _cancellation.Token);

        AssertReadinessBlocker(result, blocker);
        result.Message.Should().Contain(TemplateId);
        _installer.ReceivedCalls().Should().BeEmpty();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task EnsureDependencyAsync_StableChannelFallback_ValidatesReturnedMetadataAndRetainsWarning(bool accepted)
    {
        WorkloadEntry entry = CreateEntry(accepted ? "alias" : "wrong-alias", "1.0.0");
        SetupDependency dependency = Dependency(entry, "preview");
        Resolve(dependency with { Channel = BundleChannel.Stable }, entry.PackageVersion);
        ReturnInstalled(entry, alreadyInstalled: false);

        SetupDependencyResult result = await CreateInstaller().EnsureDependencyAsync(Options(), dependency, _cancellation.Token);

        result.Status.Should().Be(accepted ? SetupDependencyStatus.Installed : SetupDependencyStatus.Failed);
        result.Warning.Should().Contain("using stable instead").And.Contain("node-templates");
        await AssertOneInstallAsync(dependency.PackageId, entry.PackageVersion);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task EnsureDependencyAsync_StableChannelFallback_ValidatesExactInstalledMetadata(bool accepted)
    {
        WorkloadEntry entry = CreateEntry(accepted ? "alias" : "wrong-alias", "1.0.0");
        SetupDependency dependency = Dependency(entry, "preview");
        _registry.Workloads.Add(entry);
        Resolve(dependency with { Channel = BundleChannel.Stable }, entry.PackageVersion);

        SetupDependencyResult result = await CreateInstaller().EnsureDependencyAsync(
            Options() with { Check = true }, dependency, _cancellation.Token);

        result.Status.Should().Be(accepted ? SetupDependencyStatus.Satisfied : SetupDependencyStatus.Failed);
        result.Warning.Should().Contain("using stable instead");
        _installer.ReceivedCalls().Should().BeEmpty();
    }

    [Fact]
    public async Task EnsureDependencyAsync_ReturnedUnrelatedPackage_FailsEvenWithCorrectRole()
    {
        var dependency = SetupDependency.Templates("node", TemplateId, BundleChannel.Stable);
        WorkloadEntry entry = Content("contoso.other.templates", "1.0.0", ["node-templates"]);
        Resolve(dependency, entry.PackageVersion);
        ReturnInstalled(entry, alreadyInstalled: false);

        SetupDependencyResult result = await CreateInstaller().EnsureDependencyAsync(Options(), dependency, _cancellation.Token);

        AssertMismatch(result, entry);
        result.Message.Should().Contain($"does not match the requested package '{TemplateId}'");
        await AssertOneInstallAsync(dependency.PackageId, entry.PackageVersion);
    }

    [Fact]
    public async Task EnsureDependencyAsync_NonTemplateDependency_DoesNotApplyTemplateMetadataRules()
    {
        WorkloadEntry entry = CreateEntry("kind:Workload", "1.0.0");
        var dependency = SetupDependency.Stack("node", entry.PackageId);
        Resolve(dependency, entry.PackageVersion);
        ReturnInstalled(entry, alreadyInstalled: false);

        SetupDependencyResult result = await CreateInstaller().EnsureDependencyAsync(Options(), dependency, _cancellation.Token);

        result.Status.Should().Be(SetupDependencyStatus.Installed);
        await AssertOneInstallAsync(dependency.PackageId, entry.PackageVersion);
    }

    [Theory]
    [InlineData("1.0.0")]
    [InlineData("2.0.0-experimental.1")]
    [InlineData("invalid-version")]
    public async Task EnsureDependencyAsync_ReturnedWrongVersionOrChannel_FailsActualEntryValidation(string returnedVersion)
    {
        WorkloadEntry entry = CreateEntry("alias", returnedVersion);
        SetupDependency dependency = Dependency(entry, "preview");
        Resolve(dependency, "2.0.0-preview.1");
        ReturnInstalled(entry, alreadyInstalled: false);

        SetupDependencyResult result = await CreateInstaller().EnsureDependencyAsync(Options(), dependency, _cancellation.Token);

        AssertMismatch(result, entry);
        result.Message.Should().Contain("does not match requested version '2.0.0-preview.1'")
            .And.Contain("No rollback was performed");
        await AssertOneInstallAsync(dependency.PackageId, "2.0.0-preview.1");
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task EnsureDependencyAsync_ConventionalWrongAliasAndCatalogFailure_PreservesLegacyFallback(
        bool offline, bool olderInstalled)
    {
        WorkloadEntry entry = CreateEntry("legacy-wrong-alias", "2.0.0-preview.1");
        SetupDependency dependency = Dependency(entry, "preview");
        _registry.Workloads.Add(entry);
        if (olderInstalled)
        {
            _registry.Workloads.Add(Content(dependency.PackageId, "1.0.0-preview.1", ["node-templates"]));
        }

        FailResolution(dependency, offline);

        SetupDependencyResult result = await CreateInstaller().EnsureDependencyAsync(Options(), dependency, _cancellation.Token);

        result.Status.Should().Be(SetupDependencyStatus.SatisfiedFallback);
        result.Version.Should().Be("2.0.0-preview.1");
        _installer.ReceivedCalls().Should().BeEmpty();
        _registry.Workloads.Should().HaveCount(olderInstalled ? 2 : 1);
    }

    [Theory]
    [InlineData("9.0.0-beta.1", false)]
    [InlineData("9.0.0-beta.1", true)]
    [InlineData("9.0.0-rc.1", false)]
    [InlineData("9.0.0-rc.1", true)]
    [InlineData("invalid-version", false)]
    [InlineData("invalid-version", true)]
    public async Task EnsureDependencyAsync_IneligibleIncumbent_DoesNotBlockStableInstalledTarget(string version, bool offline)
    {
        WorkloadEntry stable = Content(TemplateId, "1.0.0", ["node-templates"]);
        _registry.Workloads.Add(Content(_legacyId, version, ["node-templates"]));
        _registry.Workloads.Add(stable);
        var dependency = SetupDependency.Templates("node", TemplateId, BundleChannel.Stable);
        _catalog.ResolveLatestVersionOnChannelAsync(TemplateId, null, null, Source, _cancellation.Token)
            .Returns<ResolvedPackage?>(_ => throw new HttpRequestException("offline"));

        SetupDependencyResult result = await CreateInstaller().EnsureDependencyAsync(
            Options() with { InstallPolicy = offline ? SetupInstallPolicy.LatestCompatible : SetupInstallPolicy.IfNeeded, Check = true },
            dependency, _cancellation.Token);

        result.Status.Should().Be(offline ? SetupDependencyStatus.SatisfiedFallback : SetupDependencyStatus.Satisfied);
        result.PackageId.Should().Be(TemplateId);
        result.Version.Should().Be("1.0.0");
        _installer.ReceivedCalls().Should().BeEmpty();
        var adapter = new InstalledTemplatesWorkloads(_store, Substitute.For<IWorkloadPaths>());
        (await adapter.ListInstalledAsync("node", _cancellation.Token, BundleChannel.Stable))
            .Should().ContainSingle().Which.PackageVersion.Should().Be("1.0.0");
    }

    [Theory]
    [InlineData("2.0.0-beta.1", false, false)]
    [InlineData("2.0.0-beta.1", false, true)]
    [InlineData("2.0.0-rc.1", false, false)]
    [InlineData("2.0.0-rc.1", false, true)]
    [InlineData("invalid-version", false, false)]
    [InlineData("invalid-version", false, true)]
    [InlineData("invalid-version", true, false)]
    [InlineData("invalid-version", true, true)]
    [InlineData("1.0.0", false, false)]
    [InlineData("1.0.0", false, true)]
    public async Task EnsureDependencyAsync_FailedResolution_OnlyChecksMetadataForValidMatchingVersions(
        string version, bool channelLess, bool offline)
    {
        _registry.Workloads.Add(Content(TemplateId, version, ["java-templates"]));
        var dependency = SetupDependency.Templates("node", TemplateId, channelLess ? null : BundleChannel.Stable);
        if (channelLess)
        {
            _catalog.ResolveLatestVersionAsync(TemplateId, false, null, true, Source, _cancellation.Token)
                .Returns<ResolvedPackage?>(_ => offline ? throw new HttpRequestException("offline") : null);
        }
        else
        {
            _catalog.ResolveLatestVersionOnChannelAsync(TemplateId, null, null, Source, _cancellation.Token)
                .Returns<ResolvedPackage?>(_ => offline ? throw new HttpRequestException("offline") : null);
        }

        SetupDependencyResult result = await CreateInstaller().EnsureDependencyAsync(Options(), dependency, _cancellation.Token);

        if (version == "1.0.0")
        {
            result.Status.Should().Be(SetupDependencyStatus.Failed);
            result.Message.Should().Contain("Template metadata mismatch");
        }
        else
        {
            result.Status.Should().Be(offline ? SetupDependencyStatus.Failed : SetupDependencyStatus.Skipped);
            result.Message.Should().NotContain("Template metadata mismatch");
        }

        _installer.ReceivedCalls().Should().BeEmpty();
        _registry.Workloads.Should().ContainSingle();
    }

    public void Dispose() => _cancellation.Dispose();

    private SetupDependencyInstaller CreateInstaller() => new(new TestInteractionService(), _store, _catalog, _installer);

    private void Resolve(SetupDependency dependency, string version)
    {
        var package = new ResolvedPackage(dependency.PackageId, new NuGetVersion(version), new PackageSource(Source));
        if (dependency.Channel is { } channel)
        {
            _catalog.ResolveLatestVersionOnChannelAsync(dependency.PackageId, channel.ToPrereleaseLabel(), null, Source, _cancellation.Token)
                .Returns(package);
        }
        else
        {
            _catalog.ResolveLatestVersionAsync(dependency.PackageId, false, null, true, Source, _cancellation.Token).Returns(package);
        }
    }

    private void FailResolution(SetupDependency dependency, bool offline)
        => _catalog.ResolveLatestVersionOnChannelAsync(dependency.PackageId, "preview", null, Source, _cancellation.Token)
            .Returns<ResolvedPackage?>(_ => offline ? throw new HttpRequestException("offline") : null);

    private void ReturnInstalled(WorkloadEntry entry, bool alreadyInstalled)
        => _installer.InstallFromCatalogAsync(Arg.Any<string>(), Arg.Any<NuGetVersion?>(), Arg.Any<string?>(), Arg.Any<bool?>(),
                Arg.Any<bool>(), Arg.Any<bool>(), Arg.Any<IProgress<WorkloadInstallProgress>?>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                if (!_registry.Workloads.Contains(entry))
                {
                    _registry.Workloads.Add(entry);
                }

                return new WorkloadInstallResult(entry, alreadyInstalled);
            });

    private async Task AssertOneInstallAsync(string packageId, string version)
    {
        await _installer.Received(1).InstallFromCatalogAsync(packageId, Arg.Is<NuGetVersion?>(v => v != null && v.Equals(new NuGetVersion(version))),
            Source, false, true, false, Arg.Any<IProgress<WorkloadInstallProgress>?>(), _cancellation.Token);
        _installer.ReceivedCalls().Should().ContainSingle("setup must not silently uninstall or roll back a mismatched entry");
    }

    private async Task AssertAdapterAcceptanceAsync(WorkloadEntry entry, bool accepted)
    {
        var adapter = new InstalledTemplatesWorkloads(_store, Substitute.For<IWorkloadPaths>());
        if (!string.IsNullOrEmpty(entry.RuntimeIdentifier)
            && !string.Equals(entry.RuntimeIdentifier, "any", StringComparison.OrdinalIgnoreCase))
        {
            await FluentActions.Awaiting(() => adapter.ListInstalledAsync("node", _cancellation.Token))
                .Should().ThrowExactlyAsync<InvalidOperationException>();
        }
        else
        {
            (await adapter.ListInstalledAsync("node", _cancellation.Token)).Should().HaveCount(accepted ? 1 : 0);
        }
    }

    private static void AssertMismatch(SetupDependencyResult result, WorkloadEntry entry)
    {
        result.Status.Should().Be(SetupDependencyStatus.Failed);
        result.PackageId.Should().Be(entry.PackageId);
        result.Version.Should().Be(entry.PackageVersion);
        result.Message.Should().Contain("Template metadata mismatch").And.Contain("node-templates")
            .And.Contain($"'{entry.PackageId}' version '{entry.PackageVersion}'")
            .And.Contain($"func workload uninstall {entry.LogicalPackage?.PackageId ?? entry.PackageId} "
                + $"--version {entry.LogicalPackage?.PackageVersion ?? entry.PackageVersion} --exact");
    }

    private static void AssertReadinessBlocker(SetupDependencyResult result, WorkloadEntry entry)
    {
        result.Status.Should().Be(SetupDependencyStatus.Failed);
        result.Message.Should().Contain("node-templates").And.Contain("blocked")
            .And.Contain($"'{entry.PackageId}' version '{entry.PackageVersion}'")
            .And.Contain($"func workload uninstall {entry.LogicalPackage?.PackageId ?? entry.PackageId} "
                + $"--version {entry.LogicalPackage?.PackageVersion ?? entry.PackageVersion} --exact");
    }

    private static SetupCommandOptions Options()
        => new(new DirectoryInfo(Path.GetTempPath()), ["node"], [], Source, SetupInstallPolicy.LatestCompatible,
            IncludePrerelease: false, NonInteractive: true, AssumeYes: true, Check: false, SetupOutputMode.Json);

    private static SetupDependency Dependency(WorkloadEntry entry, string? channel)
        => SetupDependency.Templates("node", entry.LogicalPackage?.PackageId ?? entry.PackageId,
            channel is null ? null : Enum.Parse<BundleChannel>(channel, ignoreCase: true));

    private static string VersionFor(string? channel) => channel is null or "stable" ? "1.0.0" : $"1.0.0-{channel}.1";

    private static IEnumerable<(string Metadata, bool Accepted)> MetadataCases()
    {
        yield return ("alias", true);
        yield return ("alias-case", true);
        yield return ("legacy", true);
        yield return ("logical-alias", true);
        yield return ("logical-legacy", true);
        yield return ("rid-empty", true);
        yield return ("rid-any", true);
        yield return ("rid-case", true);
        yield return ("wrong-alias", false);
        yield return ("partial-alias", false);
        yield return ("padded-alias", false);
        yield return ("missing-alias", false);
        yield return ("legacy-wrong-alias", true);
        yield return ("logical-wrong-alias", false);
        yield return ("logical-empty-alias", false);
        yield return ("logical-shadows-legacy-id", false);
        yield return ("rid-specific", false);
        yield return ("rid-padded", false);
        yield return ("rid-whitespace", false);
        foreach (WorkloadKind kind in Enum.GetValues<WorkloadKind>().Where(kind => kind != WorkloadKind.Content))
        {
            yield return ($"kind:{kind}", false);
        }
    }

    private static WorkloadEntry CreateEntry(string metadata, string version)
        => metadata switch
        {
            "alias" => Content(TemplateId, version, ["node-templates"]),
            "alias-case" => Content(TemplateId.ToUpperInvariant(), version, ["other", "NODE-TEMPLATES", "node-templates"]),
            "legacy" => Content(_legacyId.ToUpperInvariant(), version, []),
            "logical-alias" => Content(PhysicalId, version, ["java-templates"], logical: Logical(TemplateId, version, ["NODE-TEMPLATES"])),
            "logical-legacy" => Content(PhysicalId, version, ["java-templates"], logical: Logical(_legacyId, version, [])),
            "rid-empty" => Content(TemplateId, version, ["node-templates"], rid: ""),
            "rid-any" => Content(TemplateId, version, ["node-templates"], rid: "any"),
            "rid-case" => Content(TemplateId, version, ["node-templates"], rid: "AnY"),
            "wrong-alias" => Content(TemplateId, version, ["java-templates"]),
            "partial-alias" => Content(TemplateId, version, ["node-templates-preview"]),
            "padded-alias" => Content(TemplateId, version, [" node-templates "]),
            "missing-alias" => Content(TemplateId, version, []),
            "legacy-wrong-alias" => Content(_legacyId, version, ["java-templates"]),
            "logical-wrong-alias" => Content(PhysicalId, version, ["node-templates"], logical: Logical(TemplateId, version, ["java-templates"])),
            "logical-empty-alias" => Content(PhysicalId, version, ["node-templates"], logical: Logical(TemplateId, version, [])),
            "logical-shadows-legacy-id" => Content(_legacyId, version, [], logical: Logical(TemplateId, version, [])),
            "rid-specific" => Content(TemplateId, version, ["node-templates"], rid: "win-x64"),
            "rid-padded" => Content(TemplateId, version, ["node-templates"], rid: "any "),
            "rid-whitespace" => Content(TemplateId, version, ["node-templates"], rid: " "),
            _ when metadata.StartsWith("kind:", StringComparison.Ordinal) => new WorkloadEntry
            {
                PackageId = TemplateId,
                PackageVersion = version,
                Kind = Enum.Parse<WorkloadKind>(metadata[5..]),
                Aliases = ["node-templates"],
            },
            _ => throw new ArgumentOutOfRangeException(nameof(metadata)),
        };

    private static WorkloadEntry Content(string id, string version, string[] aliases, string? rid = null, LogicalPackage? logical = null)
        => new()
        {
            PackageId = id,
            PackageVersion = version,
            Kind = WorkloadKind.Content,
            Aliases = aliases,
            RuntimeIdentifier = rid,
            LogicalPackage = logical,
        };

    private static LogicalPackage Logical(string id, string version, string[] aliases)
        => new() { PackageId = id, PackageVersion = version, Aliases = aliases };
}