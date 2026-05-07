// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Workloads;
using Azure.Functions.Cli.Workloads.Loading;
using Azure.Functions.Cli.Workloads.Storage;
using NSubstitute;
using Xunit;

namespace Azure.Functions.Cli.Tests.Workloads;

public class WorkloadProviderTests
{
    [Fact]
    public async Task GetWorkloadsAsync_ReturnsLoadedWorkloads_FromStoreEntries()
    {
        var entries = new[]
        {
            new WorkloadEntry
            {
                PackageId = "Pkg.A",
                PackageVersion = "1.0.0",
                EntryPoint = new EntryPointSpec { AssemblyPath = "A.dll", Type = "A.T" },
            },
        };
        var instance = new TestWorkload();
        var loaded = new[]
        {
            new WorkloadInfo(instance, "Pkg.A", "1.0.0", [], instance.DisplayName, instance.Description),
        };
        IWorkloadStore store = Substitute.For<IWorkloadStore>();
        store.GetWorkloadsAsync(Arg.Any<CancellationToken>()).Returns(entries);
        IWorkloadLoader loader = Substitute.For<IWorkloadLoader>();
        loader.Load(entries).Returns(loaded);

        var provider = new WorkloadProvider(store, loader);

        IReadOnlyList<WorkloadInfo> result = await provider.GetWorkloadsAsync();

        Assert.Same(loaded, result);
    }

    [Fact]
    public async Task GetWorkloadsAsync_CachesResult_AcrossCalls()
    {
        IWorkloadStore store = Substitute.For<IWorkloadStore>();
        store.GetWorkloadsAsync(Arg.Any<CancellationToken>())
            .Returns([]);
        IWorkloadLoader loader = Substitute.For<IWorkloadLoader>();
        loader.Load(Arg.Any<IReadOnlyList<WorkloadEntry>>())
            .Returns([]);
        var provider = new WorkloadProvider(store, loader);

        IReadOnlyList<WorkloadInfo> first = await provider.GetWorkloadsAsync();
        IReadOnlyList<WorkloadInfo> second = await provider.GetWorkloadsAsync();
        IReadOnlyList<WorkloadInfo> third = await provider.GetWorkloadsAsync();

        Assert.Same(first, second);
        Assert.Same(second, third);
        await store.Received(1).GetWorkloadsAsync(Arg.Any<CancellationToken>());
        loader.Received(1).Load(Arg.Any<IReadOnlyList<WorkloadEntry>>());
    }

    [Fact]
    public async Task GetWorkloadsAsync_ConcurrentCallers_LoadOnlyOnce()
    {
        // Have the store block until released so multiple callers all queue
        // behind the gate, then assert the loader is still only invoked once.
        var release = new TaskCompletionSource<IReadOnlyList<WorkloadEntry>>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        IWorkloadStore store = Substitute.For<IWorkloadStore>();
        store.GetWorkloadsAsync(Arg.Any<CancellationToken>()).Returns(release.Task);
        IWorkloadLoader loader = Substitute.For<IWorkloadLoader>();
        loader.Load(Arg.Any<IReadOnlyList<WorkloadEntry>>())
            .Returns([]);
        var provider = new WorkloadProvider(store, loader);

        Task<IReadOnlyList<WorkloadInfo>>[] callers = Enumerable.Range(0, 10)
            .Select(_ => Task.Run(async () => await provider.GetWorkloadsAsync()))
            .ToArray();

        // Give the callers time to queue behind the gate before releasing.
        await Task.Delay(50);
        release.SetResult([]);

        IReadOnlyList<WorkloadInfo>[] results = await Task.WhenAll(callers);

        Assert.All(results, r => Assert.Same(results[0], r));
        loader.Received(1).Load(Arg.Any<IReadOnlyList<WorkloadEntry>>());
    }

    [Fact]
    public async Task GetWorkloadsAsync_PassesCancellationToken_ToStore()
    {
        IWorkloadStore store = Substitute.For<IWorkloadStore>();
        store.GetWorkloadsAsync(Arg.Any<CancellationToken>())
            .Returns([]);
        IWorkloadLoader loader = Substitute.For<IWorkloadLoader>();
        loader.Load(Arg.Any<IReadOnlyList<WorkloadEntry>>())
            .Returns([]);
        var provider = new WorkloadProvider(store, loader);
        using var cts = new CancellationTokenSource();

        await provider.GetWorkloadsAsync(cts.Token);

        await store.Received(1).GetWorkloadsAsync(cts.Token);
    }

    [Fact]
    public void Ctor_NullStore_Throws()
    {
        IWorkloadLoader loader = Substitute.For<IWorkloadLoader>();
        Assert.Throws<ArgumentNullException>(() => new WorkloadProvider(null!, loader));
    }

    [Fact]
    public void Ctor_NullLoader_Throws()
    {
        IWorkloadStore store = Substitute.For<IWorkloadStore>();
        Assert.Throws<ArgumentNullException>(() => new WorkloadProvider(store, null!));
    }

    private sealed class TestWorkload : global::Azure.Functions.Cli.Workloads.Workload
    {
        public override string DisplayName => "Test";

        public override string Description => "Test workload.";

        public override void Configure(FunctionsCliBuilder builder)
        {
        }
    }
}
