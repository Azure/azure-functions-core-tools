// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Workloads;
using Azure.Functions.Cli.Workloads.Resolution;
using NSubstitute;
using Xunit;

namespace Azure.Functions.Cli.Tests.Workloads.Resolution;

public sealed class WorkloadResolverTests
{
    private readonly DirectoryInfo _dir = new(Path.Combine(Path.GetTempPath(), "resolver-tests"));
    private readonly ILocalSettingsReader _settings = Substitute.For<ILocalSettingsReader>();

    public WorkloadResolverTests()
    {
        _settings.ReadWorkerRuntime(Arg.Any<DirectoryInfo>()).Returns((string?)null);
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
        IProjectDetector dotnetDetector = NewDetector(
            DetectionResult.Yes("found .csproj", workerRuntime: "dotnet-isolated"));
        IProjectDetector pythonDetector = NewDetector(DetectionResult.No());
        _settings.ReadWorkerRuntime(_dir).Returns("dotnet-isolated");

        WorkloadResolver resolver = NewResolver(
            workloads: [dotnet, python],
            detectors: [(dotnet, dotnetDetector), (python, pythonDetector)]);

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
        // local.settings.json declares a runtime no detector backs for this
        // directory (here: detector claims the dir but reports a different
        // runtime). Surface a runtime-specific message rather than falling
        // through to the generic "no claim" path.
        WorkloadInfo dotnet = NewWorkload("Pkg.Dotnet");
        IProjectDetector detector = NewDetector(
            DetectionResult.Yes("found .csproj", workerRuntime: "dotnet-isolated"));
        _settings.ReadWorkerRuntime(_dir).Returns("custom-runtime");

        WorkloadResolver resolver = NewResolver(
            workloads: [dotnet],
            detectors: [(dotnet, detector)]);

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
        IProjectDetector aDetector = NewDetector(
            DetectionResult.Yes("matched", workerRuntime: "dotnet"));
        IProjectDetector bDetector = NewDetector(
            DetectionResult.Yes("matched", workerRuntime: "dotnet"));
        _settings.ReadWorkerRuntime(_dir).Returns("dotnet");

        WorkloadResolver resolver = NewResolver(
            workloads: [a, b],
            detectors: [(a, aDetector), (b, bDetector)]);

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
    public async Task Detectors_NotClaimed_AreIgnored()
    {
        WorkloadInfo dotnet = NewWorkload("Pkg.Dotnet");
        WorkloadInfo python = NewWorkload("Pkg.Python");
        IProjectDetector dotnetDetector = NewDetector(DetectionResult.No("no .csproj"));
        IProjectDetector pythonDetector = NewDetector(DetectionResult.Yes("found requirements.txt"));

        WorkloadResolver resolver = NewResolver(
            workloads: [dotnet, python],
            detectors: [(dotnet, dotnetDetector), (python, pythonDetector)]);

        WorkloadResolution result = await resolver.ResolveAsync(
            new WorkloadResolutionContext(_dir, StackSelector: null),
            CancellationToken.None);

        var resolved = Assert.IsType<WorkloadResolution.Resolved>(result);
        Assert.Same(python, resolved.Workload);
    }

    [Fact]
    public async Task Detectors_SingleClaim_Resolves()
    {
        WorkloadInfo dotnet = NewWorkload("Pkg.Dotnet");
        WorkloadInfo python = NewWorkload("Pkg.Python");
        IProjectDetector dotnetDetector = NewDetector(DetectionResult.Yes("found .csproj"));
        IProjectDetector pythonDetector = NewDetector(DetectionResult.No());

        WorkloadResolver resolver = NewResolver(
            workloads: [dotnet, python],
            detectors: [(dotnet, dotnetDetector), (python, pythonDetector)]);

        WorkloadResolution result = await resolver.ResolveAsync(
            new WorkloadResolutionContext(_dir, StackSelector: null),
            CancellationToken.None);

        var resolved = Assert.IsType<WorkloadResolution.Resolved>(result);
        Assert.Same(dotnet, resolved.Workload);
        Assert.Contains("found .csproj", resolved.Message);
    }

    [Fact]
    public async Task Detectors_MultipleClaim_ReturnsNoneWithReasons()
    {
        WorkloadInfo dotnet = NewWorkload("Pkg.Dotnet");
        WorkloadInfo node = NewWorkload("Pkg.Node");
        IProjectDetector dotnetDetector = NewDetector(DetectionResult.Yes("found .csproj"));
        IProjectDetector nodeDetector = NewDetector(DetectionResult.Yes("found package.json"));

        WorkloadResolver resolver = NewResolver(
            workloads: [dotnet, node],
            detectors: [(dotnet, dotnetDetector), (node, nodeDetector)]);

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
    public async Task NoRuntimeNoSelector_SingleDetectorClaims_Resolves()
    {
        // Flex Consumption customers don't have FUNCTIONS_WORKER_RUNTIME in
        // local.settings.json (it's not in their Azure config either, by
        // design). The resolver must still pick a workload from project shape
        // alone via IProjectDetector. This test pins that contract: no --stack,
        // no runtime, single claiming detector resolves cleanly.
        WorkloadInfo python = NewWorkload("Pkg.Python");
        IProjectDetector pythonDetector = NewDetector(
            DetectionResult.Yes("found requirements.txt"));
        _settings.ReadWorkerRuntime(_dir).Returns((string?)null);

        WorkloadResolver resolver = NewResolver(
            workloads: [python],
            detectors: [(python, pythonDetector)]);

        WorkloadResolution result = await resolver.ResolveAsync(
            new WorkloadResolutionContext(_dir, StackSelector: null),
            CancellationToken.None);

        var resolved = Assert.IsType<WorkloadResolution.Resolved>(result);
        Assert.Same(python, resolved.Workload);
        Assert.Contains("found requirements.txt", resolved.Message);
    }

    [Fact]
    public async Task Detectors_ZeroCandidates_ReturnsNone()
    {
        WorkloadResolver resolver = NewResolver(workloads: [], detectors: []);

        WorkloadResolution result = await resolver.ResolveAsync(
            new WorkloadResolutionContext(_dir, StackSelector: null),
            CancellationToken.None);

        Assert.IsType<WorkloadResolution.None>(result);
    }

    [Fact]
    public async Task SkipDirectoryDetection_NoSelector_ReturnsNoneWithoutInvokingDetectors()
    {
        WorkloadInfo dotnet = NewWorkload("Pkg.Dotnet");
        IProjectDetector detector = NewDetector(DetectionResult.Yes("would have matched"));

        WorkloadResolver resolver = NewResolver(
            workloads: [dotnet],
            detectors: [(dotnet, detector)]);

        WorkloadResolution result = await resolver.ResolveAsync(
            new WorkloadResolutionContext(_dir, StackSelector: null, SkipDirectoryDetection: true),
            CancellationToken.None);

        var none = Assert.IsType<WorkloadResolution.None>(result);
        Assert.Contains("No --stack flag", none.Message);
        Assert.Contains("Pkg.Dotnet", none.Message);
        await detector.DidNotReceive().DetectAsync(Arg.Any<DirectoryInfo>(), Arg.Any<CancellationToken>());
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
        IReadOnlyList<(WorkloadInfo Workload, IProjectDetector Detector)>? detectors = null)
        => new(
            new StubWorkloadProvider(workloads ?? []),
            (detectors ?? []).Select(d => new WorkloadDetectorContribution(d.Workload, d.Detector)).ToList(),
            _settings);

    private static WorkloadInfo NewWorkload(string packageId, IReadOnlyList<string>? aliases = null)
        => TestWorkloads.CreateInfo(packageId) with { Aliases = aliases ?? [] };

    private static IProjectDetector NewDetector(DetectionResult detectResult)
    {
        IProjectDetector detector = Substitute.For<IProjectDetector>();
        detector.DetectAsync(Arg.Any<DirectoryInfo>(), Arg.Any<CancellationToken>())
            .Returns(detectResult);
        return detector;
    }

    private sealed class StubWorkloadProvider(IReadOnlyList<WorkloadInfo> workloads) : IWorkloadProvider
    {
        public IReadOnlyList<WorkloadInfo> GetWorkloads() => workloads;
    }
}
