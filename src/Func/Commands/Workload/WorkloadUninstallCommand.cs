// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.CommandLine;
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
        Description = "Workload package id or alias to uninstall.",
    };

    public Option<string?> VersionOption { get; } = new("--version", "-v")
    {
        Description = "Specific version to uninstall. Default: the only installed version.",
    };

    public Option<bool> AllVersionsOption { get; } = new("--all-versions", "-a")
    {
        Description = "Uninstall every installed version of the workload.",
    };

    public Option<bool> ExactOption { get; } = new("--exact", "-e")
    {
        Description = "Disable alias matching. <id> must be the literal package id.",
    };

    public WorkloadUninstallCommand(IInteractionService interaction, IWorkloadInstaller installer, IWorkloadStore store)
        : base("uninstall", "Uninstall a workload.")
    {
        _interaction = interaction ?? throw new ArgumentNullException(nameof(interaction));
        _installer = installer ?? throw new ArgumentNullException(nameof(installer));
        _store = store ?? throw new ArgumentNullException(nameof(store));

        WorkloadArgument.AddRequiredIdValidator();

        Arguments.Add(WorkloadArgument);
        Options.Add(VersionOption);
        Options.Add(AllVersionsOption);
        Options.Add(ExactOption);

        Validators.Add(result =>
        {
            // --version names a single version; --all-versions wipes them
            // all. They contradict each other, so reject the combination
            // at parse time rather than picking a precedence at runtime.
            bool versionSpecified = result.GetResult(VersionOption) is not null;
            bool allSpecified = result.GetResult(AllVersionsOption) is not null;

            if (versionSpecified && allSpecified)
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
        IReadOnlyList<UninstallCandidate> matches = ResolveCandidates(identifier, installed, exact);

        if (matches.Count == 0)
        {
            _interaction.WriteWarning($"Workload '{identifier}' is not installed; nothing to do.");
            return 0;
        }

        string[] distinctPackageIds = [.. matches
            .Select(m => m.OwnerPackageId)
            .Distinct(StringComparer.OrdinalIgnoreCase)];

        if (distinctPackageIds.Length > 1)
        {
            throw new GracefulException(
                $"Alias '{identifier}' matches multiple installed workloads ({string.Join(", ", distinctPackageIds)}). " +
                "Pass the workload ID instead.",
                isUserError: true);
        }

        string packageId = distinctPackageIds[0];
        IReadOnlyList<UninstallCandidate> toRemove = ResolveVersionsToRemove(packageId, version, all, matches);

        foreach (UninstallCandidate candidate in toRemove)
        {
            bool removed = candidate.Ownership == WorkloadOwnershipKind.Logical
                ? await _installer.UninstallAsync(
                    candidate.OwnerPackageId,
                    candidate.OwnerPackageVersion,
                    WorkloadOwnershipKind.Logical,
                    cancellationToken: cancellationToken)
                : await _installer.UninstallAsync(
                    candidate.OwnerPackageId,
                    candidate.OwnerPackageVersion,
                    cancellationToken: cancellationToken);
            if (removed)
            {
                _interaction.WriteSuccess(
                    $"Uninstalled workload '{candidate.OwnerPackageId}' version '{candidate.OwnerPackageVersion}'.");
            }
        }

        return 0;
    }

    private static IReadOnlyList<UninstallCandidate> ResolveCandidates(
        string identifier,
        IReadOnlyList<WorkloadEntry> installed,
        bool exact)
    {
        List<UninstallCandidate> logicalMatches = [.. installed
            .Where(w => w.LogicalPackage is not null
                && (string.Equals(w.LogicalPackage.PackageId, identifier, StringComparison.OrdinalIgnoreCase)
                    || (!exact && w.LogicalPackage.Aliases.Any(a =>
                        string.Equals(a, identifier, StringComparison.OrdinalIgnoreCase)))))
            .Select(w => new UninstallCandidate(
                w.LogicalPackage!.PackageId,
                w.LogicalPackage.PackageVersion,
                WorkloadOwnershipKind.Logical))];
        if (logicalMatches.Count > 0)
        {
            return logicalMatches;
        }

        return [.. installed
            .Where(w => !w.IsImplicitlyInstalled
                && (string.Equals(w.PackageId, identifier, StringComparison.OrdinalIgnoreCase)
                    || (!exact && w.Aliases.Any(a =>
                        string.Equals(a, identifier, StringComparison.OrdinalIgnoreCase)))))
            .Select(w => new UninstallCandidate(
                w.PackageId,
                w.PackageVersion,
                WorkloadOwnershipKind.Explicit))];
    }

    private static IReadOnlyList<UninstallCandidate> ResolveVersionsToRemove(
        string packageId,
        string? version,
        bool all,
        IReadOnlyList<UninstallCandidate> matches)
    {
        if (all)
        {
            return matches;
        }

        if (!string.IsNullOrEmpty(version))
        {
            UninstallCandidate? match = matches.FirstOrDefault(
                m => string.Equals(m.OwnerPackageVersion, version, StringComparison.Ordinal));

            if (match is null)
            {
                string available = string.Join(", ", matches.Select(m => m.OwnerPackageVersion));
                throw new GracefulException(
                    $"Workload '{packageId}' version '{version}' is not installed. " +
                    $"Installed versions: {available}.",
                    isUserError: true);
            }

            return [match];
        }

        if (matches.Count > 1)
        {
            string available = string.Join(", ", matches.Select(m => m.OwnerPackageVersion));
            throw new GracefulException(
                $"Multiple versions of '{packageId}' are installed ({available}). " +
                "Pass --version <v> or --all-versions.",
                isUserError: true);
        }

        return matches;
    }

    private sealed record UninstallCandidate(
        string OwnerPackageId,
        string OwnerPackageVersion,
        WorkloadOwnershipKind Ownership);
}
