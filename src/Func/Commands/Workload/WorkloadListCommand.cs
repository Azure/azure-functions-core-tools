// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.CommandLine;
using Azure.Functions.Cli.Console;
using Azure.Functions.Cli.Workloads.Storage;

namespace Azure.Functions.Cli.Commands.Workload;

/// <summary>
/// Lists workloads recorded in the global manifest. Reads directly from
/// <see cref="IGlobalManifestStore"/> so listing works without invoking the
/// workload loader (no assembly loads, no ALC creation).
/// </summary>
internal sealed class WorkloadListCommand(IInteractionService interaction, IGlobalManifestStore store)
    : FuncCliCommand("list", "List installed workloads.")
{
    private const string AliasesPlaceholder = "-";

    private readonly IInteractionService _interaction = interaction ?? throw new ArgumentNullException(nameof(interaction));
    private readonly IGlobalManifestStore _store = store ?? throw new ArgumentNullException(nameof(store));

    protected override async Task<int> ExecuteAsync(ParseResult parseResult, CancellationToken cancellationToken)
    {
        var workloads = await _store.GetWorkloadsAsync(cancellationToken).ConfigureAwait(false);

        if (workloads.Count == 0)
        {
            _interaction.WriteHint("No workloads installed.");
            return 0;
        }

        var rows = workloads.Select(w => new[]
        {
            w.PackageId,
            w.Entry.Aliases.Count == 0 ? AliasesPlaceholder : string.Join(", ", w.Entry.Aliases),
            w.Entry.DisplayName,
            w.Entry.Description,
            w.Version,
        });

        _interaction.WriteTable(["Package", "Aliases", "Name", "Description", "Version"], rows);
        return 0;
    }
}
