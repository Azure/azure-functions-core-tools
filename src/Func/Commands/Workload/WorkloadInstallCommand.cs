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

    public Argument<string> WorkloadArgument { get; } = new("id")
    {
        Description = "Workload package id or alias to install.",
    };

    public Option<string?> VersionOption { get; } = new("--version", "-v")
    {
        Description = "Specific semver version to install. Default: the latest stable version in the catalog.",
    };

    public Option<string?> SourceOption { get; } = new("--source")
    {
        Description = "Catalog source URL or local directory to resolve from. Default: the configured catalog.",
    };

    public Option<bool> IncludePrereleasesOption { get; } = new("--include-prereleases")
    {
        Description = "Allow prerelease versions when resolving from the catalog.",
    };

    public Option<bool> ForceOption { get; } = new("--force", "-f")
    {
        Description = "Overwrite an existing install of the same id and version.",
    };

    public Option<bool> ExactOption { get; } = new("--exact", "-e")
    {
        Description = "Disable alias matching. <id> must be the literal package id.",
    };

    public WorkloadInstallCommand(IInteractionService interaction, IWorkloadInstaller installer)
        : base("install", "Install a workload.")
    {
        _interaction = interaction ?? throw new ArgumentNullException(nameof(interaction));
        _installer = installer ?? throw new ArgumentNullException(nameof(installer));

        WorkloadArgument.Validators.Add(result =>
        {
            string? value = result.GetValue(WorkloadArgument);
            if (string.IsNullOrWhiteSpace(value))
            {
                result.AddError("A workload id is required.");
            }
        });

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
        Options.Add(SourceOption);
        Options.Add(IncludePrereleasesOption);
        Options.Add(ExactOption);
        Options.Add(ForceOption);
    }

    protected override async Task<int> ExecuteAsync(ParseResult parseResult, CancellationToken cancellationToken)
    {
        string workload = parseResult.GetValue(WorkloadArgument)!;
        string? versionText = parseResult.GetValue(VersionOption);
        string? source = parseResult.GetValue(SourceOption);
        bool includePrereleases = parseResult.GetValue(IncludePrereleasesOption);
        bool exact = parseResult.GetValue(ExactOption);
        bool force = parseResult.GetValue(ForceOption);

        try
        {
            WorkloadInstallResult result = await _installer.InstallFromCatalogAsync(
                workload,
                string.IsNullOrEmpty(versionText) ? null : NuGetVersion.Parse(versionText),
                source,
                includePrereleases,
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
        catch (AmbiguousAliasException ex)
        {
            throw new GracefulException(ex.Message, isUserError: true);
        }
        catch (InvalidWorkloadException ex)
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
