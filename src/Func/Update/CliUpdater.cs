// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Runtime.InteropServices;
using Azure.Functions.Cli.Common.Processes;
using Azure.Functions.Cli.Common;
using Microsoft.Extensions.Logging;

namespace Azure.Functions.Cli.Update;

/// <inheritdoc cref="ICliUpdater" />
internal sealed class CliUpdater(
    HttpClient httpClient,
    IUpdateFileSystem fileSystem,
    ICliEnvironment environment,
    IProcessRunner processRunner,
    ILogger<CliUpdater> logger) : ICliUpdater
{
    private const string OldFileSuffix = ".old";

    private readonly HttpClient _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
    private readonly IUpdateFileSystem _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
    private readonly ICliEnvironment _environment = environment ?? throw new ArgumentNullException(nameof(environment));
    private readonly IProcessRunner _processRunner = processRunner ?? throw new ArgumentNullException(nameof(processRunner));
    private readonly ILogger<CliUpdater> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    public async Task UpdateAsync(Release release, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(release);

        string installDir = GetInstallDirectory();

        // Stage the extraction on the same volume as the install directory so
        // Directory.Move / File.Move never crosses volume boundaries.
        string tempWorkDir = _fileSystem.CreateTempDirectory(installDir);
        string zipPath = Path.Combine(tempWorkDir, "func-update.zip");
        string extractDir = _fileSystem.CreateTempDirectory(installDir);
        List<string> copiedFiles = [];

        try
        {
            _logger.LogInformation("Downloading func {Version}.", release.Version);
            await DownloadAsync(release, zipPath, cancellationToken);

            _logger.LogInformation("Extracting update package.");
            try
            {
                _fileSystem.ExtractZip(zipPath, extractDir);
            }
            catch (InvalidDataException ex)
            {
                throw new GracefulException(
                    $"Failed to extract the downloaded package for func {release.Version}. The archive may be corrupt. Try running 'func update' again.",
                    ex,
                    isUserError: true);
            }

            // On Windows a running process holds its own exe and dlls locked,
            // so we can't move or delete the install directory. Instead, rename
            // existing files in-place (func.exe → func.exe.old) and copy the
            // new files into the same directory.
            copiedFiles = SwapInPlace(installDir, extractDir);

            await VerifyAsync(release, installDir, cancellationToken);

            CleanupOldFiles(installDir);
            _logger.LogInformation("func {Version} installed successfully.", release.Version);
        }
        catch (Exception) when (TryRollbackInPlace(installDir, copiedFiles))
        {
            // TryRollbackInPlace always returns false; this catch exists
            // solely to trigger rollback as a filter side-effect.
            throw;
        }
        finally
        {
            TryDeleteDirectory(tempWorkDir);
            TryDeleteDirectory(extractDir);
        }
    }

    private List<string> SwapInPlace(string installDir, string extractDir)
    {
        // Rename every existing file in the install directory to .old
        IReadOnlyList<string> existingFiles = _fileSystem.GetFiles(installDir);
        foreach (string file in existingFiles)
        {
            // Skip any leftover .old files from a previous failed update
            if (file.EndsWith(OldFileSuffix, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string oldPath = file + OldFileSuffix;

            // Use overwrite so stale .old files left behind by a previous update
            // (e.g. the running exe was locked and couldn't be deleted) don't
            // block the rename.
            _fileSystem.RenameFile(file, oldPath, overwrite: true);
        }

        // Copy all new files from the extract directory into the install directory
        // and track which files were introduced for rollback purposes.
        IReadOnlyList<string> newFiles = _fileSystem.GetFiles(extractDir);
        List<string> copiedFiles = new(newFiles.Count);
        foreach (string newFile in newFiles)
        {
            string relativePath = Path.GetRelativePath(extractDir, newFile);
            copiedFiles.Add(Path.Combine(installDir, relativePath));
        }

        _fileSystem.CopyDirectory(extractDir, installDir);
        return copiedFiles;
    }

    private void CleanupOldFiles(string installDir)
    {
        // Best-effort removal of .old files after successful verification.
        IReadOnlyList<string> files = _fileSystem.GetFiles(installDir);
        foreach (string file in files)
        {
            if (file.EndsWith(OldFileSuffix, StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    _fileSystem.DeleteFile(file);
                }
                catch (Exception ex)
                {
                    // Locked files (e.g. the running exe) can't be deleted yet;
                    // they'll be cleaned up on the next launch or update.
                    _logger.LogDebug(ex, "Could not remove {File}; it will be cleaned up on next launch.", file);
                }
            }
        }
    }

    private bool TryRollbackInPlace(string installDir, IReadOnlyList<string> copiedFiles)
    {
        // Restore .old files and remove new files that were introduced by the
        // update. Best-effort per-file so a single locked file doesn't prevent
        // the rest from being restored. Always returns false so the exception
        // filter rethrows.
        bool anyRestored = false;

        // Remove files that were copied in during the update. This handles
        // both files that replaced an existing file and entirely new files
        // that have no .old counterpart.
        foreach (string file in copiedFiles)
        {
            try
            {
                if (_fileSystem.FileExists(file))
                {
                    _fileSystem.DeleteFile(file);
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Rollback: could not remove new file {File}.", file);
            }
        }

        // Restore .old files to their original paths
        try
        {
            IReadOnlyList<string> files = _fileSystem.GetFiles(installDir);
            foreach (string file in files)
            {
                if (file.EndsWith(OldFileSuffix, StringComparison.OrdinalIgnoreCase))
                {
                    string originalPath = file[..^OldFileSuffix.Length];
                    try
                    {
                        _fileSystem.RenameFile(file, originalPath);
                        anyRestored = true;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogDebug(ex, "Rollback: could not restore {File}.", file);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Rollback failed. The installation at {InstallDir} may be in an inconsistent state.", installDir);
        }

        if (anyRestored)
        {
            _logger.LogInformation("Previous version restored.");
        }

        return false;
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

    private async Task VerifyAsync(Release release, string installDir, CancellationToken cancellationToken)
    {
        string funcBinary = GetFuncBinaryPath(installDir);
        string expectedVersion = release.Version.ToString();

        ProcessOutcome outcome = await _processRunner.RunAsync(
            new ProcessRunRequest(funcBinary, ["--version"], installDir, TimeSpan.FromSeconds(30)),
            cancellationToken);

        if (outcome.ExitCode is not 0 || !VersionOutputMatches(outcome.StandardOutput, expectedVersion))
        {
            throw new GracefulException(
                $"Verification failed after installing func {expectedVersion}. " +
                $"The binary reported: '{outcome.StandardOutput.Trim()}'.",
                isUserError: true);
        }
    }

    private string GetInstallDirectory()
    {
        string? processPath = _environment.ProcessPath;
        if (string.IsNullOrEmpty(processPath))
        {
            throw new GracefulException("Could not determine the current func installation path.", isUserError: true);
        }

        string? dir = Path.GetDirectoryName(processPath);
        if (string.IsNullOrEmpty(dir))
        {
            throw new GracefulException("Could not determine the func installation directory.", isUserError: true);
        }

        return dir;
    }

    private static string GetFuncBinaryPath(string installDir) =>
        Path.Combine(installDir, RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "func.exe" : "func");

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

    private void TryDeleteDirectory(string path)
    {
        // Best-effort cleanup; swallowed so temp-dir failures don't mask the real outcome.
        try
        {
            if (_fileSystem.DirectoryExists(path))
            {
                _fileSystem.DeleteDirectory(path);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not remove temporary directory {Path}.", path);
        }
    }
}
