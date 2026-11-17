// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Workloads;
using Azure.Functions.Cli.Workloads.Storage;
using NSubstitute;

namespace Azure.Functions.Cli.Bundles.Tests;

public class InstalledBundleWorkloadsTests
{
    [Fact]
    public async Task FiltersToBundleWorkloadPackageId_CaseInsensitive()
    {
        IWorkloadStore store = Substitute.For<IWorkloadStore>();

        store.GetWorkloadsAsync(Arg.Any<CancellationToken>()).Returns<IReadOnlyList<WorkloadEntry>>(
        [
            new WorkloadEntry { PackageId = "Azure.Functions.Cli.Workloads.Node", PackageVersion = "1.0.0", Kind = WorkloadKind.Workload },
            new WorkloadEntry { PackageId = "azure.functions.cli.workloads.extensionbundles", PackageVersion = "4.35.0", Kind = WorkloadKind.Content },
            new WorkloadEntry { PackageId = "Azure.Functions.Cli.Workloads.ExtensionBundles", PackageVersion = "4.36.0", Kind = WorkloadKind.Content },
        ]);
        IReadOnlyList<InstalledBundleWorkload> result = await new InstalledBundleWorkloads(store).ListInstalledAsync();

        result.Count.Should().Be(2);
        result.Should().Contain(r => r.PackageVersion == "4.35.0");
        result.Should().Contain(r => r.PackageVersion == "4.36.0");
    }

    [Fact]
    public async Task SkipsWorkloadKindEntries()
    {
        IWorkloadStore store = Substitute.For<IWorkloadStore>();

        store.GetWorkloadsAsync(Arg.Any<CancellationToken>()).Returns<IReadOnlyList<WorkloadEntry>>(
        [
            new WorkloadEntry
            {
                PackageId = IInstalledBundleWorkloads.BundleWorkloadPackageId,
                PackageVersion = "4.35.0",
                Kind = WorkloadKind.Workload,
            },
        ]);
        (await new InstalledBundleWorkloads(store).ListInstalledAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task ReturnsInstallDirectoryFromEntry()
    {
        IWorkloadStore store = Substitute.For<IWorkloadStore>();

        store.GetWorkloadsAsync(Arg.Any<CancellationToken>()).Returns<IReadOnlyList<WorkloadEntry>>(
        [
            new WorkloadEntry
            {
                PackageId = IInstalledBundleWorkloads.BundleWorkloadPackageId,
                PackageVersion = "4.35.0",
                PackageRoot = "/home/.func/workloads/bundles/4.35.0",
                Kind = WorkloadKind.Content,
            },
        ]);
        IReadOnlyList<InstalledBundleWorkload> result = await new InstalledBundleWorkloads(store).ListInstalledAsync();

        result.Should().ContainSingle().Which.InstallDirectory.Should().Be("/home/.func/workloads/bundles/4.35.0");
    }
}
