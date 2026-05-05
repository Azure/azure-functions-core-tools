// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.CommandLine;
using Azure.Functions.Cli.Commands.Workload;
using Azure.Functions.Cli.Workloads;
using Azure.Functions.Cli.Workloads.Loading;
using Azure.Functions.Cli.Workloads.Storage;
using NSubstitute;
using Xunit;

namespace Azure.Functions.Cli.Tests.Commands.Workload;

public class WorkloadListCommandTests
{
    private readonly TestInteractionService _interaction = new();
    private readonly IWorkloadStore _store = Substitute.For<IWorkloadStore>();
    private readonly IWorkloadLoader _loader = Substitute.For<IWorkloadLoader>();

    [Fact]
    public async Task EmptyRegistry_WritesNoWorkloadsHint_ReturnsZero()
    {
        _store.GetWorkloadsAsync(Arg.Any<CancellationToken>())
            .Returns(Array.Empty<WorkloadEntry>());

        var cmd = new WorkloadListCommand(_interaction, _store, _loader);
        var exit = await InvokeAsync(cmd);

        Assert.Equal(0, exit);
        Assert.Contains("HINT: No workloads installed.", _interaction.Lines);
        Assert.DoesNotContain(_interaction.Lines, l => l.StartsWith("TABLE:"));
        _loader.DidNotReceive().Load(Arg.Any<IReadOnlyList<WorkloadEntry>>());
    }

    [Fact]
    public async Task SingleEntry_WritesTableWithRow()
    {
        var entry = NewEntry(
            packageId: "Azure.Functions.Cli.Workload.Dotnet",
            version: "1.0.0",
            aliases: new[] { "dotnet", "dotnet-isolated" });
        _store.GetWorkloadsAsync(Arg.Any<CancellationToken>())
            .Returns(new[] { entry });
        _loader.Load(Arg.Any<IReadOnlyList<WorkloadEntry>>())
            .Returns(new[]
            {
                new WorkloadInfo(
                    Instance: new FakeWorkload(displayName: ".NET", description: "C# / F# workload."),
                    PackageId: entry.PackageId,
                    PackageVersion: entry.PackageVersion,
                    Aliases: entry.Aliases),
            });

        var cmd = new WorkloadListCommand(_interaction, _store, _loader);
        var exit = await InvokeAsync(cmd);

        Assert.Equal(0, exit);
        Assert.Contains("TABLE: [Package, Aliases, Name, Description, Version]", _interaction.Lines);
        Assert.Contains(
            "  ROW: [Azure.Functions.Cli.Workload.Dotnet, dotnet, dotnet-isolated, .NET, C# / F# workload., 1.0.0]",
            _interaction.Lines);
    }

    [Fact]
    public async Task MissingAliases_RendersDashPlaceholder()
    {
        var entry = NewEntry(
            packageId: "Azure.Functions.Cli.Workload.Custom",
            version: "0.1.0",
            aliases: Array.Empty<string>());
        _store.GetWorkloadsAsync(Arg.Any<CancellationToken>())
            .Returns(new[] { entry });
        _loader.Load(Arg.Any<IReadOnlyList<WorkloadEntry>>())
            .Returns(new[]
            {
                new WorkloadInfo(
                    Instance: new FakeWorkload(),
                    PackageId: entry.PackageId,
                    PackageVersion: entry.PackageVersion,
                    Aliases: entry.Aliases),
            });

        var cmd = new WorkloadListCommand(_interaction, _store, _loader);
        await InvokeAsync(cmd);

        var rowLine = _interaction.Lines.Single(l => l.StartsWith("  ROW:"));
        Assert.Contains(", -, ", rowLine);
        Assert.DoesNotContain("\u2014", rowLine);
    }

    [Fact]
    public async Task MultipleEntries_WritesOneRowEach()
    {
        var entries = new[]
        {
            NewEntry("Pkg.A", "1.0.0"),
            NewEntry("Pkg.B", "2.0.0"),
            NewEntry("Pkg.A", "1.1.0"),
        };
        _store.GetWorkloadsAsync(Arg.Any<CancellationToken>()).Returns(entries);
        _loader.Load(Arg.Any<IReadOnlyList<WorkloadEntry>>())
            .Returns(entries.Select(e => new WorkloadInfo(
                Instance: new FakeWorkload(),
                PackageId: e.PackageId,
                PackageVersion: e.PackageVersion,
                Aliases: e.Aliases)).ToArray());

        var cmd = new WorkloadListCommand(_interaction, _store, _loader);
        await InvokeAsync(cmd);

        var rows = _interaction.Lines.Where(l => l.StartsWith("  ROW:")).ToList();
        Assert.Equal(3, rows.Count);
    }

    private static Task<int> InvokeAsync(WorkloadListCommand cmd, params string[] args)
    {
        var root = new RootCommand();
        root.Subcommands.Add(cmd);
        return root.Parse(new[] { cmd.Name }.Concat(args).ToArray()).InvokeAsync();
    }

    private static WorkloadEntry NewEntry(
        string packageId,
        string version,
        IReadOnlyList<string>? aliases = null)
        => new()
        {
            PackageId = packageId,
            PackageVersion = version,
            Aliases = aliases ?? Array.Empty<string>(),
            EntryPoint = new EntryPointSpec { AssemblyPath = "test.dll", Type = "T" },
        };

    private sealed class FakeWorkload(
        string displayName = "Test Workload",
        string description = "A workload for tests.") : global::Azure.Functions.Cli.Workloads.Workload
    {
        public override string Name => "Fake";

        public override string Version => "0.0.0";

        public override string DisplayName { get; } = displayName;

        public override string Description { get; } = description;

        public override void Configure(FunctionsCliBuilder builder)
        {
        }
    }
}
