// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Workers;
using Azure.Functions.Cli.Workloads;
using Azure.Functions.Cli.Workloads.Catalog;
using Microsoft.Extensions.Options;
using NSubstitute;
using NuGet.Versioning;

namespace Azure.Functions.Cli.Tests.Workers;

public class DefaultFunctionsWorkerResolverTests
{
    private const string NodeWorkerPackageId = "Azure.Functions.Cli.Workloads.Workers.Node";
    private const string PythonWorkerPackageId = "Azure.Functions.Cli.Workloads.Workers.Python";
    private const string GoWorkerPackageId = "Azure.Functions.Cli.Workloads.Workers.Go";

    private readonly IWorkloadProvider _workloads = Substitute.For<IWorkloadProvider>();
    private readonly IWorkerConfigFileSystem _fileSystem = Substitute.For<IWorkerConfigFileSystem>();

    public DefaultFunctionsWorkerResolverTests()
    {
        _workloads.GetContentWorkloads().Returns([]);
        _workloads.GetContentWorkloadsByPackageId(Arg.Any<string>()).Returns([]);
        _fileSystem.FileExists(Arg.Any<string>()).Returns(true);
    }

    [Fact]
    public async Task ResolveWorkerAsync_InstalledNodeWorker_ReturnsResolvedWorker()
    {
        ContentWorkloadInfo workload = CreateContentWorkload(NodeWorkerPackageId, "3.13.0");
        UseContentWorkloads(workload);
        DefaultFunctionsWorkerResolver resolver = CreateResolver();

        FunctionsWorkerResolutionResult result = await resolver.ResolveWorkerAsync(
            new FunctionsWorkerId("node"),
            CancellationToken.None);

        FunctionsWorkerResolutionResult.Resolved resolved = result.Should().BeOfType<FunctionsWorkerResolutionResult.Resolved>().Subject;
        resolved.Worker.Id.Value.Should().Be("node");
        resolved.Worker.WorkerRuntime.Should().Be("node");
        resolved.Worker.WorkerConfigPath.Should().Be(Path.Combine(workload.ContentRoot, "worker.config.json"));
        resolved.Worker.Version.Should().Be("3.13.0");
    }

    [Fact]
    public async Task ResolveWorkerAsync_MultipleInstalledVersions_ReturnsHighestVersion()
    {
        ContentWorkloadInfo older = CreateContentWorkload(NodeWorkerPackageId, "3.12.0");
        ContentWorkloadInfo newer = CreateContentWorkload(NodeWorkerPackageId, "3.13.0");
        UseContentWorkloads(older, newer);
        DefaultFunctionsWorkerResolver resolver = CreateResolver();

        FunctionsWorkerResolutionResult result = await resolver.ResolveWorkerAsync(
            new FunctionsWorkerId("node"),
            CancellationToken.None);

        FunctionsWorkerResolutionResult.Resolved resolved = result.Should().BeOfType<FunctionsWorkerResolutionResult.Resolved>().Subject;
        resolved.Worker.Version.Should().Be("3.13.0");
        resolved.Worker.WorkerConfigPath.Should().Be(Path.Combine(newer.ContentRoot, "worker.config.json"));
    }

    [Fact]
    public async Task ResolveWorkerAsync_WorkloadAlias_ReturnsResolvedWorker()
    {
        ContentWorkloadInfo workload = CreateContentWorkload("custom.node.worker", "3.13.0", ["node-worker"]);
        UseContentWorkloads(workload);
        DefaultFunctionsWorkerResolver resolver = CreateResolver();

        FunctionsWorkerResolutionResult result = await resolver.ResolveWorkerAsync(
            new FunctionsWorkerId("node"),
            CancellationToken.None);

        FunctionsWorkerResolutionResult.Resolved resolved = result.Should().BeOfType<FunctionsWorkerResolutionResult.Resolved>().Subject;
        resolved.Worker.Id.Value.Should().Be("node");
        resolved.Worker.Version.Should().Be("3.13.0");
        resolved.Worker.WorkerConfigPath.Should().Be(Path.Combine(workload.ContentRoot, "worker.config.json"));
    }

    [Fact]
    public async Task ResolveWorkerAsync_ConventionPackageWorker_ReturnsResolvedWorker()
    {
        ContentWorkloadInfo workload = CreateContentWorkload("Azure.Functions.Cli.Workloads.Workers.Ruby", "1.2.3");
        UseContentWorkloads(workload);
        DefaultFunctionsWorkerResolver resolver = CreateResolver();

        FunctionsWorkerResolutionResult result = await resolver.ResolveWorkerAsync(
            new FunctionsWorkerId("ruby"),
            CancellationToken.None);

        FunctionsWorkerResolutionResult.Resolved resolved = result.Should().BeOfType<FunctionsWorkerResolutionResult.Resolved>().Subject;
        resolved.Worker.Id.Value.Should().Be("ruby");
        resolved.Worker.WorkerRuntime.Should().Be("ruby");
        resolved.Worker.WorkerConfigPath.Should().Be(Path.Combine(workload.ContentRoot, "worker.config.json"));
        _workloads.Received().GetContentWorkloadsByPackageId("Azure.Functions.Cli.Workloads.Workers.ruby");
    }

    [Fact]
    public async Task ResolveWorkerAsync_NoMatchingConventionPackage_ReturnsNotInstalled()
    {
        UseContentWorkloads(CreateContentWorkload("Azure.Functions.Cli.Workloads.Host", "4.0.0"));
        DefaultFunctionsWorkerResolver resolver = CreateResolver();

        FunctionsWorkerResolutionResult result = await resolver.ResolveWorkerAsync(
            new FunctionsWorkerId("python"),
            CancellationToken.None);

        FunctionsWorkerResolutionResult.NotResolved notResolved = result.Should().BeOfType<FunctionsWorkerResolutionResult.NotResolved>().Subject;
        FunctionsWorkerResolutionFailure.NotInstalled failure =
            notResolved.Failure.Should().BeOfType<FunctionsWorkerResolutionFailure.NotInstalled>().Subject;
        failure.WorkerId.Value.Should().Be("python");
    }

    [Fact]
    public async Task ResolveWorkerAsync_NoInstalledConventionPackage_ReturnsNotInstalled()
    {
        UseContentWorkloads(CreateContentWorkload(NodeWorkerPackageId, "3.13.0"));
        DefaultFunctionsWorkerResolver resolver = CreateResolver();

        FunctionsWorkerResolutionResult result = await resolver.ResolveWorkerAsync(
            new FunctionsWorkerId("ruby"),
            CancellationToken.None);

        FunctionsWorkerResolutionResult.NotResolved notResolved = result.Should().BeOfType<FunctionsWorkerResolutionResult.NotResolved>().Subject;
        FunctionsWorkerResolutionFailure.NotInstalled failure =
            notResolved.Failure.Should().BeOfType<FunctionsWorkerResolutionFailure.NotInstalled>().Subject;
        failure.WorkerId.Value.Should().Be("ruby");
        failure.Message.Should().Contain("func workload install Azure.Functions.Cli.Workloads.Workers.ruby --exact");
    }

    [Fact]
    public async Task ResolveWorkerAsync_InvalidInstalledVersion_ReturnsMissingCompatibleVersion()
    {
        UseContentWorkloads(CreateContentWorkload(NodeWorkerPackageId, "not-a-version"));
        DefaultFunctionsWorkerResolver resolver = CreateResolver();

        FunctionsWorkerResolutionResult result = await resolver.ResolveWorkerAsync(
            new FunctionsWorkerId("node"),
            CancellationToken.None);

        FunctionsWorkerResolutionResult.NotResolved notResolved = result.Should().BeOfType<FunctionsWorkerResolutionResult.NotResolved>().Subject;
        FunctionsWorkerResolutionFailure.MissingCompatibleVersion failure =
            notResolved.Failure.Should().BeOfType<FunctionsWorkerResolutionFailure.MissingCompatibleVersion>().Subject;
        failure.WorkerId.Value.Should().Be("node");
        failure.VersionConstraint.Should().BeNull();
        failure.Message.Should().Contain("func workload install Azure.Functions.Cli.Workloads.Workers.node --exact --force");
        _fileSystem.DidNotReceive().FileExists(Arg.Any<string>());
    }

    [Fact]
    public async Task ResolveWorkerAsync_NoWorkerConstraint_AllowsInstalledVersionBelowFormerTemporaryRange()
    {
        UseContentWorkloads(CreateContentWorkload(GoWorkerPackageId, "0.1.0"));
        DefaultFunctionsWorkerResolver resolver = CreateResolver();

        FunctionsWorkerResolutionResult result = await resolver.ResolveWorkerAsync(
            new FunctionsWorkerId("go"),
            CancellationToken.None);

        FunctionsWorkerResolutionResult.Resolved resolved = result.Should().BeOfType<FunctionsWorkerResolutionResult.Resolved>().Subject;
        resolved.Worker.Id.Value.Should().Be("go");
        resolved.Worker.WorkerRuntime.Should().Be("go");
        resolved.Worker.Version.Should().Be("0.1.0");
    }

    [Fact]
    public async Task ResolveWorkerAsync_ProfileConstraint_SelectsHighestCompatibleVersion()
    {
        ContentWorkloadInfo older = CreateContentWorkload(NodeWorkerPackageId, "3.12.0");
        ContentWorkloadInfo compatible = CreateContentWorkload(NodeWorkerPackageId, "3.13.0");
        ContentWorkloadInfo tooNew = CreateContentWorkload(NodeWorkerPackageId, "3.14.0");
        UseContentWorkloads(older, compatible, tooNew);
        Dictionary<string, VersionRange> workerVersionRanges = new(StringComparer.OrdinalIgnoreCase)
        {
            ["node"] = VersionRange.Parse("[3.13.0]"),
        };
        DefaultFunctionsWorkerResolver resolver = CreateResolver(workerVersionRanges);

        FunctionsWorkerResolutionResult result = await resolver.ResolveWorkerAsync(
            new FunctionsWorkerId("node"),
            CancellationToken.None);

        FunctionsWorkerResolutionResult.Resolved resolved = result.Should().BeOfType<FunctionsWorkerResolutionResult.Resolved>().Subject;
        resolved.Worker.Version.Should().Be("3.13.0");
    }

    [Fact]
    public async Task ResolveWorkerAsync_ProfileConstraintWithoutCompatibleInstall_ReturnsMissingCompatibleVersion()
    {
        UseContentWorkloads(CreateContentWorkload(NodeWorkerPackageId, "3.12.0"));
        Dictionary<string, VersionRange> workerVersionRanges = new(StringComparer.OrdinalIgnoreCase)
        {
            ["node"] = VersionRange.Parse("[3.13.0]"),
        };
        DefaultFunctionsWorkerResolver resolver = CreateResolver(workerVersionRanges);

        FunctionsWorkerResolutionResult result = await resolver.ResolveWorkerAsync(
            new FunctionsWorkerId("node"),
            CancellationToken.None);

        FunctionsWorkerResolutionResult.NotResolved notResolved = result.Should().BeOfType<FunctionsWorkerResolutionResult.NotResolved>().Subject;
        FunctionsWorkerResolutionFailure.MissingCompatibleVersion failure =
            notResolved.Failure.Should().BeOfType<FunctionsWorkerResolutionFailure.MissingCompatibleVersion>().Subject;
        failure.VersionConstraint.Should().Be("[3.13.0]");
        failure.Message.Should().Contain("func workload install Azure.Functions.Cli.Workloads.Workers.node --exact");
    }

    [Fact]
    public async Task ResolveWorkerAsync_MissingWorkerConfig_ReturnsInvalidInstallation()
    {
        ContentWorkloadInfo workload = CreateContentWorkload(PythonWorkerPackageId, "4.43.0");
        string workerConfigPath = Path.Combine(workload.ContentRoot, "worker.config.json");
        UseContentWorkloads(workload);
        _fileSystem.FileExists(workerConfigPath).Returns(false);
        DefaultFunctionsWorkerResolver resolver = CreateResolver();

        FunctionsWorkerResolutionResult result = await resolver.ResolveWorkerAsync(
            new FunctionsWorkerId("python"),
            CancellationToken.None);

        FunctionsWorkerResolutionResult.NotResolved notResolved = result.Should().BeOfType<FunctionsWorkerResolutionResult.NotResolved>().Subject;
        FunctionsWorkerResolutionFailure.InvalidInstallation failure =
            notResolved.Failure.Should().BeOfType<FunctionsWorkerResolutionFailure.InvalidInstallation>().Subject;
        failure.WorkerId.Value.Should().Be("python");
        failure.PackageId.Should().Be(PythonWorkerPackageId);
        failure.PackageVersion.Should().Be("4.43.0");
        failure.WorkerConfigPath.Should().Be(workerConfigPath);
        failure.Message.Should().Contain("func workload install Azure.Functions.Cli.Workloads.Workers.python --exact --force");
    }

    [Fact]
    public async Task ResolveWorkerAsync_CanceledToken_ThrowsOperationCanceled()
    {
        using CancellationTokenSource source = new();
        source.Cancel();
        DefaultFunctionsWorkerResolver resolver = CreateResolver();

        await FluentActions.Awaiting(async () => await resolver.ResolveWorkerAsync(new FunctionsWorkerId("node"), source.Token)).Should().ThrowAsync<OperationCanceledException>();
        _workloads.DidNotReceive().GetContentWorkloadsByPackageId(Arg.Any<string>());
    }

    [Fact]
    public async Task ResolveWorkerAsync_NullWorkerId_Throws()
    {
        DefaultFunctionsWorkerResolver resolver = CreateResolver();

        await FluentActions.Awaiting(async () => await resolver.ResolveWorkerAsync(null!, CancellationToken.None)).Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public void Ctor_NullWorkloadProvider_Throws()
    {
        FluentActions.Invoking(() => new DefaultFunctionsWorkerResolver(null!, CreateContentResolver())).Should().ThrowExactly<ArgumentNullException>();
    }

    [Fact]
    public void Ctor_NullContentResolver_Throws()
    {
        FluentActions.Invoking(() => new DefaultFunctionsWorkerResolver(_workloads, null!)).Should().ThrowExactly<ArgumentNullException>();
    }

    [Fact]
    public void ContentResolverCtor_NullWorkerConfigFileSystem_Throws()
    {
        FluentActions.Invoking(() => new DefaultFunctionsWorkerContentResolver(null!, Options.Create(new WorkloadCatalogOptions()))).Should().ThrowExactly<ArgumentNullException>();
    }

    [Fact]
    public void ContentResolverCtor_NullCatalogOptions_Throws()
    {
        FluentActions.Invoking(() => new DefaultFunctionsWorkerContentResolver(_fileSystem, null!)).Should().ThrowExactly<ArgumentNullException>();
    }

    [Fact]
    public async Task ResolveWorkerAsync_ExactStableRange_ResolvesPrereleaseInstalled_WhenIncludePrereleaseTrue()
    {
        // Repro for issue #5286: built-in profile pins worker to exact stable [3.13.0]
        // but the catalog/installer only has 3.13.0-preview.1 installed. The resolver
        // must accept the prerelease when prerelease is enabled.
        ContentWorkloadInfo preview = CreateContentWorkload(NodeWorkerPackageId, "3.13.0-preview.1");
        UseContentWorkloads(preview);

        DefaultFunctionsWorkerResolver resolver = CreateResolver(
            new Dictionary<string, VersionRange> { ["node"] = VersionRange.Parse("[3.13.0]") },
            includePrerelease: true);

        FunctionsWorkerResolutionResult result = await resolver.ResolveWorkerAsync(
            new FunctionsWorkerId("node"),
            CancellationToken.None);

        FunctionsWorkerResolutionResult.Resolved resolved = result.Should().BeOfType<FunctionsWorkerResolutionResult.Resolved>().Subject;
        resolved.Worker.Version.Should().Be("3.13.0-preview.1");
    }

    [Fact]
    public async Task ResolveWorkerAsync_ExactStableRange_RejectsPrereleaseInstalled_WhenIncludePrereleaseFalse()
    {
        ContentWorkloadInfo preview = CreateContentWorkload(NodeWorkerPackageId, "3.13.0-preview.1");
        UseContentWorkloads(preview);

        DefaultFunctionsWorkerResolver resolver = CreateResolver(
            new Dictionary<string, VersionRange> { ["node"] = VersionRange.Parse("[3.13.0]") },
            includePrerelease: false);

        FunctionsWorkerResolutionResult result = await resolver.ResolveWorkerAsync(
            new FunctionsWorkerId("node"),
            CancellationToken.None);

        result.Should().BeOfType<FunctionsWorkerResolutionResult.NotResolved>();
    }

    private void UseContentWorkloads(params ContentWorkloadInfo[] workloads)
    {
        _workloads.GetContentWorkloads().Returns(workloads);
        _workloads.GetContentWorkloadsByPackageId(Arg.Any<string>())
            .Returns(callInfo =>
            {
                string packageId = callInfo.Arg<string>();
                IReadOnlyList<ContentWorkloadInfo> matching =
                    [.. workloads.Where(w => string.Equals(w.PackageId, packageId, StringComparison.OrdinalIgnoreCase))];
                return matching;
            });
    }

    private DefaultFunctionsWorkerResolver CreateResolver(
        IReadOnlyDictionary<string, VersionRange>? workerVersionRanges = null,
        bool includePrerelease = false)
        => new(_workloads, CreateContentResolver(includePrerelease), workerVersionRanges);

    private DefaultFunctionsWorkerContentResolver CreateContentResolver(bool includePrerelease = false)
        => new(_fileSystem, Options.Create(new WorkloadCatalogOptions { IncludePrerelease = includePrerelease }));

    private static ContentWorkloadInfo CreateContentWorkload(string packageId, string packageVersion, IReadOnlyList<string>? aliases = null)
    {
        string installDirectory = Path.Combine(Path.GetTempPath(), "workloads", packageId, packageVersion);
        return new ContentWorkloadInfo(
            packageId,
            packageVersion,
            aliases ?? [],
            installDirectory,
            Path.Combine(installDirectory, "tools", "any"),
            packageId,
            string.Empty);
    }

}
