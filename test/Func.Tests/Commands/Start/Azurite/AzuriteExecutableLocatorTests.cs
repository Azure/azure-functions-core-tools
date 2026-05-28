// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Commands.Start.Azurite;
using Azure.Functions.Cli.Commands.Start.Azurite.Processes;
using NSubstitute;
using Xunit;

namespace Azure.Functions.Cli.Tests.Commands.Start.Azurite;

public class AzuriteExecutableLocatorTests
{
    private const string ProjectRoot = "/proj";

    private readonly IPlatform _platform = Substitute.For<IPlatform>();
    private readonly IFileExistenceChecker _fileChecker = Substitute.For<IFileExistenceChecker>();
    private readonly IEnvironmentReader _environment = Substitute.For<IEnvironmentReader>();
    private readonly IProcessRunner _processRunner = Substitute.For<IProcessRunner>();

    private AzuriteExecutableLocator CreateSut() =>
        new(_platform, _fileChecker, _environment, _processRunner);

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

        await Assert.ThrowsAsync<ArgumentNullException>(
            () => sut.FindAsync(null!, CancellationToken.None));
    }

    [Fact]
    public async Task FindAsync_Windows_PrefersProjectLocalOverPath()
    {
        _platform.IsWindows.Returns(true);
        string projectLocal = Path.Combine(ProjectRoot, "node_modules", ".bin", "azurite.cmd");
        _fileChecker.Exists(projectLocal).Returns(true);
        _environment.GetEnvironmentVariable("PATH").Returns("/tools");
        _fileChecker.Exists(Path.Combine("/tools", "azurite.cmd")).Returns(true);

        StubVersionProbeSuccess();

        AzuriteExecutable? result = await CreateSut().FindAsync(ProjectRoot, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(projectLocal, result!.FilePath);
        Assert.Equal(AzuriteExecutableSource.ProjectLocal, result.Source);
        Assert.Equal("3.30.0", result.Version);
    }

    [Fact]
    public async Task FindAsync_Unix_PrefersProjectLocalOverPath()
    {
        _platform.IsWindows.Returns(false);
        string projectLocal = Path.Combine(ProjectRoot, "node_modules", ".bin", "azurite");
        _fileChecker.Exists(projectLocal).Returns(true);
        _environment.GetEnvironmentVariable("PATH").Returns("/usr/local/bin");
        _fileChecker.Exists("/usr/local/bin/azurite").Returns(true);

        StubVersionProbeSuccess();

        AzuriteExecutable? result = await CreateSut().FindAsync(ProjectRoot, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(projectLocal, result!.FilePath);
        Assert.Equal(AzuriteExecutableSource.ProjectLocal, result.Source);
    }

    [Fact]
    public async Task FindAsync_Windows_PathLookup_TriesCmdThenExeThenBare()
    {
        _platform.IsWindows.Returns(true);
        _environment.GetEnvironmentVariable("PATH").Returns("/tools/a;/tools/b");

        _fileChecker.Exists(Arg.Any<string>()).Returns(false);
        string match = Path.Combine("/tools/b", "azurite");
        _fileChecker.Exists(match).Returns(true);

        StubVersionProbeSuccess();

        AzuriteExecutable? result = await CreateSut().FindAsync(ProjectRoot, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(match, result!.FilePath);
        Assert.Equal(AzuriteExecutableSource.Path, result.Source);
    }

    [Fact]
    public async Task FindAsync_Windows_PathLookup_PrefersCmdOverExeOverBare()
    {
        _platform.IsWindows.Returns(true);
        _environment.GetEnvironmentVariable("PATH").Returns("/tools/a");

        _fileChecker.Exists(Path.Combine(ProjectRoot, "node_modules", ".bin", "azurite.cmd")).Returns(false);

        string cmd = Path.Combine("/tools/a", "azurite.cmd");
        string exe = Path.Combine("/tools/a", "azurite.exe");
        string bare = Path.Combine("/tools/a", "azurite");

        _fileChecker.Exists(cmd).Returns(true);
        _fileChecker.Exists(exe).Returns(true);
        _fileChecker.Exists(bare).Returns(true);

        StubVersionProbeSuccess();

        AzuriteExecutable? result = await CreateSut().FindAsync(ProjectRoot, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(cmd, result!.FilePath);
    }

    [Fact]
    public async Task FindAsync_Unix_PathLookup_OnlyTriesBareName()
    {
        _platform.IsWindows.Returns(false);
        _environment.GetEnvironmentVariable("PATH").Returns("/usr/local/bin:/opt/bin");
        _fileChecker.Exists(Path.Combine(ProjectRoot, "node_modules", ".bin", "azurite")).Returns(false);
        _fileChecker.Exists("/usr/local/bin/azurite").Returns(false);
        _fileChecker.Exists("/opt/bin/azurite").Returns(true);

        StubVersionProbeSuccess();

        AzuriteExecutable? result = await CreateSut().FindAsync(ProjectRoot, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal("/opt/bin/azurite", result!.FilePath);

        // Ensure we never asked about .cmd/.exe variants on Unix.
        _fileChecker.DidNotReceive().Exists(Arg.Is<string>(p =>
            p.EndsWith(".cmd", StringComparison.Ordinal) || p.EndsWith(".exe", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task FindAsync_NoMatch_ReturnsNull()
    {
        _platform.IsWindows.Returns(false);
        _environment.GetEnvironmentVariable("PATH").Returns("/usr/local/bin");
        _fileChecker.Exists(Arg.Any<string>()).Returns(false);

        AzuriteExecutable? result = await CreateSut().FindAsync(ProjectRoot, CancellationToken.None);

        Assert.Null(result);
        await _processRunner.DidNotReceiveWithAnyArgs().RunAsync(default!, default);
    }

    [Fact]
    public async Task FindAsync_VersionProbeFailure_ReturnsExecutableWithNullVersion()
    {
        _platform.IsWindows.Returns(false);
        string projectLocal = Path.Combine(ProjectRoot, "node_modules", ".bin", "azurite");
        _fileChecker.Exists(projectLocal).Returns(true);

        _processRunner.RunAsync(Arg.Any<ProcessRunRequest>(), Arg.Any<CancellationToken>())
            .Returns(new ProcessOutcome(
                ExitCode: 1,
                StandardOutput: string.Empty,
                StandardError: "boom",
                TimedOut: false,
                ExecutableNotFound: false));

        AzuriteExecutable? result = await CreateSut().FindAsync(ProjectRoot, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(projectLocal, result!.FilePath);
        Assert.Null(result.Version);
    }

    [Fact]
    public async Task FindAsync_VersionProbeThrows_DoesNotPropagate()
    {
        _platform.IsWindows.Returns(false);
        string projectLocal = Path.Combine(ProjectRoot, "node_modules", ".bin", "azurite");
        _fileChecker.Exists(projectLocal).Returns(true);

        _processRunner.RunAsync(Arg.Any<ProcessRunRequest>(), Arg.Any<CancellationToken>())
            .Returns<Task<ProcessOutcome>>(_ => throw new InvalidOperationException("nope"));

        AzuriteExecutable? result = await CreateSut().FindAsync(ProjectRoot, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Null(result!.Version);
    }

    [Fact]
    public async Task FindAsync_VersionProbe_StoresRawLineWhenNotSemver()
    {
        _platform.IsWindows.Returns(false);
        string projectLocal = Path.Combine(ProjectRoot, "node_modules", ".bin", "azurite");
        _fileChecker.Exists(projectLocal).Returns(true);

        StubVersionProbeSuccess("Azurite-Blob 3.30.0 (build 20250101)\nextra noise");

        AzuriteExecutable? result = await CreateSut().FindAsync(ProjectRoot, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal("Azurite-Blob 3.30.0 (build 20250101)", result!.Version);
    }

    [Fact]
    public async Task FindAsync_CancellationPropagates()
    {
        _platform.IsWindows.Returns(false);
        using CancellationTokenSource cts = new();
        cts.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => CreateSut().FindAsync(ProjectRoot, cts.Token));
    }
}
