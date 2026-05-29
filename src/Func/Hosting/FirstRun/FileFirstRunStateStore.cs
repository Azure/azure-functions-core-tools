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
        => await GetStateAsync(cancellationToken) == FirstRunState.NeverPrompted;

    public async Task<FirstRunState> GetStateAsync(CancellationToken cancellationToken = default)
    {
        bool markerExists = File.Exists(MarkerPath);

        // Existing users may have installed workloads through `func setup`
        // or `func workload install` before this marker was introduced, or
        // before we wrote it on success. Treat any installed workload as
        // "not first run" so we don't re-prompt them.
        bool hasWorkloads;
        try
        {
            IReadOnlyList<WorkloadEntry> installed = await _workloadStore.GetWorkloadsAsync(cancellationToken);
            hasWorkloads = installed.Count > 0;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            // If we can't read the workload registry, lean toward the
            // marker: it's the user's explicit signal. Treat as no
            // workloads so the prompt still surfaces when there's no
            // marker either.
            hasWorkloads = false;
        }

        if (hasWorkloads)
        {
            return FirstRunState.WorkloadsInstalled;
        }

        return markerExists ? FirstRunState.MarkerWithoutWorkloads : FirstRunState.NeverPrompted;
    }

    public async Task MarkCompleteAsync(CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(_paths.Home);

        // Touch the marker. Content is informational only; presence is the signal.
        await File.WriteAllTextAsync(MarkerPath, DateTimeOffset.UtcNow.ToString("O"), cancellationToken);
    }
}
