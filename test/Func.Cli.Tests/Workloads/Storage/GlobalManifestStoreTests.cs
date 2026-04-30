// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Workloads.Storage;
using Microsoft.Extensions.Options;
using Xunit;

namespace Azure.Functions.Cli.Tests.Workloads.Storage;

public class GlobalManifestStoreTests : IDisposable
{
    private readonly string _tempHome;
    private readonly GlobalManifestStore _store;

    public GlobalManifestStoreTests()
    {
        _tempHome = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        _store = new GlobalManifestStore(
            Options.Create(new WorkloadPathsOptions { Home = _tempHome }));
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempHome))
        {
            Directory.Delete(_tempHome, recursive: true);
        }
    }

    [Fact]
    public void Read_ReturnsEmptyManifest_WhenFileMissing()
    {
        var manifest = _store.Read();

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

        _store.Write(original);
        var roundTripped = _store.Read();

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

        _store.Write(new GlobalManifest());

        Assert.True(File.Exists(Path.Combine(_tempHome, WorkloadPathsOptions.GlobalManifestFileName)));
    }

    [Fact]
    public void Read_ThrowsGracefulException_OnMalformedJson()
    {
        Directory.CreateDirectory(_tempHome);
        var manifestPath = Path.Combine(_tempHome, WorkloadPathsOptions.GlobalManifestFileName);
        File.WriteAllText(manifestPath, "{ not valid json");

        var ex = Assert.Throws<GracefulException>(() => _store.Read());
        Assert.True(ex.IsUserError);
        Assert.Contains(manifestPath, ex.Message);
    }

    [Fact]
    public void Write_LeavesNoTempFile_OnSuccess()
    {
        _store.Write(new GlobalManifest());

        var stragglers = Directory.GetFiles(_tempHome, "*.json.tmp");
        Assert.Empty(stragglers);
    }

    [Fact]
    public void Write_PreservesPreviousManifest_OnRepeatedWrites()
    {
        // Atomic-rename guarantees the final path always points at a fully
        // written file — never a partial one. Roundtripping after multiple
        // writes proves the rename actually happened and the file is intact.
        _store.Write(new GlobalManifest
        {
            Workloads =
            {
                new GlobalManifestEntry
                {
                    PackageId = "first",
                    DisplayName = "first",
                    Version = "1.0.0",
                    InstallPath = "/tmp/first",
                    EntryPoint = new EntryPointSpec { Assembly = "a.dll", Type = "T" },
                },
            },
        });

        _store.Write(new GlobalManifest
        {
            Workloads =
            {
                new GlobalManifestEntry
                {
                    PackageId = "second",
                    DisplayName = "second",
                    Version = "2.0.0",
                    InstallPath = "/tmp/second",
                    EntryPoint = new EntryPointSpec { Assembly = "b.dll", Type = "T" },
                },
            },
        });

        var entry = Assert.Single(_store.Read().Workloads);
        Assert.Equal("second", entry.PackageId);
    }
}
