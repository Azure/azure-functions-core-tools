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
        new Uri("public/cli/v5/5.1.0/Azure.Functions.Cli.linux-x64.5.1.0.zip", UriKind.Relative))
    {
        Sha256Checksum = new string('a', 64),
    };

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
        await updater.UpdateAsync(_stableRelease, progress: null, CancellationToken.None);

        // Assert: binary renamed to .old, new binary copied in
        fileSystem.Received(1).MoveFile(_fakeProcessPath, _fakeBackupPath, true);
        fileSystem.Received(1).CopyFile(_fakeExtractedBinary, _fakeProcessPath);

        // Verify was run
        await processRunner.Received(1).RunAsync(Arg.Any<ProcessRunRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UpdateAsync_WithProgress_ReportsDownloadBytesAndPipelinePhases()
    {
        (CliUpdater updater, IFileSystem fileSystem, IProcessRunner processRunner, _) = CreateUpdater(
            httpHandler: SuccessDownloadHandler());
        RecordingProgress progress = new();

        processRunner.RunAsync(Arg.Any<ProcessRunRequest>(), Arg.Any<CancellationToken>())
            .Returns(OkOutcome("5.1.0\n"));
        fileSystem.SaveStreamToFileAsync(Arg.Any<string>(), Arg.Any<Stream>(), Arg.Any<CancellationToken>())
            .Returns(async call =>
            {
                Stream content = call.ArgAt<Stream>(1);
                CancellationToken cancellationToken = call.ArgAt<CancellationToken>(2);
                await content.CopyToAsync(Stream.Null, cancellationToken);
            });
        fileSystem.GetFiles(_fakeExtractDir).Returns([_fakeExtractedBinary]);
        fileSystem.FileExists(_fakeProcessPath).Returns(true);

        await updater.UpdateAsync(_stableRelease, progress, CancellationToken.None);

        Assert.Collection(
            progress.Values.Where(value => value.BytesRead is null),
            value => Assert.Equal(UpdatePhase.Downloading, value.Phase),
            value => Assert.Equal(UpdatePhase.Extracting, value.Phase),
            value => Assert.Equal(UpdatePhase.Installing, value.Phase),
            value => Assert.Equal(UpdatePhase.Verifying, value.Phase));
        UpdateProgress download = Assert.Single(progress.Values, value => value.BytesRead is not null);
        Assert.Equal(UpdatePhase.Downloading, download.Phase);
        Assert.Equal(22, download.BytesRead);
        Assert.Equal(22, download.TotalBytes);
    }

    [Fact]
    public async Task UpdateAsync_DownloadNonSuccessStatus_ThrowsGracefulWithRetryHint()
    {
        // Arrange
        var handler = new StubHttpMessageHandler((_, _) => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        (CliUpdater updater, _, _, _) = CreateUpdater(httpHandler: handler);

        // Act + Assert
        GracefulException ex = await Assert.ThrowsAsync<GracefulException>(
            () => updater.UpdateAsync(_stableRelease, progress: null, CancellationToken.None));

        Assert.Contains("503", ex.Message, StringComparison.Ordinal);
        Assert.Contains("again", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task UpdateAsync_DownloadTimeout_ThrowsGracefulWithRetryHint()
    {
        var handler = new StubHttpMessageHandler((_, _) => throw new OperationCanceledException("timeout"));
        (CliUpdater updater, _, _, _) = CreateUpdater(httpHandler: handler);

        GracefulException ex = await Assert.ThrowsAsync<GracefulException>(
            () => updater.UpdateAsync(_stableRelease, progress: null, CancellationToken.None));

        Assert.True(ex.IsUserError);
        Assert.Contains("Timed out", ex.Message, StringComparison.Ordinal);
        Assert.Contains("again", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.IsType<OperationCanceledException>(ex.InnerException);
    }

    [Fact]
    public async Task UpdateAsync_CallerCancellation_PropagatesCancellation()
    {
        (CliUpdater updater, _, _, _) = CreateUpdater(httpHandler: SuccessDownloadHandler());
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => updater.UpdateAsync(_stableRelease, progress: null, cancellation.Token));
    }

    [Fact]
    public async Task UpdateAsync_LockUnavailable_DoesNotStageOrDownload()
    {
        IUpdateLockProvider updateLockProvider = Substitute.For<IUpdateLockProvider>();
        updateLockProvider.Acquire(_fakeInstallDir).Returns(_ =>
            throw new GracefulException("another update", isUserError: true));
        (CliUpdater updater, IFileSystem fileSystem, _, _) = CreateUpdater(
            httpHandler: SuccessDownloadHandler(),
            updateLockProvider);

        GracefulException ex = await Assert.ThrowsAsync<GracefulException>(
            () => updater.UpdateAsync(_stableRelease, progress: null, CancellationToken.None));

        Assert.Equal("another update", ex.Message);
        fileSystem.DidNotReceive().CreateTempDirectory(Arg.Any<string>());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task UpdateAsync_BodyTimeout_ThrowsGracefulAndReleasesLock(bool failOnOpen)
    {
        var failure = new OperationCanceledException("body timeout");
        var handler = new StubHttpMessageHandler((_, _) => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new FailingHttpContent(failure, failOnOpen),
        });
        IUpdateLockProvider lockProvider = Substitute.For<IUpdateLockProvider>();
        IDisposable updateLock = Substitute.For<IDisposable>();
        lockProvider.Acquire(_fakeInstallDir).Returns(updateLock);
        (CliUpdater updater, IFileSystem fileSystem, _, _) = CreateUpdater(handler, lockProvider);
        ReadDownloadBody(fileSystem);

        var ex = await Assert.ThrowsAsync<GracefulException>(
            () => updater.UpdateAsync(_stableRelease, progress: null, CancellationToken.None));

        Assert.True(ex.IsUserError);
        Assert.Contains("Timed out", ex.Message, StringComparison.Ordinal);
        Assert.Contains("run 'func update' again", ex.Message, StringComparison.Ordinal);
        Assert.Same(failure, ex.InnerException);
        fileSystem.DidNotReceive().CopyFile(Arg.Any<string>(), Arg.Any<string>());
        updateLock.Received(1).Dispose();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task UpdateAsync_CallerCancellationDuringBodyRead_PropagatesCancellation(bool failOnOpen)
    {
        using var cancellation = new CancellationTokenSource();
        var failure = new OperationCanceledException(cancellation.Token);
        var handler = new StubHttpMessageHandler((_, _) => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new FailingHttpContent(failure, failOnOpen, cancellation.Cancel),
        });
        (CliUpdater updater, IFileSystem fileSystem, _, _) = CreateUpdater(handler);
        ReadDownloadBody(fileSystem);

        var ex = await Assert.ThrowsAsync<OperationCanceledException>(
            () => updater.UpdateAsync(_stableRelease, progress: null, cancellation.Token));

        Assert.Same(failure, ex);
        Assert.Equal(cancellation.Token, ex.CancellationToken);
    }

    [Fact]
    public async Task UpdateAsync_DownloadWriteFailure_PreservesIoError()
    {
        (CliUpdater updater, IFileSystem fileSystem, _, _) = CreateUpdater(SuccessDownloadHandler());
        var failure = new IOException("disk full");
        fileSystem.SaveStreamToFileAsync(Arg.Any<string>(), Arg.Any<Stream>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(failure);

        var ex = await Assert.ThrowsAsync<IOException>(
            () => updater.UpdateAsync(_stableRelease, progress: null, CancellationToken.None));

        Assert.Same(failure, ex);
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
            () => updater.UpdateAsync(_stableRelease, progress: null, CancellationToken.None));

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
            () => updater.UpdateAsync(_stableRelease, progress: null, CancellationToken.None));

        // Swap never completed, so rollback should NOT attempt to restore backup
        fileSystem.DidNotReceive().MoveFile(_fakeBackupPath, _fakeProcessPath);
    }

    [Fact]
    public async Task UpdateAsync_ChecksumMismatch_ThrowsGracefulBeforeExtract()
    {
        // Arrange — release carries an expected checksum that won't match
        Release releaseWithChecksum = _stableRelease;
        string actualChecksum = new('b', 64);

        (CliUpdater updater, IFileSystem fileSystem, _, _) = CreateUpdater(
            httpHandler: SuccessDownloadHandler());

        fileSystem.ComputeSha256Async(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(actualChecksum);

        // Act + Assert
        GracefulException ex = await Assert.ThrowsAsync<GracefulException>(
            () => updater.UpdateAsync(releaseWithChecksum, progress: null, CancellationToken.None));

        Assert.Contains("Checksum mismatch", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(releaseWithChecksum.Sha256Checksum, ex.Message, StringComparison.Ordinal);
        Assert.Contains(actualChecksum, ex.Message, StringComparison.Ordinal);

        // Extract should never have been called
        fileSystem.DidNotReceive().ExtractZip(Arg.Any<string>(), Arg.Any<string>());
        fileSystem.DidNotReceive().ExtractTarGz(Arg.Any<string>(), Arg.Any<string>());
        fileSystem.DidNotReceive().CopyFile(Arg.Any<string>(), Arg.Any<string>());
    }

    [Fact]
    public async Task UpdateAsync_MatchingChecksum_VerifiesBeforeExtraction()
    {
        (CliUpdater updater, IFileSystem fileSystem, IProcessRunner processRunner, _) = CreateUpdater(
            httpHandler: SuccessDownloadHandler());

        processRunner.RunAsync(Arg.Any<ProcessRunRequest>(), Arg.Any<CancellationToken>())
            .Returns(OkOutcome("5.1.0\n"));

        fileSystem.GetFiles(_fakeExtractDir).Returns([_fakeExtractedBinary]);
        fileSystem.FileExists(_fakeProcessPath).Returns(true);

        // Act
        await updater.UpdateAsync(_stableRelease, progress: null, CancellationToken.None);

        await fileSystem.Received(1).ComputeSha256Async(Arg.Any<string>(), CancellationToken.None);
        Received.InOrder(() =>
        {
            _ = fileSystem.ComputeSha256Async(Arg.Any<string>(), CancellationToken.None);
            if (Release.ArchiveExtension == "zip")
            {
                fileSystem.ExtractZip(Arg.Any<string>(), _fakeExtractDir);
            }
            else
            {
                fileSystem.ExtractTarGz(Arg.Any<string>(), _fakeExtractDir);
            }
        });
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
            () => updater.UpdateAsync(_stableRelease, progress: null, CancellationToken.None));

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
    public async Task UpdateAsync_CopyFileFails_RollsBackAlreadyRenamedFile()
    {
        // Arrange — MoveFile succeeds (original renamed to .old) but CopyFile
        // throws (e.g. disk full). The entry is tracked before CopyFile so
        // rollback knows to restore the backup.
        (CliUpdater updater, IFileSystem fileSystem, _, _) = CreateUpdater(
            httpHandler: SuccessDownloadHandler());

        fileSystem.GetFiles(_fakeExtractDir).Returns([_fakeExtractedBinary]);
        fileSystem.FileExists(_fakeProcessPath).Returns(true);
        fileSystem.FileExists(_fakeBackupPath).Returns(true);

        fileSystem.When(fs => fs.CopyFile(_fakeExtractedBinary, _fakeProcessPath))
            .Throw(new IOException("disk full"));

        // Act + Assert
        await Assert.ThrowsAsync<IOException>(
            () => updater.UpdateAsync(_stableRelease, progress: null, CancellationToken.None));

        // The original was renamed to .old successfully
        fileSystem.Received(1).MoveFile(_fakeProcessPath, _fakeBackupPath, true);

        // Rollback should restore it: delete the (non-existent) new file attempt,
        // then move .old back to the original path
        fileSystem.Received(1).MoveFile(_fakeBackupPath, _fakeProcessPath);
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
            () => updater.UpdateAsync(_stableRelease, progress: null, CancellationToken.None));

        Assert.Contains("empty", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task UpdateAsync_PathTraversal_ThrowsGracefulBeforeSwap()
    {
        // Arrange — simulate an extracted file that escapes the extract directory
        string maliciousPath = Path.GetFullPath(Path.Combine(_fakeExtractDir, "..", "etc", "crontab"));

        (CliUpdater updater, IFileSystem fileSystem, _, _) = CreateUpdater(
            httpHandler: SuccessDownloadHandler());

        fileSystem.GetFiles(_fakeExtractDir).Returns([maliciousPath]);

        // Act + Assert
        GracefulException ex = await Assert.ThrowsAsync<GracefulException>(
            () => updater.UpdateAsync(_stableRelease, progress: null, CancellationToken.None));

        Assert.Contains("escapes the install directory", ex.Message, StringComparison.OrdinalIgnoreCase);

        // No file operations should have been attempted
        fileSystem.DidNotReceive().MoveFile(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<bool>());
        fileSystem.DidNotReceive().CopyFile(Arg.Any<string>(), Arg.Any<string>());
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private static (CliUpdater Updater, IFileSystem FileSystem, IProcessRunner ProcessRunner, CliEnvironmentOptions Environment)
        CreateUpdater(StubHttpMessageHandler httpHandler, IUpdateLockProvider? updateLockProvider = null)
    {
        IFileSystem fileSystem = Substitute.For<IFileSystem>();
        var environment = new CliEnvironmentOptions { ProcessPath = _fakeProcessPath };
        IProcessRunner processRunner = Substitute.For<IProcessRunner>();
        fileSystem.ComputeSha256Async(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(_stableRelease.Sha256Checksum);
        if (updateLockProvider is null)
        {
            updateLockProvider = Substitute.For<IUpdateLockProvider>();
            updateLockProvider.Acquire(_fakeInstallDir).Returns(Substitute.For<IDisposable>());
        }

        fileSystem.CreateTempDirectory(Arg.Any<string>())
            .Returns(
                new TempDirectory(_fakeTempWorkDir, fileSystem),
                new TempDirectory(_fakeExtractDir, fileSystem));

        var client = new HttpClient(httpHandler, disposeHandler: false)
        {
            BaseAddress = new Uri("https://cdn.functions.azure.com/"),
        };

        CliUpdater updater = new(
            client,
            fileSystem,
            Options.Create(environment),
            processRunner,
            updateLockProvider,
            NullLogger<CliUpdater>.Instance);
        return (updater, fileSystem, processRunner, environment);
    }

    private static void ReadDownloadBody(IFileSystem fileSystem)
    {
        fileSystem.SaveStreamToFileAsync(Arg.Any<string>(), Arg.Any<Stream>(), Arg.Any<CancellationToken>())
            .Returns(async call =>
            {
                Stream content = call.ArgAt<Stream>(1);
                CancellationToken cancellationToken = call.ArgAt<CancellationToken>(2);
                byte[] buffer = new byte[1];
                await content.ReadExactlyAsync(buffer.AsMemory(), cancellationToken);
            });
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

    private sealed class RecordingProgress : IProgress<UpdateProgress>
    {
        public List<UpdateProgress> Values { get; } = [];

        public void Report(UpdateProgress value) => Values.Add(value);
    }
}
