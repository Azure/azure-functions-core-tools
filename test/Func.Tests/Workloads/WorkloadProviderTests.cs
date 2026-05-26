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
    public void GetWorkloadsByPackageId_ReturnsMatchingKindVersions_CaseInsensitive()
    {
        RuntimeWorkloadInfo runtimeMatch = TestWorkloads.CreateInfo("Pkg.Shared", "1.0.0");
        RuntimeWorkloadInfo runtimeOther = TestWorkloads.CreateInfo("Pkg.Other", "1.0.0");
        ContentWorkloadInfo contentOld = TestWorkloads.CreateContentInfo("Pkg.Shared", "1.0.0");
        ContentWorkloadInfo contentNew = TestWorkloads.CreateContentInfo("pkg.shared", "2.0.0");

        var provider = new WorkloadProvider([runtimeMatch, runtimeOther, contentOld, contentNew]);

        IReadOnlyList<RuntimeWorkloadInfo> runtimeWorkloads = provider.GetRuntimeWorkloadsByPackageId("pkg.shared");
        IReadOnlyList<ContentWorkloadInfo> contentWorkloads = provider.GetContentWorkloadsByPackageId("PKG.SHARED");

        Assert.Same(runtimeWorkloads, provider.GetRuntimeWorkloadsByPackageId("Pkg.Shared"));
        Assert.Same(contentWorkloads, provider.GetContentWorkloadsByPackageId("pkg.shared"));
        Assert.Same(runtimeMatch, Assert.Single(runtimeWorkloads));
        Assert.Collection(
            contentWorkloads,
            workload => Assert.Same(contentOld, workload),
            workload => Assert.Same(contentNew, workload));
    }

    [Fact]
    public void GetWorkloadsByPackageId_NoMatch_ReturnsEmptyList()
    {
        var provider = new WorkloadProvider(
            [
                TestWorkloads.CreateInfo("Pkg.Runtime"),
                TestWorkloads.CreateContentInfo("Pkg.Content"),
            ]);

        Assert.Empty(provider.GetRuntimeWorkloadsByPackageId("Pkg.Missing"));
        Assert.Empty(provider.GetContentWorkloadsByPackageId("Pkg.Missing"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public void GetRuntimeWorkloadsByPackageId_InvalidPackageId_Throws(string? packageId)
    {
        var provider = new WorkloadProvider([]);

        Assert.ThrowsAny<ArgumentException>(() => provider.GetRuntimeWorkloadsByPackageId(packageId!));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public void GetContentWorkloadsByPackageId_InvalidPackageId_Throws(string? packageId)
    {
        var provider = new WorkloadProvider([]);

        Assert.ThrowsAny<ArgumentException>(() => provider.GetContentWorkloadsByPackageId(packageId!));
    }

    [Fact]
    public void Ctor_NullWorkloads_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new WorkloadProvider(null!));
    }
}
