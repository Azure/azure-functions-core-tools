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
/// showing only the loaded version per workload. <c>--verbose</c> switches
/// to the same definition-list "card" layout as <c>func workload search</c>
/// so package id and full description get their own lines. <c>--all-versions</c>
/// switches to a grouped layout that lists every installed side-by-side
/// version and marks the loaded one.
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
            : await BuildLoadedRowsAsync(cancellationToken);

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
        else if (verbose)
        {
            RenderLoadedCards(rows);
        }
        else
        {
            RenderLoadedTable(rows);
        }

        RenderSummary(rows, allVersions);
        return 0;
    }

    private void RenderLoadedTable(IReadOnlyList<ListRow> rows)
    {
        IEnumerable<ListRow> sorted = rows.OrderBy(r => r.DisplayName, StringComparer.OrdinalIgnoreCase);

        _interaction.WriteTable(
            ["Alias", "Display Name", "Version"],
            sorted.Select(r => new[]
            {
                PrimaryAlias(r),
                DisplayNameOrPackageId(r),
                r.PackageVersion,
            }));
    }

    private void RenderLoadedCards(IReadOnlyList<ListRow> rows)
    {
        var card = new WorkloadCardWriter(_interaction);
        bool first = true;
        foreach (ListRow row in rows.OrderBy(r => r.DisplayName, StringComparer.OrdinalIgnoreCase))
        {
            if (!first)
            {
                card.WriteSeparator();
            }

            first = false;

            card.WriteHeading(DisplayNameOrPackageId(row));
            card.WriteField("Version", row.PackageVersion);
            card.WriteField("Package ID", row.PackageId);
            card.WriteField("Implementation", $"{row.PhysicalPackageId} {row.PhysicalPackageVersion}");
            if (!string.IsNullOrWhiteSpace(row.RuntimeIdentifier))
            {
                card.WriteField("Runtime Identifier", row.RuntimeIdentifier);
            }

            card.WriteField("Ownership", row.Ownership);
            card.WriteAliases(row.Aliases);
            card.WriteDescription(row.Description);
        }
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

    private async Task<IReadOnlyList<ListRow>> BuildLoadedRowsAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<WorkloadInfo> workloads = _workloads.GetWorkloads();
        IReadOnlyList<WorkloadEntry>? installed = await _store.GetWorkloadsAsync(cancellationToken);
        if (installed is null || installed.Count == 0)
        {
            return [.. workloads.Select(CreateLegacyRow)];
        }

        HashSet<(string PackageId, string Version)> loadedKeys = new(
            workloads.Select(w => (w.PackageId, w.PackageVersion)),
            new PackageVersionKeyComparer());
        Dictionary<(string PackageId, string Version), WorkloadInfo> loadedByKey = workloads.ToDictionary(
            w => (w.PackageId, w.PackageVersion),
            w => w,
            new PackageVersionKeyComparer());
        return [.. installed
            .Where(e => loadedKeys.Contains((e.PackageId, e.PackageVersion)))
            .Select(e => CreateRow(e, loaded: null, loadedByKey[(e.PackageId, e.PackageVersion)]))];
    }

    private async Task<IReadOnlyList<ListRow>> BuildAllVersionsRowsAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<WorkloadEntry>? installed = await _store.GetWorkloadsAsync(cancellationToken);
        if (installed is null || installed.Count == 0)
        {
            return [.. _workloads.GetWorkloads().Select(CreateLegacyRow)];
        }

        Dictionary<(string PackageId, string Version), WorkloadInfo> loadedByKey = _workloads
            .GetWorkloads()
            .ToDictionary(
                w => (w.PackageId, w.PackageVersion),
                w => w,
                new PackageVersionKeyComparer());

        return [.. installed
            .Select(e =>
            {
                bool loaded = loadedByKey.TryGetValue((e.PackageId, e.PackageVersion), out WorkloadInfo? info);
                return CreateRow(e, loaded, info);
            })
            .OrderBy(e => e.PackageId, StringComparer.OrdinalIgnoreCase)
            .ThenByDescending(e => ParseVersion(e.PackageVersion))];
    }

    private static ListRow CreateRow(WorkloadEntry entry, bool? loaded, WorkloadInfo? loadedInfo)
    {
        LogicalPackage? logical = entry.LogicalPackage;
        return new ListRow(
            logical?.PackageId ?? entry.PackageId,
            logical?.PackageVersion ?? entry.PackageVersion,
            logical?.Aliases ?? entry.Aliases,
            logical?.DisplayName ?? loadedInfo?.DisplayName ?? GetDisplayName(entry),
            logical?.Description ?? loadedInfo?.Description ?? entry.Description,
            entry.PackageId,
            entry.PackageVersion,
            entry.RuntimeIdentifier,
            entry.IsExplicitlyInstalled,
            entry.InstallRefCount,
            DescribeOwnership(entry),
            loaded);
    }

    private static ListRow CreateLegacyRow(WorkloadInfo workload)
        => new(
            workload.PackageId,
            workload.PackageVersion,
            workload.Aliases,
            workload.DisplayName,
            workload.Description,
            workload.PackageId,
            workload.PackageVersion,
            RuntimeIdentifier: null,
            IsExplicitlyInstalled: true,
            InstallRefCount: 1,
            Ownership: "explicit",
            Loaded: null);

    private static string DescribeOwnership(WorkloadEntry entry)
    {
        List<string> owners = [];
        if (entry.IsExplicitlyInstalled)
        {
            owners.Add("explicit");
        }

        if (entry.LogicalPackage is not null)
        {
            owners.Add($"logical:{entry.LogicalPackage.PackageId}");
        }

        int knownOwnerCount = (entry.IsExplicitlyInstalled ? 1 : 0) + (entry.LogicalPackage is null ? 0 : 1);
        int metaOwnerCount = Math.Max(0, entry.InstallRefCount - knownOwnerCount);
        if (metaOwnerCount > 0)
        {
            owners.Add($"meta:{metaOwnerCount}");
        }

        return string.Join(", ", owners);
    }

    private static string PrimaryAlias(ListRow row)
        => row.Aliases.Count == 0 ? string.Empty : row.Aliases[0];

    private static string DisplayNameOrPackageId(ListRow row)
        => string.IsNullOrWhiteSpace(row.DisplayName) ? row.PackageId : row.DisplayName;

    private static string GetDisplayName(WorkloadEntry entry)
        => string.IsNullOrWhiteSpace(entry.DisplayName) ? entry.LogicalPackage?.PackageId ?? entry.PackageId : entry.DisplayName;

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
        string PhysicalPackageId,
        string PhysicalPackageVersion,
        string? RuntimeIdentifier,
        bool IsExplicitlyInstalled,
        int InstallRefCount,
        string Ownership,
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
