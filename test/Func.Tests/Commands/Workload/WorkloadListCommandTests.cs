// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.CommandLine;
using Azure.Functions.Cli.Commands.Workload;
using Azure.Functions.Cli.Workloads;
using NSubstitute;
using Xunit;

namespace Azure.Functions.Cli.Tests.Commands.Workload;

public class WorkloadListCommandTests
{
    private readonly TestInteractionService _interaction = new();

    [Fact]
    public async Task EmptyList_WritesNoWorkloadsHint_ReturnsZero()
    {
        var cmd = new WorkloadListCommand(_interaction, ProviderReturning(Array.Empty<WorkloadInfo>()));

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
            new WorkloadInfo(
                Instance: new FakeWorkload(displayName: ".NET", description: "C# / F# workload."),
                PackageId: "Azure.Functions.Cli.Workload.Dotnet",
                PackageVersion: "1.0.0",
                Aliases: new[] { "dotnet", "dotnet-isolated" }),
        };

        var cmd = new WorkloadListCommand(_interaction, ProviderReturning(workloads));
        int exit = await InvokeAsync(cmd);

        Assert.Equal(0, exit);
        Assert.Contains("TABLE: [Package, Aliases, Name, Description, Version]", _interaction.Lines);
        Assert.Contains(
            "  ROW: [Azure.Functions.Cli.Workload.Dotnet, dotnet, dotnet-isolated, .NET, C# / F# workload., 1.0.0]",
            _interaction.Lines);
    }

    [Fact]
    public async Task MissingAliases_RendersDashPlaceholder()
    {
        var workloads = new[]
        {
            new WorkloadInfo(
                Instance: new FakeWorkload(),
                PackageId: "Azure.Functions.Cli.Workload.Custom",
                PackageVersion: "0.1.0",
                Aliases: Array.Empty<string>()),
        };

        var cmd = new WorkloadListCommand(_interaction, ProviderReturning(workloads));
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
            new WorkloadInfo(new FakeWorkload(), "Pkg.A", "1.0.0", Array.Empty<string>()),
            new WorkloadInfo(new FakeWorkload(), "Pkg.B", "2.0.0", Array.Empty<string>()),
            new WorkloadInfo(new FakeWorkload(), "Pkg.A", "1.1.0", Array.Empty<string>()),
        };

        var cmd = new WorkloadListCommand(_interaction, ProviderReturning(workloads));
        await InvokeAsync(cmd);

        var rows = _interaction.Lines.Where(l => l.StartsWith("  ROW:")).ToList();
        Assert.Equal(3, rows.Count);
    }

    private static IWorkloadProvider ProviderReturning(IReadOnlyList<WorkloadInfo> workloads)
    {
        IWorkloadProvider provider = Substitute.For<IWorkloadProvider>();
        provider.GetWorkloadsAsync(Arg.Any<CancellationToken>())
            .Returns(new ValueTask<IReadOnlyList<WorkloadInfo>>(workloads));
        return provider;
    }

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
        public override string Name => "Fake";

        public override string Version => "0.0.0";

        public override string DisplayName { get; } = displayName;

        public override string Description { get; } = description;

        public override void Configure(FunctionsCliBuilder builder)
        {
        }
    }
}
