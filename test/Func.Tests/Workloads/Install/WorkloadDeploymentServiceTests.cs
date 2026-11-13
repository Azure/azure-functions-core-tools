// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Text.Json;
using Azure.Functions.Cli.Workloads;
using Azure.Functions.Cli.Workloads.Discovery;
using Azure.Functions.Cli.Workloads.Install;
using Azure.Functions.Cli.Workloads.Storage;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using NuGet.Packaging;
using NuGet.Versioning;

namespace Azure.Functions.Cli.Tests.Workloads.Install;

public sealed class WorkloadDeploymentServiceTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("workload-lifecycle-").FullName;
    private readonly IWorkloadStore _store = Substitute.For<IWorkloadStore>();
    private readonly IWorkloadMetadataReader _metadataReader = Substitute.For<IWorkloadMetadataReader>();
    private readonly IWorkloadPaths _paths;
    private readonly WorkloadDeploymentService _deploymentService;

    public WorkloadDeploymentServiceTests()
    {
        _paths = new WorkloadPathsOptions(Path.Combine(_root, ".azure-functions"));
        _store.GetWorkloadsAsync(Arg.Any<CancellationToken>()).Returns([]);
        _deploymentService = new WorkloadDeploymentService(_paths, _store, _metadataReader);
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public async Task TryReuseExplicitAsync_InstalledPayload_ReturnsExistingEntry()
    {
        WorkloadEntry entry = Entry("example.workload", "1.0.0", explicitlyInstalled: true);
        _store.GetWorkloadsAsync(Arg.Any<CancellationToken>()).Returns([entry]);
        Directory.CreateDirectory(_paths.GetInstallDirectory(entry.PackageId, entry.PackageVersion));

        WorkloadInstallResult? result = await _deploymentService.TryReuseExplicitAsync(entry.PackageId, entry.PackageVersion);

        result.Should().NotBeNull();
        result!.AlreadyInstalled.Should().BeTrue();
        result.Entry.Should().BeSameAs(entry);
        _metadataReader.DidNotReceiveWithAnyArgs().Read(default!);
    }

    [Fact]
    public async Task TryReuseImplementationForUpdateAsync_MovesOwnershipAndDeletesOrphanedPayload()
    {
        LogicalPackage oldLogical = Logical("example.pointer", "1.0.0");
        LogicalPackage newLogical = Logical("example.pointer", "2.0.0");
        WorkloadEntry current = Entry("example.workload.win-x64", "1.0.0", logicalPackage: oldLogical);
        WorkloadEntry reusable = Entry("example.workload.win-x64", "2.0.0", runtimeIdentifier: "win-x64");
        _store.GetWorkloadsAsync(Arg.Any<CancellationToken>()).Returns([current, reusable]);
        string currentPath = _paths.GetInstallDirectory(current.PackageId, current.PackageVersion);
        string reusablePath = _paths.GetInstallDirectory(reusable.PackageId, reusable.PackageVersion);
        Directory.CreateDirectory(currentPath);
        Directory.CreateDirectory(reusablePath);
        _metadataReader.Read(reusablePath).Returns(Metadata("win-x64"));
        _store.MoveLogicalOwnershipAsync(
                current.PackageId, current.PackageVersion, Arg.Any<WorkloadEntry>(), Arg.Any<CancellationToken>())
            .Returns(call => new WorkloadOwnershipMoveResult(call.Arg<WorkloadEntry>(), PreviousEntryRemoved: true));

        WorkloadInstallResult? result =
            await _deploymentService.TryReuseImplementationForUpdateAsync(current, reusable.PackageId, reusable.PackageVersion, "win-x64", newLogical);

        result.Should().NotBeNull();
        result!.AlreadyInstalled.Should().BeFalse();
        result.Entry.LogicalPackage.Should().BeSameAs(newLogical);
        Directory.Exists(currentPath).Should().BeFalse();
        Directory.Exists(reusablePath).Should().BeTrue();
    }

    [Fact]
    public async Task TryReuseImplementationAsync_AttachingOwnership_PreservesAllEntryFields()
    {
        LogicalPackage logicalPackage = Logical("example.pointer", "1.0.0");
        WorkloadEntry existing = CompleteEntry();
        _store.GetWorkloadsAsync(Arg.Any<CancellationToken>()).Returns([existing]);
        string installPath = _paths.GetInstallDirectory(existing.PackageId, existing.PackageVersion);
        Directory.CreateDirectory(installPath);
        _metadataReader.Read(installPath).Returns(Metadata(existing.RuntimeIdentifier!));
        _store.AddOwnershipAsync(Arg.Any<WorkloadEntry>(), WorkloadOwnershipKind.Logical, Arg.Any<CancellationToken>())
            .Returns(call => call.Arg<WorkloadEntry>());

        WorkloadInstallResult? result = await _deploymentService.TryReuseImplementationAsync(
            existing.PackageId, existing.PackageVersion, existing.RuntimeIdentifier!, logicalPackage);

        result.Should().NotBeNull();
        result!.Entry.Should().BeEquivalentTo(CompleteEntry(logicalPackage));
    }

    [Fact]
    public async Task InstallAsync_ForcedReinstall_PreservesIncomingFieldsAndExistingOwnership()
    {
        LogicalPackage logicalPackage = Logical("example.pointer", "1.0.0");
        WorkloadEntry existing = CompleteEntry(logicalPackage);
        _store.GetWorkloadsAsync(Arg.Any<CancellationToken>()).Returns([existing]);
        EntryPointSpec entryPoint = new() { AssemblyPath = "New.dll", Type = "New.Type" };
        WorkloadMetadata metadata = new()
        {
            Schema = WorkloadManifestSchema.PackageManifestV1Schema,
            Kind = WorkloadKind.Workload,
            EntryPoint = entryPoint,
            RuntimeIdentifier = "win-x64",
            TemplateRoot = "templates/new",
            DisplayName = "New display name",
            Description = "New description.",
        };
        _metadataReader.Read(Arg.Any<string>()).Returns(metadata);
        const string packageId = "example.workload.win-x64";
        const string version = "1.0.0";
        const string source = "https://new.example.test/v3/index.json";
        WorkloadPackageIdentity identity = new(
            packageId, version, [WorkloadPackageTypes.RuntimeIdentifierPackage], ["new-alias"], ["win-x64"], "Nuspec title", "Nuspec description");
        InspectedWorkloadPackage package = new(
            BuildPackage(packageId, version), identity, metadata, WorkloadPackageRole.RuntimeIdentifierImplementation);

        WorkloadInstallResult result = await _deploymentService.InstallAsync(
            package, source, WorkloadOwnershipKind.Explicit, logicalPackage: null, force: true);

        WorkloadEntry expected = new()
        {
            PackageId = packageId,
            PackageVersion = version,
            Kind = WorkloadKind.Workload,
            Aliases = ["new-alias"],
            DisplayName = metadata.DisplayName,
            Description = metadata.Description,
            Source = source,
            RuntimeIdentifier = metadata.RuntimeIdentifier,
            TemplateRoot = metadata.TemplateRoot,
            IsImplicitlyInstalled = false,
            LogicalPackage = logicalPackage,
            InstallRefCount = existing.InstallRefCount + 1,
            EntryPoint = entryPoint,
        };
        result.Entry.Should().BeEquivalentTo(expected);
        await _store.Received(1).SaveWorkloadAsync(result.Entry, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task TryReuseImplementationAsync_InvalidInstalledMetadata_DoesNotReuse()
    {
        WorkloadEntry reusable = Entry("example.workload.win-x64", "1.0.0", runtimeIdentifier: "win-x64");
        _store.GetWorkloadsAsync(Arg.Any<CancellationToken>()).Returns([reusable]);
        string installPath = _paths.GetInstallDirectory(reusable.PackageId, reusable.PackageVersion);
        Directory.CreateDirectory(installPath);
        _metadataReader.Read(installPath).Throws(new InvalidWorkloadException("invalid manifest"));

        WorkloadInstallResult? result = await _deploymentService.TryReuseImplementationAsync(
            reusable.PackageId, reusable.PackageVersion, "win-x64", Logical("example.pointer", "1.0.0"));

        result.Should().BeNull();
    }

    [Theory]
    [InlineData(false, "templates/initial", "templates/updated")]
    [InlineData(true, "templates/initial", "templates/reinstalled")]
    [InlineData(false, "templates/initial", null)]
    [InlineData(true, "templates/initial", null)]
    [InlineData(false, null, "templates/updated")]
    [InlineData(true, null, "templates/reinstalled")]
    public async Task Deployment_PersistsTemplateRootOnInstallAndReplacement(bool reinstall, string? initialRoot, string? newRoot)
    {
        WorkloadStore store = new(_paths);
        WorkloadDeploymentService deployment = new(_paths, store, new WorkloadMetadataReader());
        WorkloadMetadata initialMetadata = TemplateMetadata(initialRoot);
        InspectedWorkloadPackage initial = Package("1.0.0", initialMetadata);
        WorkloadInstallResult installed = await deployment.InstallAsync(
            initial, _root, WorkloadOwnershipKind.Explicit, logicalPackage: null, force: false);
        (await store.GetWorkloadsAsync()).Should().ContainSingle().Which.TemplateRoot.Should().Be(initialRoot);
        WorkloadMetadata newMetadata = TemplateMetadata(newRoot);
        InspectedWorkloadPackage replacement = Package(reinstall ? "1.0.0" : "2.0.0", newMetadata);

        if (reinstall)
        {
            await deployment.InstallAsync(replacement, _root, WorkloadOwnershipKind.Explicit, logicalPackage: null, force: true);
        }
        else
        {
            await deployment.UpdateAsync(installed.Entry, replacement, _root, WorkloadOwnershipKind.Explicit, logicalPackage: null);
        }

        WorkloadEntry persisted = (await store.GetWorkloadsAsync()).Should().ContainSingle().Subject;
        persisted.TemplateRoot.Should().Be(newRoot);
        persisted.PackageVersion.Should().Be(replacement.Identity.Version);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Deployment_InvalidTemplateRoot_DoesNotMutateRegistry(bool update)
    {
        WorkloadStore store = new(_paths);
        WorkloadDeploymentService deployment = new(_paths, store, new WorkloadMetadataReader());
        InspectedWorkloadPackage initial = Package("1.0.0", TemplateMetadata("templates"));
        WorkloadInstallResult installed = await deployment.InstallAsync(
            initial, _root, WorkloadOwnershipKind.Explicit, logicalPackage: null, force: false);
        byte[] before = await File.ReadAllBytesAsync(_paths.WorkloadRegistryPath);
        InspectedWorkloadPackage invalid = Package("2.0.0", TemplateMetadata("../templates"));

        Func<Task> action = update
            ? () => deployment.UpdateAsync(installed.Entry, invalid, _root, WorkloadOwnershipKind.Explicit, logicalPackage: null)
            : () => deployment.InstallAsync(invalid, _root, WorkloadOwnershipKind.Explicit, logicalPackage: null, force: false);

        await action.Should().ThrowExactlyAsync<InvalidWorkloadException>().WithMessage("*templateRoot*");
        (await File.ReadAllBytesAsync(_paths.WorkloadRegistryPath)).Should().Equal(before);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Deployment_ChangedTemplateRootSinceInspection_DoesNotMutateRegistry(bool update)
    {
        WorkloadStore store = new(_paths);
        WorkloadDeploymentService deployment = new(_paths, store, new WorkloadMetadataReader());
        WorkloadInstallResult installed = await deployment.InstallAsync(
            Package("1.0.0", TemplateMetadata("templates/original")),
            _root, WorkloadOwnershipKind.Explicit, logicalPackage: null, force: false);
        byte[] before = await File.ReadAllBytesAsync(_paths.WorkloadRegistryPath);
        InspectedWorkloadPackage package = Package("2.0.0", TemplateMetadata("templates/actual"));
        InspectedWorkloadPackage changed = package with { Metadata = TemplateMetadata("templates/inspected") };
        Func<Task> action = update
            ? () => deployment.UpdateAsync(installed.Entry, changed, _root, WorkloadOwnershipKind.Explicit, logicalPackage: null)
            : () => deployment.InstallAsync(changed, _root, WorkloadOwnershipKind.Explicit, logicalPackage: null, force: false);

        await action.Should().ThrowExactlyAsync<InvalidWorkloadException>().WithMessage("*changed*");

        (await File.ReadAllBytesAsync(_paths.WorkloadRegistryPath)).Should().Equal(before);
        Directory.Exists(_paths.GetInstallDirectory(package.Identity.PackageId, package.Identity.Version)).Should().BeFalse();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("templates/stale")]
    public async Task InstallAsync_StaleRegistryTemplateRoot_RefreshesMetadataWithoutForce(string? registeredRoot)
    {
        WorkloadStore store = new(_paths);
        WorkloadDeploymentService deployment = new(_paths, store, new WorkloadMetadataReader());
        InspectedWorkloadPackage package = Package("1.0.0", TemplateMetadata("templates/current"));
        await deployment.InstallAsync(package, _root, WorkloadOwnershipKind.Explicit, logicalPackage: null, force: false);
        await store.SaveWorkloadAsync(new WorkloadEntry
        {
            PackageId = package.Identity.PackageId,
            PackageVersion = package.Identity.Version,
            TemplateRoot = registeredRoot,
        });

        WorkloadInstallResult result = await deployment.InstallAsync(
            package, _root, WorkloadOwnershipKind.Explicit, logicalPackage: null, force: false);

        result.AlreadyInstalled.Should().BeFalse();
        (await store.GetWorkloadsAsync()).Should().ContainSingle().Which.TemplateRoot.Should().Be("templates/current");
    }

    [Fact]
    public async Task GetUpdateTargetAsync_LogicalOwnership_SelectsHighestLogicalVersion()
    {
        WorkloadEntry older = Entry("example.workload.win-x64", "7.0.0", logicalPackage: Logical("example.pointer", "1.0.0"));
        WorkloadEntry newer = Entry("example.workload.win-x64", "3.0.0", logicalPackage: Logical("example.pointer", "2.0.0"));
        _store.GetWorkloadsAsync(Arg.Any<CancellationToken>()).Returns([older, newer]);

        WorkloadEntry result =
            await _deploymentService.GetUpdateTargetAsync("example.pointer", targetInstalledVersion: null, WorkloadOwnershipKind.Logical);

        result.Should().BeSameAs(newer);
    }

    [Fact]
    public async Task UninstallAsync_FinalOwnershipRemoved_DeletesPayload()
    {
        WorkloadEntry entry = Entry("example.workload", "1.0.0", explicitlyInstalled: true);
        string installPath = _paths.GetInstallDirectory(entry.PackageId, entry.PackageVersion);
        Directory.CreateDirectory(installPath);
        _store.RemoveOwnershipAsync(
                entry.PackageId, entry.PackageVersion, WorkloadOwnershipKind.Explicit, Arg.Any<CancellationToken>())
            .Returns(new WorkloadOwnershipRemovalResult(OwnershipRemoved: true, EntryRemoved: true, Entry: null));

        bool removed = await _deploymentService.UninstallAsync(entry.PackageId, entry.PackageVersion, WorkloadOwnershipKind.Explicit);

        removed.Should().BeTrue();
        Directory.Exists(installPath).Should().BeFalse();
    }

    private static WorkloadEntry Entry(string packageId, string version, bool explicitlyInstalled = false,
        string? runtimeIdentifier = null, LogicalPackage? logicalPackage = null)
        => new()
        {
            PackageId = packageId,
            PackageVersion = version,
            IsImplicitlyInstalled = !explicitlyInstalled,
            InstallRefCount = 1,
            RuntimeIdentifier = runtimeIdentifier,
            LogicalPackage = logicalPackage,
        };

    private static WorkloadEntry CompleteEntry(LogicalPackage? logicalPackage = null)
        => new()
        {
            PackageId = "example.workload.win-x64",
            PackageVersion = "1.0.0",
            Kind = WorkloadKind.Workload,
            Aliases = ["example", "sample"],
            DisplayName = "Existing display name",
            Description = "Existing description.",
            Source = "https://old.example.test/v3/index.json",
            RuntimeIdentifier = "win-x64",
            TemplateRoot = "templates/existing",
            IsImplicitlyInstalled = true,
            LogicalPackage = logicalPackage,
            InstallRefCount = 2,
            EntryPoint = new EntryPointSpec { AssemblyPath = "Existing.dll", Type = "Existing.Type" },
        };

    private static LogicalPackage Logical(string packageId, string version)
        => new()
        {
            PackageId = packageId,
            PackageVersion = version,
            Aliases = ["pointer"],
            DisplayName = "Pointer display name",
            Description = "Pointer description.",
            Source = "https://pointer.example.test/v3/index.json",
        };

    private static WorkloadMetadata TemplateMetadata(string? templateRoot)
        => new()
        {
            Schema = WorkloadManifestSchema.PackageManifestV1Schema,
            EntryPoint = new EntryPointSpec { AssemblyPath = "Example.dll", Type = "Example.Workload" },
            TemplateRoot = templateRoot,
        };

    private InspectedWorkloadPackage Package(string version, WorkloadMetadata metadata)
        => new(
            BuildPackage("example.workload", version, metadata),
            new WorkloadPackageIdentity("example.workload", version, [WorkloadPackageTypes.Workload], [], [], null, null),
            metadata,
            WorkloadPackageRole.Ordinary);

    private string BuildPackage(string packageId, string version, WorkloadMetadata? metadata = null)
    {
        string manifestPath = Path.Combine(_root, $"workload-{Guid.NewGuid():N}.json");
        File.WriteAllText(manifestPath, metadata is null ? "{}" : JsonSerializer.Serialize(metadata, WorkloadJsonContext.Default.WorkloadMetadata));
        var builder = new PackageBuilder
        {
            Id = packageId,
            Version = NuGetVersion.Parse(version),
            Description = "Test package.",
        };
        builder.Authors.Add("test");
        builder.Files.Add(new PhysicalPackageFile { SourcePath = manifestPath, TargetPath = "workload.json" });

        string packagePath = Path.Combine(_root, $"{packageId}.{version}.nupkg");
        using FileStream stream = File.Create(packagePath);
        builder.Save(stream);
        return packagePath;
    }

    private static WorkloadMetadata Metadata(string runtimeIdentifier)
        => new()
        {
            Schema = WorkloadManifestSchema.PackageManifestV1Schema,
            Kind = WorkloadKind.Workload,
            RuntimeIdentifier = runtimeIdentifier,
        };
}
