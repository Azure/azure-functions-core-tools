// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Workers;
using Azure.Functions.Cli.Workloads;
using NSubstitute;
using NuGet.Versioning;
using Xunit;

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

        FunctionsWorkerResolutionResult.Resolved resolved = Assert.IsType<FunctionsWorkerResolutionResult.Resolved>(result);
        Assert.Equal("node", resolved.Worker.Id.Value);
        Assert.Equal("node", resolved.Worker.WorkerRuntime);
        Assert.Equal(Path.Combine(workload.ContentRoot, "worker.config.json"), resolved.Worker.WorkerConfigPath);
        Assert.Equal("3.13.0", resolved.Worker.Version);
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

        FunctionsWorkerResolutionResult.Resolved resolved = Assert.IsType<FunctionsWorkerResolutionResult.Resolved>(result);
        Assert.Equal("3.13.0", resolved.Worker.Version);
        Assert.Equal(Path.Combine(newer.ContentRoot, "worker.config.json"), resolved.Worker.WorkerConfigPath);
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

        FunctionsWorkerResolutionResult.Resolved resolved = Assert.IsType<FunctionsWorkerResolutionResult.Resolved>(result);
        Assert.Equal("node", resolved.Worker.Id.Value);
        Assert.Equal("3.13.0", resolved.Worker.Version);
        Assert.Equal(Path.Combine(workload.ContentRoot, "worker.config.json"), resolved.Worker.WorkerConfigPath);
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

        FunctionsWorkerResolutionResult.Resolved resolved = Assert.IsType<FunctionsWorkerResolutionResult.Resolved>(result);
        Assert.Equal("ruby", resolved.Worker.Id.Value);
        Assert.Equal("ruby", resolved.Worker.WorkerRuntime);
        Assert.Equal(Path.Combine(workload.ContentRoot, "worker.config.json"), resolved.Worker.WorkerConfigPath);
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

        FunctionsWorkerResolutionResult.NotResolved notResolved = Assert.IsType<FunctionsWorkerResolutionResult.NotResolved>(result);
        FunctionsWorkerResolutionFailure.NotInstalled failure =
            Assert.IsType<FunctionsWorkerResolutionFailure.NotInstalled>(notResolved.Failure);
        Assert.Equal("python", failure.WorkerId.Value);
    }

    [Fact]
    public async Task ResolveWorkerAsync_NoInstalledConventionPackage_ReturnsNotInstalled()
    {
        UseContentWorkloads(CreateContentWorkload(NodeWorkerPackageId, "3.13.0"));
        DefaultFunctionsWorkerResolver resolver = CreateResolver();

        FunctionsWorkerResolutionResult result = await resolver.ResolveWorkerAsync(
            new FunctionsWorkerId("ruby"),
            CancellationToken.None);

        FunctionsWorkerResolutionResult.NotResolved notResolved = Assert.IsType<FunctionsWorkerResolutionResult.NotResolved>(result);
        FunctionsWorkerResolutionFailure.NotInstalled failure =
            Assert.IsType<FunctionsWorkerResolutionFailure.NotInstalled>(notResolved.Failure);
        Assert.Equal("ruby", failure.WorkerId.Value);
        Assert.Contains("func workload install Azure.Functions.Cli.Workloads.Workers.ruby --exact", failure.Message);
    }

    [Fact]
    public async Task ResolveWorkerAsync_InvalidInstalledVersion_ReturnsMissingCompatibleVersion()
    {
        UseContentWorkloads(CreateContentWorkload(NodeWorkerPackageId, "not-a-version"));
        DefaultFunctionsWorkerResolver resolver = CreateResolver();

        FunctionsWorkerResolutionResult result = await resolver.ResolveWorkerAsync(
            new FunctionsWorkerId("node"),
            CancellationToken.None);

        FunctionsWorkerResolutionResult.NotResolved notResolved = Assert.IsType<FunctionsWorkerResolutionResult.NotResolved>(result);
        FunctionsWorkerResolutionFailure.MissingCompatibleVersion failure =
            Assert.IsType<FunctionsWorkerResolutionFailure.MissingCompatibleVersion>(notResolved.Failure);
        Assert.Equal("node", failure.WorkerId.Value);
        Assert.Null(failure.VersionConstraint);
        Assert.Contains("func workload install Azure.Functions.Cli.Workloads.Workers.node --exact --force", failure.Message);
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

        FunctionsWorkerResolutionResult.Resolved resolved = Assert.IsType<FunctionsWorkerResolutionResult.Resolved>(result);
        Assert.Equal("go", resolved.Worker.Id.Value);
        Assert.Equal("go", resolved.Worker.WorkerRuntime);
        Assert.Equal("0.1.0", resolved.Worker.Version);
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

        FunctionsWorkerResolutionResult.Resolved resolved = Assert.IsType<FunctionsWorkerResolutionResult.Resolved>(result);
        Assert.Equal("3.13.0", resolved.Worker.Version);
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

        FunctionsWorkerResolutionResult.NotResolved notResolved = Assert.IsType<FunctionsWorkerResolutionResult.NotResolved>(result);
        FunctionsWorkerResolutionFailure.MissingCompatibleVersion failure =
            Assert.IsType<FunctionsWorkerResolutionFailure.MissingCompatibleVersion>(notResolved.Failure);
        Assert.Equal("[3.13.0]", failure.VersionConstraint);
        Assert.Contains("func workload install Azure.Functions.Cli.Workloads.Workers.node --exact", failure.Message);
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

        FunctionsWorkerResolutionResult.NotResolved notResolved = Assert.IsType<FunctionsWorkerResolutionResult.NotResolved>(result);
        FunctionsWorkerResolutionFailure.InvalidInstallation failure =
            Assert.IsType<FunctionsWorkerResolutionFailure.InvalidInstallation>(notResolved.Failure);
        Assert.Equal("python", failure.WorkerId.Value);
        Assert.Equal(PythonWorkerPackageId, failure.PackageId);
        Assert.Equal("4.43.0", failure.PackageVersion);
        Assert.Equal(workerConfigPath, failure.WorkerConfigPath);
        Assert.Contains("func workload install Azure.Functions.Cli.Workloads.Workers.python --exact --force", failure.Message);
    }

    [Fact]
    public async Task ResolveWorkerAsync_CanceledToken_ThrowsOperationCanceled()
    {
        using CancellationTokenSource source = new();
        source.Cancel();
        DefaultFunctionsWorkerResolver resolver = CreateResolver();

        await Assert.ThrowsAsync<OperationCanceledException>(
            async () => await resolver.ResolveWorkerAsync(new FunctionsWorkerId("node"), source.Token));
        _workloads.DidNotReceive().GetContentWorkloadsByPackageId(Arg.Any<string>());
    }

    [Fact]
    public async Task ResolveWorkerAsync_NullWorkerId_Throws()
    {
        DefaultFunctionsWorkerResolver resolver = CreateResolver();

        await Assert.ThrowsAsync<ArgumentNullException>(
            async () => await resolver.ResolveWorkerAsync(null!, CancellationToken.None));
    }

    [Fact]
    public void Ctor_NullWorkloadProvider_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new DefaultFunctionsWorkerResolver(null!, _fileSystem));
    }

    [Fact]
    public void Ctor_NullWorkerConfigFileSystem_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new DefaultFunctionsWorkerResolver(_workloads, null!));
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

    private DefaultFunctionsWorkerResolver CreateResolver(IReadOnlyDictionary<string, VersionRange>? workerVersionRanges = null)
        => new(_workloads, _fileSystem, workerVersionRanges);

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
