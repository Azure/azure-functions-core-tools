// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Text.Json;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Tests.Templates.Acquisition;
using Azure.Functions.Cli.Workloads;
using Azure.Functions.Cli.Workloads.Catalog;
using Azure.Functions.Cli.Workloads.Discovery;
using Azure.Functions.Cli.Workloads.Install;
using Azure.Functions.Cli.Workloads.Storage;
using NSubstitute;
using NuGet.Versioning;
using Microsoft.Extensions.Options;
using PackageSource = NuGet.Configuration.PackageSource;

namespace Azure.Functions.Cli.Tests.Workloads.Storage;

public sealed class WorkloadOperationTests
{
    [Theory]
    [InlineData("legacy")]
    [InlineData("missing")]
    [InlineData("root")]
    [InlineData("prefixed")]
    [InlineData("subdirectory")]
    [InlineData("root-precedence")]
    public async Task PrepareFromCatalogAsync_RetainedRuntimeAssemblyLayout_PreservesLoaderPathCompatibility(string layout)
    {
        using var fixture = new BasicAcquisitionFixture();
        string assemblyPath = layout switch
        {
            "prefixed" => "tools/any/Fixture.dll",
            "subdirectory" => "lib/Fixture.dll",
            _ => "Fixture.dll",
        };
        var entryPoint = new EntryPointSpec { AssemblyPath = assemblyPath, Type = "Fixture.Runtime" };
        var entry = new WorkloadEntry
        {
            PackageId = "fixture.stacka", PackageVersion = "1.0.0", Kind = WorkloadKind.Workload,
            Source = BasicAcquisitionFixture.Source, EntryPoint = entryPoint,
        };
        string directory = fixture.Paths.GetInstallDirectory(entry.PackageId, entry.PackageVersion);
        Directory.CreateDirectory(Path.Combine(directory, "tools", "any"));
        var metadata = new WorkloadMetadata
        {
            Schema = WorkloadManifestSchema.PackageManifestV1Schema,
            Kind = WorkloadKind.Workload, EntryPoint = entryPoint,
        };
        File.WriteAllText(Path.Combine(directory, "workload.json"), JsonSerializer.Serialize(metadata, WorkloadJsonContext.Default.WorkloadMetadata));
        string primary = Path.GetFullPath(Path.Combine(directory, assemblyPath));
        string fallback = Path.GetFullPath(Path.Combine(directory, "tools", "any", assemblyPath));
        if (layout is not ("legacy" or "missing"))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(primary)!);
            File.WriteAllText(primary, "Owned root-relative assembly bytes, not activated.");
        }
        if (layout is "legacy" or "root-precedence")
        {
            Directory.CreateDirectory(Path.GetDirectoryName(fallback)!);
            File.WriteAllText(fallback, "Owned legacy assembly bytes, not activated.");
        }
        await fixture.Store.SaveWorkloadAsync(entry);
        string registry = File.ReadAllText(fixture.Paths.WorkloadRegistryPath);
        var before = Directory.GetFiles(directory, "*", SearchOption.AllDirectories).ToDictionary(path => path, File.ReadAllText);
        var fileSystem = Substitute.For<IFileSystem>();
        PhysicalFileSystem physical = new();
        fileSystem.DirectoryExists(Arg.Any<string>()).Returns(call => physical.DirectoryExists(call.Arg<string>()));
        fileSystem.FileExists(Arg.Any<string>()).Returns(call => physical.FileExists(call.Arg<string>()));
        fileSystem.GetFiles(Arg.Any<string>()).Returns(call => physical.GetFiles(call.Arg<string>()));
        fileSystem.ComputeSha256Async(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(call => physical.ComputeSha256Async(call.Arg<string>(), call.Arg<CancellationToken>()));
        var coordinator = new FileWorkloadOperationCoordinator(fixture.Paths);
        var runtime = Substitute.For<IWorkloadRuntimeIdentifierProvider>();
        runtime.Current.Returns("test-rid");
        WorkloadRidPackageSelector selector = new(runtime);
        WorkloadPackageInspector inspector = new(new WorkloadMetadataReader(), selector);
        WorkloadDeploymentService deployment = new(fixture.Paths, fixture.Store, new WorkloadMetadataReader(), coordinator, fileSystem);
        WorkloadInstaller installer = new(new WorkloadPackageSource(fixture.WorkloadCatalog, inspector, Options.Create(new WorkloadCatalogOptions())),
            inspector, selector, deployment, coordinator, fixture.Store, fileSystem);

        Func<Task<PreparedWorkloadInstall>> prepare = () => installer.PrepareFromCatalogAsync("Fixture.StackA", NuGetVersion.Parse("1.0.0"),
            BasicAcquisitionFixture.Source, false, true, false, null, CancellationToken.None);

        if (layout == "missing")
        {
            await prepare.Should().ThrowAsync<InvalidWorkloadException>().WithMessage("*damaged or conflicts*");
        }
        else
        {
            using var prepared = await prepare();
            using var operation = await coordinator.AcquireAsync(CancellationToken.None);
            var result = await installer.CommitPreparedAsync(operation, prepared, null, CancellationToken.None);
            result.AlreadyInstalled.Should().BeTrue();
            result.Entry.Should().BeEquivalentTo(entry);
            prepared.Reuse!.Hashes.Keys.Should().BeEquivalentTo(before.Keys.Select(path => Path.GetRelativePath(directory, path)));
        }
        fileSystem.Received(1).FileExists(Arg.Is<string>(path => Path.GetFullPath(path) == primary));
        if (layout == "legacy")
        {
            fileSystem.Received(1).FileExists(Arg.Is<string>(path => Path.GetFullPath(path) == fallback));
        }
        else if (layout != "missing")
        {
            fileSystem.DidNotReceive().FileExists(Arg.Is<string>(path => Path.GetFullPath(path) == fallback));
        }
        File.ReadAllText(fixture.Paths.WorkloadRegistryPath).Should().Be(registry);
        Directory.GetFiles(directory, "*", SearchOption.AllDirectories).ToDictionary(path => path, File.ReadAllText)
            .Should().BeEquivalentTo(before);
        fixture.Downloads.Should().BeEmpty();
        File.Exists(Path.Combine(fixture.Paths.Home, FileWorkloadOperationCoordinator.ControlDirectoryName,
            FileWorkloadOperationCoordinator.IntentFileName)).Should().BeFalse();
    }

    [Fact]
    public async Task Fixture_RidImplementation_UsesDeclaredPayloadRootBeforePreparingReuse()
    {
        using var fixture = new BasicAcquisitionFixture();
        await fixture.SeedStackAsync("Fixture.StackA", "1.0.0");
        var entry = (await fixture.Store.GetWorkloadsAsync()).Should().ContainSingle().Subject;
        string directory = fixture.Paths.GetInstallDirectory(entry.PackageId, entry.PackageVersion);
        var declared = new WorkloadMetadataReader().Read(directory);
        entry.RuntimeIdentifier.Should().Be("test-rid");
        declared.RuntimeIdentifier.Should().Be(entry.RuntimeIdentifier);
        declared.Kind.Should().Be(WorkloadKind.Content);
        Directory.Exists(Path.Combine(directory, "tools", "test-rid")).Should().BeTrue();
        Directory.Exists(Path.Combine(directory, "tools", "any")).Should().BeFalse();
        string registry = File.ReadAllText(fixture.Paths.WorkloadRegistryPath);
        var deployment = new WorkloadDeploymentService(fixture.Paths, fixture.Store, new WorkloadMetadataReader(),
            new FileWorkloadOperationCoordinator(fixture.Paths), new PhysicalFileSystem());

        var prepared = await deployment.PrepareReuseAsync(entry, CancellationToken.None);

        prepared.Hashes.Keys.Should().Contain(Path.Combine("tools", "test-rid", "fixture.txt"));
        File.ReadAllText(fixture.Paths.WorkloadRegistryPath).Should().Be(registry);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UninstallAsync_PayloadDeletionFails_RetainsDirtyIntentAndRemainingPayload(bool throwOnDelete)
    {
        using var fixture = new BasicAcquisitionFixture();
        await fixture.SeedStackAsync("Fixture.StackA", "1.0.0");
        var entry = (await fixture.Store.GetWorkloadsAsync()).Should().ContainSingle().Subject;
        string directory = fixture.Paths.GetInstallDirectory(entry.PackageId, entry.PackageVersion);
        var fileSystem = Substitute.For<IFileSystem>();
        fileSystem.DirectoryExists(directory).Returns(_ => Directory.Exists(directory));
        if (throwOnDelete)
        {
            fileSystem.When(fs => fs.DeleteDirectory(directory)).Do(_ => throw new IOException("Owned payload deletion failure."));
        }
        var coordinator = new FileWorkloadOperationCoordinator(fixture.Paths);
        var deployment = new WorkloadDeploymentService(fixture.Paths, fixture.Store, new WorkloadMetadataReader(), coordinator, fileSystem);

        Func<Task> uninstall = () => deployment.UninstallAsync("Fixture.StackA", "1.0.0", WorkloadOwnershipKind.Logical);

        await uninstall.Should().ThrowAsync<IOException>();
        fileSystem.Received(1).DeleteDirectory(directory);
        Directory.Exists(directory).Should().BeTrue();
        (await fixture.Store.GetWorkloadsAsync()).Should().BeEmpty();
        Func<Task> nextWriter = async () =>
        {
            using var operation = await coordinator.AcquireAsync(CancellationToken.None);
        };
        await nextWriter.Should().ThrowAsync<WorkloadRecoveryRequiredException>();
    }

    [Fact]
    public async Task UninstallAsync_UnknownOwnership_RejectsBeforeRegistryOrPayloadMutation()
    {
        using var fixture = new BasicAcquisitionFixture();
        await fixture.SeedStackAsync("Fixture.StackA", "1.0.0");
        string registry = File.ReadAllText(fixture.Paths.WorkloadRegistryPath);
        var deployment = new WorkloadDeploymentService(fixture.Paths, fixture.Store, new WorkloadMetadataReader(),
            new FileWorkloadOperationCoordinator(fixture.Paths), new PhysicalFileSystem());

        Func<Task> uninstall = () => deployment.UninstallAsync("fixture.stacka.test-rid", "1.0.0", (WorkloadOwnershipKind)42);

        await uninstall.Should().ThrowAsync<ArgumentOutOfRangeException>();
        File.ReadAllText(fixture.Paths.WorkloadRegistryPath).Should().Be(registry);
        Directory.Exists(fixture.Paths.GetInstallDirectory("fixture.stacka.test-rid", "1.0.0")).Should().BeTrue();
        File.Exists(Path.Combine(fixture.Paths.Home, FileWorkloadOperationCoordinator.ControlDirectoryName,
            FileWorkloadOperationCoordinator.IntentFileName)).Should().BeFalse();
    }

    [Fact]
    public async Task StoreFailureAfterActualRegistryPersistence_PreservesPayloadInsteadOfGuessingRollback()
    {
        using var fixture = new BasicAcquisitionFixture();
        using var prepared = await fixture.Installer.PrepareFromCatalogAsync("Fixture.StackA", NuGetVersion.Parse("1.0.0"),
            BasicAcquisitionFixture.Source, false, true, false, null, CancellationToken.None);
        var faultingStore = Substitute.For<IWorkloadStore>();
        faultingStore.GetWorkloadsAsync(Arg.Any<CancellationToken>())
            .Returns(call => fixture.Store.GetWorkloadsAsync(call.Arg<CancellationToken>()));
        faultingStore.SaveWorkloadAsync(Arg.Any<IWorkloadOperation>(), Arg.Any<WorkloadEntry>(), Arg.Any<CancellationToken>())
            .Returns(async call =>
            {
                await fixture.Store.SaveWorkloadAsync(call.Arg<IWorkloadOperation>(), call.Arg<WorkloadEntry>(), call.Arg<CancellationToken>());
                throw new IOException("Owned post-persistence read-back failure.");
            });
        WorkloadDeploymentService deployment = new(fixture.Paths, faultingStore, new WorkloadMetadataReader(),
            new FileWorkloadOperationCoordinator(fixture.Paths), new PhysicalFileSystem(), Substitute.For<IBundledTemplateInstaller>());
        using var operation = await new FileWorkloadOperationCoordinator(fixture.Paths).AcquireAsync(CancellationToken.None);

        Func<Task> commit = () => deployment.InstallPreparedAsync(operation, prepared.Deployment!, null, CancellationToken.None);

        await commit.Should().ThrowAsync<IOException>().WithMessage("*post-persistence*");
        var row = (await fixture.Store.GetWorkloadsAsync()).Should().ContainSingle().Subject;
        Directory.Exists(fixture.Paths.GetInstallDirectory(row.PackageId, row.PackageVersion)).Should().BeTrue();
        operation.IsDirty.Should().BeTrue();
    }

    [Fact]
    public async Task IntentCompletionFailure_PreservesRegisteredVerifiedPayloadAndDirtyGate()
    {
        using var fixture = new BasicAcquisitionFixture();
        using var prepared = await fixture.Installer.PrepareFromCatalogAsync("Fixture.StackA", NuGetVersion.Parse("1.0.0"),
            BasicAcquisitionFixture.Source, false, true, false, null, CancellationToken.None);
        using var owner = await new FileWorkloadOperationCoordinator(fixture.Paths).AcquireAsync(CancellationToken.None);
        ThrowingCompletion operation = new(owner);

        Func<Task> commit = () => fixture.Installer.CommitPreparedAsync(operation, prepared, null, CancellationToken.None);

        await commit.Should().ThrowAsync<IOException>().WithMessage("*intent completion*");
        var row = (await fixture.Store.GetWorkloadsAsync()).Should().ContainSingle().Subject;
        File.ReadAllText(Path.Combine(fixture.Paths.GetInstallDirectory(row.PackageId, row.PackageVersion), "tools", "test-rid", "fixture.txt"))
            .Should().Be("Installation fixture, not an activated stack implementation.");
        owner.IsDirty.Should().BeTrue();
        File.Exists(Path.Combine(fixture.Paths.Home, FileWorkloadOperationCoordinator.ControlDirectoryName,
            FileWorkloadOperationCoordinator.IntentFileName)).Should().BeTrue();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LogicalUpdatePreparedReuseDestinationChanges_RejectsBeforeOwnershipEffects(bool changeSource)
    {
        using var fixture = new BasicAcquisitionFixture();
        await fixture.SeedStackAsync("Fixture.StackA", "1.0.0");
        string archive = fixture.Archive("Fixture.StackA.test-rid", "2.0.0");
        await fixture.Installer.InstallFromPackageAsync(archive);
        fixture.WorkloadCatalog.ResolveLatestVersionAsync(Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<NuGetVersion>(), Arg.Any<bool>(),
                Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new ResolvedPackage("Fixture.StackA", NuGetVersion.Parse("2.0.0"), new PackageSource(BasicAcquisitionFixture.Source)));
        AdmissionGate gate = new(new FileWorkloadOperationCoordinator(fixture.Paths));
        PhysicalFileSystem fileSystem = new();
        WorkloadMetadataReader metadata = new();
        var runtime = Substitute.For<IWorkloadRuntimeIdentifierProvider>();
        runtime.Current.Returns("test-rid");
        WorkloadRidPackageSelector selector = new(runtime);
        WorkloadPackageInspector inspector = new(metadata, selector);
        WorkloadDeploymentService deployment = new(fixture.Paths, fixture.Store, metadata, gate, fileSystem, Substitute.For<IBundledTemplateInstaller>());
        WorkloadInstaller installer = new(new WorkloadPackageSource(fixture.WorkloadCatalog, inspector, Options.Create(new WorkloadCatalogOptions())),
            inspector, selector, deployment, gate, fixture.Store, fileSystem);
        Task<WorkloadUpdateResult> update = installer.UpdateAsync("Fixture.StackA", null, BasicAcquisitionFixture.Source, false, false,
            WorkloadOwnershipKind.Logical, cancellationToken: CancellationToken.None);
        await gate.Prepared.Task.WaitAsync(TimeSpan.FromSeconds(30));
        string replacement = changeSource ? Path.Combine(fixture.Root, "different-source.nupkg") : archive;
        if (changeSource)
        {
            File.Copy(archive, replacement);
        }
        using (var zip = System.IO.Compression.ZipFile.Open(replacement, System.IO.Compression.ZipArchiveMode.Update))
        using (var text = new StreamWriter(zip.CreateEntry("tools/test-rid/replacement.txt").Open()))
        {
            text.Write("a different participating writer's actual payload");
        }
        await fixture.Installer.InstallFromPackageAsync(replacement, force: true);
        var before = await fixture.Store.GetWorkloadsAsync();

        gate.Admit.SetResult();
        Func<Task> finish = async () => await update.WaitAsync(TimeSpan.FromSeconds(30));

        await finish.Should().ThrowAsync<WorkloadPlanChangedException>();
        (await fixture.Store.GetWorkloadsAsync()).Should().BeEquivalentTo(before);
        before.Single(row => row.PackageVersion == "1.0.0").LogicalPackage.Should().NotBeNull();
        before.Single(row => row.PackageVersion == "2.0.0").LogicalPackage.Should().BeNull();
        Directory.Exists(fixture.Paths.GetInstallDirectory("fixture.stacka.test-rid", "1.0.0")).Should().BeTrue();
    }

    [Fact]
    public async Task InspectedArchiveChangesBeforeExtraction_RejectsBeforeLiveMutation()
    {
        using var fixture = new BasicAcquisitionFixture();
        string archive = fixture.Archive("Fixture.StackA.test-rid");
        var runtime = Substitute.For<IWorkloadRuntimeIdentifierProvider>();
        runtime.Current.Returns("test-rid");
        WorkloadPackageInspector inspector = new(new WorkloadMetadataReader(), new(runtime));
        var inspected = await inspector.InspectAsync(archive, CancellationToken.None);
        using (var zip = System.IO.Compression.ZipFile.Open(archive, System.IO.Compression.ZipArchiveMode.Update))
        using (var writer = new StreamWriter(zip.CreateEntry("tools/test-rid/changed.txt").Open()))
        {
            writer.Write("different selected bytes");
        }
        WorkloadDeploymentService deployment = new(fixture.Paths, fixture.Store, new WorkloadMetadataReader(),
            new FileWorkloadOperationCoordinator(fixture.Paths), new PhysicalFileSystem(), Substitute.For<IBundledTemplateInstaller>());

        Func<Task> prepare = () => deployment.PrepareAsync(inspected, archive, WorkloadOwnershipKind.Explicit, null, false, null, CancellationToken.None);

        await prepare.Should().ThrowAsync<WorkloadPlanChangedException>();
        Directory.Exists(fixture.Paths.WorkloadsRoot).Should().BeFalse();
        File.Exists(fixture.Paths.WorkloadRegistryPath).Should().BeFalse();
    }

    [Fact]
    public async Task EquivalentConfiguredRoots_Contend_WhileDifferentRootsAreIndependent()
    {
        using var fixture = new BasicAcquisitionFixture();
        WorkloadPathsOptions equivalent = new(Path.Combine(fixture.Paths.Home, "..", Path.GetFileName(fixture.Paths.Home), "."));
        using var first = await new FileWorkloadOperationCoordinator(fixture.Paths).AcquireAsync(CancellationToken.None);
        using var other = await new FileWorkloadOperationCoordinator(new WorkloadPathsOptions(Path.Combine(fixture.Root, "other"))).AcquireAsync(CancellationToken.None);
        using var cancel = new CancellationTokenSource();
        bool waited = false;

        Func<Task> acquire = async () =>
        {
            using var lease = await new FileWorkloadOperationCoordinator(equivalent).AcquireAsync(cancel.Token, () =>
            {
                waited = true;
                cancel.Cancel();
            });
        };

        await acquire.Should().ThrowAsync<OperationCanceledException>();
        waited.Should().BeTrue();
        first.Validate(equivalent);
        (await fixture.Store.GetWorkloadsAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task ForeignOrDisposedCapability_RejectsAllRegistryWriterBoundariesBeforeMutation()
    {
        using var fixture = new BasicAcquisitionFixture();
        WorkloadPathsOptions other = new(Path.Combine(fixture.Root, "other"));
        using var foreign = await new FileWorkloadOperationCoordinator(other).AcquireAsync(CancellationToken.None);
        var disposed = await new FileWorkloadOperationCoordinator(fixture.Paths).AcquireAsync(CancellationToken.None);
        disposed.Dispose();
        WorkloadEntry entry = new() { PackageId = "fixture", PackageVersion = "1.0.0" };
        foreach (var capability in new[] { foreign, disposed })
        {
            Func<Task>[] calls =
            [
                () => fixture.Store.SaveWorkloadAsync(capability, entry, CancellationToken.None),
                () => fixture.Store.RemoveWorkloadAsync(capability, "fixture", "1.0.0", CancellationToken.None),
                () => fixture.Store.ReplaceWorkloadAsync(capability, "fixture", "1.0.0", entry, CancellationToken.None),
                () => fixture.Store.AddOwnershipAsync(capability, entry, WorkloadOwnershipKind.Explicit, CancellationToken.None),
                () => fixture.Store.RemoveOwnershipAsync(capability, "fixture", "1.0.0", WorkloadOwnershipKind.Explicit, CancellationToken.None),
                () => fixture.Store.MoveLogicalOwnershipAsync(capability, "fixture", "1.0.0", entry, CancellationToken.None),
                () => fixture.Store.MoveExplicitOwnershipAsync(capability, "fixture", "1.0.0", entry, CancellationToken.None),
            ];
            foreach (var call in calls)
            {
                await call.Should().ThrowAsync<Exception>().Where(exception =>
                    capability == foreign ? exception is ArgumentException : exception is ObjectDisposedException);
            }
        }

        File.Exists(fixture.Paths.WorkloadRegistryPath).Should().BeFalse();
    }

    [Theory]
    [InlineData("bytes")]
    [InlineData("ownership")]
    [InlineData("source")]
    public async Task PreparedPayloadOrAuthoritativeStateChanges_RequireVisibleReplanBeforeEffects(string change)
    {
        using var fixture = new BasicAcquisitionFixture();
        using var prepared = await fixture.Installer.PrepareFromCatalogAsync("Fixture.StackA", NuGetVersion.Parse("1.0.0"),
            BasicAcquisitionFixture.Source, false, true, false, null, CancellationToken.None);
        if (change == "bytes")
        {
            await File.WriteAllTextAsync(Path.Combine(prepared.Deployment!.StagingPath, "tools", "test-rid", "fixture.txt"), "changed private payload");
        }
        else
        {
            await fixture.Store.SaveWorkloadAsync(new()
            {
                PackageId = "fixture.stacka.test-rid", PackageVersion = "1.0.0", Kind = WorkloadKind.Content,
                Source = change == "source" ? "https://changed.invalid/v3/index.json" : BasicAcquisitionFixture.Source,
                LogicalPackage = change == "ownership" ? new() { PackageId = "other.pointer", PackageVersion = "1.0.0" } : null,
            });
        }
        var before = await fixture.Store.GetWorkloadsAsync();
        using var operation = await new FileWorkloadOperationCoordinator(fixture.Paths).AcquireAsync(CancellationToken.None);

        Func<Task> commit = () => fixture.Installer.CommitPreparedAsync(operation, prepared, null, CancellationToken.None);

        await commit.Should().ThrowAsync<WorkloadPlanChangedException>();
        (await fixture.Store.GetWorkloadsAsync()).Should().BeEquivalentTo(before);
        Directory.Exists(fixture.Paths.WorkloadsRoot).Should().BeFalse();
        File.Exists(Path.Combine(fixture.Paths.Home, FileWorkloadOperationCoordinator.ControlDirectoryName,
            FileWorkloadOperationCoordinator.IntentFileName)).Should().BeFalse();
    }

    [Fact]
    public async Task InteractionExpectationChangedBeforePreparation_DoesNotExpandConsent()
    {
        using var fixture = new BasicAcquisitionFixture();
        WorkloadInstallExpectation expectation = new([]);
        await fixture.SeedStackAsync("Fixture.StackA", "1.0.0");
        var before = await fixture.Store.GetWorkloadsAsync();

        Func<Task> install = () => fixture.Installer.InstallFromCatalogAsync("Fixture.StackA", NuGetVersion.Parse("2.0.0"),
            BasicAcquisitionFixture.Source, false, true, false, expectation, null, CancellationToken.None);

        await install.Should().ThrowAsync<WorkloadPlanChangedException>();
        (await fixture.Store.GetWorkloadsAsync()).Should().BeEquivalentTo(before);
        Directory.Exists(fixture.Paths.GetInstallDirectory("fixture.stacka.test-rid", "2.0.0")).Should().BeFalse();
    }

    [Fact]
    public async Task RegistryCommitUnderCapability_DoesNotReacquire_AndClearsOnlyAtOwningCompletion()
    {
        using var fixture = new BasicAcquisitionFixture();
        using var operation = await new FileWorkloadOperationCoordinator(fixture.Paths).AcquireAsync(CancellationToken.None);
        await fixture.Store.SaveWorkloadAsync(operation, new() { PackageId = "first", PackageVersion = "1.0.0" }, CancellationToken.None);
        await fixture.Store.SaveWorkloadAsync(operation, new() { PackageId = "second", PackageVersion = "1.0.0" }, CancellationToken.None);
        string intent = Path.Combine(fixture.Paths.Home, FileWorkloadOperationCoordinator.ControlDirectoryName, FileWorkloadOperationCoordinator.IntentFileName);
        File.Exists(intent).Should().BeTrue();
        (await fixture.Store.GetWorkloadsAsync()).Select(row => row.PackageId).Should().BeEquivalentTo(["first", "second"]);

        operation.CompleteMutation();

        File.Exists(intent).Should().BeFalse();
    }

    [Fact]
    public void Constructors_NullCoordinatorAndDependencies_RejectProgrammerErrors()
    {
        using var fixture = new BasicAcquisitionFixture();
        var fileSystem = new PhysicalFileSystem();
        var coordinator = new FileWorkloadOperationCoordinator(fixture.Paths);
        Action[] calls =
        [
            () => new FileWorkloadOperationCoordinator(null!),
            () => new WorkloadStore(null!, coordinator),
            () => new WorkloadStore(fixture.Paths, null!),
            () => new WorkloadDeploymentService(fixture.Paths, fixture.Store, new WorkloadMetadataReader(), null!, fileSystem, Substitute.For<IBundledTemplateInstaller>()),
            () => new WorkloadDeploymentService(fixture.Paths, fixture.Store, new WorkloadMetadataReader(), coordinator, null!, Substitute.For<IBundledTemplateInstaller>()),
            () => new WorkloadDeploymentService(fixture.Paths, fixture.Store, new WorkloadMetadataReader(), coordinator, fileSystem, null!),
        ];

        foreach (var call in calls)
        {
            call.Should().Throw<ArgumentNullException>();
        }
    }

    private sealed class ThrowingCompletion(IWorkloadOperation inner) : IWorkloadOperation
    {
        public bool IsDirty => inner.IsDirty;
        public void Validate(IWorkloadPaths paths) => inner.Validate(paths);
        public void BeginMutation() => inner.BeginMutation();
        public void CompleteMutation() => throw new IOException("Owned fixture intent completion failed.");
        public void Dispose() => inner.Dispose();
    }

    private sealed class AdmissionGate(IWorkloadOperationCoordinator inner) : IWorkloadOperationCoordinator
    {
        public TaskCompletionSource Prepared { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Admit { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task<IWorkloadOperation> AcquireAsync(CancellationToken cancellationToken, Action? waiting = null)
        {
            Prepared.SetResult();
            await Admit.Task.WaitAsync(cancellationToken);
            return await inner.AcquireAsync(cancellationToken, waiting);
        }
    }
}
