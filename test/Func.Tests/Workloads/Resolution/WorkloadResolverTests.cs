// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Workloads;
using Azure.Functions.Cli.Workloads.Resolution;
using NSubstitute;
using Xunit;

namespace Azure.Functions.Cli.Tests.Workloads.Resolution;

public sealed class WorkloadResolverTests : IDisposable
{
    private readonly DirectoryInfo _dir;
    private readonly ILocalSettingsReader _settings = Substitute.For<ILocalSettingsReader>();

    public WorkloadResolverTests()
    {
        _dir = new DirectoryInfo(Path.Combine(Path.GetTempPath(), "func-resolver-tests-" + Guid.NewGuid().ToString("N")));
        _dir.Create();
        File.WriteAllText(Path.Combine(_dir.FullName, "host.json"), "{}");

        _settings.ReadWorkerRuntime(Arg.Any<DirectoryInfo>()).Returns((string?)null);
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
            // Best-effort cleanup; temp dirs are reclaimed by the OS.
        }
    }

    [Fact]
    public async Task Selector_SingleAliasMatch_Resolves()
    {
        WorkloadInfo dotnet = NewWorkload("Pkg.Dotnet", aliases: ["dotnet", "dotnet-isolated"]);
        WorkloadResolver resolver = NewResolver(workloads: [dotnet]);

        WorkloadResolution result = await resolver.ResolveAsync(
            new WorkloadResolutionContext(_dir, StackSelector: "dotnet-isolated"),
            CancellationToken.None);

        var resolved = Assert.IsType<WorkloadResolution.Resolved>(result);
        Assert.Same(dotnet, resolved.Workload);
        Assert.Contains("--stack 'dotnet-isolated'", resolved.Message);
    }

    [Fact]
    public async Task Selector_NoMatch_ReturnsNoneWithInstalledList()
    {
        WorkloadInfo dotnet = NewWorkload("Pkg.Dotnet", aliases: ["dotnet"]);
        WorkloadResolver resolver = NewResolver(workloads: [dotnet]);

        WorkloadResolution result = await resolver.ResolveAsync(
            new WorkloadResolutionContext(_dir, StackSelector: "python"),
            CancellationToken.None);

        var none = Assert.IsType<WorkloadResolution.None>(result);
        Assert.Contains("'python'", none.Message);
        Assert.Contains("Pkg.Dotnet", none.Message);
    }

    [Fact]
    public async Task Selector_MultipleMatches_ReturnsNone()
    {
        WorkloadInfo a = NewWorkload("Pkg.A", aliases: ["dotnet"]);
        WorkloadInfo b = NewWorkload("Pkg.B", aliases: ["dotnet"]);
        WorkloadResolver resolver = NewResolver(workloads: [a, b]);

        WorkloadResolution result = await resolver.ResolveAsync(
            new WorkloadResolutionContext(_dir, StackSelector: "dotnet"),
            CancellationToken.None);

        var none = Assert.IsType<WorkloadResolution.None>(result);
        Assert.Contains("Multiple installed workloads claim stack 'dotnet'", none.Message);
        Assert.Contains("Pkg.A", none.Message);
        Assert.Contains("Pkg.B", none.Message);
        Assert.Contains("--stack", none.Message);
    }

    [Fact]
    public async Task Runtime_FromLocalSettings_SingleClaimMatchingRuntime_Resolves()
    {
        WorkloadInfo dotnet = NewWorkload("Pkg.Dotnet");
        WorkloadInfo python = NewWorkload("Pkg.Python");
        IProjectResolver dotnetResolver = NewProjectResolver(
            EvaluationResult.Match("found .csproj", workerRuntime: "dotnet-isolated"));
        IProjectResolver pythonResolver = NewProjectResolver(EvaluationResult.NoMatch());
        _settings.ReadWorkerRuntime(_dir).Returns("dotnet-isolated");

        WorkloadResolver resolver = NewResolver(
            workloads: [dotnet, python],
            resolvers: [(dotnet, dotnetResolver), (python, pythonResolver)]);

        WorkloadResolution result = await resolver.ResolveAsync(
            new WorkloadResolutionContext(_dir, StackSelector: null),
            CancellationToken.None);

        var resolved = Assert.IsType<WorkloadResolution.Resolved>(result);
        Assert.Same(dotnet, resolved.Workload);
        Assert.Contains("FUNCTIONS_WORKER_RUNTIME='dotnet-isolated'", resolved.Message);
    }

    [Fact]
    public async Task Runtime_FromLocalSettings_NoClaimWithMatchingRuntime_ReturnsRuntimeMessage()
    {
        // local.settings.json declares a runtime no resolver backs for this
        // directory (here: resolver matches the dir but reports a different
        // runtime). Surface a runtime-specific message rather than falling
        // through to the generic "no claim" path.
        WorkloadInfo dotnet = NewWorkload("Pkg.Dotnet");
        IProjectResolver resolverStub = NewProjectResolver(
            EvaluationResult.Match("found .csproj", workerRuntime: "dotnet-isolated"));
        _settings.ReadWorkerRuntime(_dir).Returns("custom-runtime");

        WorkloadResolver resolver = NewResolver(
            workloads: [dotnet],
            resolvers: [(dotnet, resolverStub)]);

        WorkloadResolution result = await resolver.ResolveAsync(
            new WorkloadResolutionContext(_dir, StackSelector: null),
            CancellationToken.None);

        var none = Assert.IsType<WorkloadResolution.None>(result);
        Assert.Contains("FUNCTIONS_WORKER_RUNTIME='custom-runtime'", none.Message);
        Assert.Contains("no installed workload claims that runtime", none.Message);
        Assert.Contains("Pkg.Dotnet", none.Message);
    }

    [Fact]
    public async Task Runtime_MultipleClaimsMatchSameRuntime_ReturnsNone()
    {
        WorkloadInfo a = NewWorkload("Pkg.A");
        WorkloadInfo b = NewWorkload("Pkg.B");
        IProjectResolver aResolver = NewProjectResolver(
            EvaluationResult.Match("matched", workerRuntime: "dotnet"));
        IProjectResolver bResolver = NewProjectResolver(
            EvaluationResult.Match("matched", workerRuntime: "dotnet"));
        _settings.ReadWorkerRuntime(_dir).Returns("dotnet");

        WorkloadResolver resolver = NewResolver(
            workloads: [a, b],
            resolvers: [(a, aResolver), (b, bResolver)]);

        WorkloadResolution result = await resolver.ResolveAsync(
            new WorkloadResolutionContext(_dir, StackSelector: null),
            CancellationToken.None);

        var none = Assert.IsType<WorkloadResolution.None>(result);
        Assert.Contains("Multiple installed workloads claim worker runtime 'dotnet'", none.Message);
        Assert.Contains("Pkg.A", none.Message);
        Assert.Contains("Pkg.B", none.Message);
        Assert.Contains("--stack", none.Message);
    }

    [Fact]
    public async Task Resolvers_NoMatch_AreIgnored()
    {
        WorkloadInfo dotnet = NewWorkload("Pkg.Dotnet");
        WorkloadInfo python = NewWorkload("Pkg.Python");
        IProjectResolver dotnetResolver = NewProjectResolver(EvaluationResult.NoMatch("no .csproj"));
        IProjectResolver pythonResolver = NewProjectResolver(EvaluationResult.Match("found requirements.txt"));

        WorkloadResolver resolver = NewResolver(
            workloads: [dotnet, python],
            resolvers: [(dotnet, dotnetResolver), (python, pythonResolver)]);

        WorkloadResolution result = await resolver.ResolveAsync(
            new WorkloadResolutionContext(_dir, StackSelector: null),
            CancellationToken.None);

        var resolved = Assert.IsType<WorkloadResolution.Resolved>(result);
        Assert.Same(python, resolved.Workload);
    }

    [Fact]
    public async Task Resolvers_SingleMatch_Resolves()
    {
        WorkloadInfo dotnet = NewWorkload("Pkg.Dotnet");
        WorkloadInfo python = NewWorkload("Pkg.Python");
        IProjectResolver dotnetResolver = NewProjectResolver(EvaluationResult.Match("found .csproj"));
        IProjectResolver pythonResolver = NewProjectResolver(EvaluationResult.NoMatch());

        WorkloadResolver resolver = NewResolver(
            workloads: [dotnet, python],
            resolvers: [(dotnet, dotnetResolver), (python, pythonResolver)]);

        WorkloadResolution result = await resolver.ResolveAsync(
            new WorkloadResolutionContext(_dir, StackSelector: null),
            CancellationToken.None);

        var resolved = Assert.IsType<WorkloadResolution.Resolved>(result);
        Assert.Same(dotnet, resolved.Workload);
        Assert.Contains("found .csproj", resolved.Message);
    }

    [Fact]
    public async Task Resolvers_MultipleMatches_ReturnsNoneWithReasons()
    {
        WorkloadInfo dotnet = NewWorkload("Pkg.Dotnet");
        WorkloadInfo node = NewWorkload("Pkg.Node");
        IProjectResolver dotnetResolver = NewProjectResolver(EvaluationResult.Match("found .csproj"));
        IProjectResolver nodeResolver = NewProjectResolver(EvaluationResult.Match("found package.json"));

        WorkloadResolver resolver = NewResolver(
            workloads: [dotnet, node],
            resolvers: [(dotnet, dotnetResolver), (node, nodeResolver)]);

        WorkloadResolution result = await resolver.ResolveAsync(
            new WorkloadResolutionContext(_dir, StackSelector: null),
            CancellationToken.None);

        var none = Assert.IsType<WorkloadResolution.None>(result);
        Assert.Contains("Multiple installed workloads claim this directory", none.Message);
        Assert.Contains("Pkg.Dotnet (found .csproj)", none.Message);
        Assert.Contains("Pkg.Node (found package.json)", none.Message);
        Assert.Contains("--stack", none.Message);
    }

    [Fact]
    public async Task NoRuntimeNoSelector_SingleResolverMatches_Resolves()
    {
        // Flex Consumption customers don't have FUNCTIONS_WORKER_RUNTIME in
        // local.settings.json (it's not in their Azure config either, by
        // design). The resolver must still pick a workload from project shape
        // alone via IProjectResolver's evaluation. This test pins that contract: no --stack,
        // no runtime, single matching resolver resolves cleanly.
        WorkloadInfo python = NewWorkload("Pkg.Python");
        IProjectResolver pythonResolver = NewProjectResolver(
            EvaluationResult.Match("found requirements.txt"));
        _settings.ReadWorkerRuntime(_dir).Returns((string?)null);

        WorkloadResolver resolver = NewResolver(
            workloads: [python],
            resolvers: [(python, pythonResolver)]);

        WorkloadResolution result = await resolver.ResolveAsync(
            new WorkloadResolutionContext(_dir, StackSelector: null),
            CancellationToken.None);

        var resolved = Assert.IsType<WorkloadResolution.Resolved>(result);
        Assert.Same(python, resolved.Workload);
        Assert.Contains("found requirements.txt", resolved.Message);
    }

    [Fact]
    public async Task Resolvers_ZeroCandidates_ReturnsNone()
    {
        WorkloadResolver resolver = NewResolver(workloads: [], resolvers: []);

        WorkloadResolution result = await resolver.ResolveAsync(
            new WorkloadResolutionContext(_dir, StackSelector: null),
            CancellationToken.None);

        Assert.IsType<WorkloadResolution.None>(result);
    }

    [Fact]
    public async Task NoHostJson_ReturnsNoneWithoutInvokingResolvers()
    {
        // CLI-wide invariant: a directory without host.json is not a
        // Functions project. The host gates this so per-stack resolvers
        // don't have to repeat the check.
        var emptyDir = new DirectoryInfo(Path.Combine(Path.GetTempPath(), "func-resolver-no-host-" + Guid.NewGuid().ToString("N")));
        emptyDir.Create();
        try
        {
            WorkloadInfo dotnet = NewWorkload("Pkg.Dotnet");
            IProjectResolver resolverStub = NewProjectResolver(EvaluationResult.Match("would have matched"));

            WorkloadResolver resolver = NewResolver(
                workloads: [dotnet],
                resolvers: [(dotnet, resolverStub)]);

            WorkloadResolution result = await resolver.ResolveAsync(
                new WorkloadResolutionContext(emptyDir, StackSelector: null),
                CancellationToken.None);

            var none = Assert.IsType<WorkloadResolution.None>(result);
            Assert.Contains("does not look like an Azure Functions project", none.Message);
            Assert.Contains("host.json", none.Message);
            Assert.Contains(emptyDir.FullName, none.Message);
            Assert.Contains("func init", none.Message);
            await resolverStub.DidNotReceive().EvaluateAsync(Arg.Any<DirectoryInfo>(), Arg.Any<CancellationToken>());
        }
        finally
        {
            try { emptyDir.Delete(recursive: true); } catch { }
        }
    }

    [Fact]
    public async Task SkipDirectoryDetection_NoSelector_ReturnsNoneWithoutInvokingResolvers()
    {
        WorkloadInfo dotnet = NewWorkload("Pkg.Dotnet");
        IProjectResolver resolverStub = NewProjectResolver(EvaluationResult.Match("would have matched"));

        WorkloadResolver resolver = NewResolver(
            workloads: [dotnet],
            resolvers: [(dotnet, resolverStub)]);

        WorkloadResolution result = await resolver.ResolveAsync(
            new WorkloadResolutionContext(_dir, StackSelector: null, SkipDirectoryDetection: true),
            CancellationToken.None);

        var none = Assert.IsType<WorkloadResolution.None>(result);
        Assert.Contains("No --stack flag", none.Message);
        Assert.Contains("Pkg.Dotnet", none.Message);
        await resolverStub.DidNotReceive().EvaluateAsync(Arg.Any<DirectoryInfo>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SkipDirectoryDetection_WithSelector_StillResolvesBySelector()
    {
        WorkloadInfo dotnet = NewWorkload("Pkg.Dotnet", aliases: ["dotnet"]);
        WorkloadResolver resolver = NewResolver(workloads: [dotnet]);

        WorkloadResolution result = await resolver.ResolveAsync(
            new WorkloadResolutionContext(_dir, StackSelector: "dotnet", SkipDirectoryDetection: true),
            CancellationToken.None);

        var resolved = Assert.IsType<WorkloadResolution.Resolved>(result);
        Assert.Same(dotnet, resolved.Workload);
    }

    private WorkloadResolver NewResolver(
        IReadOnlyList<WorkloadInfo>? workloads = null,
        IReadOnlyList<(WorkloadInfo Workload, IProjectResolver Resolver)>? resolvers = null)
        => new(
            new StubWorkloadProvider(workloads ?? []),
            (resolvers ?? []).Select(d => new WorkloadProjectResolverContribution(d.Workload, d.Resolver)).ToList(),
            _settings);

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
    }
}
