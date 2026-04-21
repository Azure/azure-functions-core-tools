// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.CommandLine;
using Azure.Functions.Cli.Console;
using Azure.Functions.Cli.Workloads;

namespace Azure.Functions.Cli.Commands;

/// <summary>
/// <c>func workload</c> parent command. Subcommands manage the on-disk pool
/// of out-of-process workloads (install / uninstall / list / search).
/// </summary>
public class WorkloadCommand : Command
{
    public WorkloadCommand(IInteractionService interaction, IWorkloadInstaller installer, IWorkloadHost host)
        : base("workload", "Manage CLI workloads (out-of-process language packs).")
    {
        Subcommands.Add(new WorkloadInstallCommand(interaction, installer));
        Subcommands.Add(new WorkloadUninstallCommand(interaction, installer));
        Subcommands.Add(new WorkloadListCommand(interaction, installer, host));
        Subcommands.Add(new WorkloadSearchCommand(interaction, installer));
    }
}

public class WorkloadInstallCommand : BaseCommand
{
    public static readonly Argument<string> IdArgument = new("id-or-package")
    {
        Description = "Short id (e.g., 'sample', 'dotnet') or NuGet package id.",
    };

    public static readonly Option<string?> FromOption = new("--from")
    {
        Description = "Local directory containing workload.json + executable. Bypasses the catalog.",
    };

    private readonly IInteractionService _interaction;
    private readonly IWorkloadInstaller _installer;

    public WorkloadInstallCommand(IInteractionService interaction, IWorkloadInstaller installer)
        : base("install", "Install an out-of-process workload.")
    {
        _interaction = interaction;
        _installer = installer;
        Arguments.Add(IdArgument);
        Options.Add(FromOption);
    }

    protected override async Task<int> ExecuteAsync(ParseResult parseResult, CancellationToken cancellationToken)
    {
        var id = parseResult.GetValue(IdArgument)!;
        var from = parseResult.GetValue(FromOption);

        await _interaction.StatusAsync(
            $"Installing workload '{id}'...",
            ct => _installer.InstallAsync(id, from, ct),
            cancellationToken);
        return 0;
    }
}

public class WorkloadUninstallCommand : BaseCommand
{
    public static readonly Argument<string> IdArgument = new("id")
    {
        Description = "Workload id to uninstall.",
    };

    private readonly IInteractionService _interaction;
    private readonly IWorkloadInstaller _installer;

    public WorkloadUninstallCommand(IInteractionService interaction, IWorkloadInstaller installer)
        : base("uninstall", "Uninstall a workload.")
    {
        _interaction = interaction;
        _installer = installer;
        Arguments.Add(IdArgument);
    }

    protected override async Task<int> ExecuteAsync(ParseResult parseResult, CancellationToken cancellationToken)
    {
        var id = parseResult.GetValue(IdArgument)!;
        await _interaction.StatusAsync(
            $"Uninstalling workload '{id}'...",
            ct => _installer.UninstallAsync(id, ct),
            cancellationToken);
        return 0;
    }
}

public class WorkloadListCommand : BaseCommand
{
    private readonly IInteractionService _interaction;
    private readonly IWorkloadInstaller _installer;
    private readonly IWorkloadHost _host;

    public WorkloadListCommand(IInteractionService interaction, IWorkloadInstaller installer, IWorkloadHost host)
        : base("list", "List installed workloads.")
    {
        _interaction = interaction;
        _installer = installer;
        _host = host;
    }

    protected override Task<int> ExecuteAsync(ParseResult parseResult, CancellationToken cancellationToken)
    {
        var installed = _installer.GetInstalled();
        if (installed.Count == 0)
        {
            _interaction.WriteMarkupLine("[grey]No workloads installed.[/]");
            _interaction.WriteBlankLine();
            _interaction.WriteMarkupLine("[grey]Install a workload with:[/] [white]func workload install <id>[/]");
            return Task.FromResult(0);
        }

        var discovered = _host.DiscoverWorkloads().ToDictionary(d => d.Manifest.Id, StringComparer.OrdinalIgnoreCase);

        _interaction.WriteTable(
            ["Workload", "Version", "Runtimes", "Installed"],
            installed.Select(w => new[]
            {
                w.Id,
                w.Version,
                discovered.TryGetValue(w.Id, out var d) ? string.Join(", ", d.Manifest.WorkerRuntimes) : "(missing)",
                w.InstalledAt.LocalDateTime.ToString("yyyy-MM-dd HH:mm"),
            }));

        return Task.FromResult(0);
    }
}

public class WorkloadSearchCommand : BaseCommand
{
    public static readonly Argument<string?> QueryArgument = new("query")
    {
        Description = "Optional filter term.",
        Arity = ArgumentArity.ZeroOrOne,
    };

    private readonly IInteractionService _interaction;
    private readonly IWorkloadInstaller _installer;

    public WorkloadSearchCommand(IInteractionService interaction, IWorkloadInstaller installer)
        : base("search", "List workloads available for installation.")
    {
        _interaction = interaction;
        _installer = installer;
        Arguments.Add(QueryArgument);
    }

    protected override Task<int> ExecuteAsync(ParseResult parseResult, CancellationToken cancellationToken)
    {
        var query = parseResult.GetValue(QueryArgument);
        var available = _installer.GetAvailable();

        if (!string.IsNullOrWhiteSpace(query))
        {
            available = [.. available.Where(w =>
                w.Id.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                w.Description.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                w.Languages.Contains(query, StringComparison.OrdinalIgnoreCase))];
        }

        if (available.Count == 0)
        {
            _interaction.WriteMarkupLine("[grey]No workloads found.[/]");
            return Task.FromResult(0);
        }

        _interaction.WriteTable(
            ["Id", "Description", "Languages", "Status"],
            available.Select(w => new[]
            {
                w.Id,
                w.Description,
                w.Languages,
                w.IsInstalled ? $"✓ {w.InstalledVersion}" : "Not installed",
            }));

        _interaction.WriteBlankLine();
        _interaction.WriteMarkupLine("[grey]Install a workload with:[/] [white]func workload install <id>[/]");
        return Task.FromResult(0);
    }
}
