// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.IO.Compression;
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
        entry.PackageRoot = Path.Combine(_root, "reused-payload");
        _store.GetWorkloadsAsync(Arg.Any<CancellationToken>()).Returns([entry]);
        Directory.CreateDirectory(entry.PackageRoot);

        WorkloadInstallResult? result = await _deploymentService.TryReuseExplicitAsync(entry.PackageId, entry.PackageVersion);

        result.Should().NotBeNull();
        result!.AlreadyInstalled.Should().BeTrue();
        result.Entry.Should().BeSameAs(entry);
        _metadataReader.DidNotReceiveWithAnyArgs().Read(default!);
    }

    [Fact]
    public async Task TryReuseExplicitAsync_MissingPackageRoot_Throws()
    {
        WorkloadEntry entry = Entry("example.workload", "1.0.0", explicitlyInstalled: true);
        entry.PackageRoot = string.Empty;
        _store.GetWorkloadsAsync(Arg.Any<CancellationToken>()).Returns([entry]);

        await FluentActions.Awaiting(() => _deploymentService.TryReuseExplicitAsync(entry.PackageId, entry.PackageVersion))
            .Should().ThrowExactlyAsync<ArgumentException>();
    }

    [Fact]
    public async Task TryReuseImplementationForUpdateAsync_MovesOwnershipAndDeletesOrphanedPayload()
    {
        LogicalPackage oldLogical = Logical("example.pointer", "1.0.0");
        LogicalPackage newLogical = Logical("example.pointer", "2.0.0");
        WorkloadEntry current = Entry("example.workload.win-x64", "1.0.0", logicalPackage: oldLogical);
        WorkloadEntry reusable = Entry("example.workload.win-x64", "2.0.0", runtimeIdentifier: "win-x64");
        _store.GetWorkloadsAsync(Arg.Any<CancellationToken>()).Returns([current, reusable]);
        current.PackageRoot = Path.Combine(_root, "old-payload");
        reusable.PackageRoot = Path.Combine(_root, "reusable-payload");
        string currentPath = current.PackageRoot;
        string reusablePath = reusable.PackageRoot;
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
        string installPath = existing.PackageRoot;
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
        string installPath = reusable.PackageRoot;
        Directory.CreateDirectory(installPath);
        _metadataReader.Read(installPath).Throws(new InvalidWorkloadException("invalid manifest"));

        WorkloadInstallResult? result = await _deploymentService.TryReuseImplementationAsync(
            reusable.PackageId, reusable.PackageVersion, "win-x64", Logical("example.pointer", "1.0.0"));

        result.Should().BeNull();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Deployment_ReplacementExtractsAllPayloadExceptNuGetBookkeeping(bool update)
    {
        WorkloadStore store = new(_paths);
        WorkloadDeploymentService deployment = new(_paths, store, new WorkloadMetadataReader());
        WorkloadInstallResult installed = await deployment.InstallAsync(
            Package("1.0.0", TemplateMetadata(null)),
            _root, WorkloadOwnershipKind.Explicit, logicalPackage: null, force: false);
        InspectedWorkloadPackage package = Package(update ? "2.0.0" : "1.0.0", TemplateMetadata(null));
        string[] excluded =
        [
            "[Content_Types].xml", ".signature.p7s",
            "_rels/custom.xml", "_RELS/nested/relationships.xml", @"_rels\nested\windows.xml",
            "payload/../_rels/normalized.xml",
            "package/services/metadata/core-properties/extra.PSMDCP",
        ];
        string[] payload =
        [
            "README.md", "LICENSE", "extra.NUSPEC", "content/templates/function.txt", "build/custom.targets",
            "assets/library.nuspec", "assets/[Content_Types].xml", "assets/.signature.p7s",
            "assets/_rels/data.xml", "_rels-other/data.xml", "other.psmdcp",
            "package/services/metadata/core-properties/readme.txt",
        ];
        using (ZipArchive archive = ZipFile.Open(package.Path, ZipArchiveMode.Update))
        {
            archive.CreateEntry("empty/");
            archive.CreateEntry(@"windows-empty\");
            archive.CreateEntry("empty-parent/child/");
            archive.CreateEntry("content/");
            foreach (string path in excluded.Concat(payload))
            {
                using StreamWriter writer = new(archive.CreateEntry(path).Open());
                writer.Write(path);
            }
        }

        WorkloadEntry entry = update
            ? await deployment.UpdateAsync(installed.Entry, package, _root, WorkloadOwnershipKind.Explicit, logicalPackage: null)
            : (await deployment.InstallAsync(package, _root, WorkloadOwnershipKind.Explicit, logicalPackage: null, force: true)).Entry;

        string installPath = _paths.GetInstallDirectory(entry.PackageId, entry.PackageVersion);
        string[] extracted = [.. Directory.EnumerateFiles(installPath, "*", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(installPath, path).Replace('\\', '/'))];
        extracted.Should().BeEquivalentTo(payload.Concat(["workload.json", "example.workload.nuspec"]));
        foreach (string path in payload)
        {
            File.ReadAllText(Path.Combine(installPath, path)).Should().Be(path);
        }

        Directory.Exists(Path.Combine(installPath, "_rels")).Should().BeFalse();
        Directory.Exists(Path.Combine(installPath, "empty")).Should().BeFalse();
        Directory.Exists(Path.Combine(installPath, "windows-empty")).Should().BeFalse();
        Directory.Exists(Path.Combine(installPath, "empty-parent")).Should().BeFalse();
        entry.TemplateRoot.Should().BeNull();
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
        InspectedWorkloadPackage initial = Package("1.0.0", initialMetadata, includeTemplates: true);
        WorkloadInstallResult installed = await deployment.InstallAsync(
            initial, _root, WorkloadOwnershipKind.Explicit, logicalPackage: null, force: false);
        (await store.GetWorkloadsAsync()).Should().ContainSingle().Which.TemplateRoot.Should().Be(initialRoot);
        string initialPath = _paths.GetInstallDirectory(initial.Identity.PackageId, initial.Identity.Version);
        if (initialRoot is not null)
        {
            File.ReadAllText(Path.Combine(initialPath, initialRoot, "template.txt")).Should().Be("1.0.0");
        }

        WorkloadMetadata newMetadata = TemplateMetadata(newRoot);
        InspectedWorkloadPackage replacement = Package(reinstall ? "1.0.0" : "2.0.0", newMetadata, includeTemplates: true);

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
        string replacementPath = _paths.GetInstallDirectory(persisted.PackageId, persisted.PackageVersion);
        if (newRoot is not null)
        {
            File.ReadAllText(Path.Combine(replacementPath, newRoot, "template.txt")).Should().Be(replacement.Identity.Version);
        }

        if (initialRoot is not null)
        {
            Directory.Exists(Path.Combine(replacementPath, initialRoot)).Should().BeFalse();
        }
    }

    [Theory]
    [InlineData(false, "templates/../../escaped.txt")]
    [InlineData(true, "templates/../../escaped.txt")]
    [InlineData(false, @"templates\..\..\escaped.txt")]
    [InlineData(true, @"templates\..\..\escaped.txt")]
    [InlineData(false, "docs/../../escaped.txt")]
    [InlineData(true, "docs/../../escaped.txt")]
    [InlineData(false, "_rels/../../escaped.txt")]
    [InlineData(true, "_rels/../../escaped.txt")]
    public async Task Deployment_PackageEntryEscapesStaging_LeavesExistingInstallIntact(bool update, string entryPath)
    {
        WorkloadStore store = new(_paths);
        WorkloadDeploymentService deployment = new(_paths, store, new WorkloadMetadataReader());
        WorkloadInstallResult installed = await deployment.InstallAsync(
            Package("1.0.0", TemplateMetadata("templates"), includeTemplates: true),
            _root, WorkloadOwnershipKind.Explicit, logicalPackage: null, force: false);
        byte[] before = await File.ReadAllBytesAsync(_paths.WorkloadRegistryPath);
        InspectedWorkloadPackage package = Package(update ? "2.0.0" : "1.0.0", TemplateMetadata("templates"));
        using (ZipArchive archive = ZipFile.Open(package.Path, ZipArchiveMode.Update))
        {
            using StreamWriter writer = new(archive.CreateEntry(entryPath).Open());
            writer.Write("escaped");
        }

        Func<Task> action = update
            ? () => deployment.UpdateAsync(installed.Entry, package, _root, WorkloadOwnershipKind.Explicit, logicalPackage: null)
            : () => deployment.InstallAsync(package, _root, WorkloadOwnershipKind.Explicit, logicalPackage: null, force: true);

        await action.Should().ThrowExactlyAsync<InvalidWorkloadException>().WithMessage("*outside*install directory*");
        (await File.ReadAllBytesAsync(_paths.WorkloadRegistryPath)).Should().Equal(before);
        string initialPath = _paths.GetInstallDirectory(installed.Entry.PackageId, installed.Entry.PackageVersion);
        File.ReadAllText(Path.Combine(initialPath, "templates", "template.txt")).Should().Be("1.0.0");
        Directory.EnumerateFiles(_paths.WorkloadsRoot, "escaped.txt", SearchOption.AllDirectories).Should().BeEmpty();
        Directory.EnumerateDirectories(Path.GetDirectoryName(initialPath)!, "*.staging-*").Should().BeEmpty();
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
    public async Task InstallAsync_DifferentRegistryTemplateRoot_ReusesInstalledPayload(string? registeredRoot)
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
        byte[] before = await File.ReadAllBytesAsync(_paths.WorkloadRegistryPath);
        string payloadPath = Path.Combine(_paths.GetInstallDirectory(package.Identity.PackageId, package.Identity.Version), "existing-payload.txt");
        File.WriteAllText(payloadPath, "preserve");

        WorkloadInstallResult result = await deployment.InstallAsync(
            package, _root, WorkloadOwnershipKind.Explicit, logicalPackage: null, force: false);

        result.AlreadyInstalled.Should().BeTrue();
        (await File.ReadAllBytesAsync(_paths.WorkloadRegistryPath)).Should().Equal(before);
        File.ReadAllText(payloadPath).Should().Be("preserve");
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
        entry.PackageRoot = Path.Combine(_root, "removed-payload");
        string installPath = entry.PackageRoot;
        Directory.CreateDirectory(installPath);
        _store.RemoveOwnershipAsync(
                entry.PackageId, entry.PackageVersion, WorkloadOwnershipKind.Explicit, Arg.Any<CancellationToken>())
            .Returns(new WorkloadOwnershipRemovalResult(OwnershipRemoved: true, EntryRemoved: true, Entry: entry));

        bool removed = await _deploymentService.UninstallAsync(entry.PackageId, entry.PackageVersion, WorkloadOwnershipKind.Explicit);

        removed.Should().BeTrue();
        Directory.Exists(installPath).Should().BeFalse();
    }

    [Fact]
    public async Task UninstallAsync_RemovedEntryMissing_Throws()
    {
        _store.RemoveOwnershipAsync("pkg", "1.0.0", WorkloadOwnershipKind.Explicit, Arg.Any<CancellationToken>())
            .Returns(new WorkloadOwnershipRemovalResult(true, true, null));

        await FluentActions.Awaiting(() => _deploymentService.UninstallAsync("pkg", "1.0.0", WorkloadOwnershipKind.Explicit))
            .Should().ThrowExactlyAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task UninstallAsync_RemovedEntryHasNoPackageRoot_Throws()
    {
        WorkloadEntry entry = Entry("pkg", "1.0.0", explicitlyInstalled: true);
        entry.PackageRoot = string.Empty;
        _store.RemoveOwnershipAsync(entry.PackageId, entry.PackageVersion, WorkloadOwnershipKind.Explicit, Arg.Any<CancellationToken>())
            .Returns(new WorkloadOwnershipRemovalResult(true, true, entry));

        await FluentActions.Awaiting(() => _deploymentService.UninstallAsync(entry.PackageId, entry.PackageVersion, WorkloadOwnershipKind.Explicit))
            .Should().ThrowExactlyAsync<ArgumentException>();
    }

    private WorkloadEntry Entry(string packageId, string version, bool explicitlyInstalled = false,
        string? runtimeIdentifier = null, LogicalPackage? logicalPackage = null)
        => new()
        {
            PackageId = packageId,
            PackageVersion = version,
            PackageRoot = _paths.GetInstallDirectory(packageId, version),
            IsImplicitlyInstalled = !explicitlyInstalled,
            InstallRefCount = 1,
            RuntimeIdentifier = runtimeIdentifier,
            LogicalPackage = logicalPackage,
        };

    private WorkloadEntry CompleteEntry(LogicalPackage? logicalPackage = null)
        => new()
        {
            PackageId = "example.workload.win-x64",
            PackageVersion = "1.0.0",
            PackageRoot = Path.Combine(_root, "existing-package-root"),
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

    private InspectedWorkloadPackage Package(string version, WorkloadMetadata metadata, bool includeTemplates = false)
        => new(
            BuildPackage("example.workload", version, metadata, includeTemplates),
            new WorkloadPackageIdentity("example.workload", version, [WorkloadPackageTypes.Workload], [], [], null, null),
            metadata,
            WorkloadPackageRole.Ordinary);

    private string BuildPackage(string packageId, string version, WorkloadMetadata? metadata = null, bool includeTemplates = false)
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
        if (includeTemplates && metadata?.TemplateRoot is string templateRoot)
        {
            string templatePath = Path.Combine(_root, $"template-{Guid.NewGuid():N}.txt");
            File.WriteAllText(templatePath, version);
            builder.Files.Add(new PhysicalPackageFile { SourcePath = templatePath, TargetPath = $"{templateRoot}/template.txt" });
        }

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
