// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Security.Cryptography;
using System.IO.Compression;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Configuration;
using Azure.Functions.Cli.Templates.Engine;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.TemplateEngine.Abstractions;
using Microsoft.TemplateEngine.Abstractions.Installer;
using Microsoft.TemplateEngine.Abstractions.TemplatePackage;
using Microsoft.TemplateEngine.Edge.Settings;
using NSubstitute;

namespace Azure.Functions.Cli.Tests.Templates.Engine;

public sealed class TemplateStoreTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "func-protected-store-tests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [Fact]
    public async Task ReadSessions_MissingLiveCache_OverlapWithoutWritingLiveSettings()
    {
        var store = Store();
        var package = WriteNuGetPackage();
        await TemplateFixtures.InstallAsync(package, store.Hive);
        var before = Bytes(store.Hive);
        using var first = new Templater(await store.OpenReadAsync(Context(), CancellationToken.None));
        using var second = new Templater(await Store().OpenReadAsync(Context(), CancellationToken.None));

        var catalogs = await Task.WhenAll(first.ListAsync(TemplateType.Item, CancellationToken.None),
            second.ListAsync(TemplateType.Item, CancellationToken.None));

        catalogs.Should().OnlyContain(catalog => catalog.Count == 1);
        Bytes(store.Hive).Should().BeEquivalentTo(before);
        Directory.GetFiles(store.Hive, "templatecache.json", SearchOption.AllDirectories).Should().BeEmpty();
    }

    [Fact]
    public async Task ActiveRead_WriterWaitsUntilCapabilityScopeIsDisposed()
    {
        var store = Store();
        await InstallAsync(store);
        var reader = new Templater(await store.OpenReadAsync(Context(), CancellationToken.None));
        var selected = (await reader.ResolveGroupAsync(TemplateFixtures.ItemTemplateIdentity, TemplateType.Item, CancellationToken.None)).Group![0];
        var waiting = new TaskCompletionSource<TemplateStoreWaitReason>(TaskCreationOptions.RunContinuationsAsynchronously);
        var pending = Store().OpenWriteAsync(Context(), CancellationToken.None, reason => waiting.TrySetResult(reason));
        (await waiting.Task.WaitAsync(TimeSpan.FromSeconds(10))).Should().Be(TemplateStoreWaitReason.ActiveReaders);
        pending.IsCompleted.Should().BeFalse();

        reader.Dispose();
        using var writer = await pending.WaitAsync(TimeSpan.FromSeconds(10));

        Func<Task> stale = () => selected.InvokeAsync(new(WorkingDirectory.FromExplicit(Path.Combine(_root, "stale")), new Dictionary<string, string?>()),
            CancellationToken.None);
        await stale.Should().ThrowAsync<ObjectDisposedException>();
    }

    [Fact]
    public async Task NativeReplacementFailure_RestoresArchiveRegistrationCacheAndInvalidatesSelection()
    {
        var store = Store();
        var package = WriteNuGetPackage();
        await TemplateFixtures.InstallAsync(package, store.Hive);
        using (var warm = new TemplateEngineSession(Context(), store.Hive))
        {
            await warm.PackageManager.GetTemplatesAsync(CancellationToken.None);
        }

        var before = Bytes(store.Hive);
        ResolvedTemplate? selected = null;
        using var transaction = await store.OpenWriteAsync(Context(), CancellationToken.None);
        Func<Task> replace = () => transaction.ExecuteAsync(async (session, token) =>
        {
            var templater = new Templater(session);
            selected = (await templater.ResolveGroupAsync(TemplateFixtures.ItemTemplateIdentity, TemplateType.Item, token)).Group![0];
            var provider = session.PackageManager.GetBuiltInManagedProvider(InstallationScope.Global);
            var installed = (await session.PackageManager.GetManagedTemplatePackagesAsync(false, token)).Single();
            var result = (await provider.UpdateAsync([new UpdateRequest(installed, "invalid-version")], token)).Single();
            result.Success.Should().BeFalse();
            File.Exists(installed.MountPointUri).Should().BeFalse("the pinned provider deletes the old archive before acquisition");
            (await provider.GetAllTemplatePackagesAsync(token)).Should().BeEmpty();
            throw new IOException(result.ErrorMessage);
        }, ValidateTemplatesAsync, CancellationToken.None);

        await replace.Should().ThrowAsync<IOException>();

        transaction.State.Should().Be(TemplateStoreTransactionState.Restored);
        transaction.RecoverySessionRecreated.Should().BeTrue();
        Bytes(store.Hive).Should().BeEquivalentTo(before);
        Func<Task> stale = () => selected!.InvokeAsync(new(WorkingDirectory.FromExplicit(Path.Combine(_root, "stale")), new Dictionary<string, string?>()),
            CancellationToken.None);
        await stale.Should().ThrowAsync<ObjectDisposedException>();
        transaction.Dispose();
        using var recovered = new Templater(await store.OpenReadAsync(Context(), CancellationToken.None));
        (await recovered.ListAsync(TemplateType.Item, CancellationToken.None)).Should().ContainSingle();
    }

    [Fact]
    public async Task SuccessfulReplacement_RebuildsCacheAndFreshFlowInvokesNewContent()
    {
        var store = Store();
        var original = TemplateFixtures.WriteBasicPackage(Path.Combine(_root, "original"));
        var replacement = TemplateFixtures.WriteBasicPackage(Path.Combine(_root, "replacement"));
        File.WriteAllText(Path.Combine(replacement, "item", "ItemFunction.txt"), "new ItemFunction content");
        await TemplateFixtures.InstallAsync(original, store.Hive);
        for (int iteration = 0; iteration < 2; iteration++)
        {
            using var transaction = await store.OpenWriteAsync(Context(), CancellationToken.None);
            await transaction.ExecuteAsync(async (session, token) =>
            {
                var provider = session.PackageManager.GetBuiltInManagedProvider(InstallationScope.Global);
                var installed = await session.PackageManager.GetManagedTemplatePackagesAsync(false, token);
                (await provider.UninstallAsync(installed, token)).Should().OnlyContain(result => result.Success);
                (await provider.InstallAsync([new InstallRequest(replacement)], token)).Should().ContainSingle().Which.Success.Should().BeTrue();
            }, ValidateTemplatesAsync, CancellationToken.None);
            transaction.State.Should().Be(TemplateStoreTransactionState.Committed);
        }

        Directory.GetFiles(store.Hive, "templatecache.json", SearchOption.AllDirectories).Should().ContainSingle();
        File.Exists(store.Journal).Should().BeFalse();
        using var reader = new Templater(await store.OpenReadAsync(Context(), CancellationToken.None));
        var selected = (await reader.ResolveGroupAsync(TemplateFixtures.ItemTemplateIdentity, TemplateType.Item, CancellationToken.None)).Group![0];
        var output = WorkingDirectory.FromExplicit(Path.Combine(_root, "output"));
        var result = await selected.InvokeAsync(new(output, new Dictionary<string, string?>(), Name: "Created"), CancellationToken.None);
        result.Status.Should().Be(TemplateInvocationStatus.Success);
        File.ReadAllText(Path.Combine(output.Info.FullName, "Created.txt")).Should().Be("new Created content");
    }

    [Fact]
    public async Task SuccessfulNuGetReplacement_OnlyNewArchiveRemainsAndFreshFlowInvokesIt()
    {
        var store = Store();
        var original = WriteNuGetPackage("1.0.0", "old ItemFunction");
        var replacement = WriteNuGetPackage("2.0.0", "new ItemFunction");
        await TemplateFixtures.InstallAsync(original, store.Hive);
        using (var transaction = await store.OpenWriteAsync(Context(), CancellationToken.None))
        {
            await transaction.ExecuteAsync(async (session, token) =>
            {
                var provider = session.PackageManager.GetBuiltInManagedProvider(InstallationScope.Global);
                var installed = await session.PackageManager.GetManagedTemplatePackagesAsync(false, token);
                (await provider.UninstallAsync(installed, token)).Should().ContainSingle().Which.Success.Should().BeTrue();
                (await provider.InstallAsync([new InstallRequest(replacement)], token)).Should().ContainSingle().Which.Success.Should().BeTrue();
            }, async (session, token) =>
            {
                var installed = await session.PackageManager.GetManagedTemplatePackagesAsync(false, token);
                installed.Should().ContainSingle().Which.Version.Should().Be("2.0.0");
                await ValidateTemplatesAsync(session, token);
            }, CancellationToken.None);
            transaction.State.Should().Be(TemplateStoreTransactionState.Committed);
        }

        Directory.GetFiles(Path.Combine(store.Hive, "packages"), "*.nupkg").Should().ContainSingle()
            .Which.Should().EndWith("Func.Tests.Store.2.0.0.nupkg");
        using var reader = new Templater(await store.OpenReadAsync(Context(), CancellationToken.None));
        var selected = (await reader.ResolveGroupAsync(TemplateFixtures.ItemTemplateIdentity, TemplateType.Item, CancellationToken.None)).Group![0];
        var output = WorkingDirectory.FromExplicit(Path.Combine(_root, "nuget-output"));

        var result = await selected.InvokeAsync(new(output, new Dictionary<string, string?>(), Name: "Created"), CancellationToken.None);

        result.Status.Should().Be(TemplateInvocationStatus.Success);
        File.ReadAllText(Path.Combine(output.Info.FullName, "Created.txt")).Should().Be("new Created");
    }

    [Fact]
    public async Task CanceledReplacement_RestoresPriorBytesBeforePropagatingCancellation()
    {
        var store = Store();
        await InstallAsync(store);
        var before = Bytes(store.Hive);
        using var cancellation = new CancellationTokenSource();
        using var transaction = await store.OpenWriteAsync(Context(), cancellation.Token);
        Func<Task> action = () => transaction.ExecuteAsync(async (session, token) =>
        {
            var provider = session.PackageManager.GetBuiltInManagedProvider(InstallationScope.Global);
            var installed = await session.PackageManager.GetManagedTemplatePackagesAsync(false, token);
            (await provider.UninstallAsync(installed, token)).Should().OnlyContain(result => result.Success);
            await cancellation.CancelAsync();
        }, (_, _) => throw new InvalidOperationException("Validation must not run after cancellation."), cancellation.Token);

        await action.Should().ThrowAsync<OperationCanceledException>();

        transaction.State.Should().Be(TemplateStoreTransactionState.Restored);
        transaction.RecoverySessionRecreated.Should().BeTrue();
        Bytes(store.Hive).Should().BeEquivalentTo(before);
    }

    [Fact]
    public async Task CancellationAfterSnapshotIsArmed_DoesNotStartNativeMutation()
    {
        using var cancellation = new CancellationTokenSource();
        var physical = new PhysicalFileSystem();
        var fileSystem = Substitute.For<IFileSystem>();
        fileSystem.DirectoryExists(Arg.Any<string>()).Returns(call => physical.DirectoryExists(call.Arg<string>()));
        fileSystem.When(fs => fs.CreateDirectory(Arg.Any<string>())).Do(call => physical.CreateDirectory(call.Arg<string>()));
        fileSystem.When(fs => fs.DeleteFile(Arg.Any<string>())).Do(call => physical.DeleteFile(call.Arg<string>()));
        fileSystem.CreateTempDirectory(Arg.Any<string>()).Returns(call => physical.CreateTempDirectory(call.Arg<string>()));
        fileSystem.ReadAllTextIfExistsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(call => physical.ReadAllTextIfExistsAsync(call.Arg<string>(), call.Arg<CancellationToken>()));
        fileSystem.WriteAllTextAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(async call =>
        {
            await physical.WriteAllTextAsync(call.ArgAt<string>(0), call.ArgAt<string>(1), call.Arg<CancellationToken>());
            await cancellation.CancelAsync();
        });
        var store = new TemplateStore(new(_root), fileSystem, new FileTemplateStoreLock(), NullLoggerFactory.Instance);
        using var transaction = await store.OpenWriteAsync(Context(), cancellation.Token);
        var mutated = false;
        Func<Task> execute = () => transaction.ExecuteAsync((_, _) =>
        {
            mutated = true;
            return Task.CompletedTask;
        }, ValidateTemplatesAsync, cancellation.Token);

        await execute.Should().ThrowAsync<OperationCanceledException>();

        mutated.Should().BeFalse();
        transaction.State.Should().Be(TemplateStoreTransactionState.Restored);
        Directory.Exists(store.Hive).Should().BeFalse();
        File.Exists(store.Journal).Should().BeFalse();
    }

    [Fact]
    public async Task CancellationDuringReadBootstrap_DisposesEngineAndSharedLeaseBeforeReturning()
    {
        using var cancellation = new CancellationTokenSource();
        var loggerFactory = Substitute.For<ILoggerFactory>();
        loggerFactory.CreateLogger(Arg.Any<string>()).Returns(_ =>
        {
            cancellation.Cancel();
            return NullLogger.Instance;
        });
        var store = new TemplateStore(new(_root), new PhysicalFileSystem(), new FileTemplateStoreLock(), loggerFactory);
        Func<Task> open = () => store.OpenReadAsync(Context(), cancellation.Token);

        await open.Should().ThrowAsync<OperationCanceledException>();

        using var writer = await Store().OpenWriteAsync(Context(), CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(10));
        loggerFactory.DidNotReceive().Dispose();
        Directory.Exists(store.Hive).Should().BeFalse();
    }

    [Fact]
    public async Task NativeCacheWriteFailure_IsVisibleAndRestoresPriorBytes()
    {
        var store = Store();
        await InstallAsync(store);
        var before = Bytes(store.Hive);
        using var transaction = await store.OpenWriteAsync(Context(), CancellationToken.None);
        Func<Task> action = () => transaction.ExecuteAsync((session, _) =>
        {
            // A directory where the native cache file belongs makes its write fail on the actual filesystem.
            Directory.CreateDirectory(Path.Combine(session.Settings.Paths.HostVersionSettingsDir, "templatecache.json"));
            return Task.CompletedTask;
        }, ValidateTemplatesAsync, CancellationToken.None);

        await action.Should().ThrowAsync<IOException>().WithMessage("*cache rebuilding failed*");

        transaction.State.Should().Be(TemplateStoreTransactionState.Restored);
        Bytes(store.Hive).Should().BeEquivalentTo(before);
    }

    [Fact]
    public async Task FailedFirstInstall_RestoresAbsentHiveAndDoesNotPublishRegistration()
    {
        var store = Store();
        var package = TemplateFixtures.WriteBasicPackage(_root);
        using var transaction = await store.OpenWriteAsync(Context(), CancellationToken.None);
        Func<Task> action = () => transaction.ExecuteAsync(async (session, token) =>
        {
            var provider = session.PackageManager.GetBuiltInManagedProvider(InstallationScope.Global);
            (await provider.InstallAsync([new InstallRequest(package)], token)).Should().ContainSingle().Which.Success.Should().BeTrue();
        }, (_, _) => throw new InvalidTemplateMetadataException("Actual content did not match preflight."), CancellationToken.None);

        await action.Should().ThrowAsync<InvalidTemplateMetadataException>();

        transaction.State.Should().Be(TemplateStoreTransactionState.Restored);
        File.Exists(Path.Combine(store.Hive, "packages.json")).Should().BeFalse();
        Directory.Exists(store.Hive).Should().BeFalse();
    }

    [Fact]
    public async Task FailedReadBootstrap_ReleasesSharedOwnership()
    {
        var store = Store();
        var loggerFactory = Substitute.For<ILoggerFactory>();
        loggerFactory.CreateLogger(Arg.Any<string>()).Returns(_ => throw new InvalidOperationException("Injected native bootstrap failure."));
        var failingStore = new TemplateStore(new(_root), new PhysicalFileSystem(), new FileTemplateStoreLock(), loggerFactory);
        Func<Task> action = () => failingStore.OpenReadAsync(Context(), CancellationToken.None);

        await action.Should().ThrowAsync<InvalidOperationException>().WithMessage("Injected native bootstrap failure.");

        using var writer = await store.OpenWriteAsync(Context(), CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(10));
        writer.State.Should().Be(TemplateStoreTransactionState.Ready);
    }

    [Fact]
    public async Task PendingWriterCancellation_DoesNotLeakTicketOrBlockNewReaders()
    {
        var store = Store();
        using var reader = await store.OpenReadAsync(Context(), CancellationToken.None);
        using var cancellation = new CancellationTokenSource();
        var waiting = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var pending = store.OpenWriteAsync(Context(), cancellation.Token, _ => waiting.TrySetResult());
        await waiting.Task.WaitAsync(TimeSpan.FromSeconds(10));

        await cancellation.CancelAsync();
        Func<Task> canceled = () => pending;
        await canceled.Should().ThrowAsync<OperationCanceledException>();

        using var second = await store.OpenReadAsync(Context(), CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(10));
        Directory.GetFiles(store.ControlDirectory, "*.writer").Should().BeEmpty();
    }

    [Fact]
    public async Task CorruptRecoveryJournal_FailsClosedBeforeOpeningEngine()
    {
        var store = Store();
        Directory.CreateDirectory(store.ControlDirectory);
        File.WriteAllText(store.Journal, "unknown");
        Func<Task> read = () => store.OpenReadAsync(Context(), CancellationToken.None);

        await read.Should().ThrowAsync<InvalidDataException>();

        Directory.Exists(store.Hive).Should().BeFalse();
        File.Exists(store.Journal).Should().BeTrue();
    }

    [Theory]
    [InlineData("paths")]
    [InlineData("fileSystem")]
    [InlineData("storeLock")]
    [InlineData("loggerFactory")]
    public void Constructor_NullDependency_Throws(string argument)
    {
        Action construct = () => new TemplateStore(argument == "paths" ? null! : new(_root),
            argument == "fileSystem" ? null! : new PhysicalFileSystem(),
            argument == "storeLock" ? null! : new FileTemplateStoreLock(),
            argument == "loggerFactory" ? null! : NullLoggerFactory.Instance);

        construct.Should().Throw<ArgumentNullException>().WithParameterName(argument);
    }

    [Fact]
    public async Task PreCanceledOpen_DoesNotAcquireLeaseOrCreateHive()
    {
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        var store = Store();
        Func<Task> read = () => store.OpenReadAsync(Context(), cancellation.Token);
        Func<Task> write = () => store.OpenWriteAsync(Context(), cancellation.Token);

        await read.Should().ThrowAsync<OperationCanceledException>();
        await write.Should().ThrowAsync<OperationCanceledException>();

        Directory.Exists(store.Hive).Should().BeFalse();
        Directory.Exists(store.ControlDirectory).Should().BeFalse();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Open_NullContext_ThrowsBeforeAcquiringOwnership(bool write)
    {
        var store = Store();
        Func<Task> open = () => write
            ? store.OpenWriteAsync(null!, CancellationToken.None)
            : store.OpenReadAsync(null!, CancellationToken.None);

        await open.Should().ThrowAsync<ArgumentNullException>().WithParameterName("context");

        Directory.Exists(store.ControlDirectory).Should().BeFalse();
    }

    [Theory]
    [InlineData("store")]
    [InlineData("context")]
    [InlineData("lease")]
    public void WriteSession_NullDependency_Throws(string argument)
    {
        Action construct = () => new TemplateStoreWriteSession(argument == "store" ? null! : Store(),
            argument == "context" ? null! : Context(), argument == "lease" ? null! : Substitute.For<IDisposable>());

        construct.Should().Throw<ArgumentNullException>().WithParameterName(argument);
    }

    [Theory]
    [InlineData("mutate")]
    [InlineData("validate")]
    public async Task ExecuteAsync_NullCallback_ThrowsBeforeCreatingHive(string argument)
    {
        var store = Store();
        using var transaction = await store.OpenWriteAsync(Context(), CancellationToken.None);
        Func<TemplateEngineSession, CancellationToken, Task> callback = (_, _) => Task.CompletedTask;
        Func<Task> execute = () => transaction.ExecuteAsync(argument == "mutate" ? null! : callback,
            argument == "validate" ? null! : callback, CancellationToken.None);

        await execute.Should().ThrowAsync<ArgumentNullException>().WithParameterName(argument);

        transaction.State.Should().Be(TemplateStoreTransactionState.Ready);
        Directory.Exists(store.Hive).Should().BeFalse();
        File.Exists(store.Journal).Should().BeFalse();
    }

    [Fact]
    public void Diagnostics_NullLoggerFactory_Throws()
    {
        Action construct = () => new TemplateStoreEngineDiagnostics(null!);

        construct.Should().Throw<ArgumentNullException>().WithParameterName("loggerFactory");
    }

    [Fact]
    public async Task Factory_ForwardsCancellationSnapshotAndWaitCallback()
    {
        var boundary = Substitute.For<ITemplateStore>();
        var context = Context();
        var snapshot = new TemplateConstraintEvaluationSnapshot([]);
        using var cancellation = new CancellationTokenSource();
        Action<TemplateStoreWaitReason> waiting = _ => { };
        using var engine = new TemplateEngineSession(context, Path.Combine(_root, "isolated"));
        boundary.OpenReadAsync(context, cancellation.Token, snapshot, null, waiting).Returns(engine);
        var factory = new TemplaterFactory(boundary);

        using var flow = await factory.CreateAsync(context, snapshot, cancellation.Token, waiting);

        await boundary.Received(1).OpenReadAsync(context, cancellation.Token, snapshot, null, waiting);
    }

    [Fact]
    public async Task RunningTransaction_DisposalCannotReleaseExclusiveOwnership()
    {
        var store = Store();
        await InstallAsync(store);
        using var transaction = await store.OpenWriteAsync(Context(), CancellationToken.None);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var operation = transaction.ExecuteAsync(async (_, _) =>
        {
            entered.TrySetResult();
            await finish.Task;
        }, ValidateTemplatesAsync, CancellationToken.None);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));

        try
        {
            Action dispose = transaction.Dispose;
            dispose.Should().Throw<InvalidOperationException>();
            var waiting = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var pending = store.OpenReadAsync(Context(), CancellationToken.None, waiting: _ => waiting.TrySetResult());
            await waiting.Task.WaitAsync(TimeSpan.FromSeconds(10));
            pending.IsCompleted.Should().BeFalse();
            finish.TrySetResult();
            await operation;
            Func<Task> again = () => transaction.ExecuteAsync((_, _) => Task.CompletedTask, ValidateTemplatesAsync, CancellationToken.None);
            await again.Should().ThrowAsync<InvalidOperationException>();
            transaction.Dispose();
            using var reader = await pending.WaitAsync(TimeSpan.FromSeconds(10));
        }
        finally
        {
            finish.TrySetResult();
            await operation;
        }
    }

    [Fact]
    public async Task ConcurrentExecution_ClaimsOneTransactionAndRetainsLeaseThroughCompletion()
    {
        var lease = Substitute.For<IDisposable>();
        using var transaction = new TemplateStoreWriteSession(Store(), Context(), lease);
        using var start = new Barrier(9);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var mutations = 0;
        var executions = Enumerable.Range(0, 8).Select(_ => Task.Factory.StartNew(async () =>
        {
            start.SignalAndWait();
            try
            {
                await transaction.ExecuteAsync(async (_, _) =>
                {
                    Interlocked.Increment(ref mutations);
                    entered.TrySetResult();
                    await finish.Task;
                }, (_, _) => Task.CompletedTask, CancellationToken.None);
                return true;
            }
            catch (InvalidOperationException)
            {
                return false;
            }
        }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default).Unwrap()).ToArray();
        start.SignalAndWait();
        bool[] results = [];
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Action dispose = transaction.Dispose;

            dispose.Should().Throw<InvalidOperationException>();
            lease.DidNotReceive().Dispose();
        }
        finally
        {
            finish.TrySetResult();
            results = await Task.WhenAll(executions);
        }

        results.Count(success => success).Should().Be(1);
        mutations.Should().Be(1);
        transaction.State.Should().Be(TemplateStoreTransactionState.Committed);
        transaction.Dispose();
        lease.Received(1).Dispose();
    }

    [Fact]
    public async Task ConcurrentDisposal_ReleasesLeaseOnceAndPreventsLaterExecution()
    {
        var lease = Substitute.For<IDisposable>();
        using var transaction = new TemplateStoreWriteSession(Store(), Context(), lease);
        using var start = new Barrier(9);
        var disposals = Enumerable.Range(0, 8).Select(_ => Task.Factory.StartNew(() =>
        {
            start.SignalAndWait();
            transaction.Dispose();
        }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default)).ToArray();
        start.SignalAndWait();

        await Task.WhenAll(disposals);
        Func<Task> execute = () => transaction.ExecuteAsync((_, _) => Task.CompletedTask, ValidateTemplatesAsync, CancellationToken.None);

        await execute.Should().ThrowAsync<ObjectDisposedException>();
        lease.Received(1).Dispose();
        Directory.Exists(Store().Hive).Should().BeFalse();
    }

    [Fact]
    public async Task ConcurrentExecutionAndDisposal_NeverMutatesAfterLeaseRelease()
    {
        for (var iteration = 0; iteration < 12; iteration++)
        {
            var releases = 0;
            var mutations = 0;
            var lease = Substitute.For<IDisposable>();
            lease.When(value => value.Dispose()).Do(_ => Interlocked.Increment(ref releases));
            using var transaction = new TemplateStoreWriteSession(Store(), Context(), lease);
            using var start = new Barrier(3);
            var finish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var execution = Task.Factory.StartNew(async () =>
            {
                start.SignalAndWait();
                try
                {
                    await transaction.ExecuteAsync(async (_, _) =>
                    {
                        Volatile.Read(ref releases).Should().Be(0);
                        Interlocked.Increment(ref mutations);
                        await finish.Task;
                        Volatile.Read(ref releases).Should().Be(0);
                    }, (_, _) => Task.CompletedTask, CancellationToken.None);
                    return true;
                }
                catch (ObjectDisposedException)
                {
                    return false;
                }
            }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default).Unwrap();
            var disposal = Task.Factory.StartNew(() =>
            {
                start.SignalAndWait();
                try
                {
                    transaction.Dispose();
                    return true;
                }
                catch (InvalidOperationException)
                {
                    return false;
                }
            }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
            start.SignalAndWait();
            bool disposed;
            try
            {
                disposed = await disposal.WaitAsync(TimeSpan.FromSeconds(10));
            }
            finally
            {
                finish.TrySetResult();
            }

            var executed = await execution.WaitAsync(TimeSpan.FromSeconds(10));

            executed.Should().NotBe(disposed);
            mutations.Should().Be(executed ? 1 : 0);
            transaction.Dispose();
            releases.Should().Be(1);
        }
    }

    [Fact]
    public async Task RestoreFailure_RemainsVisibleAndNextOwnerRetriesBeforeReading()
    {
        var store = Store();
        await InstallAsync(store);
        var before = Bytes(store.Hive);
        var physical = new PhysicalFileSystem();
        var fileSystem = Substitute.For<IFileSystem>();
        fileSystem.DirectoryExists(Arg.Any<string>()).Returns(call => physical.DirectoryExists(call.Arg<string>()));
        fileSystem.FileExists(Arg.Any<string>()).Returns(call => physical.FileExists(call.Arg<string>()));
        fileSystem.When(fs => fs.CreateDirectory(Arg.Any<string>())).Do(call => physical.CreateDirectory(call.Arg<string>()));
        fileSystem.When(fs => fs.DeleteDirectory(Arg.Any<string>())).Do(call => physical.DeleteDirectory(call.Arg<string>()));
        fileSystem.When(fs => fs.DeleteFile(Arg.Any<string>())).Do(call => physical.DeleteFile(call.Arg<string>()));
        fileSystem.ReadAllTextIfExistsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(call => physical.ReadAllTextIfExistsAsync(call.Arg<string>(), call.Arg<CancellationToken>()));
        fileSystem.WriteAllTextAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(call => physical.WriteAllTextAsync(call.ArgAt<string>(0), call.ArgAt<string>(1), call.Arg<CancellationToken>()));
        fileSystem.CopyDirectoryAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(call =>
            call.ArgAt<string>(0) == store.Backup
                ? throw new IOException("Injected snapshot restore I/O failure.")
                : physical.CopyDirectoryAsync(call.ArgAt<string>(0), call.ArgAt<string>(1), call.Arg<CancellationToken>()));
        var failingStore = new TemplateStore(new(_root), fileSystem, new FileTemplateStoreLock(), NullLoggerFactory.Instance);
        using var transaction = await failingStore.OpenWriteAsync(Context(), CancellationToken.None);
        Func<Task> operation = () => transaction.ExecuteAsync((_, _) => throw new IOException("Mutation failed."),
            ValidateTemplatesAsync, CancellationToken.None);

        await operation.Should().ThrowAsync<IOException>().WithMessage("*restore I/O failure*");

        transaction.State.Should().Be(TemplateStoreTransactionState.RecoveryRequired);
        transaction.RecoverySessionRecreated.Should().BeFalse();
        File.Exists(store.Journal).Should().BeTrue();
        transaction.Dispose();
        using var reader = await store.OpenReadAsync(Context(), CancellationToken.None);
        Bytes(store.Hive).Should().BeEquivalentTo(before);
        File.Exists(store.Journal).Should().BeFalse();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("relative")]
    public async Task Lock_InvalidDirectory_ThrowsBeforeCreatingFiles(string? directory)
    {
        var storeLock = new FileTemplateStoreLock();
        Func<Task> action = () => storeLock.AcquireAsync(directory!, TemplateStoreAccess.Read, CancellationToken.None);

        await action.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task Lock_InvalidAccess_Throws()
    {
        Func<Task> action = () => new FileTemplateStoreLock().AcquireAsync(_root, (TemplateStoreAccess)42, CancellationToken.None);

        await action.Should().ThrowAsync<ArgumentOutOfRangeException>();
    }

    [WindowsFact]
    public async Task Lock_ReadOnlyMarkerAccessDenied_IsVisibleRatherThanAbsentOrBusy()
    {
        var control = Path.Combine(_root, ".template-store");
        Directory.CreateDirectory(control);
        var marker = Path.Combine(control, $"{Guid.NewGuid():N}.intent");
        File.WriteAllText(marker, string.Empty);
        File.SetAttributes(marker, FileAttributes.ReadOnly);
        try
        {
            Func<Task> read = () => new FileTemplateStoreLock().AcquireAsync(control, TemplateStoreAccess.Read, CancellationToken.None);

            await read.Should().ThrowAsync<UnauthorizedAccessException>();

            File.Exists(marker).Should().BeTrue();
            File.Exists(Path.Combine(control, "owners.lock")).Should().BeFalse();
        }
        finally
        {
            File.SetAttributes(marker, FileAttributes.Normal);
        }
    }

    [WindowsFact]
    public async Task QueuedWriter_MarkerPathsRemainLinkedAndExclusiveUntilCancellation()
    {
        var control = Path.Combine(_root, ".template-store");
        var storeLock = new FileTemplateStoreLock();
        using var reader = await storeLock.AcquireAsync(control, TemplateStoreAccess.Read, CancellationToken.None);
        using var cancellation = new CancellationTokenSource();
        var waiting = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var pending = storeLock.AcquireAsync(control, TemplateStoreAccess.Write, cancellation.Token, _ => waiting.TrySetResult());
        await waiting.Task.WaitAsync(TimeSpan.FromSeconds(10));
        try
        {
            var markers = Directory.GetFiles(control, "*.intent").Concat(Directory.GetFiles(control, "*.writer")).ToArray();
            markers.Should().HaveCount(2);
            foreach (var marker in markers)
            {
                Action inspect = () =>
                {
                    using var forbidden = new FileStream(marker, FileMode.OpenOrCreate, FileAccess.ReadWrite,
                        FileShare.None, bufferSize: 1, FileOptions.DeleteOnClose);
                };
                Action unlink = () => File.Delete(marker);
                Action rename = () => File.Move(marker, marker + ".moved");

                inspect.Should().Throw<IOException>();
                unlink.Should().Throw<IOException>();
                rename.Should().Throw<IOException>();
                File.Exists(marker).Should().BeTrue();
            }
        }
        finally
        {
            await cancellation.CancelAsync();
            Func<Task> canceled = () => pending;
            await canceled.Should().ThrowAsync<OperationCanceledException>();
        }

        Directory.GetFiles(control, "*.intent").Should().BeEmpty();
        Directory.GetFiles(control, "*.writer").Should().BeEmpty();
        using var later = await storeLock.AcquireAsync(control, TemplateStoreAccess.Read, CancellationToken.None);
    }

    [Fact]
    public async Task PrivateRuntimeCacheFailure_DoesNotBecomeSuccessfulCatalog()
    {
        var store = Store();
        await InstallAsync(store);
        using var session = await store.OpenReadAsync(Context(), CancellationToken.None);
        Directory.CreateDirectory(Path.Combine(session.Settings.Paths.HostVersionSettingsDir, "templatecache.json"));
        using var templater = new Templater(session);
        Func<Task> list = () => templater.ListAsync(TemplateType.Item, CancellationToken.None);

        await list.Should().ThrowAsync<IOException>().WithMessage("*cache rebuilding failed*");

        Directory.GetFiles(store.Hive, "templatecache.json", SearchOption.AllDirectories).Should().BeEmpty();
    }

    private TemplateStore Store()
        => new(new(_root), new PhysicalFileSystem(), new FileTemplateStoreLock(), NullLoggerFactory.Instance);

    private TemplateEngineContext Context() => new(WorkingDirectory.FromExplicit(_root));

    private async Task InstallAsync(TemplateStore store)
        => await TemplateFixtures.InstallAsync(TemplateFixtures.WriteBasicPackage(_root), store.Hive);

    private string WriteNuGetPackage(string version = "1.0.0", string content = "ItemFunction content")
    {
        var folder = TemplateFixtures.WriteBasicPackage(Path.Combine(_root, version));
        File.WriteAllText(Path.Combine(folder, "item", "ItemFunction.txt"), content);
        File.WriteAllText(Path.Combine(folder, "Func.Tests.Store.nuspec"),
            $$"""
            <?xml version="1.0"?>
            <package>
              <metadata>
                <id>Func.Tests.Store</id><version>{{version}}</version><authors>func-tests</authors>
                <description>Protected-store fixture.</description>
                <packageTypes><packageType name="FuncTemplate" /></packageTypes>
              </metadata>
            </package>
            """);
        var archive = Path.Combine(_root, $"Func.Tests.Store.{version}.nupkg");
        ZipFile.CreateFromDirectory(folder, archive);
        return archive;
    }

    private static async Task ValidateTemplatesAsync(TemplateEngineSession session, CancellationToken cancellationToken)
    {
        var packages = await session.PackageManager.GetManagedTemplatePackagesAsync(false, cancellationToken);
        packages.Should().NotBeEmpty();
        var templates = await session.PackageManager.GetTemplatesAsync(cancellationToken);
        templates.Should().NotBeEmpty();
    }

    private static Dictionary<string, string> Bytes(string path)
        => Directory.GetFiles(path, "*", SearchOption.AllDirectories)
            .ToDictionary(file => Path.GetRelativePath(path, file), file => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(file))));

    private sealed class WindowsFactAttribute : FactAttribute
    {
        public WindowsFactAttribute()
        {
            if (!OperatingSystem.IsWindows())
            {
                Skip = "Windows mandatory namespace/access protection; Unix permission and interleaving behavior require separate qualification.";
            }
        }
    }
}
