// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.CommandLine;
using Azure.Functions.Cli.Commands.Workload;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Workloads;
using Azure.Functions.Cli.Workloads.Install;
using Azure.Functions.Cli.Workloads.Storage;
using NSubstitute;

namespace Azure.Functions.Cli.Tests.Commands.Workload;

public class WorkloadUninstallCommandTests
{
    private readonly TestInteractionService _interaction = new();
    private readonly IWorkloadInstaller _installer = Substitute.For<IWorkloadInstaller>();
    private readonly IWorkloadStore _store = Substitute.For<IWorkloadStore>();

    [Fact]
    public async Task Uninstall_NoVersionsInstalled_WarnsAndSucceeds()
    {
        StubInstalled();

        var cmd = NewCommand();
        int exit = await InvokeAsync(cmd, "Test.Workload");

        exit.Should().Be(0);
        _interaction.Lines.Should().Contain(l => l.StartsWith("WARNING:") && l.Contains("not installed"));
        await _installer.DidNotReceive().UninstallAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Uninstall_SingleVersionInstalled_NoFlags_Removes()
    {
        StubInstalled(("Test.Workload", "1.0.0"));
        _installer.UninstallAsync("Test.Workload", "1.0.0", Arg.Any<CancellationToken>())
            .Returns(true);

        var cmd = NewCommand();
        int exit = await InvokeAsync(cmd, "Test.Workload");

        exit.Should().Be(0);
        await _installer.Received(1).UninstallAsync("Test.Workload", "1.0.0", Arg.Any<CancellationToken>());
        _interaction.Lines.Should().Contain(l => l.StartsWith("SUCCESS:") && l.Contains("1.0.0"));
    }

    [Fact]
    public async Task Uninstall_MultipleVersions_NoFlags_Throws()
    {
        StubInstalled(("Test.Workload", "1.0.0"), ("Test.Workload", "2.0.0"));

        var cmd = NewCommand();
        GracefulException ex = (await FluentActions.Awaiting(() => InvokeAsync(cmd, "Test.Workload")).Should().ThrowAsync<GracefulException>()).Which;

        ex.Message.Should().Contain("Multiple versions");
        ex.Message.Should().Contain("--all-versions");
    }

    [Fact]
    public async Task Uninstall_VersionFlag_RemovesOnlyThatVersion()
    {
        StubInstalled(("Test.Workload", "1.0.0"), ("Test.Workload", "2.0.0"));
        _installer.UninstallAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(true);

        var cmd = NewCommand();
        await InvokeAsync(cmd, "Test.Workload", "--version", "2.0.0");

        await _installer.Received(1).UninstallAsync("Test.Workload", "2.0.0", Arg.Any<CancellationToken>());
        await _installer.DidNotReceive().UninstallAsync("Test.Workload", "1.0.0", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Uninstall_VersionFlag_UnknownVersion_Throws()
    {
        StubInstalled(("Test.Workload", "1.0.0"));

        var cmd = NewCommand();
        GracefulException ex = (await FluentActions.Awaiting(() => InvokeAsync(cmd, "Test.Workload", "--version", "9.9.9")).Should().ThrowAsync<GracefulException>()).Which;

        ex.Message.Should().Contain("9.9.9");
        ex.Message.Should().Contain("Installed versions: 1.0.0");
    }

    [Fact]
    public async Task Uninstall_AllFlag_RemovesEveryVersion()
    {
        StubInstalled(("Test.Workload", "1.0.0"), ("Test.Workload", "2.0.0"));
        _installer.UninstallAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(true);

        var cmd = NewCommand();
        await InvokeAsync(cmd, "Test.Workload", "--all-versions");

        await _installer.Received(1).UninstallAsync("Test.Workload", "1.0.0", Arg.Any<CancellationToken>());
        await _installer.Received(1).UninstallAsync("Test.Workload", "2.0.0", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Uninstall_VersionFlag_ShortAlias_RemovesOnlyThatVersion()
    {
        StubInstalled(("Test.Workload", "1.0.0"), ("Test.Workload", "2.0.0"));
        _installer.UninstallAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(true);

        var cmd = NewCommand();
        await InvokeAsync(cmd, "Test.Workload", "-v", "2.0.0");

        await _installer.Received(1).UninstallAsync("Test.Workload", "2.0.0", Arg.Any<CancellationToken>());
        await _installer.DidNotReceive().UninstallAsync("Test.Workload", "1.0.0", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Uninstall_AllFlag_ShortAlias_RemovesEveryVersion()
    {
        StubInstalled(("Test.Workload", "1.0.0"), ("Test.Workload", "2.0.0"));
        _installer.UninstallAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(true);

        var cmd = NewCommand();
        await InvokeAsync(cmd, "Test.Workload", "-a");

        await _installer.Received(1).UninstallAsync("Test.Workload", "1.0.0", Arg.Any<CancellationToken>());
        await _installer.Received(1).UninstallAsync("Test.Workload", "2.0.0", Arg.Any<CancellationToken>());
    }

    [Fact]
    public void Uninstall_AllAndVersion_FailsValidation()
    {
        var cmd = NewCommand();
        var root = new RootCommand();
        root.Subcommands.Add(cmd);

        ParseResult parse = root.Parse([cmd.Name, "Test.Workload", "--all-versions", "--version", "1.0.0"]);

        parse.Errors.Should().NotBeEmpty();
        parse.Errors.Should().Contain(e => e.Message.Contains("--all-versions and --version"));
    }

    [Fact]
    public void Uninstall_WhitespaceArg_FailsValidation()
    {
        var cmd = NewCommand();
        var root = new RootCommand();
        root.Subcommands.Add(cmd);

        ParseResult parse = root.Parse([cmd.Name, "   "]);

        parse.Errors.Should().NotBeEmpty();
        parse.Errors.Should().Contain(e => e.Message.Contains("workload id is required"));
    }

    [Fact]
    public async Task Uninstall_PackageIdMatchIsCaseInsensitive()
    {
        StubInstalled(("Test.Workload", "1.0.0"));
        _installer.UninstallAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(true);

        var cmd = NewCommand();
        int exit = await InvokeAsync(cmd, "test.workload");

        exit.Should().Be(0);
        await _installer.Received(1).UninstallAsync("Test.Workload", "1.0.0", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Uninstall_ByAlias_RemovesMatchingPackage()
    {
        StubInstalledWithAliases((PackageId: "Test.Workload", Version: "1.0.0", Aliases: new[] { "stub", "test-workload" }));
        _installer.UninstallAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(true);

        var cmd = NewCommand();
        int exit = await InvokeAsync(cmd, "stub");

        exit.Should().Be(0);
        await _installer.Received(1).UninstallAsync("Test.Workload", "1.0.0", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Uninstall_ByAlias_IsCaseInsensitive()
    {
        StubInstalledWithAliases((PackageId: "Test.Workload", Version: "1.0.0", Aliases: new[] { "stub" }));
        _installer.UninstallAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(true);

        var cmd = NewCommand();
        int exit = await InvokeAsync(cmd, "STUB");

        exit.Should().Be(0);
        await _installer.Received(1).UninstallAsync("Test.Workload", "1.0.0", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Uninstall_AmbiguousAlias_Throws()
    {
        StubInstalledWithAliases(
            (PackageId: "First.Workload", Version: "1.0.0", Aliases: new[] { "shared" }),
            (PackageId: "Second.Workload", Version: "1.0.0", Aliases: new[] { "shared" }));

        var cmd = NewCommand();
        GracefulException ex = (await FluentActions.Awaiting(() => InvokeAsync(cmd, "shared")).Should().ThrowAsync<GracefulException>()).Which;

        ex.Message.Should().Contain("matches multiple installed workloads");
        ex.Message.Should().Contain("First.Workload");
        ex.Message.Should().Contain("Second.Workload");
    }

    [Fact]
    public async Task Uninstall_ExactFlag_SuppressesAliasMatching()
    {
        // Without --exact, "shared" would resolve via the alias and uninstall
        // would proceed. With --exact, the argument must be a literal package
        // id; an unmatched id warns and exits 0.
        StubInstalledWithAliases(
            (PackageId: "First.Workload", Version: "1.0.0", Aliases: new[] { "shared" }));

        var cmd = NewCommand();
        int exit = await InvokeAsync(cmd, "shared", "--exact");

        exit.Should().Be(0);
        _interaction.Lines.Should().Contain(l => l.StartsWith("WARNING:") && l.Contains("not installed"));
        await _installer.DidNotReceive().UninstallAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Uninstall_ExactFlag_StillMatchesByPackageId()
    {
        // --exact only suppresses alias matching; the literal package id
        // still resolves (case-insensitive, per NuGet).
        StubInstalledWithAliases(
            (PackageId: "First.Workload", Version: "1.0.0", Aliases: new[] { "shared" }));
        _installer.UninstallAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(true);

        var cmd = NewCommand();
        int exit = await InvokeAsync(cmd, "First.Workload", "-e");

        exit.Should().Be(0);
        await _installer.Received(1).UninstallAsync("First.Workload", "1.0.0", Arg.Any<CancellationToken>());
    }

    private WorkloadUninstallCommand NewCommand() => new(_interaction, _installer, _store);

    private void StubInstalled(params (string PackageId, string Version)[] entries)
    {
        WorkloadEntry[] workloads = [.. entries
            .Select(e => new WorkloadEntry
            {
                PackageId = e.PackageId,
                PackageVersion = e.Version,
                EntryPoint = new EntryPointSpec { AssemblyPath = "x.dll", Type = "T" },
            })];
        _store.GetWorkloadsAsync(Arg.Any<CancellationToken>()).Returns(workloads);
    }

    private void StubInstalledWithAliases(params (string PackageId, string Version, string[] Aliases)[] entries)
    {
        WorkloadEntry[] workloads = [.. entries
            .Select(e => new WorkloadEntry
            {
                PackageId = e.PackageId,
                PackageVersion = e.Version,
                Aliases = e.Aliases,
                EntryPoint = new EntryPointSpec { AssemblyPath = "x.dll", Type = "T" },
            })];
        _store.GetWorkloadsAsync(Arg.Any<CancellationToken>()).Returns(workloads);
    }

    private static Task<int> InvokeAsync(WorkloadUninstallCommand cmd, params string[] args)
    {
        var root = new RootCommand();
        root.Subcommands.Add(cmd);
        var config = new InvocationConfiguration { EnableDefaultExceptionHandler = false };
        return root.Parse(new[] { cmd.Name }.Concat(args).ToArray()).InvokeAsync(config);
    }
}
