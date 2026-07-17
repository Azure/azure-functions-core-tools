// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Workloads.Storage;

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

        options.Home.Should().Be(Path.GetFullPath("relative/path"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Constructor_ThrowsForMissingHome(string? home)
    {
        FluentActions.Invoking(() => new WorkloadPathsOptions(home!)).Should().Throw<ArgumentException>();
    }

    [Fact]
    public void WorkloadsRoot_IsHomeJoinedWithWorkloads()
    {
        var options = new WorkloadPathsOptions(Path.Combine(Path.GetTempPath(), "funcs"));

        options.WorkloadsRoot.Should().Be(Path.Combine(options.Home, "workloads"));
    }

    [Fact]
    public void WorkloadRegistryPath_IsHomeJoinedWithRegistryFileName()
    {
        var options = new WorkloadPathsOptions(Path.Combine(Path.GetTempPath(), "funcs"));

        options.WorkloadRegistryPath.Should().Be(Path.Combine(options.Home, WorkloadPathsOptions.WorkloadRegistryFileName));
    }

    [Fact]
    public void GetInstallDirectory_LayoutIsPackageIdThenVersion()
    {
        var options = new WorkloadPathsOptions(Path.Combine(Path.GetTempPath(), "funcs"));

        options.GetInstallDirectory("Azure.Functions.Cli.Workloads.Dotnet", "1.2.3").Should().Be(Path.Combine(options.Home, "workloads", "Azure.Functions.Cli.Workloads.Dotnet", "1.2.3"));
    }
}
