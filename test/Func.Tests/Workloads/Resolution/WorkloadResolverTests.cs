// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Projects;
using Azure.Functions.Cli.Workloads;
using Azure.Functions.Cli.Workloads.Resolution;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace Azure.Functions.Cli.Tests.Workloads.Resolution;

public sealed class WorkloadResolverTests : IDisposable
{
    private readonly DirectoryInfo _dir;

    public WorkloadResolverTests()
    {
        _dir = new DirectoryInfo(Path.Combine(Path.GetTempPath(), "func-resolver-tests-" + Guid.NewGuid().ToString("N")));
        _dir.Create();
    }

    public void Dispose()
    {
        try
        {
            if (_dir.Exists)
            {
                _dir.Delete(recursive: true);
            }
        }
        catch
        {
            // Best-effort cleanup.
        }
    }

    [Fact]
    public async Task StackPin_AliasMatch_Resolves()
    {
        WorkloadInfo dotnet = NewWorkload("Pkg.Dotnet", aliases: ["dotnet", "dotnet-isolated"]);
        WorkloadResolver resolver = NewResolver(workloads: [dotnet], stack: "dotnet-isolated");

        WorkloadResolution result = await resolver.ResolveAsync(_dir, CancellationToken.None);

        var resolved = Assert.IsType<WorkloadResolution.Resolved>(result);
        Assert.Same(dotnet, resolved.Workload);
        Assert.Contains("'dotnet-isolated'", resolved.Message);
    }

    [Fact]
    public async Task StackPin_NoAliasMatch_FallsThroughToResolvers()
    {
        WorkloadInfo go = NewWorkload("Pkg.Go", aliases: ["go"]);
        IProjectResolver goResolver = NewProjectResolver(EvaluationResult.Match("found go.mod"));
        WorkloadResolver resolver = NewResolver(
            workloads: [go],
            resolvers: [(go, goResolver)],
            stack: "native");

        WorkloadResolution result = await resolver.ResolveAsync(_dir, CancellationToken.None);

        var resolved = Assert.IsType<WorkloadResolution.Resolved>(result);
        Assert.Same(go, resolved.Workload);
        Assert.Contains("found go.mod", resolved.Message);
    }

    [Fact]
    public async Task StackPin_NoAliasMatch_NoResolverClaims_ReturnsNoneWithStackContext()
    {
        WorkloadInfo dotnet = NewWorkload("Pkg.Dotnet", aliases: ["dotnet"]);
        WorkloadResolver resolver = NewResolver(workloads: [dotnet], stack: "native");

        WorkloadResolution result = await resolver.ResolveAsync(_dir, CancellationToken.None);

        var none = Assert.IsType<WorkloadResolution.None>(result);
        Assert.Contains("'native'", none.Message);
        Assert.Contains("Pkg.Dotnet", none.Message);
    }

    [Fact]
    public async Task NoStackPin_SingleClaim_Resolves()
    {
        WorkloadInfo python = NewWorkload("Pkg.Python");
        IProjectResolver pyResolver = NewProjectResolver(EvaluationResult.Match("found requirements.txt"));
        WorkloadResolver resolver = NewResolver(
            workloads: [python],
            resolvers: [(python, pyResolver)]);

        WorkloadResolution result = await resolver.ResolveAsync(_dir, CancellationToken.None);

        var resolved = Assert.IsType<WorkloadResolution.Resolved>(result);
        Assert.Same(python, resolved.Workload);
        Assert.Contains("found requirements.txt", resolved.Message);
    }

    [Fact]
    public async Task NoStackPin_NoClaim_ReturnsNone()
    {
        WorkloadResolver resolver = NewResolver(workloads: []);

        WorkloadResolution result = await resolver.ResolveAsync(_dir, CancellationToken.None);

        Assert.IsType<WorkloadResolution.None>(result);
    }

    [Fact]
    public async Task NoStackPin_MultipleClaims_ReturnsNoneListingCandidates()
    {
        WorkloadInfo a = NewWorkload("Pkg.A");
        WorkloadInfo b = NewWorkload("Pkg.B");
        WorkloadResolver resolver = NewResolver(
            workloads: [a, b],
            resolvers:
            [
                (a, NewProjectResolver(EvaluationResult.Match("found foo"))),
                (b, NewProjectResolver(EvaluationResult.Match("found bar"))),
            ]);

        WorkloadResolution result = await resolver.ResolveAsync(_dir, CancellationToken.None);

        var none = Assert.IsType<WorkloadResolution.None>(result);
        Assert.Contains("Multiple installed workloads claim this directory", none.Message);
        Assert.Contains("Pkg.A", none.Message);
        Assert.Contains("Pkg.B", none.Message);
    }

    [Fact]
    public async Task MultipleResolversForSameWorkload_CountAsOneClaim()
    {
        WorkloadInfo workload = NewWorkload("Pkg.Multi");
        WorkloadResolver resolver = NewResolver(
            workloads: [workload],
            resolvers:
            [
                (workload, NewProjectResolver(EvaluationResult.Match("first hit"))),
                (workload, NewProjectResolver(EvaluationResult.Match("second hit"))),
            ]);

        WorkloadResolution result = await resolver.ResolveAsync(_dir, CancellationToken.None);

        var resolved = Assert.IsType<WorkloadResolution.Resolved>(result);
        Assert.Same(workload, resolved.Workload);
    }

    private static WorkloadResolver NewResolver(
        IReadOnlyList<WorkloadInfo>? workloads = null,
        IReadOnlyList<(WorkloadInfo Workload, IProjectResolver Resolver)>? resolvers = null,
        string? stack = null)
        => new(
            new StubWorkloadProvider(workloads ?? []),
            (resolvers ?? []).Select(d => new WorkloadProjectResolverContribution(d.Workload, d.Resolver)).ToList(),
            Options.Create(new StackOptions { Stack = stack }));

    private static WorkloadInfo NewWorkload(string packageId, IReadOnlyList<string>? aliases = null)
        => TestWorkloads.CreateInfo(packageId) with { Aliases = aliases ?? [] };

    private static IProjectResolver NewProjectResolver(EvaluationResult result)
    {
        IProjectResolver resolver = Substitute.For<IProjectResolver>();
        resolver.EvaluateAsync(Arg.Any<DirectoryInfo>(), Arg.Any<CancellationToken>())
            .Returns(result);
        return resolver;
    }

    private sealed class StubWorkloadProvider(IReadOnlyList<WorkloadInfo> workloads) : IWorkloadProvider
    {
        public IReadOnlyList<WorkloadInfo> GetWorkloads() => workloads;

        public WorkloadInfo? FindByStack(string stack)
        {
            foreach (WorkloadInfo workload in workloads)
            {
                foreach (string alias in workload.Aliases)
                {
                    if (string.Equals(alias, stack, StringComparison.OrdinalIgnoreCase))
                    {
                        return workload;
                    }
                }
            }

            return null;
        }
    }
}
