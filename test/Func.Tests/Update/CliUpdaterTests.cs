// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Net;
using Azure.Functions.Cli.Common.Processes;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Update;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Semver;
using Xunit;

namespace Azure.Functions.Cli.Tests.Update;

public sealed class CliUpdaterTests
{
    // Build paths through the same System.IO APIs the production code uses so
    // separators match on every platform (/ on Linux/macOS, \ on Windows).
    private static readonly string FakeProcessPath = Path.GetFullPath(Path.Combine("/fake", "install", "func"));
    private static readonly string FakeInstallDir = Path.GetDirectoryName(FakeProcessPath)!;
    private static readonly string FakeBackupDir = FakeInstallDir + ".backup";
    private static readonly string FakeTempWorkDir = Path.GetFullPath(Path.Combine("/fake", "tmp", "work"));
    private static readonly string FakeExtractDir = Path.GetFullPath(Path.Combine("/fake", "tmp", "extract"));
    private static readonly string FakeZipPath = Path.Combine(FakeTempWorkDir, "func-update.zip");

    private static readonly Release _stableRelease = new(
        SemVersion.Parse("5.1.0", SemVersionStyles.Strict),
        new Uri("public/cli/v5/5.1.0/Azure.Functions.Cli.linux-x64.5.1.0.zip", UriKind.Relative));

    [Fact]
    public async Task UpdateAsync_HappyPath_DownloadsExtractsSwapsAndVerifies()
    {
        // Arrange
        (CliUpdater updater, IUpdateFileSystem fileSystem, IProcessRunner processRunner, _) = CreateUpdater(
            httpHandler: SuccessDownloadHandler());

        processRunner.RunAsync(Arg.Any<ProcessRunRequest>(), Arg.Any<CancellationToken>())
            .Returns(OkOutcome("5.1.0\n"));

        // backupDir must not exist for the precondition check, but does exist
        // later so the cleanup path deletes it.
        fileSystem.DirectoryExists(FakeBackupDir).Returns(false, true);
        fileSystem.DirectoryExists(Arg.Is<string>(p => p != FakeBackupDir)).Returns(true);

        // Act
        await updater.UpdateAsync(_stableRelease, CancellationToken.None);

        // Assert: backup, then swap into place
        fileSystem.Received(1).MoveDirectory(FakeInstallDir, FakeBackupDir);
        fileSystem.Received(1).MoveDirectory(FakeExtractDir, FakeInstallDir);

        // Verify was run
        await processRunner.Received(1).RunAsync(Arg.Any<ProcessRunRequest>(), Arg.Any<CancellationToken>());

        // Backup cleaned up on success
        fileSystem.Received(1).DeleteDirectory(FakeBackupDir);
    }

    [Fact]
    public async Task UpdateAsync_DownloadNonSuccessStatus_ThrowsGracefulWithRetryHint()
    {
        // Arrange
        var handler = new StubHttpMessageHandler((_, _) => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        (CliUpdater updater, _, _, _) = CreateUpdater(httpHandler: handler);

        // Act + Assert
        GracefulException ex = await Assert.ThrowsAsync<GracefulException>(
            () => updater.UpdateAsync(_stableRelease, CancellationToken.None));

        Assert.Contains("503", ex.Message, StringComparison.Ordinal);
        Assert.Contains("again", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task UpdateAsync_VerificationOutputMismatch_RollsBackAndThrowsGraceful()
    {
        // Arrange
        (CliUpdater updater, IUpdateFileSystem fileSystem, IProcessRunner processRunner, _) = CreateUpdater(
            httpHandler: SuccessDownloadHandler());

        processRunner.RunAsync(Arg.Any<ProcessRunRequest>(), Arg.Any<CancellationToken>())
            .Returns(OkOutcome("4.0.0\n")); // wrong version

        // backupDir must not exist for precondition, but exists during rollback
        fileSystem.DirectoryExists(FakeBackupDir).Returns(false, true);
        fileSystem.DirectoryExists(FakeInstallDir).Returns(true);

        // Act + Assert
        GracefulException ex = await Assert.ThrowsAsync<GracefulException>(
            () => updater.UpdateAsync(_stableRelease, CancellationToken.None));

        Assert.Contains("Verification failed", ex.Message, StringComparison.OrdinalIgnoreCase);

        // Rollback: remove the bad install and restore the backup
        fileSystem.Received(1).DeleteDirectory(FakeInstallDir);
        fileSystem.Received(1).MoveDirectory(FakeBackupDir, FakeInstallDir);
    }

    [Fact]
    public async Task UpdateAsync_SwapFails_RollsBackAndRethrows()
    {
        // Arrange
        (CliUpdater updater, IUpdateFileSystem fileSystem, _, _) = CreateUpdater(
            httpHandler: SuccessDownloadHandler());

        // The swap (extractDir → installDir) throws; the backup (installDir → backupDir) succeeds.
        fileSystem.When(fs => fs.MoveDirectory(FakeExtractDir, FakeInstallDir))
            .Throw(new IOException("disk full"));

        // backupDir must not exist for precondition, but exists during rollback
        fileSystem.DirectoryExists(FakeInstallDir).Returns(false);
        fileSystem.DirectoryExists(FakeBackupDir).Returns(false, true);

        // Act + Assert
        await Assert.ThrowsAsync<IOException>(
            () => updater.UpdateAsync(_stableRelease, CancellationToken.None));

        // Rollback: backup restored since installDir doesn't exist
        fileSystem.DidNotReceive().DeleteDirectory(FakeInstallDir);
        fileSystem.Received(1).MoveDirectory(FakeBackupDir, FakeInstallDir);
    }

    [Fact]
    public async Task UpdateAsync_StaleBackupExists_ThrowsGracefulWithCleanupHint()
    {
        // Arrange
        (CliUpdater updater, IUpdateFileSystem fileSystem, _, _) = CreateUpdater(
            httpHandler: SuccessDownloadHandler());

        fileSystem.DirectoryExists(FakeBackupDir).Returns(true);

        // Act + Assert
        GracefulException ex = await Assert.ThrowsAsync<GracefulException>(
            () => updater.UpdateAsync(_stableRelease, CancellationToken.None));

        Assert.Contains("previous update backup", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private static (CliUpdater Updater, IUpdateFileSystem FileSystem, IProcessRunner ProcessRunner, ICliEnvironment Environment)
        CreateUpdater(StubHttpMessageHandler httpHandler)
    {
        IUpdateFileSystem fileSystem = Substitute.For<IUpdateFileSystem>();
        ICliEnvironment environment = Substitute.For<ICliEnvironment>();
        IProcessRunner processRunner = Substitute.For<IProcessRunner>();

        fileSystem.CreateTempDirectory().Returns(FakeTempWorkDir, FakeExtractDir);
        environment.ProcessPath.Returns(FakeProcessPath);

        var client = new HttpClient(httpHandler, disposeHandler: false)
        {
            BaseAddress = new Uri("https://cdn.functions.azure.com/"),
        };

        CliUpdater updater = new(client, fileSystem, environment, processRunner, NullLogger<CliUpdater>.Instance);
        return (updater, fileSystem, processRunner, environment);
    }

    private static StubHttpMessageHandler SuccessDownloadHandler()
    {
        byte[] fakeZipBytes = [0x50, 0x4B, 0x05, 0x06, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0]; // empty zip end-of-central-dir
        return new StubHttpMessageHandler((_, _) => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(fakeZipBytes),
        });
    }

    private static ProcessOutcome OkOutcome(string stdout) =>
        new(ExitCode: 0, StandardOutput: stdout, StandardError: string.Empty, TimedOut: false, ExecutableNotFound: false);
}
