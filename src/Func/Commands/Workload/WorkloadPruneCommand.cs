// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.CommandLine;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Console;
using Azure.Functions.Cli.Workloads.Install;
using Azure.Functions.Cli.Workloads.Storage;
using NuGet.Versioning;

namespace Azure.Functions.Cli.Commands.Workload;

/// <summary>
/// <c>func workload prune [&lt;id&gt;] [--exact|-e]</c>. Removes inactive
/// side-by-side installs per spec §4.1: for each in-scope <c>packageId</c>,
/// keeps the highest installed version and uninstalls every older
/// version. Local-only; never touches the catalog.
/// </summary>
internal sealed class WorkloadPruneCommand : FuncCliCommand
{
    private readonly IInteractionService _interaction;
    private readonly IWorkloadInstaller _installer;
    private readonly IWorkloadStore _store;

    public Argument<string?> WorkloadArgument { get; } = new("id")
    {
        Arity = ArgumentArity.ZeroOrOne,
        Description = "Workload package id or alias to prune. Default: prune every installed workload.",
    };

    public Option<bool> ExactOption { get; } = new("--exact", "-e")
    {
        Description = "Disable alias matching. <id> must be the literal package id.",
    };

    public WorkloadPruneCommand(IInteractionService interaction, IWorkloadInstaller installer, IWorkloadStore store)
        : base("prune", "Remove inactive side-by-side workload installs.")
    {
        _interaction = interaction ?? throw new ArgumentNullException(nameof(interaction));
        _installer = installer ?? throw new ArgumentNullException(nameof(installer));
        _store = store ?? throw new ArgumentNullException(nameof(store));

        WorkloadArgument.AddOptionalIdValidator();

        Arguments.Add(WorkloadArgument);
        Options.Add(ExactOption);
    }

    protected override async Task<int> ExecuteAsync(ParseResult parseResult, CancellationToken cancellationToken)
    {
        string? identifier = parseResult.GetValue(WorkloadArgument);
        bool exact = parseResult.GetValue(ExactOption);

        IReadOnlyList<WorkloadEntry> installed = await _store.GetWorkloadsAsync(cancellationToken);
        if (installed.Count == 0)
        {
            _interaction.WriteHint("No workloads installed.");
            return 0;
        }

        IReadOnlyList<WorkloadEntry> scoped = ScopeToTarget(installed, identifier, exact);
        if (scoped.Count == 0)
        {
            _interaction.WriteWarning($"Workload '{identifier}' is not installed; nothing to prune.");
            return 0;
        }

        // Group by canonical package id so an alias that maps to one
        // package doesn't accidentally prune another. Within each group
        // keep the highest installable semver and remove the rest.
        IEnumerable<IGrouping<string, WorkloadEntry>> byPackageId = scoped
            .GroupBy(e => e.PackageId, StringComparer.OrdinalIgnoreCase);

        int removedCount = 0;
        foreach (IGrouping<string, WorkloadEntry> group in byPackageId)
        {
            List<WorkloadEntry> ordered = [.. group.OrderByDescending(e => ParseVersion(e.PackageVersion))];
            if (ordered.Count <= 1)
            {
                continue;
            }

            // Skip ordered[0] (the highest version), uninstall the rest.
            for (int i = 1; i < ordered.Count; i++)
            {
                WorkloadEntry candidate = ordered[i];
                bool removed = await _installer.UninstallAsync(
                    candidate.PackageId, candidate.PackageVersion, cancellationToken);
                if (removed)
                {
                    removedCount++;
                    _interaction.WriteSuccess(
                        $"Pruned workload '{candidate.PackageId}' version '{candidate.PackageVersion}'.");
                }
            }
        }

        if (removedCount == 0)
        {
            _interaction.WriteHint("Nothing to prune; only one version installed per workload.");
        }

        return 0;
    }

    private static IReadOnlyList<WorkloadEntry> ScopeToTarget(IReadOnlyList<WorkloadEntry> installed, string? identifier, bool exact)
    {
        if (string.IsNullOrWhiteSpace(identifier))
        {
            return installed;
        }

        return [.. installed.Where(e =>
            string.Equals(e.PackageId, identifier, StringComparison.OrdinalIgnoreCase)
            || (!exact && (e.Aliases?.Any(a => string.Equals(a, identifier, StringComparison.OrdinalIgnoreCase)) ?? false)))];
    }

    private static NuGetVersion ParseVersion(string raw) =>
        NuGetVersion.TryParse(raw, out NuGetVersion? v) ? v : new NuGetVersion(0, 0, 0);
}
