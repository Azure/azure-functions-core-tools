// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Configuration;

namespace Azure.Functions.Cli.Commands.Start.Azurite;

/// <summary>
/// On-disk locations the CLI uses for a managed Azurite instance. v1 keeps a
/// single shared directory under <c>&lt;funcHome&gt;/azurite/</c>; per-scope
/// isolation, metadata, and lease files are deferred until there is a real
/// consumer (multi-session sharing or a cleanup command).
/// </summary>
internal sealed record AzuriteManagedPaths(string DataDirectory, string LogFilePath);

/// <summary>
/// Computes <see cref="AzuriteManagedPaths"/> rooted at
/// <c>&lt;funcHome&gt;/azurite/</c>.
/// </summary>
internal interface IAzuriteManagedPathsProvider
{
    /// <summary>
    /// Returns the path layout. No directories are created.
    /// </summary>
    public AzuriteManagedPaths GetPaths();

    /// <summary>
    /// Ensures the data directory and the log file's parent directory exist on disk.
    /// </summary>
    public Task EnsureCreatedAsync(AzuriteManagedPaths paths, CancellationToken cancellationToken);
}

/// <inheritdoc cref="IAzuriteManagedPathsProvider"/>
internal sealed class AzuriteManagedPathsProvider(CliConfigurationPathsOptions configurationPaths) : IAzuriteManagedPathsProvider
{
    internal const string AzuriteFolderName = "azurite";
    internal const string DataFolderName = "data";
    internal const string LogFileName = "azurite.log";

    private readonly CliConfigurationPathsOptions _configurationPaths = configurationPaths ?? throw new ArgumentNullException(nameof(configurationPaths));

    public AzuriteManagedPaths GetPaths()
    {
        string root = Path.Combine(_configurationPaths.Home, AzuriteFolderName);
        return new AzuriteManagedPaths(
            DataDirectory: Path.Combine(root, DataFolderName),
            LogFilePath: Path.Combine(root, LogFileName));
    }

    public Task EnsureCreatedAsync(AzuriteManagedPaths paths, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(paths);
        cancellationToken.ThrowIfCancellationRequested();

        Directory.CreateDirectory(paths.DataDirectory);
        string? logDir = Path.GetDirectoryName(paths.LogFilePath);
        if (!string.IsNullOrEmpty(logDir))
        {
            Directory.CreateDirectory(logDir);
        }

        return Task.CompletedTask;
    }
}
