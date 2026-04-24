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
    private readonly IReadOnlyList<WorkloadSummary> _summaries;

    public WorkloadListCommand(IInteractionService interaction, IReadOnlyList<WorkloadSummary> summaries)
        : base("list", "List installed workloads.")
    {
        _interaction = interaction;
        _summaries = summaries;
    }

    protected override Task<int> ExecuteAsync(ParseResult parseResult, CancellationToken cancellationToken)
    {
        if (_summaries.Count == 0)
        {
            _interaction.WriteHint("No workloads installed.");
            return Task.FromResult(0);
        }

        var rows = _summaries.Select(s => new[]
        {
            s.PackageId,
            s.Aliases.Count == 0 ? "—" : string.Join(", ", s.Aliases),
            s.DisplayName,
            s.Description,
        });

        _interaction.WriteTable(["Package", "Aliases", "Name", "Description"], rows);
        return Task.FromResult(0);
    }
}
