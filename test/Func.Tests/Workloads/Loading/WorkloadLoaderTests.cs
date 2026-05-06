// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Runtime.Loader;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Workloads;
using Azure.Functions.Cli.Workloads.Loading;
using Azure.Functions.Cli.Workloads.Storage;
using NSubstitute;
using Xunit;

namespace Azure.Functions.Cli.Tests.Workloads.Loading;

public class WorkloadLoaderTests
{
    private const string FixturePackageId = "Azure.Functions.Cli.Workload.Tests.Fixtures.Default";
    private const string FixtureAssemblyFile = "Azure.Functions.Cli.Workload.Tests.Fixtures.Default.dll";
    private const string FixtureTypeName = "Azure.Functions.Cli.Workload.Tests.Fixtures.Default.StubWorkload";

    private const string SdkFixturePackageId = "Azure.Functions.Cli.Workload.Tests.Fixtures.Sdk";
    private const string SdkFixtureAssemblyFile = "Azure.Functions.Cli.Workload.Tests.Fixtures.Sdk.dll";
    private const string SdkFixtureTypeName = "Azure.Functions.Cli.Workload.Tests.Fixtures.Sdk.StubWorkload";

    [Fact]
    public void Load_ReturnsEmpty_WhenNoEntries()
    {
        var loader = new WorkloadLoader(StubPaths());

        var loaded = loader.Load(Array.Empty<WorkloadEntry>());

        Assert.Empty(loaded);
    }

    [Fact]
    public void Load_HydratesEntry_FromAssemblyOnDisk()
    {
        var loader = new WorkloadLoader(StubPaths());

        var loaded = loader.Load(new[] { FixtureEntry(FixturePackageId) });

        var item = Assert.Single(loaded);
        Assert.Equal(FixtureTypeName, item.Instance.GetType().FullName);
        Assert.Equal(FixturePackageId, item.PackageId);
    }

    [Fact]
    public void Load_Throws_WhenAssemblyFileMissing()
    {
        var loader = new WorkloadLoader(StubPaths());
        var entry = FixtureEntry(FixturePackageId, assemblyFileOverride: "DoesNotExist.dll");

        var ex = Assert.Throws<GracefulException>(() => loader.Load(new[] { entry }));

        Assert.True(ex.IsUserError);
        Assert.StartsWith($"[{FixturePackageId}]", ex.Message);
        Assert.Contains("DoesNotExist.dll", ex.Message);
        Assert.Contains(AppContext.BaseDirectory, ex.Message);
    }

    [Fact]
    public void Load_Throws_WhenTypeNotFoundInAssembly()
    {
        var loader = new WorkloadLoader(StubPaths());
        var entry = FixtureEntry(FixturePackageId, typeNameOverride: "Some.Missing.Type");

        var ex = Assert.Throws<GracefulException>(() => loader.Load(new[] { entry }));

        Assert.True(ex.IsUserError);
        Assert.StartsWith($"[{FixturePackageId}]", ex.Message);
        Assert.Contains("Some.Missing.Type", ex.Message);
        Assert.Contains(AppContext.BaseDirectory, ex.Message);
    }

    [Fact]
    public void Load_Throws_WhenTypeDoesNotDeriveFromWorkload()
    {
        var loader = new WorkloadLoader(StubPaths());
        var entry = FixtureEntry(
            FixturePackageId,
            typeNameOverride: "Azure.Functions.Cli.Workload.Tests.Fixtures.Default.NotAWorkload");

        var ex = Assert.Throws<GracefulException>(() => loader.Load(new[] { entry }));

        Assert.True(ex.IsUserError);
        Assert.StartsWith($"[{FixturePackageId}]", ex.Message);
        Assert.Contains("NotAWorkload", ex.Message);
        Assert.Contains(nameof(Workload), ex.Message);
        Assert.Contains(AppContext.BaseDirectory, ex.Message);
    }

    [Fact]
    public void Load_LoadsEachWorkloadInOwnAssemblyLoadContext()
    {
        var loader = new WorkloadLoader(StubPaths());
        var entries = new[]
        {
            FixtureEntry("fixture-a"),
            FixtureEntry("fixture-b"),
        };

        var loaded = loader.Load(entries);

        Assert.Equal(2, loaded.Count);
        var ctxA = AssemblyLoadContext.GetLoadContext(loaded[0].Instance.GetType().Assembly);
        var ctxB = AssemblyLoadContext.GetLoadContext(loaded[1].Instance.GetType().Assembly);
        Assert.NotNull(ctxA);
        Assert.NotNull(ctxB);
        Assert.NotSame(ctxA, ctxB);
        Assert.NotSame(AssemblyLoadContext.Default, ctxA);
    }

    [Fact]
    public void Load_LoadContextIsCollectible()
    {
        var loader = new WorkloadLoader(StubPaths());

        var loaded = loader.Load(new[] { FixtureEntry("collectible-fixture") });

        var ctx = AssemblyLoadContext.GetLoadContext(loaded[0].Instance.GetType().Assembly);
        Assert.NotNull(ctx);
        Assert.True(
            ctx!.IsCollectible,
            "WorkloadLoadContext must be collectible so a single CLI invocation can release the workload's DLL handle.");
    }

    [Fact]
    public void Load_DelegatesAbstractionsAssemblyToDefaultContext()
    {
        var loader = new WorkloadLoader(StubPaths());

        var loaded = loader.Load(new[] { FixtureEntry("identity-fixture") });

        // The Workload type the workload was activated as must be the same Type
        // identity the host knows. If the load context loaded its own copy of
        // Azure.Functions.Cli.Abstractions, the cast in WorkloadLoader would
        // have failed; this test pins that contract.
        var hostWorkload = typeof(Workload);
        var workloadBase = loaded[0].Instance.GetType().BaseType;
        Assert.NotNull(workloadBase);
        Assert.Same(hostWorkload, workloadBase);
        Assert.Same(hostWorkload.Assembly, workloadBase!.Assembly);
    }

    [Fact]
    public void Load_HydratesEntry_WithSdkShapedFixture()
    {
        // Sibling of Load_HydratesEntry_FromAssemblyOnDisk, but using
        // the Workload.Tests.Fixtures.Sdk project, whose csproj follows the
        // future workload SDK convention (Private="false",
        // ExcludeAssets="runtime") so its deps.json does not list the contract
        // assemblies as runtime assets. This exercises the natural-resolution
        // path: AssemblyDependencyResolver returns null for the contract
        // assemblies and the default context resolves them, distinct from the
        // defensive intercept path exercised by the sibling test using the
        // mis-packaged Workload.Tests.Fixtures.Default fixture.
        var loader = new WorkloadLoader(StubPaths());

        var loaded = loader.Load(new[] { SdkFixtureEntry(SdkFixturePackageId) });

        var item = Assert.Single(loaded);
        Assert.Equal(SdkFixtureTypeName, item.Instance.GetType().FullName);

        var hostWorkload = typeof(Workload);
        var workloadBase = item.Instance.GetType().BaseType;
        Assert.NotNull(workloadBase);
        Assert.Same(hostWorkload, workloadBase);
        Assert.Same(hostWorkload.Assembly, workloadBase!.Assembly);
    }

    [Fact]
    public void SdkShapedFixture_ExcludesContractAssemblyFromRuntimeClosure()
    {
        // Pins the SDK convention end-to-end: the WorkloadSdk fixture project
        // must NOT ship Azure.Functions.Cli.Abstractions in its runtime closure
        // (deps.json). If this fails, someone removed Private="false" /
        // ExcludeAssets="runtime" from Workload.Tests.Fixtures.Sdk.csproj
        // and the fixture is silently no longer exercising the natural-
        // resolution path: the Load_HydratesEntry_WithSdkShapedFixture
        // test would still pass via the loader's defensive intercept, hiding
        // the regression. Asserting the resolver invariant directly is what
        // separates the two test cases.
        var fixturePath = Path.Combine(AppContext.BaseDirectory, "tools", SdkFixtureAssemblyFile);
        var resolver = new AssemblyDependencyResolver(fixturePath);

        var resolved = resolver.ResolveAssemblyToPath(
            new System.Reflection.AssemblyName("Azure.Functions.Cli.Abstractions"));

        Assert.Null(resolved);
    }

    [Fact]
    public void Ctor_NullPaths_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new WorkloadLoader(null!));
    }

    private static IWorkloadPaths StubPaths()
    {
        // Loader resolves the assembly via paths.GetInstallDirectory(...).
        // Test fixtures sit next to the test bin directory, so return that
        // for any (packageId, version) pair the tests pass.
        var paths = Substitute.For<IWorkloadPaths>();
        paths.GetInstallDirectory(Arg.Any<string>(), Arg.Any<string>())
            .Returns(AppContext.BaseDirectory);
        return paths;
    }

    private static WorkloadEntry FixtureEntry(
        string packageId,
        string? assemblyFileOverride = null,
        string? typeNameOverride = null) => new()
        {
            PackageId = packageId,
            PackageVersion = "1.0.0",
            EntryPoint = new EntryPointSpec
            {
                AssemblyPath = assemblyFileOverride ?? FixtureAssemblyFile,
                Type = typeNameOverride ?? FixtureTypeName,
            },
        };

    private static WorkloadEntry SdkFixtureEntry(string packageId) => new()
    {
        PackageId = packageId,
        PackageVersion = "1.0.0",
        EntryPoint = new EntryPointSpec
        {
            AssemblyPath = SdkFixtureAssemblyFile,
            Type = SdkFixtureTypeName,
        },
    };
}
