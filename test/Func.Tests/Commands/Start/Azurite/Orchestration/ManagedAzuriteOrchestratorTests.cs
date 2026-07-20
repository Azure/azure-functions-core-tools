// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using Azure.Functions.Cli.Commands.Start.Azurite;
using Azure.Functions.Cli.Commands.Start.Azurite.Launching;
using Azure.Functions.Cli.Commands.Start.Azurite.Orchestration;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Azure.Functions.Cli.Tests.Commands.Start.Azurite.Orchestration;

public class ManagedAzuriteOrchestratorTests
{
    private const string ProjectRoot = "/proj";
    private const string Conn = "UseDevelopmentStorage=true";

    private readonly IAzureWebJobsStorageClassifier _classifier = Substitute.For<IAzureWebJobsStorageClassifier>();
    private readonly IAzuriteProbe _probe = Substitute.For<IAzuriteProbe>();
    private readonly IAzuriteExecutableLocator _locator = Substitute.For<IAzuriteExecutableLocator>();
    private readonly IDockerAvailabilityProbe _dockerProbe = Substitute.For<IDockerAvailabilityProbe>();
    private readonly IAzuriteLauncher _launcher = Substitute.For<IAzuriteLauncher>();
    private readonly IAzuriteManagedPathsProvider _paths = Substitute.For<IAzuriteManagedPathsProvider>();

    public ManagedAzuriteOrchestratorTests()
    {
        AzuriteManagedPaths managed = new(
            DataDirectory: Path.Combine(Path.GetTempPath(), "azurite-data"),
            LogFilePath: Path.Combine(Path.GetTempPath(), "azurite", "azurite.log"));
        _paths.GetPaths().Returns(managed);
        _paths.EnsureCreatedAsync(Arg.Any<AzuriteManagedPaths>(), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
    }

    private ManagedAzuriteOrchestrator CreateSut() => new(
        _classifier, _probe, _locator, _dockerProbe, _launcher, _paths,
        NullLogger<ManagedAzuriteOrchestrator>.Instance);

    private static ManagedAzuriteRequest Request(bool disabled = false, TimeSpan? timeout = null) =>
        new(Conn, ProjectRoot, disabled, timeout ?? TimeSpan.FromSeconds(5));

    private static AzureWebJobsStorageReference Manageable() =>
        AzureWebJobsStorageReference.Manageable(endpoints: null, reason: "UseDevelopmentStorage=true.");

    private static AzureWebJobsStorageReference UserConfigured() =>
        AzureWebJobsStorageReference.UserConfigured(endpoints: null, reason: "User-configured proxy.");

    private static AzuriteProbeResult ProbeReady() => AzuriteProbeResult.From(new[]
    {
        new AzuriteEndpointProbeOutcome(AzuriteService.Blob, new Uri("http://127.0.0.1:10000/devstoreaccount1"), AzuriteEndpointStatus.Ready),
        new AzuriteEndpointProbeOutcome(AzuriteService.Queue, new Uri("http://127.0.0.1:10001/devstoreaccount1"), AzuriteEndpointStatus.Ready),
        new AzuriteEndpointProbeOutcome(AzuriteService.Table, new Uri("http://127.0.0.1:10002/devstoreaccount1"), AzuriteEndpointStatus.Ready),
    });

    private static AzuriteProbeResult ProbeNotListening() => AzuriteProbeResult.From(new[]
    {
        new AzuriteEndpointProbeOutcome(AzuriteService.Blob, new Uri("http://127.0.0.1:10000/devstoreaccount1"), AzuriteEndpointStatus.NotListening),
        new AzuriteEndpointProbeOutcome(AzuriteService.Queue, new Uri("http://127.0.0.1:10001/devstoreaccount1"), AzuriteEndpointStatus.NotListening),
        new AzuriteEndpointProbeOutcome(AzuriteService.Table, new Uri("http://127.0.0.1:10002/devstoreaccount1"), AzuriteEndpointStatus.NotListening),
    });

    private static AzuriteProbeResult ProbePortConflict() => AzuriteProbeResult.From(new[]
    {
        new AzuriteEndpointProbeOutcome(AzuriteService.Blob, new Uri("http://127.0.0.1:10000/devstoreaccount1"), AzuriteEndpointStatus.PortConflict),
        new AzuriteEndpointProbeOutcome(AzuriteService.Queue, new Uri("http://127.0.0.1:10001/devstoreaccount1"), AzuriteEndpointStatus.PortConflict),
        new AzuriteEndpointProbeOutcome(AzuriteService.Table, new Uri("http://127.0.0.1:10002/devstoreaccount1"), AzuriteEndpointStatus.PortConflict),
    });

    [Fact]
    public async Task Disabled_ReturnsDisabled_WithoutTouchingOtherDependencies()
    {
        ManagedAzuriteOrchestrator sut = CreateSut();

        ManagedAzuriteResult result = await sut.EnsureReadyAsync(Request(disabled: true), progress: null, CancellationToken.None);

        result.Should().BeOfType<ManagedAzuriteResult.Disabled>();
        await _probe.DidNotReceive().ProbeAsync(Arg.Any<AzuriteEndpointTuple>(), Arg.Any<CancellationToken>());
        await _locator.DidNotReceive().FindAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _launcher.DidNotReceive().StartAsync(Arg.Any<AzuriteLaunchRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task NotLocalStorage_ReturnsDisabled()
    {
        _classifier.Classify(Arg.Any<string>()).Returns(AzureWebJobsStorageReference.NotLocal("Real Azure Storage."));

        ManagedAzuriteResult result = await CreateSut().EnsureReadyAsync(Request(), progress: null, CancellationToken.None);

        result.Should().BeOfType<ManagedAzuriteResult.Disabled>();
        await _probe.DidNotReceive().ProbeAsync(Arg.Any<AzuriteEndpointTuple>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UserConfigured_AndProbeReady_ReturnsUserManaged()
    {
        _classifier.Classify(Conn).Returns(UserConfigured());
        _probe.ProbeAsync(Arg.Any<AzuriteEndpointTuple>(), Arg.Any<CancellationToken>()).Returns(ProbeReady());

        ManagedAzuriteResult result = await CreateSut().EnsureReadyAsync(Request(), progress: null, CancellationToken.None);

        result.Should().BeOfType<ManagedAzuriteResult.UserManaged>();
        await _launcher.DidNotReceive().StartAsync(Arg.Any<AzuriteLaunchRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UserConfigured_AndProbeNotReady_ReturnsFailed_WithGuidance()
    {
        _classifier.Classify(Conn).Returns(UserConfigured());
        _probe.ProbeAsync(Arg.Any<AzuriteEndpointTuple>(), Arg.Any<CancellationToken>()).Returns(ProbeNotListening());

        ManagedAzuriteResult result = await CreateSut().EnsureReadyAsync(Request(), progress: null, CancellationToken.None);

        var failed = result.Should().BeOfType<ManagedAzuriteResult.Failed>().Subject;
        failed.UserMessage.Should().Contain("cannot start this configuration automatically");
        failed.UserMessage.Should().Contain("Start Azurite");
    }

    [Fact]
    public async Task Manageable_AndProbeReady_ReturnsUserManaged_WithoutLaunching()
    {
        _classifier.Classify(Conn).Returns(Manageable());
        _probe.ProbeAsync(Arg.Any<AzuriteEndpointTuple>(), Arg.Any<CancellationToken>()).Returns(ProbeReady());

        ManagedAzuriteResult result = await CreateSut().EnsureReadyAsync(Request(), progress: null, CancellationToken.None);

        result.Should().BeOfType<ManagedAzuriteResult.UserManaged>();
        await _launcher.DidNotReceive().StartAsync(Arg.Any<AzuriteLaunchRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Manageable_PortConflict_ReturnsFailed_WithPortConflictMessage()
    {
        _classifier.Classify(Conn).Returns(Manageable());
        _probe.ProbeAsync(Arg.Any<AzuriteEndpointTuple>(), Arg.Any<CancellationToken>()).Returns(ProbePortConflict());

        ManagedAzuriteResult result = await CreateSut().EnsureReadyAsync(Request(), progress: null, CancellationToken.None);

        result.Should().BeOfType<ManagedAzuriteResult.Failed>()
            .Which.UserMessage.Should().Contain("another process is using the Azurite ports");
    }

    [Fact]
    public async Task Manageable_NotListening_NativeFound_LaunchesNative_AndReturnsStarted()
    {
        _classifier.Classify(Conn).Returns(Manageable());
        SetupProbeSequence(ProbeNotListening(), ProbeReady());
        _locator.FindAsync(ProjectRoot, Arg.Any<CancellationToken>())
            .Returns(new AzuriteExecutable("/usr/bin/azurite", AzuriteExecutableSource.Path, "3.30.0"));

        FakeAzuriteProcess fake = new();
        _launcher.StartAsync(Arg.Any<AzuriteLaunchRequest>(), Arg.Any<CancellationToken>()).Returns(fake);

        ManagedAzuriteResult result = await CreateSut().EnsureReadyAsync(Request(), progress: null, CancellationToken.None);

        var started = result.Should().BeOfType<ManagedAzuriteResult.Started>().Subject;
        started.Mode.Should().Be(AzuriteLaunchMode.Native);
        started.Process.Should().BeSameAs(fake);
        await _launcher.Received(1).StartAsync(
            Arg.Is<AzuriteLaunchRequest>(r => r.Mode == AzuriteLaunchMode.Native && r.ExecutablePath == "/usr/bin/azurite"),
            Arg.Any<CancellationToken>());
        await _dockerProbe.DidNotReceive().ProbeAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Manageable_NotListening_NoNativeButDockerAvailable_LaunchesDocker()
    {
        _classifier.Classify(Conn).Returns(Manageable());
        SetupProbeSequence(ProbeNotListening(), ProbeReady());
        _locator.FindAsync(ProjectRoot, Arg.Any<CancellationToken>()).Returns((AzuriteExecutable?)null);
        _dockerProbe.ProbeAsync(Arg.Any<CancellationToken>())
            .Returns(new DockerAvailability(DockerAvailabilityStatus.Available, "ok", "Docker 25.0"));

        FakeAzuriteProcess fake = new();
        _launcher.StartAsync(Arg.Any<AzuriteLaunchRequest>(), Arg.Any<CancellationToken>()).Returns(fake);

        ManagedAzuriteResult result = await CreateSut().EnsureReadyAsync(Request(), progress: null, CancellationToken.None);

        result.Should().BeOfType<ManagedAzuriteResult.Started>().Which.Mode.Should().Be(AzuriteLaunchMode.Docker);
        await _launcher.Received(1).StartAsync(
            Arg.Is<AzuriteLaunchRequest>(r => r.Mode == AzuriteLaunchMode.Docker),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Manageable_NotListening_NoNativeAndDockerUnavailable_ReturnsFailedWithInstallGuidance()
    {
        _classifier.Classify(Conn).Returns(Manageable());
        _probe.ProbeAsync(Arg.Any<AzuriteEndpointTuple>(), Arg.Any<CancellationToken>()).Returns(ProbeNotListening());
        _locator.FindAsync(ProjectRoot, Arg.Any<CancellationToken>()).Returns((AzuriteExecutable?)null);
        _dockerProbe.ProbeAsync(Arg.Any<CancellationToken>())
            .Returns(new DockerAvailability(DockerAvailabilityStatus.ExecutableNotFound, "not found"));

        ManagedAzuriteResult result = await CreateSut().EnsureReadyAsync(Request(), progress: null, CancellationToken.None);

        var failed = result.Should().BeOfType<ManagedAzuriteResult.Failed>().Subject;
        failed.UserMessage.Should().Contain("Azurite is not running");
        failed.UserMessage.Should().Contain("npm install -g azurite");
        failed.UserMessage.Should().Contain("Docker Desktop");
        await _launcher.DidNotReceive().StartAsync(Arg.Any<AzuriteLaunchRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PollingTimesOut_StopsProcess_ReturnsFailed()
    {
        _classifier.Classify(Conn).Returns(Manageable());
        _probe.ProbeAsync(Arg.Any<AzuriteEndpointTuple>(), Arg.Any<CancellationToken>()).Returns(ProbeNotListening());
        _locator.FindAsync(ProjectRoot, Arg.Any<CancellationToken>())
            .Returns(new AzuriteExecutable("/usr/bin/azurite", AzuriteExecutableSource.Path, "3.30.0"));

        FakeAzuriteProcess fake = new();
        _launcher.StartAsync(Arg.Any<AzuriteLaunchRequest>(), Arg.Any<CancellationToken>()).Returns(fake);

        ManagedAzuriteResult result = await CreateSut().EnsureReadyAsync(
            Request(timeout: TimeSpan.FromMilliseconds(300)),
            progress: null,
            CancellationToken.None);

        result.Should().BeOfType<ManagedAzuriteResult.Failed>()
            .Which.UserMessage.Should().Contain("did not become ready");
        fake.StopCalled.Should().BeTrue("Orchestrator must stop the process on timeout.");
    }

    [Fact]
    public async Task ProcessExitsBeforeReady_ReturnsFailedWithStderrTail()
    {
        _classifier.Classify(Conn).Returns(Manageable());
        _probe.ProbeAsync(Arg.Any<AzuriteEndpointTuple>(), Arg.Any<CancellationToken>()).Returns(ProbeNotListening());
        _locator.FindAsync(ProjectRoot, Arg.Any<CancellationToken>())
            .Returns(new AzuriteExecutable("/usr/bin/azurite", AzuriteExecutableSource.Path, "3.30.0"));

        FakeAzuriteProcess fake = new(exitedImmediately: true, stderr: ["ENOENT: missing data dir"]);
        _launcher.StartAsync(Arg.Any<AzuriteLaunchRequest>(), Arg.Any<CancellationToken>()).Returns(fake);

        ManagedAzuriteResult result = await CreateSut().EnsureReadyAsync(Request(), progress: null, CancellationToken.None);

        var failed = result.Should().BeOfType<ManagedAzuriteResult.Failed>().Subject;
        failed.UserMessage.Should().Contain("Azurite exited before it was ready");
        failed.UserMessage.Should().Contain("ENOENT");
    }

    [Fact]
    public async Task CancellationDuringPolling_StopsProcess_AndPropagates()
    {
        _classifier.Classify(Conn).Returns(Manageable());
        _probe.ProbeAsync(Arg.Any<AzuriteEndpointTuple>(), Arg.Any<CancellationToken>()).Returns(ProbeNotListening());
        _locator.FindAsync(ProjectRoot, Arg.Any<CancellationToken>())
            .Returns(new AzuriteExecutable("/usr/bin/azurite", AzuriteExecutableSource.Path, "3.30.0"));

        FakeAzuriteProcess fake = new();
        _launcher.StartAsync(Arg.Any<AzuriteLaunchRequest>(), Arg.Any<CancellationToken>()).Returns(fake);

        using var cts = new CancellationTokenSource();
        Task<ManagedAzuriteResult> resultTask = CreateSut().EnsureReadyAsync(
            Request(timeout: TimeSpan.FromSeconds(30)),
            progress: null,
            cts.Token);

        await Task.Delay(100);
        cts.Cancel();

        await FluentActions.Awaiting(() => resultTask).Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task Manageable_ReportsPhaseProgress_ToCaller()
    {
        _classifier.Classify(Conn).Returns(Manageable());
        SetupProbeSequence(ProbeNotListening(), ProbeReady());
        _locator.FindAsync(ProjectRoot, Arg.Any<CancellationToken>())
            .Returns(new AzuriteExecutable("/usr/bin/azurite", AzuriteExecutableSource.Path, "3.30.0"));

        FakeAzuriteProcess fake = new();
        _launcher.StartAsync(Arg.Any<AzuriteLaunchRequest>(), Arg.Any<CancellationToken>()).Returns(fake);

        List<string> messages = [];
        IProgress<string> progress = new CapturingProgress(messages);

        ManagedAzuriteResult result = await CreateSut().EnsureReadyAsync(Request(), progress, CancellationToken.None);

        result.Should().BeOfType<ManagedAzuriteResult.Started>();
        messages.Should().Contain(m => m.Contains("checking AzureWebJobsStorage configuration", StringComparison.OrdinalIgnoreCase));
        messages.Should().Contain(m => m.Contains("checking for an existing Azurite endpoint", StringComparison.OrdinalIgnoreCase));
        messages.Should().Contain(m => m.Contains("looking for a local Azurite installation", StringComparison.OrdinalIgnoreCase));
        messages.Should().Contain(m => m.Contains("starting Azurite (native)", StringComparison.OrdinalIgnoreCase));
        messages.Should().Contain(m => m.Contains("Azurite ready", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Manageable_NoExecutable_NoDocker_ReportsDockerCheck_BeforeFailing()
    {
        _classifier.Classify(Conn).Returns(Manageable());
        _probe.ProbeAsync(Arg.Any<AzuriteEndpointTuple>(), Arg.Any<CancellationToken>()).Returns(ProbeNotListening());
        _locator.FindAsync(ProjectRoot, Arg.Any<CancellationToken>()).Returns((AzuriteExecutable?)null);
        _dockerProbe.ProbeAsync(Arg.Any<CancellationToken>())
            .Returns(new DockerAvailability(DockerAvailabilityStatus.ExecutableNotFound, Reason: "docker not on PATH", Version: null));

        List<string> messages = [];
        IProgress<string> progress = new CapturingProgress(messages);

        ManagedAzuriteResult result = await CreateSut().EnsureReadyAsync(Request(), progress, CancellationToken.None);

        result.Should().BeOfType<ManagedAzuriteResult.Failed>();
        messages.Should().Contain(m => m.Contains("looking for a local Azurite installation", StringComparison.OrdinalIgnoreCase));
        messages.Should().Contain(m => m.Contains("checking Docker availability", StringComparison.OrdinalIgnoreCase));
    }

    private sealed class CapturingProgress(List<string> messages) : IProgress<string>
    {
        public void Report(string value)
        {
            lock (messages)
            {
                messages.Add(value);
            }
        }
    }

    private void SetupProbeSequence(params AzuriteProbeResult[] results)
    {
        if (results.Length == 0)
        {
            return;
        }

        if (results.Length == 1)
        {
            _probe.ProbeAsync(Arg.Any<AzuriteEndpointTuple>(), Arg.Any<CancellationToken>()).Returns(results[0]);
            return;
        }

        _probe.ProbeAsync(Arg.Any<AzuriteEndpointTuple>(), Arg.Any<CancellationToken>())
            .Returns(results[0], [.. results.Skip(1)]);
    }

    private sealed class FakeAzuriteProcess : IAzuriteProcess
    {
        private readonly TaskCompletionSource<int> _exit = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly ImmutableArray<string> _stderr;

        public FakeAzuriteProcess(bool exitedImmediately = false, string[]? stderr = null)
        {
            _stderr = stderr is null ? [] : [.. stderr];
            if (exitedImmediately)
            {
                _exit.TrySetResult(1);
            }
        }

        public int ProcessId => 12345;

        public bool StopCalled { get; private set; }

        public Task<int> WaitForExitAsync(CancellationToken cancellationToken) => _exit.Task;

        public Task StopAsync(CancellationToken cancellationToken)
        {
            StopCalled = true;
            _exit.TrySetResult(143);
            return Task.CompletedTask;
        }

        public async IAsyncEnumerable<string> ReadStdoutLinesAsync([EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await Task.CompletedTask;
            yield break;
        }

        public async IAsyncEnumerable<string> ReadStderrLinesAsync([EnumeratorCancellation] CancellationToken cancellationToken)
        {
            foreach (string line in _stderr)
            {
                yield return line;
            }

            await Task.CompletedTask;
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
