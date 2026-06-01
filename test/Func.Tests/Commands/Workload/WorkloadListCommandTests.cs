// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.CommandLine;
using Azure.Functions.Cli.Commands.Workload;
using Azure.Functions.Cli.Workloads;
using Azure.Functions.Cli.Workloads.Storage;
using NSubstitute;
using Xunit;

namespace Azure.Functions.Cli.Tests.Commands.Workload;

public class WorkloadListCommandTests
{
    private readonly TestInteractionService _interaction = new();

    [Fact]
    public async Task EmptyList_WritesNoWorkloadsHint_ReturnsZero()
    {
        var cmd = new WorkloadListCommand(_interaction, Provider(), Substitute.For<IWorkloadStore>());
        int exit = await InvokeAsync(cmd);

        Assert.Equal(0, exit);
        Assert.Contains("HINT: No workloads installed.", _interaction.Lines);
        Assert.DoesNotContain(_interaction.Lines, l => l.StartsWith("TABLE:"));
    }

    [Fact]
    public async Task DefaultView_RendersCompactThreeColumnTable()
    {
        var workloads = new[]
        {
            NewInfo(
                instance: new FakeWorkload(displayName: ".NET", description: "C# / F# workload."),
                packageId: "Azure.Functions.Cli.Workloads.Dotnet",
                packageVersion: "1.0.0",
                aliases: ["dotnet", "dotnet-isolated"]),
        };

        var cmd = new WorkloadListCommand(_interaction, Provider(workloads), Substitute.For<IWorkloadStore>());
        int exit = await InvokeAsync(cmd);

        Assert.Equal(0, exit);
        Assert.Contains("TABLE: [Alias, Display Name, Version]", _interaction.Lines);
        Assert.Contains("  ROW: [dotnet, .NET, 1.0.0]", _interaction.Lines);
    }

    [Fact]
    public async Task DefaultView_SortsByDisplayName()
    {
        var workloads = new[]
        {
            NewInfo(new FakeWorkload(displayName: "Python"), "Pkg.Python", "1.0.0", ["python"]),
            NewInfo(new FakeWorkload(displayName: "Node.js"), "Pkg.Node", "1.0.0", ["node"]),
            NewInfo(new FakeWorkload(displayName: ".NET"), "Pkg.Dotnet", "1.0.0", ["dotnet"]),
        };

        var cmd = new WorkloadListCommand(_interaction, Provider(workloads), Substitute.For<IWorkloadStore>());
        await InvokeAsync(cmd);

        var rows = _interaction.Lines.Where(l => l.StartsWith("  ROW:")).ToList();
        Assert.Equal(3, rows.Count);
        Assert.Contains(".NET", rows[0]);
        Assert.Contains("Node.js", rows[1]);
        Assert.Contains("Python", rows[2]);
    }

    [Fact]
    public async Task MissingAliases_RendersEmptyAliasCell()
    {
        var workloads = new[]
        {
            NewInfo(
                instance: new FakeWorkload(),
                packageId: "Azure.Functions.Cli.Workloads.Custom",
                packageVersion: "0.1.0",
                aliases: []),
        };

        var cmd = new WorkloadListCommand(_interaction, Provider(workloads), Substitute.For<IWorkloadStore>());
        await InvokeAsync(cmd);

        string rowLine = _interaction.Lines.Single(l => l.StartsWith("  ROW:"));
        Assert.StartsWith("  ROW: [, ", rowLine);
        Assert.DoesNotContain(", -, ", rowLine);
    }

    [Fact]
    public async Task MissingDisplayName_FallsBackToPackageId()
    {
        WorkloadInfo[] workloads =
        [
            TestWorkloads.CreateContentInfo(
                packageId: "Azure.Functions.Cli.Workloads.Host",
                version: "4.0.0"),
        ];

        var cmd = new WorkloadListCommand(_interaction, Provider(workloads), Substitute.For<IWorkloadStore>());
        int exit = await InvokeAsync(cmd);

        Assert.Equal(0, exit);
        Assert.Contains(
            "  ROW: [, Azure.Functions.Cli.Workloads.Host, 4.0.0]",
            _interaction.Lines);
    }

    [Fact]
    public async Task Verbose_RendersCardWithFields()
    {
        var workloads = new[]
        {
            NewInfo(
                instance: new FakeWorkload(displayName: ".NET Development Stack", description: "C# / F# workload."),
                packageId: "Azure.Functions.Cli.Workloads.Dotnet",
                packageVersion: "1.0.0",
                aliases: ["dotnet"]),
        };

        var cmd = new WorkloadListCommand(_interaction, Provider(workloads), Substitute.For<IWorkloadStore>());
        int exit = await InvokeAsync(cmd, includeRootVerbose: true, "--verbose");

        Assert.Equal(0, exit);
        Assert.DoesNotContain(_interaction.Lines, l => l.StartsWith("TABLE:"));
        Assert.Contains(".NET Development Stack", _interaction.Lines);
        Assert.Contains(_interaction.Lines, l => l.StartsWith("Version:") && l.EndsWith("1.0.0"));
        Assert.Contains(_interaction.Lines, l => l.StartsWith("Package ID:") && l.EndsWith("Azure.Functions.Cli.Workloads.Dotnet"));
        Assert.Contains(_interaction.Lines, l => l.StartsWith("Alias:") && l.EndsWith("dotnet"));
        Assert.Contains(_interaction.Lines, l => l.StartsWith("Description:") && l.EndsWith("C# / F# workload."));
    }

    [Fact]
    public async Task Verbose_RendersFullDescriptionOnItsOwnLine()
    {
        string longDescription = new string('x', 200);
        var workloads = new[]
        {
            NewInfo(
                instance: new FakeWorkload(displayName: "Long", description: longDescription),
                packageId: "Pkg.Long",
                packageVersion: "1.0.0",
                aliases: ["long"]),
        };

        var cmd = new WorkloadListCommand(_interaction, Provider(workloads), Substitute.For<IWorkloadStore>());
        await InvokeAsync(cmd, includeRootVerbose: true, "--verbose");

        // Card now puts description inline next to its label, so the full
        // string lands at the end of that line verbatim.
        Assert.Contains(_interaction.Lines, l => l.StartsWith("Description:") && l.EndsWith(longDescription));
    }

    [Fact]
    public async Task Verbose_MultipleAliases_UsesPluralLabel()
    {
        var workloads = new[]
        {
            NewInfo(
                instance: new FakeWorkload(displayName: ".NET Development Stack"),
                packageId: "Pkg.Dotnet",
                packageVersion: "1.0.0",
                aliases: ["dotnet", "dotnet-isolated"]),
        };

        var cmd = new WorkloadListCommand(_interaction, Provider(workloads), Substitute.For<IWorkloadStore>());
        await InvokeAsync(cmd, includeRootVerbose: true, "--verbose");

        Assert.Contains(
            _interaction.Lines,
            l => l.StartsWith("Aliases:") && l.EndsWith("dotnet, dotnet-isolated"));
    }

    [Fact]
    public async Task DefaultView_WritesSummaryFooter()
    {
        var workloads = new[]
        {
            NewInfo(new FakeWorkload(displayName: "A"), "Pkg.A", "1.0.0", ["a"]),
            NewInfo(new FakeWorkload(displayName: "B"), "Pkg.B", "1.0.0", ["b"]),
        };

        var cmd = new WorkloadListCommand(_interaction, Provider(workloads), Substitute.For<IWorkloadStore>());
        await InvokeAsync(cmd);

        Assert.Contains("HINT: 2 workloads installed.", _interaction.Lines);
    }

    [Fact]
    public async Task SingleEntry_SummaryUsesSingular()
    {
        var workloads = new[]
        {
            NewInfo(new FakeWorkload(displayName: "Only"), "Pkg.Only", "1.0.0", ["only"]),
        };

        var cmd = new WorkloadListCommand(_interaction, Provider(workloads), Substitute.For<IWorkloadStore>());
        await InvokeAsync(cmd);

        Assert.Contains("HINT: 1 workload installed.", _interaction.Lines);
    }

    [Fact]
    public async Task List_Json_EmitsLoadedRowsAsJson()
    {
        var workloads = new[]
        {
            NewInfo(
                instance: new FakeWorkload(displayName: ".NET", description: "C#"),
                packageId: "Azure.Functions.Cli.Workloads.Dotnet",
                packageVersion: "1.0.0",
                aliases: ["dotnet"]),
        };

        var cmd = new WorkloadListCommand(_interaction, Provider(workloads), Substitute.For<IWorkloadStore>());
        int exit = await InvokeAsync(cmd, "--json");

        Assert.Equal(0, exit);
        Assert.DoesNotContain(_interaction.Lines, l => l.StartsWith("TABLE:"));
        string jsonLine = _interaction.Lines.Single(l => l.StartsWith("JSON:"));
        Assert.Contains("\"packageId\":\"Azure.Functions.Cli.Workloads.Dotnet\"", jsonLine);
        Assert.Contains("\"packageVersion\":\"1.0.0\"", jsonLine);
        // Loaded view omits the Loaded flag (null on the row, so JSON drops it).
        Assert.DoesNotContain("\"loaded\"", jsonLine);
    }

    [Fact]
    public async Task List_AllVersions_RendersGroupedOutputWithLoadedMarker()
    {
        var loaded = new[]
        {
            NewInfo(
                instance: new FakeWorkload(displayName: ".NET", description: "C#"),
                packageId: "Pkg.A",
                packageVersion: "2.0.0",
                aliases: ["a"]),
        };

        var entries = new[]
        {
            new WorkloadEntry
            {
                PackageId = "Pkg.A",
                PackageVersion = "1.0.0",
                Aliases = ["a"],
            },
            new WorkloadEntry
            {
                PackageId = "Pkg.A",
                PackageVersion = "2.0.0",
                Aliases = ["a"],
            },
        };

        var store = Substitute.For<IWorkloadStore>();
        store.GetWorkloadsAsync(Arg.Any<CancellationToken>()).Returns(entries);

        var cmd = new WorkloadListCommand(_interaction, Provider(loaded), store);
        int exit = await InvokeAsync(cmd, "--all-versions");

        Assert.Equal(0, exit);
        Assert.DoesNotContain(_interaction.Lines, l => l.StartsWith("TABLE:"));
        // Header shows display name + alias in parentheses.
        Assert.Contains(".NET (a)", _interaction.Lines);
        // Highest version first, marked as loaded.
        int loadedIndex = _interaction.Lines.ToList().FindIndex(l => l.Contains("2.0.0"));
        int olderIndex = _interaction.Lines.ToList().FindIndex(l => l.Contains("1.0.0"));
        Assert.True(loadedIndex < olderIndex);
        Assert.Contains("(loaded)", _interaction.Lines[loadedIndex]);
        Assert.DoesNotContain("(loaded)", _interaction.Lines[olderIndex]);
        Assert.Contains("HINT: 1 workload, 2 versions installed.", _interaction.Lines);
    }

    [Fact]
    public async Task List_AllVersions_Json_IncludesLoadedFlag()
    {
        var loaded = new[]
        {
            NewInfo(new FakeWorkload(displayName: ".NET"), "Pkg.A", "2.0.0", ["a"]),
        };

        var entries = new[]
        {
            new WorkloadEntry { PackageId = "Pkg.A", PackageVersion = "1.0.0", Aliases = ["a"] },
            new WorkloadEntry { PackageId = "Pkg.A", PackageVersion = "2.0.0", Aliases = ["a"] },
        };

        var store = Substitute.For<IWorkloadStore>();
        store.GetWorkloadsAsync(Arg.Any<CancellationToken>()).Returns(entries);

        var cmd = new WorkloadListCommand(_interaction, Provider(loaded), store);
        await InvokeAsync(cmd, "--all-versions", "--json");

        string jsonLine = _interaction.Lines.Single(l => l.StartsWith("JSON:"));
        Assert.Contains("\"loaded\":true", jsonLine);
        Assert.Contains("\"loaded\":false", jsonLine);
    }

    [Fact]
    public async Task List_HasJsonAndAllVersionsOptions()
    {
        var cmd = new WorkloadListCommand(_interaction, Provider(), Substitute.For<IWorkloadStore>());
        Assert.Contains(cmd.Options, o => o.Name == "--json");
        Assert.Contains(cmd.Options, o => o.Name == "--all-versions");
        await Task.CompletedTask;
    }

    private static IWorkloadProvider Provider(params WorkloadInfo[] workloads)
    {
        var provider = Substitute.For<IWorkloadProvider>();
        provider.GetWorkloads().Returns(workloads);
        provider.GetRuntimeWorkloads().Returns([.. workloads.OfType<RuntimeWorkloadInfo>()]);
        provider.GetRuntimeWorkloadsByPackageId(Arg.Any<string>())
            .Returns(callInfo => GetWorkloadsByPackageId<RuntimeWorkloadInfo>(workloads, callInfo.Arg<string>()));
        provider.GetContentWorkloads().Returns([.. workloads.OfType<ContentWorkloadInfo>()]);
        provider.GetContentWorkloadsByPackageId(Arg.Any<string>())
            .Returns(callInfo => GetWorkloadsByPackageId<ContentWorkloadInfo>(workloads, callInfo.Arg<string>()));
        return provider;
    }

    private static IReadOnlyList<TWorkload> GetWorkloadsByPackageId<TWorkload>(IReadOnlyList<WorkloadInfo> workloads, string packageId)
        where TWorkload : WorkloadInfo
        => [.. workloads
            .OfType<TWorkload>()
            .Where(w => string.Equals(w.PackageId, packageId, StringComparison.OrdinalIgnoreCase))];

    private static RuntimeWorkloadInfo NewInfo(
        FakeWorkload instance,
        string packageId,
        string packageVersion,
        IReadOnlyList<string> aliases)
        => new(
            instance,
            packageId,
            packageVersion,
            aliases,
            AppContext.BaseDirectory,
            AppContext.BaseDirectory,
            instance.DisplayName,
            instance.Description);

    private static Task<int> InvokeAsync(WorkloadListCommand cmd, params string[] args)
        => InvokeAsync(cmd, includeRootVerbose: false, args);

    private static Task<int> InvokeAsync(WorkloadListCommand cmd, bool includeRootVerbose, params string[] args)
    {
        var root = new RootCommand();
        if (includeRootVerbose)
        {
            // Mirror FuncRootCommand's recursive --verbose so the list
            // command can pick it up via parent traversal.
            root.Options.Add(new Option<bool>("--verbose") { Recursive = true });
        }

        root.Subcommands.Add(cmd);
        return root.Parse(new[] { cmd.Name }.Concat(args).ToArray()).InvokeAsync();
    }

    private sealed class FakeWorkload(
        string displayName = "Test Workload",
        string description = "A workload for tests.") : global::Azure.Functions.Cli.Workloads.Workload
    {
        public override string DisplayName { get; } = displayName;

        public override string Description { get; } = description;

        public override void Configure(FunctionsCliBuilder builder)
        {
        }
    }
}
