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
            SwapInPlace(installDir, extractDir);

            await VerifyAsync(release, installDir, cancellationToken);

            CleanupOldFiles(installDir);
            _logger.LogInformation("func {Version} installed successfully.", release.Version);
        }
        catch (Exception) when (TryRollbackInPlace(installDir))
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

    private void SwapInPlace(string installDir, string extractDir)
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

            _fileSystem.RenameFile(file, file + OldFileSuffix);
        }

        // Copy all new files from the extract directory into the install directory
        _fileSystem.CopyDirectory(extractDir, installDir);
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

    private bool TryRollbackInPlace(string installDir)
    {
        // Restore .old files and remove new files. Best-effort so the original
        // failure isn't masked. Always returns false so the exception filter
        // rethrows.
        try
        {
            IReadOnlyList<string> files = _fileSystem.GetFiles(installDir);
            foreach (string file in files)
            {
                if (file.EndsWith(OldFileSuffix, StringComparison.OrdinalIgnoreCase))
                {
                    string originalPath = file[..^OldFileSuffix.Length];

                    // Remove the new file that was copied in, if present
                    if (_fileSystem.FileExists(originalPath))
                    {
                        _fileSystem.DeleteFile(originalPath);
                    }

                    _fileSystem.RenameFile(file, originalPath);
                }
            }

            _logger.LogInformation("Previous version restored.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Rollback failed. The installation at {InstallDir} may be in an inconsistent state.", installDir);
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

        if (outcome.ExitCode is not 0 || !outcome.StandardOutput.Contains(expectedVersion, StringComparison.Ordinal))
        {
            throw new GracefulException(
                $"Verification failed after installing func {expectedVersion}. " +
                $"The binary reported: '{outcome.StandardOutput.Trim()}'. The previous version has been restored.",
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
