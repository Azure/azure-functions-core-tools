// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Commands.Setup;
using Azure.Functions.Cli.Commands.Start.Host;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Projects;
using Azure.Functions.Cli.Workloads;
using Azure.Functions.Cli.Workloads.Catalog;
using Azure.Functions.Cli.Workloads.Discovery;
using Azure.Functions.Cli.Workloads.Install;
using Azure.Functions.Cli.Workloads.Storage;
using NSubstitute;
using NuGet.Versioning;

namespace Azure.Functions.Cli.Tests.Commands.Setup;

public sealed class SetupDependencyInstallerTests
{
    private const string Source = "https://example.test/v3/index.json";
    private readonly IWorkloadStore _store = Substitute.For<IWorkloadStore>();
    private readonly IWorkloadCatalog _catalog = Substitute.For<IWorkloadCatalog>();
    private readonly IWorkloadInstaller _installer = Substitute.For<IWorkloadInstaller>();
    private readonly IFileSystem _files = Substitute.For<IFileSystem>();
    private readonly WorkloadPathsOptions _paths = new(Path.Combine(Path.GetTempPath(), "setup-dependency-validation"));

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, true)]
    [InlineData(false, false)]
    [InlineData(true, false)]
    public async Task EnsureDependencyAsync_BundledTemplates_VerifiesStackWithoutResolvingIndependentPackage(
        bool check, bool ifNeeded)
    {
        WorkloadEntry entry = Stack();
        _store.GetWorkloadsAsync(Arg.Any<CancellationToken>()).Returns([entry]);
        _installer.EnsureInstalledAsync(entry, Arg.Any<CancellationToken>(), check).Returns(new WorkloadInstallResult(entry, true));

        SetupDependencyResult result = await CreateInstaller().EnsureDependencyAsync(
            Options(check, ifNeeded ? SetupInstallPolicy.IfNeeded : SetupInstallPolicy.LatestCompatible),
            Templates(entry), CancellationToken.None);

        result.Status.Should().Be(SetupDependencyStatus.Satisfied);
        result.PackageId.Should().Be(entry.PackageId);
        result.Version.Should().Be(entry.PackageVersion);
        result.Message.Should().Contain("bundled");
        await _installer.Received(1).EnsureInstalledAsync(entry, Arg.Any<CancellationToken>(), check);
        _catalog.ReceivedCalls().Should().BeEmpty();
    }

    [Fact]
    public async Task EnsureDependencyAsync_BundledTemplates_UsesSelectedStackNotNewerSibling()
    {
        WorkloadEntry selected = Stack();
        WorkloadEntry sibling = new()
        {
            PackageId = selected.PackageId,
            PackageVersion = "2.0.0",
            Source = Source,
            BundledTemplatesDeclared = true,
            Kind = WorkloadKind.Content,
        };
        _store.GetWorkloadsAsync(Arg.Any<CancellationToken>()).Returns([sibling, selected]);
        _installer.EnsureInstalledAsync(selected, Arg.Any<CancellationToken>(), true).Returns(new WorkloadInstallResult(selected, true));

        SetupDependencyResult result = await CreateInstaller().EnsureDependencyAsync(
            Options(check: true), Templates(selected), CancellationToken.None);

        result.Status.Should().Be(SetupDependencyStatus.Satisfied);
        result.Version.Should().Be(selected.PackageVersion);
        await _installer.Received(1).EnsureInstalledAsync(selected, Arg.Any<CancellationToken>(), true);
        await _installer.DidNotReceive().EnsureInstalledAsync(sibling, Arg.Any<CancellationToken>(), Arg.Any<bool>());
    }

    [Fact]
    public async Task EnsureDependencyAsync_SelectedStackDisappeared_DoesNotReportIndependentTemplatesAsSatisfied()
    {
        _store.GetWorkloadsAsync(Arg.Any<CancellationToken>()).Returns([]);

        SetupDependencyResult result = await CreateInstaller().EnsureDependencyAsync(
            Options(check: true), Templates(Stack()), CancellationToken.None);

        result.Status.Should().Be(SetupDependencyStatus.Failed);
        result.Message.Should().Contain("selected stack changed");
        _catalog.ReceivedCalls().Should().BeEmpty();
    }

    [Fact]
    public async Task EnsureDependencyAsync_BundledTemplatesWrongSource_FailsWithoutRepairOrCatalog()
    {
        WorkloadEntry entry = Stack();
        _store.GetWorkloadsAsync(Arg.Any<CancellationToken>()).Returns([entry]);

        SetupDependencyResult result = await CreateInstaller().EnsureDependencyAsync(
            Options() with { Source = "https://different.test/v3/index.json" },
            Templates(entry), CancellationToken.None);

        result.Status.Should().Be(SetupDependencyStatus.Failed);
        result.Message.Should().Contain("validated origin").And.Contain(Source);
        await _installer.DidNotReceive().EnsureInstalledAsync(
            Arg.Any<WorkloadEntry>(), Arg.Any<CancellationToken>(), Arg.Any<bool>());
        _catalog.ReceivedCalls().Should().BeEmpty();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task EnsureDependencyAsync_BundledTemplatesInvalidRegistration_DoesNotReportReadiness(bool check)
    {
        WorkloadEntry entry = Stack();
        _store.GetWorkloadsAsync(Arg.Any<CancellationToken>()).Returns([entry]);
        _installer.EnsureInstalledAsync(entry, Arg.Any<CancellationToken>(), check)
            .Returns(Task.FromException<WorkloadInstallResult>(new InvalidWorkloadException("Retained native registration is missing.")));

        SetupDependencyResult result = await CreateInstaller().EnsureDependencyAsync(
            Options(check), Templates(entry), CancellationToken.None);

        result.Status.Should().Be(SetupDependencyStatus.Failed);
        result.Message.Should().Contain("registration is missing");
        _catalog.ReceivedCalls().Should().BeEmpty();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task EnsureDependencyAsync_LegacyRegistryWithBundledAssets_StillVerifiesRegistration(bool check)
    {
        WorkloadEntry entry = Stack(declared: false);
        _store.GetWorkloadsAsync(Arg.Any<CancellationToken>()).Returns([entry]);
        _installer.HasBundledTemplates(entry).Returns(true);
        _installer.EnsureInstalledAsync(entry, Arg.Any<CancellationToken>(), check)
            .Returns(Task.FromException<WorkloadInstallResult>(new InvalidWorkloadException("Retained native registration is missing.")));

        SetupDependencyResult result = await CreateInstaller().EnsureDependencyAsync(
            Options(check), SetupDependency.Stack("node"), CancellationToken.None);

        result.Status.Should().Be(SetupDependencyStatus.Failed);
        await _installer.Received(1).EnsureInstalledAsync(entry, Arg.Any<CancellationToken>(), check);
    }

    [Theory]
    [InlineData("3.13.0-preview.1", "[3.13.0]")]
    [InlineData("3.13.0-preview.1", "[3.13.0,4.0.0)")]
    [InlineData("3.13.0", "[3.13.0]")]
    public async Task EnsureDependencyAsync_IfNeededInstalledVersionInProfileRange_DoesNotConsultCatalog(string version, string range)
    {
        WorkloadEntry entry = new()
        {
            PackageId = "Azure.Functions.Cli.Workloads.Workers.Node",
            PackageVersion = version,
            Kind = WorkloadKind.Content,
        };
        _store.GetWorkloadsAsync(Arg.Any<CancellationToken>()).Returns([entry]);

        SetupDependencyResult result = await CreateInstaller().EnsureDependencyAsync(
            Options(), SetupDependency.Worker("node", VersionRange.Parse(range)), CancellationToken.None);

        result.Status.Should().Be(SetupDependencyStatus.Satisfied);
        result.Version.Should().Be(version);
        _catalog.ReceivedCalls().Should().BeEmpty();
    }

    [Fact]
    public async Task EnsureDependencyAsync_IfNeededPrereleaseDisabled_DoesNotReusePrerelease()
    {
        WorkloadEntry entry = new()
        {
            PackageId = "Azure.Functions.Cli.Workloads.Workers.Node",
            PackageVersion = "3.13.0-preview.1",
            Kind = WorkloadKind.Content,
        };
        _store.GetWorkloadsAsync(Arg.Any<CancellationToken>()).Returns([entry]);

        SetupDependencyResult result = await CreateInstaller().EnsureDependencyAsync(
            Options() with { IncludePrerelease = false },
            SetupDependency.Worker("node", VersionRange.Parse("[3.13.0]")), CancellationToken.None);

        result.Status.Should().NotBe(SetupDependencyStatus.Satisfied);
        _catalog.ReceivedCalls().Should().ContainSingle();
    }

    [Fact]
    public async Task EnsureDependencyAsync_UnexpectedRetainedFailure_IsNotConvertedToReadinessResult()
    {
        WorkloadEntry entry = Stack();
        _store.GetWorkloadsAsync(Arg.Any<CancellationToken>()).Returns([entry]);
        InvalidOperationException failure = new("Unexpected retained verification failure.");
        _installer.EnsureInstalledAsync(entry, Arg.Any<CancellationToken>(), true)
            .Returns(Task.FromException<WorkloadInstallResult>(failure));

        Func<Task> ensure = () => CreateInstaller().EnsureDependencyAsync(
            Options(check: true), SetupDependency.Stack("node"), CancellationToken.None);

        (await ensure.Should().ThrowAsync<InvalidOperationException>()).Which.Should().BeSameAs(failure);
    }

    [Fact]
    public async Task EnsureDependencyAsync_ProfileWorkerVersionUnavailable_DoesNotSkipExplicitConstraint()
    {
        _store.GetWorkloadsAsync(Arg.Any<CancellationToken>()).Returns([]);

        SetupDependencyResult result = await CreateInstaller().EnsureDependencyAsync(
            Options(check: true), SetupDependency.Worker("node", VersionRange.Parse("[99.0.0]")), CancellationToken.None);

        result.Status.Should().Be(SetupDependencyStatus.Failed);
        result.Message.Should().Contain("[99.0.0]");
        await _installer.DidNotReceive().InstallFromCatalogAsync(Arg.Any<string>(), Arg.Any<NuGetVersion?>(),
            Arg.Any<string?>(), Arg.Any<bool?>(), Arg.Any<bool>(), Arg.Any<bool>(),
            Arg.Any<IProgress<WorkloadInstallProgress>?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task EnsureDependencyAsync_UnconstrainedUnpublishedWorker_RemainsOptional()
    {
        _store.GetWorkloadsAsync(Arg.Any<CancellationToken>()).Returns([]);

        SetupDependencyResult result = await CreateInstaller().EnsureDependencyAsync(
            Options(check: true), SetupDependency.Worker("java", null), CancellationToken.None);

        result.Status.Should().Be(SetupDependencyStatus.Skipped);
    }

    [Fact]
    public async Task EnsureDependencyAsync_UnbundledStack_RetainsOptionalIndependentTemplatesBehavior()
    {
        _store.GetWorkloadsAsync(Arg.Any<CancellationToken>()).Returns([Stack(declared: false)]);

        SetupDependencyResult result = await CreateInstaller().EnsureDependencyAsync(
            Options(), SetupDependency.Templates("node", BundleChannel.Stable), CancellationToken.None);

        result.Status.Should().Be(SetupDependencyStatus.Skipped);
        _catalog.ReceivedCalls().Should().ContainSingle();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task EnsureDependencyAsync_MissingHostExecutable_DoesNotReportReady(bool check)
    {
        WorkloadEntry entry = new()
        {
            PackageId = HostWorkloadPackage.CurrentPackageId,
            PackageVersion = "4.1.0",
            RuntimeIdentifier = HostWorkloadPackage.CurrentRuntimeIdentifier,
            Source = Source,
            LogicalPackage = new() { PackageId = HostWorkloadPackage.PackageId, PackageVersion = "4.1.0", Source = Source },
            Kind = WorkloadKind.Content,
        };
        _store.GetWorkloadsAsync(Arg.Any<CancellationToken>()).Returns([entry]);

        SetupDependencyResult result = await CreateInstaller().EnsureDependencyAsync(
            Options(check), SetupDependency.Host(null), CancellationToken.None);

        result.Status.Should().Be(SetupDependencyStatus.Failed);
        result.Message.Should().Contain("executable was not found").And.Contain(Source);
        _catalog.ReceivedCalls().Should().BeEmpty();
    }

    [Fact]
    public async Task EnsureDependencyAsync_PresentHostExecutable_ReportsReady()
    {
        WorkloadEntry entry = new()
        {
            PackageId = HostWorkloadPackage.CurrentPackageId,
            PackageVersion = "4.1.0",
            RuntimeIdentifier = HostWorkloadPackage.CurrentRuntimeIdentifier,
            LogicalPackage = new() { PackageId = HostWorkloadPackage.PackageId, PackageVersion = "4.1.0" },
            Kind = WorkloadKind.Content,
        };
        string contentRoot = WorkloadPackageLayout.GetContentRoot(
            _paths.GetInstallDirectory(entry.PackageId, entry.PackageVersion), entry.RuntimeIdentifier);
        _files.FileExists(HostProcessStartInfoFactory.ResolveExecutablePath(contentRoot)).Returns(true);
        _store.GetWorkloadsAsync(Arg.Any<CancellationToken>()).Returns([entry]);

        SetupDependencyResult result = await CreateInstaller().EnsureDependencyAsync(
            Options(check: true), SetupDependency.Host(null), CancellationToken.None);

        result.Status.Should().Be(SetupDependencyStatus.Satisfied);
    }

    private SetupDependencyInstaller CreateInstaller() => new(new TestInteractionService(), _store, _catalog, _installer, _paths, _files);

    private static SetupDependency Templates(WorkloadEntry entry) => SetupDependency.Templates("node", BundleChannel.Stable)
        with { StackSelection = new(entry.PackageId, entry.PackageVersion) };

    private static WorkloadEntry Stack(bool declared = true) => new()
    {
        PackageId = "Azure.Functions.Cli.Workloads.Node",
        PackageVersion = "1.0.0-preview.2",
        Source = Source,
        BundledTemplatesDeclared = declared,
        Kind = WorkloadKind.Content,
    };

    private static SetupCommandOptions Options(bool check = false, SetupInstallPolicy policy = SetupInstallPolicy.IfNeeded)
        => new(new DirectoryInfo(Path.GetTempPath()), ["node"], [], null, policy, IncludePrerelease: true,
            NonInteractive: true, AssumeYes: true, check, SetupOutputMode.Json);
}
