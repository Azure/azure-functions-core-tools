// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.ComponentModel.DataAnnotations;
using Azure.Functions.Cli.Workloads.Storage;
using Xunit;

namespace Azure.Functions.Cli.Tests.Workloads.Storage;

public class WorkloadPathsOptionsTests
{
    [Fact]
    public void Home_DefaultsToEmpty()
    {
        // Setup runs through DI; bare construction leaves Home empty so
        // ValidateDataAnnotations catches a missing WorkloadPathsOptionsSetup
        // registration on startup.
        var options = new WorkloadPathsOptions();

        Assert.Equal(string.Empty, options.Home);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Home_FailsValidation_WhenMissingOrEmpty(string? home)
    {
        var options = new WorkloadPathsOptions { Home = home! };

        var results = new List<ValidationResult>();
        var valid = Validator.TryValidateObject(
            options,
            new ValidationContext(options),
            results,
            validateAllProperties: true);

        Assert.False(valid);
        Assert.Contains(results, r => r.MemberNames.Contains(nameof(WorkloadPathsOptions.Home)));
    }

    [Fact]
    public void WorkloadsRoot_IsHomeJoinedWithWorkloads()
    {
        var options = new WorkloadPathsOptions { Home = "/tmp/funcs" };

        Assert.Equal(Path.Combine("/tmp/funcs", "workloads"), options.WorkloadsRoot);
    }

    [Fact]
    public void WorkloadRegistryPath_IsHomeJoinedWithRegistryFileName()
    {
        var options = new WorkloadPathsOptions { Home = "/tmp/funcs" };

        Assert.Equal(
            Path.Combine("/tmp/funcs", WorkloadPathsOptions.WorkloadRegistryFileName),
            options.WorkloadRegistryPath);
    }

    [Fact]
    public void GetInstallDirectory_LayoutIsPackageIdThenVersion()
    {
        var options = new WorkloadPathsOptions { Home = "/tmp/funcs" };

        Assert.Equal(
            Path.Combine("/tmp/funcs", "workloads", "Azure.Functions.Cli.Workloads.Dotnet", "1.2.3"),
            options.GetInstallDirectory("Azure.Functions.Cli.Workloads.Dotnet", "1.2.3"));
    }
}
