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
    private readonly IDirectoryMarkerMatcher _matcher = Substitute.For<IDirectoryMarkerMatcher>();

    public WorkloadResolverTests()
    {
        // Default behaviour: no local settings, every marker list matches.
        _settings.ReadWorkerRuntime(Arg.Any<DirectoryInfo>()).Returns((string?)null);
        _matcher.AnyMatch(Arg.Any<DirectoryInfo>(), Arg.Any<IReadOnlyList<string>>()).Returns(true);
    }

    [Fact]
    public async Task Selector_SingleAliasMatch_Resolves()
    {
        WorkloadInfo dotnet = NewWorkload("Pkg.Dotnet", aliases: ["dotnet", "dotnet-isolated"]);
        WorkloadResolver resolver = NewResolver(workloads: [dotnet]);

        WorkloadResolution result = await resolver.ResolveAsync(
            new WorkloadResolutionContext(_dir, StackSelector: "dotnet-isolated"),
            CancellationToken.None);

        Assert.Equal(WorkloadResolutionStatus.Resolved, result.Status);
        Assert.Same(dotnet, result.Resolved);
        Assert.Contains("--stack 'dotnet-isolated'", result.Message);
    }

    [Fact]
    public async Task Selector_NoMatch_ReturnsNoneWithInstalledList()
    {
        WorkloadInfo dotnet = NewWorkload("Pkg.Dotnet", aliases: ["dotnet"]);
        WorkloadResolver resolver = NewResolver(workloads: [dotnet]);

        WorkloadResolution result = await resolver.ResolveAsync(
            new WorkloadResolutionContext(_dir, StackSelector: "python"),
            CancellationToken.None);

        Assert.Equal(WorkloadResolutionStatus.None, result.Status);
        Assert.Null(result.Resolved);
        Assert.Contains("'python'", result.Message);
        Assert.Contains("Pkg.Dotnet", result.Message);
    }

    [Fact]
    public async Task Selector_MultipleMatches_ReturnsAmbiguous()
    {
        WorkloadInfo a = NewWorkload("Pkg.A", aliases: ["dotnet"]);
        WorkloadInfo b = NewWorkload("Pkg.B", aliases: ["dotnet"]);
        WorkloadResolver resolver = NewResolver(workloads: [a, b]);

        WorkloadResolution result = await resolver.ResolveAsync(
            new WorkloadResolutionContext(_dir, StackSelector: "dotnet"),
            CancellationToken.None);

        Assert.Equal(WorkloadResolutionStatus.Ambiguous, result.Status);
        Assert.Equal(2, result.Candidates.Count);
        Assert.Contains(a, result.Candidates);
        Assert.Contains(b, result.Candidates);
    }

    [Fact]
    public async Task Runtime_FromLocalSettings_MatchesSingleDetector_Resolves()
    {
        WorkloadInfo dotnet = NewWorkload("Pkg.Dotnet");
        WorkloadInfo python = NewWorkload("Pkg.Python");
        IProjectDetector dotnetDetector = NewDetector(workerRuntimes: ["dotnet-isolated"]);
        IProjectDetector pythonDetector = NewDetector(workerRuntimes: ["python"]);
        _settings.ReadWorkerRuntime(_dir).Returns("dotnet-isolated");

        WorkloadResolver resolver = NewResolver(
            workloads: [dotnet, python],
            detectors: [(dotnet, dotnetDetector), (python, pythonDetector)]);

        WorkloadResolution result = await resolver.ResolveAsync(
            new WorkloadResolutionContext(_dir, StackSelector: null),
            CancellationToken.None);

        Assert.Equal(WorkloadResolutionStatus.Resolved, result.Status);
        Assert.Same(dotnet, result.Resolved);
        Assert.Contains("FUNCTIONS_WORKER_RUNTIME='dotnet-isolated'", result.Message);

        // Detectors must not have been invoked: runtime path short-circuited.
        await dotnetDetector.DidNotReceive().DetectAsync(Arg.Any<DirectoryInfo>(), Arg.Any<CancellationToken>());
        await pythonDetector.DidNotReceive().DetectAsync(Arg.Any<DirectoryInfo>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Runtime_FromLocalSettings_NoDetectorClaimsIt_FallsThroughToDetectors()
    {
        WorkloadInfo dotnet = NewWorkload("Pkg.Dotnet");
        IProjectDetector detector = NewDetector(
            workerRuntimes: [],
            detectResult: DetectionResult.Yes("found .csproj"));
        _settings.ReadWorkerRuntime(_dir).Returns("custom-runtime");

        WorkloadResolver resolver = NewResolver(
            workloads: [dotnet],
            detectors: [(dotnet, detector)]);

        WorkloadResolution result = await resolver.ResolveAsync(
            new WorkloadResolutionContext(_dir, StackSelector: null),
            CancellationToken.None);

        Assert.Equal(WorkloadResolutionStatus.Resolved, result.Status);
        Assert.Same(dotnet, result.Resolved);
        await detector.Received(1).DetectAsync(_dir, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Detectors_PreFilterExcludesNonMatching_DoesNotInvokeDetect()
    {
        WorkloadInfo dotnet = NewWorkload("Pkg.Dotnet");
        IProjectDetector detector = NewDetector(
            projectMarkers: ["*.csproj"],
            detectResult: DetectionResult.Yes());
        _matcher.AnyMatch(_dir, Arg.Is<IReadOnlyList<string>>(m => m.SequenceEqual(new[] { "*.csproj" })))
            .Returns(false);

        WorkloadResolver resolver = NewResolver(
            workloads: [dotnet],
            detectors: [(dotnet, detector)]);

        WorkloadResolution result = await resolver.ResolveAsync(
            new WorkloadResolutionContext(_dir, StackSelector: null),
            CancellationToken.None);

        Assert.Equal(WorkloadResolutionStatus.None, result.Status);
        await detector.DidNotReceive().DetectAsync(Arg.Any<DirectoryInfo>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Detectors_EmptyMarkers_AlwaysCandidate_InvokesDetect()
    {
        // Real matcher: empty markers → true; this asserts the behaviour
        // contract regardless of how the substitute is configured.
        var realMatcher = new DirectoryMarkerMatcher();
        WorkloadInfo dotnet = NewWorkload("Pkg.Dotnet");
        IProjectDetector detector = NewDetector(
            projectMarkers: [],
            detectResult: DetectionResult.Yes("runtime check"));

        WorkloadResolver resolver = new(
            new StubWorkloadProvider([dotnet]),
            [new WorkloadDetectorContribution(dotnet, detector)],
            _settings,
            realMatcher);

        WorkloadResolution result = await resolver.ResolveAsync(
            new WorkloadResolutionContext(_dir, StackSelector: null),
            CancellationToken.None);

        Assert.Equal(WorkloadResolutionStatus.Resolved, result.Status);
        Assert.Same(dotnet, result.Resolved);
        await detector.Received(1).DetectAsync(_dir, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Detectors_SingleYes_Resolves()
    {
        WorkloadInfo dotnet = NewWorkload("Pkg.Dotnet");
        WorkloadInfo python = NewWorkload("Pkg.Python");
        IProjectDetector dotnetDetector = NewDetector(detectResult: DetectionResult.Yes("found .csproj"));
        IProjectDetector pythonDetector = NewDetector(detectResult: DetectionResult.No());

        WorkloadResolver resolver = NewResolver(
            workloads: [dotnet, python],
            detectors: [(dotnet, dotnetDetector), (python, pythonDetector)]);

        WorkloadResolution result = await resolver.ResolveAsync(
            new WorkloadResolutionContext(_dir, StackSelector: null),
            CancellationToken.None);

        Assert.Equal(WorkloadResolutionStatus.Resolved, result.Status);
        Assert.Same(dotnet, result.Resolved);
        Assert.Contains("found .csproj", result.Message);
    }

    [Fact]
    public async Task Detectors_MultipleYes_ReturnsAmbiguousWithReasons()
    {
        WorkloadInfo dotnet = NewWorkload("Pkg.Dotnet");
        WorkloadInfo node = NewWorkload("Pkg.Node");
        IProjectDetector dotnetDetector = NewDetector(detectResult: DetectionResult.Yes("found .csproj"));
        IProjectDetector nodeDetector = NewDetector(detectResult: DetectionResult.Yes("found package.json"));

        WorkloadResolver resolver = NewResolver(
            workloads: [dotnet, node],
            detectors: [(dotnet, dotnetDetector), (node, nodeDetector)]);

        WorkloadResolution result = await resolver.ResolveAsync(
            new WorkloadResolutionContext(_dir, StackSelector: null),
            CancellationToken.None);

        Assert.Equal(WorkloadResolutionStatus.Ambiguous, result.Status);
        Assert.Equal(2, result.Candidates.Count);
        Assert.Contains("Pkg.Dotnet (found .csproj)", result.Message);
        Assert.Contains("Pkg.Node (found package.json)", result.Message);
        Assert.Contains("--stack", result.Message);
    }

    [Fact]
    public async Task Detectors_MaybeOnly_DoesNotResolve_ReturnsNone()
    {
        WorkloadInfo dotnet = NewWorkload("Pkg.Dotnet");
        IProjectDetector detector = NewDetector(detectResult: DetectionResult.Maybe("might be it"));

        WorkloadResolver resolver = NewResolver(
            workloads: [dotnet],
            detectors: [(dotnet, detector)]);

        WorkloadResolution result = await resolver.ResolveAsync(
            new WorkloadResolutionContext(_dir, StackSelector: null),
            CancellationToken.None);

        Assert.Equal(WorkloadResolutionStatus.None, result.Status);
        Assert.Contains("--stack", result.Message);
    }

    [Fact]
    public async Task Detectors_ZeroCandidates_ReturnsNone()
    {
        WorkloadResolver resolver = NewResolver(workloads: [], detectors: []);

        WorkloadResolution result = await resolver.ResolveAsync(
            new WorkloadResolutionContext(_dir, StackSelector: null),
            CancellationToken.None);

        Assert.Equal(WorkloadResolutionStatus.None, result.Status);
    }

    [Fact]
    public async Task Runtime_MultipleDetectorsClaimSameRuntime_ReturnsAmbiguous()
    {
        WorkloadInfo a = NewWorkload("Pkg.A");
        WorkloadInfo b = NewWorkload("Pkg.B");
        IProjectDetector aDetector = NewDetector(workerRuntimes: ["dotnet"]);
        IProjectDetector bDetector = NewDetector(workerRuntimes: ["dotnet"]);
        _settings.ReadWorkerRuntime(_dir).Returns("dotnet");

        WorkloadResolver resolver = NewResolver(
            workloads: [a, b],
            detectors: [(a, aDetector), (b, bDetector)]);

        WorkloadResolution result = await resolver.ResolveAsync(
            new WorkloadResolutionContext(_dir, StackSelector: null),
            CancellationToken.None);

        Assert.Equal(WorkloadResolutionStatus.Ambiguous, result.Status);
        Assert.Equal(2, result.Candidates.Count);
    }

    private WorkloadResolver NewResolver(
        IReadOnlyList<WorkloadInfo>? workloads = null,
        IReadOnlyList<(WorkloadInfo Workload, IProjectDetector Detector)>? detectors = null)
        => new(
            new StubWorkloadProvider(workloads ?? []),
            (detectors ?? []).Select(d => new WorkloadDetectorContribution(d.Workload, d.Detector)).ToList(),
            _settings,
            _matcher);

    private static WorkloadInfo NewWorkload(string packageId, IReadOnlyList<string>? aliases = null)
        => TestWorkloads.CreateInfo(packageId) with { Aliases = aliases ?? [] };

    private static IProjectDetector NewDetector(
        IReadOnlyList<string>? projectMarkers = null,
        IReadOnlyList<string>? workerRuntimes = null,
        DetectionResult? detectResult = null)
    {
        IProjectDetector detector = Substitute.For<IProjectDetector>();
        detector.ProjectMarkers.Returns(projectMarkers ?? []);
        detector.WorkerRuntimes.Returns(workerRuntimes ?? []);
        detector.DetectAsync(Arg.Any<DirectoryInfo>(), Arg.Any<CancellationToken>())
            .Returns(detectResult ?? DetectionResult.No());
        return detector;
    }

    private sealed class StubWorkloadProvider(IReadOnlyList<WorkloadInfo> workloads) : IWorkloadProvider
    {
        public IReadOnlyList<WorkloadInfo> GetWorkloads() => workloads;
    }
}
