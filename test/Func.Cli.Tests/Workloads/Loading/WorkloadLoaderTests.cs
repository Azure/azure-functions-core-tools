// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Workloads;
using Azure.Functions.Cli.Workloads.Loading;
using Azure.Functions.Cli.Workloads.Storage;
using Xunit;

namespace Azure.Functions.Cli.Tests.Workloads.Loading;

public class WorkloadLoaderTests
{
    private const string FixtureAssemblyFile = "Azure.Functions.Cli.Tests.Fixtures.Workload.dll";
    private const string FixtureTypeName = "Azure.Functions.Cli.Tests.Fixtures.Workload.StubWorkload";

    [Fact]
    public async Task LoadInstalledAsync_ReturnsEmpty_WhenManifestIsEmpty()
    {
        var loader = new WorkloadLoader();

        var loaded = await loader.LoadInstalledAsync(new GlobalManifest(), CancellationToken.None);

        Assert.Empty(loaded);
    }

    [Fact]
    public async Task LoadInstalledAsync_HydratesEntry_FromAssemblyOnDisk()
    {
        var manifest = ManifestForFixture();
        var loader = new WorkloadLoader();

        var loaded = await loader.LoadInstalledAsync(manifest, CancellationToken.None);

        var item = Assert.Single(loaded);
        Assert.Equal(FixtureTypeName, item.Instance.GetType().FullName);
        Assert.Equal("Azure.Functions.Cli.Tests.Fixtures.Workload", item.Info.PackageId);
    }

    [Fact]
    public async Task LoadInstalledAsync_Throws_WhenAssemblyFileMissing()
    {
        var manifest = ManifestForFixture(assemblyFileOverride: "DoesNotExist.dll");
        var loader = new WorkloadLoader();

        var ex = await Assert.ThrowsAsync<GracefulException>(
            () => loader.LoadInstalledAsync(manifest, CancellationToken.None));

        Assert.True(ex.IsUserError);
        Assert.Contains("Azure.Functions.Cli.Tests.Fixtures.Workload", ex.Message);
        Assert.Contains("DoesNotExist.dll", ex.Message);
    }

    [Fact]
    public async Task LoadInstalledAsync_Throws_WhenTypeNotFoundInAssembly()
    {
        var manifest = ManifestForFixture(typeNameOverride: "Some.Missing.Type");
        var loader = new WorkloadLoader();

        var ex = await Assert.ThrowsAsync<GracefulException>(
            () => loader.LoadInstalledAsync(manifest, CancellationToken.None));

        Assert.True(ex.IsUserError);
        Assert.Contains("Azure.Functions.Cli.Tests.Fixtures.Workload", ex.Message);
        Assert.Contains("Some.Missing.Type", ex.Message);
    }

    [Fact]
    public async Task LoadInstalledAsync_Throws_WhenTypeDoesNotImplementIWorkload()
    {
        var manifest = ManifestForFixture(
            typeNameOverride: "Azure.Functions.Cli.Tests.Fixtures.Workload.NotAWorkload");
        var loader = new WorkloadLoader();

        var ex = await Assert.ThrowsAsync<GracefulException>(
            () => loader.LoadInstalledAsync(manifest, CancellationToken.None));

        Assert.True(ex.IsUserError);
        Assert.Contains("NotAWorkload", ex.Message);
        Assert.Contains(nameof(IWorkload), ex.Message);
    }

    [Fact]
    public async Task LoadInstalledAsync_LoadsEachWorkloadInOwnAssemblyLoadContext()
    {
        var dir = AppContext.BaseDirectory;
        var manifest = new GlobalManifest
        {
            Workloads =
            {
                FixtureEntry(packageId: "fixture-a", dir),
                FixtureEntry(packageId: "fixture-b", dir),
            },
        };
        var loader = new WorkloadLoader();

        var loaded = await loader.LoadInstalledAsync(manifest, CancellationToken.None);

        Assert.Equal(2, loaded.Count);
        var ctxA = System.Runtime.Loader.AssemblyLoadContext.GetLoadContext(loaded[0].Instance.GetType().Assembly);
        var ctxB = System.Runtime.Loader.AssemblyLoadContext.GetLoadContext(loaded[1].Instance.GetType().Assembly);
        Assert.NotNull(ctxA);
        Assert.NotNull(ctxB);
        Assert.NotSame(ctxA, ctxB);
        Assert.NotSame(System.Runtime.Loader.AssemblyLoadContext.Default, ctxA);
    }

    private static GlobalManifestEntry FixtureEntry(string packageId, string dir) => new()
    {
        PackageId = packageId,
        DisplayName = packageId,
        Description = string.Empty,
        Version = "1.0.0",
        InstallPath = dir,
        EntryPoint = new EntryPointSpec
        {
            Assembly = FixtureAssemblyFile,
            Type = FixtureTypeName,
        },
    };

    private static GlobalManifest ManifestForFixture(
        string? assemblyFileOverride = null,
        string? typeNameOverride = null)
    {
        var dir = AppContext.BaseDirectory;
        return new GlobalManifest
        {
            Workloads =
            {
                new GlobalManifestEntry
                {
                    PackageId = "Azure.Functions.Cli.Tests.Fixtures.Workload",
                    DisplayName = "Stub",
                    Description = string.Empty,
                    Version = "1.0.0",
                    InstallPath = dir,
                    EntryPoint = new EntryPointSpec
                    {
                        Assembly = assemblyFileOverride ?? FixtureAssemblyFile,
                        Type = typeNameOverride ?? FixtureTypeName,
                    },
                },
            },
        };
    }
}
