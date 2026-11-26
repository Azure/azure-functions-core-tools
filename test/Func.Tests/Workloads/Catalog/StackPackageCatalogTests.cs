// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Projects;
using Azure.Functions.Cli.Tests.Commands.Setup;
using Azure.Functions.Cli.Workloads.Catalog;
using Azure.Functions.Cli.Workloads.Storage;
using NSubstitute;

namespace Azure.Functions.Cli.Tests.Workloads.Catalog;

public sealed class StackPackageCatalogTests
{
    [Fact]
    public async Task GetInstalledAsync_UsesLiveCompatibleVersionsAndLogicalPackageOwnersOffline()
    {
        StackDescriptor stack = new("comet", "native", "Installed Comet");
        var store = Substitute.For<IWorkloadStore>();
        store.GetWorkloadsAsync(Arg.Any<CancellationToken>()).Returns([
            new WorkloadEntry { PackageId = "Retired.Tools", PackageVersion = "1.2.0", Stack = stack },
            new WorkloadEntry { PackageId = "Retired.Tools", PackageVersion = "1.10.0", Stack = null },
            new WorkloadEntry { PackageId = "Foreign.Tools", PackageVersion = "1.0.0", RuntimeIdentifier = "osx-x64", Stack = stack },
            new WorkloadEntry
            {
                PackageId = "Contoso.DevKit.win-x64", PackageVersion = "1.0.0", RuntimeIdentifier = "win-x64", Stack = stack,
                LogicalPackage = new LogicalPackage { PackageId = "Contoso.DevKit", PackageVersion = "1.0.0", Source = "private-feed" },
            },
        ]);
        IWorkloadCatalog catalog = Substitute.For<IWorkloadCatalog>();

        var packages = await SetupTestInventory.StackPackages(store, catalog).GetInstalledAsync(TestContext.Current.CancellationToken);

        var package = packages.Stacks.Should().ContainSingle().Which;
        package.PackageId.Should().Be("Contoso.DevKit");
        package.Source.Should().Be("private-feed");
        package.Stack.DisplayName.Should().Be("Installed Comet");
        catalog.ReceivedCalls().Should().BeEmpty();
    }

    [Fact]
    public async Task DiscoverAsync_PreservesInstalledLabelAndDistinctOwners()
    {
        var local = SetupTestInventory.Row("Contoso.DevKit", "comet", "native", "Installed label");
        StackPackage installed = new(local.Stack!, local.PackageId, local.LatestVersion, local.Source.Source, Installed: true);
        IWorkloadCatalog catalog = SetupTestInventory.Catalog(
            SetupTestInventory.Row("Contoso.DevKit", "comet", "native", "Preview label", "2.0.0-preview.1"),
            SetupTestInventory.Row("Fabrikam.Tools", "COMET", "native", "Other owner"),
            SetupTestInventory.Row("Foreign.Tools", "comet", "native", "Linux only") with { Rid = "linux-x64" });
        const string source = "https://example.test/alternate/index.json";

        var packages = await SetupTestInventory.StackPackages(Substitute.For<IWorkloadStore>(), catalog)
            .DiscoverAsync([installed], source, true, TestContext.Current.CancellationToken);

        packages.Should().HaveCount(2);
        packages.Should().ContainSingle(p => p.PackageId == "Contoso.DevKit").Which.Stack.DisplayName.Should().Be("Installed label");
        packages.Should().ContainSingle(p => p.PackageId == "Fabrikam.Tools");
        await catalog.Received(1).SearchVersionsAsync(Arg.Is<CatalogSearchQuery>(q => q.Source == source && q.IncludePrerelease == true), TestContext.Current.CancellationToken);
    }
}
