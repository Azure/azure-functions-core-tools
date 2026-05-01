// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.CommandLine;
using Azure.Functions.Cli.Commands.Workload;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Workloads.Install;
using Azure.Functions.Cli.Workloads.Storage;
using NSubstitute;
using Xunit;

namespace Azure.Functions.Cli.Tests.Commands.Workload;

public class WorkloadUninstallCommandTests
{
    private readonly TestInteractionService _interaction = new();
    private readonly IWorkloadInstaller _installer = Substitute.For<IWorkloadInstaller>();
    private readonly IGlobalManifestStore _store = Substitute.For<IGlobalManifestStore>();

    [Fact]
    public async Task Uninstall_NoVersionsInstalled_Throws()
    {
        StubInstalled();

        var cmd = NewCommand();
        var ex = await Assert.ThrowsAsync<GracefulException>(
            () => InvokeAsync(cmd, "Test.Workload"));

        Assert.True(ex.IsUserError);
        Assert.Contains("not installed", ex.Message);
    }

    [Fact]
    public async Task Uninstall_SingleVersionInstalled_NoFlags_Removes()
    {
        StubInstalled(("Test.Workload", "1.0.0"));
        _installer.UninstallAsync("Test.Workload", "1.0.0", Arg.Any<CancellationToken>())
            .Returns(true);

        var cmd = NewCommand();
        var exit = await InvokeAsync(cmd, "Test.Workload");

        Assert.Equal(0, exit);
        await _installer.Received(1).UninstallAsync("Test.Workload", "1.0.0", Arg.Any<CancellationToken>());
        Assert.Contains(_interaction.Lines, l => l.StartsWith("SUCCESS:") && l.Contains("1.0.0"));
    }

    [Fact]
    public async Task Uninstall_MultipleVersions_NoFlags_Throws()
    {
        StubInstalled(("Test.Workload", "1.0.0"), ("Test.Workload", "2.0.0"));

        var cmd = NewCommand();
        var ex = await Assert.ThrowsAsync<GracefulException>(
            () => InvokeAsync(cmd, "Test.Workload"));

        Assert.Contains("Multiple versions", ex.Message);
        Assert.Contains("--all", ex.Message);
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
        var ex = await Assert.ThrowsAsync<GracefulException>(
            () => InvokeAsync(cmd, "Test.Workload", "--version", "9.9.9"));

        Assert.Contains("9.9.9", ex.Message);
        Assert.Contains("Installed versions: 1.0.0", ex.Message);
    }

    [Fact]
    public async Task Uninstall_AllFlag_RemovesEveryVersion()
    {
        StubInstalled(("Test.Workload", "1.0.0"), ("Test.Workload", "2.0.0"));
        _installer.UninstallAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(true);

        var cmd = NewCommand();
        await InvokeAsync(cmd, "Test.Workload", "--all");

        await _installer.Received(1).UninstallAsync("Test.Workload", "1.0.0", Arg.Any<CancellationToken>());
        await _installer.Received(1).UninstallAsync("Test.Workload", "2.0.0", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Uninstall_AllAndVersion_Throws()
    {
        StubInstalled(("Test.Workload", "1.0.0"));

        var cmd = NewCommand();
        var ex = await Assert.ThrowsAsync<GracefulException>(
            () => InvokeAsync(cmd, "Test.Workload", "--all", "--version", "1.0.0"));

        Assert.Contains("--all and --version", ex.Message);
    }

    [Fact]
    public async Task Uninstall_PackageIdMatchIsCaseInsensitive()
    {
        StubInstalled(("Test.Workload", "1.0.0"));
        _installer.UninstallAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(true);

        var cmd = NewCommand();
        var exit = await InvokeAsync(cmd, "test.workload");

        Assert.Equal(0, exit);
        await _installer.Received(1).UninstallAsync("Test.Workload", "1.0.0", Arg.Any<CancellationToken>());
    }

    private WorkloadUninstallCommand NewCommand() => new(_interaction, _installer, _store);

    private void StubInstalled(params (string PackageId, string Version)[] entries)
    {
        var workloads = entries
            .Select(e => new InstalledWorkload(
                e.PackageId,
                e.Version,
                new GlobalManifestEntry
                {
                    DisplayName = e.PackageId,
                    InstallPath = $"/install/{e.PackageId}/{e.Version}",
                    EntryPoint = new EntryPointSpec { Assembly = "x.dll", Type = "T" },
                }))
            .ToArray();
        _store.GetWorkloadsAsync(Arg.Any<CancellationToken>()).Returns(workloads);
    }

    private static Task<int> InvokeAsync(WorkloadUninstallCommand cmd, params string[] args)
    {
        var root = new RootCommand();
        root.Subcommands.Add(cmd);
        var config = new System.CommandLine.InvocationConfiguration { EnableDefaultExceptionHandler = false };
        return root.Parse(new[] { cmd.Name }.Concat(args).ToArray()).InvokeAsync(config);
    }
}
