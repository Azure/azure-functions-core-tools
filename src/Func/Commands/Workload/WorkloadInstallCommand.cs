// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.CommandLine;
using Azure.Functions.Cli.Commands.Setup;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Console;
using Azure.Functions.Cli.Workloads;
using Azure.Functions.Cli.Workloads.Catalog;
using Azure.Functions.Cli.Workloads.Discovery;
using Azure.Functions.Cli.Workloads.Install;
using Azure.Functions.Cli.Workloads.Install.Trust;
using Azure.Functions.Cli.Workloads.Storage;
using NuGet.Versioning;

namespace Azure.Functions.Cli.Commands.Workload;

/// <summary>
/// <c>func workload install &lt;package&gt;</c>. Resolves a workload package
/// id (or alias) through the configured catalog and installs it. Use
/// <c>--source</c> to point at a local folder or alternate feed.
/// </summary>
internal sealed class WorkloadInstallCommand : FuncCliCommand
{
    internal const string PrereleasePreviewHint = "Including prerelease workload versions (workloads are in preview). Pass --prerelease false to disable.";

    private readonly IInteractionService _interaction;
    private readonly IWorkloadInstaller _installer;
    private readonly IWorkloadStore _store;
    private readonly WorkloadUpdateCommand _updateCommand;

    public Argument<string> WorkloadArgument { get; } = new("id")
    {
        Description = "Workload package id, alias, or path to a local .nupkg.",
    };

    public Option<string?> VersionOption { get; } = new("--version", "-v")
    {
        Description = "Specific semver version to install. Default: the latest stable version in the catalog.",
    };

    public Option<string?> SourceOption { get; } = new("--source")
    {
        Description = "Catalog source URL or local directory to resolve from. Default: the configured catalog.",
    };

    public Option<bool> IncludePrereleaseOption { get; } = new("--prerelease")
    {
        Description = "Allow prerelease versions when resolving from the catalog. Default: enabled while workloads are in preview.",
        DefaultValueFactory = _ => true,
    };

    public Option<bool> ForceOption { get; } = new("--force", "-f")
    {
        Description = "Overwrite an existing install of the same id and version.",
    };

    public Option<bool> ExactOption { get; } = new("--exact", "-e")
    {
        Description = "Disable alias matching. <id> must be the literal package id.",
    };

    public Option<bool> AllowUntrustedOption { get; } = new("--allow-untrusted")
    {
        Description = "Bypass the publisher trust check. Required for installing unsigned local development packs.",
    };

    public WorkloadInstallCommand(
        IInteractionService interaction,
        IWorkloadInstaller installer,
        IWorkloadStore store,
        WorkloadUpdateCommand updateCommand)
        : base("install", "Install a workload.")
    {
        _interaction = interaction ?? throw new ArgumentNullException(nameof(interaction));
        _installer = installer ?? throw new ArgumentNullException(nameof(installer));
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _updateCommand = updateCommand ?? throw new ArgumentNullException(nameof(updateCommand));

        WorkloadArgument.AddRequiredIdValidator();
        VersionOption.AddSemVerValidator();

        Arguments.Add(WorkloadArgument);
        Options.Add(VersionOption);
        Options.Add(SourceOption);
        Options.Add(IncludePrereleaseOption);
        Options.Add(ExactOption);
        Options.Add(AllowUntrustedOption);
        Options.Add(ForceOption);
    }

    protected override async Task<int> ExecuteAsync(ParseResult parseResult, CancellationToken cancellationToken)
    {
        string workload = parseResult.GetValue(WorkloadArgument)!;
        string? versionText = parseResult.GetValue(VersionOption);
        string? source = parseResult.GetValue(SourceOption);
        bool includePrerelease = parseResult.GetValue(IncludePrereleaseOption);
        bool exact = parseResult.GetValue(ExactOption);
        bool allowUntrusted = parseResult.GetValue(AllowUntrustedOption);
        bool force = parseResult.GetValue(ForceOption);

        if (includePrerelease)
        {
            _interaction.WriteHint(PrereleasePreviewHint);
        }

        if (!force && !LooksLikeLocalPackagePath(workload))
        {
            // Spec §4.1: when any version of <package> is already installed,
            // prompt the user to use `update` instead. --force skips the
            // prompt entirely. Non-interactive contexts treat the prompt as
            // a decline and exit non-zero with the same hint. Local .nupkg
            // installs bypass the check since the resolver path differs.
            int? earlyExit = await HandleAlreadyInstalledAsync(workload, exact, source, includePrerelease, allowUntrusted, cancellationToken);
            if (earlyExit is { } code)
            {
                return code;
            }
        }

        try
        {
            WorkloadInstallResult result = await _interaction.RunWithProgressAsync(
                LooksLikeLocalPackagePath(workload)
                    ? $"Installing '{Path.GetFileName(workload)}'"
                    : $"Installing workload '{workload}'",
                async (ctx, ct) =>
                {
                    var progress = new WorkloadInstallProgressAdapter(ctx);

                    return LooksLikeLocalPackagePath(workload)
                        ? await _installer.InstallFromPackageAsync(workload, force, allowUntrusted, progress, ct)
                        : await _installer.InstallFromCatalogAsync(
                            workload,
                            string.IsNullOrEmpty(versionText) ? null : NuGetVersion.Parse(versionText),
                            source,
                            includePrerelease,
                            exact,
                            force,
                            allowUntrusted,
                            progress,
                            ct);
                },
                cancellationToken);

            string message = BuildSuccessMessage(result);
            if (result.AlreadyInstalled)
            {
                _interaction.WriteWarning(message);
            }
            else
            {
                _interaction.WriteSuccess(message);
                WriteNextStepsHintIfApplicable(result.Entry);
            }
            return 0;
        }
        catch (WorkloadPackageNotFoundException ex)
        {
            throw new GracefulException(ex.Message, isUserError: true);
        }
        catch (AmbiguousPackageMatchException ex)
        {
            throw new GracefulException(ex.Message, isUserError: true);
        }
        catch (InvalidWorkloadException ex)
        {
            throw new GracefulException(ex.Message, isUserError: true);
        }
        catch (UntrustedWorkloadException ex)
        {
            throw new GracefulException(
                $"{ex.Message} Pass --allow-untrusted to install this package anyway.",
                isUserError: true);
        }
        catch (FileNotFoundException ex)
        {
            throw new GracefulException(ex.Message, isUserError: true);
        }
        catch (InvalidOperationException ex)
        {
            throw new GracefulException(
                $"{ex.Message} Pass --force to repair the install.",
                isUserError: true);
        }
    }

    /// <summary>
    /// Returns <c>true</c> when the positional argument looks like a path
    /// to a <c>.nupkg</c> on disk (i.e. ends in <c>.nupkg</c> and points to
    /// an existing file). Lets the user install a local package without
    /// going through the catalog. NuGet package ids cannot legally end in
    /// <c>.nupkg</c>, so this is unambiguous.
    /// </summary>
    private static bool LooksLikeLocalPackagePath(string value)
        => value.EndsWith(".nupkg", StringComparison.OrdinalIgnoreCase) && File.Exists(value);

    private async Task<int?> HandleAlreadyInstalledAsync(
        string identifier,
        bool exact,
        string? source,
        bool includePrerelease,
        bool allowUntrusted,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<WorkloadEntry> installed = await _store.GetWorkloadsAsync(cancellationToken);
        if (installed.Count == 0)
        {
            return null;
        }

        // Same matching rules as uninstall/update: by package id always,
        // by alias unless --exact. We don't need to enforce uniqueness
        // here; if any version matches we prompt.
        WorkloadEntry? match = installed.FirstOrDefault(e =>
            string.Equals(e.PackageId, identifier, StringComparison.OrdinalIgnoreCase)
            || (!exact && (e.Aliases?.Any(a => string.Equals(a, identifier, StringComparison.OrdinalIgnoreCase)) ?? false)));

        if (match is null)
        {
            return null;
        }

        // Prefer the first alias for user-facing messages; fall back to the
        // canonical package id when no alias is published.
        string display = match.Aliases.Count > 0 ? match.Aliases[0] : match.PackageId;
        string prompt = $"'{display}' is already installed at {match.PackageVersion}. Update instead?";

        if (!_interaction.IsInteractive)
        {
            _interaction.WriteHint(
                $"'{display}' is already installed at {match.PackageVersion}. " +
                $"Run 'func workload update {display}' to upgrade, or pass --force to install side-by-side.");
            return 1;
        }

        bool runUpdate = await _interaction.ConfirmAsync(prompt, defaultValue: true, cancellationToken);
        if (!runUpdate)
        {
            // User declined: fall through to a normal install, which will
            // go side-by-side or no-op for the same version.
            return null;
        }

        return await DispatchToUpdateAsync(match.PackageId, source, includePrerelease, allowUntrusted, cancellationToken);
    }

    // Delegate to the `func workload update` command (Parse + InvokeAsync) so
    // confirm-yes goes through exactly the same flow the user would hit if they
    // had run `func workload update <id>` themselves: same options, same
    // validators, same output. Avoids forking the rendering or the underlying
    // installer call between install and update.
    private async Task<int> DispatchToUpdateAsync(
        string packageId,
        string? source,
        bool includePrerelease,
        bool allowUntrusted,
        CancellationToken cancellationToken)
    {
        var args = new List<string> { packageId };
        if (!string.IsNullOrEmpty(source))
        {
            args.Add("--source");
            args.Add(source);
        }
        if (includePrerelease)
        {
            args.Add("--prerelease");
        }
        if (allowUntrusted)
        {
            args.Add("--allow-untrusted");
        }

        return await _updateCommand.Parse(args.ToArray()).InvokeAsync(configuration: null, cancellationToken);
    }

    private static string BuildSuccessMessage(WorkloadInstallResult result)
    {
        WorkloadEntry entry = result.Entry;
        string verb = result.AlreadyInstalled
            ? $"Workload '{entry.PackageId}' version '{entry.PackageVersion}' is already installed"
            : $"Installed workload '{entry.PackageId}' version '{entry.PackageVersion}'";

        return entry.Kind switch
        {
            WorkloadKind.Workload when entry.Aliases.Count > 0
                => $"{verb} (aliases: {string.Join(", ", entry.Aliases)}).",
            WorkloadKind.Content
                => $"{verb} (content at '{entry.Source}').",
            _ => $"{verb}.",
        };
    }

    // Surfaces the higher-level `func setup --features <name>` command after a
    // single-component install so users who reached for `func workload install`
    // by mistake (e.g. issue #5214) discover the planner. Suppressed in
    // non-interactive contexts to keep CI / JSON output clean and only fires
    // when the installed package id maps to a known feature group.
    private void WriteNextStepsHintIfApplicable(WorkloadEntry entry)
    {
        if (!_interaction.IsInteractive)
        {
            return;
        }

        if (!SetupFeatureCatalog.TryGetFeatureForPackageId(entry.PackageId, out string feature))
        {
            return;
        }

        string scope = feature switch
        {
            SetupFeatureCatalog.RuntimeFeature => "the Functions host and the extension bundle",
            SetupFeatureCatalog.HostFeature => "the Functions host",
            _ => $"the full {feature} dev environment (host, worker, stack, templates, bundle)",
        };

        _interaction.WriteHint(
            "Next steps:" + Environment.NewLine
            + $"  This installs a single workload. To install {scope}, run:" + Environment.NewLine
            + $"    func setup --features {feature}");
    }

}
