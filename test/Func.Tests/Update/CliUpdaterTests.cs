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
    private static readonly string _fakeProcessPath = Path.GetFullPath(Path.Combine("/fake", "install", "func"));
    private static readonly string _fakeInstallDir = Path.GetDirectoryName(_fakeProcessPath)!;
    private static readonly string _fakeTempWorkDir = Path.GetFullPath(Path.Combine(_fakeInstallDir, ".func-update-work"));
    private static readonly string _fakeExtractDir = Path.GetFullPath(Path.Combine(_fakeInstallDir, ".func-update-extract"));

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

        string existingFile = Path.Combine(_fakeInstallDir, "func.exe");
        fileSystem.GetFiles(_fakeInstallDir).Returns([existingFile]);

        // SwapInPlace reads extract dir to track copied files
        string newFile = Path.Combine(_fakeExtractDir, "func.exe");
        fileSystem.GetFiles(_fakeExtractDir).Returns([newFile]);

        // Act
        await updater.UpdateAsync(_stableRelease, CancellationToken.None);

        // Assert: existing files renamed to .old with overwrite, new files copied in
        fileSystem.Received(1).MoveFile(existingFile, existingFile + ".old", true);
        fileSystem.Received(1).CopyDirectory(_fakeExtractDir, _fakeInstallDir);

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
    public async Task UpdateAsync_VerificationOutputMismatch_RollsBackAndThrowsGraceful()
    {
        // Arrange
        (CliUpdater updater, IFileSystem fileSystem, IProcessRunner processRunner, _) = CreateUpdater(
            httpHandler: SuccessDownloadHandler());

        processRunner.RunAsync(Arg.Any<ProcessRunRequest>(), Arg.Any<CancellationToken>())
            .Returns(OkOutcome("4.0.0\n")); // wrong version

        string existingFile = Path.Combine(_fakeInstallDir, "func.exe");
        string oldFile = existingFile + ".old";
        string extractedFile = Path.Combine(_fakeExtractDir, "func.exe");

        // First GetFiles(_fakeInstallDir) during SwapInPlace returns existing files;
        // second GetFiles(_fakeInstallDir) during rollback returns the .old files.
        fileSystem.GetFiles(_fakeInstallDir).Returns([existingFile], [oldFile]);
        fileSystem.GetFiles(_fakeExtractDir).Returns([extractedFile]);
        fileSystem.FileExists(existingFile).Returns(true);

        // Act + Assert
        GracefulException ex = await Assert.ThrowsAsync<GracefulException>(
            () => updater.UpdateAsync(_stableRelease, CancellationToken.None));

        Assert.Contains("Verification failed", ex.Message, StringComparison.OrdinalIgnoreCase);

        // Rollback: .old files restored, new files removed
        fileSystem.Received(1).DeleteFile(existingFile);
        fileSystem.Received(1).MoveFile(oldFile, existingFile);
    }

    [Fact]
    public async Task UpdateAsync_SwapRenameFileFails_RollsBackAndRethrows()
    {
        // Arrange
        (CliUpdater updater, IFileSystem fileSystem, _, _) = CreateUpdater(
            httpHandler: SuccessDownloadHandler());

        string existingFile = Path.Combine(_fakeInstallDir, "func.exe");
        string oldFile = existingFile + ".old";
        fileSystem.GetFiles(_fakeInstallDir).Returns([existingFile], [oldFile]);
        fileSystem.GetFiles(_fakeExtractDir).Returns([Path.Combine(_fakeExtractDir, "func.exe")]);

        // The rename throws to simulate a locked file scenario
        fileSystem.When(fs => fs.MoveFile(existingFile, oldFile, true))
            .Throw(new IOException("access denied"));

        // Act + Assert
        await Assert.ThrowsAsync<IOException>(
            () => updater.UpdateAsync(_stableRelease, CancellationToken.None));

        // Rollback attempted: restore .old → original
        fileSystem.Received(1).MoveFile(oldFile, existingFile);
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

        string existingFile = Path.Combine(_fakeInstallDir, "func.exe");
        fileSystem.GetFiles(_fakeInstallDir).Returns([existingFile]);
        fileSystem.GetFiles(_fakeExtractDir).Returns([Path.Combine(_fakeExtractDir, "func.exe")]);

        // Act
        await updater.UpdateAsync(_stableRelease, CancellationToken.None);

        // Assert — ComputeSha256Async was never called
        await fileSystem.DidNotReceive().ComputeSha256Async(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private static (CliUpdater Updater, IFileSystem FileSystem, IProcessRunner ProcessRunner, ICliEnvironment Environment)
        CreateUpdater(StubHttpMessageHandler httpHandler)
    {
        IFileSystem fileSystem = Substitute.For<IFileSystem>();
        ICliEnvironment environment = Substitute.For<ICliEnvironment>();
        IProcessRunner processRunner = Substitute.For<IProcessRunner>();

        fileSystem.CreateTempDirectory(Arg.Any<string>()).Returns(_fakeTempWorkDir, _fakeExtractDir);
        environment.ProcessPath.Returns(_fakeProcessPath);

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
