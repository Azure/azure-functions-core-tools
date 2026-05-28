// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Configuration;

namespace Azure.Functions.Cli.Hosting.FirstRun;

/// <summary>
/// File-backed first-run marker stored alongside other CLI state in the
/// func home directory.
/// </summary>
internal sealed class FileFirstRunStateStore(CliConfigurationPathsOptions paths) : IFirstRunStateStore
{
    internal const string MarkerFileName = ".first-run-complete";

    private readonly CliConfigurationPathsOptions _paths = paths ?? throw new ArgumentNullException(nameof(paths));

    private string MarkerPath => Path.Combine(_paths.Home, MarkerFileName);

    public bool IsFirstRun() => !File.Exists(MarkerPath);

    public async Task MarkCompleteAsync(CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(_paths.Home);

        // Touch the marker. Content is informational only; presence is the signal.
        await File.WriteAllTextAsync(MarkerPath, DateTimeOffset.UtcNow.ToString("O"), cancellationToken);
    }
}
