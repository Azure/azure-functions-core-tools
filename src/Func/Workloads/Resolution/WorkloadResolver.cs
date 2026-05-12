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

        // 2. FUNCTIONS_WORKER_RUNTIME from local.settings.json.
        string? runtime = _localSettings.ReadWorkerRuntime(context.Directory);
        if (!string.IsNullOrWhiteSpace(runtime))
        {
            WorkloadResolution? byRuntime = TryResolveByRuntime(installed, runtime);
            if (byRuntime is not null)
            {
                return byRuntime;
            }

            // Runtime declared but no detector claims it: fall through to
            // detectors. The per-detector reasons are more useful than a
            // generic "no detector claimed FUNCTIONS_WORKER_RUNTIME".
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
            1 => WorkloadResolution.AsResolved(matches[0], $"Selected by --stack '{selector}'."),
            0 => WorkloadResolution.AsNone(
                $"No installed workload claims stack '{selector}'. " +
                $"Installed: {FormatInstalled(installed)}. " +
                $"Run 'func workload install <package>' to add a workload."),
            _ => WorkloadResolution.AsAmbiguous(
                matches,
                $"Multiple installed workloads claim stack '{selector}': {FormatPackages(matches)}. " +
                $"Pass an exact package id to disambiguate."),
        };
    }

    private WorkloadResolution? TryResolveByRuntime(IReadOnlyList<WorkloadInfo> installed, string runtime)
    {
        // Group detectors by owning workload to avoid double-counting a
        // workload that ships several detectors all claiming the same runtime.
        HashSet<WorkloadInfo> matches = [.. _detectors
            .Where(d => d.Detector.WorkerRuntimes.Any(r => string.Equals(r, runtime, StringComparison.OrdinalIgnoreCase)))
            .Select(d => d.Workload)];

        return matches.Count switch
        {
            1 => WorkloadResolution.AsResolved(
                matches.First(),
                $"Selected by FUNCTIONS_WORKER_RUNTIME='{runtime}' in local.settings.json."),
            0 => null,
            _ => WorkloadResolution.AsAmbiguous(
                [.. matches],
                $"Multiple installed workloads claim worker runtime '{runtime}': {FormatPackages(matches)}. " +
                $"Pass --stack <id> to disambiguate."),
        };
    }

    private async Task<WorkloadResolution> ResolveByDetectorsAsync(DirectoryInfo directory, CancellationToken cancellationToken)
    {
        // Track the outcome per workload (not per detector) so a workload
        // that ships multiple detectors counts once.
        var verdictsByWorkload = new Dictionary<WorkloadInfo, (DetectionConfidence Confidence, string? Reason)>();
        foreach (WorkloadDetectorContribution contribution in _detectors)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!_markerMatcher.AnyMatch(directory, contribution.Detector.ProjectMarkers))
            {
                continue;
            }

            DetectionResult result = await contribution.Detector.DetectAsync(directory, cancellationToken);
            if (result.Confidence == DetectionConfidence.No)
            {
                continue;
            }

            // Promote the highest confidence seen for the workload (Yes beats Maybe).
            if (!verdictsByWorkload.TryGetValue(contribution.Workload, out (DetectionConfidence Confidence, string? Reason) existing)
                || result.Confidence > existing.Confidence)
            {
                verdictsByWorkload[contribution.Workload] = (result.Confidence, result.Reason);
            }
        }

        List<KeyValuePair<WorkloadInfo, (DetectionConfidence Confidence, string? Reason)>> yeses =
            [.. verdictsByWorkload.Where(kvp => kvp.Value.Confidence == DetectionConfidence.Yes)];

        return yeses.Count switch
        {
            1 => WorkloadResolution.AsResolved(
                yeses[0].Key,
                yeses[0].Value.Reason is { Length: > 0 } reason
                    ? $"Selected by '{yeses[0].Key.PackageId}' detector: {reason}."
                    : $"Selected by '{yeses[0].Key.PackageId}' detector."),
            0 => WorkloadResolution.AsNone(
                "No installed workload claims this directory. " +
                "Pass --stack <id> to select one explicitly, or run 'func workload install <package>' to add one."),
            _ => WorkloadResolution.AsAmbiguous(
                [.. yeses.Select(kvp => kvp.Key)],
                $"Multiple installed workloads claim this directory: " +
                $"{string.Join(", ", yeses.Select(kvp => FormatCandidate(kvp.Key, kvp.Value.Reason)))}. " +
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
