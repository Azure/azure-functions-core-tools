// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Workloads.Discovery;
using Azure.Functions.Cli.Workloads.Storage;
using Xunit;

namespace Azure.Functions.Cli.Tests.Workloads.Discovery;

public class WorkloadEntryPointScannerTests : IDisposable
{
    private const string FixtureAssemblyFile = "Azure.Functions.Cli.Tests.Fixtures.Workload.dll";
    private const string FixtureTypeName = "Azure.Functions.Cli.Tests.Fixtures.Workload.StubWorkload";
    private const string SecondFixtureAssemblyFile = "Azure.Functions.Cli.Tests.Fixtures.Workload.Second.dll";
    private const string CrossAssemblyFixtureFile = "Azure.Functions.Cli.Tests.Fixtures.Workload.CrossAssembly.dll";

    private readonly string _tempDir;

    public WorkloadEntryPointScannerTests()
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
    public void Scan_ReturnsEntryPoint_PointingAtAssemblyAndType()
    {
        CopyFixtureToTemp(FixtureAssemblyFile);
        var scanner = new WorkloadEntryPointScanner();

        var entry = scanner.Scan(_tempDir);

        Assert.Equal(FixtureAssemblyFile, entry.Assembly);
        Assert.Equal(FixtureTypeName, entry.Type);
    }

    [Fact]
    public void Scan_Throws_WhenNoAssemblyDeclaresAttribute()
    {
        File.Copy(
            Path.Combine(AppContext.BaseDirectory, "xunit.assert.dll"),
            Path.Combine(_tempDir, "xunit.assert.dll"));
        var scanner = new WorkloadEntryPointScanner();

        var ex = Assert.Throws<GracefulException>(() => scanner.Scan(_tempDir));

        Assert.True(ex.IsUserError);
        Assert.Contains(_tempDir, ex.Message);
        Assert.Contains("No workload entry point", ex.Message);
    }

    [Fact]
    public void Scan_Throws_WhenMultipleAssembliesDeclareAttribute()
    {
        CopyFixtureToTemp(FixtureAssemblyFile);
        CopyFixtureToTemp(SecondFixtureAssemblyFile);
        var scanner = new WorkloadEntryPointScanner();

        var ex = Assert.Throws<GracefulException>(() => scanner.Scan(_tempDir));

        Assert.True(ex.IsUserError);
        Assert.Contains(_tempDir, ex.Message);
        Assert.Contains("Multiple workload entry points", ex.Message);
        Assert.Contains(FixtureAssemblyFile, ex.Message);
        Assert.Contains(SecondFixtureAssemblyFile, ex.Message);
    }

    [Fact]
    public void Scan_Throws_WhenWorkloadTypeLivesInDifferentAssembly()
    {
        CopyFixtureToTemp(CrossAssemblyFixtureFile);
        var scanner = new WorkloadEntryPointScanner();

        var ex = Assert.Throws<GracefulException>(() => scanner.Scan(_tempDir));

        Assert.True(ex.IsUserError);
        Assert.Contains(CrossAssemblyFixtureFile, ex.Message);
        Assert.Contains("different assembly", ex.Message);
    }

    [Fact]
    public void Scan_SkipsFiles_ThatAreNotManagedAssemblies()
    {
        CopyFixtureToTemp(FixtureAssemblyFile);
        File.WriteAllBytes(Path.Combine(_tempDir, "native.dll"), new byte[] { 0x4D, 0x5A, 0x00, 0x01 });
        var scanner = new WorkloadEntryPointScanner();

        var entry = scanner.Scan(_tempDir);

        Assert.Equal(FixtureAssemblyFile, entry.Assembly);
    }

    [Fact]
    public void Scan_Throws_WhenInstallDirectoryDoesNotExist()
    {
        var scanner = new WorkloadEntryPointScanner();
        var missing = Path.Combine(_tempDir, "does-not-exist");

        var ex = Assert.Throws<GracefulException>(() => scanner.Scan(missing));

        Assert.True(ex.IsUserError);
        Assert.Contains(missing, ex.Message);
    }

    private void CopyFixtureToTemp(string fixtureFile)
    {
        var src = Path.Combine(AppContext.BaseDirectory, fixtureFile);
        File.Copy(src, Path.Combine(_tempDir, fixtureFile));
    }
}
