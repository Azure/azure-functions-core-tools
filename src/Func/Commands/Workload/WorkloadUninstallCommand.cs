// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.CommandLine;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Console;
using Azure.Functions.Cli.Workloads.Install;
using Azure.Functions.Cli.Workloads.Storage;

namespace Azure.Functions.Cli.Commands.Workload;

/// <summary>
/// <c>func workload uninstall &lt;packageId&gt; [--version &lt;v&gt;] [--all]</c>.
/// Removes one or all installed versions of a workload. Resolution rules:
/// <list type="bullet">
///   <item><description><c>--all</c> removes every installed version.</description></item>
///   <item><description><c>--version</c> removes that exact version.</description></item>
///   <item><description>Neither flag: succeeds when exactly one version is installed; errors otherwise.</description></item>
/// </list>
/// </summary>
internal sealed class WorkloadUninstallCommand : FuncCliCommand
{
    private readonly IInteractionService _interaction;
    private readonly IWorkloadInstaller _installer;
    private readonly IGlobalManifestStore _store;

    private readonly Argument<string> _packageIdArgument;
    private readonly Option<string?> _versionOption;
    private readonly Option<bool> _allOption;

    public WorkloadUninstallCommand(
        IInteractionService interaction,
        IWorkloadInstaller installer,
        IGlobalManifestStore store)
        : base("uninstall", "Uninstall a workload.")
    {
        _interaction = interaction ?? throw new ArgumentNullException(nameof(interaction));
        _installer = installer ?? throw new ArgumentNullException(nameof(installer));
        _store = store ?? throw new ArgumentNullException(nameof(store));

        _packageIdArgument = new Argument<string>("packageId")
        {
            Description = "Package id of the workload to uninstall.",
        };
        _versionOption = new Option<string?>("--version")
        {
            Description = "Specific version to uninstall. Omit when only one version is installed.",
        };
        _allOption = new Option<bool>("--all")
        {
            Description = "Uninstall every installed version of the package.",
        };

        Arguments.Add(_packageIdArgument);
        Options.Add(_versionOption);
        Options.Add(_allOption);
    }

    protected override async Task<int> ExecuteAsync(ParseResult parseResult, CancellationToken cancellationToken)
    {
        var packageId = parseResult.GetValue(_packageIdArgument)
            ?? throw new GracefulException("packageId is required.", isUserError: true);
        var version = parseResult.GetValue(_versionOption);
        var all = parseResult.GetValue(_allOption);

        if (all && !string.IsNullOrEmpty(version))
        {
            throw new GracefulException(
                "--all and --version cannot be combined.",
                isUserError: true);
        }

        var installed = await _store.GetWorkloadsAsync(cancellationToken).ConfigureAwait(false);
        var matches = installed
            .Where(w => string.Equals(w.PackageId, packageId, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (matches.Count == 0)
        {
            throw new GracefulException(
                $"Workload '{packageId}' is not installed.",
                isUserError: true);
        }

        var toRemove = ResolveVersionsToRemove(packageId, version, all, matches);

        foreach (var candidate in toRemove)
        {
            var removed = await _installer.UninstallAsync(candidate.PackageId, candidate.Version, cancellationToken)
                .ConfigureAwait(false);
            if (removed)
            {
                _interaction.WriteSuccess(
                    $"Uninstalled workload '{candidate.PackageId}' version '{candidate.Version}'.");
            }
        }

        return 0;
    }

    private static IReadOnlyList<InstalledWorkload> ResolveVersionsToRemove(
        string packageId,
        string? version,
        bool all,
        IReadOnlyList<InstalledWorkload> matches)
    {
        if (all)
        {
            return matches;
        }

        if (!string.IsNullOrEmpty(version))
        {
            var match = matches.FirstOrDefault(
                m => string.Equals(m.Version, version, StringComparison.Ordinal));
            if (match is null)
            {
                var available = string.Join(", ", matches.Select(m => m.Version));
                throw new GracefulException(
                    $"Workload '{packageId}' version '{version}' is not installed. " +
                    $"Installed versions: {available}.",
                    isUserError: true);
            }

            return new[] { match };
        }

        if (matches.Count > 1)
        {
            var available = string.Join(", ", matches.Select(m => m.Version));
            throw new GracefulException(
                $"Multiple versions of '{packageId}' are installed ({available}). " +
                "Pass --version <v> or --all.",
                isUserError: true);
        }

        return matches;
    }
}
