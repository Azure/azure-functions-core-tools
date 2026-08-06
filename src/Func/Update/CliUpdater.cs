// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Common.Processes;
using Azure.Functions.Cli.Common;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Azure.Functions.Cli.Update;

/// <inheritdoc cref="ICliUpdater" />
internal sealed partial class CliUpdater(
    HttpClient httpClient,
    IFileSystem fileSystem,
    IOptions<CliEnvironmentOptions> environmentOptions,
    IProcessRunner processRunner,
    ILogger<CliUpdater> logger) : ICliUpdater
{
    private const string OldFileSuffix = ".old";

    private readonly HttpClient _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
    private readonly IFileSystem _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
    private readonly CliEnvironmentOptions _environment = (environmentOptions ?? throw new ArgumentNullException(nameof(environmentOptions))).Value;
    private readonly IProcessRunner _processRunner = processRunner ?? throw new ArgumentNullException(nameof(processRunner));
    private readonly ILogger<CliUpdater> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    public async Task UpdateAsync(Release release, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(release);

        string binaryPath = GetBinaryPath();
        string installDir = Path.GetDirectoryName(binaryPath)!;

        // Stage the download on the same volume as the install directory so
        // File.Move never crosses volume boundaries.
        using TempDirectory tempWorkDir = new(_fileSystem.CreateTempDirectory(installDir), _fileSystem);
        string archivePath = Path.Combine(tempWorkDir.Path, $"func-update.{Release.ArchiveExtension}");
        using TempDirectory extractDir = new(_fileSystem.CreateTempDirectory(installDir), _fileSystem);

        // Tracks files that were successfully swapped so we can roll them back.
        List<(string TargetPath, string BackupPath)> swappedFiles = [];

        try
        {
            Log.DownloadingVersion(_logger, release.Version);
            await DownloadAsync(release, archivePath, cancellationToken);

            await VerifyChecksumAsync(release, archivePath, cancellationToken);

            Log.ExtractingUpdatePackage(_logger);
            try
            {
                ExtractArchive(archivePath, extractDir.Path);
            }
            catch (InvalidDataException ex)
            {
                throw new GracefulException(
                    $"Failed to extract the downloaded package for func {release.Version}. The archive may be corrupt. Try running 'func update' again.",
                    ex,
                    isUserError: true);
            }

            IReadOnlyList<string> extractedFiles = _fileSystem.GetFiles(extractDir.Path);
            if (extractedFiles.Count == 0)
            {
                throw new GracefulException(
                    $"The downloaded archive for func {release.Version} is empty.",
                    isUserError: true);
            }

            Log.UpdatingFiles(_logger, extractedFiles.Count);

            // Rename each existing file to .old, then copy the new one in.
            // On Windows, renaming running executables and loaded DLLs is allowed
            // by the NT kernel (the file handle follows the inode, not the name).
            foreach (string extractedPath in extractedFiles)
            {
                string relativePath = Path.GetRelativePath(extractDir.Path, extractedPath);

                // Defense-in-depth against zip-slip: reject any path that escapes
                // the install directory. .NET's extraction APIs already prevent
                // this, but we validate as a second layer.
                if (relativePath.StartsWith("..", StringComparison.Ordinal))
                {
                    throw new GracefulException(
                        $"The archive contains a path that escapes the install directory: '{relativePath}'. " +
                        "This may indicate a corrupted or tampered archive.",
                        isUserError: true);
                }

                string targetPath = Path.Combine(installDir, relativePath);
                string backupPath = targetPath + OldFileSuffix;

                // Ensure the target subdirectory exists (archive may contain nested structure)
                string? targetDir = Path.GetDirectoryName(targetPath);
                if (targetDir is not null && !_fileSystem.DirectoryExists(targetDir))
                {
                    _fileSystem.CreateDirectory(targetDir);
                }

                if (_fileSystem.FileExists(targetPath))
                {
                    _fileSystem.MoveFile(targetPath, backupPath, overwrite: true);
                }

                // Track before CopyFile so rollback can restore the backup even
                // if the copy fails (e.g. disk full).
                swappedFiles.Add((targetPath, backupPath));
                _fileSystem.CopyFile(extractedPath, targetPath);
            }

            await VerifyAsync(release, installDir, cancellationToken);

            // Best-effort removal of backups; on Windows the running exe may
            // still be locked, in which case it gets overwritten on the next update.
            foreach ((string _, string backupPath) in swappedFiles)
            {
                TryDeleteFile(backupPath);
            }

            Log.InstalledSuccessfully(_logger, release.Version);
        }
        catch (Exception)
        {
            if (swappedFiles.Count > 0)
            {
                TryRollback(swappedFiles, installDir);
            }

            throw;
        }
    }

    private void TryRollback(List<(string TargetPath, string BackupPath)> swappedFiles, string installDir)
    {
        bool anyFailed = false;

        foreach ((string targetPath, string backupPath) in swappedFiles)
        {
            try
            {
                // Remove the new file that was copied in
                if (_fileSystem.FileExists(targetPath))
                {
                    _fileSystem.DeleteFile(targetPath);
                }

                // Restore the backup if one was created
                if (_fileSystem.FileExists(backupPath))
                {
                    _fileSystem.MoveFile(backupPath, targetPath);
                }
            }
            catch (Exception ex)
            {
                anyFailed = true;
                Log.RollbackFileFailed(_logger, ex, targetPath);
            }
        }

        if (anyFailed)
        {
            Log.RollbackFailed(_logger, installDir);
        }
        else
        {
            Log.PreviousVersionRestored(_logger);
        }
    }

    private async Task DownloadAsync(Release release, string zipPath, CancellationToken cancellationToken)
    {
        HttpResponseMessage response;
        try
        {
            response = await _httpClient.GetAsync(release.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            throw new GracefulException(
                $"Could not reach the CDN to download func {release.Version}. Check your connection and run 'func update' again.",
                ex,
                isUserError: true);
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                throw new GracefulException(
                    $"CDN returned {(int)response.StatusCode} while downloading func {release.Version}. Try again later.",
                    isUserError: true);
            }

            await using Stream content = await response.Content.ReadAsStreamAsync(cancellationToken);
            await _fileSystem.SaveStreamToFileAsync(zipPath, content, cancellationToken);
        }
    }

    private async Task VerifyChecksumAsync(Release release, string filePath, CancellationToken cancellationToken)
    {
        if (release.Sha256Checksum is null)
        {
            // TODO: Remove this early-return once the release feed publishes checksums (#5445).
            Log.NoChecksumAvailable(_logger, release.Version);
            return;
        }

        string actual = await _fileSystem.ComputeSha256Async(filePath, cancellationToken);

        if (!string.Equals(actual, release.Sha256Checksum, StringComparison.OrdinalIgnoreCase))
        {
            throw new GracefulException(
                $"Checksum mismatch for func {release.Version}. " +
                $"Expected '{release.Sha256Checksum}' but got '{actual}'. " +
                "The download may be corrupt or tampered with. Try running 'func update' again.",
                isUserError: true);
        }

        Log.ChecksumVerified(_logger, release.Version);
    }

    private async Task VerifyAsync(Release release, string installDir, CancellationToken cancellationToken)
    {
        string expectedVersion = release.Version.ToString();

        ProcessOutcome outcome = await _processRunner.RunAsync(
            new ProcessRunRequest(_environment.ProcessPath!, ["--version"], installDir, TimeSpan.FromSeconds(30)),
            cancellationToken);

        if (outcome.ExitCode is not 0 || !VersionOutputMatches(outcome.StandardOutput, expectedVersion))
        {
            throw new GracefulException(
                $"Verification failed after installing func {expectedVersion}. " +
                $"The binary reported: '{outcome.StandardOutput.Trim()}'.",
                isUserError: true);
        }
    }

    private string GetBinaryPath()
    {
        string? processPath = _environment.ProcessPath;
        if (string.IsNullOrEmpty(processPath))
        {
            throw new GracefulException("Could not determine the current func installation path.", isUserError: true);
        }

        return processPath;
    }

    private static bool VersionOutputMatches(string output, string expectedVersion)
    {
        // Check each line for an exact match to avoid false positives where one
        // version string is a substring of another (e.g. "5.1.0" inside "15.1.0").
        foreach (string line in output.Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            if (line.Equals(expectedVersion, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private void ExtractArchive(string archivePath, string destinationDirectory)
    {
        if (archivePath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
        {
            _fileSystem.ExtractZip(archivePath, destinationDirectory);
        }
        else if (archivePath.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase))
        {
            _fileSystem.ExtractTarGz(archivePath, destinationDirectory);
        }
        else
        {
            throw new GracefulException(
                $"Unsupported archive format: '{Path.GetFileName(archivePath)}'. Expected .zip or .tar.gz.",
                isUserError: true);
        }
    }

    private void TryDeleteFile(string path)
    {
        try
        {
            if (_fileSystem.FileExists(path))
            {
                _fileSystem.DeleteFile(path);
            }
        }
        catch (Exception ex)
        {
            // On Windows the running exe may be locked; it'll be overwritten
            // on the next update via MoveFile with overwrite: true.
            Log.CouldNotRemoveOldFile(_logger, ex, path);
        }
    }

    private static partial class Log
    {
        [LoggerMessage(LogLevel.Information, "Downloading func {Version}.")]
        public static partial void DownloadingVersion(ILogger logger, object version);

        [LoggerMessage(LogLevel.Information, "Extracting update package.")]
        public static partial void ExtractingUpdatePackage(ILogger logger);

        [LoggerMessage(LogLevel.Information, "Updating {Count} file(s).")]
        public static partial void UpdatingFiles(ILogger logger, int count);

        [LoggerMessage(LogLevel.Information, "func {Version} installed successfully.")]
        public static partial void InstalledSuccessfully(ILogger logger, object version);

        [LoggerMessage(LogLevel.Debug, "Could not remove {File}; it will be overwritten on the next update.")]
        public static partial void CouldNotRemoveOldFile(ILogger logger, Exception ex, string file);

        [LoggerMessage(LogLevel.Error, "The installation at {InstallDir} may be in an inconsistent state. Some files could not be rolled back.")]
        public static partial void RollbackFailed(ILogger logger, string installDir);

        [LoggerMessage(LogLevel.Error, "Could not restore {File} during rollback.")]
        public static partial void RollbackFileFailed(ILogger logger, Exception ex, string file);

        [LoggerMessage(LogLevel.Information, "Previous version restored.")]
        public static partial void PreviousVersionRestored(ILogger logger);

        [LoggerMessage(LogLevel.Debug, "No checksum available for {Version}; skipping integrity check.")]
        public static partial void NoChecksumAvailable(ILogger logger, object version);

        [LoggerMessage(LogLevel.Debug, "Checksum verified for {Version}.")]
        public static partial void ChecksumVerified(ILogger logger, object version);
    }
}
