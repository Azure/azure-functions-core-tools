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
    private readonly HttpClient _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
    private readonly IUpdateFileSystem _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
    private readonly ICliEnvironment _environment = environment ?? throw new ArgumentNullException(nameof(environment));
    private readonly IProcessRunner _processRunner = processRunner ?? throw new ArgumentNullException(nameof(processRunner));
    private readonly ILogger<CliUpdater> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    public async Task UpdateAsync(Release release, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(release);

        string tempWorkDir = _fileSystem.CreateTempDirectory();
        string zipPath = Path.Combine(tempWorkDir, "func-update.zip");
        string extractDir = _fileSystem.CreateTempDirectory();

        string installDir = GetInstallDirectory();
        string backupDir = installDir + ".backup";

        if (_fileSystem.DirectoryExists(backupDir))
        {
            throw new GracefulException(
                $"A previous update backup exists at '{backupDir}'. " +
                "Delete it manually and run 'func update' again.");
        }

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
                    ex);
            }

            _fileSystem.MoveDirectory(installDir, backupDir);

            // Swap extracted files into place, then verify. Any failure after
            // the backup triggers a rollback so the previous version is restored.
            bool installSucceeded = false;
            try
            {
                _fileSystem.MoveDirectory(extractDir, installDir);
                await VerifyAsync(release, installDir, cancellationToken);
                installSucceeded = true;
            }
            finally
            {
                if (!installSucceeded)
                {
                    TryRollback(installDir, backupDir);
                }
            }

            TryDeleteDirectory(backupDir);
            _logger.LogInformation("func {Version} installed successfully.", release.Version);
        }
        finally
        {
            TryDeleteDirectory(tempWorkDir);
            TryDeleteDirectory(extractDir);
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
                $"The binary reported: '{outcome.StandardOutput.Trim()}'. The previous version has been restored.");
        }
    }

    private string GetInstallDirectory()
    {
        string? processPath = _environment.ProcessPath;
        if (string.IsNullOrEmpty(processPath))
        {
            throw new GracefulException("Could not determine the current func installation path.");
        }

        string? dir = Path.GetDirectoryName(processPath);
        if (string.IsNullOrEmpty(dir))
        {
            throw new GracefulException("Could not determine the func installation directory.");
        }

        return dir;
    }

    private static string GetFuncBinaryPath(string installDir) =>
        Path.Combine(installDir, RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "func.exe" : "func");

    private void TryRollback(string installDir, string backupDir)
    {
        // Best-effort rollback; swallowed so the original failure isn't masked.
        try
        {
            if (_fileSystem.DirectoryExists(installDir))
            {
                _fileSystem.DeleteDirectory(installDir);
            }

            if (_fileSystem.DirectoryExists(backupDir))
            {
                _fileSystem.MoveDirectory(backupDir, installDir);
                _logger.LogInformation("Previous version restored.");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Rollback failed. Manual recovery may be needed from {BackupDir}.", backupDir);
        }
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
