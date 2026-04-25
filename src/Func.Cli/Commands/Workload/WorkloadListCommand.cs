// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.CommandLine;
using Azure.Functions.Cli.Console;
using Azure.Functions.Cli.Workloads;

namespace Azure.Functions.Cli.Commands.Workload;

/// <summary>
/// Lists workloads recorded in the global manifest. Source of truth is
/// <see cref="InstalledWorkload"/>, populated by the install / discovery
/// layer from each package's <c>workload.json</c>.
/// </summary>
public class WorkloadListCommand : BaseCommand
{
    private readonly IInteractionService _interaction;
    private readonly IReadOnlyList<InstalledWorkload> _workloads;

    public WorkloadListCommand(IInteractionService interaction, IReadOnlyList<InstalledWorkload> workloads)
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

        var rows = _workloads.Select(w => new[]
        {
            w.PackageId,
            w.Type.ToString(),
            w.Aliases.Count == 0 ? "—" : string.Join(", ", w.Aliases),
            w.DisplayName,
            w.Description,
            w.Version,
        });

        _interaction.WriteTable(["Package", "Type", "Aliases", "Name", "Description", "Version"], rows);
        return Task.FromResult(0);
    }
}
