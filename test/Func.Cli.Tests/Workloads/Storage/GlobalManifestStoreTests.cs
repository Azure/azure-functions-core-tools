// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Workloads.Storage;
using Xunit;

namespace Azure.Functions.Cli.Tests.Workloads.Storage;

[Collection(nameof(WorkingDirectoryTests))]
public class GlobalManifestStoreTests : IDisposable
{
    private const string HomeEnvVar = "FUNC_CLI_HOME";

    private readonly string? _originalHome;
    private readonly string _tempHome;

    public GlobalManifestStoreTests()
    {
        _originalHome = Environment.GetEnvironmentVariable(HomeEnvVar);
        _tempHome = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Environment.SetEnvironmentVariable(HomeEnvVar, _tempHome);
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable(HomeEnvVar, _originalHome);
        if (Directory.Exists(_tempHome))
        {
            Directory.Delete(_tempHome, recursive: true);
        }
    }

    [Fact]
    public void Read_ReturnsEmptyManifest_WhenFileMissing()
    {
        var manifest = GlobalManifestStore.Read();

        Assert.NotNull(manifest);
        Assert.Empty(manifest.Workloads);
    }

    [Fact]
    public void WriteThenRead_RoundTripsAllFields()
    {
        var original = new GlobalManifest
        {
            Workloads =
            {
                new GlobalManifestEntry
                {
                    PackageId = "Azure.Functions.Cli.Workload.Dotnet",
                    DisplayName = "Dotnet",
                    Description = "DotNet workload",
                    Version = "1.0.0",
                    Aliases = new[] { "dotnet", "dotnet-isolated" },
                    InstallPath = Path.Combine(_tempHome, "workloads", "Azure.Functions.Cli.Workload.Dotnet", "1.0.0"),
                    EntryPoint = new EntryPointSpec
                    {
                        Assembly = "lib/net10.0/Azure.Functions.Cli.Workload.Dotnet.dll",
                        Type = "Azure.Functions.Cli.Workload.Dotnet.DotnetWorkload",
                    },
                },
            },
        };

        GlobalManifestStore.Write(original);
        var roundTripped = GlobalManifestStore.Read();

        var entry = Assert.Single(roundTripped.Workloads);
        var expected = original.Workloads[0];
        Assert.Equal(expected.PackageId, entry.PackageId);
        Assert.Equal(expected.DisplayName, entry.DisplayName);
        Assert.Equal(expected.Description, entry.Description);
        Assert.Equal(expected.Version, entry.Version);
        Assert.Equal(expected.Aliases, entry.Aliases);
        Assert.Equal(expected.InstallPath, entry.InstallPath);
        Assert.Equal(expected.EntryPoint.Assembly, entry.EntryPoint.Assembly);
        Assert.Equal(expected.EntryPoint.Type, entry.EntryPoint.Type);
    }

    [Fact]
    public void Write_CreatesHomeDirectory_WhenMissing()
    {
        Assert.False(Directory.Exists(_tempHome));

        GlobalManifestStore.Write(new GlobalManifest());

        Assert.True(File.Exists(WorkloadPaths.GlobalManifestPath));
    }

    [Fact]
    public void Read_ThrowsGracefulException_OnMalformedJson()
    {
        Directory.CreateDirectory(_tempHome);
        File.WriteAllText(WorkloadPaths.GlobalManifestPath, "{ not valid json");

        var ex = Assert.Throws<GracefulException>(() => GlobalManifestStore.Read());
        Assert.True(ex.IsUserError);
        Assert.Contains(WorkloadPaths.GlobalManifestPath, ex.Message);
    }
}
