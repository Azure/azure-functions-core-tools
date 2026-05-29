// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Configuration;
using Azure.Functions.Cli.Workloads.Storage;

namespace Azure.Functions.Cli.Hosting.FirstRun;

/// <summary>
/// File-backed first-run marker stored alongside other CLI state in the
/// func home directory. Also treats the presence of any installed workload
/// as "not first run", so users who already set up the CLI in a previous
/// session don't see the prompt again even when the marker is missing.
/// </summary>
internal sealed class FileFirstRunStateStore(
    CliConfigurationPathsOptions paths,
    IWorkloadStore workloadStore) : IFirstRunStateStore
{
    internal const string MarkerFileName = ".first-run-complete";

    private readonly CliConfigurationPathsOptions _paths = paths ?? throw new ArgumentNullException(nameof(paths));
    private readonly IWorkloadStore _workloadStore = workloadStore ?? throw new ArgumentNullException(nameof(workloadStore));

    private string MarkerPath => Path.Combine(_paths.Home, MarkerFileName);

    public async Task<bool> IsFirstRunAsync(CancellationToken cancellationToken = default)
    {
        if (File.Exists(MarkerPath))
        {
            return false;
        }

        // Existing users may have installed workloads through `func setup`
        // or `func workload install` before this marker was introduced, or
        // before we wrote it on success. Treat any installed workload as
        // "not first run" so we don't re-prompt them.
        try
        {
            IReadOnlyList<WorkloadEntry> installed = await _workloadStore.GetWorkloadsAsync(cancellationToken);
            return installed.Count == 0;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            // If we can't read the workload registry for any reason, fall
            // back to "first run = true" so the user still sees the prompt.
            return true;
        }
    }

    public async Task MarkCompleteAsync(CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(_paths.Home);

        // Touch the marker. Content is informational only; presence is the signal.
        await File.WriteAllTextAsync(MarkerPath, DateTimeOffset.UtcNow.ToString("O"), cancellationToken);
    }
}
