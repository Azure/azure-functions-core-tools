// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Workloads.Storage;
using Xunit;

namespace Azure.Functions.Cli.Tests.Workloads.Storage;

public class WorkloadManifestSchemaTests : IDisposable
{
    private const string V1Schema = "https://aka.ms/func-workloads/v1/schema.json";

    private readonly string _tempHome;
    private readonly GlobalManifestStore _store;
    private readonly string _manifestPath;

    public WorkloadManifestSchemaTests()
    {
        _tempHome = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var paths = new WorkloadPathsOptions { Home = _tempHome };
        _store = new GlobalManifestStore(paths);
        _manifestPath = paths.GlobalManifestPath;
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempHome))
        {
            Directory.Delete(_tempHome, recursive: true);
        }
    }

    [Fact]
    public void CurrentSchema_IsV1Url()
    {
        Assert.Equal(V1Schema, WorkloadManifestSchema.CurrentSchema);
    }

    [Fact]
    public async Task SaveWorkload_WritesSchemaUrlAtTopOfDocument()
    {
        await _store.SaveWorkloadAsync("Azure.Functions.Cli.Workload.Stub", "1.0.0", NewEntry());

        var json = await File.ReadAllTextAsync(_manifestPath);

        Assert.Contains($"\"$schema\":\"{V1Schema}\"", json);
        var schemaIdx = json.IndexOf("\"$schema\"", StringComparison.Ordinal);
        var workloadsIdx = json.IndexOf("\"workloads\"", StringComparison.Ordinal);
        Assert.True(schemaIdx >= 0 && workloadsIdx >= 0);
        Assert.True(schemaIdx < workloadsIdx, "$schema must appear before workloads.");
    }

    [Fact]
    public async Task GetWorkloads_AcceptsManifest_WithKnownSchemaUrl()
    {
        await WriteRawManifestAsync($$"""
            {
              "$schema":"{{V1Schema}}",
              "workloads": {
                "Pkg": { "1.0.0": { "displayName": "Pkg", "installPath": "/x", "entryPoint": { "assembly": "X.dll", "type": "X" } } }
              }
            }
            """);

        var workloads = await _store.GetWorkloadsAsync();

        Assert.Single(workloads);
    }

    [Fact]
    public async Task GetWorkloads_AcceptsManifest_WithMissingSchema_AsLegacyV1()
    {
        // Manifests written before $schema landed must still load. They are
        // implicitly v1 and re-emitted with the field on next write.
        await WriteRawManifestAsync("""
            {
              "workloads": {
                "Pkg": { "1.0.0": { "displayName": "Pkg", "installPath": "/x", "entryPoint": { "assembly": "X.dll", "type": "X" } } }
              }
            }
            """);

        var workloads = await _store.GetWorkloadsAsync();

        Assert.Single(workloads);
    }

    [Fact]
    public async Task GetWorkloads_RejectsManifest_WithUnknownSchemaUrl()
    {
        await WriteRawManifestAsync("""
            {
              "$schema": "https://aka.ms/func-workloads/v999/schema.json",
              "workloads": {}
            }
            """);

        var ex = await Assert.ThrowsAsync<GracefulException>(() => _store.GetWorkloadsAsync());

        Assert.True(ex.IsUserError);
        Assert.Contains("v999", ex.Message);
        Assert.Contains("update", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RoundTrip_PreservesSchemaUrl()
    {
        await _store.SaveWorkloadAsync("Pkg", "1.0.0", NewEntry());
        await _store.GetWorkloadsAsync();
        await _store.SaveWorkloadAsync("Pkg2", "2.0.0", NewEntry());

        var json = await File.ReadAllTextAsync(_manifestPath);

        Assert.Contains($"\"$schema\":\"{V1Schema}\"", json);
    }

    private static GlobalManifestEntry NewEntry() => new()
    {
        DisplayName = "Stub",
        InstallPath = "/installs/stub",
        EntryPoint = new EntryPointSpec { Assembly = "Stub.dll", Type = "Stub" },
    };

    private async Task WriteRawManifestAsync(string json)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_manifestPath)!);
        await File.WriteAllTextAsync(_manifestPath, json);
    }
}
