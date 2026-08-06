// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.CommandLine;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Console;
using Azure.Functions.Cli.Workloads.Catalog;
using Azure.Functions.Cli.Workloads.Discovery;
using Azure.Functions.Cli.Workloads.Install;
using Azure.Functions.Cli.Workloads.Storage;
using Microsoft.Extensions.Options;
using NuGet.Versioning;

namespace Azure.Functions.Cli.Commands.Workload;

/// <summary>
/// <c>func workload update [&lt;id&gt;]</c>. Moves the selected explicit or
/// logical ownership to the resolved package version. Pass <c>--all</c> to
/// update every installed workload identity.
/// </summary>
internal sealed class WorkloadUpdateCommand : FuncCliCommand
{
    private readonly IInteractionService _interaction;
    private readonly IWorkloadInstaller _installer;
    private readonly IWorkloadStore _store;
    private readonly WorkloadCatalogOptions _catalogOptions;

    public Argument<string?> WorkloadArgument { get; } = new("id")
    {
        Arity = ArgumentArity.ZeroOrOne,
        Description = "Workload package id or alias to update. Mutually exclusive with --all.",
    };

    public Option<string?> VersionOption { get; } = new("--version", "-v")
    {
        Description = "Installed version to replace. Default: the highest installed semver.",
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
        Description = "Catalog source URL or local directory to resolve from. Default: the configured catalog.",
    };

    public Option<bool?> IncludePrereleaseOption { get; } = new("--prerelease")
    {
        Description = "Allow prerelease versions when resolving from the catalog. Default: stable when running a stable CLI build, prerelease when running a prerelease CLI build.",
    };

    public Option<bool> ExactOption { get; } = new("--exact", "-e")
    {
        Description = "Disable alias matching. <id> must be the literal package id.",
    };

    public WorkloadUpdateCommand(IInteractionService interaction, IWorkloadInstaller installer, IWorkloadStore store, IOptions<WorkloadCatalogOptions> catalogOptions)
        : base("update", "Update an installed workload in place.")
    {
        _interaction = interaction ?? throw new ArgumentNullException(nameof(interaction));
        _installer = installer ?? throw new ArgumentNullException(nameof(installer));
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _catalogOptions = catalogOptions?.Value ?? throw new ArgumentNullException(nameof(catalogOptions));

        WorkloadArgument.AddOptionalIdValidator();
        VersionOption.AddSemVerValidator();

        Arguments.Add(WorkloadArgument);
        Options.Add(VersionOption);
        Options.Add(AllOption);
        Options.Add(MajorOption);
        Options.Add(SourceOption);
        Options.Add(IncludePrereleaseOption);
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

            if (all && result.GetResult(VersionOption) is not null)
            {
                result.AddError("--version cannot be combined with --all.");
            }

            if (all && result.GetResult(ExactOption) is not null && result.GetValue(ExactOption))
            {
                // --exact only narrows alias resolution for a single id, so
                // it's meaningless without one. Reject explicitly so the
                // user doesn't think it filtered the --all set somehow.
                result.AddError("--exact cannot be combined with --all.");
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
        bool? includePrerelease = parseResult.GetValue(IncludePrereleaseOption);
        bool exact = parseResult.GetValue(ExactOption);

        if (EffectivePrerelease(includePrerelease))
        {
            _interaction.WriteHint(WorkloadInstallCommand.PrereleasePreviewHint);
        }

        if (all)
        {
            return await UpdateAllAsync(source, includePrerelease, allowMajor, cancellationToken);
        }

        UpdateTarget target = await ResolveInstalledTargetAsync(identifier!, exact, cancellationToken);

        return await UpdateOneAsync(
            target,
            string.IsNullOrEmpty(versionText) ? null : NuGetVersion.Parse(versionText),
            source,
            includePrerelease,
            allowMajor,
            cancellationToken);
    }

    private async Task<UpdateTarget> ResolveInstalledTargetAsync(string identifier, bool exact, CancellationToken cancellationToken)
    {
        IReadOnlyList<WorkloadEntry> installed = await _store.GetWorkloadsAsync(cancellationToken);

        string[] logicalIds = [.. installed
            .Where(e => e.LogicalPackage is not null
                && (string.Equals(e.LogicalPackage.PackageId, identifier, StringComparison.OrdinalIgnoreCase)
                    || (!exact && e.LogicalPackage.Aliases.Any(a =>
                        string.Equals(a, identifier, StringComparison.OrdinalIgnoreCase)))))
            .Select(e => e.LogicalPackage!.PackageId)
            .Distinct(StringComparer.OrdinalIgnoreCase)];
        if (logicalIds.Length > 1)
        {
            throw CreateAmbiguousInstalledAlias(identifier, logicalIds);
        }

        if (logicalIds.Length == 1)
        {
            return new UpdateTarget(logicalIds[0], WorkloadOwnershipKind.Logical);
        }

        string[] matchedIds = [.. installed
            .Where(e => e.IsExplicitlyInstalled
                && (string.Equals(e.PackageId, identifier, StringComparison.OrdinalIgnoreCase)
                    || (!exact && e.Aliases.Any(a =>
                        string.Equals(a, identifier, StringComparison.OrdinalIgnoreCase)))))
            .Select(e => e.PackageId)
            .Distinct(StringComparer.OrdinalIgnoreCase)];

        if (matchedIds.Length > 1)
        {
            throw CreateAmbiguousInstalledAlias(identifier, matchedIds);
        }

        return new UpdateTarget(
            matchedIds.Length == 1 ? matchedIds[0] : identifier,
            WorkloadOwnershipKind.Explicit);
    }

    private async Task<int> UpdateOneAsync(
        UpdateTarget target,
        NuGetVersion? targetVersion,
        string? source,
        bool? includePrerelease,
        bool allowMajor,
        CancellationToken cancellationToken)
    {
        try
        {
            WorkloadUpdateResult             result = await _interaction.RunWithProgressAsync(
                $"Updating workload '{target.PackageId}'",
                async (ctx, ct) => await UpdateTargetAsync(
                    target,
                    targetVersion,
                    source,
                    includePrerelease,
                    allowMajor,
                    new WorkloadInstallProgressAdapter(ctx),
                    ct),
                cancellationToken);

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

    private async Task<int> UpdateAllAsync(string? source, bool? includePrerelease, bool allowMajor, CancellationToken cancellationToken)
    {
        IReadOnlyList<WorkloadEntry> installed = await _store.GetWorkloadsAsync(cancellationToken);
        IReadOnlyList<UpdateTarget> targets = BuildUpdateAllTargets(installed);

        if (targets.Count == 0)
        {
            _interaction.WriteHint("No workloads installed.");
            return 0;
        }

        bool anyFailed = false;
        foreach (UpdateTarget target in targets)
        {
            try
            {
                WorkloadUpdateResult result = await _interaction.RunWithProgressAsync(
                    $"Updating workload '{target.PackageId}'",
                    async (ctx, ct) => await UpdateTargetAsync(
                        target,
                        targetInstalledVersion: null,
                        source,
                        includePrerelease,
                        allowMajor,
                        new WorkloadInstallProgressAdapter(ctx),
                        ct),
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
                anyFailed = true;
                _interaction.WriteError($"Update failed for '{target.PackageId}': {ex.Message}");
            }
        }

        return anyFailed ? 1 : 0;
    }

    private void RenderSingle(WorkloadUpdateResult result)
    {
        // Prefer the published alias for user-facing messages so the output
        // matches the token the user typed; fall back to the package id when
        // no alias is published.
        LogicalPackage? logical = result.Entry.LogicalPackage;
        IReadOnlyList<string> aliases = logical?.Aliases ?? result.Entry.Aliases;
        string display = aliases.Count > 0
            ? aliases[0]
            : logical?.PackageId ?? result.Entry.PackageId;
        string version = logical?.PackageVersion ?? result.Entry.PackageVersion;

        if (result.NoCandidateOnSource)
        {
            _interaction.WriteHint(
                $"No version of '{display}' was found on the configured source. " +
                "Pass --source to point at the feed that publishes it.");
            return;
        }

        if (result.NoUpdateAvailable)
        {
            _interaction.WriteWarning(
                $"Workload '{display}' is already at the latest available version " +
                $"({version}).");
            return;
        }

        // A RID pivot can land on the same version, where "from X to X" would read as a no-op.
        if (string.Equals(result.PreviousVersion, version, StringComparison.Ordinal)
            && !string.IsNullOrEmpty(result.Entry.RuntimeIdentifier))
        {
            _interaction.WriteSuccess(
                $"Updated workload '{display}' to runtime identifier '{result.Entry.RuntimeIdentifier}' (version {version} unchanged).");
            return;
        }

        _interaction.WriteSuccess(
            $"Updated workload '{display}' from {result.PreviousVersion} to {version}.");
    }

    private bool EffectivePrerelease(bool? userOverride) => userOverride ?? _catalogOptions.IncludePrerelease;

    private Task<WorkloadUpdateResult> UpdateTargetAsync(
        UpdateTarget target,
        NuGetVersion? targetInstalledVersion,
        string? source,
        bool? includePrerelease,
        bool allowMajor,
        IProgress<WorkloadInstallProgress>? progress,
        CancellationToken cancellationToken)
        => target.Ownership == WorkloadOwnershipKind.Logical
            ? _installer.UpdateAsync(
                target.PackageId,
                targetInstalledVersion,
                source,
                includePrerelease,
                allowMajor,
                WorkloadOwnershipKind.Logical,
                progress,
                cancellationToken)
            : _installer.UpdateAsync(
                target.PackageId,
                targetInstalledVersion,
                source,
                includePrerelease,
                allowMajor,
                progress,
                cancellationToken);

    private static GracefulException CreateAmbiguousInstalledAlias(string identifier, IReadOnlyList<string> packageIds)
        => new(
            $"Alias '{identifier}' matches multiple installed workloads ({string.Join(", ", packageIds)}). " +
            "Pass the workload ID instead.",
            isUserError: true);

    private static IReadOnlyList<UpdateTarget> BuildUpdateAllTargets(IReadOnlyList<WorkloadEntry> installed)
    {
        List<UpdateTarget> targets = [];
        HashSet<string> logicalIds = new(StringComparer.OrdinalIgnoreCase);
        foreach (WorkloadEntry entry in installed.Where(e => e.LogicalPackage is not null))
        {
            if (logicalIds.Add(entry.LogicalPackage!.PackageId))
            {
                targets.Add(new UpdateTarget(entry.LogicalPackage.PackageId, WorkloadOwnershipKind.Logical));
            }
        }

        // A physical package owned by a pointer is updated through that pointer. Adding it again as an
        // explicit target would re-resolve and replace the payload the logical update just staged.
        HashSet<string> logicallyOwned = new(
            installed.Where(e => e.LogicalPackage is not null).Select(e => e.PackageId),
            StringComparer.OrdinalIgnoreCase);
        HashSet<string> explicitIds = new(StringComparer.OrdinalIgnoreCase);
        foreach (WorkloadEntry entry in installed.Where(e => e.IsExplicitlyInstalled && !logicallyOwned.Contains(e.PackageId)))
        {
            if (explicitIds.Add(entry.PackageId))
            {
                targets.Add(new UpdateTarget(entry.PackageId, WorkloadOwnershipKind.Explicit));
            }
        }

        return targets;
    }

    private sealed record UpdateTarget(string PackageId, WorkloadOwnershipKind Ownership);
}
