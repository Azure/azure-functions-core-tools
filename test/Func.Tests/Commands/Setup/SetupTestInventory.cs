// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Projects;
using Azure.Functions.Cli.Workloads;
using Azure.Functions.Cli.Workloads.Catalog;
using Azure.Functions.Cli.Workloads.Storage;
using NSubstitute;
using NuGet.Configuration;
using NuGet.Versioning;

namespace Azure.Functions.Cli.Tests.Commands.Setup;

internal static class SetupTestInventory
{
    public static CatalogSearchResult Row(string packageId, string stack, string workerRuntime, string display, string version = "1.0.0")
        => new(packageId, NuGetVersion.Parse(version), display, null, ["unrelated-alias"], new PackageSource("https://example.test/v3/index.json"))
        {
            Stack = new StackDescriptor(stack, workerRuntime, display),
        };

    public static IWorkloadCatalog Catalog(params CatalogSearchResult[] rows)
    {
        var catalog = Substitute.For<IWorkloadCatalog>();
        catalog.SearchVersionsAsync(Arg.Any<CatalogSearchQuery>(), Arg.Any<CancellationToken>()).Returns(rows);
        return catalog;
    }

    public static StackPackageCatalog StackPackages(IWorkloadStore store, IWorkloadCatalog catalog, string rid = "win-x64")
    {
        IWorkloadRuntimeIdentifierProvider runtimeIdentifiers = Substitute.For<IWorkloadRuntimeIdentifierProvider>();
        runtimeIdentifiers.Current.Returns(rid);
        return new StackPackageCatalog(store, catalog, runtimeIdentifiers);
    }
}
