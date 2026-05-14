// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.CommandLine;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Console;
using Azure.Functions.Cli.Workloads;
using Azure.Functions.Cli.Workloads.Catalog;
using Azure.Functions.Cli.Workloads.Discovery;
using Azure.Functions.Cli.Workloads.Install;
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
    private readonly IInteractionService _interaction;
    private readonly IWorkloadInstaller _installer;
    private readonly IWorkloadStore _store;

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
        Description = "Allow prerelease versions when resolving from the catalog. Default: stable only.",
    };

    public Option<bool> ForceOption { get; } = new("--force", "-f")
    {
        Description = "Overwrite an existing install of the same id and version.",
    };

    public Option<bool> ExactOption { get; } = new("--exact", "-e")
    {
        Description = "Disable alias matching. <id> must be the literal package id.",
    };

    public WorkloadInstallCommand(
        IInteractionService interaction,
        IWorkloadInstaller installer,
        IWorkloadStore store)
        : base("install", "Install a workload.")
    {
        _interaction = interaction ?? throw new ArgumentNullException(nameof(interaction));
        _installer = installer ?? throw new ArgumentNullException(nameof(installer));
        _store = store ?? throw new ArgumentNullException(nameof(store));

        WorkloadArgument.AddRequiredIdValidator();
        VersionOption.AddSemVerValidator();

        Arguments.Add(WorkloadArgument);
        Options.Add(VersionOption);
        Options.Add(SourceOption);
        Options.Add(IncludePrereleaseOption);
        Options.Add(ExactOption);
        Options.Add(ForceOption);
    }

    protected override async Task<int> ExecuteAsync(ParseResult parseResult, CancellationToken cancellationToken)
    {
        string workload = parseResult.GetValue(WorkloadArgument)!;
        string? versionText = parseResult.GetValue(VersionOption);
        string? source = parseResult.GetValue(SourceOption);
        bool includePrerelease = parseResult.GetValue(IncludePrereleaseOption);
        bool exact = parseResult.GetValue(ExactOption);
        bool force = parseResult.GetValue(ForceOption);

        if (!force && !LooksLikeLocalPackagePath(workload))
        {
            // Spec §4.1: when any version of <package> is already installed,
            // prompt the user to use `update` instead. --force skips the
            // prompt entirely. Non-interactive contexts treat the prompt as
            // a decline and exit non-zero with the same hint. Local .nupkg
            // installs bypass the check since the resolver path differs.
            int? earlyExit = await CheckAlreadyInstalledAsync(workload, exact, cancellationToken);
            if (earlyExit is { } code)
            {
                return code;
            }
        }

        try
        {
            WorkloadInstallResult result = LooksLikeLocalPackagePath(workload)
                ? await _installer.InstallFromPackageAsync(workload, force, cancellationToken)
                : await _installer.InstallFromCatalogAsync(
                    workload,
                    string.IsNullOrEmpty(versionText) ? null : NuGetVersion.Parse(versionText),
                    source,
                    includePrerelease,
                    exact,
                    force,
                    cancellationToken);

            _interaction.WriteSuccess(BuildSuccessMessage(result));
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

    private async Task<int?> CheckAlreadyInstalledAsync(
        string identifier,
        bool exact,
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

        string canonicalId = match.PackageId;
        string prompt =
            $"'{canonicalId}' is already installed at {match.PackageVersion}. " +
            $"Run 'func workload update {canonicalId}' instead?";

        if (!_interaction.IsInteractive)
        {
            _interaction.WriteHint(
                $"'{canonicalId}' is already installed at {match.PackageVersion}. " +
                $"Run 'func workload update {canonicalId}' to upgrade, or pass --force to install side-by-side.");
            return 1;
        }

        bool useUpdate = await _interaction.ConfirmAsync(prompt, defaultValue: true, cancellationToken);
        if (useUpdate)
        {
            _interaction.WriteHint($"Run 'func workload update {canonicalId}' to upgrade.");
            return 1;
        }

        // User declined the suggestion: fall through to a normal install,
        // which will go side-by-side or no-op for the same version.
        return null;
    }

    private static string BuildSuccessMessage(WorkloadInstallResult result)
    {
        WorkloadEntry entry = result.Entry;
        string verb = result.AlreadyInstalled
            ? $"Workload '{entry.PackageId}' version '{entry.PackageVersion}' is already installed"
            : $"Installed workload '{entry.PackageId}' version '{entry.PackageVersion}'";

        return entry.Kind switch
        {
            WorkloadKind.Workload when entry.EntryPoint is not null
                => $"{verb} (entry point: {entry.EntryPoint.Type}).",
            WorkloadKind.Content
                => $"{verb} (content at '{entry.Source}').",
            _ => $"{verb}.",
        };
    }
}
