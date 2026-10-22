// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.CommandLine;
using Azure.Functions.Cli.Commands.Workload;
using Azure.Functions.Cli.Workloads.Catalog;
using Azure.Functions.Cli.Workloads.Install;
using Azure.Functions.Cli.Workloads.Storage;
using Microsoft.Extensions.Options;
using NSubstitute;
using NuGet.Versioning;

namespace Azure.Functions.Cli.Tests.Commands.Workload;

public sealed class WorkloadTrustCommandTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Install_ForwardsExplicitTrustChoice(bool local, bool allowUntrusted)
    {
        IWorkloadInstaller installer = Substitute.For<IWorkloadInstaller>();
        IWorkloadStore store = Substitute.For<IWorkloadStore>();
        TestInteractionService interaction = new();
        var options = Options.Create(new WorkloadCatalogOptions());
        var result = new WorkloadInstallResult(new WorkloadEntry { PackageId = "test.workload", PackageVersion = "1.0.0" }, false);
        installer.InstallFromPackageAsync(Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<IProgress<WorkloadInstallProgress>?>(),
            Arg.Any<CancellationToken>(), Arg.Any<bool>()).Returns(result);
        installer.InstallFromCatalogAsync(Arg.Any<string>(), Arg.Any<NuGetVersion?>(), Arg.Any<string?>(),
            Arg.Any<bool?>(), Arg.Any<bool>(), Arg.Any<bool>(), Arg.Any<IProgress<WorkloadInstallProgress>?>(),
            Arg.Any<CancellationToken>(), Arg.Any<bool>()).Returns(result);
        WorkloadInstallCommand command = new(interaction, installer, store,
            new WorkloadUpdateCommand(interaction, installer, store, options), options);
        List<string> args = [local ? "test.nupkg" : "test.workload"];
        if (allowUntrusted)
        {
            args.Add("--allow-untrusted");
        }

        int exit = await InvokeAsync(command, args);

        exit.Should().Be(0);
        if (local)
        {
            await installer.Received(1).InstallFromPackageAsync("test.nupkg", false,
                Arg.Any<IProgress<WorkloadInstallProgress>?>(), Arg.Any<CancellationToken>(), allowUntrusted);
        }
        else
        {
            await installer.Received(1).InstallFromCatalogAsync("test.workload", null, null, null, false, false,
                Arg.Any<IProgress<WorkloadInstallProgress>?>(), Arg.Any<CancellationToken>(), allowUntrusted);
        }
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(false, false, true)]
    [InlineData(false, true, true)]
    [InlineData(true, false, true)]
    [InlineData(true, true, true)]
    public async Task Update_ForwardsTrustChoiceForExplicitAndLogicalTargets(bool all, bool logical, bool allowUntrusted)
    {
        IWorkloadInstaller installer = Substitute.For<IWorkloadInstaller>();
        IWorkloadStore store = Substitute.For<IWorkloadStore>();
        WorkloadEntry entry = new()
        {
            PackageId = "test.workload",
            PackageVersion = "1.0.0",
            LogicalPackage = logical ? new LogicalPackage { PackageId = "test.pointer", PackageVersion = "1.0.0" } : null,
        };
        store.GetWorkloadsAsync(Arg.Any<CancellationToken>()).Returns([entry]);
        var result = new WorkloadUpdateResult(entry, "1.0.0", true);
        installer.UpdateAsync(Arg.Any<string>(), Arg.Any<NuGetVersion?>(), Arg.Any<string?>(), Arg.Any<bool?>(),
            Arg.Any<bool>(), Arg.Any<IProgress<WorkloadInstallProgress>?>(), Arg.Any<CancellationToken>(), Arg.Any<bool>()).Returns(result);
        installer.UpdateAsync(Arg.Any<string>(), Arg.Any<NuGetVersion?>(), Arg.Any<string?>(), Arg.Any<bool?>(),
            Arg.Any<bool>(), Arg.Any<WorkloadOwnershipKind>(), Arg.Any<IProgress<WorkloadInstallProgress>?>(),
            Arg.Any<CancellationToken>(), Arg.Any<bool>()).Returns(result);
        WorkloadUpdateCommand command = new(new TestInteractionService(), installer, store, Options.Create(new WorkloadCatalogOptions()));
        string id = logical ? "test.pointer" : "test.workload";
        List<string> args = [all ? "--all" : id];
        if (allowUntrusted)
        {
            args.Add("--allow-untrusted");
        }

        int exit = await InvokeAsync(command, args);

        exit.Should().Be(0);
        if (logical)
        {
            await installer.Received(1).UpdateAsync(id, null, null, null, false, WorkloadOwnershipKind.Logical,
                Arg.Any<IProgress<WorkloadInstallProgress>?>(), Arg.Any<CancellationToken>(), allowUntrusted);
        }
        else
        {
            await installer.Received(1).UpdateAsync(id, null, null, null, false,
                Arg.Any<IProgress<WorkloadInstallProgress>?>(), Arg.Any<CancellationToken>(), allowUntrusted);
        }
    }

    [Fact]
    public async Task InteractiveInstall_ConfirmedUpdate_PreservesExplicitTrustBypass()
    {
        IWorkloadInstaller installer = Substitute.For<IWorkloadInstaller>();
        IWorkloadStore store = Substitute.For<IWorkloadStore>();
        var options = Options.Create(new WorkloadCatalogOptions());
        WorkloadEntry entry = new() { PackageId = "test.workload", PackageVersion = "1.0.0" };
        store.GetWorkloadsAsync(Arg.Any<CancellationToken>()).Returns([entry]);
        installer.UpdateAsync("test.workload", null, null, null, false, Arg.Any<IProgress<WorkloadInstallProgress>?>(),
            Arg.Any<CancellationToken>(), true).Returns(new WorkloadUpdateResult(entry, "1.0.0", true));
        InteractiveInteraction interaction = new();
        WorkloadInstallCommand command = new(interaction, installer, store,
            new WorkloadUpdateCommand(interaction, installer, store, options), options);

        int exit = await InvokeAsync(command, ["test.workload", "--allow-untrusted"]);

        exit.Should().Be(0);
        await installer.Received(1).UpdateAsync("test.workload", null, null, null, false,
            Arg.Any<IProgress<WorkloadInstallProgress>?>(), Arg.Any<CancellationToken>(), true);
    }

    private static Task<int> InvokeAsync(Command command, List<string> args)
    {
        RootCommand root = [];
        root.Subcommands.Add(command);
        return root.Parse([command.Name, .. args]).InvokeAsync(new InvocationConfiguration { EnableDefaultExceptionHandler = false });
    }

    private sealed class InteractiveInteraction : TestInteractionService
    {
        public override bool IsInteractive => true;
    }
}
