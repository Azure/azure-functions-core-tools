// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Configuration;
using Azure.Functions.Cli.Workloads.Install;
using Microsoft.Extensions.Logging;

namespace Azure.Functions.Cli.Templates.Engine;

/// <summary>
/// Deletes the template engine's cache when workloads change, so the next discovery rebuilds it from the live workload inventory.
/// </summary>
/// <param name="configurationPaths">The service to resolve configuration paths from.</param>
/// <param name="fileSystem">The file system used to locate and delete the cache files.</param>
/// <param name="logger">The logger used to report cache files that could not be deleted.</param>
internal sealed class WorkloadTemplateCacheInvalidator(
    CliConfigurationPathsOptions configurationPaths, IFileSystem fileSystem, ILogger<WorkloadTemplateCacheInvalidator> logger)
    : IWorkloadChangeObserver
{
    // The engine's cache file name is internal to the engine.
    private const string TemplateCacheFileName = "templatecache.json";

    private readonly CliConfigurationPathsOptions _configurationPaths = configurationPaths
        ?? throw new ArgumentNullException(nameof(configurationPaths));
    private readonly IFileSystem _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
    private readonly ILogger<WorkloadTemplateCacheInvalidator> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    public Task OnWorkloadsChangedAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        // Workloads are shared by every CLI version, and the engine keeps one cache per CLI version under this directory.
        string hostDirectory = Path.Combine(_configurationPaths.Home, TemplaterFactory.SettingsDirectoryName, FuncTemplateEngineHost.Identifier);
        if (!_fileSystem.DirectoryExists(hostDirectory))
        {
            return Task.CompletedTask;
        }

        IReadOnlyList<string> files;
        try
        {
            files = _fileSystem.GetFiles(hostDirectory);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The cache also refreshes lazily when roots or timestamps change, so a stale cache is not a failure of the workload change.
            _logger.LogWarning(ex, "Could not enumerate the template cache under {Directory}.", hostDirectory);
            return Task.CompletedTask;
        }

        foreach (string file in files.Where(IsTemplateCache))
        {
            try
            {
                _fileSystem.DeleteFile(file);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // See above: the lazy refresh covers a cache file that could not be deleted.
                _logger.LogWarning(ex, "Could not delete the template cache {File}.", file);
            }
        }

        return Task.CompletedTask;
    }

    private static bool IsTemplateCache(string path)
        => string.Equals(Path.GetFileName(path), TemplateCacheFileName, StringComparison.OrdinalIgnoreCase);
}
