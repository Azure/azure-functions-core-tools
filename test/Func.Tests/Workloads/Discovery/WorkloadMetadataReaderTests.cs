// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Workloads;
using Azure.Functions.Cli.Workloads.Discovery;
using Xunit;

namespace Azure.Functions.Cli.Tests.Workloads.Discovery;

public class WorkloadMetadataReaderTests : IDisposable
{
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
            """
            {
              "entryPoint": {
                "assemblyPath": "Foo.dll",
                "type": "Foo.MyWorkload"
              }
            }
            """);

        WorkloadMetadata metadata = _reader.Read(_tempDir);

        Assert.Equal("Foo.dll", metadata.EntryPoint.AssemblyPath);
        Assert.Equal("Foo.MyWorkload", metadata.EntryPoint.Type);
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
    public void Read_Throws_WhenEntryPointMissing()
    {
        WriteMetadata("{}");

        // `required` on init-only properties surfaces as a JsonException;
        // the reader wraps it as an InvalidWorkloadException so callers only
        // handle one type.
        Assert.Throws<InvalidWorkloadException>(() => _reader.Read(_tempDir));
    }

    [Fact]
    public void Read_Throws_WhenAssemblyPathBlank()
    {
        WriteMetadata(
            """
            {
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
            """
            {
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

    [Fact]
    public void Read_HydratesShippedFixtureMetadata()
    {
        // The Workload.Tests.Fixtures.Default project ships a workload.json
        // copied next to its assembly. This test pins the contract that the
        // reader handles a real (not synthesized) fixture manifest, so a
        // breaking change to the on-disk shape (or to the fixture's manifest)
        // surfaces here.
        WorkloadMetadata metadata = _reader.Read(AppContext.BaseDirectory);

        Assert.Equal(
            "Azure.Functions.Cli.Workload.Tests.Fixtures.Default.dll",
            metadata.EntryPoint.AssemblyPath);
        Assert.Equal(
            "Azure.Functions.Cli.Workload.Tests.Fixtures.Default.StubWorkload",
            metadata.EntryPoint.Type);
    }

    private void WriteMetadata(string json)
        => File.WriteAllText(
            Path.Combine(_tempDir, WorkloadMetadataReader.MetadataFileName),
            json);
}
