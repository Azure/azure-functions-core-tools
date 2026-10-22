// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Templates.Acquisition;
using Azure.Functions.Cli.Templates.Engine;
using Azure.Functions.Cli.Workloads.Storage;

namespace Azure.Functions.Cli.Tests.Templates.Acquisition;

public sealed class BasicAcquisitionCoordinationTests
{
    [Fact]
    public async Task PreparedCompanionBytesChangeWhileWaiting_RejectsBeforeStackEffects()
    {
        using var fixture = new BasicAcquisitionFixture();
        var fileSystem = new PhysicalFileSystem();
        TrackingLifecycle lifecycle = new(new BasicTemplateLifecycle(fixture.Templates, fixture.Payload,
            new BasicArtifactInspector(fileSystem), fixture.Ownership, fileSystem));
        GateCoordinator gate = new(new FileWorkloadOperationCoordinator(fixture.Paths));
        BasicAcquisitionRunner runner = new(fixture.Catalog, new(), fixture.SourceProvider, fixture.Store, fixture.Paths,
            fixture.Installer, lifecycle, fileSystem, gate);
        Task<BasicAcquisitionResult> running = runner.RunAsync(fixture.Request(), CancellationToken.None);
        await gate.Prepared.Task.WaitAsync(TimeSpan.FromSeconds(30));
        await File.WriteAllTextAsync(lifecycle.Prepared!.Archive!, "changed after private native preflight");

        gate.Admit.SetResult();
        var result = await running.WaitAsync(TimeSpan.FromSeconds(30));

        result.Status.Should().Be(BasicAcquisitionStatus.Rejected);
        result.Failure.Should().Be(BasicAcquisitionFailure.PlanChanged);
        result.Completed.Should().BeEmpty();
        File.Exists(fixture.Paths.WorkloadRegistryPath).Should().BeFalse();
        Directory.Exists(fixture.Paths.WorkloadsRoot).Should().BeFalse();
        fixture.HiveHashes().Should().BeEmpty();
    }

    [Fact]
    public async Task OldPreparedB_WaitsForActualACommitAndNativeFailure_ThenRejectsRetainedDisjointRequirement()
    {
        using var fixture = new BasicAcquisitionFixture();
        GateCoordinator gate = new(new FileWorkloadOperationCoordinator(fixture.Paths));
        Task<BasicAcquisitionResult> b = fixture.Runner(coordinator: gate).RunAsync(fixture.Request("Fixture.StackB"), CancellationToken.None);
        await gate.Prepared.Task.WaitAsync(TimeSpan.FromSeconds(30));
        (await fixture.Store.GetWorkloadsAsync()).Should().BeEmpty();
        CommitFailureInspector inspector = new(new PhysicalFileSystem());

        Task<BasicAcquisitionResult> a = fixture.Runner(inspector).RunAsync(fixture.Request(), CancellationToken.None);
        await inspector.Live.Task.WaitAsync(TimeSpan.FromSeconds(30));
        var retained = await fixture.Store.GetWorkloadsAsync();
        retained.Should().ContainSingle().Which.LogicalPackage!.PackageId.Should().Be("fixture.stacka");
        gate.Admit.SetResult();
        await gate.Waiting.Task.WaitAsync(TimeSpan.FromSeconds(30));
        b.IsCompleted.Should().BeFalse();
        inspector.Fail.SetResult();
        var resultA = await a.WaitAsync(TimeSpan.FromSeconds(30));
        var resultB = await b.WaitAsync(TimeSpan.FromSeconds(30));

        resultA.Status.Should().Be(BasicAcquisitionStatus.PartialFailure);
        resultA.Completed.Should().ContainSingle().Which.PackageId.Should().Be("Fixture.StackA");
        resultB.Status.Should().Be(BasicAcquisitionStatus.Rejected);
        resultB.Failure.Should().Be(BasicAcquisitionFailure.RangeConflict);
        (await fixture.Store.GetWorkloadsAsync()).Should().BeEquivalentTo(retained);
        (await fixture.ReadTemplatesAsync()).Should().BeEmpty();
        fixture.HiveHashes().Should().BeEmpty();
        Directory.Exists(fixture.Paths.GetInstallDirectory("fixture.stackb.test-rid", "1.0.0")).Should().BeFalse();
        fixture.Payload.Opened.Should().Equal(["2.0.0", "1.0.0"], "B prepared its old plan before A committed; no replacement was fetched under the lease");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PreparationOrAdmissionCancellation_LeavesBothLiveStoresUnchanged(bool waiting)
    {
        using var fixture = new BasicAcquisitionFixture();
        using var cancellation = new CancellationTokenSource();
        if (!waiting)
        {
            fixture.Payload.Opening = _ => cancellation.Cancel();
            var preparedCancellation = await fixture.Runner().RunAsync(fixture.Request(), cancellation.Token);
            preparedCancellation.Status.Should().Be(BasicAcquisitionStatus.Canceled);
        }
        else
        {
            using var holder = await new FileWorkloadOperationCoordinator(fixture.Paths).AcquireAsync(CancellationToken.None);
            CancellationCoordinator coordinator = new(new FileWorkloadOperationCoordinator(fixture.Paths), cancellation);
            var admissionCancellation = await fixture.Runner(coordinator: coordinator).RunAsync(fixture.Request(), cancellation.Token);
            admissionCancellation.Status.Should().Be(BasicAcquisitionStatus.Canceled);
        }

        (await fixture.Store.GetWorkloadsAsync()).Should().BeEmpty();
        File.Exists(fixture.Paths.WorkloadRegistryPath).Should().BeFalse();
        fixture.HiveHashes().Should().BeEmpty();
        Directory.Exists(fixture.Paths.WorkloadsRoot).Should().BeFalse();
    }

    [Fact]
    public async Task FreshCompanionPreparationFailure_DoesNotCommitStackOrRetainRequirements()
    {
        using var fixture = new BasicAcquisitionFixture();
        fixture.Payload.Missing = true;

        var failure = await fixture.Runner().RunAsync(fixture.Request(), CancellationToken.None);
        fixture.Payload.Missing = false;
        var b = await fixture.Runner().RunAsync(fixture.Request("Fixture.StackB"), CancellationToken.None);

        failure.Status.Should().Be(BasicAcquisitionStatus.Rejected);
        failure.Completed.Should().BeEmpty();
        b.Status.Should().Be(BasicAcquisitionStatus.Completed);
        (await fixture.Store.GetWorkloadsAsync()).Should().ContainSingle().Which.LogicalPackage!.PackageId.Should().Be("fixture.stackb");
    }

    private sealed class GateCoordinator(IWorkloadOperationCoordinator inner) : IWorkloadOperationCoordinator
    {
        public TaskCompletionSource Prepared { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Admit { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Waiting { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task<IWorkloadOperation> AcquireAsync(CancellationToken cancellationToken, Action? waiting = null)
        {
            Prepared.SetResult();
            await Admit.Task.WaitAsync(cancellationToken);
            return await inner.AcquireAsync(cancellationToken, () =>
            {
                Waiting.SetResult();
                waiting?.Invoke();
            });
        }

    }

    private sealed class TrackingLifecycle(IBasicTemplateLifecycle inner) : IBasicTemplateLifecycle
    {
        public PreparedBasicTemplate? Prepared { get; private set; }
        public Task<BasicTemplateSelection> SelectAsync(BasicCompanionPlan plan, TemplateEngineContext context, CancellationToken cancellationToken)
            => inner.SelectAsync(plan, context, cancellationToken);
        public Task<BasicTemplateSelection> SelectOwnedUpdateAsync(BasicCompanionPlan plan, BasicTemplateArtifact after,
            string selectedProjectIdentity, NativeTemplateOwnerIdentity owner, string workloadInventory,
            TemplateEngineContext context, CancellationToken cancellationToken)
            => inner.SelectOwnedUpdateAsync(plan, after, selectedProjectIdentity, owner, workloadInventory, context, cancellationToken);
        public Task<BasicTemplateSelection> RevalidateOwnedUpdateAsync(BasicCompanionPlan plan, BasicOwnedTemplateUpdate update,
            TemplateEngineContext context, CancellationToken cancellationToken)
            => inner.RevalidateOwnedUpdateAsync(plan, update, context, cancellationToken);
        public Task<bool> EnsureAsync(BasicTemplateArtifact artifact, TemplateEngineContext context, bool offline, CancellationToken cancellationToken)
            => inner.EnsureAsync(artifact, context, offline, cancellationToken);
        public async Task<PreparedBasicTemplate> PrepareAsync(BasicTemplateSelection selection, TemplateEngineContext context,
            bool offline, CancellationToken cancellationToken)
        {
            Prepared = await inner.PrepareAsync(selection, context, offline, cancellationToken);
            return Prepared;
        }
        public Task RevalidatePreparedAsync(PreparedBasicTemplate prepared, TemplateEngineContext context, CancellationToken cancellationToken)
            => inner.RevalidatePreparedAsync(prepared, context, cancellationToken);
        public Task<bool> CommitAsync(PreparedBasicTemplate prepared, TemplateEngineContext context, CancellationToken cancellationToken)
            => inner.CommitAsync(prepared, context, cancellationToken);
    }

    private sealed class CancellationCoordinator(IWorkloadOperationCoordinator inner, CancellationTokenSource cancellation) : IWorkloadOperationCoordinator
    {
        public Task<IWorkloadOperation> AcquireAsync(CancellationToken cancellationToken, Action? waiting = null)
            => inner.AcquireAsync(cancellationToken, () =>
            {
                cancellation.Cancel();
                waiting?.Invoke();
            });
    }

    private sealed class CommitFailureInspector(IFileSystem fileSystem) : IBasicArtifactInspector
    {
        private readonly BasicArtifactInspector _inner = new(fileSystem);
        private int _calls;
        public TaskCompletionSource Live { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Fail { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task ValidateAsync(string archive, BasicTemplateArtifact artifact, CancellationToken cancellationToken)
        {
            await _inner.ValidateAsync(archive, artifact, cancellationToken);
            if (++_calls == 3)
            {
                Live.SetResult();
                await Fail.Task.WaitAsync(cancellationToken);
                throw new BasicAcquisitionException(BasicAcquisitionFailure.InstallFailed, "Owned fixture rejects actual companion completion.");
            }
        }
    }
}
