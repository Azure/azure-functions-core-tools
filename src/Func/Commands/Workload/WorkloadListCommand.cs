// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.CommandLine;
using Azure.Functions.Cli.Console;
using Azure.Functions.Cli.Workloads;
using Azure.Functions.Cli.Workloads.Storage;
using NuGet.Versioning;

namespace Azure.Functions.Cli.Commands.Workload;

/// <summary>
/// <c>func workload list [--all-versions|-a] [--json] [--verbose]</c>.
/// Default view is a compact 3-column table (Alias, Display Name, Version)
/// showing only the loaded version per workload. <c>--verbose</c> adds the
/// Package ID and Description columns. <c>--all-versions</c> switches to a
/// grouped layout that lists every installed side-by-side version and marks
/// the loaded one.
/// </summary>
internal sealed class WorkloadListCommand : FuncCliCommand
{
    private const int DescriptionMaxWidth = 60;
    private const string LoadedMarker = "(loaded)";

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

    public WorkloadListCommand(IInteractionService interaction, IWorkloadProvider workloads, IWorkloadStore store)
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
        bool verbose = IsVerbose(parseResult);

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

        if (allVersions)
        {
            RenderGroupedView(rows, verbose);
        }
        else
        {
            RenderLoadedTable(rows, verbose);
        }

        RenderSummary(rows, allVersions);
        return 0;
    }

    private void RenderLoadedTable(IReadOnlyList<ListRow> rows, bool verbose)
    {
        IEnumerable<ListRow> sorted = rows.OrderBy(r => r.DisplayName, StringComparer.OrdinalIgnoreCase);

        if (verbose)
        {
            _interaction.WriteTable(
                ["Alias", "Display Name", "Version", "Package ID", "Description"],
                sorted.Select(r => new[]
                {
                    PrimaryAlias(r),
                    DisplayNameOrPackageId(r),
                    r.PackageVersion,
                    r.PackageId,
                    Truncate(r.Description, DescriptionMaxWidth),
                }));
            return;
        }

        _interaction.WriteTable(
            ["Alias", "Display Name", "Version"],
            sorted.Select(r => new[]
            {
                PrimaryAlias(r),
                DisplayNameOrPackageId(r),
                r.PackageVersion,
            }));
    }

    private void RenderGroupedView(IReadOnlyList<ListRow> rows, bool verbose)
    {
        IEnumerable<IGrouping<string, ListRow>> groups = rows
            .GroupBy(r => r.PackageId, StringComparer.OrdinalIgnoreCase)
            .OrderBy(GroupDisplayName, StringComparer.OrdinalIgnoreCase);

        bool first = true;
        foreach (IGrouping<string, ListRow> group in groups)
        {
            if (!first)
            {
                _interaction.WriteBlankLine();
            }

            first = false;

            ListRow header = group.First();
            string alias = PrimaryAlias(header);
            string displayName = DisplayNameOrPackageId(header);

            _interaction.WriteLine(line =>
            {
                line.Heading(displayName);
                if (!string.IsNullOrEmpty(alias))
                {
                    line.Plain(" ").Muted($"({alias})");
                }
            });

            if (verbose)
            {
                _interaction.WriteLine(line => line.Muted("  ").Muted(header.PackageId));
                if (!string.IsNullOrWhiteSpace(header.Description))
                {
                    _interaction.WriteLine(line => line.Muted("  ").Muted(Truncate(header.Description, DescriptionMaxWidth)));
                }
            }

            foreach (ListRow entry in group.OrderByDescending(r => ParseVersion(r.PackageVersion)))
            {
                _interaction.WriteLine(line =>
                {
                    line.Plain("  ").Command(entry.PackageVersion);
                    if (entry.Loaded == true)
                    {
                        line.Plain("  ").Success(LoadedMarker);
                    }
                });
            }
        }
    }

    private void RenderSummary(IReadOnlyList<ListRow> rows, bool allVersions)
    {
        _interaction.WriteBlankLine();

        if (allVersions)
        {
            int workloadCount = rows
                .Select(r => r.PackageId)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count();
            _interaction.WriteHint(
                $"{workloadCount} {Plural(workloadCount, "workload")}, " +
                $"{rows.Count} {Plural(rows.Count, "version")} installed.");
            return;
        }

        _interaction.WriteHint($"{rows.Count} {Plural(rows.Count, "workload")} installed.");
    }

    private IReadOnlyList<ListRow> BuildLoadedRows()
    {
        IReadOnlyList<WorkloadInfo> workloads = _workloads.GetWorkloads();
        return [.. workloads.Select(w => new ListRow(
            w.PackageId,
            w.PackageVersion,
            w.Aliases,
            w.DisplayName,
            w.Description,
            Loaded: null))];
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
                bool loaded = loadedByKey.TryGetValue((e.PackageId, e.PackageVersion), out RuntimeWorkloadInfo? info);
                return new ListRow(
                    e.PackageId,
                    e.PackageVersion,
                    e.Aliases ?? [],
                    info?.DisplayName ?? GetDisplayName(e),
                    info?.Description ?? (e.Description ?? string.Empty),
                    Loaded: loaded);
            })];
    }

    private static bool IsVerbose(ParseResult parseResult)
    {
        // Walk from the invoked command up to the root looking for a bool
        // option named "--verbose". The CLI's global verbose flag lives on
        // FuncRootCommand and is recursive, so it surfaces here; tests that
        // construct the command under a bare RootCommand without that option
        // simply see the default of false.
        Command? current = parseResult.CommandResult.Command;
        while (current is not null)
        {
            Option<bool>? verbose = current.Options
                .OfType<Option<bool>>()
                .FirstOrDefault(o => string.Equals(o.Name, "--verbose", StringComparison.Ordinal));
            if (verbose is not null)
            {
                return parseResult.GetValue(verbose);
            }

            current = current.Parents.OfType<Command>().FirstOrDefault();
        }

        return false;
    }

    private static string PrimaryAlias(ListRow row)
        => row.Aliases.Count == 0 ? string.Empty : row.Aliases[0];

    private static string DisplayNameOrPackageId(ListRow row)
        => string.IsNullOrWhiteSpace(row.DisplayName) ? row.PackageId : row.DisplayName;

    private static string GetDisplayName(WorkloadEntry entry)
        => string.IsNullOrWhiteSpace(entry.DisplayName) ? entry.PackageId : entry.DisplayName;

    private static string GroupDisplayName(IGrouping<string, ListRow> group)
    {
        ListRow representative = group.FirstOrDefault(r => !string.IsNullOrWhiteSpace(r.DisplayName)) ?? group.First();
        return DisplayNameOrPackageId(representative);
    }

    private static string Truncate(string value, int max)
    {
        if (string.IsNullOrEmpty(value) || value.Length <= max)
        {
            return value ?? string.Empty;
        }

        return value[..(max - 1)] + "\u2026";
    }

    private static string Plural(int count, string singular)
        => count == 1 ? singular : singular + "s";

    private static NuGetVersion ParseVersion(string raw) =>
        NuGetVersion.TryParse(raw, out NuGetVersion? v) ? v : new NuGetVersion(0, 0, 0);

    /// <summary>
    /// Row projection used for both table/grouped rendering and JSON output.
    /// Property names are camelCased by the JSON serializer. <c>Loaded</c>
    /// is only emitted by <c>--all-versions</c>; the loaded view omits it.
    /// </summary>
    internal sealed record ListRow(
        string PackageId,
        string PackageVersion,
        IReadOnlyList<string> Aliases,
        string DisplayName,
        string Description,
        bool? Loaded);

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
