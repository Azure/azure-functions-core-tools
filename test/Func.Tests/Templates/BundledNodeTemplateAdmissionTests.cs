// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Security.Cryptography;
using System.Xml.Linq;
using Azure.Functions.Cli.Commands.Setup;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Console;
using Azure.Functions.Cli.Console.Theme;
using Azure.Functions.Cli.Templates.Acquisition;
using Azure.Functions.Cli.Templates.Engine;
using Azure.Functions.Cli.Tests.Console;
using Azure.Functions.Cli.Workloads;
using Azure.Functions.Cli.Workloads.Catalog;
using Azure.Functions.Cli.Workloads.Discovery;
using Azure.Functions.Cli.Workloads.Install;
using Azure.Functions.Cli.Workloads.Storage;
using NSubstitute;
using Microsoft.Extensions.Logging.Abstractions;

namespace Azure.Functions.Cli.Tests.Templates;

public sealed partial class BundledNodeTemplateTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DamagedBundledIncumbent_MustNotReplacePayloadOrOrigin(bool malformed)
    {
        _inspector.FailLive = true;
        Func<Task> first = () => _installer.InstallFromPackageAsync(BuiltArchive);
        await first.Should().ThrowAsync<BundledTemplateRegistrationException>();
        _inspector.FailLive = false;
        var incumbent = (await _workloads.GetWorkloadsAsync()).Single();
        string root = _paths.GetInstallDirectory(incumbent.PackageId, incumbent.PackageVersion);
        string manifest = Path.Combine(root, "workload.json");
        if (malformed) File.WriteAllText(manifest, "{invalid");
        else File.Delete(manifest);
        string incoming = AdmissionArchive("different-source.nupkg");
        string before = File.ReadAllText(_paths.WorkloadRegistryPath);
        string beforeOrigin = File.ReadAllText(Path.Combine(root, ".bundled-template-origin.json"));
        Func<Task> install = () => _installer.InstallFromPackageAsync(incoming);

        await install.Should().ThrowAsync<InvalidWorkloadException>().WithMessage("*damaged*preserve*");
        File.ReadAllText(_paths.WorkloadRegistryPath).Should().Be(before);
        File.ReadAllText(Path.Combine(root, ".bundled-template-origin.json")).Should().Be(beforeOrigin);
        File.Exists(Path.Combine(root, "tools", "any", "admission-marker.txt")).Should().BeFalse();
        using var reader = await _templates.OpenReadAsync(new(WorkingDirectory.FromExplicit(_root)), CancellationToken.None);
        (await reader.PackageManager.GetManagedTemplatePackagesAsync(false, CancellationToken.None)).Should().BeEmpty();
    }

    [Fact]
    public async Task DifferentOuterIdsPreparedAgainstEmptyHive_MustRefuseBeforeSecondStackAdmission()
    {
        string secondArchive = AdmissionArchive("other-outer.nupkg", "Admission.Second.Stack");
        var inspector = AdmissionPackageInspector();
        var coordinator = new FileWorkloadOperationCoordinator(_paths);
        var deployment = AdmissionDeployment(coordinator);
        var first = await inspector.InspectAsync(BuiltArchive);
        var second = await inspector.InspectAsync(secondArchive);
        using var firstPlan = await deployment.PrepareAsync(first, BuiltArchive, WorkloadOwnershipKind.Explicit, null, false, null, CancellationToken.None);
        using var secondPlan = await deployment.PrepareAsync(second, secondArchive, WorkloadOwnershipKind.Explicit, null, false, null, CancellationToken.None);
        using (var operation = await coordinator.AcquireAsync(CancellationToken.None))
        {
            await deployment.InstallPreparedAsync(operation, firstPlan, null, CancellationToken.None);
        }
        string before = File.ReadAllText(_paths.WorkloadRegistryPath);
        string receipt = Path.Combine(Home, "templates", ".bundled-owners", "azure.functions.templates.node.basic.json");
        string beforeReceipt = File.ReadAllText(receipt);
        using var rejected = await coordinator.AcquireAsync(CancellationToken.None);
        Func<Task> commit = () => deployment.InstallPreparedAsync(rejected, secondPlan, null, CancellationToken.None);

        await commit.Should().ThrowAsync<InvalidWorkloadException>().WithMessage("*unknown or conflicting native ownership*");
        File.ReadAllText(_paths.WorkloadRegistryPath).Should().Be(before);
        File.ReadAllText(receipt).Should().Be(beforeReceipt);
        (await _workloads.GetWorkloadsAsync()).Should().ContainSingle();
        Directory.Exists(_paths.GetInstallDirectory(second.Identity.PackageId, second.Identity.Version)).Should().BeFalse();
        rejected.IsDirty.Should().BeFalse();
        File.Exists(Path.Combine(Home, ".template-store", "recovery.ready")).Should().BeFalse();
        using var reader = await _templates.OpenReadAsync(new(WorkingDirectory.FromExplicit(_root)), CancellationToken.None);
        (await reader.PackageManager.GetManagedTemplatePackagesAsync(false, CancellationToken.None)).Should().ContainSingle();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MissingInstalledAsset_SetupCheckReturnsTypedFailedDependency(bool parentMissing)
    {
        var installed = await _installer.InstallFromPackageAsync(BuiltArchive);
        string asset = Path.Combine(_paths.GetInstallDirectory(installed.Entry.PackageId, installed.Entry.PackageVersion),
            "tools", "any", "templates", "node-basic.nupkg");
        if (parentMissing) Directory.Delete(Path.GetDirectoryName(asset)!, true);
        else File.Delete(asset);
        var setup = new SetupDependencyInstaller(_interaction, _workloads, Substitute.For<IWorkloadCatalog>(), _installer);
        var options = new SetupCommandOptions(new(_root), ["node"], [], null, SetupInstallPolicy.IfNeeded, true, true, true, true, SetupOutputMode.Json);
        string before = File.ReadAllText(_paths.WorkloadRegistryPath);

        var result = await setup.EnsureDependencyAsync(options, SetupDependency.Stack("node"), CancellationToken.None);

        result.Status.Should().Be(SetupDependencyStatus.Failed);
        result.Message.Should().Contain("Declared bundled archive", "node-basic.nupkg", "missing", "Preserve");
        File.ReadAllText(_paths.WorkloadRegistryPath).Should().Be(before);
        Func<Task> prepare = () => _installer.EnsureInstalledAsync(installed.Entry, CancellationToken.None, checkOnly: true);
        var error = (await prepare.Should().ThrowAsync<InvalidWorkloadException>()).Which;
        error.InnerException.Should().BeOfType(parentMissing ? typeof(DirectoryNotFoundException) : typeof(FileNotFoundException));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MissingInstalledAsset_ActualSetupJsonReportsFailedDependencyAndNonzeroExit(bool parentMissing)
    {
        var installed = await _installer.InstallFromPackageAsync(BuiltArchive);
        string asset = Path.Combine(_paths.GetInstallDirectory(installed.Entry.PackageId, installed.Entry.PackageVersion),
            "tools", "any", "templates", "node-basic.nupkg");
        if (parentMissing) Directory.Delete(Path.GetDirectoryName(asset)!, true);
        else File.Delete(asset);
        using var stdout = new BufferedConsole(false, false, noColor: true);
        using var stderr = new BufferedConsole(false, false, noColor: true);
        var interaction = new SpectreInteractionService(new DefaultTheme(), stdout, stderr);
        var features = Substitute.For<ISetupFeatureResolver>();
        features.ResolveFeaturesAsync(Arg.Any<SetupCommandOptions>(), Arg.Any<CancellationToken>())
            .Returns(new SetupFeaturePlan(["node"], [], [], false));
        var scopes = Substitute.For<ISetupProfileScopeResolver>();
        scopes.ResolveProfileScopesAsync(Arg.Any<SetupCommandOptions>(), Arg.Any<SetupRenderer>(), Arg.Any<CancellationToken>())
            .Returns([SetupProfileScope.Unconstrained]);
        var plans = Substitute.For<ISetupDependencyPlanBuilder>();
        plans.BuildDependencyPlanAsync(Arg.Any<DirectoryInfo>(), Arg.Any<SetupFeaturePlan>(), Arg.Any<SetupProfileScope>(),
            Arg.Any<CancellationToken>()).Returns(new SetupDependencyPlan([SetupDependency.Stack("node")], []));
        var runner = new SetupRunner(interaction, features, scopes, plans,
            new SetupDependencyInstaller(interaction, _workloads, Substitute.For<IWorkloadCatalog>(), _installer));
        var options = new SetupCommandOptions(new(_root), ["node"], [], null, SetupInstallPolicy.IfNeeded, true, true, true, true, SetupOutputMode.Json);

        var result = await runner.RunAsync(options, CancellationToken.None);

        result.ExitCode.Should().Be(1);
        var events = stdout.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(line =>
        {
            using var document = JsonDocument.Parse(line);
            return document.RootElement.Clone();
        }).ToArray();
        var failed = events.Should().ContainSingle(item => item.GetProperty("type").GetString() == "dependency.result").Subject;
        failed.GetProperty("status").GetString().Should().Be("failed");
        failed.GetProperty("message").GetString().Should().Contain("Declared bundled archive", "missing", "Preserve");
        events.Should().ContainSingle(item => item.GetProperty("type").GetString() == "setup.failed");
        events.Should().NotContain(item => item.GetProperty("type").GetString() == "setup.completed");
        stderr.Output.Should().BeEmpty();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ProtectedAdmission_CompetingNativeWriterWaitsThroughWorkloadAndNativeCompletionOrRecovery(bool failNative)
    {
        var coordinator = new FileWorkloadOperationCoordinator(_paths);
        var gatedStore = AdmissionStore();
        var workloadEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var admit = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        gatedStore.SaveWorkloadAsync(Arg.Any<IWorkloadOperation>(), Arg.Any<WorkloadEntry>(), Arg.Any<CancellationToken>())
            .Returns(async call =>
            {
                workloadEntered.SetResult();
                await admit.Task.WaitAsync(call.Arg<CancellationToken>());
                await _workloads.SaveWorkloadAsync(call.Arg<IWorkloadOperation>(), call.Arg<WorkloadEntry>(), call.Arg<CancellationToken>());
            });
        var native = new AdmissionValidationGate(_files, Home, failNative);
        var deployment = AdmissionDeployment(coordinator, gatedStore, native);
        var inspected = await AdmissionPackageInspector().InspectAsync(BuiltArchive);
        using var prepared = await deployment.PrepareAsync(inspected, BuiltArchive, WorkloadOwnershipKind.Explicit, null, false, null, CancellationToken.None);
        using var operation = await coordinator.AcquireAsync(CancellationToken.None);
        Task<WorkloadInstallResult> install = deployment.InstallPreparedAsync(operation, prepared, null, CancellationToken.None);
        await workloadEntered.Task.WaitAsync(TimeSpan.FromSeconds(30));
        var waiting = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task<TemplateStoreWriteSession> competing = _templates.OpenWriteAsync(new(WorkingDirectory.FromExplicit(_root)),
            CancellationToken.None, _ => waiting.TrySetResult());
        try
        {
            await waiting.Task.WaitAsync(TimeSpan.FromSeconds(30));
            competing.IsCompleted.Should().BeFalse();
            (await _workloads.GetWorkloadsAsync()).Should().BeEmpty();
            admit.SetResult();
            await native.Entered.Task.WaitAsync(TimeSpan.FromSeconds(30));
            (await _workloads.GetWorkloadsAsync()).Should().ContainSingle();
            competing.IsCompleted.Should().BeFalse();
        }
        finally
        {
            admit.TrySetResult();
            native.Continue.TrySetResult();
            if (failNative)
            {
                Func<Task> finish = async () => await install.WaitAsync(TimeSpan.FromSeconds(30));
                (await finish.Should().ThrowAsync<BundledTemplateRegistrationException>()).Which.InnerException
                    .Should().BeOfType<IOException>().Which.Message.Should().Be("Owned post-admission native validation failure.");
            }
            else
            {
                await install.WaitAsync(TimeSpan.FromSeconds(30));
            }
            using var writer = await competing.WaitAsync(TimeSpan.FromSeconds(30));
        }
        operation.IsDirty.Should().BeFalse();
        File.Exists(Path.Combine(Home, ".template-store", "recovery.ready")).Should().BeFalse();
        using var reader = await _templates.OpenReadAsync(new(WorkingDirectory.FromExplicit(_root)), CancellationToken.None);
        (await reader.PackageManager.GetManagedTemplatePackagesAsync(false, CancellationToken.None)).Count.Should().Be(failNative ? 0 : 1);
        (await _workloads.GetWorkloadsAsync()).Should().ContainSingle();
    }

    [Fact]
    public async Task ReuseAdmission_NativeOwnerAppearingAfterPreparationPreventsWorkloadCallback()
    {
        _inspector.FailLive = true;
        Func<Task> first = () => _installer.InstallFromPackageAsync(BuiltArchive);
        await first.Should().ThrowAsync<BundledTemplateRegistrationException>();
        _inspector.FailLive = false;
        var retained = (await _workloads.GetWorkloadsAsync()).Single();
        var coordinator = new FileWorkloadOperationCoordinator(_paths);
        var deployment = AdmissionDeployment(coordinator);
        using var prepared = await deployment.PrepareReuseAsync(retained, CancellationToken.None);
        string userArchive = Path.Combine(_root, "user-owned.nupkg");
        using (var outer = new NuGet.Packaging.PackageArchiveReader(BuiltArchive))
        using (var output = File.Create(userArchive))
        {
            outer.GetStream("tools/any/templates/node-basic.nupkg").CopyTo(output);
        }
        using (var writer = await _templates.OpenWriteAsync(new(WorkingDirectory.FromExplicit(_root)), CancellationToken.None))
        {
            await writer.ExecuteAsync(async (session, token) =>
            {
                var provider = session.PackageManager.GetBuiltInManagedProvider(Microsoft.TemplateEngine.Edge.Settings.InstallationScope.Global);
                (await provider.InstallAsync([new Microsoft.TemplateEngine.Abstractions.Installer.InstallRequest(userArchive)], token)).Single()
                    .Success.Should().BeTrue();
            }, async (session, token) =>
            {
                (await session.PackageManager.GetManagedTemplatePackagesAsync(false, token)).Should().ContainSingle();
            }, CancellationToken.None);
        }
        bool callbackEntered = false;
        string before = File.ReadAllText(_paths.WorkloadRegistryPath);
        using var operation = await coordinator.AcquireAsync(CancellationToken.None);
        Func<Task> commit = () => deployment.CommitPreparedReuseAsync(operation, prepared, _ =>
        {
            callbackEntered = true;
            return Task.FromResult(new WorkloadInstallResult(retained, true));
        }, CancellationToken.None);

        await commit.Should().ThrowAsync<InvalidWorkloadException>().WithMessage("*unknown or conflicting native ownership*");
        callbackEntered.Should().BeFalse();
        File.ReadAllText(_paths.WorkloadRegistryPath).Should().Be(before);
        operation.IsDirty.Should().BeFalse();
        Directory.Exists(Path.Combine(Home, "templates", ".bundled-owners")).Should().BeFalse();
        using var reader = await _templates.OpenReadAsync(new(WorkingDirectory.FromExplicit(_root)), CancellationToken.None);
        (await reader.PackageManager.GetManagedTemplatePackagesAsync(false, CancellationToken.None)).Should().ContainSingle();
    }

    [Fact]
    public async Task AllDeclaredAssets_AConflictingLastAssetPreventsFirstRegistrationAndWorkloadEffects()
    {
        string archive = AdmissionMultiAssetArchive();
        var coordinator = new FileWorkloadOperationCoordinator(_paths);
        var deployment = AdmissionDeployment(coordinator);
        var inspected = await AdmissionPackageInspector().InspectAsync(archive);
        using var prepared = await deployment.PrepareAsync(inspected, archive, WorkloadOwnershipKind.Explicit, null, false, null, CancellationToken.None);
        string userArchive = Path.Combine(_root, "last-user-owned.nupkg");
        using (var outer = new NuGet.Packaging.PackageArchiveReader(BuiltArchive))
        using (var output = File.Create(userArchive))
        {
            outer.GetStream("tools/any/templates/node-basic.nupkg").CopyTo(output);
        }
        using (var writer = await _templates.OpenWriteAsync(new(WorkingDirectory.FromExplicit(_root)), CancellationToken.None))
        {
            await writer.ExecuteAsync(async (session, token) =>
            {
                var provider = session.PackageManager.GetBuiltInManagedProvider(Microsoft.TemplateEngine.Edge.Settings.InstallationScope.Global);
                (await provider.InstallAsync([new Microsoft.TemplateEngine.Abstractions.Installer.InstallRequest(userArchive)], token)).Single()
                    .Success.Should().BeTrue();
            }, async (session, token) =>
            {
                (await session.PackageManager.GetManagedTemplatePackagesAsync(false, token)).Should().ContainSingle();
            }, CancellationToken.None);
        }
        using var operation = await coordinator.AcquireAsync(CancellationToken.None);
        Func<Task> commit = () => deployment.InstallPreparedAsync(operation, prepared, null, CancellationToken.None);

        await commit.Should().ThrowAsync<InvalidWorkloadException>().WithMessage("*unknown or conflicting native ownership*");
        (await _workloads.GetWorkloadsAsync()).Should().BeEmpty();
        Directory.Exists(_paths.WorkloadsRoot).Should().BeFalse();
        Directory.Exists(Path.Combine(Home, "templates", ".bundled-owners")).Should().BeFalse();
        operation.IsDirty.Should().BeFalse();
        File.Exists(Path.Combine(Home, ".template-store", "recovery.ready")).Should().BeFalse();
        using var reader = await _templates.OpenReadAsync(new(WorkingDirectory.FromExplicit(_root)), CancellationToken.None);
        (await reader.PackageManager.GetManagedTemplatePackagesAsync(false, CancellationToken.None)).Should().ContainSingle().Which
            .Identifier.Should().Be("Azure.Functions.Templates.Node.Basic");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OwningOperationDisposal_FollowsNativeLeaseDisposalAndSettledRecovery(bool failNative)
    {
        var nativeLock = new AdmissionObservedLock(new FileTemplateStoreLock());
        var templates = new TemplateStore(new(Home), _files, nativeLock, NullLoggerFactory.Instance);
        var coordinator = new AdmissionObservedCoordinator(new FileWorkloadOperationCoordinator(_paths), nativeLock,
            () => File.Exists(Path.Combine(Home, ".template-store", "recovery.ready")));
        var native = new AdmissionValidationGate(_files, Home, failNative);
        native.Continue.SetResult();
        var deployment = AdmissionDeployment(coordinator, inspector: native, templateStore: templates);
        var inspected = await AdmissionPackageInspector().InspectAsync(BuiltArchive);
        Func<Task> install = () => deployment.InstallAsync(inspected, BuiltArchive, WorkloadOwnershipKind.Explicit, null, false,
            cancellationToken: CancellationToken.None);

        if (failNative)
        {
            await install.Should().ThrowAsync<BundledTemplateRegistrationException>();
        }
        else
        {
            await install();
        }
        coordinator.Disposed.Should().BeTrue();
        coordinator.NativeReleasedFirst.Should().BeTrue();
        coordinator.RecoverySettledFirst.Should().BeTrue();
        nativeLock.Writers.Should().Be(0);
    }

    [Fact]
    public async Task ProtectedAdmission_NullCallbackOrNullResultCannotRegisterTemplates()
    {
        var coordinator = new FileWorkloadOperationCoordinator(_paths);
        var deployment = AdmissionDeployment(coordinator);
        var inspected = await AdmissionPackageInspector().InspectAsync(BuiltArchive);
        using var prepared = await deployment.PrepareAsync(inspected, BuiltArchive, WorkloadOwnershipKind.Explicit, null, false, null, CancellationToken.None);
        var bundles = new BundledTemplateInstaller(_templates, _inspector, _files, new(Home));
        Func<Task> nullCallback = () => bundles.CommitAsync(prepared.BundledTemplates!, null!, CancellationToken.None);
        Func<Task> nullResult = () => bundles.CommitAsync(prepared.BundledTemplates!, _ => Task.FromResult<WorkloadInstallResult>(null!), CancellationToken.None);

        await nullCallback.Should().ThrowAsync<ArgumentNullException>();
        await nullResult.Should().ThrowAsync<InvalidOperationException>().WithMessage("*callback returned no result*");
        (await _workloads.GetWorkloadsAsync()).Should().BeEmpty();
        Directory.Exists(Path.Combine(Home, "templates", ".bundled-owners")).Should().BeFalse();
        File.Exists(Path.Combine(Home, ".template-store", "recovery.ready")).Should().BeFalse();
        using var reader = await _templates.OpenReadAsync(new(WorkingDirectory.FromExplicit(_root)), CancellationToken.None);
        (await reader.PackageManager.GetManagedTemplatePackagesAsync(false, CancellationToken.None)).Should().BeEmpty();
    }

    [Fact]
    public async Task NativeAdmissionWaitCancellation_LeavesWorkloadAndNativePackagesUnchanged()
    {
        using var cancel = new CancellationTokenSource();
        var trackingStore = new AdmissionWaitStore(_templates);
        var coordinator = new FileWorkloadOperationCoordinator(_paths);
        var deployment = AdmissionDeployment(coordinator, templateStore: trackingStore);
        var inspected = await AdmissionPackageInspector().InspectAsync(BuiltArchive);
        using var prepared = await deployment.PrepareAsync(inspected, BuiltArchive, WorkloadOwnershipKind.Explicit, null, false, null, CancellationToken.None);
        using var operation = await coordinator.AcquireAsync(CancellationToken.None);
        using (var nativeOwner = await _templates.OpenWriteAsync(new(WorkingDirectory.FromExplicit(_root)), CancellationToken.None))
        {
            Task<WorkloadInstallResult> install = deployment.InstallPreparedAsync(operation, prepared, null, cancel.Token);
            await trackingStore.Waiting.Task.WaitAsync(TimeSpan.FromSeconds(30));
            cancel.Cancel();
            Func<Task> finish = async () => await install;
            (await finish.Should().ThrowAsync<OperationCanceledException>()).Which.Should().NotBeOfType<BundledTemplateRegistrationCanceledException>();
        }
        (await _workloads.GetWorkloadsAsync()).Should().BeEmpty();
        Directory.Exists(_paths.WorkloadsRoot).Should().BeFalse();
        operation.IsDirty.Should().BeFalse();
        using var reader = await _templates.OpenReadAsync(new(WorkingDirectory.FromExplicit(_root)), CancellationToken.None);
        (await reader.PackageManager.GetManagedTemplatePackagesAsync(false, CancellationToken.None)).Should().BeEmpty();
    }

    [Fact]
    public async Task CancellationAfterWorkloadAdmissionBeforeNativeRegistration_RetainsStackAndSettlesRecovery()
    {
        using var cancel = new CancellationTokenSource();
        var store = AdmissionStore();
        store.SaveWorkloadAsync(Arg.Any<IWorkloadOperation>(), Arg.Any<WorkloadEntry>(), Arg.Any<CancellationToken>())
            .Returns(async call =>
            {
                await _workloads.SaveWorkloadAsync(call.Arg<IWorkloadOperation>(), call.Arg<WorkloadEntry>(), call.Arg<CancellationToken>());
                cancel.Cancel();
            });
        var coordinator = new FileWorkloadOperationCoordinator(_paths);
        var deployment = AdmissionDeployment(coordinator, store);
        var inspected = await AdmissionPackageInspector().InspectAsync(BuiltArchive);
        using var prepared = await deployment.PrepareAsync(inspected, BuiltArchive, WorkloadOwnershipKind.Explicit, null, false, null, CancellationToken.None);
        using var operation = await coordinator.AcquireAsync(CancellationToken.None);
        Func<Task> install = () => deployment.InstallPreparedAsync(operation, prepared, null, cancel.Token);

        var error = (await install.Should().ThrowAsync<BundledTemplateRegistrationCanceledException>()).Which;
        error.CancellationToken.Should().Be(cancel.Token);
        (await _workloads.GetWorkloadsAsync()).Should().ContainSingle();
        operation.IsDirty.Should().BeFalse();
        File.Exists(Path.Combine(Home, ".template-store", "recovery.ready")).Should().BeFalse();
        Directory.Exists(Path.Combine(Home, "templates", ".bundled-owners")).Should().BeFalse();
        using var reader = await _templates.OpenReadAsync(new(WorkingDirectory.FromExplicit(_root)), CancellationToken.None);
        (await reader.PackageManager.GetManagedTemplatePackagesAsync(false, CancellationToken.None)).Should().BeEmpty();
    }

    [Fact]
    public async Task WorkloadCallbackFailsAfterRegistryPersistence_PreservesCausePayloadAndDirtyGate()
    {
        var failure = new IOException("Owned admission read-back failure.");
        var store = AdmissionStore();
        store.SaveWorkloadAsync(Arg.Any<IWorkloadOperation>(), Arg.Any<WorkloadEntry>(), Arg.Any<CancellationToken>())
            .Returns(async call =>
            {
                await _workloads.SaveWorkloadAsync(call.Arg<IWorkloadOperation>(), call.Arg<WorkloadEntry>(), call.Arg<CancellationToken>());
                throw failure;
            });
        var coordinator = new FileWorkloadOperationCoordinator(_paths);
        var deployment = AdmissionDeployment(coordinator, store);
        var inspected = await AdmissionPackageInspector().InspectAsync(BuiltArchive);
        using var prepared = await deployment.PrepareAsync(inspected, BuiltArchive, WorkloadOwnershipKind.Explicit, null, false, null, CancellationToken.None);
        using var operation = await coordinator.AcquireAsync(CancellationToken.None);
        Func<Task> install = () => deployment.InstallPreparedAsync(operation, prepared, null, CancellationToken.None);

        (await install.Should().ThrowAsync<IOException>()).Which.Should().BeSameAs(failure);
        var retained = (await _workloads.GetWorkloadsAsync()).Should().ContainSingle().Subject;
        Directory.Exists(_paths.GetInstallDirectory(retained.PackageId, retained.PackageVersion)).Should().BeTrue();
        operation.IsDirty.Should().BeTrue();
        File.Exists(Path.Combine(Home, ".template-store", "recovery.ready")).Should().BeFalse();
        using var reader = await _templates.OpenReadAsync(new(WorkingDirectory.FromExplicit(_root)), CancellationToken.None);
        (await reader.PackageManager.GetManagedTemplatePackagesAsync(false, CancellationToken.None)).Should().BeEmpty();
    }

    private WorkloadPackageInspector AdmissionPackageInspector()
    {
        var runtime = Substitute.For<IWorkloadRuntimeIdentifierProvider>();
        runtime.Current.Returns("win-x64");
        return new(new WorkloadMetadataReader(), new(runtime));
    }

    private WorkloadDeploymentService AdmissionDeployment(IWorkloadOperationCoordinator coordinator, IWorkloadStore? store = null,
        IBasicArtifactInspector? inspector = null, ITemplateStore? templateStore = null)
        => new(_paths, store ?? _workloads, new WorkloadMetadataReader(), coordinator, _files,
            new BundledTemplateInstaller(templateStore ?? _templates, inspector ?? _inspector, _files, new(Home)));

    private IWorkloadStore AdmissionStore()
    {
        var store = Substitute.For<IWorkloadStore>();
        store.GetWorkloadsAsync(Arg.Any<CancellationToken>()).Returns(call => _workloads.GetWorkloadsAsync(call.Arg<CancellationToken>()));
        return store;
    }

    private string AdmissionArchive(string name, string? packageId = null)
    {
        string path = Path.Combine(_root, name);
        File.Copy(BuiltArchive, path);
        using var archive = ZipFile.Open(path, ZipArchiveMode.Update);
        if (packageId is not null)
        {
            var entry = archive.Entries.Single(entry => entry.FullName.EndsWith(".nuspec", StringComparison.Ordinal));
            XDocument document;
            using (var stream = entry.Open()) document = XDocument.Load(stream);
            var ns = document.Root!.Name.Namespace;
            document.Root.Element(ns + "metadata")!.Element(ns + "id")!.Value = packageId;
            string target = entry.FullName;
            entry.Delete();
            using var output = archive.CreateEntry(target).Open();
            document.Save(output);
        }
        using var marker = new StreamWriter(archive.CreateEntry("tools/any/admission-marker.txt").Open());
        marker.Write("Negative admission fixture, not a shipped artifact.");
        return path;
    }

    private string AdmissionMultiAssetArchive()
    {
        string original = Path.Combine(_root, "native-original.nupkg");
        BasicTemplateDefinition[] definitions;
        using (var outer = new NuGet.Packaging.PackageArchiveReader(BuiltArchive))
        using (var output = File.Create(original))
        {
            outer.GetStream("tools/any/templates/node-basic.nupkg").CopyTo(output);
            using var declaration = JsonDocument.Parse(outer.GetStream("bundled-templates.json"));
            definitions = [.. declaration.RootElement.GetProperty("assets")[0].GetProperty("templates").EnumerateArray()
                .Select(template => new BasicTemplateDefinition(template.GetProperty("path").GetString()!,
                    template.GetProperty("identity").GetString()!, template.GetProperty("type").GetString()!))];
        }
        string other = Path.Combine(_root, "native-other.nupkg");
        File.Copy(original, other);
        using (var native = ZipFile.Open(other, ZipArchiveMode.Update))
        {
            var nuspec = native.GetEntry("Azure.Functions.Templates.Node.Basic.nuspec")!;
            XDocument metadata;
            using (var input = nuspec.Open()) metadata = XDocument.Load(input);
            var ns = metadata.Root!.Name.Namespace;
            metadata.Root.Element(ns + "metadata")!.Element(ns + "id")!.Value = "Admission.Other.Basic";
            nuspec.Delete();
            using (var output = native.CreateEntry("Azure.Functions.Templates.Node.Basic.nuspec").Open()) metadata.Save(output);
            foreach (var definition in definitions)
            {
                var entry = native.GetEntry(definition.Path)!;
                JsonNode configuration;
                using (var input = new StreamReader(entry.Open())) configuration = JsonNode.Parse(input.ReadToEnd())!;
                configuration["identity"] = definition.Identity + ".Other";
                entry.Delete();
                using var output = new StreamWriter(native.CreateEntry(definition.Path).Open());
                output.Write(configuration.ToJsonString());
            }
        }
        string archive = AdmissionArchive("two-assets-negative.nupkg");
        using (var outer = ZipFile.Open(archive, ZipArchiveMode.Update))
        {
            outer.GetEntry("bundled-templates.json")!.Delete();
            using var declaration = new StreamWriter(outer.CreateEntry("bundled-templates.json").Open());
            using var originalReader = new NuGet.Packaging.PackageArchiveReader(original);
            string version = originalReader.NuspecReader.GetVersion().ToNormalizedString();
            declaration.Write(JsonSerializer.Serialize(new
            {
                schemaVersion = 1,
                assets = new[]
                {
                    new { path = "tools/any/templates/other.nupkg", packageId = "Admission.Other.Basic", version,
                        sha256 = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(other))),
                        templates = definitions.Select(definition => new { path = definition.Path, identity = definition.Identity + ".Other", type = definition.Type }).ToArray() },
                    new { path = "tools/any/templates/node-basic.nupkg", packageId = "Azure.Functions.Templates.Node.Basic", version,
                        sha256 = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(original))),
                        templates = definitions.Select(definition => new { path = definition.Path, identity = definition.Identity, type = definition.Type }).ToArray() },
                },
            }));
            using var payload = outer.CreateEntry("tools/any/templates/other.nupkg").Open();
            payload.Write(File.ReadAllBytes(other));
        }
        return archive;
    }

    private sealed class AdmissionValidationGate(IFileSystem files, string home, bool failNative = false) : IBasicArtifactInspector
    {
        private readonly BasicArtifactInspector _inner = new(files);
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Continue { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task ValidateAsync(string archive, BasicTemplateArtifact artifact, CancellationToken cancellationToken)
        {
            await _inner.ValidateAsync(archive, artifact, cancellationToken);
            if (archive.StartsWith(Path.Combine(home, "templates"), StringComparison.Ordinal))
            {
                Entered.TrySetResult();
                await Continue.Task.WaitAsync(cancellationToken);
                if (failNative)
                {
                    throw new IOException("Owned post-admission native validation failure.");
                }
            }
        }
    }

    private sealed class AdmissionWaitStore(ITemplateStore inner) : ITemplateStore
    {
        public TaskCompletionSource Waiting { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<TemplateEngineSession> OpenReadAsync(TemplateEngineContext context, CancellationToken cancellationToken,
            TemplateConstraintEvaluationSnapshot? snapshot = null, TemplateProjectSupport? projectSupport = null,
            Action<TemplateStoreWaitReason>? waiting = null)
            => inner.OpenReadAsync(context, cancellationToken, snapshot, projectSupport, waiting);

        public Task<TemplateStoreWriteSession> OpenWriteAsync(TemplateEngineContext context, CancellationToken cancellationToken,
            Action<TemplateStoreWaitReason>? waiting = null)
            => inner.OpenWriteAsync(context, cancellationToken, reason =>
            {
                Waiting.TrySetResult();
                waiting?.Invoke(reason);
            });
    }

    private sealed class AdmissionObservedLock(ITemplateStoreLock inner) : ITemplateStoreLock
    {
        public int Writers { get; private set; }
        public async Task<IDisposable> AcquireAsync(string directory, TemplateStoreAccess access, CancellationToken cancellationToken,
            Action<TemplateStoreWaitReason>? waiting = null)
        {
            var lease = await inner.AcquireAsync(directory, access, cancellationToken, waiting);
            if (access == TemplateStoreAccess.Write) Writers++;
            return new AdmissionObservedLease(lease, () =>
            {
                if (access == TemplateStoreAccess.Write) Writers--;
            });
        }
    }

    private sealed class AdmissionObservedLease(IDisposable inner, Action released) : IDisposable
    {
        public void Dispose()
        {
            inner.Dispose();
            released();
        }
    }

    private sealed class AdmissionObservedCoordinator(IWorkloadOperationCoordinator inner, AdmissionObservedLock nativeLock,
        Func<bool> recoveryPending) : IWorkloadOperationCoordinator
    {
        public bool Disposed { get; private set; }
        public bool NativeReleasedFirst { get; private set; }
        public bool RecoverySettledFirst { get; private set; }
        public async Task<IWorkloadOperation> AcquireAsync(CancellationToken cancellationToken, Action? waiting = null)
        {
            var operation = await inner.AcquireAsync(cancellationToken, waiting);
            return new AdmissionObservedOperation(operation, () =>
            {
                NativeReleasedFirst = nativeLock.Writers == 0;
                RecoverySettledFirst = !recoveryPending();
                Disposed = true;
            });
        }
    }

    private sealed class AdmissionObservedOperation(IWorkloadOperation inner, Action disposing) : IWorkloadOperation
    {
        public bool IsDirty => inner.IsDirty;
        public void Validate(IWorkloadPaths paths) => inner.Validate(paths);
        public void BeginMutation() => inner.BeginMutation();
        public void CompleteMutation() => inner.CompleteMutation();
        public void Dispose()
        {
            disposing();
            inner.Dispose();
        }
    }
}
