// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Workloads;
using Xunit;

namespace Azure.Functions.Cli.Tests.Workloads;

public class WorkloadProviderTests
{
    [Fact]
    public void GetWorkloads_ReturnsInjectedSet_AsStableList()
    {
        WorkloadInfo a = TestWorkloads.CreateInfo("Pkg.A");
        WorkloadInfo b = TestWorkloads.CreateContentInfo("Pkg.B");

        var provider = new WorkloadProvider([a, b]);

        IReadOnlyList<WorkloadInfo> first = provider.GetWorkloads();
        IReadOnlyList<WorkloadInfo> second = provider.GetWorkloads();

        // Materialized once at construction, so repeated calls return the
        // same list instance and never re-enumerate the source.
        Assert.Same(first, second);
        Assert.Equal(2, first.Count);
        Assert.Same(a, first[0]);
        Assert.Same(b, first[1]);
    }

    [Fact]
    public void TypedViews_ReturnMatchingKinds_AsStableLists()
    {
        RuntimeWorkloadInfo runtime = TestWorkloads.CreateInfo("Pkg.Runtime");
        ContentWorkloadInfo content = TestWorkloads.CreateContentInfo("Pkg.Content");

        var provider = new WorkloadProvider([runtime, content]);

        IReadOnlyList<RuntimeWorkloadInfo> runtimeWorkloads = provider.GetRuntimeWorkloads();
        IReadOnlyList<ContentWorkloadInfo> contentWorkloads = provider.GetContentWorkloads();

        Assert.Same(runtimeWorkloads, provider.GetRuntimeWorkloads());
        Assert.Same(contentWorkloads, provider.GetContentWorkloads());
        Assert.Same(runtime, Assert.Single(runtimeWorkloads));
        Assert.Same(content, Assert.Single(contentWorkloads));
    }

    [Fact]
    public void Ctor_NullWorkloads_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new WorkloadProvider(null!));
    }
}
