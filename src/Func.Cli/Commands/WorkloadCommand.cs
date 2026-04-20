// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.CommandLine;
using Azure.Functions.Cli.Console;
using Azure.Functions.Cli.Workloads;

namespace Azure.Functions.Cli.Commands;

/// <summary>
/// Parent command for workload management: func workload [install|uninstall|list|update].
/// </summary>
public class WorkloadCommand : Command
{
    public WorkloadCommand(IInteractionService interaction, IWorkloadManager workloadManager)
        : base("workload", "Manage CLI workloads (language packs, extensions).")
    {
        Subcommands.Add(new WorkloadInstallCommand(interaction, workloadManager));
        Subcommands.Add(new WorkloadUninstallCommand(interaction, workloadManager));
        Subcommands.Add(new WorkloadListCommand(interaction, workloadManager));
        Subcommands.Add(new WorkloadUpdateCommand(interaction, workloadManager));
        Subcommands.Add(new WorkloadSearchCommand(interaction, workloadManager));
    }
}

/// <summary>
/// Installs a workload from a NuGet package: func workload install <package-id> [--version]
/// </summary>
public class WorkloadInstallCommand : BaseCommand
{
    public static readonly Argument<string> PackageIdArgument = new("package-id")
    {
        Description = "The NuGet package ID or short name (e.g., 'dotnet', 'python') of the workload to install"
    };

    public static readonly Option<string?> VersionOption = new("--version")
    {
        Description = "The version to install (default: latest)"
    };

    private readonly IInteractionService _interaction;
    private readonly IWorkloadManager _workloadManager;

    public WorkloadInstallCommand(IInteractionService interaction, IWorkloadManager workloadManager)
        : base("install", "Install a workload from a NuGet package.")
    {
        _interaction = interaction;
        _workloadManager = workloadManager;

        Arguments.Add(PackageIdArgument);
        Options.Add(VersionOption);
    }

    protected override async Task<int> ExecuteAsync(ParseResult parseResult, CancellationToken cancellationToken)
    {
        var packageId = parseResult.GetValue(PackageIdArgument)!;
        var version = parseResult.GetValue(VersionOption);

        await _interaction.StatusAsync(
            $"Installing workload from {packageId}...",
            async ct => await _workloadManager.InstallWorkloadAsync(packageId, version, ct),
            cancellationToken);

        return 0;
    }
}

/// <summary>
/// Uninstalls a workload: func workload uninstall <workload-id>
/// </summary>
public class WorkloadUninstallCommand : BaseCommand
{
    public static readonly Argument<string> WorkloadIdArgument = new("workload-id")
    {
        Description = "The ID of the workload to uninstall"
    };

    private readonly IInteractionService _interaction;
    private readonly IWorkloadManager _workloadManager;

    public WorkloadUninstallCommand(IInteractionService interaction, IWorkloadManager workloadManager)
        : base("uninstall", "Uninstall a workload.")
    {
        _interaction = interaction;
        _workloadManager = workloadManager;

        Arguments.Add(WorkloadIdArgument);
    }

    protected override async Task<int> ExecuteAsync(ParseResult parseResult, CancellationToken cancellationToken)
    {
        var workloadId = parseResult.GetValue(WorkloadIdArgument)!;

        await _interaction.StatusAsync(
            $"Uninstalling workload '{workloadId}'...",
            async ct => await _workloadManager.UninstallWorkloadAsync(workloadId, ct),
            cancellationToken);

        return 0;
    }
}

/// <summary>
/// Lists installed workloads: func workload list
/// </summary>
public class WorkloadListCommand : BaseCommand
{
    private readonly IInteractionService _interaction;
    private readonly IWorkloadManager _workloadManager;

    public WorkloadListCommand(IInteractionService interaction, IWorkloadManager workloadManager)
        : base("list", "List installed workloads.")
    {
        _interaction = interaction;
        _workloadManager = workloadManager;
    }

    protected override Task<int> ExecuteAsync(ParseResult parseResult, CancellationToken cancellationToken)
    {
        var workloads = _workloadManager.GetInstalledWorkloads();

        if (workloads.Count == 0)
        {
            _interaction.WriteMarkupLine("[grey]No workloads installed.[/]");
            _interaction.WriteBlankLine();
            _interaction.WriteMarkupLine("[grey]Install a workload with:[/] [white]func workload install <package-id>[/]");
            return Task.FromResult(0);
        }

        _interaction.WriteTable(
            ["Workload", "Version", "Package", "Installed"],
            workloads.Select(w => new[]
            {
                w.Id,
                w.Version,
                w.PackageId,
                w.InstalledAt.LocalDateTime.ToString("yyyy-MM-dd HH:mm")
            }));

        return Task.FromResult(0);
    }
}

/// <summary>
/// Updates a workload: func workload update [workload-id] [--version]
/// </summary>
public class WorkloadUpdateCommand : BaseCommand
{
    public static readonly Argument<string?> WorkloadIdArgument = new("workload-id")
    {
        Description = "The ID of the workload to update (updates all if omitted)",
        Arity = ArgumentArity.ZeroOrOne
    };

    public static readonly Option<string?> VersionOption = new("--version")
    {
        Description = "The version to update to (default: latest)"
    };

    private readonly IInteractionService _interaction;
    private readonly IWorkloadManager _workloadManager;

    public WorkloadUpdateCommand(IInteractionService interaction, IWorkloadManager workloadManager)
        : base("update", "Update installed workloads.")
    {
        _interaction = interaction;
        _workloadManager = workloadManager;

        Arguments.Add(WorkloadIdArgument);
        Options.Add(VersionOption);
    }

    protected override async Task<int> ExecuteAsync(ParseResult parseResult, CancellationToken cancellationToken)
    {
        var workloadId = parseResult.GetValue(WorkloadIdArgument);
        var version = parseResult.GetValue(VersionOption);

        if (!string.IsNullOrEmpty(workloadId))
        {
            await _interaction.StatusAsync(
                $"Updating workload '{workloadId}'...",
                async ct => await _workloadManager.UpdateWorkloadAsync(workloadId, version, ct),
                cancellationToken);
        }
        else
        {
            var workloads = _workloadManager.GetInstalledWorkloads();
            if (workloads.Count == 0)
            {
                _interaction.WriteMarkupLine("[grey]No workloads installed to update.[/]");
                return 0;
            }

            foreach (var w in workloads)
            {
                await _interaction.StatusAsync(
                    $"Updating workload '{w.Id}'...",
                    async ct => await _workloadManager.UpdateWorkloadAsync(w.Id, version, ct),
                    cancellationToken);
            }
        }

        return 0;
    }
}

/// <summary>
/// Searches for available workloads: func workload search [query]
/// Queries NuGet for packages matching the Azure.Functions.Cli.Workload.* convention,
/// merged with the built-in catalog. Shows install status for each.
/// </summary>
public class WorkloadSearchCommand : BaseCommand
{
    public static readonly Argument<string?> QueryArgument = new("query")
    {
        Description = "Optional search term to filter workloads",
        Arity = ArgumentArity.ZeroOrOne
    };

    private readonly IInteractionService _interaction;
    private readonly IWorkloadManager _workloadManager;

    public WorkloadSearchCommand(IInteractionService interaction, IWorkloadManager workloadManager)
        : base("search", "Search for available workloads on NuGet.")
    {
        _interaction = interaction;
        _workloadManager = workloadManager;

        Arguments.Add(QueryArgument);
    }

    protected override async Task<int> ExecuteAsync(ParseResult parseResult, CancellationToken cancellationToken)
    {
        var query = parseResult.GetValue(QueryArgument);

        _interaction.WriteMarkupLine("[grey]Searching for workloads...[/]");

        var workloads = await _workloadManager.GetAvailableWorkloadsAsync(cancellationToken);

        // Apply local filter if query provided
        if (!string.IsNullOrWhiteSpace(query))
        {
            workloads = workloads
                .Where(w =>
                    w.Id.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                    w.Description.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                    w.Languages.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                    w.PackageId.Contains(query, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        if (workloads.Count == 0)
        {
            _interaction.WriteMarkupLine(string.IsNullOrWhiteSpace(query)
                ? "[grey]No workloads found.[/]"
                : $"[grey]No workloads matching '{query}' found.[/]");
            return 0;
        }

        _interaction.WriteTable(
            ["Name", "Package", "Description", "Languages", "Status"],
            workloads.Select(w => new[]
            {
                w.Id,
                w.PackageId,
                w.Description,
                w.Languages,
                w.IsInstalled ? $"✓ {w.InstalledVersion}" : "Not installed"
            }));

        _interaction.WriteBlankLine();
        _interaction.WriteMarkupLine("[grey]Install a workload with:[/] [white]func workload install <name>[/]");

        return 0;
    }
}
