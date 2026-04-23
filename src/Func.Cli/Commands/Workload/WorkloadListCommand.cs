// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.CommandLine;
using Azure.Functions.Cli.Console;
using Azure.Functions.Cli.Workloads;

namespace Azure.Functions.Cli.Commands.Workload;

/// <summary>
/// Lists every workload registered with the host. Intentionally minimal — the
/// real list command will read manifests, show install paths and source feeds.
/// </summary>
public class WorkloadListCommand : BaseCommand
{
    private readonly IInteractionService _interaction;
    private readonly IReadOnlyList<IWorkload> _workloads;

    public WorkloadListCommand(IInteractionService interaction, IReadOnlyList<IWorkload> workloads)
        : base("list", "List installed workloads.")
    {
        _interaction = interaction;
        _workloads = workloads;
    }

    protected override Task<int> ExecuteAsync(ParseResult parseResult, CancellationToken cancellationToken)
    {
        if (_workloads.Count == 0)
        {
            _interaction.WriteHint("No workloads installed.");
            return Task.FromResult(0);
        }

        var items = _workloads.Select(w => new DefinitionItem(w.Id, $"{w.DisplayName} — {w.Description}"));
        _interaction.WriteDefinitionList(items);
        return Task.FromResult(0);
    }
}
