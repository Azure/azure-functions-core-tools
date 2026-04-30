// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Workloads.Storage;
using Microsoft.Extensions.Options;
using Xunit;

namespace Azure.Functions.Cli.Tests.Workloads.Storage;

public class WorkloadPathsTests
{
    [Fact]
    public void Home_ReflectsOptionsValue()
    {
        var paths = CreatePaths("/tmp/funcs");

        Assert.Equal("/tmp/funcs", paths.Home);
    }

    [Fact]
    public void WorkloadsRoot_IsHomeJoinedWithWorkloads()
    {
        var paths = CreatePaths("/tmp/funcs");

        Assert.Equal(Path.Combine("/tmp/funcs", "workloads"), paths.WorkloadsRoot);
    }

    [Fact]
    public void GlobalManifestPath_IsHomeJoinedWithManifestFileName()
    {
        var paths = CreatePaths("/tmp/funcs");

        Assert.Equal(
            Path.Combine("/tmp/funcs", WorkloadPaths.GlobalManifestFileName),
            paths.GlobalManifestPath);
    }

    [Fact]
    public void GetInstallDirectory_LayoutIsPackageIdThenVersion()
    {
        var paths = CreatePaths("/tmp/funcs");

        Assert.Equal(
            Path.Combine("/tmp/funcs", "workloads", "Azure.Functions.Cli.Workload.Dotnet", "1.2.3"),
            paths.GetInstallDirectory("Azure.Functions.Cli.Workload.Dotnet", "1.2.3"));
    }

    private static WorkloadPaths CreatePaths(string home)
        => new(Options.Create(new WorkloadPathsOptions { Home = home }));
}
