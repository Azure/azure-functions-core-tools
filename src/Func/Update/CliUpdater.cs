// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Runtime.InteropServices;
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
        string backupPath = binaryPath + OldFileSuffix;

        // Stage the download on the same volume as the install directory so
        // File.Move never crosses volume boundaries.
        using TempDirectory tempWorkDir = new(_fileSystem.CreateTempDirectory(installDir), _fileSystem);
        string archiveExtension = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "zip" : "tar.gz";
        string archivePath = Path.Combine(tempWorkDir.Path, $"func-update.{archiveExtension}");
        using TempDirectory extractDir = new(_fileSystem.CreateTempDirectory(installDir), _fileSystem);
        bool swapped = false;

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

            string binaryName = Path.GetFileName(binaryPath);
            string extractedBinary = Path.Combine(extractDir.Path, binaryName);

            if (!_fileSystem.FileExists(extractedBinary))
            {
                throw new GracefulException(
                    $"The downloaded archive for func {release.Version} does not contain '{binaryName}'.",
                    isUserError: true);
            }

            // Rename the running binary out of the way, then copy the new one in.
            // On Windows, renaming a running executable is allowed by the NT kernel.
            _fileSystem.MoveFile(binaryPath, backupPath, overwrite: true);
            _fileSystem.CopyFile(extractedBinary, binaryPath);
            swapped = true;

            await VerifyAsync(release, installDir, cancellationToken);

            // Best-effort removal of the backup; on Windows the running exe may
            // still be locked, in which case it gets cleaned up on next launch.
            TryDeleteFile(backupPath);
            Log.InstalledSuccessfully(_logger, release.Version);
        }
        catch (Exception)
        {
            if (swapped)
            {
                TryRollback(binaryPath, backupPath);
            }

            throw;
        }
    }

    private void TryRollback(string binaryPath, string backupPath)
    {
        try
        {
            // Remove the new binary if it was copied in
            if (_fileSystem.FileExists(binaryPath))
            {
                _fileSystem.DeleteFile(binaryPath);
            }

            // Restore the backup
            if (_fileSystem.FileExists(backupPath))
            {
                _fileSystem.MoveFile(backupPath, binaryPath);
                Log.PreviousVersionRestored(_logger);
            }
        }
        catch (Exception ex)
        {
            Log.RollbackFailed(_logger, ex, Path.GetDirectoryName(binaryPath)!);
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

        [LoggerMessage(LogLevel.Information, "func {Version} installed successfully.")]
        public static partial void InstalledSuccessfully(ILogger logger, object version);

        [LoggerMessage(LogLevel.Debug, "Could not remove {File}; it will be cleaned up on next launch.")]
        public static partial void CouldNotRemoveOldFile(ILogger logger, Exception ex, string file);

        [LoggerMessage(LogLevel.Error, "Rollback failed. The installation at {InstallDir} may be in an inconsistent state.")]
        public static partial void RollbackFailed(ILogger logger, Exception ex, string installDir);

        [LoggerMessage(LogLevel.Information, "Previous version restored.")]
        public static partial void PreviousVersionRestored(ILogger logger);

        [LoggerMessage(LogLevel.Debug, "No checksum available for {Version}; skipping integrity check.")]
        public static partial void NoChecksumAvailable(ILogger logger, object version);

        [LoggerMessage(LogLevel.Debug, "Checksum verified for {Version}.")]
        public static partial void ChecksumVerified(ILogger logger, object version);
    }
}
