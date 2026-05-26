// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Workloads.Storage;
using Xunit;

namespace Azure.Functions.Cli.Tests.Workloads.Storage;

public class WorkloadPathsOptionsTests
{
    [Fact]
    public void Constructor_NormalisesHomeWithGetFullPath()
    {
        // Relative paths get resolved against the current working directory;
        // the surface area of WorkloadPathsOptions is "Home is always a full
        // path," so callers don't have to normalise themselves.
        var options = new WorkloadPathsOptions("relative/path");

        Assert.Equal(Path.GetFullPath("relative/path"), options.Home);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Constructor_ThrowsForMissingHome(string? home)
    {
        Assert.ThrowsAny<ArgumentException>(() => new WorkloadPathsOptions(home!));
    }

    [Fact]
    public void WorkloadsRoot_IsHomeJoinedWithWorkloads()
    {
        var options = new WorkloadPathsOptions(Path.Combine(Path.GetTempPath(), "funcs"));

        Assert.Equal(Path.Combine(options.Home, "workloads"), options.WorkloadsRoot);
    }

    [Fact]
    public void WorkloadRegistryPath_IsHomeJoinedWithRegistryFileName()
    {
        var options = new WorkloadPathsOptions(Path.Combine(Path.GetTempPath(), "funcs"));

        Assert.Equal(
            Path.Combine(options.Home, WorkloadPathsOptions.WorkloadRegistryFileName),
            options.WorkloadRegistryPath);
    }

    [Fact]
    public void GetInstallDirectory_LayoutIsPackageIdThenVersion()
    {
        var options = new WorkloadPathsOptions(Path.Combine(Path.GetTempPath(), "funcs"));

        Assert.Equal(
            Path.Combine(options.Home, "workloads", "Azure.Functions.Cli.Workloads.Dotnet", "1.2.3"),
            options.GetInstallDirectory("Azure.Functions.Cli.Workloads.Dotnet", "1.2.3"));
    }
}
