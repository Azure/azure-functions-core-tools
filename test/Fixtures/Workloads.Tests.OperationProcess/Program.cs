// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Templates.Acquisition;
using Azure.Functions.Cli.Templates.Engine;
using Azure.Functions.Cli.Workloads;
using Azure.Functions.Cli.Workloads.Discovery;
using Azure.Functions.Cli.Workloads.Install;
using Azure.Functions.Cli.Workloads.Storage;
using Microsoft.Extensions.Logging.Abstractions;
using NuGet.Versioning;
using TestConsole = System.Console;

namespace Azure.Functions.Cli.Workloads.Tests.OperationProcess;

internal sealed class Program
{
    private static async Task Main(string[] args)
    {
        string mode = args[0];
        if (mode == "bundled-node")
        {
            Environment.ExitCode = await new BundledNodeProcess().RunAsync(args, CancellationToken.None);
            return;
        }
        WorkloadPathsOptions paths = new(args[1]);
        SignalingCoordinator coordinator = new(new FileWorkloadOperationCoordinator(paths));
        PhysicalFileSystem fileSystem = new();
        WorkloadMetadataReader metadata = new();
        WorkloadStore store = mode is "save" or "move" or "directory-crash" or "uninstall-held"
            ? new PausingStore(paths, coordinator) : new WorkloadStore(paths, coordinator);
        TemplateStore bundledStore = new(new(paths.Home), fileSystem, new FileTemplateStoreLock(), NullLoggerFactory.Instance);
        WorkloadDeploymentService deployment = new(paths, store, metadata, coordinator, fileSystem,
            new BundledTemplateInstaller(bundledStore, new BasicArtifactInspector(fileSystem), fileSystem, new(paths.Home)));
        using CancellationTokenSource cancellation = new();
        CancellationToken token = cancellation.Token;
        try
        {
            if (mode == "basic")
            {
                await new BasicProcess().RunAsync(args, token);
                return;
            }

            if (mode == "save")
            {
                TestConsole.WriteLine("STARTING");
                await store.SaveWorkloadAsync(new() { PackageId = args[2], PackageVersion = "1.0.0", Kind = WorkloadKind.Content }, token);
                TestConsole.WriteLine("COMMITTED");
                return;
            }

            if (mode == "move")
            {
                TestConsole.WriteLine("STARTING");
                await store.MoveLogicalOwnershipAsync(args[2], "1.0.0", new()
                {
                    PackageId = args[2], PackageVersion = "2.0.0", Kind = WorkloadKind.Content, IsImplicitlyInstalled = true,
                    LogicalPackage = new() { PackageId = args[3], PackageVersion = "2.0.0" },
                }, token);
                TestConsole.WriteLine("COMMITTED");
                return;
            }

            if (mode is "uninstall" or "uninstall-held")
            {
                TestConsole.WriteLine("STARTING");
                string version = args.Length > 3 ? args[3] : "1.0.0";
                WorkloadOwnershipKind ownership = args.Length > 4 && args[4] == "logical"
                    ? WorkloadOwnershipKind.Logical : WorkloadOwnershipKind.Explicit;
                bool removed = await deployment.UninstallAsync(args[2], version, ownership, token);
                TestConsole.WriteLine(removed ? "REMOVED" : "ABSENT");
                return;
            }

            if (mode == "reuse")
            {
                TestConsole.WriteLine("STARTING");
                await deployment.TryReuseImplementationAsync(args[2], "1.0.0", "test-rid",
                    new() { PackageId = args[3], PackageVersion = "1.0.0" }, token);
                TestConsole.WriteLine("COMMITTED");
                return;
            }

            if (mode is "install" or "directory-crash" or "template-crash" or "update")
            {
                WorkloadPackageInspector inspector = new(metadata, new(new FixedRuntime()));
                InspectedWorkloadPackage package = await inspector.InspectAsync(args[2], token);
                WorkloadEntry? current = mode == "update" ? (await store.GetWorkloadsAsync(token)).Single() : null;
                using PreparedWorkloadDeployment prepared = await deployment.PrepareAsync(package, args[2], WorkloadOwnershipKind.Explicit,
                    logicalPackage: null, force: false, progress: null, token);
                PreparedBasicTemplate? basic = null;
                BasicTemplateLifecycle? lifecycle = null;
                TemplateEngineContext context = new(WorkingDirectory.FromExplicit(paths.Home));
                if (mode == "template-crash")
                {
                    string archive = args[3];
                    BasicTemplateArtifact artifact = new("Fixture.Basic", NuGetVersion.Parse("1.0.0"), "https://fixtures.invalid/v3/index.json",
                        await fileSystem.ComputeSha256Async(archive, token), "fixture",
                        [new("basic/.template.config/template.json", "Fixture.Basic.Item", "item")]);
                    TemplateStore templates = new(new(paths.Home), fileSystem, new FileTemplateStoreLock(), NullLoggerFactory.Instance);
                    lifecycle = new(templates, new LocalPayload(archive), new PausingInspector(fileSystem), new NoOwner(), fileSystem);
                    basic = await lifecycle.PrepareAsync(new(artifact, false), context, false, token);
                }

                using (basic)
                {
                    TestConsole.WriteLine("PREPARED");
                    await TestConsole.In.ReadLineAsync(token);
                    if (current is not null)
                    {
                        await deployment.UpdateAsync(current, package, args[2], WorkloadOwnershipKind.Explicit,
                            logicalPackage: null, progress: null, token);
                        TestConsole.WriteLine("STACK_COMMITTED");
                        return;
                    }

                    using IWorkloadOperation operation = await coordinator.AcquireAsync(token);
                    await deployment.InstallPreparedAsync(operation, prepared, progress: null, token);
                    TestConsole.WriteLine("STACK_COMMITTED");
                    if (basic is not null)
                    {
                        await lifecycle!.CommitAsync(basic, context, token);
                    }
                }

                return;
            }

            if (mode == "cancel-wait")
            {
                Task<IWorkloadOperation> acquisition = coordinator.AcquireAsync(token);
                await TestConsole.In.ReadLineAsync(token);
                await cancellation.CancelAsync();
                using IWorkloadOperation unexpected = await acquisition;
                throw new InvalidOperationException("The held writer must prevent admission.");
            }

            using IWorkloadOperation lease = await coordinator.AcquireAsync(token);
            TestConsole.WriteLine("ACQUIRED");
            await TestConsole.In.ReadLineAsync(token);
        }
        catch (WorkloadRecoveryRequiredException)
        {
            TestConsole.WriteLine("RECOVERY_REQUIRED");
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            TestConsole.WriteLine("CANCELED");
        }
    }

    private sealed class SignalingCoordinator(IWorkloadOperationCoordinator inner) : IWorkloadOperationCoordinator
    {
        public Task<IWorkloadOperation> AcquireAsync(CancellationToken cancellationToken, Action? waiting = null)
            => inner.AcquireAsync(cancellationToken, () =>
            {
                TestConsole.WriteLine("WAITING");
                waiting?.Invoke();
            });
    }

    private sealed class PausingStore(IWorkloadPaths paths, IWorkloadOperationCoordinator coordinator) : WorkloadStore(paths, coordinator)
    {
        internal override async Task SerializeAsync(Stream stream, WorkloadRegistry registry, CancellationToken cancellationToken)
        {
            TestConsole.WriteLine("BEFORE_REGISTRY");
            await TestConsole.In.ReadLineAsync(cancellationToken);
            await base.SerializeAsync(stream, registry, cancellationToken);
        }
    }

    private sealed class FixedRuntime : IWorkloadRuntimeIdentifierProvider
    {
        public string Current => "test-rid";
    }

    private sealed class LocalPayload(string archive) : IBasicPackagePayloadSource
    {
        public Task<Stream> OpenAsync(BasicTemplateArtifact artifact, bool offline, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult<Stream>(File.OpenRead(archive));
        }
    }

    private sealed class NoOwner : IBasicTemplateOwnership
    {
        public Task<BasicTemplateOwner?> GetAsync(string packageId, string version, string sha256, CancellationToken cancellationToken)
            => Task.FromResult<BasicTemplateOwner?>(null);
    }

    private sealed class PausingInspector(IFileSystem fileSystem) : IBasicArtifactInspector
    {
        private readonly BasicArtifactInspector _inner = new(fileSystem);
        private int _calls;
        public async Task ValidateAsync(string archive, BasicTemplateArtifact artifact, CancellationToken cancellationToken)
        {
            await _inner.ValidateAsync(archive, artifact, cancellationToken);
            if (++_calls == 3)
            {
                TestConsole.WriteLine("TEMPLATE_MUTATING");
                await TestConsole.In.ReadLineAsync(cancellationToken);
            }
        }
    }
}
