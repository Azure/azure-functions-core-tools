// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Workloads;
using Azure.Functions.Cli.Workloads.Discovery;
using Azure.Functions.Cli.Workloads.Storage;
using Xunit;

namespace Azure.Functions.Cli.Tests.Workloads.Discovery;

public class WorkloadMetadataReaderTests : IDisposable
{
    private const string SchemaUrl = WorkloadManifestSchema.PackageManifestV1Schema;

    private readonly string _tempDir;
    private readonly WorkloadMetadataReader _reader = new();

    public WorkloadMetadataReaderTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, recursive: true);
        }
    }

    [Fact]
    public void Read_ReturnsMetadata_WhenManifestIsValid()
    {
        WriteMetadata(
            $$"""
            {
              "$schema": "{{SchemaUrl}}",
              "kind": "workload",
              "entryPoint": {
                "assemblyPath": "Foo.dll",
                "type": "Foo.MyWorkload"
              }
            }
            """);

        WorkloadMetadata metadata = _reader.Read(_tempDir);

        Assert.Equal(SchemaUrl, metadata.Schema);
        Assert.Equal(WorkloadKind.Workload, metadata.Kind);
        Assert.Equal("Foo.dll", metadata.EntryPoint!.AssemblyPath);
        Assert.Equal("Foo.MyWorkload", metadata.EntryPoint.Type);
    }

    [Fact]
    public void Read_DefaultsKindToWorkload_WhenKindOmitted()
    {
        // workload-package-layout §5.4: kind defaults to "workload" when
        // omitted, matching the authoring default for normal workloads.
        WriteMetadata(
            $$"""
            {
              "$schema": "{{SchemaUrl}}",
              "entryPoint": {
                "assemblyPath": "Foo.dll",
                "type": "Foo.MyWorkload"
              }
            }
            """);

        WorkloadMetadata metadata = _reader.Read(_tempDir);

        Assert.Equal(WorkloadKind.Workload, metadata.Kind);
    }

    [Fact]
    public void Read_ReturnsContentMetadata_WhenKindIsContent()
    {
        // workload-package-layout §5.4: content packages declare "kind":
        // "content" and omit entryPoint entirely.
        WriteMetadata(
            $$"""
            {
              "$schema": "{{SchemaUrl}}",
              "kind": "content"
            }
            """);

        WorkloadMetadata metadata = _reader.Read(_tempDir);

        Assert.Equal(WorkloadKind.Content, metadata.Kind);
        Assert.Null(metadata.EntryPoint);
    }

    [Fact]
    public void Read_ReturnsMetaMetadata_WhenKindIsMeta()
    {
        WriteMetadata(
            $$"""
            {
              "$schema": "{{SchemaUrl}}",
              "kind": "meta"
            }
            """);

        WorkloadMetadata metadata = _reader.Read(_tempDir);

        Assert.Equal(WorkloadKind.Meta, metadata.Kind);
        Assert.Null(metadata.EntryPoint);
    }

    [Theory]
    [InlineData("content")]
    [InlineData("meta")]
    public void Read_Throws_WhenNonWorkloadKindDeclaresEntryPoint(string kind)
    {
        // workload-package-layout §5.4: entryPoint is forbidden for content
        // and meta. The author should remove it (or set kind to workload).
        WriteMetadata(
            $$"""
            {
              "$schema": "{{SchemaUrl}}",
              "kind": "{{kind}}",
              "entryPoint": {
                "assemblyPath": "Foo.dll",
                "type": "Foo.MyWorkload"
              }
            }
            """);

        InvalidWorkloadException ex = Assert.Throws<InvalidWorkloadException>(
            () => _reader.Read(_tempDir));

        Assert.Contains(kind, ex.Message);
        Assert.Contains("entryPoint", ex.Message);
    }

    [Fact]
    public void Read_Throws_WhenSchemaUnknown()
    {
        WriteMetadata(
            """
            {
              "$schema": "https://aka.ms/func-workloads/package/v999/schema.json",
              "kind": "workload",
              "entryPoint": {
                "assemblyPath": "Foo.dll",
                "type": "Foo.MyWorkload"
              }
            }
            """);

        InvalidWorkloadException ex = Assert.Throws<InvalidWorkloadException>(
            () => _reader.Read(_tempDir));

        Assert.Contains("schema", ex.Message);
        Assert.Contains("v999", ex.Message);
    }

    [Fact]
    public void Read_Throws_WhenManifestMissing()
    {
        InvalidWorkloadException ex = Assert.Throws<InvalidWorkloadException>(
            () => _reader.Read(_tempDir));

        Assert.Contains(_tempDir, ex.Message);
        Assert.Contains(WorkloadMetadataReader.MetadataFileName, ex.Message);
    }

    [Fact]
    public void Read_Throws_WhenManifestIsMalformed()
    {
        WriteMetadata("{ not valid json");

        InvalidWorkloadException ex = Assert.Throws<InvalidWorkloadException>(
            () => _reader.Read(_tempDir));

        Assert.Contains(WorkloadMetadataReader.MetadataFileName, ex.Message);
        Assert.IsType<System.Text.Json.JsonException>(ex.InnerException);
    }

    [Fact]
    public void Read_Throws_WhenWorkloadKindMissingEntryPoint()
    {
        // kind=workload requires entryPoint.
        WriteMetadata(
            $$"""
            {
              "$schema": "{{SchemaUrl}}",
              "kind": "workload"
            }
            """);

        InvalidWorkloadException ex = Assert.Throws<InvalidWorkloadException>(
            () => _reader.Read(_tempDir));

        Assert.Contains("entryPoint", ex.Message);
    }

    [Fact]
    public void Read_Throws_WhenAssemblyPathBlank()
    {
        WriteMetadata(
            $$"""
            {
              "$schema": "{{SchemaUrl}}",
              "kind": "workload",
              "entryPoint": {
                "assemblyPath": "   ",
                "type": "Foo.MyWorkload"
              }
            }
            """);

        InvalidWorkloadException ex = Assert.Throws<InvalidWorkloadException>(
            () => _reader.Read(_tempDir));

        Assert.Contains("assemblyPath", ex.Message);
    }

    [Fact]
    public void Read_Throws_WhenTypeBlank()
    {
        WriteMetadata(
            $$"""
            {
              "$schema": "{{SchemaUrl}}",
              "kind": "workload",
              "entryPoint": {
                "assemblyPath": "Foo.dll",
                "type": ""
              }
            }
            """);

        InvalidWorkloadException ex = Assert.Throws<InvalidWorkloadException>(
            () => _reader.Read(_tempDir));

        Assert.Contains("type", ex.Message);
    }

    [Fact]
    public void Read_Throws_WhenPackageDirectoryDoesNotExist()
    {
        string missing = Path.Combine(_tempDir, "does-not-exist");

        DirectoryNotFoundException ex = Assert.Throws<DirectoryNotFoundException>(
            () => _reader.Read(missing));

        Assert.Contains(missing, ex.Message);
    }

    [Theory]
    [InlineData("/etc/passwd.dll")]
    [InlineData("/abs/path/foo.dll")]
    public void Read_Throws_WhenAssemblyPathIsAbsolute(string assemblyPath)
    {
        WriteMetadata(
            $$"""
            {
              "$schema": "{{SchemaUrl}}",
              "kind": "workload",
              "entryPoint": {
                "assemblyPath": "{{assemblyPath}}",
                "type": "Foo.MyWorkload"
              }
            }
            """);

        InvalidWorkloadException ex = Assert.Throws<InvalidWorkloadException>(
            () => _reader.Read(_tempDir));

        Assert.Contains("absolute", ex.Message);
        Assert.Contains("assemblyPath", ex.Message);
    }

    [Theory]
    [InlineData("../escape.dll")]
    [InlineData("../../etc/passwd.dll")]
    [InlineData("nested/../../escape.dll")]
    public void Read_Throws_WhenAssemblyPathContainsParentSegment(string assemblyPath)
    {
        WriteMetadata(
            $$"""
            {
              "$schema": "{{SchemaUrl}}",
              "kind": "workload",
              "entryPoint": {
                "assemblyPath": "{{assemblyPath}}",
                "type": "Foo.MyWorkload"
              }
            }
            """);

        InvalidWorkloadException ex = Assert.Throws<InvalidWorkloadException>(
            () => _reader.Read(_tempDir));

        Assert.Contains("..", ex.Message);
        Assert.Contains("assemblyPath", ex.Message);
    }

    [Fact]
    public void Read_HydratesShippedFixtureMetadata()
    {
        // The Workloads.Tests.Fixtures.Default project ships a workload.json
        // copied next to its assembly. This test pins the contract that the
        // reader handles a real (not synthesized) fixture manifest, so a
        // breaking change to the on-disk shape (or to the fixture's manifest)
        // surfaces here.
        WorkloadMetadata metadata = _reader.Read(AppContext.BaseDirectory);

        Assert.Equal(WorkloadKind.Workload, metadata.Kind);
        Assert.Equal(
            "Azure.Functions.Cli.Workloads.Tests.Fixtures.Default.dll",
            metadata.EntryPoint!.AssemblyPath);
        Assert.Equal(
            "Azure.Functions.Cli.Workloads.Tests.Fixtures.Default.StubWorkload",
            metadata.EntryPoint.Type);
    }

    private void WriteMetadata(string json)
        => File.WriteAllText(
            Path.Combine(_tempDir, WorkloadMetadataReader.MetadataFileName),
            json);
}
