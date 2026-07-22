// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Workers;
using NSubstitute;

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

        result.Should().BeSameAs(expected);
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

        FunctionsWorkerResolutionResult.Resolved resolved = result.Should().BeOfType<FunctionsWorkerResolutionResult.Resolved>().Subject;
        resolved.Worker.Id.Value.Should().Be("go");
        resolved.Worker.WorkerRuntime.Should().Be("native");
        resolved.Worker.WorkerConfigPath.Should().Be("/workloads/go/worker.config.json");
        resolved.Worker.Version.Should().Be("1.0.0");
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

        result.Should().BeSameAs(expected);
    }

    [Fact]
    public void FromWorkload_WithRuntimeOverride_RejectsNullWorkerId()
    {
        FluentActions.Invoking(() => FunctionsWorkerReference.FromWorkload(
            (FunctionsWorkerId)null!,
            workerRuntime: "native")).Should().ThrowExactly<ArgumentNullException>();
    }

    [Fact]
    public void FromWorkload_WithRuntimeOverride_RejectsBlankRuntime()
    {
        FluentActions.Invoking(() => FunctionsWorkerReference.FromWorkload(
            new FunctionsWorkerId("go"),
            workerRuntime: "   ")).Should().ThrowExactly<ArgumentException>();
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

        FunctionsWorkerResolutionResult.Resolved resolved = result.Should().BeOfType<FunctionsWorkerResolutionResult.Resolved>().Subject;
        resolved.Worker.Id.Value.Should().Be("custom");
        resolved.Worker.WorkerRuntime.Should().Be("custom");
        resolved.Worker.WorkerConfigPath.Should().Be(WorkerConfigPath);
        resolved.Worker.Version.Should().Be("1.0.0");
        _ = resolver.DidNotReceive().ResolveWorkerAsync(Arg.Any<FunctionsWorkerId>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void FromWorkerInfo_NullWorkerId_Throws()
    {
        FluentActions.Invoking(() => FunctionsWorkerReference.FromWorkerInfo(
            (FunctionsWorkerId)null!,
            "custom",
            "worker.config.json")).Should().ThrowExactly<ArgumentNullException>();
    }

    [Fact]
    public void FromWorkerInfo_EmptyWorkerRuntime_Throws()
    {
        FluentActions.Invoking(() => FunctionsWorkerReference.FromWorkerInfo(
            new FunctionsWorkerId("custom"),
            string.Empty,
            "worker.config.json")).Should().ThrowExactly<ArgumentException>();
    }

    [Fact]
    public void FromWorkerInfo_EmptyWorkerConfigPath_Throws()
    {
        FluentActions.Invoking(() => FunctionsWorkerReference.FromWorkerInfo(
            new FunctionsWorkerId("custom"),
            "custom",
            string.Empty)).Should().ThrowExactly<ArgumentException>();
    }

    [Fact]
    public void FromWorkerInfo_NullVersion_Throws()
    {
        FluentActions.Invoking(() => FunctionsWorkerReference.FromWorkerInfo(
            new FunctionsWorkerId("custom"),
            "custom",
            "worker.config.json",
            null!)).Should().ThrowExactly<ArgumentNullException>();
    }

    [Fact]
    public void Ctor_NullResolver_Throws()
    {
        FluentActions.Invoking(() => new FunctionsWorkerResolutionContext(null!)).Should().ThrowExactly<ArgumentNullException>();
    }
}
