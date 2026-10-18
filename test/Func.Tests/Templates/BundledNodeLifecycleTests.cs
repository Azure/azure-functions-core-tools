// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.CommandLine;
using System.Text.Json.Nodes;
using Azure.Functions.Cli.Commands.Workload;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Workloads.Discovery;
using Azure.Functions.Cli.Workloads.Install;
using Azure.Functions.Cli.Workloads.Storage;
using NSubstitute;

namespace Azure.Functions.Cli.Tests.Templates;

public sealed partial class BundledNodeTemplateTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Lifecycle_SiblingReceiptOriginDriftsAfterPreparation_RefusesBeforeAdmission(bool incomingBundled)
    {
        var installed = await _installer.InstallFromPackageAsync(MigrationArchive("1.0.0"));
        string incoming = MigrationArchive("1.1.0", incomingBundled);
        var coordinator = new FileWorkloadOperationCoordinator(_paths);
        var deployment = AdmissionDeployment(coordinator);
        var package = await AdmissionPackageInspector().InspectAsync(incoming);
        using var prepared = await deployment.PrepareAsync(package, incoming, WorkloadOwnershipKind.Explicit, null, false, null,
            CancellationToken.None);
        var receipt = JsonNode.Parse(File.ReadAllText(BundledOwnerPath("1.0.0")))!;
        receipt["origin"]!["source"] = "changed-after-preparation";
        File.WriteAllText(BundledOwnerPath("1.0.0"), receipt.ToJsonString());
        string registry = File.ReadAllText(_paths.WorkloadRegistryPath);
        var native = MigrationFiles(Path.Combine(Home, "templates"));
        using var operation = await coordinator.AcquireAsync(CancellationToken.None);
        Func<Task> commit = () => deployment.InstallPreparedAsync(operation, prepared, null, CancellationToken.None);

        await commit.Should().ThrowAsync<InvalidWorkloadException>().WithMessage("*ownership*");

        File.ReadAllText(_paths.WorkloadRegistryPath).Should().Be(registry);
        MigrationFiles(Path.Combine(Home, "templates")).Should().BeEquivalentTo(native);
        Directory.Exists(_paths.GetInstallDirectory(installed.Entry.PackageId, "1.1.0")).Should().BeFalse();
        operation.IsDirty.Should().BeFalse();
    }

    [Theory]
    [InlineData("manifest")]
    [InlineData("assembly")]
    [InlineData("contentDirectory")]
    [InlineData("kind")]
    [InlineData("rid")]
    [InlineData("entryType")]
    public async Task Lifecycle_UnbundledReuse_DamagedOrConflictingPayloadCannotBecomeSuccessfulSnapshot(string corruption)
    {
        var installed = await _installer.InstallFromPackageAsync(MigrationArchive("1.0.0", bundled: false));
        string path = _paths.GetInstallDirectory(installed.Entry.PackageId, "1.0.0");
        string manifestPath = Path.Combine(path, "workload.json");
        if (corruption == "manifest") File.Delete(manifestPath);
        else if (corruption == "assembly") File.Delete(Path.Combine(path, installed.Entry.EntryPoint!.AssemblyPath));
        else if (corruption == "contentDirectory") Directory.Delete(Path.Combine(path, "tools", "any"), recursive: true);
        else
        {
            var manifest = JsonNode.Parse(File.ReadAllText(manifestPath))!;
            if (corruption == "rid") manifest["runtimeIdentifier"] = "different-rid";
            else if (corruption == "kind")
            {
                manifest["kind"] = "content";
                manifest.AsObject().Remove("entryPoint");
            }
            else manifest["entryPoint"]!["type"] = "Different.Type";
            File.WriteAllText(manifestPath, manifest.ToJsonString());
        }
        var before = MigrationFiles(path);
        string registry = File.ReadAllText(_paths.WorkloadRegistryPath);
        Func<Task> ensure = () => _installer.EnsureInstalledAsync(installed.Entry, CancellationToken.None, checkOnly: true);

        await ensure.Should().ThrowAsync<InvalidWorkloadException>().WithMessage("*damaged*");

        File.ReadAllText(_paths.WorkloadRegistryPath).Should().Be(registry);
        MigrationFiles(path).Should().BeEquivalentTo(before);
        Directory.Exists(Path.Combine(Home, "templates")).Should().BeFalse();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Lifecycle_MissingRegistrationWithCachedArchive_RepairsOnlyExactProvenBytes(bool corrupt)
    {
        var installed = await _installer.InstallFromPackageAsync(MigrationArchive("1.0.0"));
        string nativeRegistry = Path.Combine(Home, "templates", "packages.json");
        var registry = JsonNode.Parse(File.ReadAllText(nativeRegistry))!;
        string archive = registry["Packages"]![0]!["MountPointUri"]!.GetValue<string>();
        registry["Packages"]!.AsArray().Clear();
        File.WriteAllText(nativeRegistry, registry.ToJsonString());
        if (corrupt) File.AppendAllText(archive, "changed");
        var before = MigrationFiles(Path.Combine(Home, "templates"));
        Func<Task> check = () => _installer.EnsureInstalledAsync(installed.Entry, CancellationToken.None, checkOnly: true);
        await check.Should().ThrowAsync<InvalidWorkloadException>();
        MigrationFiles(Path.Combine(Home, "templates")).Should().BeEquivalentTo(before);

        Func<Task> repair = () => _installer.EnsureInstalledAsync(installed.Entry, CancellationToken.None);

        if (corrupt)
        {
            await repair.Should().ThrowAsync<InvalidWorkloadException>().WithMessage("*digest*");
            MigrationFiles(Path.Combine(Home, "templates")).Should().BeEquivalentTo(before);
        }
        else
        {
            await repair();
            await AssertMigrationNativeVersionsAsync("1.0.0");
            await _installer.EnsureInstalledAsync(installed.Entry, CancellationToken.None, checkOnly: true);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Lifecycle_UninstallCleanupOrRollbackFailure_LeavesTruthfulPartialStateAndRecoveryGate(bool failRollback)
    {
        var installed = await _installer.InstallFromPackageAsync(MigrationArchive("1.0.0"));
        string path = _paths.GetInstallDirectory(installed.Entry.PackageId, "1.0.0");
        var files = Substitute.For<IFileSystem>();
        files.DirectoryExists(Arg.Any<string>()).Returns(call => _files.DirectoryExists(call.Arg<string>()));
        files.FileExists(Arg.Any<string>()).Returns(call => _files.FileExists(call.Arg<string>()));
        files.GetFiles(Arg.Any<string>()).Returns(call => _files.GetFiles(call.Arg<string>()));
        files.ComputeSha256Async(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(call => _files.ComputeSha256Async(call.Arg<string>(), call.Arg<CancellationToken>()));
        files.ReadAllTextIfExistsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(call => _files.ReadAllTextIfExistsAsync(call.Arg<string>(), call.Arg<CancellationToken>()));
        files.WriteAllTextAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new IOException("Owned rollback failure.")));
        files.When(fs => fs.DeleteDirectory(path)).Do(_ => throw new IOException("Owned payload cleanup failure."));
        var store = AdmissionStore();
        store.RemoveOwnershipAsync(Arg.Any<IWorkloadOperation>(), Arg.Any<string>(), Arg.Any<string>(),
            Arg.Any<WorkloadOwnershipKind>(), Arg.Any<CancellationToken>()).Returns<Task<WorkloadOwnershipRemovalResult>>(async call =>
        {
            var result = await _workloads.RemoveOwnershipAsync(call.Arg<IWorkloadOperation>(), call.ArgAt<string>(1), call.ArgAt<string>(2),
                call.Arg<WorkloadOwnershipKind>(), call.Arg<CancellationToken>());
            if (failRollback) throw new IOException("Owned registry failure requiring rollback.");
            return result;
        });
        var installer = MigrationInstaller(_inspector, store: store, deploymentFiles: files);
        Func<Task> remove = () => installer.UninstallAsync(installed.Entry.PackageId, "1.0.0");

        if (failRollback)
        {
            await remove.Should().ThrowAsync<IOException>().WithMessage("*Owned rollback failure*");
            await AssertMigrationNativeVersionsAsync("1.0.0");
        }
        else
        {
            await remove.Should().ThrowAsync<InvalidWorkloadException>().WithMessage("*ownership and native templates were removed*could not be deleted*");
            await AssertMigrationNativeVersionsAsync();
        }
        Directory.Exists(path).Should().BeTrue();
        (await _workloads.GetWorkloadsAsync()).Should().BeEmpty();
        File.Exists(Path.Combine(_paths.Home, ".workload-operations", "dirty.intent")).Should().BeTrue();
        Func<Task> retry = () => _installer.UninstallAsync(installed.Entry.PackageId, "1.0.0");
        await retry.Should().ThrowAsync<WorkloadRecoveryRequiredException>();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Lifecycle_UninstallNativeProtection_BlocksOtherNativeWriterThroughCommitOrRecovery(bool fail)
    {
        var installed = await _installer.InstallFromPackageAsync(MigrationArchive("1.0.0"));
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var store = AdmissionStore();
        store.RemoveOwnershipAsync(Arg.Any<IWorkloadOperation>(), Arg.Any<string>(), Arg.Any<string>(),
            Arg.Any<WorkloadOwnershipKind>(), Arg.Any<CancellationToken>()).Returns<Task<WorkloadOwnershipRemovalResult>>(async call =>
        {
            var result = await _workloads.RemoveOwnershipAsync(call.Arg<IWorkloadOperation>(), call.ArgAt<string>(1), call.ArgAt<string>(2),
                call.Arg<WorkloadOwnershipKind>(), call.Arg<CancellationToken>());
            entered.TrySetResult();
            await release.Task.WaitAsync(call.Arg<CancellationToken>());
            if (fail) throw new IOException("Owned protected removal failure.");
            return result;
        });
        Task<bool> removal = MigrationInstaller(_inspector, store: store).UninstallAsync(installed.Entry.PackageId, "1.0.0");
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(30));
        var waiting = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var writer = _templates.OpenWriteAsync(new(WorkingDirectory.FromExplicit(_root)), CancellationToken.None,
            _ => waiting.TrySetResult());
        try
        {
            await waiting.Task.WaitAsync(TimeSpan.FromSeconds(30));
            writer.IsCompleted.Should().BeFalse();
        }
        finally
        {
            release.TrySetResult();
            if (fail)
            {
                Func<Task> finish = async () => await removal.WaitAsync(TimeSpan.FromSeconds(30));
                await finish.Should().ThrowAsync<IOException>().WithMessage("*Owned protected removal failure*");
            }
            else
            {
                (await removal.WaitAsync(TimeSpan.FromSeconds(30))).Should().BeTrue();
            }
            using var lease = await writer.WaitAsync(TimeSpan.FromSeconds(30));
        }
        await AssertMigrationNativeVersionsAsync(fail ? ["1.0.0"] : []);
        File.Exists(Path.Combine(_paths.Home, ".workload-operations", "dirty.intent")).Should().BeFalse();
    }

    [Fact]
    public async Task Lifecycle_MissingOwnedNativeArchive_CheckFailsWithoutWritesAndExplicitRepairRestoresExactBytes()
    {
        var installed = await _installer.InstallFromPackageAsync(MigrationArchive("1.0.0"));
        string archive;
        string expected;
        using (var native = await _templates.OpenReadAsync(new(WorkingDirectory.FromExplicit(_root)), CancellationToken.None))
        {
            var package = (await native.PackageManager.GetManagedTemplatePackagesAsync(false, CancellationToken.None)).Single();
            archive = package.MountPointUri;
            expected = await _files.ComputeSha256Async(archive, CancellationToken.None);
        }
        File.Delete(archive);
        var before = MigrationFiles(Path.Combine(Home, "templates"));
        string registry = File.ReadAllText(_paths.WorkloadRegistryPath);
        Func<Task> check = () => _installer.EnsureInstalledAsync(installed.Entry, CancellationToken.None, checkOnly: true);

        await check.Should().ThrowAsync<InvalidWorkloadException>();
        MigrationFiles(Path.Combine(Home, "templates")).Should().BeEquivalentTo(before);
        await _installer.EnsureInstalledAsync(installed.Entry, CancellationToken.None);

        File.ReadAllText(_paths.WorkloadRegistryPath).Should().Be(registry);
        await AssertMigrationNativeVersionsAsync("1.0.0");
        (await _files.ComputeSha256Async(archive, CancellationToken.None)).Should().Be(expected);
        await _installer.EnsureInstalledAsync(installed.Entry, CancellationToken.None, checkOnly: true);
    }

    [Theory]
    [InlineData("1.0.0", "1.1.0", false)]
    [InlineData("1.1.0", "1.0.0", false)]
    [InlineData("1.0.0", "1.1.0", true)]
    [InlineData("1.1.0", "1.0.0", true)]
    public async Task Lifecycle_SideBySideInEitherOrder_RetainsExactOwnersAndSupportsRepeatedInstall(
        string firstVersion, string secondVersion, bool force)
    {
        var first = await _installer.InstallFromPackageAsync(MigrationArchive(firstVersion));
        string firstPath = _paths.GetInstallDirectory(first.Entry.PackageId, firstVersion);
        var payload = MigrationFiles(firstPath);
        string receipt = File.ReadAllText(BundledOwnerPath(firstVersion));
        string secondArchive = MigrationArchive(secondVersion);

        var second = await _installer.InstallFromPackageAsync(secondArchive, force);
        var repeated = await _installer.InstallFromPackageAsync(secondArchive);

        repeated.AlreadyInstalled.Should().BeTrue();
        repeated.Entry.Should().BeEquivalentTo(second.Entry);
        (await _workloads.GetWorkloadsAsync()).Should().HaveCount(2);
        await AssertMigrationNativeVersionsAsync(firstVersion, secondVersion);
        MigrationFiles(firstPath).Should().BeEquivalentTo(payload);
        File.ReadAllText(BundledOwnerPath(firstVersion)).Should().Be(receipt);
        await _installer.EnsureInstalledAsync(first.Entry, CancellationToken.None, checkOnly: true);
        await _installer.EnsureInstalledAsync(second.Entry, CancellationToken.None, checkOnly: true);
    }

    [Theory]
    [InlineData("1.0.0")]
    [InlineData("1.1.0")]
    public async Task Lifecycle_UninstallExactVersion_PreservesSiblingUserTemplatesAndProject(string removedVersion)
    {
        var first = await _installer.InstallFromPackageAsync(MigrationArchive("1.0.0"));
        var second = await _installer.InstallFromPackageAsync(MigrationArchive("1.1.0"));
        string retainedVersion = removedVersion == "1.0.0" ? "1.1.0" : "1.0.0";
        var retained = retainedVersion == "1.0.0" ? first.Entry : second.Entry;
        string retainedPath = _paths.GetInstallDirectory(retained.PackageId, retainedVersion);
        var payload = MigrationFiles(retainedPath);
        string receipt = File.ReadAllText(BundledOwnerPath(retainedVersion));
        await MigrationInstallUserPackageAsync(TemplateFixtures.WriteBasicPackage(_root));
        string project = Path.Combine(_root, "authored-project");
        Directory.CreateDirectory(project);
        File.WriteAllText(Path.Combine(project, "authored.js"), "export const retained = true;");
        var projectFiles = MigrationFiles(project);

        (await _installer.UninstallAsync(first.Entry.PackageId, removedVersion)).Should().BeTrue();
        (await _installer.UninstallAsync(first.Entry.PackageId, removedVersion)).Should().BeFalse();

        (await _workloads.GetWorkloadsAsync()).Should().ContainSingle().Which.Should().BeEquivalentTo(retained);
        Directory.Exists(_paths.GetInstallDirectory(first.Entry.PackageId, removedVersion)).Should().BeFalse();
        File.Exists(BundledOwnerPath(removedVersion)).Should().BeFalse();
        File.ReadAllText(BundledOwnerPath(retainedVersion)).Should().Be(receipt);
        MigrationFiles(retainedPath).Should().BeEquivalentTo(payload);
        MigrationFiles(project).Should().BeEquivalentTo(projectFiles);
        using var native = await _templates.OpenReadAsync(new(WorkingDirectory.FromExplicit(_root)), CancellationToken.None);
        var packages = await native.PackageManager.GetManagedTemplatePackagesAsync(false, CancellationToken.None);
        packages.Should().HaveCount(2).And.Contain(package => package.Version == retainedVersion);
        (await native.PackageManager.GetTemplatesAsync(CancellationToken.None)).Should()
            .Contain(template => template.Identity == TemplateFixtures.ItemTemplateIdentity);
        File.Exists(Path.Combine(_paths.Home, ".workload-operations", "dirty.intent")).Should().BeFalse();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Lifecycle_UninstallSharedPhysicalOwner_DetachesOnlyRequestedReferenceThenRemovesLastOwner(bool logicalFirst)
    {
        var shared = await MigrationSharedBundledAsync();
        string path = _paths.GetInstallDirectory(shared.PackageId, shared.PackageVersion);
        var payload = MigrationFiles(path);
        var native = MigrationFiles(Path.Combine(Home, "templates"));
        var firstOwnership = logicalFirst ? WorkloadOwnershipKind.Logical : WorkloadOwnershipKind.Explicit;
        string firstId = logicalFirst ? shared.LogicalPackage!.PackageId : shared.PackageId;
        var lastOwnership = logicalFirst ? WorkloadOwnershipKind.Explicit : WorkloadOwnershipKind.Logical;
        string lastId = logicalFirst ? shared.PackageId : shared.LogicalPackage!.PackageId;

        (await _installer.UninstallAsync(firstId, "1.0.0", firstOwnership)).Should().BeTrue();

        var remaining = (await _workloads.GetWorkloadsAsync()).Should().ContainSingle().Subject;
        remaining.InstallRefCount.Should().Be(1);
        remaining.IsImplicitlyInstalled.Should().Be(!logicalFirst);
        (remaining.LogicalPackage is null).Should().Be(logicalFirst);
        MigrationFiles(path).Should().BeEquivalentTo(payload);
        MigrationFiles(Path.Combine(Home, "templates")).Should().BeEquivalentTo(native);
        await _installer.EnsureInstalledAsync(remaining, CancellationToken.None, checkOnly: true);
        (await _installer.UninstallAsync(lastId, "1.0.0", lastOwnership)).Should().BeTrue();
        (await _workloads.GetWorkloadsAsync()).Should().BeEmpty();
        Directory.Exists(path).Should().BeFalse();
        File.Exists(BundledOwnerPath("1.0.0")).Should().BeFalse();
        await AssertMigrationNativeVersionsAsync();
    }

    [Theory]
    [InlineData("receipt")]
    [InlineData("origin")]
    [InlineData("archive")]
    [InlineData("legacyConflict")]
    public async Task Lifecycle_UninstallUnprovenOwner_FailsClosedWithoutChangingEitherStore(string corruption)
    {
        var installed = await _installer.InstallFromPackageAsync(MigrationArchive("1.0.0"));
        string path = _paths.GetInstallDirectory(installed.Entry.PackageId, "1.0.0");
        if (corruption == "receipt") File.Delete(BundledOwnerPath("1.0.0"));
        else if (corruption == "origin") File.WriteAllText(Path.Combine(path, ".bundled-template-origin.json"), "{");
        else if (corruption == "legacyConflict")
        {
            var receipt = JsonNode.Parse(File.ReadAllText(BundledOwnerPath("1.0.0")))!;
            receipt["origin"]!["source"] = "conflicting-source";
            File.WriteAllText(Path.Combine(Home, "templates", ".bundled-owners", "azure.functions.templates.node.basic.json"),
                receipt.ToJsonString());
        }
        else
        {
            using var native = await _templates.OpenReadAsync(new(WorkingDirectory.FromExplicit(_root)), CancellationToken.None);
            var package = (await native.PackageManager.GetManagedTemplatePackagesAsync(false, CancellationToken.None)).Single();
            File.AppendAllText(package.MountPointUri, "changed");
        }
        var payload = MigrationFiles(path);
        var nativeFiles = MigrationFiles(Path.Combine(Home, "templates"));
        string registry = File.ReadAllText(_paths.WorkloadRegistryPath);

        Func<Task> remove = () => _installer.UninstallAsync(installed.Entry.PackageId, "1.0.0");

        await remove.Should().ThrowAsync<InvalidWorkloadException>();
        File.ReadAllText(_paths.WorkloadRegistryPath).Should().Be(registry);
        MigrationFiles(path).Should().BeEquivalentTo(payload);
        MigrationFiles(Path.Combine(Home, "templates")).Should().BeEquivalentTo(nativeFiles);
        File.Exists(Path.Combine(_paths.Home, ".workload-operations", "dirty.intent")).Should().BeFalse();
    }

    [Fact]
    public async Task Lifecycle_PruneBundledVersions_KeepsHighestAndItsExactNativeOwner()
    {
        var first = await _installer.InstallFromPackageAsync(MigrationArchive("1.1.0"));
        await _installer.InstallFromPackageAsync(MigrationArchive("1.0.0"));
        var command = new WorkloadPruneCommand(_interaction, _installer, _workloads, new FileWorkloadOperationCoordinator(_paths));
        RootCommand root = [command];

        int exit = await root.Parse(["prune", first.Entry.PackageId, "--exact"]).InvokeAsync(new() { EnableDefaultExceptionHandler = false });

        exit.Should().Be(0, _interaction.AllOutput);
        (await _workloads.GetWorkloadsAsync()).Should().ContainSingle().Which.PackageVersion.Should().Be("1.1.0");
        await AssertMigrationNativeVersionsAsync("1.1.0");
        Directory.Exists(_paths.GetInstallDirectory(first.Entry.PackageId, "1.0.0")).Should().BeFalse();
        _interaction.AllOutput.Should().Contain("Pruned workload").And.Contain("1.0.0");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Lifecycle_UninstallRegistryFailureOrCancellation_RestoresNativeAndRegistryBeforeReleasingOperation(bool cancel)
    {
        var installed = await _installer.InstallFromPackageAsync(MigrationArchive("1.0.0"));
        string path = _paths.GetInstallDirectory(installed.Entry.PackageId, "1.0.0");
        var payload = MigrationFiles(path);
        var native = MigrationFiles(Path.Combine(Home, "templates"));
        string registry = File.ReadAllText(_paths.WorkloadRegistryPath);
        using var cancellation = new CancellationTokenSource();
        var fault = Substitute.For<IWorkloadStore>();
        fault.GetWorkloadsAsync(Arg.Any<CancellationToken>()).Returns(call => _workloads.GetWorkloadsAsync(call.Arg<CancellationToken>()));
        fault.RemoveOwnershipAsync(Arg.Any<IWorkloadOperation>(), Arg.Any<string>(), Arg.Any<string>(),
            Arg.Any<WorkloadOwnershipKind>(), Arg.Any<CancellationToken>()).Returns<Task<WorkloadOwnershipRemovalResult>>(async call =>
        {
            await _workloads.RemoveOwnershipAsync(call.Arg<IWorkloadOperation>(), call.ArgAt<string>(1), call.ArgAt<string>(2),
                call.Arg<WorkloadOwnershipKind>(), call.Arg<CancellationToken>());
            if (cancel)
            {
                cancellation.Cancel();
                cancellation.Token.ThrowIfCancellationRequested();
            }
            throw new IOException("Owned removal registry failure.");
        });
        var installer = MigrationInstaller(_inspector, store: fault);

        Func<Task> remove = () => installer.UninstallAsync(installed.Entry.PackageId, "1.0.0", cancellationToken: cancellation.Token);

        if (cancel) await remove.Should().ThrowAsync<OperationCanceledException>();
        else await remove.Should().ThrowAsync<IOException>().WithMessage("*Owned removal registry failure*");
        File.ReadAllText(_paths.WorkloadRegistryPath).Should().Be(registry);
        MigrationFiles(path).Should().BeEquivalentTo(payload);
        MigrationFiles(Path.Combine(Home, "templates")).Should().BeEquivalentTo(native);
        File.Exists(Path.Combine(_paths.Home, ".workload-operations", "dirty.intent")).Should().BeFalse();
        (await _installer.UninstallAsync(installed.Entry.PackageId, "1.0.0")).Should().BeTrue();
    }
}
