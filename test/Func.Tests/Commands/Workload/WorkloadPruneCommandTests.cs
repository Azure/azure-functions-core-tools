// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.CommandLine;
using Azure.Functions.Cli.Commands.Workload;
using Azure.Functions.Cli.Workloads.Install;
using Azure.Functions.Cli.Workloads.Storage;
using NSubstitute;
using Xunit;

namespace Azure.Functions.Cli.Tests.Commands.Workload;

public class WorkloadPruneCommandTests
{
    private readonly TestInteractionService _interaction = new();
    private readonly IWorkloadInstaller _installer = Substitute.For<IWorkloadInstaller>();
    private readonly IWorkloadStore _store = Substitute.For<IWorkloadStore>();

    [Fact]
    public void Prune_HasExpectedArgsAndOptions()
    {
        var cmd = new WorkloadPruneCommand(_interaction, _installer, _store);
        Assert.Single(cmd.Arguments, a => a.Name == "id");
        Assert.Contains(cmd.Options, o => o.Name == "--exact");
    }

    [Fact]
    public async Task Prune_EmptyStore_HintsAndReturnsZero()
    {
        _store.GetWorkloadsAsync(Arg.Any<CancellationToken>()).Returns(Array.Empty<WorkloadEntry>());

        var cmd = new WorkloadPruneCommand(_interaction, _installer, _store);
        int exit = await InvokeAsync(cmd);

        Assert.Equal(0, exit);
        Assert.Contains(_interaction.Lines, l => l.StartsWith("HINT:") && l.Contains("No workloads installed"));
        await _installer.DidNotReceive().UninstallAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Prune_SingleVersionPerPackage_NoOpHint()
    {
        _store.GetWorkloadsAsync(Arg.Any<CancellationToken>()).Returns(new[]
        {
            new WorkloadEntry { PackageId = "Pkg.A", PackageVersion = "1.0.0" },
            new WorkloadEntry { PackageId = "Pkg.B", PackageVersion = "2.0.0" },
        });

        var cmd = new WorkloadPruneCommand(_interaction, _installer, _store);
        int exit = await InvokeAsync(cmd);

        Assert.Equal(0, exit);
        Assert.Contains(_interaction.Lines, l => l.StartsWith("HINT:") && l.Contains("Nothing to prune"));
        await _installer.DidNotReceive().UninstallAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Prune_KeepsHighestVersion_RemovesOlderSideBySide()
    {
        _store.GetWorkloadsAsync(Arg.Any<CancellationToken>()).Returns(new[]
        {
            new WorkloadEntry { PackageId = "Pkg.A", PackageVersion = "1.0.0" },
            new WorkloadEntry { PackageId = "Pkg.A", PackageVersion = "2.0.0" },
            new WorkloadEntry { PackageId = "Pkg.A", PackageVersion = "1.5.0" },
        });
        _installer.UninstallAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(true);

        var cmd = new WorkloadPruneCommand(_interaction, _installer, _store);
        int exit = await InvokeAsync(cmd);

        Assert.Equal(0, exit);
        await _installer.Received(1).UninstallAsync("Pkg.A", "1.5.0", Arg.Any<CancellationToken>());
        await _installer.Received(1).UninstallAsync("Pkg.A", "1.0.0", Arg.Any<CancellationToken>());
        await _installer.DidNotReceive().UninstallAsync("Pkg.A", "2.0.0", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Prune_ScopedById_OnlyTouchesThatPackage()
    {
        _store.GetWorkloadsAsync(Arg.Any<CancellationToken>()).Returns(new[]
        {
            new WorkloadEntry { PackageId = "Pkg.A", PackageVersion = "1.0.0" },
            new WorkloadEntry { PackageId = "Pkg.A", PackageVersion = "2.0.0" },
            new WorkloadEntry { PackageId = "Pkg.B", PackageVersion = "1.0.0" },
            new WorkloadEntry { PackageId = "Pkg.B", PackageVersion = "2.0.0" },
        });
        _installer.UninstallAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(true);

        var cmd = new WorkloadPruneCommand(_interaction, _installer, _store);
        int exit = await InvokeAsync(cmd, "Pkg.A");

        Assert.Equal(0, exit);
        await _installer.Received(1).UninstallAsync("Pkg.A", "1.0.0", Arg.Any<CancellationToken>());
        await _installer.DidNotReceive().UninstallAsync("Pkg.B", Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Prune_ScopedByAlias_MatchesAlias()
    {
        _store.GetWorkloadsAsync(Arg.Any<CancellationToken>()).Returns(new[]
        {
            new WorkloadEntry { PackageId = "Pkg.A", PackageVersion = "1.0.0", Aliases = ["a"] },
            new WorkloadEntry { PackageId = "Pkg.A", PackageVersion = "2.0.0", Aliases = ["a"] },
        });
        _installer.UninstallAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(true);

        var cmd = new WorkloadPruneCommand(_interaction, _installer, _store);
        int exit = await InvokeAsync(cmd, "a");

        Assert.Equal(0, exit);
        await _installer.Received(1).UninstallAsync("Pkg.A", "1.0.0", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Prune_Exact_DoesNotMatchAlias()
    {
        _store.GetWorkloadsAsync(Arg.Any<CancellationToken>()).Returns(new[]
        {
            new WorkloadEntry { PackageId = "Pkg.A", PackageVersion = "1.0.0", Aliases = ["a"] },
            new WorkloadEntry { PackageId = "Pkg.A", PackageVersion = "2.0.0", Aliases = ["a"] },
        });

        var cmd = new WorkloadPruneCommand(_interaction, _installer, _store);
        int exit = await InvokeAsync(cmd, "a", "--exact");

        Assert.Equal(0, exit);
        Assert.Contains(_interaction.Lines, l => l.StartsWith("WARNING:") && l.Contains("'a' is not installed"));
        await _installer.DidNotReceive().UninstallAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Prune_WhitespaceArg_FailsValidation()
    {
        var cmd = new WorkloadPruneCommand(_interaction, _installer, _store);
        int exit = await InvokeAsync(cmd, "   ");

        Assert.NotEqual(0, exit);
        await _store.DidNotReceive().GetWorkloadsAsync(Arg.Any<CancellationToken>());
    }

    private static Task<int> InvokeAsync(WorkloadPruneCommand cmd, params string[] args)
    {
        var root = new RootCommand();
        root.Subcommands.Add(cmd);
        var config = new InvocationConfiguration { EnableDefaultExceptionHandler = false };
        return root.Parse(new[] { cmd.Name }.Concat(args).ToArray()).InvokeAsync(config);
    }
}
