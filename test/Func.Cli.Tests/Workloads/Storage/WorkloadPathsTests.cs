// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Workloads.Storage;
using Xunit;

namespace Azure.Functions.Cli.Tests.Workloads.Storage;

[Collection(nameof(WorkingDirectoryTests))]
public class WorkloadPathsTests : IDisposable
{
    private const string HomeEnvVar = "FUNC_CLI_HOME";

    private readonly string? _originalHome;

    public WorkloadPathsTests()
    {
        _originalHome = Environment.GetEnvironmentVariable(HomeEnvVar);
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable(HomeEnvVar, _originalHome);
    }

    [Fact]
    public void Home_HonorsFuncCliHomeOverride()
    {
        var temp = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Environment.SetEnvironmentVariable(HomeEnvVar, temp);

        Assert.Equal(temp, WorkloadPaths.Home);
    }

    [Fact]
    public void Home_DefaultsToUserProfile_WhenOverrideUnset()
    {
        Environment.SetEnvironmentVariable(HomeEnvVar, null);

        var expected = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".azure-functions");

        Assert.Equal(expected, WorkloadPaths.Home);
    }

    [Fact]
    public void GlobalManifestPath_IsUnderHome()
    {
        var temp = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Environment.SetEnvironmentVariable(HomeEnvVar, temp);

        Assert.Equal(Path.Combine(temp, "workloads.json"), WorkloadPaths.GlobalManifestPath);
    }

    [Fact]
    public void GetInstallDirectory_LayoutIsPackageIdThenVersion()
    {
        var temp = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Environment.SetEnvironmentVariable(HomeEnvVar, temp);

        var dir = WorkloadPaths.GetInstallDirectory("Azure.Functions.Cli.Workload.Dotnet", "1.2.3");
        Assert.Equal(Path.Combine(temp, "workloads", "Azure.Functions.Cli.Workload.Dotnet", "1.2.3"), dir);
    }
}
