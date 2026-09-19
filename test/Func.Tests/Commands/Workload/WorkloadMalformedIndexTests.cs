// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.CommandLine;
using Azure.Functions.Cli.Commands.Workload;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Workloads;
using Azure.Functions.Cli.Workloads.Catalog;
using Azure.Functions.Cli.Workloads.Install;
using Azure.Functions.Cli.Workloads.Storage;
using Microsoft.Extensions.Options;
using NSubstitute;
using NuGet.Protocol;
using NuGet.Protocol.Core.Types;
using NuGet.Versioning;

namespace Azure.Functions.Cli.Tests.Commands.Workload;

public sealed class WorkloadMalformedIndexTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task SearchAndAliasInstall_MalformedIndex_IsGracefulWithOriginalCause(bool install, bool optionalFlag)
    {
        InvalidOperationException cause = new("Invalid service index token.");
        FatalProtocolException protocol = new("Unable to load service index.", cause);
        FailingIndexProvider provider = new(protocol);
        var options = Options.Create(new WorkloadCatalogOptions { IncludePrerelease = false });
        WorkloadCatalog catalog = new(options, new PackageSourceProvider(options), source => new NuGetProtocolSourceClient(
            new SourceRepository(source, [new Lazy<INuGetResourceProvider>(() => provider)])));
        TestInteractionService interaction = new();
        var store = Substitute.For<IWorkloadStore>();
        store.GetWorkloadsAsync(Arg.Any<CancellationToken>()).Returns([]);
        var installer = Substitute.For<IWorkloadInstaller>();
        WorkloadPackageSource packageSource = new(catalog, Substitute.For<IWorkloadPackageInspector>(), options);
        installer.InstallFromCatalogAsync(Arg.Any<string>(), Arg.Any<NuGetVersion?>(), Arg.Any<string?>(), Arg.Any<bool?>(),
            Arg.Any<bool>(), Arg.Any<bool>(), Arg.Any<IProgress<WorkloadInstallProgress>?>(), Arg.Any<CancellationToken>())
            .Returns(async call =>
            {
                await packageSource.ResolveAsync(call.ArgAt<string>(0), call.ArgAt<NuGetVersion?>(1), call.ArgAt<string?>(2),
                    call.ArgAt<bool?>(3), call.ArgAt<bool>(4), call.ArgAt<CancellationToken>(7));
                return await Task.FromException<WorkloadInstallResult>(
                    new InvalidOperationException("Malformed index must stop before installation."));
            });
        WorkloadUpdateCommand update = new(interaction, installer, store, options);
        Command command = install ? new WorkloadInstallCommand(interaction, installer, store, update, options)
            : new WorkloadSearchCommand(interaction, catalog, options);
        string[] arguments = optionalFlag ? ["node", install ? "--force" : "--json"] : ["node"];
        using CancellationTokenSource cancellation = new();

        var failure = await FluentActions.Awaiting(() => command.Parse(arguments).InvokeAsync(
            new InvocationConfiguration { EnableDefaultExceptionHandler = false }, cancellation.Token)).Should().ThrowExactlyAsync<GracefulException>();

        failure.Which.IsUserError.Should().BeTrue();
        failure.Which.Message.Should().Contain("malformed V3 service index").And.Contain("--source");
        failure.Which.InnerException.Should().BeOfType<InvalidWorkloadSourceException>()
            .Which.InnerException.Should().BeSameAs(protocol);
        failure.Which.GetBaseException().Should().BeSameAs(cause);
        provider.Calls.Should().Be(1);
        interaction.AllOutput.Should().NotContain("SUCCESS:").And.NotContain("No workloads found");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExactInstall_MalformedIndex_IsGracefulWithOriginalCause(bool pinnedVersion)
    {
        MalformedIndexFixture fixture = new();
        WorkloadUpdateCommand update = new(fixture.Interaction, fixture.Installer, fixture.Store, fixture.Options);
        WorkloadInstallCommand command = new(fixture.Interaction, fixture.Installer, fixture.Store, update, fixture.Options);
        string[] arguments = pinnedVersion
            ? ["broken.workload", "--exact", "--version", "2.0.0"]
            : ["broken.workload", "--exact"];
        using CancellationTokenSource cancellation = new();

        Exception? failure = await Record.ExceptionAsync(() => command.Parse(arguments).InvokeAsync(
            new InvocationConfiguration { EnableDefaultExceptionHandler = false }, cancellation.Token));

        fixture.AssertGracefulFailure(failure);
        fixture.Interaction.AllOutput.Should().NotContain("SUCCESS:").And.NotContain("already installed");
        fixture.Deployment.ReceivedCalls().Should().BeEmpty();
        fixture.Inspector.ReceivedCalls().Should().BeEmpty();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Update_MalformedIndex_IsGracefulWithOriginalCause(bool logical)
    {
        MalformedIndexFixture fixture = new();
        WorkloadEntry entry = CreateBrokenEntry(logical);
        fixture.Store.GetWorkloadsAsync(Arg.Any<CancellationToken>()).Returns([entry]);
        WorkloadOwnershipKind ownership = logical ? WorkloadOwnershipKind.Logical : WorkloadOwnershipKind.Explicit;
        fixture.Deployment.GetUpdateTargetAsync("broken.workload", null, ownership, Arg.Any<CancellationToken>()).Returns(entry);
        WorkloadUpdateCommand command = new(fixture.Interaction, fixture.Installer, fixture.Store, fixture.Options);
        using CancellationTokenSource cancellation = new();

        Exception? failure = await Record.ExceptionAsync(() => command.Parse(["broken"]).InvokeAsync(
            new InvocationConfiguration { EnableDefaultExceptionHandler = false }, cancellation.Token));

        fixture.AssertGracefulFailure(failure);
        await fixture.Deployment.Received(1).GetUpdateTargetAsync("broken.workload", null, ownership, Arg.Any<CancellationToken>());
        fixture.Deployment.ReceivedCalls().Should().ContainSingle();
        fixture.Inspector.ReceivedCalls().Should().BeEmpty();
        fixture.Interaction.AllOutput.Should().NotContain("SUCCESS:").And.NotContain("latest available").And.NotContain("No version");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UpdateAll_MalformedIndex_ReportsFailureAndContinuesToHealthyTarget(bool logical)
    {
        MalformedIndexFixture fixture = new();
        WorkloadEntry broken = CreateBrokenEntry(logical);
        WorkloadEntry healthy = new() { PackageId = "healthy.workload", PackageVersion = "1.0.0" };
        fixture.Store.GetWorkloadsAsync(Arg.Any<CancellationToken>()).Returns([broken, healthy]);
        WorkloadOwnershipKind ownership = logical ? WorkloadOwnershipKind.Logical : WorkloadOwnershipKind.Explicit;
        fixture.Deployment.GetUpdateTargetAsync("broken.workload", null, ownership, Arg.Any<CancellationToken>()).Returns(broken);
        List<string> attempts = [];
        var installer = Substitute.For<IWorkloadInstaller>();
        // Only the healthy result is simulated. The failed target uses the real installer and NuGet resource acquisition.
        installer.UpdateAsync(Arg.Any<string>(), Arg.Any<NuGetVersion?>(), Arg.Any<string?>(), Arg.Any<bool?>(),
            Arg.Any<bool>(), Arg.Any<IProgress<WorkloadInstallProgress>?>(), Arg.Any<CancellationToken>())
            .Returns(call => UpdateTargetAsync(call, WorkloadOwnershipKind.Explicit));
        installer.UpdateAsync(Arg.Any<string>(), Arg.Any<NuGetVersion?>(), Arg.Any<string?>(), Arg.Any<bool?>(),
            Arg.Any<bool>(), Arg.Any<WorkloadOwnershipKind>(), Arg.Any<IProgress<WorkloadInstallProgress>?>(), Arg.Any<CancellationToken>())
            .Returns(call => UpdateTargetAsync(call, call.ArgAt<WorkloadOwnershipKind>(5)));
        WorkloadUpdateCommand command = new(fixture.Interaction, installer, fixture.Store, fixture.Options);
        using CancellationTokenSource cancellation = new();
        int? exit = null;

        Exception? failure = await Record.ExceptionAsync(async () => exit = await command.Parse(["--all"]).InvokeAsync(
            new InvocationConfiguration { EnableDefaultExceptionHandler = false }, cancellation.Token));

        fixture.Provider.Calls.Should().Be(1, "the real Find provider must reach the failing service-index resource");
        failure.Should().BeNull();
        exit.Should().Be(1);
        attempts.Should().Equal("broken.workload", "healthy.workload");
        fixture.Interaction.Lines.Where(line => line.StartsWith("ERROR:", StringComparison.Ordinal)).Should().ContainSingle()
            .Which.Should().Contain("Update failed for 'broken.workload'").And.Contain("malformed V3 service index").And.Contain("--source");
        fixture.Interaction.Lines.Where(line => line.StartsWith("SUCCESS:", StringComparison.Ordinal)).Should().ContainSingle()
            .Which.Should().Contain("Updated workload 'healthy.workload' from 1.0.0 to 2.0.0");
        fixture.Deployment.ReceivedCalls().Should().ContainSingle();
        fixture.Inspector.ReceivedCalls().Should().BeEmpty();

        Task<WorkloadUpdateResult> UpdateTargetAsync(NSubstitute.Core.CallInfo call, WorkloadOwnershipKind targetOwnership)
        {
            string id = call.ArgAt<string>(0);
            attempts.Add(id);
            return id == healthy.PackageId
                ? Task.FromResult(new WorkloadUpdateResult(
                    new WorkloadEntry { PackageId = healthy.PackageId, PackageVersion = "2.0.0" }, healthy.PackageVersion, false))
                : fixture.Installer.UpdateAsync(id, call.ArgAt<NuGetVersion?>(1), call.ArgAt<string?>(2), call.ArgAt<bool?>(3),
                    call.ArgAt<bool>(4), targetOwnership, call.Arg<IProgress<WorkloadInstallProgress>?>(), call.Arg<CancellationToken>());
        }
    }

    [Fact]
    public async Task Download_MalformedIndex_IsSourceErrorWithOriginalCause()
    {
        MalformedIndexFixture fixture = new();
        // Seed a previously resolved identity to reach download acquisition without failing version lookup first.
        ResolvedPackage package = new("broken.workload", new NuGetVersion(2, 0, 0), new PackageSourceProvider(fixture.Options).GetSource());
        using CancellationTokenSource cancellation = new();

        Exception? failure = await Record.ExceptionAsync(async () =>
        {
            await using Stream stream = await fixture.Catalog.DownloadAsync(package, cancellation.Token);
        });

        fixture.AssertSourceFailure(failure);
    }

    private static WorkloadEntry CreateBrokenEntry(bool logical)
        => new()
        {
            PackageId = logical ? "broken.workload.win-x64" : "broken.workload",
            PackageVersion = "1.0.0",
            Aliases = logical ? [] : ["broken"],
            IsImplicitlyInstalled = logical,
            LogicalPackage = logical ? new LogicalPackage
            {
                PackageId = "broken.workload",
                PackageVersion = "1.0.0",
                Aliases = ["broken"],
            } : null,
        };

    // Exercises command/installer resolution, not archive inspection, deployment, or real HTTP index parsing.
    private sealed class MalformedIndexFixture
    {
        public MalformedIndexFixture()
        {
            Protocol = new FatalProtocolException("Unable to load service index.", Cause);
            Provider = new FailingIndexProvider(Protocol);
            Catalog = new WorkloadCatalog(Options, new PackageSourceProvider(Options), source => new NuGetProtocolSourceClient(
                new SourceRepository(source,
                [
                    new Lazy<INuGetResourceProvider>(() => Provider),
                    new Lazy<INuGetResourceProvider>(() => new HttpFileSystemBasedFindPackageByIdResourceProvider()),
                ])));
            WorkloadPackageSource packageSource = new(Catalog, Inspector, Options);
            Installer = new WorkloadInstaller(packageSource, Inspector,
                new WorkloadRidPackageSelector(Substitute.For<IWorkloadRuntimeIdentifierProvider>()), Deployment);
            Store.GetWorkloadsAsync(Arg.Any<CancellationToken>()).Returns([]);
        }

        public InvalidOperationException Cause { get; } = new("Invalid service index token.");
        public FatalProtocolException Protocol { get; }
        public FailingIndexProvider Provider { get; }
        public IOptions<WorkloadCatalogOptions> Options { get; } = Microsoft.Extensions.Options.Options.Create(
            new WorkloadCatalogOptions { Source = "https://malformed.test/v3/index.json", IncludePrerelease = false });
        public WorkloadCatalog Catalog { get; }
        public WorkloadInstaller Installer { get; }
        public IWorkloadStore Store { get; } = Substitute.For<IWorkloadStore>();
        public IWorkloadDeploymentService Deployment { get; } = Substitute.For<IWorkloadDeploymentService>();
        public IWorkloadPackageInspector Inspector { get; } = Substitute.For<IWorkloadPackageInspector>();
        public TestInteractionService Interaction { get; } = new();

        public void AssertGracefulFailure(Exception? failure)
        {
            AssertResourceBoundary(failure);
            GracefulException graceful = failure.Should().BeOfType<GracefulException>().Which;
            graceful.IsUserError.Should().BeTrue();
            graceful.Message.Should().Contain("malformed V3 service index").And.Contain("--source");
            AssertSourceFailure(graceful.InnerException);
        }

        public void AssertSourceFailure(Exception? failure)
        {
            AssertResourceBoundary(failure);
            InvalidWorkloadSourceException sourceFailure = failure.Should().BeOfType<InvalidWorkloadSourceException>().Which;
            sourceFailure.Message.Should().Contain("malformed V3 service index").And.Contain("--source");
            sourceFailure.InnerException.Should().BeSameAs(Protocol);
        }

        private void AssertResourceBoundary(Exception? failure)
        {
            Provider.Calls.Should().Be(1, "the real Find provider must reach the failing service-index resource");
            failure.Should().NotBeNull();
            failure!.GetBaseException().Should().BeSameAs(Cause);
        }
    }

    private sealed class FailingIndexProvider(Exception failure) : INuGetResourceProvider
    {
        public int Calls { get; private set; }
        public Type ResourceType => typeof(ServiceIndexResourceV3);
        public string Name => nameof(FailingIndexProvider);
        public IEnumerable<string> Before => [];
        public IEnumerable<string> After => [];

        public Task<Tuple<bool, INuGetResource?>> TryCreate(SourceRepository source, CancellationToken token)
        {
            Calls++;
            return Task.FromException<Tuple<bool, INuGetResource?>>(failure);
        }
    }
}