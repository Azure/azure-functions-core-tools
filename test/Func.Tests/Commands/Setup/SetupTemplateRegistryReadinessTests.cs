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

public sealed class SetupTemplateRegistryReadinessTests : IDisposable
{
    private const string TargetId = "contoso.templates.node";
    private const string Source = "https://template-readiness.test/v3/index.json";
    private readonly string _home = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
    private readonly CancellationTokenSource _cancellation = new();
    private readonly IWorkloadCatalog _catalog = Substitute.For<IWorkloadCatalog>();
    private readonly IWorkloadInstaller _installer = Substitute.For<IWorkloadInstaller>();
    private readonly WorkloadPathsOptions _paths;
    private readonly WorkloadStore _store;
    private readonly InstalledTemplatesWorkloads _adapter;

    public SetupTemplateRegistryReadinessTests()
    {
        _paths = new WorkloadPathsOptions(_home);
        _store = new WorkloadStore(_paths);
        _adapter = new InstalledTemplatesWorkloads(_store, _paths);
        _installer.InstallFromCatalogAsync(TargetId, Arg.Any<NuGetVersion?>(), Source, false, true, false,
                Arg.Any<IProgress<WorkloadInstallProgress>?>(), _cancellation.Token)
            .Returns(async call =>
            {
                WorkloadEntry entry = Content(TargetId, call.ArgAt<NuGetVersion>(1).ToNormalizedString(), ["node-templates"]);
                await _store.SaveWorkloadAsync(entry, _cancellation.Token);
                return new WorkloadInstallResult(entry, false);
            });
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(false, false, true)]
    [InlineData(false, true, false)]
    [InlineData(false, true, true)]
    [InlineData(true, false, false)]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    [InlineData(true, true, true)]
    public async Task EnsureDependencyAsync_DifferentUsableOwner_RequiresExplicitMigrationInBothDirections(
        bool conventionalTarget, bool ifNeeded, bool check)
    {
        string conventionalId = TemplatesWorkloadConstants.GetPackageId("node");
        string targetId = conventionalTarget ? conventionalId : TargetId;
        WorkloadEntry incumbent = Content(conventionalTarget ? TargetId : conventionalId, "1.0.0", ["node-templates"]);
        await _store.SaveWorkloadAsync(incumbent, _cancellation.Token);
        byte[] before = await File.ReadAllBytesAsync(_paths.WorkloadRegistryPath, _cancellation.Token);
        (await _adapter.ListInstalledAsync("node", _cancellation.Token)).Should().ContainSingle()
            .Which.InstallDirectory.Should().Be(_paths.GetInstallDirectory(incumbent.PackageId, incumbent.PackageVersion));
        ConfigureTarget(targetId, "2.0.0");

        SetupDependencyResult result = await EnsureAsync(
            ifNeeded ? SetupInstallPolicy.IfNeeded : SetupInstallPolicy.LatestCompatible, check, targetId);

        AssertBlocker(result, incumbent);
        result.Message.Should().Contain(targetId).And.Contain("Explicit migration")
            .And.Contain("No additional templates were installed");
        _installer.ReceivedCalls().Should().BeEmpty();
        (await File.ReadAllBytesAsync(_paths.WorkloadRegistryPath, _cancellation.Token)).Should().Equal(before);
        (await _store.GetWorkloadsAsync(_cancellation.Token)).Should().ContainSingle().Which.Should().BeEquivalentTo(incumbent);
        (await _adapter.ListInstalledAsync("node", _cancellation.Token)).Should().ContainSingle()
            .Which.PackageVersion.Should().Be("1.0.0");
    }

    [Theory]
    [InlineData(BundleChannel.Stable, "2.0.0", "1.0.0", true)]
    [InlineData(BundleChannel.Preview, "2.0.0-preview.1", "1.0.0-preview.1", true)]
    [InlineData(BundleChannel.Experimental, "2.0.0-experimental.1", "1.0.0-experimental.1", true)]
    [InlineData(null, "2.0.0", "1.0.0-preview.1", true)]
    [InlineData(BundleChannel.Preview, "2.0.0", "1.0.0", true)]
    [InlineData(BundleChannel.Stable, "2.0.0", "1.0.0-preview.1", false)]
    [InlineData(BundleChannel.Preview, "2.0.0", "1.0.0-preview.1", false)]
    public async Task EnsureDependencyAsync_ConventionalTarget_ProtectsEffectiveChannelLogicalIncumbent(
        BundleChannel? channel, string targetVersion, string incumbentVersion, bool blocked)
    {
        string targetId = TemplatesWorkloadConstants.GetPackageId("node");
        WorkloadEntry incumbent = Content("payload.custom", incumbentVersion, ["java-templates"], logical: new LogicalPackage
        {
            PackageId = TargetId, PackageVersion = incumbentVersion, Aliases = ["node-templates"],
        });
        await _store.SaveWorkloadAsync(incumbent, _cancellation.Token);
        byte[] before = await File.ReadAllBytesAsync(_paths.WorkloadRegistryPath, _cancellation.Token);
        BundleChannel? resolvedChannel = channel is null ? null : BundleHelpers.GetBundleChannel(new NuGetVersion(targetVersion));
        ConfigureTarget(targetId, targetVersion, resolvedChannel);

        SetupDependencyResult result = await EnsureAsync(SetupInstallPolicy.LatestCompatible, check: false, targetId, channel);

        if (blocked)
        {
            AssertBlocker(result, incumbent);
            _installer.ReceivedCalls().Should().BeEmpty();
            (await File.ReadAllBytesAsync(_paths.WorkloadRegistryPath, _cancellation.Token)).Should().Equal(before);
            (await _adapter.ListInstalledAsync("node", _cancellation.Token, resolvedChannel)).Should()
                .ContainSingle().Which.InstallDirectory.Should().Be(_paths.GetInstallDirectory(incumbent.PackageId, incumbentVersion));
        }
        else
        {
            result.Status.Should().Be(SetupDependencyStatus.Installed);
            _installer.ReceivedCalls().Should().ContainSingle();
            (await _adapter.ListInstalledAsync("node", _cancellation.Token, resolvedChannel)).Should()
                .ContainSingle().Which.InstallDirectory.Should().Be(_paths.GetInstallDirectory(targetId, targetVersion));
        }

        if (channel == BundleChannel.Preview && targetVersion == "2.0.0") result.Warning.Should().Contain("using stable instead");
    }

    [Theory]
    [InlineData("none")]
    [InlineData("same-owner")]
    [InlineData("nonportable-same-owner")]
    [InlineData("nonportable-other-owner")]
    [InlineData("ambiguous-custom")]
    [InlineData("already-conventional")]
    public async Task EnsureDependencyAsync_NoDifferentUsableIncumbent_PreservesExistingInstallBehavior(string state)
    {
        string targetId = TemplatesWorkloadConstants.GetPackageId("node");
        WorkloadEntry[] entries = state switch
        {
            "same-owner" => [Content(targetId, "1.0.0", [])],
            "nonportable-same-owner" => [Content(targetId, "1.0.0", [], rid: "win-x64")],
            "nonportable-other-owner" => [Content(TargetId, "1.0.0", ["node-templates"], rid: "win-x64")],
            "ambiguous-custom" => [Content(TargetId, "1.0.0", ["node-templates"]), Content("other.custom", "1.0.0", ["node-templates"])],
            "already-conventional" => [Content(TargetId, "1.0.0", ["node-templates"]), Content(targetId, "1.0.0", [])],
            _ => [],
        };
        foreach (WorkloadEntry entry in entries) await _store.SaveWorkloadAsync(entry, _cancellation.Token);
        ConfigureTarget(targetId, "2.0.0");

        SetupDependencyResult result = await EnsureAsync(SetupInstallPolicy.LatestCompatible, check: false, targetId);

        result.Status.Should().Be(SetupDependencyStatus.Installed);
        _installer.ReceivedCalls().Should().ContainSingle();
        (await _store.GetWorkloadsAsync(_cancellation.Token)).Should().HaveCount(entries.Length + 1);
        (await _adapter.ListInstalledAsync("node", _cancellation.Token, BundleChannel.Stable))
            .Should().Contain(row => row.InstallDirectory == _paths.GetInstallDirectory(targetId, "2.0.0"));
    }

    [Theory]
    [InlineData("rid", "if-needed", true)]
    [InlineData("rid", "exact-check", true)]
    [InlineData("rid", "offline", true)]
    [InlineData("rid", "install", false)]
    [InlineData("rid", "check", false)]
    [InlineData("logical-other", "if-needed", true)]
    [InlineData("logical-other", "exact-check", true)]
    [InlineData("logical-other", "offline", true)]
    [InlineData("logical-other", "missing", false)]
    [InlineData("wrong-alias-rid", "if-needed", true)]
    [InlineData("wrong-alias-rid", "offline", true)]
    [InlineData("wrong-alias-rid", "install", false)]
    [InlineData("non-content", "offline", true)]
    [InlineData("non-content", "missing", true)]
    [InlineData("logical-shadow", "offline", true)]
    [InlineData("legacy-wrong-alias", "exact-check", true)]
    [InlineData("same-owner", "exact-check", true)]
    public async Task EnsureDependencyAsync_HistoricalRows_UsesOnlyEligibleStableConsumerClaims(
        string metadata, string mode, bool validInstalled)
    {
        WorkloadEntry[] historical = HistoricalRows(metadata);
        foreach (WorkloadEntry entry in historical)
        {
            await _store.SaveWorkloadAsync(entry, _cancellation.Token);
        }

        if (validInstalled)
        {
            await _store.SaveWorkloadAsync(Content(TargetId, "2.0.0", ["node-templates"]), _cancellation.Token);
        }

        byte[] before = await File.ReadAllBytesAsync(_paths.WorkloadRegistryPath, _cancellation.Token);
        (await _adapter.ListInstalledAsync("node", _cancellation.Token, BundleChannel.Stable)).Should()
            .HaveCount(validInstalled ? 1 : 0);

        if (mode == "offline")
        {
            _catalog.ResolveLatestVersionOnChannelAsync(TargetId, null, null, Source, _cancellation.Token)
                .Returns<ResolvedPackage?>(_ => throw new HttpRequestException("offline"));
        }
        else if (mode != "missing")
        {
            Resolve(mode is "install" or "check" ? "3.0.0" : "2.0.0");
        }

        SetupDependencyResult result = await EnsureAsync(
            mode == "if-needed" ? SetupInstallPolicy.IfNeeded : SetupInstallPolicy.LatestCompatible,
            check: mode is "check" or "exact-check" or "offline" or "missing");

        result.Status.Should().Be(mode switch
        {
            "check" when !validInstalled => SetupDependencyStatus.Failed,
            "missing" when !validInstalled => SetupDependencyStatus.Skipped,
            "offline" or "missing" => SetupDependencyStatus.SatisfiedFallback,
            "install" => SetupDependencyStatus.Installed,
            _ => SetupDependencyStatus.Satisfied,
        });
        if (result.Status is not (SetupDependencyStatus.Failed or SetupDependencyStatus.Skipped))
        {
            result.PackageId.Should().Be(TargetId);
            result.Version.Should().Be(mode == "install" ? "3.0.0" : "2.0.0");
        }

        (await _adapter.ListInstalledAsync("node", _cancellation.Token, BundleChannel.Stable)).Should()
            .HaveCount(validInstalled || mode == "install" ? 1 : 0);

        if (mode == "install")
        {
            _installer.ReceivedCalls().Should().ContainSingle();
            (await _store.GetWorkloadsAsync(_cancellation.Token)).Should().HaveCount(historical.Length + 1);
        }
        else
        {
            _installer.ReceivedCalls().Should().BeEmpty();
            (await File.ReadAllBytesAsync(_paths.WorkloadRegistryPath, _cancellation.Token)).Should().Equal(before);
        }

        if (mode == "if-needed")
        {
            _catalog.ReceivedCalls().Should().BeEmpty();
        }
    }

    [Fact]
    public async Task EnsureDependencyAsync_MultipleCustomOwnersOnStable_ReportsEveryPhysicalAndLogicalVersion()
    {
        WorkloadEntry[] blockers =
        [
            Content("payload.one", "1.0.0", [], logical: new LogicalPackage
            {
                PackageId = "custom.one", PackageVersion = "1.0.0", Aliases = ["node-templates"],
            }),
            Content("custom.two", "2.0.0", ["node-templates"]),
        ];
        foreach (WorkloadEntry entry in blockers)
        {
            await _store.SaveWorkloadAsync(entry, _cancellation.Token);
        }

        Resolve("2.0.0");
        byte[] before = await File.ReadAllBytesAsync(_paths.WorkloadRegistryPath, _cancellation.Token);

        SetupDependencyResult result = await EnsureAsync(SetupInstallPolicy.LatestCompatible, check: false);

        foreach (WorkloadEntry entry in blockers)
        {
            AssertBlocker(result, entry);
        }

        _installer.ReceivedCalls().Should().BeEmpty();
        (await File.ReadAllBytesAsync(_paths.WorkloadRegistryPath, _cancellation.Token)).Should().Equal(before);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task EnsureDependencyAsync_InstallIntroducesDifferentOwner_DoesNotReportTargetReady(bool logicalOwner)
    {
        WorkloadEntry other = logicalOwner
            ? Content(TargetId, "2.0.0", ["node-templates"], logical: new LogicalPackage
            {
                PackageId = "legacy.templates",
                PackageVersion = "1.5.0",
                Aliases = ["node-templates"],
            })
            : Content("legacy.templates", "1.5.0", ["node-templates"]);
        _installer.InstallFromCatalogAsync(TargetId, Arg.Any<NuGetVersion?>(), Source, false, true, false,
                Arg.Any<IProgress<WorkloadInstallProgress>?>(), _cancellation.Token)
            .Returns(async _ =>
            {
                WorkloadEntry entry = logicalOwner ? other : Content(TargetId, "2.0.0", ["node-templates"]);
                await _store.SaveWorkloadAsync(entry, _cancellation.Token);
                await _store.SaveWorkloadAsync(other, _cancellation.Token);
                return new WorkloadInstallResult(entry, false);
            });
        Resolve("2.0.0");

        SetupDependencyResult result = await EnsureAsync(SetupInstallPolicy.LatestCompatible, check: false);

        AssertBlocker(result, other);
        result.Message.Should().Contain("Failure detected after install").And.Contain("No rollback was performed")
            .And.NotContain("No additional templates were installed");
        _installer.ReceivedCalls().Should().ContainSingle();
        (await _store.GetWorkloadsAsync(_cancellation.Token)).Should().HaveCount(logicalOwner ? 1 : 2);
        if (logicalOwner)
        {
            (await _adapter.ListInstalledAsync("node", _cancellation.Token)).Should().ContainSingle();
        }
        else
        {
            await FluentActions.Awaiting(() => _adapter.ListInstalledAsync("node", _cancellation.Token))
                .Should().ThrowExactlyAsync<InvalidOperationException>();
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task EnsureDependencyAsync_TargetAlreadyInstalledBesideLegacy_StillRequiresExplicitMigration(bool ifNeeded)
    {
        WorkloadEntry legacy = Content(TemplatesWorkloadConstants.GetPackageId("node"), "1.0.0", ["java-templates"]);
        await _store.SaveWorkloadAsync(legacy, _cancellation.Token);
        await _store.SaveWorkloadAsync(Content(TargetId, "2.0.0", ["node-templates"]), _cancellation.Token);
        byte[] before = await File.ReadAllBytesAsync(_paths.WorkloadRegistryPath, _cancellation.Token);
        Resolve("2.0.0");

        SetupDependencyResult result = await EnsureAsync(
            ifNeeded ? SetupInstallPolicy.IfNeeded : SetupInstallPolicy.LatestCompatible, check: false);

        AssertBlocker(result, legacy);
        result.Message.Should().Contain("Explicit migration");
        _installer.ReceivedCalls().Should().BeEmpty();
        (await File.ReadAllBytesAsync(_paths.WorkloadRegistryPath, _cancellation.Token)).Should().Equal(before);
        (await _adapter.ListInstalledAsync("node", _cancellation.Token, BundleChannel.Stable)).Should()
            .ContainSingle().Which.PackageVersion.Should().Be("1.0.0");
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task EnsureDependencyAsync_StableFallback_ChecksStableOwnersNotRequestedPreview(bool stableBlocker, bool targetInstalled)
    {
        WorkloadEntry incumbent = Content(TemplatesWorkloadConstants.GetPackageId("node"),
            stableBlocker ? "1.0.0" : "1.0.0-preview.1", []);
        await _store.SaveWorkloadAsync(incumbent, _cancellation.Token);
        if (targetInstalled)
        {
            await _store.SaveWorkloadAsync(Content(TargetId, "2.0.0", ["node-templates"]), _cancellation.Token);
        }

        byte[] before = await File.ReadAllBytesAsync(_paths.WorkloadRegistryPath, _cancellation.Token);
        Resolve("2.0.0");
        SetupCommandOptions options = new(new DirectoryInfo(_home), ["node"], [], Source, SetupInstallPolicy.LatestCompatible,
            IncludePrerelease: false, NonInteractive: true, AssumeYes: true, Check: false, SetupOutputMode.Json);
        SetupDependencyInstaller installer = new(new TestInteractionService(), _store, _catalog, _installer);

        SetupDependencyResult result = await installer.EnsureDependencyAsync(options,
            SetupDependency.Templates("node", TargetId, BundleChannel.Preview), _cancellation.Token);

        result.Warning.Should().Contain("using stable instead");
        if (stableBlocker)
        {
            AssertBlocker(result, incumbent);
            _installer.ReceivedCalls().Should().BeEmpty();
            (await File.ReadAllBytesAsync(_paths.WorkloadRegistryPath, _cancellation.Token)).Should().Equal(before);
        }
        else
        {
            result.Status.Should().Be(targetInstalled ? SetupDependencyStatus.Satisfied : SetupDependencyStatus.Installed);
            _installer.ReceivedCalls().Should().HaveCount(targetInstalled ? 0 : 1);
            (await _adapter.ListInstalledAsync("node", _cancellation.Token, BundleChannel.Stable)).Should()
                .ContainSingle().Which.InstallDirectory.Should().Be(_paths.GetInstallDirectory(TargetId, "2.0.0"));
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task EnsureDependencyAsync_InstallDoesNotRegisterTargetVersion_DoesNotReportReady(bool registersOlder)
    {
        _installer.InstallFromCatalogAsync(TargetId, Arg.Any<NuGetVersion?>(), Source, false, true, false,
                Arg.Any<IProgress<WorkloadInstallProgress>?>(), _cancellation.Token)
            .Returns(async _ =>
            {
                if (registersOlder)
                {
                    await _store.SaveWorkloadAsync(Content(TargetId, "1.0.0", ["node-templates"]), _cancellation.Token);
                }

                return new WorkloadInstallResult(Content(TargetId, "2.0.0", ["node-templates"]), false);
            });
        Resolve("2.0.0");

        SetupDependencyResult result = await EnsureAsync(SetupInstallPolicy.LatestCompatible, check: false);

        result.Status.Should().Be(SetupDependencyStatus.Failed);
        result.Message.Should().Contain("requested templates version is not available to the consumer")
            .And.Contain("Failure detected after install").And.Contain("No rollback was performed");
        _installer.ReceivedCalls().Should().ContainSingle();
    }

    [Fact]
    public async Task EnsureDependencyAsync_ExactPhysicalConventionalIdOwnedByCustom_DoesNotReportConventionalSelected()
    {
        string conventionalId = TemplatesWorkloadConstants.GetPackageId("node");
        WorkloadEntry entry = Content(conventionalId, "2.0.0", ["node-templates"], logical: new LogicalPackage
        {
            PackageId = TargetId, PackageVersion = "2.0.0", Aliases = ["node-templates"],
        });
        await _store.SaveWorkloadAsync(entry, _cancellation.Token);
        byte[] before = await File.ReadAllBytesAsync(_paths.WorkloadRegistryPath, _cancellation.Token);
        _catalog.ResolveLatestVersionOnChannelAsync(conventionalId, null, null, Source, _cancellation.Token)
            .Returns(new ResolvedPackage(conventionalId, new NuGetVersion("2.0.0"), new PackageSource(Source)));
        SetupCommandOptions options = new(new DirectoryInfo(_home), ["node"], [], Source, SetupInstallPolicy.LatestCompatible,
            IncludePrerelease: false, NonInteractive: true, AssumeYes: true, Check: false, SetupOutputMode.Json);
        SetupDependencyInstaller installer = new(new TestInteractionService(), _store, _catalog, _installer);

        SetupDependencyResult result = await installer.EnsureDependencyAsync(options,
            SetupDependency.Templates("node", conventionalId, BundleChannel.Stable), _cancellation.Token);

        AssertBlocker(result, entry);
        result.Message.Should().Contain($"consumer selects owner '{TargetId}'");
        _installer.ReceivedCalls().Should().BeEmpty();
        (await File.ReadAllBytesAsync(_paths.WorkloadRegistryPath, _cancellation.Token)).Should().Equal(before);
    }

    public void Dispose()
    {
        _cancellation.Dispose();
        if (Directory.Exists(_home))
        {
            Directory.Delete(_home, recursive: true);
        }
    }

    private Task<SetupDependencyResult> EnsureAsync(SetupInstallPolicy policy, bool check,
        string targetId = TargetId, BundleChannel? channel = BundleChannel.Stable)
    {
        SetupCommandOptions options = new(new DirectoryInfo(_home), ["node"], [], Source, policy,
            IncludePrerelease: false, NonInteractive: true, AssumeYes: true, Check: check, SetupOutputMode.Json);
        SetupDependencyInstaller installer = new(new TestInteractionService(), _store, _catalog, _installer);
        return installer.EnsureDependencyAsync(options, SetupDependency.Templates("node", targetId, channel), _cancellation.Token);
    }

    private void ConfigureTarget(string targetId, string version, BundleChannel? channel = BundleChannel.Stable)
    {
        ResolvedPackage package = new(targetId, new NuGetVersion(version), new PackageSource(Source));
        if (channel is { } value)
            _catalog.ResolveLatestVersionOnChannelAsync(targetId, value.ToPrereleaseLabel(), null, Source, _cancellation.Token).Returns(package);
        else
            _catalog.ResolveLatestVersionAsync(targetId, false, null, true, Source, _cancellation.Token).Returns(package);
        _installer.InstallFromCatalogAsync(targetId, Arg.Any<NuGetVersion?>(), Source, false, true, false,
                Arg.Any<IProgress<WorkloadInstallProgress>?>(), _cancellation.Token)
            .Returns(async call =>
            {
                WorkloadEntry entry = Content(targetId, call.ArgAt<NuGetVersion>(1).ToNormalizedString(), ["node-templates"]);
                await _store.SaveWorkloadAsync(entry, _cancellation.Token);
                return new WorkloadInstallResult(entry, false);
            });
    }

    private void Resolve(string version)
        => _catalog.ResolveLatestVersionOnChannelAsync(TargetId, null, null, Source, _cancellation.Token)
            .Returns(new ResolvedPackage(TargetId, new NuGetVersion(version), new PackageSource(Source)));

    private static void AssertBlocker(SetupDependencyResult result, WorkloadEntry entry)
    {
        result.Status.Should().Be(SetupDependencyStatus.Failed);
        result.Message.Should().Contain("node-templates").And.Contain(entry.PackageId).And.Contain(entry.PackageVersion)
            .And.Contain($"func workload uninstall {entry.LogicalPackage?.PackageId ?? entry.PackageId} "
                + $"--version {entry.LogicalPackage?.PackageVersion ?? entry.PackageVersion} --exact");
    }

    private static WorkloadEntry[] HistoricalRows(string metadata)
        => metadata switch
        {
            "rid" => [Content(TargetId, "1.0.0-experimental.1", ["NODE-TEMPLATES"], rid: "win-x64")],
            "logical-other" => [Content("payload.other", "1.0.0-experimental.1", ["java-templates"],
                logical: new LogicalPackage { PackageId = "legacy.templates", PackageVersion = "1.5.0", Aliases = ["NODE-TEMPLATES"] })],
            "wrong-alias-rid" => [Content(TargetId, "1.0.0", ["java-templates"], rid: "win-x64")],
            "non-content" => [.. Enum.GetValues<WorkloadKind>().Where(kind => kind != WorkloadKind.Content)
                .Select(kind => new WorkloadEntry
                {
                    PackageId = TargetId,
                    PackageVersion = $"1.0.{(int)kind}",
                    Kind = kind,
                    Aliases = ["node-templates"],
                    RuntimeIdentifier = "win-x64",
                })],
            "logical-shadow" => [Content(TargetId, "1.0.0", ["node-templates"], rid: "win-x64",
                logical: new LogicalPackage { PackageId = "unrelated.owner", PackageVersion = "1.0.0", Aliases = [] })],
            "legacy-wrong-alias" => [Content(TemplatesWorkloadConstants.GetPackageId("node"), "1.0.0", ["java-templates"], rid: "win-x64")],
            "same-owner" => [Content("payload.old", "1.0.0-experimental.1", ["java-templates"], rid: "AnY",
                logical: new LogicalPackage { PackageId = TargetId.ToUpperInvariant(), PackageVersion = "1.0.0", Aliases = ["NODE-TEMPLATES"] })],
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
}