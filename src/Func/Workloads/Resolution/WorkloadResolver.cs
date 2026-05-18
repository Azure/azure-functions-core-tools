// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Projects;
using Microsoft.Extensions.Options;

namespace Azure.Functions.Cli.Workloads.Resolution;

/// <summary>
/// Default <see cref="IWorkloadResolver"/>. Read-only: it reports a verdict
/// and never mutates state. Callers handle the response (print hints,
/// dispatch, etc.).
/// </summary>
internal sealed class WorkloadResolver(
    IWorkloadProvider workloads,
    IEnumerable<WorkloadProjectResolverContribution> resolvers,
    IOptions<StackOptions> stackOptions) : IWorkloadResolver
{
    private readonly IWorkloadProvider _workloads = workloads ?? throw new ArgumentNullException(nameof(workloads));
    private readonly IReadOnlyList<WorkloadProjectResolverContribution> _resolvers =
        (resolvers ?? throw new ArgumentNullException(nameof(resolvers))).ToList();
    private readonly StackOptions _stackOptions = (stackOptions ?? throw new ArgumentNullException(nameof(stackOptions))).Value;

    public async Task<WorkloadResolution> ResolveAsync(DirectoryInfo directory, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(directory);

        IReadOnlyList<WorkloadInfo> installed = _workloads.GetWorkloads();

        // 1. Honour an explicit stack pin (from local.settings.json,
        // .func/config.json, etc.). Aliases are the sole identifier.
        string? stack = _stackOptions.Stack;
        if (!string.IsNullOrWhiteSpace(stack))
        {
            WorkloadInfo? match = _workloads.FindByStack(stack);
            if (match is not null)
            {
                return new WorkloadResolution.Resolved(match, $"Selected by stack '{stack}'.");
            }

            // No alias match: fall through to project-based detection.
            // The pin may be a runtime name (e.g. 'native') that several
            // installed workloads can claim via their resolvers.
        }

        // 2. Project-based detection: unique claimant wins.
        var claimsByWorkload = new Dictionary<WorkloadInfo, string?>();
        foreach (WorkloadProjectResolverContribution contribution in _resolvers)
        {
            cancellationToken.ThrowIfCancellationRequested();

            EvaluationResult result = await contribution.Resolver.EvaluateAsync(directory, cancellationToken);
            if (result.IsMatch && !claimsByWorkload.ContainsKey(contribution.Workload))
            {
                claimsByWorkload[contribution.Workload] = result.Reason;
            }
        }

        return claimsByWorkload.Count switch
        {
            1 => Resolved(claimsByWorkload),
            0 => new WorkloadResolution.None(
                string.IsNullOrWhiteSpace(stack)
                    ? "No installed workload claims this directory. " +
                      $"Installed: {FormatInstalled(installed)}. " +
                      "Set 'FUNCTIONS_WORKER_RUNTIME' in local.settings.json, set 'stack' in .func/config.json, " +
                      "or run 'func workload install <package>' to add one."
                    : $"Stack '{stack}' matched no installed alias and no resolver claims this directory. " +
                      $"Installed: {FormatInstalled(installed)}."),
            _ => new WorkloadResolution.None(
                "Multiple installed workloads claim this directory: " +
                $"{string.Join(", ", claimsByWorkload.Select(kvp => FormatCandidate(kvp.Key, kvp.Value)))}. " +
                "Set 'stack' in .func/config.json to disambiguate."),
        };
    }

    private static WorkloadResolution Resolved(IReadOnlyDictionary<WorkloadInfo, string?> claims)
    {
        KeyValuePair<WorkloadInfo, string?> only = claims.First();
        return new WorkloadResolution.Resolved(
            only.Key,
            only.Value is { Length: > 0 } reason
                ? $"Selected by '{only.Key.PackageId}' resolver: {reason}."
                : $"Selected by '{only.Key.PackageId}' resolver.");
    }

    private static string FormatCandidate(WorkloadInfo workload, string? reason)
        => reason is { Length: > 0 }
            ? $"{workload.PackageId} ({reason})"
            : workload.PackageId;

    private static string FormatInstalled(IReadOnlyList<WorkloadInfo> installed)
        => installed.Count == 0 ? "(none)" : string.Join(", ", installed.Select(w => w.PackageId));
}
