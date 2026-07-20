// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Runtime.Loader;
using Azure.Functions.Cli.Workloads;
using Azure.Functions.Cli.Workloads.Discovery;
using Azure.Functions.Cli.Workloads.Loading;
using Azure.Functions.Cli.Workloads.Storage;
using NSubstitute;

namespace Azure.Functions.Cli.Tests.Workloads.Loading;

public class WorkloadLoaderTests
{
    private const string FixturePackageId = "Azure.Functions.Cli.Workloads.Tests.Fixtures.Default";
    private const string FixtureAssemblyFile = "Azure.Functions.Cli.Workloads.Tests.Fixtures.Default.dll";
    private const string FixtureTypeName = "Azure.Functions.Cli.Workloads.Tests.Fixtures.Default.StubWorkload";

    private const string SdkFixturePackageId = "Azure.Functions.Cli.Workloads.Tests.Fixtures.Sdk";
    private const string SdkFixtureAssemblyFile = "Azure.Functions.Cli.Workloads.Tests.Fixtures.Sdk.dll";
    private const string SdkFixtureTypeName = "Azure.Functions.Cli.Workloads.Tests.Fixtures.Sdk.StubWorkload";

    [Fact]
    public void Load_ReturnsEmpty_WhenNoEntries()
    {
        var loader = new WorkloadLoader(StubPaths());

        var loaded = loader.Load([]);

        loaded.Should().BeEmpty();
    }

    [Fact]
    public void Load_HydratesEntry_FromAssemblyOnDisk()
    {
        var loader = new WorkloadLoader(StubPaths());

        var loaded = loader.Load([FixtureEntry(FixturePackageId)]);

        var item = loaded.Should().ContainSingle().Subject;
        item.Instance.GetType().FullName.Should().Be(FixtureTypeName);
        item.PackageId.Should().Be(FixturePackageId);
    }

    [Fact]
    public void Load_Throws_WhenAssemblyFileMissing()
    {
        var loader = new WorkloadLoader(StubPaths());
        var entry = FixtureEntry(FixturePackageId, assemblyFileOverride: "DoesNotExist.dll");

        var ex = FluentActions.Invoking(() => loader.Load([entry])).Should().ThrowExactly<InvalidWorkloadException>().Which;

        ex.Message.Should().StartWith($"[{FixturePackageId}]");
        ex.Message.Should().Contain("DoesNotExist.dll");
        ex.Message.Should().Contain(AppContext.BaseDirectory);
    }

    [Fact]
    public void Load_Throws_WhenTypeNotFoundInAssembly()
    {
        var loader = new WorkloadLoader(StubPaths());
        var entry = FixtureEntry(FixturePackageId, typeNameOverride: "Some.Missing.Type");

        var ex = FluentActions.Invoking(() => loader.Load([entry])).Should().ThrowExactly<InvalidWorkloadException>().Which;

        ex.Message.Should().StartWith($"[{FixturePackageId}]");
        ex.Message.Should().Contain("Some.Missing.Type");
        ex.Message.Should().Contain(AppContext.BaseDirectory);
    }

    [Fact]
    public void Load_Throws_WhenTypeDoesNotDeriveFromWorkload()
    {
        var loader = new WorkloadLoader(StubPaths());
        var entry = FixtureEntry(
            FixturePackageId,
            typeNameOverride: "Azure.Functions.Cli.Workloads.Tests.Fixtures.Default.NotAWorkload");

        var ex = FluentActions.Invoking(() => loader.Load([entry])).Should().ThrowExactly<InvalidWorkloadException>().Which;

        ex.Message.Should().StartWith($"[{FixturePackageId}]");
        ex.Message.Should().Contain("NotAWorkload");
        ex.Message.Should().Contain(nameof(Workload));
        ex.Message.Should().Contain(AppContext.BaseDirectory);
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

        loaded.Count.Should().Be(2);
        var ctxA = AssemblyLoadContext.GetLoadContext(loaded[0].Instance.GetType().Assembly);
        var ctxB = AssemblyLoadContext.GetLoadContext(loaded[1].Instance.GetType().Assembly);
        ctxA.Should().NotBeNull();
        ctxB.Should().NotBeNull();
        ctxB.Should().NotBeSameAs(ctxA);
        ctxA.Should().NotBeSameAs(AssemblyLoadContext.Default);
    }

    [Fact]
    public void Load_LoadContextIsCollectible()
    {
        var loader = new WorkloadLoader(StubPaths());

        var loaded = loader.Load([FixtureEntry("collectible-fixture")]);

        var ctx = AssemblyLoadContext.GetLoadContext(loaded[0].Instance.GetType().Assembly);
        ctx.Should().NotBeNull();
        ctx!.IsCollectible.Should().BeTrue("WorkloadLoadContext must be collectible so a single CLI invocation can release the workload's DLL handle.");
    }

    [Fact]
    public void Load_DelegatesAbstractionsAssemblyToDefaultContext()
    {
        var loader = new WorkloadLoader(StubPaths());

        var loaded = loader.Load([FixtureEntry("identity-fixture")]);

        // The Workload type the workload was activated as must be the same Type
        // identity the host knows. If the load context loaded its own copy of
        // Azure.Functions.Cli.Abstractions, the cast in WorkloadLoader would
        // have failed; this test pins that contract.
        var hostWorkload = typeof(Workload);
        var workloadBase = loaded[0].Instance.GetType().BaseType;
        workloadBase.Should().NotBeNull();
        workloadBase.Should().BeSameAs(hostWorkload);
        workloadBase!.Assembly.Should().BeSameAs(hostWorkload.Assembly);
    }

    [Fact]
    public void Load_HydratesEntry_WithSdkShapedFixture()
    {
        // Sibling of Load_HydratesEntry_FromAssemblyOnDisk, but using
        // the Workloads.Tests.Fixtures.Sdk project, whose csproj follows the
        // future workload SDK convention (Private="false",
        // ExcludeAssets="runtime") so its deps.json does not list the contract
        // assemblies as runtime assets. This exercises the natural-resolution
        // path: AssemblyDependencyResolver returns null for the contract
        // assemblies and the default context resolves them, distinct from the
        // defensive intercept path exercised by the sibling test using the
        // mis-packaged Workloads.Tests.Fixtures.Default fixture.
        var loader = new WorkloadLoader(StubPaths());

        var loaded = loader.Load([SdkFixtureEntry(SdkFixturePackageId)]);

        var item = loaded.Should().ContainSingle().Subject;
        item.Instance.GetType().FullName.Should().Be(SdkFixtureTypeName);

        var hostWorkload = typeof(Workload);
        var workloadBase = item.Instance.GetType().BaseType;
        workloadBase.Should().NotBeNull();
        workloadBase.Should().BeSameAs(hostWorkload);
        workloadBase!.Assembly.Should().BeSameAs(hostWorkload.Assembly);
    }

    [Fact]
    public void SdkShapedFixture_ExcludesContractAssemblyFromRuntimeClosure()
    {
        // Pins the SDK convention end-to-end: the WorkloadSdk fixture project
        // must NOT ship Azure.Functions.Cli.Abstractions in its runtime closure
        // (deps.json). If this fails, someone removed Private="false" /
        // ExcludeAssets="runtime" from Workloads.Tests.Fixtures.Sdk.csproj
        // and the fixture is silently no longer exercising the natural-
        // resolution path: the Load_HydratesEntry_WithSdkShapedFixture
        // test would still pass via the loader's defensive intercept, hiding
        // the regression. Asserting the resolver invariant directly is what
        // separates the two test cases.
        var fixturePath = Path.Combine(AppContext.BaseDirectory, "tools", "any", SdkFixtureAssemblyFile);
        var resolver = new AssemblyDependencyResolver(fixturePath);

        var resolved = resolver.ResolveAssemblyToPath(
            new System.Reflection.AssemblyName("Azure.Functions.Cli.Abstractions"));

        resolved.Should().BeNull();
    }

    [Fact]
    public void Ctor_NullPaths_Throws()
    {
        FluentActions.Invoking(() => new WorkloadLoader(null!)).Should().ThrowExactly<ArgumentNullException>();
    }

    [Fact]
    public void Load_ResolvesAssembly_FromInstallRootDirectly()
    {
        // Simulates the new publish layout where the DLL lives directly relative
        // to the install root (where workload.json is), without tools/any/.
        string tempDir = Path.Combine(Path.GetTempPath(), $"wl-loader-test-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(tempDir);
            CopyFixtureToDirectory(FixtureAssemblyFile, tempDir);

            var paths = Substitute.For<IWorkloadPaths>();
            paths.WorkloadsRoot.Returns(tempDir);
            paths.GetInstallDirectory(Arg.Any<string>(), Arg.Any<string>()).Returns(tempDir);

            var loader = new WorkloadLoader(paths);
            var entry = FixtureEntry(FixturePackageId);

            var loaded = loader.Load([entry]);

            var item = loaded.Should().ContainSingle().Subject;
            item.Instance.GetType().FullName.Should().Be(FixtureTypeName);
            item.ContentRoot.Should().Be(tempDir);
            UnloadAndRelease(item.LoadContext);
        }
        finally
        {
            TryDeleteDirectory(tempDir);
        }
    }

    [Fact]
    public void Load_ResolvesAssembly_FromSubdirectoryRelativeToInstallRoot()
    {
        // Assembly path includes a subdirectory (e.g. "lib/MyWorkload.dll")
        // resolved relative to the install root.
        string tempDir = Path.Combine(Path.GetTempPath(), $"wl-loader-test-{Guid.NewGuid():N}");
        try
        {
            string subDir = Path.Combine(tempDir, "lib");
            Directory.CreateDirectory(subDir);
            CopyFixtureToDirectory(FixtureAssemblyFile, subDir);

            var paths = Substitute.For<IWorkloadPaths>();
            paths.WorkloadsRoot.Returns(tempDir);
            paths.GetInstallDirectory(Arg.Any<string>(), Arg.Any<string>()).Returns(tempDir);

            var loader = new WorkloadLoader(paths);
            var entry = FixtureEntry(FixturePackageId, assemblyFileOverride: $"lib/{FixtureAssemblyFile}");

            var loaded = loader.Load([entry]);

            var item = loaded.Should().ContainSingle().Subject;
            item.Instance.GetType().FullName.Should().Be(FixtureTypeName);
            item.ContentRoot.Should().Be(subDir);
            UnloadAndRelease(item.LoadContext);
        }
        finally
        {
            TryDeleteDirectory(tempDir);
        }
    }

    [Fact]
    public void Load_PrefersInstallRoot_OverToolsAny()
    {
        // When the assembly exists both at the install root and under tools/any/,
        // the install root takes precedence.
        string tempDir = Path.Combine(Path.GetTempPath(), $"wl-loader-test-{Guid.NewGuid():N}");
        try
        {
            string toolsAny = Path.Combine(tempDir, "tools", "any");
            Directory.CreateDirectory(toolsAny);
            CopyFixtureToDirectory(FixtureAssemblyFile, tempDir);
            CopyFixtureToDirectory(FixtureAssemblyFile, toolsAny);

            var paths = Substitute.For<IWorkloadPaths>();
            paths.WorkloadsRoot.Returns(tempDir);
            paths.GetInstallDirectory(Arg.Any<string>(), Arg.Any<string>()).Returns(tempDir);

            var loader = new WorkloadLoader(paths);
            var entry = FixtureEntry(FixturePackageId);

            var loaded = loader.Load([entry]);

            var item = loaded.Should().ContainSingle().Subject;
            item.Instance.GetType().FullName.Should().Be(FixtureTypeName);
            // ContentRoot is the directory of the resolved assembly — should be the install root.
            item.ContentRoot.Should().Be(tempDir);
            UnloadAndRelease(item.LoadContext);
        }
        finally
        {
            TryDeleteDirectory(tempDir);
        }
    }

    [Fact]
    public void Load_Throws_WhenSubdirectoryPathEscapesInstallRoot()
    {
        // Relative paths that escape the install directory must be rejected,
        // even when they point to valid files on disk.
        string tempDir = Path.Combine(Path.GetTempPath(), $"wl-loader-test-{Guid.NewGuid():N}");
        try
        {
            string nested = Path.Combine(tempDir, "inner");
            Directory.CreateDirectory(nested);

            // Place the DLL one level above the "install directory" to simulate escape.
            CopyFixtureToDirectory(FixtureAssemblyFile, tempDir);

            var paths = Substitute.For<IWorkloadPaths>();
            paths.WorkloadsRoot.Returns(nested);
            paths.GetInstallDirectory(Arg.Any<string>(), Arg.Any<string>()).Returns(nested);

            var loader = new WorkloadLoader(paths);
            var entry = FixtureEntry(FixturePackageId, assemblyFileOverride: $"../{FixtureAssemblyFile}");

            var ex = FluentActions.Invoking(() => loader.Load([entry])).Should().ThrowExactly<InvalidWorkloadException>().Which;

            ex.Message.Should().StartWith($"[{FixturePackageId}]");
            ex.Message.Should().Contain("outside the install directory");
        }
        finally
        {
            TryDeleteDirectory(tempDir);
        }
    }

    private static IWorkloadPaths StubPaths()
    {
        // Loader resolves assemblies via paths.GetInstallDirectory + tools/any.
        // Fixture dlls land at <BaseDirectory>/tools/any/<file> via test
        // project copies, so returning BaseDirectory as the install directory
        // lines up with a registry entry of AssemblyPath = "<file>" without
        // having to materialize a <package_id>/<version>/tools/any tree.
        var paths = Substitute.For<IWorkloadPaths>();
        paths.WorkloadsRoot.Returns(AppContext.BaseDirectory);
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

    /// <summary>
    /// Copies the fixture assembly (and its deps.json / runtimeconfig.json) from the
    /// test output's tools/any/ directory into the specified target directory.
    /// </summary>
    private static void CopyFixtureToDirectory(string assemblyFileName, string targetDir)
    {
        string sourceDir = Path.Combine(AppContext.BaseDirectory, "tools", "any");
        string baseName = Path.GetFileNameWithoutExtension(assemblyFileName);

        foreach (string ext in new[] { ".dll", ".deps.json", ".runtimeconfig.json", ".pdb" })
        {
            string source = Path.Combine(sourceDir, baseName + ext);
            if (File.Exists(source))
            {
                File.Copy(source, Path.Combine(targetDir, baseName + ext), overwrite: true);
            }
        }
    }

    /// <summary>
    /// Unloads a collectible <see cref="WorkloadLoadContext"/> and waits for the GC
    /// to release file handles so temp directories can be deleted on Windows.
    /// </summary>
    private static void UnloadAndRelease(WorkloadLoadContext context)
    {
        context.Unload();
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }

    /// <summary>
    /// Best-effort cleanup of temp directories. On Windows, collectible ALC file
    /// handles may linger briefly after unload; swallow access errors so test
    /// assertions are not masked by cleanup failures.
    /// </summary>
    private static void TryDeleteDirectory(string path)
    {
        try
        {
            Directory.Delete(path, recursive: true);
        }
        catch (UnauthorizedAccessException)
        {
            // File still locked by the OS after ALC unload; acceptable in tests.
        }
        catch (IOException)
        {
            // Same — lingering handle.
        }
    }
}
