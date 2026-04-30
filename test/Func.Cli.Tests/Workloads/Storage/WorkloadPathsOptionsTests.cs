// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Workloads.Storage;
using Xunit;

namespace Azure.Functions.Cli.Tests.Workloads.Storage;

public class WorkloadPathsOptionsTests
{
    [Fact]
    public void Home_DefaultsToUserProfileAzureFunctions()
    {
        var options = new WorkloadPathsOptions();

        var expected = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".azure-functions");
        Assert.Equal(expected, options.Home);
    }

    [Fact]
    public void GlobalManifestPath_IsHomeJoinedWithManifestFileName()
    {
        var options = new WorkloadPathsOptions { Home = "/tmp/funcs" };

        Assert.Equal(
            Path.Combine("/tmp/funcs", WorkloadPathsOptions.GlobalManifestFileName),
            options.GlobalManifestPath);
    }

    [Fact]
    public void WorkloadsRoot_IsHomeJoinedWithWorkloads()
    {
        var options = new WorkloadPathsOptions { Home = "/tmp/funcs" };

        Assert.Equal(Path.Combine("/tmp/funcs", "workloads"), options.WorkloadsRoot);
    }

    [Fact]
    public void GetInstallDirectory_LayoutIsPackageIdThenVersion()
    {
        var options = new WorkloadPathsOptions { Home = "/tmp/funcs" };

        Assert.Equal(
            Path.Combine("/tmp/funcs", "workloads", "Azure.Functions.Cli.Workload.Dotnet", "1.2.3"),
            options.GetInstallDirectory("Azure.Functions.Cli.Workload.Dotnet", "1.2.3"));
    }
}
