// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Workloads;
using Azure.Functions.Cli.Workloads.Discovery;
using Azure.Functions.Cli.Workloads.Storage;

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

        metadata.Schema.Should().Be(SchemaUrl);
        metadata.Kind.Should().Be(WorkloadKind.Workload);
        metadata.EntryPoint!.AssemblyPath.Should().Be("Foo.dll");
        metadata.EntryPoint.Type.Should().Be("Foo.MyWorkload");
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

        metadata.Kind.Should().Be(WorkloadKind.Workload);
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

        metadata.Kind.Should().Be(WorkloadKind.Content);
        metadata.EntryPoint.Should().BeNull();
    }

    [Fact]
    public void Read_ReturnsDisplayMetadata()
    {
        WriteMetadata(
            $$"""
            {
              "$schema": "{{SchemaUrl}}",
              "kind": "content",
              "displayName": "Functions Host",
              "description": "Azure Functions host payload."
            }
            """);

        WorkloadMetadata metadata = _reader.Read(_tempDir);

        metadata.DisplayName.Should().Be("Functions Host");
        metadata.Description.Should().Be("Azure Functions host payload.");
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

        metadata.Kind.Should().Be(WorkloadKind.Meta);
        metadata.EntryPoint.Should().BeNull();
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

        InvalidWorkloadException ex = FluentActions.Invoking(() => _reader.Read(_tempDir)).Should().ThrowExactly<InvalidWorkloadException>().Which;

        ex.Message.Should().Contain(kind);
        ex.Message.Should().Contain("entryPoint");
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

        InvalidWorkloadException ex = FluentActions.Invoking(() => _reader.Read(_tempDir)).Should().ThrowExactly<InvalidWorkloadException>().Which;

        ex.Message.Should().Contain("schema");
        ex.Message.Should().Contain("v999");
    }

    [Fact]
    public void Read_Throws_WhenManifestMissing()
    {
        InvalidWorkloadException ex = FluentActions.Invoking(() => _reader.Read(_tempDir)).Should().ThrowExactly<InvalidWorkloadException>().Which;

        ex.Message.Should().Contain(_tempDir);
        ex.Message.Should().Contain(WorkloadMetadataReader.MetadataFileName);
    }

    [Fact]
    public void Read_Throws_WhenManifestIsMalformed()
    {
        WriteMetadata("{ not valid json");

        InvalidWorkloadException ex = FluentActions.Invoking(() => _reader.Read(_tempDir)).Should().ThrowExactly<InvalidWorkloadException>().Which;

        ex.Message.Should().Contain(WorkloadMetadataReader.MetadataFileName);
        ex.InnerException.Should().BeOfType<System.Text.Json.JsonException>();
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

        InvalidWorkloadException ex = FluentActions.Invoking(() => _reader.Read(_tempDir)).Should().ThrowExactly<InvalidWorkloadException>().Which;

        ex.Message.Should().Contain("entryPoint");
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

        InvalidWorkloadException ex = FluentActions.Invoking(() => _reader.Read(_tempDir)).Should().ThrowExactly<InvalidWorkloadException>().Which;

        ex.Message.Should().Contain("assemblyPath");
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

        InvalidWorkloadException ex = FluentActions.Invoking(() => _reader.Read(_tempDir)).Should().ThrowExactly<InvalidWorkloadException>().Which;

        ex.Message.Should().Contain("type");
    }

    [Fact]
    public void Read_Throws_WhenPackageDirectoryDoesNotExist()
    {
        string missing = Path.Combine(_tempDir, "does-not-exist");

        DirectoryNotFoundException ex = FluentActions.Invoking(() => _reader.Read(missing)).Should().ThrowExactly<DirectoryNotFoundException>().Which;

        ex.Message.Should().Contain(missing);
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

        InvalidWorkloadException ex = FluentActions.Invoking(() => _reader.Read(_tempDir)).Should().ThrowExactly<InvalidWorkloadException>().Which;

        ex.Message.Should().Contain("absolute");
        ex.Message.Should().Contain("assemblyPath");
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

        InvalidWorkloadException ex = FluentActions.Invoking(() => _reader.Read(_tempDir)).Should().ThrowExactly<InvalidWorkloadException>().Which;

        ex.Message.Should().Contain("..");
        ex.Message.Should().Contain("assemblyPath");
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

        metadata.Kind.Should().Be(WorkloadKind.Workload);
        metadata.EntryPoint!.AssemblyPath.Should().Be("Azure.Functions.Cli.Workloads.Tests.Fixtures.Default.dll");
        metadata.EntryPoint.Type.Should().Be("Azure.Functions.Cli.Workloads.Tests.Fixtures.Default.StubWorkload");
    }

    private void WriteMetadata(string json)
        => File.WriteAllText(
            Path.Combine(_tempDir, WorkloadMetadataReader.MetadataFileName),
            json);
}
