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

    public Option<bool> IncludePrereleaseOption { get; } = new("--prerelease")
    {
        Description = "Allow prerelease versions when resolving from the catalog. Default: stable versions only.",
    };

    public Option<bool> ExactOption { get; } = new("--exact", "-e")
    {
        Description = "Disable alias matching. <id> must be the literal package id.",
    };

    public WorkloadUpdateCommand(IInteractionService interaction, IWorkloadInstaller installer, IWorkloadStore store)
        : base("update", "Update an installed workload in place.")
    {
        _interaction = interaction ?? throw new ArgumentNullException(nameof(interaction));
        _installer = installer ?? throw new ArgumentNullException(nameof(installer));
        _store = store ?? throw new ArgumentNullException(nameof(store));

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
        bool includePrerelease = parseResult.GetValue(IncludePrereleaseOption);
        bool exact = parseResult.GetValue(ExactOption);

        if (includePrerelease)
        {
            _interaction.WriteHint(WorkloadInstallCommand.PrereleasePreviewHint);
        }

        if (all)
        {
            return await UpdateAllAsync(source, includePrerelease, allowMajor, cancellationToken);
        }

        string packageId = await ResolveInstalledPackageIdAsync(identifier!, exact, cancellationToken);

        return await UpdateOneAsync(
            packageId,
            string.IsNullOrEmpty(versionText) ? null : NuGetVersion.Parse(versionText),
            source,
            includePrerelease,
            allowMajor,
            cancellationToken);
    }

    private async Task<string> ResolveInstalledPackageIdAsync(string identifier, bool exact, CancellationToken cancellationToken)
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
        bool includePrerelease,
        bool allowMajor,
        CancellationToken cancellationToken)
    {
        try
        {
            WorkloadUpdateResult result = await _interaction.RunWithProgressAsync(
                $"Updating workload '{packageId}'",
                async (ctx, ct) => await _installer.UpdateAsync(
                    packageId, targetVersion, source, includePrerelease, allowMajor,
                    new ProgressAdapter(ctx),
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

    private async Task<int> UpdateAllAsync(string? source, bool includePrerelease, bool allowMajor, CancellationToken cancellationToken)
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

        bool anyFailed = false;
        foreach (string packageId in packageIds)
        {
            try
            {
                WorkloadUpdateResult result = await _interaction.RunWithProgressAsync(
                    $"Updating workload '{packageId}'",
                    async (ctx, ct) => await _installer.UpdateAsync(
                        packageId,
                        targetInstalledVersion: null,
                        source,
                        includePrerelease,
                        allowMajor,
                        new ProgressAdapter(ctx),
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
                _interaction.WriteError($"Update failed for '{packageId}': {ex.Message}");
            }
        }

        return anyFailed ? 1 : 0;
    }

    private void RenderSingle(WorkloadUpdateResult result)
    {
        // Prefer the published alias for user-facing messages so the output
        // matches the token the user typed; fall back to the package id when
        // no alias is published.
        string display = result.Entry.Aliases.Count > 0 ? result.Entry.Aliases[0] : result.Entry.PackageId;

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
                $"({result.Entry.PackageVersion}).");
            return;
        }

        _interaction.WriteSuccess(
            $"Updated workload '{display}' from {result.PreviousVersion} to {result.Entry.PackageVersion}.");
    }

    /// <summary>
    /// Bridges <see cref="IProgress{T}"/> reports from the installer onto
    /// the live progress bar.
    /// </summary>
    private sealed class ProgressAdapter(IProgressContext context) : IProgress<WorkloadInstallProgress>
    {
        private readonly IProgressContext _context = context;

        public void Report(WorkloadInstallProgress value)
        {
            ArgumentNullException.ThrowIfNull(value);
            _context.SetDescription(value.Description);
        }
    }
}
