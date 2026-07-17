// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Commands.Start.Azurite;
using Azure.Functions.Cli.Commands.Start.Azurite.Processes;
using NSubstitute;

namespace Azure.Functions.Cli.Tests.Commands.Start.Azurite;

public class AzuriteExecutableLocatorTests
{
    private const string ProjectRoot = "/proj";

    private readonly IPlatform _platform = Substitute.For<IPlatform>();
    private readonly IAzuriteHostEnvironment _hostEnv = Substitute.For<IAzuriteHostEnvironment>();
    private readonly IProcessRunner _processRunner = Substitute.For<IProcessRunner>();

    private AzuriteExecutableLocator CreateSut() =>
        new(_platform, _hostEnv, _processRunner);

    private void StubVersionProbeSuccess(string stdout = "3.30.0")
    {
        _processRunner.RunAsync(Arg.Any<ProcessRunRequest>(), Arg.Any<CancellationToken>())
            .Returns(new ProcessOutcome(
                ExitCode: 0,
                StandardOutput: stdout,
                StandardError: string.Empty,
                TimedOut: false,
                ExecutableNotFound: false));
    }

    [Fact]
    public async Task FindAsync_NullProjectRoot_Throws()
    {
        AzuriteExecutableLocator sut = CreateSut();

        await FluentActions.Awaiting(() => sut.FindAsync(null!, CancellationToken.None)).Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task FindAsync_Windows_PrefersProjectLocalOverPath()
    {
        _platform.IsWindows.Returns(true);
        string projectLocal = Path.Combine(ProjectRoot, "node_modules", ".bin", "azurite.cmd");
        _hostEnv.ExecutableExists(projectLocal).Returns(true);
        _hostEnv.GetPathVariable().Returns("/tools");
        _hostEnv.ExecutableExists(Path.Combine("/tools", "azurite.cmd")).Returns(true);

        StubVersionProbeSuccess();

        AzuriteExecutable? result = await CreateSut().FindAsync(ProjectRoot, CancellationToken.None);

        result.Should().NotBeNull();
        result!.FilePath.Should().Be(projectLocal);
        result.Source.Should().Be(AzuriteExecutableSource.ProjectLocal);
        result.Version.Should().Be("3.30.0");
    }

    [Fact]
    public async Task FindAsync_Unix_PrefersProjectLocalOverPath()
    {
        _platform.IsWindows.Returns(false);
        string projectLocal = Path.Combine(ProjectRoot, "node_modules", ".bin", "azurite");
        _hostEnv.ExecutableExists(projectLocal).Returns(true);
        _hostEnv.GetPathVariable().Returns("/usr/local/bin");
        _hostEnv.ExecutableExists("/usr/local/bin/azurite").Returns(true);

        StubVersionProbeSuccess();

        AzuriteExecutable? result = await CreateSut().FindAsync(ProjectRoot, CancellationToken.None);

        result.Should().NotBeNull();
        result!.FilePath.Should().Be(projectLocal);
        result.Source.Should().Be(AzuriteExecutableSource.ProjectLocal);
    }

    [Fact]
    public async Task FindAsync_Windows_PathLookup_TriesCmdThenExeThenBare()
    {
        _platform.IsWindows.Returns(true);
        _hostEnv.GetPathVariable().Returns("/tools/a;/tools/b");

        _hostEnv.ExecutableExists(Arg.Any<string>()).Returns(false);
        string match = Path.Combine("/tools/b", "azurite");
        _hostEnv.ExecutableExists(match).Returns(true);

        StubVersionProbeSuccess();

        AzuriteExecutable? result = await CreateSut().FindAsync(ProjectRoot, CancellationToken.None);

        result.Should().NotBeNull();
        result!.FilePath.Should().Be(match);
        result.Source.Should().Be(AzuriteExecutableSource.Path);
    }

    [Fact]
    public async Task FindAsync_Windows_PathLookup_PrefersCmdOverExeOverBare()
    {
        _platform.IsWindows.Returns(true);
        _hostEnv.GetPathVariable().Returns("/tools/a");

        _hostEnv.ExecutableExists(Path.Combine(ProjectRoot, "node_modules", ".bin", "azurite.cmd")).Returns(false);

        string cmd = Path.Combine("/tools/a", "azurite.cmd");
        string exe = Path.Combine("/tools/a", "azurite.exe");
        string bare = Path.Combine("/tools/a", "azurite");

        _hostEnv.ExecutableExists(cmd).Returns(true);
        _hostEnv.ExecutableExists(exe).Returns(true);
        _hostEnv.ExecutableExists(bare).Returns(true);

        StubVersionProbeSuccess();

        AzuriteExecutable? result = await CreateSut().FindAsync(ProjectRoot, CancellationToken.None);

        result.Should().NotBeNull();
        result!.FilePath.Should().Be(cmd);
    }

    [Fact]
    public async Task FindAsync_Unix_PathLookup_OnlyTriesBareName()
    {
        _platform.IsWindows.Returns(false);
        _hostEnv.GetPathVariable().Returns("/usr/local/bin:/opt/bin");
        _hostEnv.ExecutableExists(Path.Combine(ProjectRoot, "node_modules", ".bin", "azurite")).Returns(false);
        string match = Path.Combine("/opt/bin", "azurite");
        _hostEnv.ExecutableExists(Path.Combine("/usr/local/bin", "azurite")).Returns(false);
        _hostEnv.ExecutableExists(match).Returns(true);

        StubVersionProbeSuccess();

        AzuriteExecutable? result = await CreateSut().FindAsync(ProjectRoot, CancellationToken.None);

        result.Should().NotBeNull();
        result!.FilePath.Should().Be(match);

        // Ensure we never asked about .cmd/.exe variants on Unix.
        _hostEnv.DidNotReceive().ExecutableExists(Arg.Is<string>(p =>
            p.EndsWith(".cmd", StringComparison.Ordinal) || p.EndsWith(".exe", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task FindAsync_NoMatch_ReturnsNull()
    {
        _platform.IsWindows.Returns(false);
        _hostEnv.GetPathVariable().Returns("/usr/local/bin");
        _hostEnv.ExecutableExists(Arg.Any<string>()).Returns(false);

        AzuriteExecutable? result = await CreateSut().FindAsync(ProjectRoot, CancellationToken.None);

        result.Should().BeNull();
        await _processRunner.DidNotReceiveWithAnyArgs().RunAsync(default!, default);
    }

    [Fact]
    public async Task FindAsync_VersionProbeFailure_ReturnsExecutableWithNullVersion()
    {
        _platform.IsWindows.Returns(false);
        string projectLocal = Path.Combine(ProjectRoot, "node_modules", ".bin", "azurite");
        _hostEnv.ExecutableExists(projectLocal).Returns(true);

        _processRunner.RunAsync(Arg.Any<ProcessRunRequest>(), Arg.Any<CancellationToken>())
            .Returns(new ProcessOutcome(
                ExitCode: 1,
                StandardOutput: string.Empty,
                StandardError: "boom",
                TimedOut: false,
                ExecutableNotFound: false));

        AzuriteExecutable? result = await CreateSut().FindAsync(ProjectRoot, CancellationToken.None);

        result.Should().NotBeNull();
        result!.FilePath.Should().Be(projectLocal);
        result.Version.Should().BeNull();
    }

    [Fact]
    public async Task FindAsync_VersionProbeThrows_DoesNotPropagate()
    {
        _platform.IsWindows.Returns(false);
        string projectLocal = Path.Combine(ProjectRoot, "node_modules", ".bin", "azurite");
        _hostEnv.ExecutableExists(projectLocal).Returns(true);

        _processRunner.RunAsync(Arg.Any<ProcessRunRequest>(), Arg.Any<CancellationToken>())
            .Returns<Task<ProcessOutcome>>(_ => throw new InvalidOperationException("nope"));

        AzuriteExecutable? result = await CreateSut().FindAsync(ProjectRoot, CancellationToken.None);

        result.Should().NotBeNull();
        result!.Version.Should().BeNull();
    }

    [Fact]
    public async Task FindAsync_VersionProbe_StoresRawLineWhenNotSemver()
    {
        _platform.IsWindows.Returns(false);
        string projectLocal = Path.Combine(ProjectRoot, "node_modules", ".bin", "azurite");
        _hostEnv.ExecutableExists(projectLocal).Returns(true);

        StubVersionProbeSuccess("Azurite-Blob 3.30.0 (build 20250101)\nextra noise");

        AzuriteExecutable? result = await CreateSut().FindAsync(ProjectRoot, CancellationToken.None);

        result.Should().NotBeNull();
        result!.Version.Should().Be("Azurite-Blob 3.30.0 (build 20250101)");
    }

    [Fact]
    public async Task FindAsync_CancellationPropagates()
    {
        _platform.IsWindows.Returns(false);
        using CancellationTokenSource cts = new();
        cts.Cancel();

        await FluentActions.Awaiting(() => CreateSut().FindAsync(ProjectRoot, cts.Token)).Should().ThrowAsync<OperationCanceledException>();
    }
}
