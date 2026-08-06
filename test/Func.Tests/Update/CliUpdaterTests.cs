// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Net;
using Azure.Functions.Cli.Common.Processes;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Update;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Semver;
using Xunit;

namespace Azure.Functions.Cli.Tests.Update;

public sealed class CliUpdaterTests
{
    // Build paths through the same System.IO APIs the production code uses so
    // separators match on every platform (/ on Linux/macOS, \ on Windows).
    private static readonly string _fakeProcessPath = Path.GetFullPath(Path.Combine("/fake", "install", "func"));
    private static readonly string _fakeInstallDir = Path.GetDirectoryName(_fakeProcessPath)!;
    private static readonly string _fakeBackupPath = _fakeProcessPath + ".old";
    private static readonly string _fakeTempWorkDir = Path.GetFullPath(Path.Combine(_fakeInstallDir, ".func-update-work"));
    private static readonly string _fakeExtractDir = Path.GetFullPath(Path.Combine(_fakeInstallDir, ".func-update-extract"));
    private static readonly string _fakeExtractedBinary = Path.Combine(_fakeExtractDir, Path.GetFileName(_fakeProcessPath));

    private static readonly Release _stableRelease = new(
        SemVersion.Parse("5.1.0", SemVersionStyles.Strict),
        new Uri("public/cli/v5/5.1.0/Azure.Functions.Cli.linux-x64.5.1.0.zip", UriKind.Relative));

    [Fact]
    public async Task UpdateAsync_HappyPath_DownloadsExtractsSwapsAndVerifies()
    {
        // Arrange
        (CliUpdater updater, IFileSystem fileSystem, IProcessRunner processRunner, _) = CreateUpdater(
            httpHandler: SuccessDownloadHandler());

        processRunner.RunAsync(Arg.Any<ProcessRunRequest>(), Arg.Any<CancellationToken>())
            .Returns(OkOutcome("5.1.0\n"));

        fileSystem.GetFiles(_fakeExtractDir).Returns([_fakeExtractedBinary]);
        fileSystem.FileExists(_fakeProcessPath).Returns(true);

        // Act
        await updater.UpdateAsync(_stableRelease, CancellationToken.None);

        // Assert: binary renamed to .old, new binary copied in
        fileSystem.Received(1).MoveFile(_fakeProcessPath, _fakeBackupPath, true);
        fileSystem.Received(1).CopyFile(_fakeExtractedBinary, _fakeProcessPath);

        // Verify was run
        await processRunner.Received(1).RunAsync(Arg.Any<ProcessRunRequest>(), Arg.Any<CancellationToken>());
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
    public async Task UpdateAsync_VerificationOutputMismatch_RollsBackAllFilesAndThrowsGraceful()
    {
        // Arrange
        (CliUpdater updater, IFileSystem fileSystem, IProcessRunner processRunner, _) = CreateUpdater(
            httpHandler: SuccessDownloadHandler());

        processRunner.RunAsync(Arg.Any<ProcessRunRequest>(), Arg.Any<CancellationToken>())
            .Returns(OkOutcome("4.0.0\n")); // wrong version

        fileSystem.GetFiles(_fakeExtractDir).Returns([_fakeExtractedBinary]);
        fileSystem.FileExists(_fakeProcessPath).Returns(true);
        fileSystem.FileExists(_fakeBackupPath).Returns(true);

        // Act + Assert
        GracefulException ex = await Assert.ThrowsAsync<GracefulException>(
            () => updater.UpdateAsync(_stableRelease, CancellationToken.None));

        Assert.Contains("Verification failed", ex.Message, StringComparison.OrdinalIgnoreCase);

        // Rollback: new binary removed, backup restored
        fileSystem.Received(1).DeleteFile(_fakeProcessPath);
        fileSystem.Received(1).MoveFile(_fakeBackupPath, _fakeProcessPath);
    }

    [Fact]
    public async Task UpdateAsync_SwapRenameFileFails_DoesNotRollbackAndRethrows()
    {
        // Arrange
        (CliUpdater updater, IFileSystem fileSystem, _, _) = CreateUpdater(
            httpHandler: SuccessDownloadHandler());

        fileSystem.GetFiles(_fakeExtractDir).Returns([_fakeExtractedBinary]);
        fileSystem.FileExists(_fakeProcessPath).Returns(true);

        // The rename throws to simulate a locked file scenario
        fileSystem.When(fs => fs.MoveFile(_fakeProcessPath, _fakeBackupPath, true))
            .Throw(new IOException("access denied"));

        // Act + Assert
        await Assert.ThrowsAsync<IOException>(
            () => updater.UpdateAsync(_stableRelease, CancellationToken.None));

        // Swap never completed, so rollback should NOT attempt to restore backup
        fileSystem.DidNotReceive().MoveFile(_fakeBackupPath, _fakeProcessPath);
    }

    [Fact]
    public async Task UpdateAsync_ChecksumMismatch_ThrowsGracefulBeforeExtract()
    {
        // Arrange — release carries an expected checksum that won't match
        Release releaseWithChecksum = _stableRelease with { Sha256Checksum = "expected0000" };

        (CliUpdater updater, IFileSystem fileSystem, _, _) = CreateUpdater(
            httpHandler: SuccessDownloadHandler());

        fileSystem.ComputeSha256Async(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns("actual1111");

        // Act + Assert
        GracefulException ex = await Assert.ThrowsAsync<GracefulException>(
            () => updater.UpdateAsync(releaseWithChecksum, CancellationToken.None));

        Assert.Contains("Checksum mismatch", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("expected0000", ex.Message, StringComparison.Ordinal);
        Assert.Contains("actual1111", ex.Message, StringComparison.Ordinal);

        // Extract should never have been called
        fileSystem.DidNotReceive().ExtractZip(Arg.Any<string>(), Arg.Any<string>());
    }

    [Fact]
    public async Task UpdateAsync_NoChecksum_SkipsVerificationAndProceeds()
    {
        // Arrange — null checksum (current state until feed publishes them)
        (CliUpdater updater, IFileSystem fileSystem, IProcessRunner processRunner, _) = CreateUpdater(
            httpHandler: SuccessDownloadHandler());

        processRunner.RunAsync(Arg.Any<ProcessRunRequest>(), Arg.Any<CancellationToken>())
            .Returns(OkOutcome("5.1.0\n"));

        fileSystem.GetFiles(_fakeExtractDir).Returns([_fakeExtractedBinary]);
        fileSystem.FileExists(_fakeProcessPath).Returns(true);

        // Act
        await updater.UpdateAsync(_stableRelease, CancellationToken.None);

        // Assert — ComputeSha256Async was never called
        await fileSystem.DidNotReceive().ComputeSha256Async(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UpdateAsync_MultipleFiles_SwapsAllAndRollsBackOnFailure()
    {
        // Arrange — archive contains two files (e.g. func + a native dependency)
        string fakeLib = Path.Combine(_fakeExtractDir, "libgrpc.so");
        string targetLib = Path.Combine(_fakeInstallDir, "libgrpc.so");
        string backupLib = targetLib + ".old";

        (CliUpdater updater, IFileSystem fileSystem, IProcessRunner processRunner, _) = CreateUpdater(
            httpHandler: SuccessDownloadHandler());

        processRunner.RunAsync(Arg.Any<ProcessRunRequest>(), Arg.Any<CancellationToken>())
            .Returns(OkOutcome("4.0.0\n")); // wrong version — triggers rollback

        fileSystem.GetFiles(_fakeExtractDir).Returns([_fakeExtractedBinary, fakeLib]);
        fileSystem.FileExists(_fakeProcessPath).Returns(true);
        fileSystem.FileExists(targetLib).Returns(true);
        fileSystem.FileExists(_fakeBackupPath).Returns(true);
        fileSystem.FileExists(backupLib).Returns(true);

        // Act + Assert
        await Assert.ThrowsAsync<GracefulException>(
            () => updater.UpdateAsync(_stableRelease, CancellationToken.None));

        // Both files were swapped
        fileSystem.Received(1).MoveFile(_fakeProcessPath, _fakeBackupPath, true);
        fileSystem.Received(1).CopyFile(_fakeExtractedBinary, _fakeProcessPath);
        fileSystem.Received(1).MoveFile(targetLib, backupLib, true);
        fileSystem.Received(1).CopyFile(fakeLib, targetLib);

        // Both files were rolled back
        fileSystem.Received(1).DeleteFile(_fakeProcessPath);
        fileSystem.Received(1).MoveFile(_fakeBackupPath, _fakeProcessPath);
        fileSystem.Received(1).DeleteFile(targetLib);
        fileSystem.Received(1).MoveFile(backupLib, targetLib);
    }

    [Fact]
    public async Task UpdateAsync_EmptyArchive_ThrowsGraceful()
    {
        // Arrange
        (CliUpdater updater, IFileSystem fileSystem, _, _) = CreateUpdater(
            httpHandler: SuccessDownloadHandler());

        fileSystem.GetFiles(_fakeExtractDir).Returns([]);

        // Act + Assert
        GracefulException ex = await Assert.ThrowsAsync<GracefulException>(
            () => updater.UpdateAsync(_stableRelease, CancellationToken.None));

        Assert.Contains("empty", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private static (CliUpdater Updater, IFileSystem FileSystem, IProcessRunner ProcessRunner, CliEnvironmentOptions Environment)
        CreateUpdater(StubHttpMessageHandler httpHandler)
    {
        IFileSystem fileSystem = Substitute.For<IFileSystem>();
        var environment = new CliEnvironmentOptions { ProcessPath = _fakeProcessPath };
        IProcessRunner processRunner = Substitute.For<IProcessRunner>();

        fileSystem.CreateTempDirectory(Arg.Any<string>()).Returns(_fakeTempWorkDir, _fakeExtractDir);

        var client = new HttpClient(httpHandler, disposeHandler: false)
        {
            BaseAddress = new Uri("https://cdn.functions.azure.com/"),
        };

        CliUpdater updater = new(client, fileSystem, Options.Create(environment), processRunner, NullLogger<CliUpdater>.Instance);
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
