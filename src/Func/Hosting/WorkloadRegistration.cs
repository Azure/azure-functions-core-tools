// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Diagnostics;
using Azure.Functions.Cli.Console;
using Azure.Functions.Cli.Telemetry;
using Azure.Functions.Cli.Workloads;
using Azure.Functions.Cli.Workloads.Invocation;
using Azure.Functions.Cli.Workloads.Loading;
using Azure.Functions.Cli.Workloads.Resolution;
using Azure.Functions.Cli.Workloads.Storage;
using Microsoft.Extensions.DependencyInjection;
using NuGet.Versioning;

namespace Azure.Functions.Cli.Hosting;

/// <summary>
/// Bridges installed workloads into the host. Reads the global manifest at
/// <c>~/.azure-functions/workloads.json</c>, loads each live workload's
/// entry-point assembly into its own
/// <see cref="System.Runtime.Loader.AssemblyLoadContext"/>, and invokes
/// <see cref="Workload.Configure"/> so each workload can contribute
/// services and commands.
/// </summary>
/// <remarks>
/// Runs before <see cref="Microsoft.Extensions.Hosting.IHostBuilder.Build"/>
/// so workloads can mutate the <see cref="IServiceCollection"/>. Per-workload
/// failures (load or <see cref="Workload.Configure"/>) are isolated: a single
/// throw becomes a stderr warning and the remaining workloads still load.
/// Only the highest installed semver per <c>packageId</c> goes live; older
/// versions stay in the on-disk registry for rollback but aren't loaded into
/// the process. Content-only and meta entries are skipped.
/// <para>
/// Boot duration is captured by the <c>cli.workload.boot</c> activity opened
/// here; the <see cref="WorkloadBootMetricListener"/> translates that
/// activity's stop into the boot-duration histogram so the metric and the
/// trace stay in sync (workload count, error.type on failure).
/// </para>
/// </remarks>
internal static class WorkloadRegistration
{
    /// <summary>
    /// Loads every live workload, publishes them via
    /// <see cref="IWorkloadProvider"/>, and invokes
    /// <see cref="Workload.Configure"/> on each.
    /// </summary>
    /// <param name="services">The host's service collection. Workload-contributed services are added here.</param>
    /// <param name="paths">Pre-resolved workload paths, including the workload home. Construct with the default ctor to use the env-var-aware resolver, or with an explicit home in tests.</param>
    /// <param name="interaction">Used to surface per-workload load and Configure failures as warnings.</param>
    /// <param name="cancellationToken">Cancellation propagated to manifest reads.</param>
    public static async Task RegisterWorkloadsAsync(
        IServiceCollection services,
        WorkloadPathsOptions paths,
        IInteractionService interaction,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(interaction);

        // The activity name doubles as the metric scope: WorkloadBootMetricListener
        // translates the stop event into cli.workload.boot_duration with the
        // same count tag and, on failure, the same error.type tag.
        using Activity? activity = CliTelemetry.Trace.StartWorkloadBootActivity();
        try
        {
            int loadedCount = await RegisterCoreAsync(services, paths, interaction, cancellationToken);
            activity?.SetTag(TelemetryConventions.CliWorkloadCount, loadedCount);
        }
        catch (Exception ex)
        {
            activity?.Fail(ex);
            throw;
        }
    }

    private static async Task<int> RegisterCoreAsync(
        IServiceCollection services,
        WorkloadPathsOptions paths,
        IInteractionService interaction,
        CancellationToken cancellationToken)
    {
        var store = new WorkloadStore(paths);
        var loader = new WorkloadLoader(paths);

        IReadOnlyList<WorkloadEntry> allEntries = await store.GetWorkloadsAsync(cancellationToken);
        IReadOnlyList<WorkloadEntry> liveEntries = SelectLiveEntries(allEntries);

        var workloads = new List<WorkloadInfo>(liveEntries.Count);
        foreach (WorkloadEntry entry in liveEntries)
        {
            try
            {
                // Load per-entry so one failure becomes a warning instead of
                // aborting discovery for the rest.
                IReadOnlyList<WorkloadInfo> loaded = loader.Load([entry]);
                workloads.Add(loaded[0]);
            }
            catch (Exception ex)
            {
                interaction.WriteWarning(
                    $"Workload '{entry.PackageId}@{entry.PackageVersion}' could not be loaded: {ex.Message}");
            }
        }

        // Register each WorkloadInfo individually so anything can contribute
        // another via AddSingleton. Consumers depend on IWorkloadProvider,
        // not IEnumerable<WorkloadInfo>, to name the domain concept and leave
        // room for lookup behavior later.
        foreach (WorkloadInfo workload in workloads)
        {
            services.AddSingleton(workload);
        }

        services.AddSingleton<IWorkloadProvider, WorkloadProvider>();

        services.AddSingleton<IWorkloadResolver, WorkloadResolver>();
        services.AddSingleton<IWorkloadInvoker, WorkloadInvoker>();

        foreach (WorkloadInfo workload in workloads)
        {
            // Per-workload builder so RegisterCommand can tag the resulting
            // ExternalCommand with its owning workload.
            var builder = new DefaultFunctionsCliBuilder(services, workload);
            try
            {
                workload.Instance.Configure(builder);
            }
            catch (Exception ex)
            {
                // A misbehaving workload must not brick the CLI: the user
                // still needs `func workload uninstall` to remove it.
                // Not transactional: services registered before the throw
                // remain in the collection. Tracked in #4948.
                interaction.WriteWarning(
                    $"Workload '{workload.PackageId}@{workload.PackageVersion}' failed to initialize: {ex.Message}");
            }
        }

        return workloads.Count;
    }

    /// <summary>
    /// Skips non-workload entries (content-only, meta) and keeps only the
    /// highest installed semver per <c>packageId</c>. Older versions stay on
    /// disk for rollback but don't go live in the process.
    /// </summary>
    private static IReadOnlyList<WorkloadEntry> SelectLiveEntries(IReadOnlyList<WorkloadEntry> entries)
    {
        if (entries.Count == 0)
        {
            return [];
        }

        return [.. entries
            .Where(e => e.Kind == WorkloadKind.Workload)
            .GroupBy(e => e.PackageId, StringComparer.OrdinalIgnoreCase)
            .Select(group => group
                .OrderByDescending(e => ParseVersionOrZero(e.PackageVersion))
                .First())];
    }

    /// <summary>
    /// Parses a registry version string, falling back to 0.0.0 on failure so
    /// the "highest semver wins" sort stays deterministic instead of throwing
    /// at boot. The malformed entry surfaces its own loader warning later.
    /// </summary>
    private static NuGetVersion ParseVersionOrZero(string version)
        => NuGetVersion.TryParse(version, out NuGetVersion? parsed) ? parsed : new NuGetVersion(0, 0, 0);
}
