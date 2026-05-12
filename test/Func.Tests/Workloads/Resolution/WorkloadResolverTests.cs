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

        var resolved = Assert.IsType<WorkloadResolution.Resolved>(result);
        Assert.Same(dotnet, resolved.Workload);
        Assert.Contains("FUNCTIONS_WORKER_RUNTIME='dotnet-isolated'", resolved.Message);

        // Detectors must not have been invoked: runtime path short-circuited.
        await dotnetDetector.DidNotReceive().DetectAsync(Arg.Any<DirectoryInfo>(), Arg.Any<CancellationToken>());
        await pythonDetector.DidNotReceive().DetectAsync(Arg.Any<DirectoryInfo>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Runtime_FromLocalSettings_NoDetectorClaimsIt_ReturnsNoneWithRuntimeMessage()
    {
        // An explicit FUNCTIONS_WORKER_RUNTIME with no claiming workload is a
        // user-declared mismatch; surface it directly rather than falling
        // through to detectors and producing a generic ambiguity error.
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

        var none = Assert.IsType<WorkloadResolution.None>(result);
        Assert.Contains("FUNCTIONS_WORKER_RUNTIME='custom-runtime'", none.Message);
        Assert.Contains("no installed workload claims that runtime", none.Message);
        Assert.Contains("Pkg.Dotnet", none.Message);
        await detector.DidNotReceive().DetectAsync(Arg.Any<DirectoryInfo>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Runtime_MultipleDetectorsClaimSameRuntime_ReturnsNone()
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

        var none = Assert.IsType<WorkloadResolution.None>(result);
        Assert.Contains("Multiple installed workloads claim worker runtime 'dotnet'", none.Message);
        Assert.Contains("Pkg.A", none.Message);
        Assert.Contains("Pkg.B", none.Message);
        Assert.Contains("--stack", none.Message);
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

        Assert.IsType<WorkloadResolution.None>(result);
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

        var resolved = Assert.IsType<WorkloadResolution.Resolved>(result);
        Assert.Same(dotnet, resolved.Workload);
        await detector.Received(1).DetectAsync(_dir, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Detectors_SingleClaim_Resolves()
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

        var resolved = Assert.IsType<WorkloadResolution.Resolved>(result);
        Assert.Same(dotnet, resolved.Workload);
        Assert.Contains("found .csproj", resolved.Message);
    }

    [Fact]
    public async Task Detectors_MultipleClaim_ReturnsNoneWithReasons()
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

        var none = Assert.IsType<WorkloadResolution.None>(result);
        Assert.Contains("Multiple installed workloads claim this directory", none.Message);
        Assert.Contains("Pkg.Dotnet (found .csproj)", none.Message);
        Assert.Contains("Pkg.Node (found package.json)", none.Message);
        Assert.Contains("--stack", none.Message);
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
    public async Task SkipDirectoryDetection_NoSelectorNoRuntime_ReturnsNoneWithoutInvokingDetectors()
    {
        WorkloadInfo dotnet = NewWorkload("Pkg.Dotnet");
        IProjectDetector detector = NewDetector(detectResult: DetectionResult.Yes("would have matched"));

        WorkloadResolver resolver = NewResolver(
            workloads: [dotnet],
            detectors: [(dotnet, detector)]);

        WorkloadResolution result = await resolver.ResolveAsync(
            new WorkloadResolutionContext(_dir, StackSelector: null, SkipDirectoryDetection: true),
            CancellationToken.None);

        var none = Assert.IsType<WorkloadResolution.None>(result);
        Assert.Contains("No --stack flag", none.Message);
        Assert.Contains("FUNCTIONS_WORKER_RUNTIME", none.Message);
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
