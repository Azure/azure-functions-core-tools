// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.CommandLine;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Console;
using Azure.Functions.Cli.Workloads.Catalog;
using Azure.Functions.Cli.Workloads.Discovery;
using Azure.Functions.Cli.Workloads.Install;
using Azure.Functions.Cli.Workloads.Storage;
using NuGet.Versioning;

namespace Azure.Functions.Cli.Commands.Workload;

/// <summary>
/// <c>func workload update [&lt;id&gt;]</c>. In-place atomic version
/// swap per spec §6.4. Not side-by-side: use <c>install --force</c> for
/// SxS. Pass <c>--all</c> to update every installed workload.
/// </summary>
internal sealed class WorkloadUpdateCommand : FuncCliCommand
{
    private readonly IInteractionService _interaction;
    private readonly IWorkloadInstaller _installer;
    private readonly IWorkloadStore _store;

    public Argument<string?> WorkloadArgument { get; } = new("id")
    {
        Arity = ArgumentArity.ZeroOrOne,
        Description = "ID or alias of the installed workload to update. Mutually exclusive with --all.",
    };

    public Option<string?> VersionOption { get; } = new("--version", "-v")
    {
        Description = "Installed version to replace. Defaults to the highest installed semver.",
    };

    public Option<bool> AllOption { get; } = new("--all")
    {
        Description = "Update every installed workload. Mutually exclusive with <id>.",
    };

    public Option<bool> MajorOption { get; } = new("--major")
    {
        Description = "Allow crossing a major-version boundary. Default: same major only.",
    };

    public Option<string?> SourceOption { get; } = new("--source")
    {
        Description = "Catalog source URL or local directory to resolve from.",
    };

    public Option<bool> IncludePrereleasesOption { get; } = new("--include-prereleases")
    {
        Description = "Allow prerelease versions when resolving the new version.",
    };

    public Option<bool> ExactOption { get; } = new("--exact", "-e")
    {
        Description = "Match the argument as a literal package id; do not look up aliases.",
    };

    public WorkloadUpdateCommand(
        IInteractionService interaction,
        IWorkloadInstaller installer,
        IWorkloadStore store)
        : base("update", "Update an installed workload in place.")
    {
        _interaction = interaction ?? throw new ArgumentNullException(nameof(interaction));
        _installer = installer ?? throw new ArgumentNullException(nameof(installer));
        _store = store ?? throw new ArgumentNullException(nameof(store));

        VersionOption.Validators.Add(result =>
        {
            string? value = result.GetValue(VersionOption);
            if (!string.IsNullOrWhiteSpace(value) && !NuGetVersion.TryParse(value, out _))
            {
                result.AddError($"'{value}' is not a valid semver version.");
            }
        });

        Arguments.Add(WorkloadArgument);
        Options.Add(VersionOption);
        Options.Add(AllOption);
        Options.Add(MajorOption);
        Options.Add(SourceOption);
        Options.Add(IncludePrereleasesOption);
        Options.Add(ExactOption);

        Validators.Add(result =>
        {
            string? id = result.GetValue(WorkloadArgument);
            bool all = result.GetValue(AllOption);

            if (string.IsNullOrWhiteSpace(id) && !all)
            {
                result.AddError("Specify a workload <id> or pass --all.");
                return;
            }

            if (!string.IsNullOrWhiteSpace(id) && all)
            {
                result.AddError("<id> and --all are mutually exclusive.");
                return;
            }

            if (all && !string.IsNullOrWhiteSpace(result.GetValue(VersionOption)))
            {
                result.AddError("--version cannot be combined with --all.");
            }
        });
    }

    protected override async Task<int> ExecuteAsync(ParseResult parseResult, CancellationToken cancellationToken)
    {
        string? identifier = parseResult.GetValue(WorkloadArgument);
        bool all = parseResult.GetValue(AllOption);
        string? versionText = parseResult.GetValue(VersionOption);
        bool allowMajor = parseResult.GetValue(MajorOption);
        string? source = parseResult.GetValue(SourceOption);
        bool includePrereleases = parseResult.GetValue(IncludePrereleasesOption);
        bool exact = parseResult.GetValue(ExactOption);

        if (all)
        {
            return await UpdateAllAsync(source, includePrereleases, allowMajor, cancellationToken);
        }

        string packageId = await ResolveInstalledPackageIdAsync(identifier!, exact, cancellationToken);

        return await UpdateOneAsync(
            packageId,
            string.IsNullOrEmpty(versionText) ? null : NuGetVersion.Parse(versionText),
            source,
            includePrereleases,
            allowMajor,
            cancellationToken);
    }

    private async Task<string> ResolveInstalledPackageIdAsync(
        string identifier,
        bool exact,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<WorkloadEntry> installed = await _store.GetWorkloadsAsync(cancellationToken);

        bool MatchesId(WorkloadEntry e) =>
            string.Equals(e.PackageId, identifier, StringComparison.OrdinalIgnoreCase);

        bool MatchesAlias(WorkloadEntry e) =>
            !exact && (e.Aliases?.Any(a => string.Equals(a, identifier, StringComparison.OrdinalIgnoreCase)) ?? false);

        string[] matchedIds = [.. installed
            .Where(e => MatchesId(e) || MatchesAlias(e))
            .Select(e => e.PackageId)
            .Distinct(StringComparer.OrdinalIgnoreCase)];

        if (matchedIds.Length > 1)
        {
            throw new GracefulException(
                $"Alias '{identifier}' matches multiple installed workloads ({string.Join(", ", matchedIds)}). " +
                "Pass the workload ID instead.",
                isUserError: true);
        }

        // Defer the "not installed" error to UpdateAsync so messaging stays
        // consistent with the --version path that's also resolved there.
        return matchedIds.Length == 1 ? matchedIds[0] : identifier;
    }

    private async Task<int> UpdateOneAsync(
        string packageId,
        NuGetVersion? targetVersion,
        string? source,
        bool includePrereleases,
        bool allowMajor,
        CancellationToken cancellationToken)
    {
        try
        {
            WorkloadUpdateResult result = await _installer.UpdateAsync(
                packageId, targetVersion, source, includePrereleases, allowMajor, cancellationToken);

            RenderSingle(result);
            return 0;
        }
        catch (WorkloadPackageNotFoundException ex)
        {
            throw new GracefulException(ex.Message, isUserError: true);
        }
        catch (FileNotFoundException ex)
        {
            throw new GracefulException(ex.Message, isUserError: true);
        }
        catch (InvalidWorkloadException ex)
        {
            throw new GracefulException(ex.Message, isUserError: true);
        }
        catch (InvalidOperationException ex)
        {
            throw new GracefulException(ex.Message, isUserError: true);
        }
    }

    private async Task<int> UpdateAllAsync(
        string? source,
        bool includePrereleases,
        bool allowMajor,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<WorkloadEntry> installed = await _store.GetWorkloadsAsync(cancellationToken);
        IReadOnlyList<string> packageIds = [.. installed
            .Select(e => e.PackageId)
            .Distinct(StringComparer.OrdinalIgnoreCase)];

        if (packageIds.Count == 0)
        {
            _interaction.WriteHint("No workloads installed.");
            return 0;
        }

        int failed = 0;
        foreach (string packageId in packageIds)
        {
            try
            {
                WorkloadUpdateResult result = await _installer.UpdateAsync(
                    packageId,
                    targetInstalledVersion: null,
                    source,
                    includePrereleases,
                    allowMajor,
                    cancellationToken);
                RenderSingle(result);
            }
            catch (Exception ex) when (
                ex is WorkloadPackageNotFoundException
                or FileNotFoundException
                or InvalidWorkloadException
                or InvalidOperationException)
            {
                // Per-id failure must not block other workloads. Surface
                // the message and keep going; the final exit code reflects
                // whether any failed.
                failed++;
                _interaction.WriteError($"Update failed for '{packageId}': {ex.Message}");
            }
        }

        return failed == 0 ? 0 : 1;
    }

    private void RenderSingle(WorkloadUpdateResult result)
    {
        if (result.NoCandidateOnSource)
        {
            _interaction.WriteHint(
                $"No version of '{result.Entry.PackageId}' was found on the configured source. " +
                "Pass --source to point at the feed that publishes it.");
            return;
        }

        if (result.NoUpdateAvailable)
        {
            _interaction.WriteHint(
                $"Workload '{result.Entry.PackageId}' is already at the latest available version " +
                $"({result.Entry.PackageVersion}).");
            return;
        }

        _interaction.WriteSuccess(
            $"Updated workload '{result.Entry.PackageId}' from {result.PreviousVersion} to {result.Entry.PackageVersion}.");
    }
}
