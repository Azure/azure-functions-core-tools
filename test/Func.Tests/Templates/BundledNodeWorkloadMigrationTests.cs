// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.CommandLine;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Xml.Linq;
using Azure.Functions.Cli.Commands.Workload;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Templates.Acquisition;
using Azure.Functions.Cli.Templates.Engine;
using Azure.Functions.Cli.Workloads;
using Azure.Functions.Cli.Workloads.Catalog;
using Azure.Functions.Cli.Workloads.Discovery;
using Azure.Functions.Cli.Workloads.Install;
using Azure.Functions.Cli.Workloads.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.TemplateEngine.Abstractions.Installer;
using Microsoft.TemplateEngine.Edge.Settings;
using NSubstitute;
using NuGet.Versioning;

namespace Azure.Functions.Cli.Tests.Templates;

public sealed partial class BundledNodeTemplateTests
{
    [Fact]
    public async Task ReadBundledOwner_VersionedAndLegacyReceipts_AreReadOnlyAndNeverSelectAnotherVersion()
    {
        await _installer.InstallFromPackageAsync(MigrationArchive("1.0.0"));
        IBundledTemplateInstaller reader = new BundledTemplateInstaller(_templates, _inspector, _files, new(Home));
        var versioned = await reader.ReadOwnerAsync("Azure.Functions.Templates.Node.Basic", "1.0.0", CancellationToken.None);
        versioned.Should().NotBeNull();
        versioned!.Version.Should().Be("1.0.0");
        string legacy = Path.Combine(Home, "templates", ".bundled-owners", "azure.functions.templates.node.basic.json");
        File.Move(BundledOwnerPath("1.0.0"), legacy);
        var native = MigrationFiles(Path.Combine(Home, "templates"));

        var old = await reader.ReadOwnerAsync("Azure.Functions.Templates.Node.Basic", "1.0.0", CancellationToken.None);
        var missing = await reader.ReadOwnerAsync("Azure.Functions.Templates.Node.Basic", "1.1.0", CancellationToken.None);

        old.Should().Be(versioned);
        missing.Should().BeNull();
        MigrationFiles(Path.Combine(Home, "templates")).Should().BeEquivalentTo(native);
        File.Exists(legacy).Should().BeTrue();
        File.Exists(BundledOwnerPath("1.0.0")).Should().BeFalse();
    }

    [Fact]
    public async Task ReadBundledOwner_InvalidInputsAndCancellation_DoNotReadOrWriteNativeState()
    {
        var files = Substitute.For<IFileSystem>();
        IBundledTemplateInstaller reader = new BundledTemplateInstaller(_templates, _inspector, files, new(Home));
        Func<Task>[] missing =
        [
            () => reader.ReadOwnerAsync(null!, "1.0.0", CancellationToken.None),
            () => reader.ReadOwnerAsync(" ", "1.0.0", CancellationToken.None),
            () => reader.ReadOwnerAsync("Valid.Id", null!, CancellationToken.None),
            () => reader.ReadOwnerAsync("Valid.Id", " ", CancellationToken.None),
        ];
        foreach (var call in missing)
        {
            await call.Should().ThrowAsync<ArgumentException>();
        }
        Func<Task>[] invalid =
        [
            () => reader.ReadOwnerAsync("../outside", "1.0.0", CancellationToken.None),
            () => reader.ReadOwnerAsync("..", "1.0.0", CancellationToken.None),
            () => reader.ReadOwnerAsync("Valid.Id", "not-a-version", CancellationToken.None),
        ];
        foreach (var call in invalid)
        {
            await call.Should().ThrowAsync<InvalidWorkloadException>();
        }
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Func<Task> cancelled = () => reader.ReadOwnerAsync("Valid.Id", "1.0.0", cancellation.Token);

        await cancelled.Should().ThrowAsync<OperationCanceledException>();
        files.ReceivedCalls().Should().BeEmpty();
    }

    [Fact]
    public void ReadBundledManifest_RealPackDeclaration_UsesTheSharedPureValidationContract()
    {
        using var archive = new NuGet.Packaging.PackageArchiveReader(BuiltArchive);
        string json = Read(archive, BundledTemplateInstaller.ManifestName);

        var manifest = BundledTemplateInstaller.ReadManifest(json);

        manifest.SchemaVersion.Should().Be(1);
        manifest.Assets.Should().ContainSingle().Which.PackageId.Should().Be("Azure.Functions.Templates.Node.Basic");
        manifest.Assets[0].Version.Should().Be(archive.NuspecReader.GetVersion().ToNormalizedString());
        Action missing = () => BundledTemplateInstaller.ReadManifest(null!);
        missing.Should().Throw<ArgumentNullException>();
    }

    [Theory]
    [InlineData("schema")]
    [InlineData("path")]
    [InlineData("id")]
    [InlineData("version")]
    [InlineData("digest")]
    [InlineData("inventory")]
    [InlineData("type")]
    [InlineData("identity")]
    [InlineData("package")]
    [InlineData("assetPath")]
    [InlineData("duplicateProperty")]
    [InlineData("malformed")]
    public void ReadBundledManifest_InvalidOrAmbiguousExternalDeclaration_IsRejectedWithoutPreparation(string corruption)
    {
        using var archive = new NuGet.Packaging.PackageArchiveReader(BuiltArchive);
        var declaration = JsonNode.Parse(Read(archive, BundledTemplateInstaller.ManifestName))!;
        var asset = declaration["assets"]![0]!;
        switch (corruption)
        {
            case "schema": declaration["schemaVersion"] = 2; break;
            case "path": asset["path"] = "tools/../outside.nupkg"; break;
            case "id": asset["packageId"] = "Invalid/Package"; break;
            case "version": asset["version"] = "not-a-version"; break;
            case "digest": asset["sha256"] = "invalid"; break;
            case "inventory": asset["templates"] = new JsonArray(); break;
            case "type": asset["templates"]![0]!["type"] = "solution"; break;
            case "identity":
            case "package":
            case "assetPath":
                var other = asset.DeepClone();
                other["path"] = corruption == "assetPath" ? asset["path"]!.GetValue<string>() : "tools/other.nupkg";
                other["packageId"] = corruption == "package"
                    ? asset["packageId"]!.GetValue<string>().ToLowerInvariant() : "Other.Native.Package";
                if (corruption != "identity")
                {
                    foreach (var template in other["templates"]!.AsArray())
                    {
                        var definition = template ?? throw new InvalidOperationException("The test inventory must contain template objects.");
                        definition["identity"] = definition["identity"]!.GetValue<string>() + ".Other";
                    }
                }
                declaration["assets"]!.AsArray().Add(other);
                break;
        }
        string json = declaration.ToJsonString();
        if (corruption == "duplicateProperty")
        {
            json = json.Replace("\"schemaVersion\":1", "\"schemaVersion\":1,\"schemaVersion\":1", StringComparison.Ordinal);
        }
        else if (corruption == "malformed")
        {
            json = "{";
        }
        Action parse = () => BundledTemplateInstaller.ReadManifest(json);

        parse.Should().Throw<InvalidWorkloadException>();
    }

    [Fact]
    public async Task SameHomeUpdate_LegacyThenBundled_PreservesProjectsUserTemplatesAndUnrelatedWorkloads()
    {
        var legacy = await _installer.InstallFromPackageAsync(MigrationArchive("0.1.0", bundled: false));
        string project = Path.Combine(_root, "existing-project");
        Directory.CreateDirectory(project);
        File.WriteAllText(Path.Combine(project, "host.json"), """{"version":"2.0","custom":true}""");
        File.WriteAllText(Path.Combine(project, "user-code.js"), "export const untouched = true;");
        var projectBefore = MigrationFiles(project);
        string userPackage = TemplateFixtures.WriteBasicPackage(_root);
        await MigrationInstallUserPackageAsync(userPackage);
        string unrelatedArchive = AdmissionArchive("unrelated-workload.nupkg", "Migration.Unrelated.Stack");
        using (var zip = ZipFile.Open(unrelatedArchive, ZipArchiveMode.Update))
        {
            zip.GetEntry(BundledTemplateInstaller.ManifestName)!.Delete();
            var metadata = zip.GetEntry("workload.json")!;
            JsonNode manifest;
            using (var input = new StreamReader(metadata.Open())) manifest = JsonNode.Parse(input.ReadToEnd())!;
            manifest["kind"] = "content";
            manifest.AsObject().Remove("entryPoint");
            metadata.Delete();
            using var output = new StreamWriter(zip.CreateEntry("workload.json").Open());
            output.Write(manifest.ToJsonString());
        }
        var unrelated = await _installer.InstallFromPackageAsync(unrelatedArchive);
        var unrelatedBefore = MigrationFiles(_paths.GetInstallDirectory(unrelated.Entry.PackageId, unrelated.Entry.PackageVersion));
        string first = MigrationArchive("1.1.0");
        string second = MigrationArchive("1.2.0");
        MigrationSource(first);

        var migrated = await _installer.UpdateAsync(legacy.Entry.PackageId, null, MigrationFeed, true, true);
        await AssertMigrationInstalledAsync(migrated.Entry, "1.1.0", userPackage, first);
        MigrationSource(second);
        var updated = await _installer.UpdateAsync(legacy.Entry.PackageId, null, MigrationFeed, true, false);

        updated.NoUpdateAvailable.Should().BeFalse();
        updated.PreviousVersion.Should().Be("1.1.0");
        await AssertMigrationInstalledAsync(updated.Entry, "1.2.0", userPackage, second);
        Directory.Exists(_paths.GetInstallDirectory(legacy.Entry.PackageId, "0.1.0")).Should().BeFalse();
        Directory.Exists(_paths.GetInstallDirectory(legacy.Entry.PackageId, "1.1.0")).Should().BeFalse();
        MigrationFiles(project).Should().BeEquivalentTo(projectBefore);
        MigrationFiles(_paths.GetInstallDirectory(unrelated.Entry.PackageId, unrelated.Entry.PackageVersion)).Should().BeEquivalentTo(unrelatedBefore);
        (await _workloads.GetWorkloadsAsync()).Should().HaveCount(2).And.ContainEquivalentOf(unrelated.Entry);
        using var services = await CreateServicesAsync();
        services.GetServices<WorkloadInfo>().OfType<RuntimeWorkloadInfo>().Should().Contain(workload =>
            workload.PackageId == updated.Entry.PackageId && workload.PackageVersion == updated.Entry.PackageVersion);
        await _installer.EnsureInstalledAsync(updated.Entry, CancellationToken.None, checkOnly: true);
        File.Exists(Path.Combine(Home, ".template-store", "recovery.ready")).Should().BeFalse();
        File.Exists(Path.Combine(_paths.Home, ".workload-operations", "dirty.intent")).Should().BeFalse();
    }

    [Fact]
    public async Task OrdinaryUpdateCommand_LegacyStack_UsesExistingHomeAndReportsOnlyCommittedSuccess()
    {
        var legacy = await _installer.InstallFromPackageAsync(MigrationArchive("0.1.0", bundled: false));
        MigrationSource(MigrationArchive("1.1.0"));
        var command = new WorkloadUpdateCommand(_interaction, _installer, _workloads, Options.Create(new WorkloadCatalogOptions()));
        RootCommand root = [command];

        int exit = await root.Parse(["update", legacy.Entry.PackageId, "--exact", "--source", MigrationFeed, "--major", "--prerelease"])
            .InvokeAsync(new() { EnableDefaultExceptionHandler = false });

        exit.Should().Be(0, _interaction.AllOutput);
        _interaction.AllOutput.Should().Contain("1.1.0").And.NotContain("not supported");
        (await _workloads.GetWorkloadsAsync()).Should().ContainSingle().Which.PackageVersion.Should().Be("1.1.0");
        using var native = await _templates.OpenReadAsync(new(WorkingDirectory.FromExplicit(_root)), CancellationToken.None);
        (await native.PackageManager.GetManagedTemplatePackagesAsync(false, CancellationToken.None)).Should().ContainSingle().Which
            .Version.Should().Be("1.1.0");
    }

    [Fact]
    public async Task LogicalSameHomeUpdate_LegacyThenBundled_MovesOnlySelectedOwnershipAndRecordsPointerProvenance()
    {
        string pointer = MigrationPointerArchive("1.0.0");
        string implementation = MigrationRidArchive("1.0.0", bundled: false);
        _source.FindLocalImplementation(pointer, "migration.stack.win-x64", "1.0.0", "win-x64").Returns(implementation);
        var original = await _installer.InstallFromPackageAsync(pointer);
        original.Entry.IsImplicitlyInstalled.Should().BeTrue();

        string[] versions = ["1.1.0", "1.2.0"];
        foreach (string version in versions)
        {
            MigrationSource(MigrationPointerArchive(version));
            var resolved = MigrationSource(MigrationRidArchive(version, bundled: true));
            _source.ResolveImplementationAsync(Arg.Any<InspectedWorkloadPackage>(), Arg.Any<WorkloadPointerSelection>(), MigrationFeed,
                Arg.Any<CancellationToken>()).Returns(resolved);

            var updated = await _installer.UpdateAsync("migration.stack", null, MigrationFeed, true, false, WorkloadOwnershipKind.Logical);

            updated.Entry.IsImplicitlyInstalled.Should().BeTrue();
            updated.Entry.InstallRefCount.Should().Be(1);
            updated.Entry.LogicalPackage!.PackageVersion.Should().Be(version);
            updated.Entry.LogicalPackage.Source.Should().Be(MigrationFeed);
            (await _workloads.GetWorkloadsAsync()).Should().ContainSingle();
            await _installer.EnsureInstalledAsync(updated.Entry, CancellationToken.None, checkOnly: true);
            string receipt = BundledOwnerPath(version);
            var owner = JsonSerializer.Deserialize(File.ReadAllText(receipt), BundledTemplateJsonContext.Default.BundledTemplateOwner)!;
            owner.Origin.LogicalPackageId.Should().Be("migration.stack");
            owner.Origin.LogicalVersion.Should().Be(version);
            owner.Origin.LogicalSource.Should().Be(MigrationFeed);
        }
        Directory.Exists(_paths.GetInstallDirectory(original.Entry.PackageId, "1.0.0")).Should().BeFalse();
        Directory.Exists(_paths.GetInstallDirectory(original.Entry.PackageId, "1.1.0")).Should().BeFalse();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SameHomeUpdate_UserPackageOrIdentityCollision_IsRejectedBeforeWorkloadAdmission(bool differentPackageId)
    {
        var legacy = await _installer.InstallFromPackageAsync(MigrationArchive("0.1.0", bundled: false));
        string incoming = MigrationArchive("1.1.0");
        string user = MigrationInnerArchive(incoming);
        if (differentPackageId)
        {
            using var zip = ZipFile.Open(user, ZipArchiveMode.Update);
            MigrationChangeVersion(zip, "1.1.0", "Migration.User.Templates");
        }
        await MigrationInstallUserPackageAsync(user);
        string registry = File.ReadAllText(_paths.WorkloadRegistryPath);
        var nativeBefore = MigrationFiles(Path.Combine(Home, "templates"));
        MigrationSource(incoming);
        Func<Task> update = () => _installer.UpdateAsync(legacy.Entry.PackageId, null, MigrationFeed, true, true);

        await update.Should().ThrowAsync<InvalidWorkloadException>()
            .WithMessage(differentPackageId ? "*identity collision*User-installed*" : "*unknown or conflicting native ownership*");

        File.ReadAllText(_paths.WorkloadRegistryPath).Should().Be(registry);
        MigrationFiles(Path.Combine(Home, "templates")).Should().BeEquivalentTo(nativeBefore);
        Directory.Exists(_paths.GetInstallDirectory(legacy.Entry.PackageId, "1.1.0")).Should().BeFalse();
        File.Exists(Path.Combine(_paths.Home, ".workload-operations", "dirty.intent")).Should().BeFalse();
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task SameHomeUpdate_PostAdmissionFailureOrCancellation_RestoresBothStoresAndAllowsRetry(bool oldBundled, bool cancel)
    {
        var original = await _installer.InstallFromPackageAsync(MigrationArchive("1.0.0", oldBundled));
        string user = TemplateFixtures.WriteBasicPackage(_root);
        await MigrationInstallUserPackageAsync(user);
        string incoming = MigrationArchive("1.1.0");
        MigrationSource(incoming);
        var before = MigrationFiles(_paths.GetInstallDirectory(original.Entry.PackageId, original.Entry.PackageVersion));
        string registry = File.ReadAllText(_paths.WorkloadRegistryPath);
        var nativeBefore = MigrationFiles(Path.Combine(Home, "templates"));
        using var cancellation = new CancellationTokenSource();
        var fault = new MigrationInspector(_files, Home, "1.1.0", cancel ? cancellation : null);
        var installer = MigrationInstaller(fault);
        Func<Task> update = () => installer.UpdateAsync(original.Entry.PackageId, null, MigrationFeed, true, false,
            cancellationToken: cancellation.Token);

        if (cancel)
        {
            var exception = (await update.Should().ThrowAsync<OperationCanceledException>()).Which;
            exception.Should().NotBeOfType<BundledTemplateRegistrationCanceledException>();
            exception.CancellationToken.Should().Be(cancellation.Token);
        }
        else
        {
            await update.Should().ThrowAsync<InvalidWorkloadException>().WithMessage("*migration validation failure*");
        }

        fault.Reached.Should().BeTrue();
        File.ReadAllText(_paths.WorkloadRegistryPath).Should().Be(registry);
        MigrationFiles(_paths.GetInstallDirectory(original.Entry.PackageId, original.Entry.PackageVersion)).Should().BeEquivalentTo(before);
        MigrationFiles(Path.Combine(Home, "templates")).Should().BeEquivalentTo(nativeBefore);
        Directory.Exists(_paths.GetInstallDirectory(original.Entry.PackageId, "1.1.0")).Should().BeFalse();
        File.Exists(Path.Combine(_paths.Home, ".workload-operations", "dirty.intent")).Should().BeFalse();
        File.Exists(Path.Combine(Home, ".template-store", "recovery.ready")).Should().BeFalse();
        var retried = await _installer.UpdateAsync(original.Entry.PackageId, null, MigrationFeed, true, false);
        await AssertMigrationInstalledAsync(retried.Entry, "1.1.0", user, incoming);
    }

    [Fact]
    public async Task SameHomeUpdate_RegistryWriteFailsAfterCommit_RestoresIncumbentAndNativeStore()
    {
        var original = await _installer.InstallFromPackageAsync(MigrationArchive("1.0.0"));
        var store = AdmissionStore();
        bool registryCommitted = false;
        store.MoveExplicitOwnershipAsync(Arg.Any<IWorkloadOperation>(), Arg.Any<string>(), Arg.Any<string>(),
            Arg.Any<WorkloadEntry>(), Arg.Any<CancellationToken>()).Returns(async Task<WorkloadOwnershipMoveResult> (call) =>
        {
            await _workloads.MoveExplicitOwnershipAsync(call.Arg<IWorkloadOperation>(), call.ArgAt<string>(1), call.ArgAt<string>(2),
                call.Arg<WorkloadEntry>(), call.Arg<CancellationToken>());
            registryCommitted = true;
            throw new IOException("Owned migration registry failure.");
        });
        var installer = MigrationInstaller(_inspector, store: store);
        MigrationSource(MigrationArchive("1.1.0"));
        string registry = File.ReadAllText(_paths.WorkloadRegistryPath);
        var native = MigrationFiles(Path.Combine(Home, "templates"));
        Func<Task> update = () => installer.UpdateAsync(original.Entry.PackageId, null, MigrationFeed, true, false);

        await update.Should().ThrowAsync<IOException>().WithMessage("Owned migration registry failure.");

        registryCommitted.Should().BeTrue();
        File.ReadAllText(_paths.WorkloadRegistryPath).Should().Be(registry);
        MigrationFiles(Path.Combine(Home, "templates")).Should().BeEquivalentTo(native);
        Directory.Exists(_paths.GetInstallDirectory(original.Entry.PackageId, original.Entry.PackageVersion)).Should().BeTrue();
        Directory.Exists(_paths.GetInstallDirectory(original.Entry.PackageId, "1.1.0")).Should().BeFalse();
        File.Exists(Path.Combine(_paths.Home, ".workload-operations", "dirty.intent")).Should().BeFalse();
        (await _installer.UpdateAsync(original.Entry.PackageId, null, MigrationFeed, true, false)).Entry.PackageVersion.Should().Be("1.1.0");
    }

    [Fact]
    public async Task SameHomeUpdate_OwnerReceiptWriteFails_RestoresPreviousOwnerAndPayload()
    {
        var original = await _installer.InstallFromPackageAsync(MigrationArchive("1.0.0"));
        var files = Substitute.For<IFileSystem>();
        files.ReadAllTextIfExistsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(call => _files.ReadAllTextIfExistsAsync(call.Arg<string>(), call.Arg<CancellationToken>()));
        files.CreateTempDirectory().Returns(_ => _files.CreateTempDirectory());
        files.FileExists(Arg.Any<string>()).Returns(call => _files.FileExists(call.Arg<string>()));
        files.ComputeSha256Async(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(call => _files.ComputeSha256Async(call.Arg<string>(), call.Arg<CancellationToken>()));
        files.When(mock => mock.CopyFile(Arg.Any<string>(), Arg.Any<string>()))
            .Do(call => _files.CopyFile(call.ArgAt<string>(0), call.ArgAt<string>(1)));
        bool receiptWriteReached = false;
        files.WriteAllTextAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                receiptWriteReached = true;
                return Task.FromException(new IOException("Owned migration receipt failure."));
            });
        files.When(mock => mock.DeleteFile(Arg.Any<string>())).Do(call => _files.DeleteFile(call.Arg<string>()));
        var installer = MigrationInstaller(_inspector, bundleFiles: files);
        MigrationSource(MigrationArchive("1.1.0"));
        string registry = File.ReadAllText(_paths.WorkloadRegistryPath);
        var native = MigrationFiles(Path.Combine(Home, "templates"));
        Func<Task> update = () => installer.UpdateAsync(original.Entry.PackageId, null, MigrationFeed, true, false);

        await update.Should().ThrowAsync<IOException>().WithMessage("Owned migration receipt failure.");

        receiptWriteReached.Should().BeTrue();
        File.ReadAllText(_paths.WorkloadRegistryPath).Should().Be(registry);
        MigrationFiles(Path.Combine(Home, "templates")).Should().BeEquivalentTo(native);
        Directory.Exists(_paths.GetInstallDirectory(original.Entry.PackageId, original.Entry.PackageVersion)).Should().BeTrue();
        Directory.Exists(_paths.GetInstallDirectory(original.Entry.PackageId, "1.1.0")).Should().BeFalse();
        File.Exists(Path.Combine(_paths.Home, ".workload-operations", "dirty.intent")).Should().BeFalse();
    }

    [Fact]
    public async Task SameHomeUpdate_PreparedIncumbentChanges_RefusesWithoutNativeOrRegistryMutation()
    {
        var original = await _installer.InstallFromPackageAsync(MigrationArchive("1.0.0"));
        string incoming = MigrationArchive("1.1.0");
        var coordinator = new FileWorkloadOperationCoordinator(_paths);
        var deployment = AdmissionDeployment(coordinator);
        var package = await AdmissionPackageInspector().InspectAsync(incoming);
        using var prepared = await deployment.PrepareAsync(package, MigrationFeed, WorkloadOwnershipKind.Explicit, null, true,
            null, CancellationToken.None, original.Entry);
        string oldRoot = _paths.GetInstallDirectory(original.Entry.PackageId, original.Entry.PackageVersion);
        File.WriteAllText(Path.Combine(oldRoot, "changed-after-preparation.txt"), "preserve this change");
        string registry = File.ReadAllText(_paths.WorkloadRegistryPath);
        var native = MigrationFiles(Path.Combine(Home, "templates"));
        using var operation = await coordinator.AcquireAsync(CancellationToken.None);
        Func<Task> update = () => deployment.UpdatePreparedAsync(operation, original.Entry, prepared, CancellationToken.None);

        await update.Should().ThrowAsync<WorkloadPlanChangedException>();

        File.ReadAllText(_paths.WorkloadRegistryPath).Should().Be(registry);
        MigrationFiles(Path.Combine(Home, "templates")).Should().BeEquivalentTo(native);
        File.ReadAllText(Path.Combine(oldRoot, "changed-after-preparation.txt")).Should().Be("preserve this change");
        operation.IsDirty.Should().BeFalse();
    }

    [Fact]
    public async Task SameHomeUpdate_NativeOwnerChangesAfterPreparation_RefusesBeforeWorkloadAdmission()
    {
        var original = await _installer.InstallFromPackageAsync(MigrationArchive("1.0.0"));
        string incoming = MigrationArchive("1.1.0");
        var coordinator = new FileWorkloadOperationCoordinator(_paths);
        var deployment = AdmissionDeployment(coordinator);
        var package = await AdmissionPackageInspector().InspectAsync(incoming);
        using var prepared = await deployment.PrepareAsync(package, MigrationFeed, WorkloadOwnershipKind.Explicit, null, true,
            null, CancellationToken.None, original.Entry);
        string ownerPath = BundledOwnerPath("1.0.0");
        var owner = JsonNode.Parse(File.ReadAllText(ownerPath))!;
        owner["origin"]!["source"] = "https://different-owner.invalid/index.json";
        File.WriteAllText(ownerPath, owner.ToJsonString());
        string registry = File.ReadAllText(_paths.WorkloadRegistryPath);
        using var operation = await coordinator.AcquireAsync(CancellationToken.None);
        Func<Task> update = () => deployment.UpdatePreparedAsync(operation, original.Entry, prepared, CancellationToken.None);

        await update.Should().ThrowAsync<InvalidWorkloadException>().WithMessage("*unknown or conflicting native ownership*");

        File.ReadAllText(_paths.WorkloadRegistryPath).Should().Be(registry);
        JsonNode.Parse(File.ReadAllText(ownerPath))!["origin"]!["source"]!.GetValue<string>().Should()
            .Be("https://different-owner.invalid/index.json");
        Directory.Exists(_paths.GetInstallDirectory(original.Entry.PackageId, "1.1.0")).Should().BeFalse();
        operation.IsDirty.Should().BeFalse();
    }

    [Fact]
    public async Task SameHomeUpdate_UndeclaredReplacementStillRefused_PreservesInstalledBundle()
    {
        var original = await _installer.InstallFromPackageAsync(MigrationArchive("1.0.0"));
        MigrationSource(MigrationArchive("1.1.0", bundled: false));
        string registry = File.ReadAllText(_paths.WorkloadRegistryPath);
        Func<Task> update = () => _installer.UpdateAsync(original.Entry.PackageId, null, MigrationFeed, true, false);

        await update.Should().ThrowAsync<InvalidWorkloadException>().WithMessage("*replacement is not supported*");

        File.ReadAllText(_paths.WorkloadRegistryPath).Should().Be(registry);
        await _installer.EnsureInstalledAsync(original.Entry, CancellationToken.None, checkOnly: true);
    }

    [Fact]
    public async Task SameHomeUpdate_CompetingNativeWriterWaitsUntilWorkloadRollbackHasSettled()
    {
        var original = await _installer.InstallFromPackageAsync(MigrationArchive("1.0.0"));
        MigrationSource(MigrationArchive("1.1.0"));
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var fault = new MigrationInspector(_files, Home, "1.1.0", null, entered, release.Task);
        Task<WorkloadUpdateResult> update = MigrationInstaller(fault).UpdateAsync(original.Entry.PackageId, null, MigrationFeed, true, false);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(30));
        var waiting = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task<TemplateStoreWriteSession> competing = _templates.OpenWriteAsync(new(WorkingDirectory.FromExplicit(_root)),
            CancellationToken.None, _ => waiting.TrySetResult());
        try
        {
            await waiting.Task.WaitAsync(TimeSpan.FromSeconds(30));
            competing.IsCompleted.Should().BeFalse();
            (await _workloads.GetWorkloadsAsync()).Should().ContainSingle().Which.PackageVersion.Should().Be("1.1.0");
            File.Exists(Path.Combine(_paths.Home, ".workload-operations", "dirty.intent")).Should().BeTrue();
        }
        finally
        {
            release.TrySetResult();
            Func<Task> finish = async () => await update.WaitAsync(TimeSpan.FromSeconds(30));
            await finish.Should().ThrowAsync<InvalidWorkloadException>().WithMessage("*migration validation failure*");
            using var nextWriter = await competing.WaitAsync(TimeSpan.FromSeconds(30));
            (await _workloads.GetWorkloadsAsync()).Should().ContainSingle().Which.PackageVersion.Should().Be("1.0.0");
            File.Exists(Path.Combine(_paths.Home, ".workload-operations", "dirty.intent")).Should().BeFalse();
            File.Exists(Path.Combine(Home, ".template-store", "recovery.ready")).Should().BeFalse();
        }
    }

    [Fact]
    public async Task SameHomeUpdate_UnmanagedDestinationCollision_PreservesBothDirectories()
    {
        var original = await _installer.InstallFromPackageAsync(MigrationArchive("1.0.0"));
        MigrationSource(MigrationArchive("1.1.0"));
        string destination = _paths.GetInstallDirectory(original.Entry.PackageId, "1.1.0");
        Directory.CreateDirectory(destination);
        File.WriteAllText(Path.Combine(destination, "user-owned.txt"), "do not replace");
        string registry = File.ReadAllText(_paths.WorkloadRegistryPath);
        Func<Task> update = () => _installer.UpdateAsync(original.Entry.PackageId, null, MigrationFeed, true, false);

        await update.Should().ThrowAsync<InvalidWorkloadException>().WithMessage("*destination already exists*");

        File.ReadAllText(_paths.WorkloadRegistryPath).Should().Be(registry);
        File.ReadAllText(Path.Combine(destination, "user-owned.txt")).Should().Be("do not replace");
        await _installer.EnsureInstalledAsync(original.Entry, CancellationToken.None, checkOnly: true);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SameHomeUpdate_SharedLegacyOwnership_MovesOnlySelectedOwnerAndPreservesRetainedPayload(bool logical)
    {
        var (current, sibling) = await MigrationSharedLegacyAsync(logical);
        string oldPath = _paths.GetInstallDirectory(current.PackageId, current.PackageVersion);
        string siblingPath = _paths.GetInstallDirectory(sibling.PackageId, sibling.PackageVersion);
        var oldPayload = MigrationFiles(oldPath);
        var siblingPayload = MigrationFiles(siblingPath);
        string userPackage = TemplateFixtures.WriteBasicPackage(_root);
        await MigrationInstallUserPackageAsync(userPackage);
        string incoming = MigrationSharedSource(logical);

        var updated = await MigrationUpdateSelectedAsync(_installer, current, logical, CancellationToken.None);

        var installed = await _workloads.GetWorkloadsAsync();
        installed.Should().HaveCount(3).And.ContainEquivalentOf(sibling);
        var retained = installed.Single(entry => entry.PackageVersion == current.PackageVersion);
        retained.InstallRefCount.Should().Be(1);
        retained.IsImplicitlyInstalled.Should().Be(!logical);
        retained.LogicalPackage.Should().BeEquivalentTo(logical ? null : current.LogicalPackage);
        retained.Source.Should().Be(current.Source);
        updated.Entry.IsImplicitlyInstalled.Should().Be(logical);
        (updated.Entry.LogicalPackage?.PackageVersion).Should().Be(logical ? "1.1.0" : null);
        MigrationFiles(oldPath).Should().BeEquivalentTo(oldPayload);
        MigrationFiles(siblingPath).Should().BeEquivalentTo(siblingPayload);
        await AssertMigrationInstalledAsync(updated.Entry, "1.1.0", userPackage, incoming);
        File.Exists(Path.Combine(_paths.Home, ".workload-operations", "dirty.intent")).Should().BeFalse();
    }

    [Fact]
    public async Task SameHomeUpdate_LegacySiblingVersion_IsPreservedWhileOnlyTheSelectedVersionMoves()
    {
        var older = await _installer.InstallFromPackageAsync(MigrationArchive("0.9.0", bundled: false));
        var current = await _installer.InstallFromPackageAsync(MigrationArchive("1.0.0", bundled: false));
        MigrationSource(MigrationArchive("1.1.0"));
        string siblingPath = _paths.GetInstallDirectory(older.Entry.PackageId, older.Entry.PackageVersion);
        var sibling = MigrationFiles(siblingPath);

        var updated = await _installer.UpdateAsync(current.Entry.PackageId, null, MigrationFeed, true, false);

        updated.Entry.PackageVersion.Should().Be("1.1.0");
        (await _workloads.GetWorkloadsAsync()).Should().HaveCount(2).And.ContainEquivalentOf(older.Entry);
        MigrationFiles(siblingPath).Should().BeEquivalentTo(sibling);
        Directory.Exists(_paths.GetInstallDirectory(current.Entry.PackageId, current.Entry.PackageVersion)).Should().BeFalse();
        await _installer.EnsureInstalledAsync(updated.Entry, CancellationToken.None, checkOnly: true);
        File.Exists(Path.Combine(_paths.Home, ".workload-operations", "dirty.intent")).Should().BeFalse();
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task SameHomeUpdate_SharedLegacyFailureOrCancellation_RestoresEveryOwnerSiblingAndUserTemplate(bool logical, bool cancel)
    {
        var (current, sibling) = await MigrationSharedLegacyAsync(logical);
        string oldPath = _paths.GetInstallDirectory(current.PackageId, current.PackageVersion);
        string siblingPath = _paths.GetInstallDirectory(sibling.PackageId, sibling.PackageVersion);
        var oldPayload = MigrationFiles(oldPath);
        var siblingPayload = MigrationFiles(siblingPath);
        await MigrationInstallUserPackageAsync(TemplateFixtures.WriteBasicPackage(_root));
        var native = MigrationFiles(Path.Combine(Home, "templates"));
        string registry = File.ReadAllText(_paths.WorkloadRegistryPath);
        MigrationSharedSource(logical);
        using var cancellation = new CancellationTokenSource();
        var fault = new MigrationInspector(_files, Home, "1.1.0", cancel ? cancellation : null);
        Func<Task> update = () => MigrationUpdateSelectedAsync(MigrationInstaller(fault), current, logical, cancellation.Token);

        if (cancel)
        {
            await update.Should().ThrowAsync<OperationCanceledException>();
        }
        else
        {
            await update.Should().ThrowAsync<InvalidWorkloadException>().WithMessage("*migration validation failure*");
        }

        fault.Reached.Should().BeTrue();
        File.ReadAllText(_paths.WorkloadRegistryPath).Should().Be(registry);
        MigrationFiles(oldPath).Should().BeEquivalentTo(oldPayload);
        MigrationFiles(siblingPath).Should().BeEquivalentTo(siblingPayload);
        MigrationFiles(Path.Combine(Home, "templates")).Should().BeEquivalentTo(native);
        Directory.Exists(_paths.GetInstallDirectory(current.PackageId, "1.1.0")).Should().BeFalse();
        File.Exists(Path.Combine(_paths.Home, ".workload-operations", "dirty.intent")).Should().BeFalse();
        File.Exists(Path.Combine(Home, ".template-store", "recovery.ready")).Should().BeFalse();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SameHomeUpdate_SharedBundledOwnership_PreservesBothExactNativeVersionsAndMovesOnlySelectedOwner(bool logical)
    {
        var shared = await MigrationSharedBundledAsync();
        string userPackage = TemplateFixtures.WriteBasicPackage(_root);
        await MigrationInstallUserPackageAsync(userPackage);
        var payload = MigrationFiles(_paths.GetInstallDirectory(shared.PackageId, shared.PackageVersion));
        string receipt = File.ReadAllText(BundledOwnerPath("1.0.0"));
        string incoming = MigrationRidSource(logical);

        var updated = await MigrationUpdateSelectedAsync(_installer, shared, logical, CancellationToken.None);

        var installed = await _workloads.GetWorkloadsAsync();
        installed.Should().HaveCount(2);
        var retained = installed.Single(entry => entry.PackageVersion == "1.0.0");
        retained.InstallRefCount.Should().Be(1);
        retained.IsImplicitlyInstalled.Should().Be(!logical);
        retained.LogicalPackage.Should().BeEquivalentTo(logical ? null : shared.LogicalPackage);
        updated.Entry.InstallRefCount.Should().Be(1);
        updated.Entry.IsImplicitlyInstalled.Should().Be(logical);
        MigrationFiles(_paths.GetInstallDirectory(shared.PackageId, shared.PackageVersion)).Should().BeEquivalentTo(payload);
        File.ReadAllText(BundledOwnerPath("1.0.0")).Should().Be(receipt);
        await AssertMigrationNativeVersionsAsync("1.0.0", "1.1.0");
        var owner = JsonSerializer.Deserialize(File.ReadAllText(BundledOwnerPath("1.1.0")),
            BundledTemplateJsonContext.Default.BundledTemplateOwner)!;
        owner.Origin.Sha256.Should().Be(await _files.ComputeSha256Async(incoming, CancellationToken.None));
        await _installer.EnsureInstalledAsync(retained, CancellationToken.None, checkOnly: true);
        await _installer.EnsureInstalledAsync(updated.Entry, CancellationToken.None, checkOnly: true);
        using var read = await _templates.OpenReadAsync(new(WorkingDirectory.FromExplicit(_root)), CancellationToken.None);
        (await read.PackageManager.GetManagedTemplatePackagesAsync(false, CancellationToken.None)).Should().HaveCount(3)
            .And.Contain(package => package.Identifier == userPackage);
        File.Exists(Path.Combine(_paths.Home, ".workload-operations", "dirty.intent")).Should().BeFalse();
    }

    [Fact]
    public async Task NativeProvider_SameInnerPackageId_RetainsBothVersionedRegistrations()
    {
        string first = MigrationInnerArchive(MigrationArchive("1.0.0"));
        string second = MigrationInnerArchive(MigrationArchive("1.1.0"));
        await MigrationInstallUserPackageAsync(first);
        using var write = await _templates.OpenWriteAsync(new(WorkingDirectory.FromExplicit(_root)), CancellationToken.None);
        bool? accepted = null;

        await write.ExecuteAsync(async (session, token) =>
        {
            var provider = session.PackageManager.GetBuiltInManagedProvider(InstallationScope.Global);
            var result = (await provider.InstallAsync([new InstallRequest(second)], token)).Should().ContainSingle().Subject;
            accepted = result.Success;
        }, async (session, token) =>
        {
            var packages = await session.PackageManager.GetManagedTemplatePackagesAsync(false, token);
            var native = packages.Where(package => package.Identifier == "Azure.Functions.Templates.Node.Basic").ToArray();
            accepted.Should().BeTrue();
            native.Select(package => package.Version).Should().BeEquivalentTo("1.0.0", "1.1.0");
        }, CancellationToken.None);
    }

    [Fact]
    public async Task SameHomeUpdate_BundledSiblingVersions_CoexistAndOnlyTheSupersededExactNativeVersionIsRemoved()
    {
        var older = await _installer.InstallFromPackageAsync(MigrationArchive("0.9.0", bundled: false));
        var current = await _installer.InstallFromPackageAsync(MigrationArchive("1.0.0", bundled: false));
        MigrationSource(MigrationArchive("1.1.0"));
        await _installer.UpdateAsync(current.Entry.PackageId, NuGetVersion.Parse("1.0.0"), MigrationFeed, true, false);
        MigrationSource(MigrationArchive("1.2.0"));
        var sibling = await _installer.UpdateAsync(older.Entry.PackageId, NuGetVersion.Parse("0.9.0"), MigrationFeed, true, true);
        await AssertMigrationNativeVersionsAsync("1.1.0", "1.2.0");
        string siblingPath = _paths.GetInstallDirectory(sibling.Entry.PackageId, sibling.Entry.PackageVersion);
        var siblingPayload = MigrationFiles(siblingPath);
        string receipt = File.ReadAllText(BundledOwnerPath("1.2.0"));
        MigrationSource(MigrationArchive("1.3.0"));

        var updated = await _installer.UpdateAsync(current.Entry.PackageId, NuGetVersion.Parse("1.1.0"), MigrationFeed, true, false);

        await AssertMigrationNativeVersionsAsync("1.2.0", "1.3.0");
        File.Exists(BundledOwnerPath("1.1.0")).Should().BeFalse();
        File.ReadAllText(BundledOwnerPath("1.2.0")).Should().Be(receipt);
        MigrationFiles(siblingPath).Should().BeEquivalentTo(siblingPayload);
        (await _workloads.GetWorkloadsAsync()).Should().HaveCount(2).And.ContainEquivalentOf(sibling.Entry);
        Directory.Exists(_paths.GetInstallDirectory(current.Entry.PackageId, "1.1.0")).Should().BeFalse();
        await _installer.EnsureInstalledAsync(sibling.Entry, CancellationToken.None, checkOnly: true);
        await _installer.EnsureInstalledAsync(updated.Entry, CancellationToken.None, checkOnly: true);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task SameHomeUpdate_SharedBundledFailureOrCancellation_RestoresEveryOwnerAndExactNativeVersion(bool logical, bool cancel)
    {
        var shared = await MigrationSharedBundledAsync();
        await MigrationInstallUserPackageAsync(TemplateFixtures.WriteBasicPackage(_root));
        var native = MigrationFiles(Path.Combine(Home, "templates"));
        var payload = MigrationFiles(_paths.GetInstallDirectory(shared.PackageId, shared.PackageVersion));
        string registry = File.ReadAllText(_paths.WorkloadRegistryPath);
        MigrationRidSource(logical);
        using var cancellation = new CancellationTokenSource();
        var fault = new MigrationInspector(_files, Home, "1.1.0", cancel ? cancellation : null);
        Func<Task> update = () => MigrationUpdateSelectedAsync(MigrationInstaller(fault), shared, logical, cancellation.Token);

        if (cancel)
        {
            await update.Should().ThrowAsync<OperationCanceledException>();
        }
        else
        {
            await update.Should().ThrowAsync<InvalidWorkloadException>().WithMessage("*migration validation failure*");
        }

        fault.Reached.Should().BeTrue();
        File.ReadAllText(_paths.WorkloadRegistryPath).Should().Be(registry);
        MigrationFiles(Path.Combine(Home, "templates")).Should().BeEquivalentTo(native);
        MigrationFiles(_paths.GetInstallDirectory(shared.PackageId, shared.PackageVersion)).Should().BeEquivalentTo(payload);
        await _installer.EnsureInstalledAsync(shared, CancellationToken.None, checkOnly: true);
        File.Exists(BundledOwnerPath("1.1.0")).Should().BeFalse();
        Directory.Exists(_paths.GetInstallDirectory(shared.PackageId, "1.1.0")).Should().BeFalse();
        File.Exists(Path.Combine(_paths.Home, ".workload-operations", "dirty.intent")).Should().BeFalse();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SameHomeUpdate_BundledSiblingFailureOrCancellation_RestoresSupersededAndUnrelatedVersions(bool cancel)
    {
        var older = await _installer.InstallFromPackageAsync(MigrationArchive("0.9.0", bundled: false));
        var current = await _installer.InstallFromPackageAsync(MigrationArchive("1.0.0", bundled: false));
        MigrationSource(MigrationArchive("1.1.0"));
        var selected = await _installer.UpdateAsync(current.Entry.PackageId, NuGetVersion.Parse("1.0.0"), MigrationFeed, true, false);
        MigrationSource(MigrationArchive("1.2.0"));
        var sibling = await _installer.UpdateAsync(older.Entry.PackageId, NuGetVersion.Parse("0.9.0"), MigrationFeed, true, true);
        string registry = File.ReadAllText(_paths.WorkloadRegistryPath);
        var native = MigrationFiles(Path.Combine(Home, "templates"));
        string selectedPath = _paths.GetInstallDirectory(selected.Entry.PackageId, selected.Entry.PackageVersion);
        string siblingPath = _paths.GetInstallDirectory(sibling.Entry.PackageId, sibling.Entry.PackageVersion);
        var selectedPayload = MigrationFiles(selectedPath);
        var siblingPayload = MigrationFiles(siblingPath);
        MigrationSource(MigrationArchive("1.3.0"));
        using var cancellation = new CancellationTokenSource();
        var fault = new MigrationInspector(_files, Home, "1.3.0", cancel ? cancellation : null);
        Func<Task> update = () => MigrationInstaller(fault).UpdateAsync(selected.Entry.PackageId, NuGetVersion.Parse("1.1.0"),
            MigrationFeed, true, false, cancellationToken: cancellation.Token);

        if (cancel)
        {
            await update.Should().ThrowAsync<OperationCanceledException>();
        }
        else
        {
            await update.Should().ThrowAsync<InvalidWorkloadException>().WithMessage("*migration validation failure*");
        }

        fault.Reached.Should().BeTrue();
        File.ReadAllText(_paths.WorkloadRegistryPath).Should().Be(registry);
        MigrationFiles(Path.Combine(Home, "templates")).Should().BeEquivalentTo(native);
        MigrationFiles(selectedPath).Should().BeEquivalentTo(selectedPayload);
        MigrationFiles(siblingPath).Should().BeEquivalentTo(siblingPayload);
        await AssertMigrationNativeVersionsAsync("1.1.0", "1.2.0");
        await _installer.EnsureInstalledAsync(selected.Entry, CancellationToken.None, checkOnly: true);
        await _installer.EnsureInstalledAsync(sibling.Entry, CancellationToken.None, checkOnly: true);
        Directory.Exists(_paths.GetInstallDirectory(selected.Entry.PackageId, "1.3.0")).Should().BeFalse();
        File.Exists(Path.Combine(_paths.Home, ".workload-operations", "dirty.intent")).Should().BeFalse();
    }

    [Fact]
    public async Task SameHomeUpdate_LegacyOwnershipReceipt_MigratesOnlyUnderTheNativeWriteTransaction()
    {
        var shared = await MigrationSharedBundledAsync();
        string legacy = Path.Combine(Home, "templates", ".bundled-owners", "azure.functions.templates.node.basic.json");
        string receipt = File.ReadAllText(BundledOwnerPath("1.0.0"));
        File.Move(BundledOwnerPath("1.0.0"), legacy);
        var beforeCheck = MigrationFiles(Path.Combine(Home, "templates"));
        await _installer.EnsureInstalledAsync(shared, CancellationToken.None, checkOnly: true);
        MigrationFiles(Path.Combine(Home, "templates")).Should().BeEquivalentTo(beforeCheck);
        MigrationRidSource(logical: true);

        var updated = await MigrationUpdateSelectedAsync(_installer, shared, logical: true, CancellationToken.None);

        File.Exists(legacy).Should().BeFalse();
        File.ReadAllText(BundledOwnerPath("1.0.0")).Should().Be(receipt);
        File.Exists(BundledOwnerPath("1.1.0")).Should().BeTrue();
        await AssertMigrationNativeVersionsAsync("1.0.0", "1.1.0");
        var retained = (await _workloads.GetWorkloadsAsync()).Single(entry => entry.PackageVersion == "1.0.0");
        retained.LogicalPackage.Should().BeNull();
        await _installer.EnsureInstalledAsync(retained, CancellationToken.None, checkOnly: true);
        await _installer.EnsureInstalledAsync(updated.Entry, CancellationToken.None, checkOnly: true);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SameHomeUpdate_LegacyReceiptMigrationFailure_RestoresOriginalReceiptAndOwners(bool cancel)
    {
        var shared = await MigrationSharedBundledAsync();
        string legacy = Path.Combine(Home, "templates", ".bundled-owners", "azure.functions.templates.node.basic.json");
        File.Move(BundledOwnerPath("1.0.0"), legacy);
        var native = MigrationFiles(Path.Combine(Home, "templates"));
        string registry = File.ReadAllText(_paths.WorkloadRegistryPath);
        MigrationRidSource(logical: false);
        using var cancellation = new CancellationTokenSource();
        var fault = new MigrationInspector(_files, Home, "1.1.0", cancel ? cancellation : null);
        Func<Task> update = () => MigrationUpdateSelectedAsync(MigrationInstaller(fault), shared, logical: false, cancellation.Token);

        if (cancel)
        {
            await update.Should().ThrowAsync<OperationCanceledException>();
        }
        else
        {
            await update.Should().ThrowAsync<InvalidWorkloadException>().WithMessage("*migration validation failure*");
        }

        fault.Reached.Should().BeTrue();
        File.ReadAllText(_paths.WorkloadRegistryPath).Should().Be(registry);
        MigrationFiles(Path.Combine(Home, "templates")).Should().BeEquivalentTo(native);
        File.Exists(legacy).Should().BeTrue();
        File.Exists(BundledOwnerPath("1.0.0")).Should().BeFalse();
        File.Exists(BundledOwnerPath("1.1.0")).Should().BeFalse();
        await _installer.EnsureInstalledAsync(shared, CancellationToken.None, checkOnly: true);
    }

    [Fact]
    public async Task SameHomeUpdate_ConflictingLegacyAndQualifiedReceipt_CannotBeUsedAsOriginFallback()
    {
        var original = await _installer.InstallFromPackageAsync(MigrationArchive("1.0.0"));
        string legacy = Path.Combine(Home, "templates", ".bundled-owners", "azure.functions.templates.node.basic.json");
        var conflicting = JsonNode.Parse(File.ReadAllText(BundledOwnerPath("1.0.0")))!;
        conflicting["origin"]!["source"] = "https://conflicting-legacy-origin.invalid/index.json";
        File.WriteAllText(legacy, conflicting.ToJsonString());
        var native = MigrationFiles(Path.Combine(Home, "templates"));
        string registry = File.ReadAllText(_paths.WorkloadRegistryPath);
        MigrationSource(MigrationArchive("1.1.0"));
        Func<Task> update = () => _installer.UpdateAsync(original.Entry.PackageId, null, MigrationFeed, true, false);

        await update.Should().ThrowAsync<InvalidWorkloadException>().WithMessage("*conflicting receipts*");

        File.ReadAllText(_paths.WorkloadRegistryPath).Should().Be(registry);
        MigrationFiles(Path.Combine(Home, "templates")).Should().BeEquivalentTo(native);
        Directory.Exists(_paths.GetInstallDirectory(original.Entry.PackageId, "1.1.0")).Should().BeFalse();
        File.Exists(Path.Combine(_paths.Home, ".workload-operations", "dirty.intent")).Should().BeFalse();
    }

    private async Task<WorkloadEntry> MigrationSharedBundledAsync()
    {
        string pointer = MigrationPointerArchive("1.0.0");
        string implementation = MigrationRidArchive("1.0.0", bundled: true);
        _source.FindLocalImplementation(pointer, "migration.stack.win-x64", "1.0.0", "win-x64").Returns(implementation);
        var original = await _installer.InstallFromPackageAsync(pointer);
        return await _workloads.AddOwnershipAsync(MigrationOwnershipEntry(original.Entry, original.Entry.LogicalPackage),
            WorkloadOwnershipKind.Explicit);
    }

    private string MigrationRidSource(bool logical, string version = "1.1.0")
    {
        if (logical)
        {
            MigrationSource(MigrationPointerArchive(version));
        }
        string implementation = MigrationRidArchive(version, bundled: true);
        var resolved = MigrationSource(implementation);
        if (logical)
        {
            _source.ResolveImplementationAsync(Arg.Any<InspectedWorkloadPackage>(), Arg.Any<WorkloadPointerSelection>(), MigrationFeed,
                Arg.Any<CancellationToken>()).Returns(resolved);
        }
        return implementation;
    }

    private async Task AssertMigrationNativeVersionsAsync(params string[] versions)
    {
        using var read = await _templates.OpenReadAsync(new(WorkingDirectory.FromExplicit(_root)), CancellationToken.None);
        var packages = await read.PackageManager.GetManagedTemplatePackagesAsync(false, CancellationToken.None);
        var native = packages.Where(package => package.Identifier == "Azure.Functions.Templates.Node.Basic").ToArray();
        native.Select(package => package.Version).Should().BeEquivalentTo(versions);
        foreach (var package in native)
        {
            var owner = JsonSerializer.Deserialize(File.ReadAllText(BundledOwnerPath(package.Version)),
                BundledTemplateJsonContext.Default.BundledTemplateOwner)!;
            owner.Version.Should().Be(package.Version);
            owner.Sha256.Should().Be(await _files.ComputeSha256Async(package.MountPointUri, CancellationToken.None));
        }
    }

    [Fact]
    public async Task SameHomeUpdate_ExistingExactOriginBundledDestination_AttachesExplicitOwnerWithoutReplacingEitherPayload()
    {
        var (current, sibling) = await MigrationSharedLegacyAsync(logical: true);
        MigrationSharedSource(logical: true);
        var logical = await MigrationUpdateSelectedAsync(_installer, current, logical: true, CancellationToken.None);
        string destination = _paths.GetInstallDirectory(logical.Entry.PackageId, logical.Entry.PackageVersion);
        var payload = MigrationFiles(destination);
        var native = MigrationFiles(Path.Combine(Home, "templates"));
        var siblingPayload = MigrationFiles(_paths.GetInstallDirectory(sibling.PackageId, sibling.PackageVersion));

        var attached = await _installer.UpdateAsync(current.PackageId, NuGetVersion.Parse("1.0.0"), MigrationFeed, true, false);

        attached.Entry.InstallRefCount.Should().Be(2);
        attached.Entry.IsImplicitlyInstalled.Should().BeFalse();
        attached.Entry.LogicalPackage.Should().BeEquivalentTo(logical.Entry.LogicalPackage);
        (await _workloads.GetWorkloadsAsync()).Should().HaveCount(2).And.ContainEquivalentOf(sibling);
        MigrationFiles(destination).Should().BeEquivalentTo(payload);
        MigrationFiles(Path.Combine(Home, "templates")).Should().BeEquivalentTo(native);
        MigrationFiles(_paths.GetInstallDirectory(sibling.PackageId, sibling.PackageVersion)).Should().BeEquivalentTo(siblingPayload);
        Directory.Exists(_paths.GetInstallDirectory(current.PackageId, current.PackageVersion)).Should().BeFalse();
        await _installer.EnsureInstalledAsync(attached.Entry, CancellationToken.None, checkOnly: true);
    }

    [Fact]
    public async Task SameHomeUpdate_ExistingExactOriginLogicalDestination_ReusesItsAlreadyOwnedRegistration()
    {
        var (current, sibling) = await MigrationSharedLegacyAsync(logical: true);
        MigrationSharedSource(logical: true);
        var logical = await MigrationUpdateSelectedAsync(_installer, current, logical: true, CancellationToken.None);
        var legacy = (await _workloads.GetWorkloadsAsync()).Single(entry => entry.PackageVersion == "1.0.0");
        var restoredOwner = await _workloads.AddOwnershipAsync(MigrationOwnershipEntry(legacy, current.LogicalPackage),
            WorkloadOwnershipKind.Logical);
        var native = MigrationFiles(Path.Combine(Home, "templates"));
        string destination = _paths.GetInstallDirectory(logical.Entry.PackageId, logical.Entry.PackageVersion);
        var payload = MigrationFiles(destination);

        var reused = await _installer.UpdateAsync(current.LogicalPackage!.PackageId, NuGetVersion.Parse("1.0.0"), MigrationFeed,
            true, false, WorkloadOwnershipKind.Logical);

        reused.Entry.Should().BeEquivalentTo(logical.Entry);
        var installed = await _workloads.GetWorkloadsAsync();
        installed.Should().HaveCount(3).And.ContainEquivalentOf(sibling);
        var retained = installed.Single(entry => entry.PackageVersion == restoredOwner.PackageVersion);
        retained.InstallRefCount.Should().Be(1);
        retained.LogicalPackage.Should().BeNull();
        retained.IsImplicitlyInstalled.Should().BeFalse();
        MigrationFiles(destination).Should().BeEquivalentTo(payload);
        MigrationFiles(Path.Combine(Home, "templates")).Should().BeEquivalentTo(native);
        await _installer.EnsureInstalledAsync(reused.Entry, CancellationToken.None, checkOnly: true);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SameHomeUpdate_ExistingBundledDestination_DoesNotWeakenExactHashOrSourceReuse(bool differentSource)
    {
        var (current, _) = await MigrationSharedLegacyAsync(logical: true);
        string incoming = MigrationSharedSource(logical: true);
        var logical = await MigrationUpdateSelectedAsync(_installer, current, logical: true, CancellationToken.None);
        string registry = File.ReadAllText(_paths.WorkloadRegistryPath);
        var native = MigrationFiles(Path.Combine(Home, "templates"));
        string requestedSource = differentSource ? "https://different-origin.invalid/index.json" : MigrationFeed;
        if (!differentSource)
        {
            string modified = Path.Combine(_root, "changed-update-destination.nupkg");
            File.Copy(incoming, modified);
            using (var zip = ZipFile.Open(modified, ZipArchiveMode.Update))
            using (var writer = new StreamWriter(zip.CreateEntry($"tools/{logical.Entry.RuntimeIdentifier}/changed-outer-payload.txt").Open()))
            {
                writer.Write("This is not the installed exact-origin artifact.");
            }
            incoming = modified;
        }
        MigrationSource(incoming, requestedSource);
        Func<Task> update = () => _installer.UpdateAsync(current.PackageId, NuGetVersion.Parse("1.0.0"), requestedSource, true, false);

        await update.Should().ThrowAsync<InvalidWorkloadException>().WithMessage("*different validated origin*");

        File.ReadAllText(_paths.WorkloadRegistryPath).Should().Be(registry);
        MigrationFiles(Path.Combine(Home, "templates")).Should().BeEquivalentTo(native);
        Directory.Exists(_paths.GetInstallDirectory(current.PackageId, current.PackageVersion)).Should().BeTrue();
        await _installer.EnsureInstalledAsync(logical.Entry, CancellationToken.None, checkOnly: true);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SameHomeUpdate_ExistingBundledDestinationFailure_RestoresBothOwnersWithoutDeletingTheReusedPayload(bool cancel)
    {
        var (current, _) = await MigrationSharedLegacyAsync(logical: true);
        MigrationSharedSource(logical: true);
        var logical = await MigrationUpdateSelectedAsync(_installer, current, logical: true, CancellationToken.None);
        string destination = _paths.GetInstallDirectory(logical.Entry.PackageId, logical.Entry.PackageVersion);
        var payload = MigrationFiles(destination);
        string oldPath = _paths.GetInstallDirectory(current.PackageId, current.PackageVersion);
        var oldPayload = MigrationFiles(oldPath);
        string registry = File.ReadAllText(_paths.WorkloadRegistryPath);
        var native = MigrationFiles(Path.Combine(Home, "templates"));
        using var cancellation = new CancellationTokenSource();
        var fault = new MigrationInspector(_files, Home, "1.1.0", cancel ? cancellation : null,
            afterAdmission: async token => !(await _workloads.GetWorkloadsAsync(token)).Any(entry => entry.PackageVersion == "1.0.0"));
        Func<Task> update = () => MigrationInstaller(fault).UpdateAsync(current.PackageId, NuGetVersion.Parse("1.0.0"), MigrationFeed,
            true, false, cancellationToken: cancellation.Token);

        if (cancel)
        {
            await update.Should().ThrowAsync<OperationCanceledException>();
        }
        else
        {
            await update.Should().ThrowAsync<InvalidWorkloadException>().WithMessage("*migration validation failure*");
        }

        fault.Reached.Should().BeTrue();
        File.ReadAllText(_paths.WorkloadRegistryPath).Should().Be(registry);
        MigrationFiles(destination).Should().BeEquivalentTo(payload);
        MigrationFiles(oldPath).Should().BeEquivalentTo(oldPayload);
        MigrationFiles(Path.Combine(Home, "templates")).Should().BeEquivalentTo(native);
        File.Exists(Path.Combine(_paths.Home, ".workload-operations", "dirty.intent")).Should().BeFalse();
        await _installer.EnsureInstalledAsync(logical.Entry, CancellationToken.None, checkOnly: true);
    }

    private async Task<(WorkloadEntry Current, WorkloadEntry Sibling)> MigrationSharedLegacyAsync(bool logical)
    {
        var sibling = await _installer.InstallFromPackageAsync(logical
            ? MigrationRidArchive("0.9.0", bundled: false) : MigrationArchive("0.9.0", bundled: false));
        WorkloadInstallResult original;
        if (logical)
        {
            string pointer = MigrationPointerArchive("1.0.0");
            string implementation = MigrationRidArchive("1.0.0", bundled: false);
            _source.FindLocalImplementation(pointer, "migration.stack.win-x64", "1.0.0", "win-x64").Returns(implementation);
            original = await _installer.InstallFromPackageAsync(pointer);
        }
        else
        {
            original = await _installer.InstallFromPackageAsync(MigrationArchive("1.0.0", bundled: false));
        }
        var owner = original.Entry.LogicalPackage
            ?? new LogicalPackage { PackageId = "migration.pointer", PackageVersion = "1.0.0", Source = MigrationFeed };
        var shared = await _workloads.AddOwnershipAsync(MigrationOwnershipEntry(original.Entry, owner),
            logical ? WorkloadOwnershipKind.Explicit : WorkloadOwnershipKind.Logical);
        shared.InstallRefCount.Should().Be(2);
        return (shared, sibling.Entry);
    }

    private static WorkloadEntry MigrationOwnershipEntry(WorkloadEntry entry, LogicalPackage? logicalPackage)
        => new()
        {
            PackageId = entry.PackageId,
            PackageVersion = entry.PackageVersion,
            Source = entry.Source,
            Kind = entry.Kind,
            EntryPoint = entry.EntryPoint,
            RuntimeIdentifier = entry.RuntimeIdentifier,
            Aliases = entry.Aliases,
            DisplayName = entry.DisplayName,
            Description = entry.Description,
            IsImplicitlyInstalled = entry.IsImplicitlyInstalled,
            LogicalPackage = logicalPackage,
            BundledTemplatesDeclared = entry.BundledTemplatesDeclared,
        };

    private string MigrationSharedSource(bool logical)
    {
        if (!logical)
        {
            string archive = MigrationArchive("1.1.0");
            MigrationSource(archive);
            return archive;
        }

        MigrationSource(MigrationPointerArchive("1.1.0"));
        string implementation = MigrationRidArchive("1.1.0", bundled: true);
        var resolved = MigrationSource(implementation);
        _source.ResolveImplementationAsync(Arg.Any<InspectedWorkloadPackage>(), Arg.Any<WorkloadPointerSelection>(), MigrationFeed,
            Arg.Any<CancellationToken>()).Returns(resolved);
        return implementation;
    }

    private Task<WorkloadUpdateResult> MigrationUpdateSelectedAsync(WorkloadInstaller installer, WorkloadEntry current, bool logical,
        CancellationToken cancellationToken)
        => installer.UpdateAsync(logical ? current.LogicalPackage!.PackageId : current.PackageId, null, MigrationFeed, true, false,
            logical ? WorkloadOwnershipKind.Logical : WorkloadOwnershipKind.Explicit, cancellationToken: cancellationToken);

    private string MigrationFeed => Path.Combine(_root, "migration-feed");

    private string MigrationArchive(string version, bool bundled = true)
    {
        string path = Path.Combine(_root, $"migration-{version}-{Guid.NewGuid():N}.nupkg");
        File.Copy(BuiltArchive, path);
        using var zip = ZipFile.Open(path, ZipArchiveMode.Update);
        MigrationChangeVersion(zip, version);
        if (!bundled)
        {
            zip.GetEntry(BundledTemplateInstaller.ManifestName)!.Delete();
            return path;
        }

        var declaration = zip.GetEntry(BundledTemplateInstaller.ManifestName)!;
        JsonNode manifest;
        using (var input = new StreamReader(declaration.Open())) manifest = JsonNode.Parse(input.ReadToEnd())!;
        var asset = manifest["assets"]![0]!;
        string nestedPath = asset["path"]!.GetValue<string>();
        var nested = zip.GetEntry(nestedPath)!;
        using var bytes = new MemoryStream();
        using (var input = nested.Open()) input.CopyTo(bytes);
        using (var inner = new ZipArchive(bytes, ZipArchiveMode.Update, leaveOpen: true))
        {
            MigrationChangeVersion(inner, version);
            var compatibility = inner.GetEntry("provenance/node/compatibility.json")!;
            JsonNode provenance;
            using (var input = new StreamReader(compatibility.Open())) provenance = JsonNode.Parse(input.ReadToEnd())!;
            provenance["packageVersion"] = version;
            compatibility.Delete();
            using var output = new StreamWriter(inner.CreateEntry("provenance/node/compatibility.json").Open());
            output.Write(provenance.ToJsonString());
        }
        nested.Delete();
        byte[] contents = bytes.ToArray();
        using (var output = zip.CreateEntry(nestedPath).Open()) output.Write(contents);
        asset["version"] = version;
        asset["sha256"] = Convert.ToHexStringLower(SHA256.HashData(contents));
        declaration.Delete();
        using var writer = new StreamWriter(zip.CreateEntry(BundledTemplateInstaller.ManifestName).Open());
        writer.Write(manifest.ToJsonString());
        return path;
    }

    private static void MigrationChangeVersion(ZipArchive zip, string version, string? packageId = null)
    {
        var entry = zip.Entries.Single(entry => entry.FullName.EndsWith(".nuspec", StringComparison.Ordinal));
        XDocument document;
        using (var input = entry.Open()) document = XDocument.Load(input);
        var ns = document.Root!.Name.Namespace;
        var metadata = document.Root.Element(ns + "metadata")!;
        metadata.Element(ns + "version")!.Value = version;
        if (packageId is not null) metadata.Element(ns + "id")!.Value = packageId;
        string path = entry.FullName;
        entry.Delete();
        using var output = zip.CreateEntry(path).Open();
        document.Save(output);
    }

    private ResolvedPackage MigrationSource(string archive, string? source = null)
    {
        using var reader = new NuGet.Packaging.PackageArchiveReader(archive);
        string effectiveSource = source ?? MigrationFeed;
        var resolved = new ResolvedPackage(reader.NuspecReader.GetId().ToLowerInvariant(), reader.NuspecReader.GetVersion(), new(effectiveSource));
        _source.ResolveLatestVersionAsync(resolved.PackageId, Arg.Any<bool?>(), Arg.Any<NuGetVersion>(), Arg.Any<bool>(),
            effectiveSource, Arg.Any<CancellationToken>()).Returns(resolved);
        _source.DownloadAsync(resolved, Arg.Any<IProgress<WorkloadInstallProgress>>(), Arg.Any<CancellationToken>()).Returns(_ =>
        {
            string copy = Path.Combine(_root, $"download-{Guid.NewGuid():N}.nupkg");
            File.Copy(archive, copy);
            return Task.FromResult(new TemporaryWorkloadPackageFile(copy));
        });
        return resolved;
    }

    private string MigrationPointerArchive(string version, string runtimeIdentifier = "win-x64")
    {
        string manifest = Path.Combine(_root, $"pointer-{Guid.NewGuid():N}.json");
        File.WriteAllText(manifest, $$$"""
            {"$schema":"{{{WorkloadManifestSchema.PackageManifestV1Schema}}}","kind":"rid-pointer",
             "packages":{"{{{runtimeIdentifier}}}":"Migration.Stack.{{{runtimeIdentifier}}}"}}
            """);
        var builder = new NuGet.Packaging.PackageBuilder
        {
            Id = "Migration.Stack",
            Version = NuGetVersion.Parse(version),
            Description = "Same-home pointer update fixture.",
        };
        builder.Authors.Add("test");
        builder.PackageTypes.Add(new NuGet.Packaging.Core.PackageType(WorkloadPackageTypes.Workload, new Version(0, 0)));
        builder.Files.Add(new NuGet.Packaging.PhysicalPackageFile { SourcePath = manifest, TargetPath = "workload.json" });
        string archive = Path.Combine(_root, $"pointer-{version}-{Guid.NewGuid():N}.nupkg");
        using var output = File.Create(archive);
        builder.Save(output);
        return archive;
    }

    private string MigrationRidArchive(string version, bool bundled, string runtimeIdentifier = "win-x64")
    {
        string path = MigrationArchive(version, bundled);
        using var zip = ZipFile.Open(path, ZipArchiveMode.Update);
        MigrationChangeVersion(zip, version, $"Migration.Stack.{runtimeIdentifier}");
        var nuspec = zip.Entries.Single(entry => entry.FullName.EndsWith(".nuspec", StringComparison.Ordinal));
        XDocument document;
        using (var input = nuspec.Open()) document = XDocument.Load(input);
        var ns = document.Root!.Name.Namespace;
        var metadata = document.Root.Element(ns + "metadata")!;
        metadata.Element(ns + "packageTypes")!.Element(ns + "packageType")!.SetAttributeValue("name", WorkloadPackageTypes.RuntimeIdentifierPackage);
        metadata.Element(ns + "tags")!.Value += $" rid:{runtimeIdentifier}";
        string nuspecPath = nuspec.FullName;
        nuspec.Delete();
        using (var output = zip.CreateEntry(nuspecPath).Open()) document.Save(output);
        const string originalPayload = "tools/any/";
        string selectedPayload = $"tools/{runtimeIdentifier}/";
        foreach (var entry in zip.Entries.Where(entry => entry.FullName.StartsWith(originalPayload, StringComparison.Ordinal)).ToArray())
        {
            string target = selectedPayload + entry.FullName[originalPayload.Length..];
            using var bytes = new MemoryStream();
            using (var input = entry.Open()) input.CopyTo(bytes);
            entry.Delete();
            bytes.Position = 0;
            using var output = zip.CreateEntry(target).Open();
            bytes.CopyTo(output);
        }
        var manifestEntry = zip.GetEntry("workload.json")!;
        JsonNode manifest;
        using (var input = new StreamReader(manifestEntry.Open())) manifest = JsonNode.Parse(input.ReadToEnd())!;
        manifest["runtimeIdentifier"] = runtimeIdentifier;
        manifest["entryPoint"]!["assemblyPath"] = manifest["entryPoint"]!["assemblyPath"]!.GetValue<string>()
            .Replace(originalPayload, selectedPayload, StringComparison.Ordinal);
        manifestEntry.Delete();
        using (var writer = new StreamWriter(zip.CreateEntry("workload.json").Open())) writer.Write(manifest.ToJsonString());
        if (bundled)
        {
            var declaration = zip.GetEntry(BundledTemplateInstaller.ManifestName)!;
            JsonNode templates;
            using (var input = new StreamReader(declaration.Open())) templates = JsonNode.Parse(input.ReadToEnd())!;
            foreach (var asset in templates["assets"]!.AsArray())
            {
                var definition = asset ?? throw new InvalidOperationException("The bundled fixture must declare template assets.");
                definition["path"] = definition["path"]!.GetValue<string>().Replace(originalPayload, selectedPayload, StringComparison.Ordinal);
            }
            declaration.Delete();
            using var writer = new StreamWriter(zip.CreateEntry(BundledTemplateInstaller.ManifestName).Open());
            writer.Write(templates.ToJsonString());
        }
        return path;
    }

    private WorkloadInstaller MigrationInstaller(IBasicArtifactInspector inspector, IFileSystem? bundleFiles = null, IWorkloadStore? store = null)
    {
        var coordinator = new FileWorkloadOperationCoordinator(_paths);
        var runtime = Substitute.For<IWorkloadRuntimeIdentifierProvider>();
        runtime.Current.Returns("win-x64");
        var selector = new WorkloadRidPackageSelector(runtime);
        var metadata = new WorkloadMetadataReader();
        return new(_source, new WorkloadPackageInspector(metadata, selector), selector,
            new WorkloadDeploymentService(_paths, store ?? _workloads, metadata, coordinator, _files,
                new BundledTemplateInstaller(_templates, inspector, bundleFiles ?? _files, new(Home))),
            coordinator, store ?? _workloads, _files);
    }

    private string MigrationInnerArchive(string outerArchive)
    {
        string inner = Path.Combine(_root, $"user-{Guid.NewGuid():N}.nupkg");
        using var archive = new NuGet.Packaging.PackageArchiveReader(outerArchive);
        var manifest = BundledTemplateInstaller.ReadManifest(Read(archive, BundledTemplateInstaller.ManifestName));
        using var output = File.Create(inner);
        archive.GetStream(manifest.Assets.Single().Path).CopyTo(output);
        return inner;
    }

    private async Task MigrationInstallUserPackageAsync(string archive)
    {
        using var write = await _templates.OpenWriteAsync(new(WorkingDirectory.FromExplicit(_root)), CancellationToken.None);
        await write.ExecuteAsync(async (session, token) =>
        {
            var provider = session.PackageManager.GetBuiltInManagedProvider(InstallationScope.Global);
            (await provider.InstallAsync([new InstallRequest(archive)], token)).Should().ContainSingle().Which.Success.Should().BeTrue();
        }, async (session, token) =>
        {
            (await session.PackageManager.GetTemplatesAsync(token)).Should().NotBeEmpty();
        }, CancellationToken.None);
    }

    private async Task AssertMigrationInstalledAsync(WorkloadEntry entry, string version, string userPackage, string archive, string? source = null)
    {
        entry.PackageVersion.Should().Be(version);
        entry.Source.Should().Be(source ?? MigrationFeed);
        entry.BundledTemplatesDeclared.Should().BeTrue();
        entry.InstallRefCount.Should().Be(1);
        var reopened = new TemplateStore(new(Home), _files, new FileTemplateStoreLock(), NullLoggerFactory.Instance);
        using var session = await reopened.OpenReadAsync(new(WorkingDirectory.FromExplicit(_root)), CancellationToken.None);
        var packages = await session.PackageManager.GetManagedTemplatePackagesAsync(false, CancellationToken.None);
        using var outer = new NuGet.Packaging.PackageArchiveReader(archive);
        using var declarationStream = outer.GetStream(BundledTemplateInstaller.ManifestName);
        using var declaration = JsonDocument.Parse(declarationStream);
        int declaredAssets = declaration.RootElement.GetProperty("assets").GetArrayLength();
        packages.Should().HaveCount(declaredAssets + 1).And.Contain(package => package.Identifier == userPackage);
        var native = packages.Should().ContainSingle(package => package.Identifier == "Azure.Functions.Templates.Node.Basic").Subject;
        native.Version.Should().Be(version);
        File.Exists(native.MountPointUri).Should().BeTrue();
        var templates = await session.PackageManager.GetTemplatesAsync(CancellationToken.None);
        templates.Should().Contain(template => template.Identity == TemplateFixtures.ItemTemplateIdentity)
            .And.Contain(template => template.Identity == "Azure.Functions.Node.Basic.JavaScript");
        string receipt = BundledOwnerPath(version);
        var owner = JsonSerializer.Deserialize(File.ReadAllText(receipt), BundledTemplateJsonContext.Default.BundledTemplateOwner)!;
        owner.Origin.Source.Should().Be(source ?? MigrationFeed);
        owner.Origin.Version.Should().Be(version);
        owner.Origin.Sha256.Should().Be(await _files.ComputeSha256Async(archive, CancellationToken.None));
        owner.Sha256.Should().Be(await _files.ComputeSha256Async(native.MountPointUri, CancellationToken.None));
        owner.Version.Should().Be(version);
    }

    private static Dictionary<string, string> MigrationFiles(string root)
        => Directory.GetFiles(root, "*", SearchOption.AllDirectories).ToDictionary(
            path => Path.GetRelativePath(root, path), path => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path))));

    private static void AssertMigrationFilesUnchanged(string root, IReadOnlyDictionary<string, string> expected)
    {
        Dictionary<string, string> actual = MigrationFiles(root);
        actual.Keys.Should().BeEquivalentTo(expected.Keys);
        foreach (var file in expected) actual[file.Key].Should().Be(file.Value, "persistent native file {0} must remain unchanged", file.Key);
    }

    private string BundledOwnerPath(string? version = null)
    {
        if (version is null)
        {
            using var archive = new NuGet.Packaging.PackageArchiveReader(BuiltArchive);
            version = archive.NuspecReader.GetVersion().ToNormalizedString();
        }
        return Path.Combine(Home, "templates", ".bundled-owners", "azure.functions.templates.node.basic",
            NuGetVersion.Parse(version).ToNormalizedString().ToLowerInvariant() + ".json");
    }

    private sealed class MigrationInspector(IFileSystem files, string home, string version, CancellationTokenSource? cancel,
        TaskCompletionSource? entered = null, Task? release = null,
        Func<CancellationToken, Task<bool>>? afterAdmission = null) : IBasicArtifactInspector
    {
        private readonly BasicArtifactInspector _inner = new(files);
        public bool Reached { get; private set; }

        public async Task ValidateAsync(string archive, BasicTemplateArtifact artifact, CancellationToken cancellationToken)
        {
            await _inner.ValidateAsync(archive, artifact, cancellationToken);
            if (artifact.Version.ToNormalizedString() != version
                || !archive.StartsWith(Path.Combine(home, "templates"), StringComparison.Ordinal))
            {
                return;
            }

            if (afterAdmission is not null && !await afterAdmission(cancellationToken))
            {
                return;
            }
            Reached = true;
            entered?.TrySetResult();
            if (release is not null)
            {
                await release.WaitAsync(cancellationToken);
            }
            if (cancel is not null)
            {
                cancel.Cancel();
                cancellationToken.ThrowIfCancellationRequested();
            }
            throw new BasicAcquisitionException(BasicAcquisitionFailure.InstallFailed, "Owned migration validation failure.");
        }
    }
}
