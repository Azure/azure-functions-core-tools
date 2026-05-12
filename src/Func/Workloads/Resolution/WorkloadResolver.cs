// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Workloads.Resolution;

/// <summary>
/// Default <see cref="IWorkloadResolver"/>. Implements the spec §5.2
/// algorithm against the live <see cref="IWorkloadProvider"/> and the set
/// of registered <see cref="WorkloadDetectorContribution"/>s.
/// </summary>
/// <remarks>
/// The resolver is read-only: it reports a verdict, it never mutates state.
/// All "what to do next" decisions (print install hint, prompt for
/// <c>--stack</c>, dispatch to a workload, etc.) live with the calling
/// command.
/// </remarks>
internal sealed class WorkloadResolver(
    IWorkloadProvider workloads,
    IEnumerable<WorkloadDetectorContribution> detectors,
    ILocalSettingsReader localSettings,
    IDirectoryMarkerMatcher markerMatcher) : IWorkloadResolver
{
    private readonly IWorkloadProvider _workloads = workloads ?? throw new ArgumentNullException(nameof(workloads));
    private readonly IReadOnlyList<WorkloadDetectorContribution> _detectors =
        (detectors ?? throw new ArgumentNullException(nameof(detectors))).ToList();
    private readonly ILocalSettingsReader _localSettings = localSettings ?? throw new ArgumentNullException(nameof(localSettings));
    private readonly IDirectoryMarkerMatcher _markerMatcher = markerMatcher ?? throw new ArgumentNullException(nameof(markerMatcher));

    public async Task<WorkloadResolution> ResolveAsync(WorkloadResolutionContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        IReadOnlyList<WorkloadInfo> installed = _workloads.GetWorkloads();

        // 1. Explicit selector wins.
        if (!string.IsNullOrWhiteSpace(context.StackSelector))
        {
            return ResolveBySelector(installed, context.StackSelector);
        }

        // 2. FUNCTIONS_WORKER_RUNTIME from local.settings.json. If set we
        // honour it as an explicit declaration from the user: a runtime hit
        // resolves, a runtime miss errors with a runtime-specific message
        // rather than falling through to detectors.
        string? runtime = _localSettings.ReadWorkerRuntime(context.Directory);
        if (!string.IsNullOrWhiteSpace(runtime))
        {
            return ResolveByRuntime(installed, runtime);
        }

        // `func init` (spec §4.2): no project to detect against, so steps
        // 1+2 are the only signals. Report None with an init-shaped hint.
        if (context.SkipDirectoryDetection)
        {
            return new WorkloadResolution.None(
                "No --stack flag supplied and no FUNCTIONS_WORKER_RUNTIME declared. " +
                $"Pass --stack <id> to choose a workload. " +
                $"Installed: {FormatInstalled(installed)}.");
        }

        // 3. IProjectDetector pass.
        return await ResolveByDetectorsAsync(context.Directory, cancellationToken);
    }

    private static WorkloadResolution ResolveBySelector(IReadOnlyList<WorkloadInfo> installed, string selector)
    {
        // Match against aliases on the workload itself; this mirrors how
        // `func workload install <alias>` resolves user input.
        List<WorkloadInfo> matches = [.. installed.Where(w =>
            w.Aliases.Any(a => string.Equals(a, selector, StringComparison.OrdinalIgnoreCase)))];

        return matches.Count switch
        {
            1 => new WorkloadResolution.Resolved(matches[0], $"Selected by --stack '{selector}'."),
            0 => new WorkloadResolution.None(
                $"No installed workload claims stack '{selector}'. " +
                $"Installed: {FormatInstalled(installed)}. " +
                $"Run 'func workload install <package>' to add a workload."),
            _ => new WorkloadResolution.None(
                $"Multiple installed workloads claim stack '{selector}': {FormatPackages(matches)}. " +
                $"Pass --stack with an exact package id to disambiguate."),
        };
    }

    private WorkloadResolution ResolveByRuntime(IReadOnlyList<WorkloadInfo> installed, string runtime)
    {
        // Group detectors by owning workload to avoid double-counting a
        // workload that ships several detectors all claiming the same runtime.
        HashSet<WorkloadInfo> matches = [.. _detectors
            .Where(d => d.Detector.WorkerRuntimes.Any(r => string.Equals(r, runtime, StringComparison.OrdinalIgnoreCase)))
            .Select(d => d.Workload)];

        return matches.Count switch
        {
            1 => new WorkloadResolution.Resolved(
                matches.First(),
                $"Selected by FUNCTIONS_WORKER_RUNTIME='{runtime}' in local.settings.json."),
            0 => new WorkloadResolution.None(
                $"local.settings.json declares FUNCTIONS_WORKER_RUNTIME='{runtime}' " +
                $"but no installed workload claims that runtime. " +
                $"Installed: {FormatInstalled(installed)}. " +
                $"Run 'func workload install <package>' to add a workload for '{runtime}', " +
                $"or pass --stack <id> to override."),
            _ => new WorkloadResolution.None(
                $"Multiple installed workloads claim worker runtime '{runtime}': {FormatPackages(matches)}. " +
                $"Pass --stack <id> to disambiguate."),
        };
    }

    private async Task<WorkloadResolution> ResolveByDetectorsAsync(DirectoryInfo directory, CancellationToken cancellationToken)
    {
        // Track the claim per workload (not per detector) so a workload that
        // ships multiple detectors counts once. Keep the first reason a
        // detector supplied for that workload.
        var claimsByWorkload = new Dictionary<WorkloadInfo, string?>();
        foreach (WorkloadDetectorContribution contribution in _detectors)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!_markerMatcher.AnyMatch(directory, contribution.Detector.ProjectMarkers))
            {
                continue;
            }

            DetectionResult result = await contribution.Detector.DetectAsync(directory, cancellationToken);
            if (!result.Claimed)
            {
                continue;
            }

            if (!claimsByWorkload.ContainsKey(contribution.Workload))
            {
                claimsByWorkload[contribution.Workload] = result.Reason;
            }
        }

        List<KeyValuePair<WorkloadInfo, string?>> candidates = [.. claimsByWorkload];

        return candidates.Count switch
        {
            1 => new WorkloadResolution.Resolved(
                candidates[0].Key,
                candidates[0].Value is { Length: > 0 } reason
                    ? $"Selected by '{candidates[0].Key.PackageId}' detector: {reason}."
                    : $"Selected by '{candidates[0].Key.PackageId}' detector."),
            0 => new WorkloadResolution.None(
                "No installed workload claims this directory. " +
                "Pass --stack <id> to select one explicitly, or run 'func workload install <package>' to add one."),
            _ => new WorkloadResolution.None(
                $"Multiple installed workloads claim this directory: " +
                $"{string.Join(", ", candidates.Select(c => FormatCandidate(c.Key, c.Value)))}. " +
                $"Pass --stack <id> to disambiguate."),
        };
    }

    private static string FormatCandidate(WorkloadInfo workload, string? reason)
        => reason is { Length: > 0 }
            ? $"{workload.PackageId} ({reason})"
            : workload.PackageId;

    private static string FormatPackages(IEnumerable<WorkloadInfo> workloads)
        => string.Join(", ", workloads.Select(w => w.PackageId));

    private static string FormatInstalled(IReadOnlyList<WorkloadInfo> installed)
        => installed.Count == 0 ? "(none)" : FormatPackages(installed);
}
