// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.CommandLine;
using Azure.Functions.Cli.Console;
using Azure.Functions.Cli.Workloads;

namespace Azure.Functions.Cli.Commands.Workload;

/// <summary>
/// Lists workloads recorded in the global manifest. Source of truth is
/// <see cref="WorkloadInfo"/>, populated by the install / discovery layer
/// from each package's <c>workload.json</c>.
/// </summary>
internal sealed class WorkloadListCommand(IInteractionService interaction, IReadOnlyList<WorkloadInfo> workloads)
    : BaseCommand("list", "List installed workloads.")
{
    private readonly IInteractionService _interaction = interaction ?? throw new ArgumentNullException(nameof(interaction));
    private readonly IReadOnlyList<WorkloadInfo> _workloads = workloads ?? throw new ArgumentNullException(nameof(workloads));

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
            w.Aliases.Count == 0 ? "—" : string.Join(", ", w.Aliases),
            w.DisplayName,
            w.Description,
            w.PackageVersion,
        });

        _interaction.WriteTable(["Package", "Aliases", "Name", "Description", "Version"], rows);
        return Task.FromResult(0);
    }
}
