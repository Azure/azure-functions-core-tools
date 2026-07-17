// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Workloads;
using Azure.Functions.Cli.Workloads.Storage;

namespace Azure.Functions.Cli.Tests.Workloads.Storage;

public class WorkloadManifestSchemaTests : IDisposable
{
    private const string V1Schema = WorkloadManifestSchema.RegistryV1Schema;

    private readonly string _tempHome;
    private readonly WorkloadStore _store;
    private readonly string _registryPath;

    public WorkloadManifestSchemaTests()
    {
        _tempHome = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var paths = new WorkloadPathsOptions(_tempHome);
        _store = new WorkloadStore(paths);
        _registryPath = paths.WorkloadRegistryPath;
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
        WorkloadManifestSchema.CurrentRegistrySchema.Should().Be(V1Schema);
    }

    [Fact]
    public async Task SaveWorkload_WritesSchemaUrlAtTopOfDocument()
    {
        await _store.SaveWorkloadAsync(NewEntry("Azure.Functions.Cli.Workloads.Stub", "1.0.0"));

        string json = await File.ReadAllTextAsync(_registryPath);

        json.Should().Contain($"\"$schema\":\"{V1Schema}\"");
        int schemaIdx = json.IndexOf("\"$schema\"", StringComparison.Ordinal);
        int workloadsIdx = json.IndexOf("\"workloads\"", StringComparison.Ordinal);
        (schemaIdx >= 0 && workloadsIdx >= 0).Should().BeTrue();
        (schemaIdx < workloadsIdx).Should().BeTrue("$schema must appear before workloads.");
    }

    [Fact]
    public async Task GetWorkloads_AcceptsRegistry_WithKnownSchemaUrl()
    {
        await WriteRawRegistryAsync($$"""
            {
              "$schema":"{{V1Schema}}",
              "workloads": [
                { "packageId": "Pkg", "packageVersion": "1.0.0", "entryPoint": { "assemblyPath": "X.dll", "type": "X" } }
              ]
            }
            """);

        IReadOnlyList<WorkloadEntry> workloads = await _store.GetWorkloadsAsync();

        workloads.Should().ContainSingle();
    }

    [Fact]
    public async Task GetWorkloads_AcceptsRegistry_WithMissingSchema_AsLegacyV1()
    {
        // Registries written before $schema landed must still load. They are
        // implicitly v1 and re-emitted with the field on next write.
        await WriteRawRegistryAsync("""
            {
              "workloads": [
                { "packageId": "Pkg", "packageVersion": "1.0.0", "entryPoint": { "assemblyPath": "X.dll", "type": "X" } }
              ]
            }
            """);

        IReadOnlyList<WorkloadEntry> workloads = await _store.GetWorkloadsAsync();

        workloads.Should().ContainSingle();
    }

    [Fact]
    public async Task GetWorkloads_RejectsRegistry_WithUnknownSchemaUrl()
    {
        await WriteRawRegistryAsync("""
            {
              "$schema": "https://aka.ms/func/workloads/v999/schema.json",
              "workloads": []
            }
            """);

        GracefulException ex = (await FluentActions.Awaiting(() => _store.GetWorkloadsAsync()).Should().ThrowAsync<GracefulException>()).Which;

        ex.IsUserError.Should().BeTrue();
        ex.Message.Should().Contain("v999");
        ex.Message.Should().ContainEquivalentOf("not supported");
        ex.Message.Should().ContainEquivalentOf("Supported schemas");
        ex.Message.Should().Contain(WorkloadManifestSchema.RegistryV1Schema);
        ex.Message.Should().ContainEquivalentOf("updating the CLI");
    }

    [Fact]
    public async Task RoundTrip_PreservesSchemaUrl()
    {
        await _store.SaveWorkloadAsync(NewEntry("Pkg", "1.0.0"));
        await _store.GetWorkloadsAsync();
        await _store.SaveWorkloadAsync(NewEntry("Pkg2", "2.0.0"));

        string json = await File.ReadAllTextAsync(_registryPath);

        json.Should().Contain($"\"$schema\":\"{V1Schema}\"");
    }

    private static WorkloadEntry NewEntry(string packageId, string version) => new()
    {
        PackageId = packageId,
        PackageVersion = version,
        EntryPoint = new EntryPointSpec { AssemblyPath = "Stub.dll", Type = "Stub" },
    };

    private async Task WriteRawRegistryAsync(string json)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_registryPath)!);
        await File.WriteAllTextAsync(_registryPath, json);
    }
}
