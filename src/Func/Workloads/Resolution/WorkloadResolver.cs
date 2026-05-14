// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Workloads.Resolution;

/// <summary>
/// Default <see cref="IWorkloadResolver"/>. Read-only: it reports a verdict
/// and never mutates state. Callers handle the response (print hints,
/// dispatch, etc.).
/// </summary>
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

        // No project to inspect (e.g. `func init`).
        if (context.SkipDirectoryDetection)
        {
            return new WorkloadResolution.None(
                "No --stack flag supplied. " +
                "Pass --stack <id> to choose a workload. " +
                $"Installed: {FormatInstalled(installed)}.");
        }

        IReadOnlyList<DetectorClaim> claims = await CollectClaimsAsync(context.Directory, cancellationToken);

        // FUNCTIONS_WORKER_RUNTIME, when set, is treated as an explicit
        // declaration: only claims with a matching WorkerRuntime count.
        string? runtime = _localSettings.ReadWorkerRuntime(context.Directory);
        if (!string.IsNullOrWhiteSpace(runtime))
        {
            return ResolveByRuntime(installed, claims, runtime);
        }

        return ResolveByClaims(claims);
    }

    private async Task<IReadOnlyList<DetectorClaim>> CollectClaimsAsync(DirectoryInfo directory, CancellationToken cancellationToken)
    {
        // Track per workload so a workload that ships multiple detectors
        // counts once.
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
        // Match against aliases; mirrors `func workload install <alias>`.
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
