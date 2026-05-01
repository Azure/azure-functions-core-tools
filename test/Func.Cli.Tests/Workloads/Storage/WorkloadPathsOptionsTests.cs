// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.ComponentModel.DataAnnotations;
using Azure.Functions.Cli.Common;
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
            Constants.FuncHomeDirectoryName);
        Assert.Equal(expected, options.Home);
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
    public void GlobalManifestPath_IsHomeJoinedWithManifestFileName()
    {
        var options = new WorkloadPathsOptions { Home = "/tmp/funcs" };

        Assert.Equal(
            Path.Combine("/tmp/funcs", WorkloadPathsOptions.GlobalManifestFileName),
            options.GlobalManifestPath);
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
