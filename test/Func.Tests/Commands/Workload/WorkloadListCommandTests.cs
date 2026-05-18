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
    public async Task SingleEntry_WritesTableWithRow()
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
        Assert.Contains("TABLE: [ID, Aliases, Display Name, Description, Version]", _interaction.Lines);
        Assert.Contains(
            "  ROW: [Azure.Functions.Cli.Workloads.Dotnet, dotnet, dotnet-isolated, .NET, C# / F# workload., 1.0.0]",
            _interaction.Lines);
    }

    [Fact]
    public async Task MissingAliases_RendersDashPlaceholder()
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
        Assert.Contains(", -, ", rowLine);
        Assert.DoesNotContain("\u2014", rowLine);
    }

    [Fact]
    public async Task MultipleEntries_WritesOneRowEach()
    {
        var workloads = new[]
        {
            NewInfo(new FakeWorkload(), "Pkg.A", "1.0.0", []),
            NewInfo(new FakeWorkload(), "Pkg.B", "2.0.0", []),
            NewInfo(new FakeWorkload(), "Pkg.A", "1.1.0", []),
        };

        var cmd = new WorkloadListCommand(_interaction, Provider(workloads), Substitute.For<IWorkloadStore>());
        await InvokeAsync(cmd);

        var rows = _interaction.Lines.Where(l => l.StartsWith("  ROW:")).ToList();
        Assert.Equal(3, rows.Count);
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
    }

    [Fact]
    public async Task List_AllVersions_PullsFromStoreAndSortsDescending()
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
            new global::Azure.Functions.Cli.Workloads.Storage.WorkloadEntry
            {
                PackageId = "Pkg.A",
                PackageVersion = "1.0.0",
                Aliases = ["a"],
            },
            new global::Azure.Functions.Cli.Workloads.Storage.WorkloadEntry
            {
                PackageId = "Pkg.A",
                PackageVersion = "2.0.0",
                Aliases = ["a"],
            },
        };

        var store = Substitute.For<global::Azure.Functions.Cli.Workloads.Storage.IWorkloadStore>();
        store.GetWorkloadsAsync(Arg.Any<CancellationToken>()).Returns(entries);

        var cmd = new WorkloadListCommand(_interaction, Provider(loaded), store);
        int exit = await InvokeAsync(cmd, "--all-versions");

        Assert.Equal(0, exit);
        var rows = _interaction.Lines.Where(l => l.StartsWith("  ROW:")).ToList();
        Assert.Equal(2, rows.Count);
        // Highest version first; loaded info populates DisplayName/Description.
        Assert.Contains("2.0.0", rows[0]);
        Assert.Contains(".NET", rows[0]);
        Assert.Contains("1.0.0", rows[1]);
        // Older side-by-side has no loaded info; placeholder used.
        Assert.Contains(", -, -, ", rows[1]);
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
        return provider;
    }

    private static WorkloadInfo NewInfo(
        FakeWorkload instance,
        string packageId,
        string packageVersion,
        IReadOnlyList<string> aliases)
        => new(instance, packageId, packageVersion, aliases, instance.DisplayName, instance.Description);

    private static Task<int> InvokeAsync(WorkloadListCommand cmd, params string[] args)
    {
        var root = new RootCommand();
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
