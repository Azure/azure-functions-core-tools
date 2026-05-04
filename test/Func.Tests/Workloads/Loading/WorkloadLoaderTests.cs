// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Runtime.Loader;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Workloads;
using Azure.Functions.Cli.Workloads.Loading;
using Azure.Functions.Cli.Workloads.Storage;
using Xunit;

namespace Azure.Functions.Cli.Tests.Workloads.Loading;

public class WorkloadLoaderTests
{
    private const string FixturePackageId = "Azure.Functions.Cli.Tests.Fixtures.Workload";
    private const string FixtureAssemblyFile = "Azure.Functions.Cli.Tests.Fixtures.Workload.dll";
    private const string FixtureTypeName = "Azure.Functions.Cli.Tests.Fixtures.Workload.StubWorkload";

    private const string SdkFixturePackageId = "Azure.Functions.Cli.Tests.Fixtures.WorkloadSdk";
    private const string SdkFixtureAssemblyFile = "Azure.Functions.Cli.Tests.Fixtures.WorkloadSdk.dll";
    private const string SdkFixtureTypeName = "Azure.Functions.Cli.Tests.Fixtures.WorkloadSdk.StubWorkload";

    [Fact]
    public void LoadInstalled_ReturnsEmpty_WhenNoEntries()
    {
        var loader = new WorkloadLoader();

        var loaded = loader.LoadInstalled(Array.Empty<InstalledWorkload>());

        Assert.Empty(loaded);
    }

    [Fact]
    public void LoadInstalled_HydratesEntry_FromAssemblyOnDisk()
    {
        var loader = new WorkloadLoader();

        var loaded = loader.LoadInstalled(new[] { FixtureEntry(FixturePackageId) });

        var item = Assert.Single(loaded);
        Assert.Equal(FixtureTypeName, item.Instance.GetType().FullName);
        Assert.Equal(FixturePackageId, item.Info.PackageId);
    }

    [Fact]
    public void LoadInstalled_Throws_WhenAssemblyFileMissing()
    {
        var loader = new WorkloadLoader();
        var entry = FixtureEntry(FixturePackageId, assemblyFileOverride: "DoesNotExist.dll");

        var ex = Assert.Throws<GracefulException>(() => loader.LoadInstalled(new[] { entry }));

        Assert.True(ex.IsUserError);
        Assert.StartsWith($"[{FixturePackageId}]", ex.Message);
        Assert.Contains("DoesNotExist.dll", ex.Message);
        Assert.Contains(entry.Entry.InstallPath, ex.Message);
    }

    [Fact]
    public void LoadInstalled_Throws_WhenTypeNotFoundInAssembly()
    {
        var loader = new WorkloadLoader();
        var entry = FixtureEntry(FixturePackageId, typeNameOverride: "Some.Missing.Type");

        var ex = Assert.Throws<GracefulException>(() => loader.LoadInstalled(new[] { entry }));

        Assert.True(ex.IsUserError);
        Assert.StartsWith($"[{FixturePackageId}]", ex.Message);
        Assert.Contains("Some.Missing.Type", ex.Message);
        Assert.Contains(entry.Entry.InstallPath, ex.Message);
    }

    [Fact]
    public void LoadInstalled_Throws_WhenTypeDoesNotImplementIWorkload()
    {
        var loader = new WorkloadLoader();
        var entry = FixtureEntry(
            FixturePackageId,
            typeNameOverride: "Azure.Functions.Cli.Tests.Fixtures.Workload.NotAWorkload");

        var ex = Assert.Throws<GracefulException>(() => loader.LoadInstalled(new[] { entry }));

        Assert.True(ex.IsUserError);
        Assert.StartsWith($"[{FixturePackageId}]", ex.Message);
        Assert.Contains("NotAWorkload", ex.Message);
        Assert.Contains(nameof(IWorkload), ex.Message);
        Assert.Contains(entry.Entry.InstallPath, ex.Message);
    }

    [Fact]
    public void LoadInstalled_LoadsEachWorkloadInOwnAssemblyLoadContext()
    {
        var loader = new WorkloadLoader();
        var entries = new[]
        {
            FixtureEntry("fixture-a"),
            FixtureEntry("fixture-b"),
        };

        var loaded = loader.LoadInstalled(entries);

        Assert.Equal(2, loaded.Count);
        var ctxA = AssemblyLoadContext.GetLoadContext(loaded[0].Instance.GetType().Assembly);
        var ctxB = AssemblyLoadContext.GetLoadContext(loaded[1].Instance.GetType().Assembly);
        Assert.NotNull(ctxA);
        Assert.NotNull(ctxB);
        Assert.NotSame(ctxA, ctxB);
        Assert.NotSame(AssemblyLoadContext.Default, ctxA);
    }

    [Fact]
    public void LoadInstalled_LoadContextIsCollectible()
    {
        var loader = new WorkloadLoader();

        var loaded = loader.LoadInstalled(new[] { FixtureEntry("collectible-fixture") });

        var ctx = AssemblyLoadContext.GetLoadContext(loaded[0].Instance.GetType().Assembly);
        Assert.NotNull(ctx);
        Assert.True(
            ctx!.IsCollectible,
            "WorkloadLoadContext must be collectible so a single CLI invocation can release the workload's DLL handle.");
    }

    [Fact]
    public void LoadInstalled_DelegatesAbstractionsAssemblyToDefaultContext()
    {
        var loader = new WorkloadLoader();

        var loaded = loader.LoadInstalled(new[] { FixtureEntry("identity-fixture") });

        // The IWorkload type the workload was activated as must be the same Type
        // identity the host knows. If the load context loaded its own copy of
        // Azure.Functions.Cli.Abstractions, the cast in WorkloadLoader would
        // have failed; this test pins that contract.
        var hostIWorkload = typeof(IWorkload);
        var workloadIWorkload = loaded[0].Instance.GetType()
            .GetInterfaces()
            .Single(i => i.FullName == hostIWorkload.FullName);
        Assert.Same(hostIWorkload, workloadIWorkload);
        Assert.Same(hostIWorkload.Assembly, workloadIWorkload.Assembly);
    }

    [Fact]
    public void LoadInstalled_HydratesEntry_WithSdkShapedFixture()
    {
        // Sibling of LoadInstalled_HydratesEntry_FromAssemblyOnDisk, but using
        // the Func.Tests.Fixtures.WorkloadSdk project, whose csproj follows the
        // future workload SDK convention (Private="false",
        // ExcludeAssets="runtime") so its deps.json does not list the contract
        // assemblies as runtime assets. This exercises the natural-resolution
        // path: AssemblyDependencyResolver returns null for the contract
        // assemblies and the default context resolves them — distinct from the
        // defensive intercept path exercised by the sibling test using the
        // mis-packaged Func.Tests.Fixtures.Workload fixture.
        var loader = new WorkloadLoader();

        var loaded = loader.LoadInstalled(new[] { SdkFixtureEntry(SdkFixturePackageId) });

        var item = Assert.Single(loaded);
        Assert.Equal(SdkFixtureTypeName, item.Instance.GetType().FullName);

        var hostIWorkload = typeof(IWorkload);
        var workloadIWorkload = item.Instance.GetType()
            .GetInterfaces()
            .Single(i => i.FullName == hostIWorkload.FullName);
        Assert.Same(hostIWorkload, workloadIWorkload);
        Assert.Same(hostIWorkload.Assembly, workloadIWorkload.Assembly);
    }

    [Fact]
    public void SdkShapedFixture_ExcludesContractAssemblyFromRuntimeClosure()
    {
        // Pins the SDK convention end-to-end: the WorkloadSdk fixture project
        // must NOT ship Azure.Functions.Cli.Abstractions in its runtime closure
        // (deps.json). If this fails, someone removed Private="false" /
        // ExcludeAssets="runtime" from Func.Tests.Fixtures.WorkloadSdk.csproj
        // and the fixture is silently no longer exercising the natural-
        // resolution path — the LoadInstalled_HydratesEntry_WithSdkShapedFixture
        // test would still pass via the loader's defensive intercept, hiding
        // the regression. Asserting the resolver invariant directly is what
        // separates the two test cases.
        var fixturePath = Path.Combine(AppContext.BaseDirectory, SdkFixtureAssemblyFile);
        var resolver = new AssemblyDependencyResolver(fixturePath);

        var resolved = resolver.ResolveAssemblyToPath(
            new System.Reflection.AssemblyName("Azure.Functions.Cli.Abstractions"));

        Assert.Null(resolved);
    }

    private static InstalledWorkload FixtureEntry(
        string packageId,
        string? assemblyFileOverride = null,
        string? typeNameOverride = null) => new(
            packageId,
            "1.0.0",
            new GlobalManifestEntry
            {
                DisplayName = packageId,
                Description = string.Empty,
                InstallPath = AppContext.BaseDirectory,
                EntryPoint = new EntryPointSpec
                {
                    Assembly = assemblyFileOverride ?? FixtureAssemblyFile,
                    Type = typeNameOverride ?? FixtureTypeName,
                },
            });

    private static InstalledWorkload SdkFixtureEntry(string packageId) => new(
        packageId,
        "1.0.0",
        new GlobalManifestEntry
        {
            DisplayName = packageId,
            Description = string.Empty,
            InstallPath = AppContext.BaseDirectory,
            EntryPoint = new EntryPointSpec
            {
                Assembly = SdkFixtureAssemblyFile,
                Type = SdkFixtureTypeName,
            },
        });
}
