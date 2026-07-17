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
        IWorkloadPaths paths = Substitute.For<IWorkloadPaths>();

        store.GetWorkloadsAsync(Arg.Any<CancellationToken>()).Returns<IReadOnlyList<WorkloadEntry>>(
        [
            new WorkloadEntry { PackageId = "Azure.Functions.Cli.Workloads.Node", PackageVersion = "1.0.0", Kind = WorkloadKind.Workload },
            new WorkloadEntry { PackageId = "azure.functions.cli.workloads.extensionbundles", PackageVersion = "4.35.0", Kind = WorkloadKind.Content },
            new WorkloadEntry { PackageId = "Azure.Functions.Cli.Workloads.ExtensionBundles", PackageVersion = "4.36.0", Kind = WorkloadKind.Content },
        ]);
        paths.GetInstallDirectory(Arg.Any<string>(), Arg.Any<string>()).Returns(ci => $"/install/{ci[0]}/{ci[1]}");

        IReadOnlyList<InstalledBundleWorkload> result = await new InstalledBundleWorkloads(store, paths).ListInstalledAsync();

        result.Count.Should().Be(2);
        result.Should().Contain(r => r.PackageVersion == "4.35.0");
        result.Should().Contain(r => r.PackageVersion == "4.36.0");
    }

    [Fact]
    public async Task SkipsWorkloadKindEntries()
    {
        IWorkloadStore store = Substitute.For<IWorkloadStore>();
        IWorkloadPaths paths = Substitute.For<IWorkloadPaths>();

        store.GetWorkloadsAsync(Arg.Any<CancellationToken>()).Returns<IReadOnlyList<WorkloadEntry>>(
        [
            new WorkloadEntry
            {
                PackageId = IInstalledBundleWorkloads.BundleWorkloadPackageId,
                PackageVersion = "4.35.0",
                Kind = WorkloadKind.Workload,
            },
        ]);
        paths.GetInstallDirectory(Arg.Any<string>(), Arg.Any<string>()).Returns("/x");

        (await new InstalledBundleWorkloads(store, paths).ListInstalledAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task ReturnsInstallDirectoryFromPaths()
    {
        IWorkloadStore store = Substitute.For<IWorkloadStore>();
        IWorkloadPaths paths = Substitute.For<IWorkloadPaths>();

        store.GetWorkloadsAsync(Arg.Any<CancellationToken>()).Returns<IReadOnlyList<WorkloadEntry>>(
        [
            new WorkloadEntry
            {
                PackageId = IInstalledBundleWorkloads.BundleWorkloadPackageId,
                PackageVersion = "4.35.0",
                Kind = WorkloadKind.Content,
            },
        ]);
        paths.GetInstallDirectory(IInstalledBundleWorkloads.BundleWorkloadPackageId, "4.35.0").Returns("/home/.func/workloads/bundles/4.35.0");

        IReadOnlyList<InstalledBundleWorkload> result = await new InstalledBundleWorkloads(store, paths).ListInstalledAsync();

        InstalledBundleWorkload row = result.Should().ContainSingle().Subject;
        row.InstallDirectory.Should().Be("/home/.func/workloads/bundles/4.35.0");
    }
}
