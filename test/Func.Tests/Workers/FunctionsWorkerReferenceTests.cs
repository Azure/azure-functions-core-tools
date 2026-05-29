// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Workers;
using NSubstitute;
using Xunit;

namespace Azure.Functions.Cli.Tests.Workers;

public class FunctionsWorkerReferenceTests
{
    [Fact]
    public async Task ResolveWorkerAsync_WorkloadReference_UsesWorkerResolver()
    {
        IFunctionsWorkerResolver resolver = Substitute.For<IFunctionsWorkerResolver>();
        IFunctionsWorker worker = Substitute.For<IFunctionsWorker>();
        var expected = FunctionsWorkerResolutionResults.Resolved(worker);
        resolver.ResolveWorkerAsync(
                Arg.Is<FunctionsWorkerId>(workerId => workerId.Value == "node"),
                Arg.Any<CancellationToken>())
            .Returns(expected);
        var reference = FunctionsWorkerReference.FromWorkload("node");
        var context = new FunctionsWorkerResolutionContext(resolver);

        FunctionsWorkerResolutionResult result = await reference.ResolveWorkerAsync(context, CancellationToken.None);

        Assert.Same(expected, result);
    }

    [Fact]
    public async Task ResolveWorkerAsync_WorkloadReferenceWithRuntimeOverride_ReplacesWorkerRuntime()
    {
        IFunctionsWorkerResolver resolver = Substitute.For<IFunctionsWorkerResolver>();
        IFunctionsWorker worker = FunctionsWorkerReferenceTestHelpers.Worker(
            id: "go", runtime: "go", configPath: "/workloads/go/worker.config.json", version: "1.0.0");
        resolver.ResolveWorkerAsync(
                Arg.Is<FunctionsWorkerId>(workerId => workerId.Value == "go"),
                Arg.Any<CancellationToken>())
            .Returns(FunctionsWorkerResolutionResults.Resolved(worker));
        var reference = FunctionsWorkerReference.FromWorkload("go", workerRuntime: "native");
        var context = new FunctionsWorkerResolutionContext(resolver);

        FunctionsWorkerResolutionResult result = await reference.ResolveWorkerAsync(context, CancellationToken.None);

        FunctionsWorkerResolutionResult.Resolved resolved = Assert.IsType<FunctionsWorkerResolutionResult.Resolved>(result);
        Assert.Equal("go", resolved.Worker.Id.Value);
        Assert.Equal("native", resolved.Worker.WorkerRuntime);
        Assert.Equal("/workloads/go/worker.config.json", resolved.Worker.WorkerConfigPath);
        Assert.Equal("1.0.0", resolved.Worker.Version);
    }

    [Fact]
    public async Task ResolveWorkerAsync_WorkloadReferenceWithRuntimeOverride_PassesThroughFailure()
    {
        IFunctionsWorkerResolver resolver = Substitute.For<IFunctionsWorkerResolver>();
        FunctionsWorkerResolutionResult expected = FunctionsWorkerResolutionResults.NotResolved(
            FunctionsWorkerResolutionFailures.NotInstalled(new FunctionsWorkerId("go"), "missing"));
        resolver.ResolveWorkerAsync(Arg.Any<FunctionsWorkerId>(), Arg.Any<CancellationToken>())
            .Returns(expected);
        var reference = FunctionsWorkerReference.FromWorkload("go", workerRuntime: "native");
        var context = new FunctionsWorkerResolutionContext(resolver);

        FunctionsWorkerResolutionResult result = await reference.ResolveWorkerAsync(context, CancellationToken.None);

        Assert.Same(expected, result);
    }

    [Fact]
    public void FromWorkload_WithRuntimeOverride_RejectsNullWorkerId()
    {
        Assert.Throws<ArgumentNullException>(() => FunctionsWorkerReference.FromWorkload(
            (FunctionsWorkerId)null!,
            workerRuntime: "native"));
    }

    [Fact]
    public void FromWorkload_WithRuntimeOverride_RejectsBlankRuntime()
    {
        Assert.Throws<ArgumentException>(() => FunctionsWorkerReference.FromWorkload(
            new FunctionsWorkerId("go"),
            workerRuntime: "   "));
    }

    private static class FunctionsWorkerReferenceTestHelpers
    {
        public static IFunctionsWorker Worker(string id, string runtime, string configPath, string version)
        {
            IFunctionsWorker worker = Substitute.For<IFunctionsWorker>();
            worker.Id.Returns(new FunctionsWorkerId(id));
            worker.WorkerRuntime.Returns(runtime);
            worker.WorkerConfigPath.Returns(configPath);
            worker.Version.Returns(version);
            return worker;
        }
    }

    [Fact]
    public async Task ResolveWorkerAsync_WorkerInfoReference_ReturnsWorkerInfo()
    {
        const string WorkerConfigPath = "worker.config.json";
        IFunctionsWorkerResolver resolver = Substitute.For<IFunctionsWorkerResolver>();
        var reference = FunctionsWorkerReference.FromWorkerInfo(
            "custom",
            "custom",
            WorkerConfigPath,
            "1.0.0");
        var context = new FunctionsWorkerResolutionContext(resolver);

        FunctionsWorkerResolutionResult result = await reference.ResolveWorkerAsync(context, CancellationToken.None);

        FunctionsWorkerResolutionResult.Resolved resolved = Assert.IsType<FunctionsWorkerResolutionResult.Resolved>(result);
        Assert.Equal("custom", resolved.Worker.Id.Value);
        Assert.Equal("custom", resolved.Worker.WorkerRuntime);
        Assert.Equal(WorkerConfigPath, resolved.Worker.WorkerConfigPath);
        Assert.Equal("1.0.0", resolved.Worker.Version);
        _ = resolver.DidNotReceive().ResolveWorkerAsync(Arg.Any<FunctionsWorkerId>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void FromWorkerInfo_NullWorkerId_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => FunctionsWorkerReference.FromWorkerInfo(
            (FunctionsWorkerId)null!,
            "custom",
            "worker.config.json"));
    }

    [Fact]
    public void FromWorkerInfo_EmptyWorkerRuntime_Throws()
    {
        Assert.Throws<ArgumentException>(() => FunctionsWorkerReference.FromWorkerInfo(
            new FunctionsWorkerId("custom"),
            string.Empty,
            "worker.config.json"));
    }

    [Fact]
    public void FromWorkerInfo_EmptyWorkerConfigPath_Throws()
    {
        Assert.Throws<ArgumentException>(() => FunctionsWorkerReference.FromWorkerInfo(
            new FunctionsWorkerId("custom"),
            "custom",
            string.Empty));
    }

    [Fact]
    public void FromWorkerInfo_NullVersion_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => FunctionsWorkerReference.FromWorkerInfo(
            new FunctionsWorkerId("custom"),
            "custom",
            "worker.config.json",
            null!));
    }

    [Fact]
    public void Ctor_NullResolver_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new FunctionsWorkerResolutionContext(null!));
    }
}
