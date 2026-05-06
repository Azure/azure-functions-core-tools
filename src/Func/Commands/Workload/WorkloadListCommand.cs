// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.CommandLine;
using Azure.Functions.Cli.Console;
using Azure.Functions.Cli.Workloads;
using Azure.Functions.Cli.Workloads.Loading;
using Azure.Functions.Cli.Workloads.Storage;

namespace Azure.Functions.Cli.Commands.Workload;

/// <summary>
/// Lists workloads recorded in the global registry. Reads entries from
/// <see cref="IWorkloadStore"/> and asks <see cref="IWorkloadLoader"/> to
/// hydrate them so display name and description come from the
/// <see cref="Workloads.Workload"/> instance itself rather than being
/// duplicated in the registry.
/// </summary>
internal sealed class WorkloadListCommand(
    IInteractionService interaction,
    IWorkloadStore store,
    IWorkloadLoader loader)
    : FuncCliCommand("list", "List installed workloads.")
{
    private const string AliasesPlaceholder = "-";

    private readonly IInteractionService _interaction = interaction ?? throw new ArgumentNullException(nameof(interaction));
    private readonly IWorkloadStore _store = store ?? throw new ArgumentNullException(nameof(store));
    private readonly IWorkloadLoader _loader = loader ?? throw new ArgumentNullException(nameof(loader));

    protected override async Task<int> ExecuteAsync(ParseResult parseResult, CancellationToken cancellationToken)
    {
        IReadOnlyList<WorkloadEntry> entries = await _store.GetWorkloadsAsync(cancellationToken);

        if (entries.Count == 0)
        {
            _interaction.WriteHint("No workloads installed.");
            return 0;
        }

        IReadOnlyList<WorkloadInfo> workloads = _loader.Load(entries);

        IEnumerable<string[]> rows = workloads.Select(w => new[]
        {
            w.PackageId,
            w.Aliases.Count == 0 ? AliasesPlaceholder : string.Join(", ", w.Aliases),
            w.Instance.DisplayName,
            w.Instance.Description,
            w.PackageVersion,
        });

        _interaction.WriteTable(["Package", "Aliases", "Name", "Description", "Version"], rows);
        return 0;
    }
}
