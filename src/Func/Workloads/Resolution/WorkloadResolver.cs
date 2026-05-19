// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Configuration;
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
    IOptionsMonitor<StackOptions> stackOptions) : IWorkloadResolver
{
    private readonly IWorkloadProvider _workloads = workloads ?? throw new ArgumentNullException(nameof(workloads));
    private readonly IReadOnlyList<WorkloadProjectResolverContribution> _resolvers =
        (resolvers ?? throw new ArgumentNullException(nameof(resolvers))).ToList();
    private readonly IOptionsMonitor<StackOptions> _stackOptions = stackOptions ?? throw new ArgumentNullException(nameof(stackOptions));

    public async Task<WorkloadResolution> ResolveAsync(WorkloadResolutionContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        IReadOnlyList<WorkloadInfo> installed = _workloads.GetWorkloads();

        // 1. Explicit selector wins.
        if (!string.IsNullOrWhiteSpace(context.StackSelector))
        {
            return ResolveBySelector(installed, context.StackSelector, $"--stack '{context.StackSelector}'");
        }

        // No project to inspect (e.g. `func init`).
        if (context.SkipDirectoryDetection)
        {
            return new WorkloadResolution.None(
                "No --stack flag supplied. " +
                "Pass --stack <id> to choose a workload. " +
                $"Installed: {FormatInstalled(installed)}.");
        }

        // CLI-wide invariant: a directory without host.json is not a
        // Functions project. Gate here so per-stack resolvers can focus
        // on their fingerprints and we surface one consistent diagnostic.
        if (!File.Exists(Path.Combine(context.Directory.FullName, "host.json")))
        {
            return new WorkloadResolution.None(
                "This directory does not look like an Azure Functions project. " +
                $"No 'host.json' was found in '{context.Directory.FullName}'. " +
                "Run 'func init' to scaffold one, or change to a Functions project directory.");
        }

        IReadOnlyList<ResolverClaim> claims = await CollectClaimsAsync(context.Directory, cancellationToken);

        // 2. StackOptions.Runtime, including the projection from
        // FUNCTIONS_WORKER_RUNTIME, is treated as an explicit
        // declaration: only claims with a matching WorkerRuntime count.
        string? runtime = GetStackOptions(context.Directory).Runtime;
        if (!string.IsNullOrWhiteSpace(runtime))
        {
            return ResolveByRuntime(installed, claims, runtime);
        }

        // 3. Auto-detection from registered IProjectResolvers.
        return ResolveByClaims(claims);
    }

    private StackOptions GetStackOptions(DirectoryInfo projectDirectory)
        => ProjectDirectoryResolver.IsProjectDirectory(projectDirectory)
            ? _stackOptions.CurrentValue
            : _stackOptions.Get(Path.GetFullPath(projectDirectory.FullName));

    private async Task<IReadOnlyList<ResolverClaim>> CollectClaimsAsync(DirectoryInfo directory, CancellationToken cancellationToken)
    {
        // Track per workload so a workload that ships multiple resolvers
        // counts once.
        var claimsByWorkload = new Dictionary<WorkloadInfo, ResolverClaim>();
        foreach (WorkloadProjectResolverContribution contribution in _resolvers)
        {
            cancellationToken.ThrowIfCancellationRequested();

            EvaluationResult result = await contribution.Resolver.EvaluateAsync(directory, cancellationToken);
            if (!result.IsMatch)
            {
                continue;
            }

            if (!claimsByWorkload.ContainsKey(contribution.Workload))
            {
                claimsByWorkload[contribution.Workload] = new ResolverClaim(contribution.Workload, result);
            }
        }

        return [.. claimsByWorkload.Values];
    }

    private static WorkloadResolution ResolveBySelector(IReadOnlyList<WorkloadInfo> installed, string selector, string source)
    {
        // Match against aliases; mirrors `func workload install <alias>`.
        List<WorkloadInfo> matches = [.. installed.Where(w =>
            w.Aliases.Any(a => string.Equals(a, selector, StringComparison.OrdinalIgnoreCase)))];

        return matches.Count switch
        {
            1 => new WorkloadResolution.Resolved(matches[0], $"Selected by {source}."),
            0 => new WorkloadResolution.None(
                $"No installed workload claims stack '{selector}' (from {source}). " +
                $"Installed: {FormatInstalled(installed)}. " +
                $"Run 'func workload install <package>' to add a workload."),
            _ => new WorkloadResolution.None(
                $"Multiple installed workloads claim stack '{selector}' (from {source}): {FormatPackages(matches)}. " +
                $"Pass --stack with an exact package id to disambiguate."),
        };
    }

    private static WorkloadResolution ResolveByRuntime(
        IReadOnlyList<WorkloadInfo> installed,
        IReadOnlyList<ResolverClaim> claims,
        string runtime)
    {
        List<ResolverClaim> matches = [.. claims.Where(c =>
            c.Result.WorkerRuntime is { Length: > 0 } r &&
            string.Equals(r, runtime, StringComparison.OrdinalIgnoreCase))];

        return matches.Count switch
        {
            1 => new WorkloadResolution.Resolved(
                matches[0].Workload,
                $"Selected by configured worker runtime '{runtime}'."),
            0 => new WorkloadResolution.None(
                $"Configuration declares worker runtime '{runtime}' " +
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

    private static WorkloadResolution ResolveByClaims(IReadOnlyList<ResolverClaim> claims)
    {
        return claims.Count switch
        {
            1 => new WorkloadResolution.Resolved(
                claims[0].Workload,
                claims[0].Result.Reason is { Length: > 0 } reason
                    ? $"Selected by '{claims[0].Workload.PackageId}' resolver: {reason}."
                    : $"Selected by '{claims[0].Workload.PackageId}' resolver."),
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

    private readonly record struct ResolverClaim(WorkloadInfo Workload, EvaluationResult Result);
}
