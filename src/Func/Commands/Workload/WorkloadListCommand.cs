// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.CommandLine;
using Azure.Functions.Cli.Console;
using Azure.Functions.Cli.Workloads;
using Azure.Functions.Cli.Workloads.Storage;
using NuGet.Versioning;

namespace Azure.Functions.Cli.Commands.Workload;

/// <summary>
/// <c>func workload list [--all-versions|-a] [--json]</c>. Lists installed
/// workloads. Default: only the loaded version per workload (highest
/// installed semver). Pass <c>--all-versions</c> to include every
/// side-by-side install.
/// </summary>
internal sealed class WorkloadListCommand : FuncCliCommand
{
    private const string AliasesPlaceholder = "-";

    private readonly IInteractionService _interaction;
    private readonly IWorkloadProvider _workloads;
    private readonly IWorkloadStore _store;

    public Option<bool> AllVersionsOption { get; } = new("--all-versions", "-a")
    {
        Description = "List every installed version of every workload. Default: loaded version only.",
    };

    public Option<bool> JsonOption { get; } = new("--json")
    {
        Description = "Emit machine-readable JSON instead of a table.",
    };

    public WorkloadListCommand(
        IInteractionService interaction,
        IWorkloadProvider workloads,
        IWorkloadStore store)
        : base("list", "List installed workloads.")
    {
        _interaction = interaction ?? throw new ArgumentNullException(nameof(interaction));
        _workloads = workloads ?? throw new ArgumentNullException(nameof(workloads));
        _store = store ?? throw new ArgumentNullException(nameof(store));

        Options.Add(AllVersionsOption);
        Options.Add(JsonOption);
    }

    protected override async Task<int> ExecuteAsync(ParseResult parseResult, CancellationToken cancellationToken)
    {
        bool allVersions = parseResult.GetValue(AllVersionsOption);
        bool json = parseResult.GetValue(JsonOption);

        IReadOnlyList<ListRow> rows = allVersions
            ? await BuildAllVersionsRowsAsync(cancellationToken)
            : BuildLoadedRows();

        if (json)
        {
            _interaction.WriteJson(rows);
            return 0;
        }

        if (rows.Count == 0)
        {
            _interaction.WriteHint("No workloads installed.");
            return 0;
        }

        IEnumerable<string[]> tableRows = rows.Select(r => new[]
        {
            r.PackageId,
            r.Aliases.Count == 0 ? AliasesPlaceholder : string.Join(", ", r.Aliases),
            string.IsNullOrWhiteSpace(r.DisplayName) ? AliasesPlaceholder : r.DisplayName,
            string.IsNullOrWhiteSpace(r.Description) ? AliasesPlaceholder : r.Description,
            r.PackageVersion,
        });

        _interaction.WriteTable(["ID", "Aliases", "Display Name", "Description", "Version"], tableRows);
        return 0;
    }

    private IReadOnlyList<ListRow> BuildLoadedRows()
    {
        IReadOnlyList<WorkloadInfo> workloads = _workloads.GetWorkloads();
        return [.. workloads.Select(w => new ListRow(
            w.PackageId,
            w.PackageVersion,
            w.Aliases,
            w.DisplayName,
            w.Description))];
    }

    private async Task<IReadOnlyList<ListRow>> BuildAllVersionsRowsAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<WorkloadEntry> installed = await _store.GetWorkloadsAsync(cancellationToken);

        // Cross-reference runtime info so loaded versions use presentation
        // from their instances; older and content entries use registry data.
        Dictionary<(string PackageId, string Version), RuntimeWorkloadInfo> loadedByKey = _workloads
            .GetRuntimeWorkloads()
            .ToDictionary(
                w => (w.PackageId, w.PackageVersion),
                w => w,
                new PackageVersionKeyComparer());

        return [.. installed
            .OrderBy(e => e.PackageId, StringComparer.OrdinalIgnoreCase)
            .ThenByDescending(e => ParseVersion(e.PackageVersion))
            .Select(e =>
            {
                loadedByKey.TryGetValue((e.PackageId, e.PackageVersion), out RuntimeWorkloadInfo? info);
                return new ListRow(
                    e.PackageId,
                    e.PackageVersion,
                    e.Aliases ?? [],
                    info?.DisplayName ?? GetDisplayName(e),
                    info?.Description ?? (e.Description ?? string.Empty));
            })];
    }

    private static string GetDisplayName(WorkloadEntry entry)
        => string.IsNullOrWhiteSpace(entry.DisplayName) ? entry.PackageId : entry.DisplayName;

    private static NuGetVersion ParseVersion(string raw) =>
        NuGetVersion.TryParse(raw, out NuGetVersion? v) ? v : new NuGetVersion(0, 0, 0);

    /// <summary>
    /// Row projection used for both the table and JSON output. Property
    /// names are camelCased by the JSON serializer.
    /// </summary>
    internal sealed record ListRow(
        string PackageId,
        string PackageVersion,
        IReadOnlyList<string> Aliases,
        string DisplayName,
        string Description);

    private sealed class PackageVersionKeyComparer : IEqualityComparer<(string PackageId, string Version)>
    {
        public bool Equals((string PackageId, string Version) x, (string PackageId, string Version) y) =>
            string.Equals(x.PackageId, y.PackageId, StringComparison.OrdinalIgnoreCase)
            && string.Equals(x.Version, y.Version, StringComparison.Ordinal);

        public int GetHashCode((string PackageId, string Version) obj) =>
            HashCode.Combine(
                StringComparer.OrdinalIgnoreCase.GetHashCode(obj.PackageId),
                StringComparer.Ordinal.GetHashCode(obj.Version));
    }
}

