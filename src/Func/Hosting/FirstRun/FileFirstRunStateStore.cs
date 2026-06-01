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
/// <remarks>
/// This runs on every CLI invocation, before the user's command starts, so
/// the hot path must stay cheap. We deliberately use a file-existence probe
/// on the workload registry rather than loading and deserializing it: the
/// registry is only written when at least one workload has been installed,
/// so its presence is a reliable "user has workloads" signal without any
/// JSON parsing. The corner case (user installed workloads then uninstalled
/// every one, leaving an empty registry file) only costs us a missing
/// breadcrumb hint, never a wrong prompt.
/// </remarks>
internal sealed class FileFirstRunStateStore(
    CliConfigurationPathsOptions paths,
    IWorkloadPaths workloadPaths) : IFirstRunStateStore
{
    internal const string MarkerFileName = ".first-run-complete";

    private readonly CliConfigurationPathsOptions _paths = paths ?? throw new ArgumentNullException(nameof(paths));
    private readonly IWorkloadPaths _workloadPaths = workloadPaths ?? throw new ArgumentNullException(nameof(workloadPaths));

    private string MarkerPath => Path.Combine(_paths.Home, MarkerFileName);

    public Task<bool> IsFirstRunAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(GetStateCore() == FirstRunState.NeverPrompted);

    public Task<FirstRunState> GetStateAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(GetStateCore());

    private FirstRunState GetStateCore()
    {
        // Two cheap file-existence probes; no I/O beyond stat(2). The
        // workload registry exists if and only if SaveWorkloadAsync has
        // run at least once, which is the signal we actually care about
        // (an established user, marker or not).
        bool hasWorkloads = File.Exists(_workloadPaths.WorkloadRegistryPath);
        if (hasWorkloads)
        {
            return FirstRunState.WorkloadsInstalled;
        }

        return File.Exists(MarkerPath) ? FirstRunState.MarkerWithoutWorkloads : FirstRunState.NeverPrompted;
    }

    public async Task MarkCompleteAsync(CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(_paths.Home);

        // Touch the marker. Content is informational only; presence is the signal.
        await File.WriteAllTextAsync(MarkerPath, DateTimeOffset.UtcNow.ToString("O"), cancellationToken);
    }
}
