// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Workloads;

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
        second.Should().BeSameAs(first);
        first.Count.Should().Be(2);
        first[0].Should().BeSameAs(a);
        first[1].Should().BeSameAs(b);
    }

    [Fact]
    public void TypedViews_ReturnMatchingKinds_AsStableLists()
    {
        RuntimeWorkloadInfo runtime = TestWorkloads.CreateInfo("Pkg.Runtime");
        ContentWorkloadInfo content = TestWorkloads.CreateContentInfo("Pkg.Content");

        var provider = new WorkloadProvider([runtime, content]);

        IReadOnlyList<RuntimeWorkloadInfo> runtimeWorkloads = provider.GetRuntimeWorkloads();
        IReadOnlyList<ContentWorkloadInfo> contentWorkloads = provider.GetContentWorkloads();

        provider.GetRuntimeWorkloads().Should().BeSameAs(runtimeWorkloads);
        provider.GetContentWorkloads().Should().BeSameAs(contentWorkloads);
        runtimeWorkloads.Should().ContainSingle().Subject.Should().BeSameAs(runtime);
        contentWorkloads.Should().ContainSingle().Subject.Should().BeSameAs(content);
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

        provider.GetRuntimeWorkloadsByPackageId("Pkg.Shared").Should().BeSameAs(runtimeWorkloads);
        provider.GetContentWorkloadsByPackageId("pkg.shared").Should().BeSameAs(contentWorkloads);
        runtimeWorkloads.Should().ContainSingle().Subject.Should().BeSameAs(runtimeMatch);
        contentWorkloads.Should().SatisfyRespectively(workload => workload.Should().BeSameAs(contentOld), workload => workload.Should().BeSameAs(contentNew));
    }

    [Fact]
    public void GetWorkloadsByPackageId_NoMatch_ReturnsEmptyList()
    {
        var provider = new WorkloadProvider(
            [
                TestWorkloads.CreateInfo("Pkg.Runtime"),
                TestWorkloads.CreateContentInfo("Pkg.Content"),
            ]);

        provider.GetRuntimeWorkloadsByPackageId("Pkg.Missing").Should().BeEmpty();
        provider.GetContentWorkloadsByPackageId("Pkg.Missing").Should().BeEmpty();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public void GetRuntimeWorkloadsByPackageId_InvalidPackageId_Throws(string? packageId)
    {
        var provider = new WorkloadProvider([]);

        FluentActions.Invoking(() => provider.GetRuntimeWorkloadsByPackageId(packageId!)).Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public void GetContentWorkloadsByPackageId_InvalidPackageId_Throws(string? packageId)
    {
        var provider = new WorkloadProvider([]);

        FluentActions.Invoking(() => provider.GetContentWorkloadsByPackageId(packageId!)).Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Ctor_NullWorkloads_Throws()
    {
        FluentActions.Invoking(() => new WorkloadProvider(null!)).Should().ThrowExactly<ArgumentNullException>();
    }
}
