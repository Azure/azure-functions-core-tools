// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.CommandLine;
using Azure.Functions.Cli.Commands.Workload;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Workloads.Install;
using Azure.Functions.Cli.Workloads.Storage;
using NSubstitute;

namespace Azure.Functions.Cli.Tests.Commands.Workload;

public class WorkloadPruneCommandTests
{
    private readonly TestInteractionService _interaction = new();
    private readonly IWorkloadInstaller _installer = Substitute.For<IWorkloadInstaller>();
    private readonly IWorkloadStore _store = Substitute.For<IWorkloadStore>();
    private readonly IWorkloadOperationCoordinator _coordinator = Substitute.For<IWorkloadOperationCoordinator>();
    private readonly IWorkloadOperation _operation = Substitute.For<IWorkloadOperation>();

    public WorkloadPruneCommandTests()
    {
        _coordinator.AcquireAsync(Arg.Any<CancellationToken>()).Returns(_operation);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Prune_KnownAdmissionFailure_PreservesCauseAndReadsNoSelection(bool recovery)
    {
        Exception expected = recovery ? new WorkloadRecoveryRequiredException("owned-test.intent") : new WorkloadLockNotSupportedException();
        _coordinator.AcquireAsync(Arg.Any<CancellationToken>()).Returns<Task<IWorkloadOperation>>(_ => throw expected);
        var command = new WorkloadPruneCommand(_interaction, _installer, _store, _coordinator);

        Func<Task> invoke = () => InvokeAsync(command);

        var exception = (await invoke.Should().ThrowAsync<GracefulException>()).Which;
        exception.IsUserError.Should().BeTrue();
        exception.InnerException.Should().BeSameAs(expected);
        await _store.DidNotReceive().GetWorkloadsAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public void Prune_NullCoordinator_RejectsProgrammerError()
    {
        Action create = () => new WorkloadPruneCommand(_interaction, _installer, _store, null!);

        create.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Prune_HasExpectedArgsAndOptions()
    {
        var cmd = new WorkloadPruneCommand(_interaction, _installer, _store, _coordinator);
        cmd.Arguments.Should().ContainSingle(a => a.Name == "id");
        cmd.Options.Should().Contain(o => o.Name == "--exact");
    }

    [Fact]
    public async Task Prune_EmptyStore_HintsAndReturnsZero()
    {
        _store.GetWorkloadsAsync(Arg.Any<CancellationToken>()).Returns(Array.Empty<WorkloadEntry>());

        var cmd = new WorkloadPruneCommand(_interaction, _installer, _store, _coordinator);
        int exit = await InvokeAsync(cmd);

        exit.Should().Be(0);
        _interaction.Lines.Should().Contain(l => l.StartsWith("HINT:") && l.Contains("No workloads installed"));
        await _installer.DidNotReceive().UninstallAsync(_operation,
            Arg.Any<string>(), Arg.Any<string>(), WorkloadOwnershipKind.Explicit, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Prune_SingleVersionPerPackage_NoOpHint()
    {
        _store.GetWorkloadsAsync(Arg.Any<CancellationToken>()).Returns(new[]
        {
            new WorkloadEntry { PackageId = "Pkg.A", PackageVersion = "1.0.0" },
            new WorkloadEntry { PackageId = "Pkg.B", PackageVersion = "2.0.0" },
        });

        var cmd = new WorkloadPruneCommand(_interaction, _installer, _store, _coordinator);
        int exit = await InvokeAsync(cmd);

        exit.Should().Be(0);
        _interaction.Lines.Should().Contain(l => l.StartsWith("HINT:") && l.Contains("Nothing to prune"));
        await _installer.DidNotReceive().UninstallAsync(_operation,
            Arg.Any<string>(), Arg.Any<string>(), WorkloadOwnershipKind.Explicit, Arg.Any<CancellationToken>());
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
        _installer.UninstallAsync(_operation, Arg.Any<string>(), Arg.Any<string>(), WorkloadOwnershipKind.Explicit, Arg.Any<CancellationToken>())
            .Returns(true);

        var cmd = new WorkloadPruneCommand(_interaction, _installer, _store, _coordinator);
        int exit = await InvokeAsync(cmd);

        exit.Should().Be(0);
        await _installer.Received(1).UninstallAsync(_operation, "Pkg.A", "1.5.0", WorkloadOwnershipKind.Explicit, Arg.Any<CancellationToken>());
        await _installer.Received(1).UninstallAsync(_operation, "Pkg.A", "1.0.0", WorkloadOwnershipKind.Explicit, Arg.Any<CancellationToken>());
        await _installer.DidNotReceive().UninstallAsync(_operation, "Pkg.A", "2.0.0", WorkloadOwnershipKind.Explicit, Arg.Any<CancellationToken>());
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
        _installer.UninstallAsync(_operation, Arg.Any<string>(), Arg.Any<string>(), WorkloadOwnershipKind.Explicit, Arg.Any<CancellationToken>())
            .Returns(true);

        var cmd = new WorkloadPruneCommand(_interaction, _installer, _store, _coordinator);
        int exit = await InvokeAsync(cmd, "Pkg.A");

        exit.Should().Be(0);
        await _installer.Received(1).UninstallAsync(_operation, "Pkg.A", "1.0.0", WorkloadOwnershipKind.Explicit, Arg.Any<CancellationToken>());
        await _installer.DidNotReceive().UninstallAsync(_operation, "Pkg.B", Arg.Any<string>(), WorkloadOwnershipKind.Explicit, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Prune_ScopedByAlias_MatchesAlias()
    {
        _store.GetWorkloadsAsync(Arg.Any<CancellationToken>()).Returns(new[]
        {
            new WorkloadEntry { PackageId = "Pkg.A", PackageVersion = "1.0.0", Aliases = ["a"] },
            new WorkloadEntry { PackageId = "Pkg.A", PackageVersion = "2.0.0", Aliases = ["a"] },
        });
        _installer.UninstallAsync(_operation, Arg.Any<string>(), Arg.Any<string>(), WorkloadOwnershipKind.Explicit, Arg.Any<CancellationToken>())
            .Returns(true);

        var cmd = new WorkloadPruneCommand(_interaction, _installer, _store, _coordinator);
        int exit = await InvokeAsync(cmd, "a");

        exit.Should().Be(0);
        await _installer.Received(1).UninstallAsync(_operation, "Pkg.A", "1.0.0", WorkloadOwnershipKind.Explicit, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Prune_AmbiguousExplicitAlias_ThrowsWithoutRemoving()
    {
        _store.GetWorkloadsAsync(Arg.Any<CancellationToken>()).Returns(
        [
            new WorkloadEntry { PackageId = "Pkg.A", PackageVersion = "1.0.0", Aliases = ["shared"] },
            new WorkloadEntry { PackageId = "Pkg.B", PackageVersion = "1.0.0", Aliases = ["shared"] },
        ]);
        var cmd = new WorkloadPruneCommand(_interaction, _installer, _store, _coordinator);

        GracefulException ex = (await FluentActions.Awaiting(
            () => InvokeAsync(cmd, "shared")).Should().ThrowExactlyAsync<GracefulException>()).Which;

        ex.Message.Should().Contain("matches multiple installed workloads");
        ex.Message.Should().Contain("Pkg.A");
        ex.Message.Should().Contain("Pkg.B");
        await _installer.DidNotReceive().UninstallAsync(_operation,
            Arg.Any<string>(), Arg.Any<string>(), WorkloadOwnershipKind.Explicit, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Prune_AmbiguousLogicalAlias_ThrowsWithoutRemoving()
    {
        _store.GetWorkloadsAsync(Arg.Any<CancellationToken>()).Returns(
        [
            new WorkloadEntry
            {
                PackageId = "Pkg.A.win-x64",
                PackageVersion = "1.0.0",
                IsImplicitlyInstalled = true,
                LogicalPackage = new LogicalPackage
                {
                    PackageId = "Pkg.A",
                    PackageVersion = "1.0.0",
                    Aliases = ["shared"],
                },
            },
            new WorkloadEntry
            {
                PackageId = "Pkg.B.win-x64",
                PackageVersion = "1.0.0",
                IsImplicitlyInstalled = true,
                LogicalPackage = new LogicalPackage
                {
                    PackageId = "Pkg.B",
                    PackageVersion = "1.0.0",
                    Aliases = ["shared"],
                },
            },
        ]);
        var cmd = new WorkloadPruneCommand(_interaction, _installer, _store, _coordinator);

        GracefulException ex = (await FluentActions.Awaiting(
            () => InvokeAsync(cmd, "shared")).Should().ThrowExactlyAsync<GracefulException>()).Which;

        ex.Message.Should().Contain("matches multiple installed workloads");
        ex.Message.Should().Contain("Pkg.A");
        ex.Message.Should().Contain("Pkg.B");
        await _installer.DidNotReceive().UninstallAsync(_operation,
            Arg.Any<string>(),
            Arg.Any<string>(),
            WorkloadOwnershipKind.Logical,
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Prune_Exact_DoesNotMatchAlias()
    {
        _store.GetWorkloadsAsync(Arg.Any<CancellationToken>()).Returns(new[]
        {
            new WorkloadEntry { PackageId = "Pkg.A", PackageVersion = "1.0.0", Aliases = ["a"] },
            new WorkloadEntry { PackageId = "Pkg.A", PackageVersion = "2.0.0", Aliases = ["a"] },
        });

        var cmd = new WorkloadPruneCommand(_interaction, _installer, _store, _coordinator);
        int exit = await InvokeAsync(cmd, "a", "--exact");

        exit.Should().Be(0);
        _interaction.Lines.Should().Contain(l => l.StartsWith("WARNING:") && l.Contains("'a' is not installed"));
        await _installer.DidNotReceive().UninstallAsync(_operation,
            Arg.Any<string>(), Arg.Any<string>(), WorkloadOwnershipKind.Explicit, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Prune_LogicalPointer_KeepsHighestPointerVersion()
    {
        _store.GetWorkloadsAsync(Arg.Any<CancellationToken>()).Returns(
        [
            LogicalEntry("Pkg.Pointer.win-x64", "1.0.0"),
            LogicalEntry("Pkg.Pointer.win-x64", "2.0.0"),
        ]);
        _installer.UninstallAsync(_operation,
                Arg.Any<string>(),
                Arg.Any<string>(),
                WorkloadOwnershipKind.Logical,
                Arg.Any<CancellationToken>())
            .Returns(true);

        var cmd = new WorkloadPruneCommand(_interaction, _installer, _store, _coordinator);
        int exit = await InvokeAsync(cmd, "pointer");

        exit.Should().Be(0);
        await _installer.Received(1).UninstallAsync(_operation,
            "Pkg.Pointer",
            "1.0.0",
            WorkloadOwnershipKind.Logical,
            Arg.Any<CancellationToken>());
        await _installer.DidNotReceive().UninstallAsync(_operation,
            "Pkg.Pointer",
            "2.0.0",
            WorkloadOwnershipKind.Logical,
            Arg.Any<CancellationToken>());

        static WorkloadEntry LogicalEntry(string physicalPackageId, string version) => new()
        {
            PackageId = physicalPackageId,
            PackageVersion = version,
            IsImplicitlyInstalled = true,
            LogicalPackage = new LogicalPackage
            {
                PackageId = "Pkg.Pointer",
                PackageVersion = version,
                Aliases = ["pointer"],
            },
        };
    }

    [Fact]
    public async Task Prune_WhitespaceArg_FailsValidation()
    {
        var cmd = new WorkloadPruneCommand(_interaction, _installer, _store, _coordinator);
        int exit = await InvokeAsync(cmd, "   ");

        exit.Should().NotBe(0);
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
