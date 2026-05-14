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
    ILocalSettingsReader localSettings) : IWorkloadResolver
{
    private readonly IWorkloadProvider _workloads = workloads ?? throw new ArgumentNullException(nameof(workloads));
    private readonly IReadOnlyList<WorkloadDetectorContribution> _detectors =
        (detectors ?? throw new ArgumentNullException(nameof(detectors))).ToList();
    private readonly ILocalSettingsReader _localSettings = localSettings ?? throw new ArgumentNullException(nameof(localSettings));

    public async Task<WorkloadResolution> ResolveAsync(WorkloadResolutionContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        IReadOnlyList<WorkloadInfo> installed = _workloads.GetWorkloads();

        // 1. Explicit selector wins.
        if (!string.IsNullOrWhiteSpace(context.StackSelector))
        {
            return ResolveBySelector(installed, context.StackSelector);
        }

        // `func init` (spec §4.2): no project to detect against, so detectors
        // would all return "no claim". Skip them and report None with an
        // init-shaped hint pointing the user at --stack.
        if (context.SkipDirectoryDetection)
        {
            return new WorkloadResolution.None(
                "No --stack flag supplied. " +
                "Pass --stack <id> to choose a workload. " +
                $"Installed: {FormatInstalled(installed)}.");
        }

        // 2. Run all detectors and collect claims.
        IReadOnlyList<DetectorClaim> claims = await CollectClaimsAsync(context.Directory, cancellationToken);

        // 3. If FUNCTIONS_WORKER_RUNTIME is set in local.settings.json, treat
        // it as an explicit declaration: prefer claims whose WorkerRuntime
        // matches; if none match, surface a runtime-specific message.
        string? runtime = _localSettings.ReadWorkerRuntime(context.Directory);
        if (!string.IsNullOrWhiteSpace(runtime))
        {
            return ResolveByRuntime(installed, claims, runtime);
        }

        // 4. No runtime hint: pick the unique claim, otherwise None.
        return ResolveByClaims(claims);
    }

    private async Task<IReadOnlyList<DetectorClaim>> CollectClaimsAsync(DirectoryInfo directory, CancellationToken cancellationToken)
    {
        // Track the claim per workload (not per detector) so a workload that
        // ships multiple detectors counts once. Keep the first claim a
        // detector supplied for that workload.
        var claimsByWorkload = new Dictionary<WorkloadInfo, DetectorClaim>();
        foreach (WorkloadDetectorContribution contribution in _detectors)
        {
            cancellationToken.ThrowIfCancellationRequested();

            DetectionResult result = await contribution.Detector.DetectAsync(directory, cancellationToken);
            if (!result.Claimed)
            {
                continue;
            }

            if (!claimsByWorkload.ContainsKey(contribution.Workload))
            {
                claimsByWorkload[contribution.Workload] = new DetectorClaim(contribution.Workload, result);
            }
        }

        return [.. claimsByWorkload.Values];
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

    private static WorkloadResolution ResolveByRuntime(
        IReadOnlyList<WorkloadInfo> installed,
        IReadOnlyList<DetectorClaim> claims,
        string runtime)
    {
        List<DetectorClaim> matches = [.. claims.Where(c =>
            c.Result.WorkerRuntime is { Length: > 0 } r &&
            string.Equals(r, runtime, StringComparison.OrdinalIgnoreCase))];

        return matches.Count switch
        {
            1 => new WorkloadResolution.Resolved(
                matches[0].Workload,
                $"Selected by FUNCTIONS_WORKER_RUNTIME='{runtime}' in local.settings.json."),
            0 => new WorkloadResolution.None(
                $"local.settings.json declares FUNCTIONS_WORKER_RUNTIME='{runtime}' " +
                $"but no installed workload claims that runtime for this directory. " +
                $"Installed: {FormatInstalled(installed)}. " +
                $"Run 'func workload install <package>' to add a workload for '{runtime}', " +
                $"or pass --stack <id> to override."),
            _ => new WorkloadResolution.None(
                $"Multiple installed workloads claim worker runtime '{runtime}': " +
                $"{FormatPackages(matches.Select(m => m.Workload))}. " +
                $"Pass --stack <id> to disambiguate."),
        };
    }

    private static WorkloadResolution ResolveByClaims(IReadOnlyList<DetectorClaim> claims)
    {
        return claims.Count switch
        {
            1 => new WorkloadResolution.Resolved(
                claims[0].Workload,
                claims[0].Result.Reason is { Length: > 0 } reason
                    ? $"Selected by '{claims[0].Workload.PackageId}' detector: {reason}."
                    : $"Selected by '{claims[0].Workload.PackageId}' detector."),
            0 => new WorkloadResolution.None(
                "No installed workload claims this directory. " +
                "Pass --stack <id> to select one explicitly, or run 'func workload install <package>' to add one."),
            _ => new WorkloadResolution.None(
                $"Multiple installed workloads claim this directory: " +
                $"{string.Join(", ", claims.Select(c => FormatCandidate(c.Workload, c.Result.Reason)))}. " +
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

    private readonly record struct DetectorClaim(WorkloadInfo Workload, DetectionResult Result);
}
