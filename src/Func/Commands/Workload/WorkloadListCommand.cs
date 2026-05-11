// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.CommandLine;
using Azure.Functions.Cli.Console;
using Azure.Functions.Cli.Workloads;

namespace Azure.Functions.Cli.Commands.Workload;

/// <summary>
/// Lists workloads from <see cref="IWorkloadProvider"/>.
/// </summary>
internal sealed class WorkloadListCommand(
    IInteractionService interaction,
    IWorkloadProvider workloads)
    : FuncCliCommand("list", "List installed workloads.")
{
    private const string AliasesPlaceholder = "-";

    private readonly IInteractionService _interaction = interaction ?? throw new ArgumentNullException(nameof(interaction));
    private readonly IWorkloadProvider _workloads = workloads ?? throw new ArgumentNullException(nameof(workloads));

    protected override Task<int> ExecuteAsync(ParseResult parseResult, CancellationToken cancellationToken)
    {
        IReadOnlyList<WorkloadInfo> workloads = _workloads.GetWorkloads();

        if (workloads.Count == 0)
        {
            _interaction.WriteHint("No workloads installed.");
            return Task.FromResult(0);
        }

        IEnumerable<string[]> rows = workloads.Select(w => new[]
        {
            w.PackageId,
            w.Aliases.Count == 0 ? AliasesPlaceholder : string.Join(", ", w.Aliases),
            w.DisplayName,
            w.Description,
            w.PackageVersion,
        });

        _interaction.WriteTable(["ID", "Aliases", "Display Name", "Description", "Version"], rows);
        return Task.FromResult(0);
    }
}
