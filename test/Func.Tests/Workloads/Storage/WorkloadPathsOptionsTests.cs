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
        string? previous = Environment.GetEnvironmentVariable(WorkloadPathsOptions.HomeEnvironmentVariable);
        Environment.SetEnvironmentVariable(WorkloadPathsOptions.HomeEnvironmentVariable, null);
        try
        {
            var options = new WorkloadPathsOptions();

            var expected = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                Constants.FuncHomeDirectoryName);
            Assert.Equal(expected, options.Home);
        }
        finally
        {
            Environment.SetEnvironmentVariable(WorkloadPathsOptions.HomeEnvironmentVariable, previous);
        }
    }

    [Fact]
    public void Home_DefaultsToEnvironmentVariable_WhenExplicitlySet()
    {
        string? previous = Environment.GetEnvironmentVariable(WorkloadPathsOptions.HomeEnvironmentVariable);
        var overridden = Path.Combine(Path.GetTempPath(), "func-cli-home-override", Guid.NewGuid().ToString("N"));
        Environment.SetEnvironmentVariable(WorkloadPathsOptions.HomeEnvironmentVariable, overridden);
        try
        {
            var options = new WorkloadPathsOptions();

            Assert.Equal(overridden, options.Home);
        }
        finally
        {
            Environment.SetEnvironmentVariable(WorkloadPathsOptions.HomeEnvironmentVariable, previous);
        }
    }

    [Fact]
    public void Home_IgnoresEnvironmentVariable_WhenSetToEmpty()
    {
        // An empty value is treated as "not explicitly set" so the default
        // user-profile path still wins; this mirrors how `Environment` exposes
        // an unset variable.
        string? previous = Environment.GetEnvironmentVariable(WorkloadPathsOptions.HomeEnvironmentVariable);
        Environment.SetEnvironmentVariable(WorkloadPathsOptions.HomeEnvironmentVariable, string.Empty);
        try
        {
            var options = new WorkloadPathsOptions();

            var expected = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                Constants.FuncHomeDirectoryName);
            Assert.Equal(expected, options.Home);
        }
        finally
        {
            Environment.SetEnvironmentVariable(WorkloadPathsOptions.HomeEnvironmentVariable, previous);
        }
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
