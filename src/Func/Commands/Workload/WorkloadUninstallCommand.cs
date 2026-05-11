// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.CommandLine;
using System.CommandLine.Parsing;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Console;
using Azure.Functions.Cli.Workloads.Install;
using Azure.Functions.Cli.Workloads.Storage;

namespace Azure.Functions.Cli.Commands.Workload;

/// <summary>
/// <c>func workload uninstall &lt;packageId&gt; [--version &lt;v&gt;] [--all-versions] [--exact]</c>.
/// Removes one or all installed versions of a workload.
/// </summary>
internal sealed class WorkloadUninstallCommand : FuncCliCommand
{
    private readonly IInteractionService _interaction;
    private readonly IWorkloadInstaller _installer;
    private readonly IWorkloadStore _store;

    public Argument<string> WorkloadArgument { get; } = new("id")
    {
        Description = "ID or alias of the workload to uninstall.",
    };

    public Option<string?> VersionOption { get; } = new("--version", "-v")
    {
        Description = "Specific version to uninstall. Omit when only one version is installed.",
    };

    public Option<bool> AllVersionsOption { get; } = new("--all-versions", "-a")
    {
        Description = "Uninstall every installed version of the workload.",
    };

    public Option<bool> ExactOption { get; } = new("--exact", "-e")
    {
        Description = "Match the argument as a literal package id; do not look up aliases.",
    };

    public WorkloadUninstallCommand(
        IInteractionService interaction,
        IWorkloadInstaller installer,
        IWorkloadStore store)
        : base("uninstall", "Uninstall a workload.")
    {
        _interaction = interaction ?? throw new ArgumentNullException(nameof(interaction));
        _installer = installer ?? throw new ArgumentNullException(nameof(installer));
        _store = store ?? throw new ArgumentNullException(nameof(store));

        WorkloadArgument.Validators.Add(result =>
        {
            string? value = result.GetValue(WorkloadArgument);
            if (string.IsNullOrWhiteSpace(value))
            {
                result.AddError("A workload id is required.");
            }
        });

        Arguments.Add(WorkloadArgument);
        Options.Add(VersionOption);
        Options.Add(AllVersionsOption);
        Options.Add(ExactOption);

        Validators.Add(result =>
        {
            int specified = result.Children
                .OfType<OptionResult>()
                .Count(or => or.Option == VersionOption || or.Option == AllVersionsOption);

            if (specified > 1)
            {
                result.AddError("--all-versions and --version cannot be combined.");
            }
        });
    }

    protected override async Task<int> ExecuteAsync(ParseResult parseResult, CancellationToken cancellationToken)
    {
        string identifier = parseResult.GetValue(WorkloadArgument)!;
        string? version = parseResult.GetValue(VersionOption);
        bool all = parseResult.GetValue(AllVersionsOption);
        bool exact = parseResult.GetValue(ExactOption);

        IReadOnlyList<WorkloadEntry> installed = await _store.GetWorkloadsAsync(cancellationToken);
        List<WorkloadEntry> matches = [.. installed
            .Where(w => string.Equals(w.PackageId, identifier, StringComparison.OrdinalIgnoreCase)
                || (!exact && (w.Aliases?.Any(a => string.Equals(a, identifier, StringComparison.OrdinalIgnoreCase)) ?? false)))];

        if (matches.Count == 0)
        {
            _interaction.WriteWarning(
                $"Workload '{identifier}' is not installed; nothing to do.");
            return 0;
        }

        string[] distinctPackageIds = [.. matches
            .Select(m => m.PackageId)
            .Distinct(StringComparer.OrdinalIgnoreCase)];

        if (distinctPackageIds.Length > 1)
        {
            throw new GracefulException(
                $"Alias '{identifier}' matches multiple installed workloads ({string.Join(", ", distinctPackageIds)}). " +
                "Pass the workload ID instead.",
                isUserError: true);
        }

        string packageId = distinctPackageIds[0];
        IReadOnlyList<WorkloadEntry> toRemove = ResolveVersionsToRemove(packageId, version, all, matches);

        foreach (WorkloadEntry candidate in toRemove)
        {
            bool removed = await _installer.UninstallAsync(candidate.PackageId, candidate.PackageVersion, cancellationToken);
            if (removed)
            {
                _interaction.WriteSuccess(
                    $"Uninstalled workload '{candidate.PackageId}' version '{candidate.PackageVersion}'.");
            }
        }

        return 0;
    }

    private static IReadOnlyList<WorkloadEntry> ResolveVersionsToRemove(
        string packageId,
        string? version,
        bool all,
        IReadOnlyList<WorkloadEntry> matches)
    {
        if (all)
        {
            return matches;
        }

        if (!string.IsNullOrEmpty(version))
        {
            WorkloadEntry? match = matches.FirstOrDefault(
                m => string.Equals(m.PackageVersion, version, StringComparison.Ordinal));

            if (match is null)
            {
                string available = string.Join(", ", matches.Select(m => m.PackageVersion));
                throw new GracefulException(
                    $"Workload '{packageId}' version '{version}' is not installed. " +
                    $"Installed versions: {available}.",
                    isUserError: true);
            }

            return [match];
        }

        if (matches.Count > 1)
        {
            string available = string.Join(", ", matches.Select(m => m.PackageVersion));
            throw new GracefulException(
                $"Multiple versions of '{packageId}' are installed ({available}). " +
                "Pass --version <v> or --all-versions.",
                isUserError: true);
        }

        return matches;
    }
}
