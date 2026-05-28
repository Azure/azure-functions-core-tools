// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Commands.Start.Azurite;
using Azure.Functions.Cli.Commands.Start.Azurite.Processes;
using NSubstitute;
using Xunit;

namespace Azure.Functions.Cli.Tests.Commands.Start.Azurite;

public class DockerAvailabilityProbeTests
{
    private readonly IProcessRunner _processRunner = Substitute.For<IProcessRunner>();

    private DockerAvailabilityProbe CreateSut() => new(_processRunner);

    private static ProcessOutcome Success(string stdout = "", string stderr = "") =>
        new(ExitCode: 0, StandardOutput: stdout, StandardError: stderr, TimedOut: false, ExecutableNotFound: false);

    private static ProcessOutcome NonZero(int exitCode, string stderr) =>
        new(ExitCode: exitCode, StandardOutput: string.Empty, StandardError: stderr, TimedOut: false, ExecutableNotFound: false);

    private static ProcessOutcome NotFound() =>
        new(ExitCode: null, StandardOutput: string.Empty, StandardError: string.Empty, TimedOut: false, ExecutableNotFound: true);

    private static ProcessOutcome TimedOut() =>
        new(ExitCode: null, StandardOutput: string.Empty, StandardError: string.Empty, TimedOut: true, ExecutableNotFound: false);

    private static bool IsVersionCall(ProcessRunRequest r) =>
        r.Arguments.Count == 1 && r.Arguments[0] == "--version";

    private static bool IsInfoCall(ProcessRunRequest r) =>
        r.Arguments.Count == 1 && r.Arguments[0] == "info";

    [Fact]
    public async Task ProbeAsync_BothSucceed_ReturnsAvailableWithVersion()
    {
        _processRunner.RunAsync(Arg.Is<ProcessRunRequest>(r => IsVersionCall(r)), Arg.Any<CancellationToken>())
            .Returns(Success(stdout: "Docker version 24.0.7, build afdd53b\n"));
        _processRunner.RunAsync(Arg.Is<ProcessRunRequest>(r => IsInfoCall(r)), Arg.Any<CancellationToken>())
            .Returns(Success(stdout: "Containers: 0"));

        DockerAvailability result = await CreateSut().ProbeAsync(CancellationToken.None);

        Assert.Equal(DockerAvailabilityStatus.Available, result.Status);
        Assert.Equal("Docker version 24.0.7, build afdd53b", result.Version);
    }

    [Fact]
    public async Task ProbeAsync_VersionNotFound_ReturnsExecutableNotFound()
    {
        _processRunner.RunAsync(Arg.Is<ProcessRunRequest>(r => IsVersionCall(r)), Arg.Any<CancellationToken>())
            .Returns(NotFound());

        DockerAvailability result = await CreateSut().ProbeAsync(CancellationToken.None);

        Assert.Equal(DockerAvailabilityStatus.ExecutableNotFound, result.Status);
        Assert.Null(result.Version);

        await _processRunner.DidNotReceive().RunAsync(
            Arg.Is<ProcessRunRequest>(r => IsInfoCall(r)),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ProbeAsync_InfoNonZeroExit_ReturnsDaemonUnavailableWithStderr()
    {
        _processRunner.RunAsync(Arg.Is<ProcessRunRequest>(r => IsVersionCall(r)), Arg.Any<CancellationToken>())
            .Returns(Success(stdout: "Docker version 24.0.7"));
        _processRunner.RunAsync(Arg.Is<ProcessRunRequest>(r => IsInfoCall(r)), Arg.Any<CancellationToken>())
            .Returns(NonZero(1, "Cannot connect to the Docker daemon."));

        DockerAvailability result = await CreateSut().ProbeAsync(CancellationToken.None);

        Assert.Equal(DockerAvailabilityStatus.DaemonUnavailable, result.Status);
        Assert.Contains("Cannot connect to the Docker daemon.", result.Reason);
        Assert.Equal("Docker version 24.0.7", result.Version);
    }

    [Fact]
    public async Task ProbeAsync_InfoTimesOut_ReturnsCommandFailed()
    {
        _processRunner.RunAsync(Arg.Is<ProcessRunRequest>(r => IsVersionCall(r)), Arg.Any<CancellationToken>())
            .Returns(Success(stdout: "Docker version 24.0.7"));
        _processRunner.RunAsync(Arg.Is<ProcessRunRequest>(r => IsInfoCall(r)), Arg.Any<CancellationToken>())
            .Returns(TimedOut());

        DockerAvailability result = await CreateSut().ProbeAsync(CancellationToken.None);

        Assert.Equal(DockerAvailabilityStatus.CommandFailed, result.Status);
        Assert.Contains("docker info", result.Reason);
    }

    [Fact]
    public async Task ProbeAsync_CancellationPropagates()
    {
        using CancellationTokenSource cts = new();
        cts.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => CreateSut().ProbeAsync(cts.Token));
    }
}
