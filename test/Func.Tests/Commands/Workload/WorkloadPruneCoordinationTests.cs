// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.CommandLine;
using Azure.Functions.Cli.Commands.Workload;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Tests.Templates.Acquisition;
using Azure.Functions.Cli.Tests.Workloads.Storage;
using Azure.Functions.Cli.Workloads;
using Azure.Functions.Cli.Workloads.Catalog;
using Azure.Functions.Cli.Workloads.Discovery;
using Azure.Functions.Cli.Workloads.Install;
using Azure.Functions.Cli.Workloads.Storage;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Azure.Functions.Cli.Tests.Commands.Workload;

public sealed class WorkloadPruneCoordinationTests
{
    [Fact]
    public async Task Prune_PostPersistenceFailure_ReportsNoSuccessAndKeepsRecoveryGate()
    {
        using var fixture = new BasicAcquisitionFixture();
        await SeedAsync(fixture, logical: false);
        var faultingStore = Substitute.For<IWorkloadStore>();
        faultingStore.GetWorkloadsAsync(Arg.Any<CancellationToken>())
            .Returns(call => fixture.Store.GetWorkloadsAsync(call.Arg<CancellationToken>()));
        faultingStore.RemoveOwnershipAsync(Arg.Any<IWorkloadOperation>(), Arg.Any<string>(), Arg.Any<string>(),
                Arg.Any<WorkloadOwnershipKind>(), Arg.Any<CancellationToken>())
            .Returns<Task<WorkloadOwnershipRemovalResult>>(async call =>
            {
                await fixture.Store.RemoveOwnershipAsync(call.Arg<IWorkloadOperation>(), call.ArgAt<string>(1), call.ArgAt<string>(2),
                    call.Arg<WorkloadOwnershipKind>(), call.Arg<CancellationToken>());
                throw new IOException("Owned prune registry read-back failure.");
            });
        var coordinator = new FileWorkloadOperationCoordinator(fixture.Paths);
        var interaction = new TestInteractionService();
        WorkloadPruneCommand command = new(interaction, Installer(fixture, coordinator, faultingStore), fixture.Store, coordinator);

        Func<Task> prune = () => InvokeAsync(command, "fixture.stacka.test-rid", CancellationToken.None);

        await prune.Should().ThrowAsync<IOException>().WithMessage("*prune registry read-back*");
        var row = (await fixture.Store.GetWorkloadsAsync()).Should().ContainSingle().Subject;
        row.PackageVersion.Should().Be("2.0.0");
        interaction.Lines.Should().NotContain(line => line.Contains("Pruned workload", StringComparison.Ordinal));
        Directory.Exists(fixture.Paths.GetInstallDirectory("fixture.stacka.test-rid", "1.0.0")).Should().BeTrue();
        Directory.Exists(fixture.Paths.GetInstallDirectory("fixture.stacka.test-rid", "2.0.0")).Should().BeTrue();
        Func<Task> next = async () =>
        {
            using var operation = await coordinator.AcquireAsync(CancellationToken.None);
        };
        await next.Should().ThrowAsync<WorkloadRecoveryRequiredException>();
    }

    [Theory]
    [InlineData(false, "current")]
    [InlineData(true, "current")]
    [InlineData(false, "absent")]
    [InlineData(true, "absent")]
    [InlineData(false, "obsolete")]
    [InlineData(true, "obsolete")]
    public async Task Prune_WaitsForParticipatingUninstall_UsesCurrentSelection(bool logical, string change)
    {
        using var fixture = new BasicAcquisitionFixture();
        await SeedAsync(fixture, logical);
        if (change == "obsolete")
        {
            await fixture.Installer.InstallFromPackageAsync(fixture.Archive("Fixture.StackB.test-rid"));
        }
        string identifier = logical ? "fixture.stacka" : "fixture.stacka.test-rid";
        string removedId = change == "obsolete" ? "fixture.stackb.test-rid" : identifier;
        string removedVersion = change == "absent" || change == "obsolete" ? "1.0.0" : "2.0.0";
        string removedOwner = logical && change != "obsolete" ? "logical" : "explicit";
        int downloads = fixture.Downloads.Count;
        await using var actor = OwnedWorkloadProcess.Start("uninstall-held", fixture.Paths.Home, removedId, removedVersion, removedOwner);
        await actor.WaitForAsync("BEFORE_REGISTRY");
        WaitingCoordinator coordinator = new(new FileWorkloadOperationCoordinator(fixture.Paths));
        var interaction = new TestInteractionService();
        WorkloadPruneCommand command = new(interaction, Installer(fixture, coordinator), fixture.Store, coordinator);
        Task<int> pruning = InvokeAsync(command, identifier, CancellationToken.None);
        await coordinator.Waiting.Task.WaitAsync(TimeSpan.FromSeconds(30));
        pruning.IsCompleted.Should().BeFalse();

        await actor.SignalAsync();
        await actor.WaitForExitAsync();
        int exit = await pruning.WaitAsync(TimeSpan.FromSeconds(30));

        exit.Should().Be(0);
        var row = (await fixture.Store.GetWorkloadsAsync()).Should().ContainSingle().Subject;
        row.PackageId.Should().Be("fixture.stacka.test-rid");
        row.PackageVersion.Should().Be(change == "current" ? "1.0.0" : "2.0.0");
        row.LogicalPackage?.PackageId.Should().Be(logical ? "fixture.stacka" : null);
        File.ReadAllText(Path.Combine(fixture.Paths.GetInstallDirectory(row.PackageId, row.PackageVersion), "tools", "test-rid", "fixture.txt"))
            .Should().Be("Installation fixture, not an activated stack implementation.");
        interaction.Lines.Count(line => line.Contains("Pruned workload", StringComparison.Ordinal)).Should().Be(change == "obsolete" ? 1 : 0);
        fixture.Downloads.Should().HaveCount(downloads);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Prune_CanceledWhileWaiting_GrantsNoRemovalAuthority(bool logical)
    {
        using var fixture = new BasicAcquisitionFixture();
        await SeedAsync(fixture, logical);
        var before = await fixture.Store.GetWorkloadsAsync();
        using var owner = await new FileWorkloadOperationCoordinator(fixture.Paths).AcquireAsync(CancellationToken.None);
        using var cancellation = new CancellationTokenSource();
        WaitingCoordinator coordinator = new(new FileWorkloadOperationCoordinator(fixture.Paths));
        var interaction = new TestInteractionService();
        WorkloadPruneCommand command = new(interaction, Installer(fixture, coordinator), fixture.Store, coordinator);
        Task<int> pruning = InvokeAsync(command, logical ? "fixture.stacka" : "fixture.stacka.test-rid", cancellation.Token);
        await coordinator.Waiting.Task.WaitAsync(TimeSpan.FromSeconds(30));

        cancellation.Cancel();
        Func<Task> finish = async () => await pruning.WaitAsync(TimeSpan.FromSeconds(30));

        await finish.Should().ThrowAsync<OperationCanceledException>();
        (await fixture.Store.GetWorkloadsAsync()).Should().BeEquivalentTo(before);
        interaction.Lines.Should().NotContain(line => line.Contains("Pruned workload", StringComparison.Ordinal));
        Directory.Exists(fixture.Paths.GetInstallDirectory("fixture.stacka.test-rid", "1.0.0")).Should().BeTrue();
        Directory.Exists(fixture.Paths.GetInstallDirectory("fixture.stacka.test-rid", "2.0.0")).Should().BeTrue();
    }

    private static async Task SeedAsync(BasicAcquisitionFixture fixture, bool logical)
    {
        foreach (string version in new[] { "1.0.0", "2.0.0" })
        {
            if (logical)
            {
                await fixture.SeedStackAsync("Fixture.StackA", version);
            }
            else
            {
                await fixture.Installer.InstallFromPackageAsync(fixture.Archive("Fixture.StackA.test-rid", version));
            }
        }
    }

    private static WorkloadInstaller Installer(BasicAcquisitionFixture fixture, IWorkloadOperationCoordinator coordinator)
        => Installer(fixture, coordinator, fixture.Store);

    private static WorkloadInstaller Installer(BasicAcquisitionFixture fixture, IWorkloadOperationCoordinator coordinator, IWorkloadStore store)
    {
        var runtime = Substitute.For<IWorkloadRuntimeIdentifierProvider>();
        runtime.Current.Returns("test-rid");
        PhysicalFileSystem fileSystem = new();
        WorkloadMetadataReader metadata = new();
        WorkloadRidPackageSelector selector = new(runtime);
        WorkloadPackageInspector inspector = new(metadata, selector);
        return new(new WorkloadPackageSource(fixture.WorkloadCatalog, inspector, Options.Create(new WorkloadCatalogOptions())),
            inspector, selector, new WorkloadDeploymentService(fixture.Paths, store, metadata, coordinator, fileSystem, Substitute.For<IBundledTemplateInstaller>()),
            coordinator, store, fileSystem);
    }

    private static Task<int> InvokeAsync(WorkloadPruneCommand command, string identifier, CancellationToken cancellationToken)
    {
        RootCommand root = [command];
        return root.Parse([command.Name, identifier, "--exact"]).InvokeAsync(new() { EnableDefaultExceptionHandler = false }, cancellationToken);
    }

    private sealed class WaitingCoordinator(IWorkloadOperationCoordinator inner) : IWorkloadOperationCoordinator
    {
        public TaskCompletionSource Waiting { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<IWorkloadOperation> AcquireAsync(CancellationToken cancellationToken, Action? waiting = null)
            => inner.AcquireAsync(cancellationToken, () =>
            {
                Waiting.TrySetResult();
                waiting?.Invoke();
            });
    }
}
