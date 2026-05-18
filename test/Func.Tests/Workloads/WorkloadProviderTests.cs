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
        WorkloadInfo b = TestWorkloads.CreateInfo("Pkg.B");

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
    public void Ctor_NullWorkloads_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new WorkloadProvider(null!));
    }

    [Fact]
    public void FindByStack_AliasMatch_ReturnsWorkload()
    {
        WorkloadInfo dotnet = TestWorkloads.CreateInfo("Pkg.Dotnet") with { Aliases = ["dotnet", "dotnet-isolated"] };
        WorkloadInfo python = TestWorkloads.CreateInfo("Pkg.Python") with { Aliases = ["python"] };
        var provider = new WorkloadProvider([dotnet, python]);

        Assert.Same(dotnet, provider.FindByStack("DOTNET-ISOLATED"));
        Assert.Same(python, provider.FindByStack("python"));
    }

    [Fact]
    public void FindByStack_NoMatch_ReturnsNull()
    {
        WorkloadInfo dotnet = TestWorkloads.CreateInfo("Pkg.Dotnet") with { Aliases = ["dotnet"] };
        var provider = new WorkloadProvider([dotnet]);

        Assert.Null(provider.FindByStack("native"));
    }

    [Fact]
    public void FindByStack_NullOrWhitespace_ReturnsNull()
    {
        WorkloadInfo dotnet = TestWorkloads.CreateInfo("Pkg.Dotnet") with { Aliases = ["dotnet"] };
        var provider = new WorkloadProvider([dotnet]);

        Assert.Null(provider.FindByStack(null!));
        Assert.Null(provider.FindByStack("   "));
    }
}
