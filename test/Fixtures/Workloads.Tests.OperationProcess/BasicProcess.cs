// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Templates.Acquisition;
using Azure.Functions.Cli.Templates.Engine;
using Azure.Functions.Cli.Workloads.Catalog;
using Azure.Functions.Cli.Workloads.Discovery;
using Azure.Functions.Cli.Workloads.Install;
using Azure.Functions.Cli.Workloads.Storage;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NuGet.Packaging;
using NuGet.Versioning;
using PackageSource = NuGet.Configuration.PackageSource;
using TestConsole = System.Console;

namespace Azure.Functions.Cli.Workloads.Tests.OperationProcess;

internal sealed class BasicProcess
{
    private const string Source = "https://fixtures.invalid/v3/index.json";

    public async Task RunAsync(string[] args, CancellationToken cancellationToken)
    {
        WorkloadPathsOptions paths = new(args[1]);
        FileWorkloadOperationCoordinator coordinator = new(paths);
        WorkloadStore store = new(paths, coordinator);
        PhysicalFileSystem fileSystem = new();
        WorkloadMetadataReader metadata = new();
        WorkloadRidPackageSelector selector = new(new FixedRuntime());
        WorkloadPackageInspector inspector = new(metadata, selector);
        LocalPackages packages = new(args[2]);
        WorkloadDeploymentService deployment = new(paths, store, metadata, coordinator, fileSystem);
        WorkloadInstaller installer = new(packages, inspector, selector, deployment, coordinator, store, fileSystem);
        TemplateStore templates = new(new(paths.Home), fileSystem, new FileTemplateStoreLock(), NullLoggerFactory.Instance);
        BasicTemplateLifecycle lifecycle = new(templates, packages, new CompletionInspector(fileSystem, args[4] == "fail"), new NoOwner(), fileSystem);
        BasicAcquisitionRunner runner = new(new Catalog(args[3]), new(), new PackageSourceProvider(Options.Create(new WorkloadCatalogOptions { Source = Source })),
            store, paths, installer, lifecycle, fileSystem, new AdmissionGate(coordinator));
        BasicAcquisitionResult result = await runner.RunAsync(new(args[5], NuGetVersion.Parse("1.0.0"),
            WorkingDirectory.FromExplicit(paths.Home), Source), cancellationToken);
        TestConsole.WriteLine($"RESULT:{result.Status}:{result.Failure}");
    }

    private sealed class AdmissionGate(IWorkloadOperationCoordinator inner) : IWorkloadOperationCoordinator
    {
        public async Task<IWorkloadOperation> AcquireAsync(CancellationToken cancellationToken, Action? waiting = null)
        {
            TestConsole.WriteLine("PREPARED");
            await TestConsole.In.ReadLineAsync(cancellationToken);
            return await inner.AcquireAsync(cancellationToken, () =>
            {
                TestConsole.WriteLine("WAITING");
                waiting?.Invoke();
            });
        }
    }

    private sealed class CompletionInspector(IFileSystem fileSystem, bool fail) : IBasicArtifactInspector
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
                if (fail)
                {
                    throw new BasicAcquisitionException(BasicAcquisitionFailure.InstallFailed, "Owned fixture rejects native completion.");
                }
            }
        }
    }

    private sealed class Catalog(string manifest) : IBasicCompanionCatalog
    {
        public async Task<BasicCompanionManifest> LoadAsync(CancellationToken cancellationToken)
            => new BasicCompanionManifestReader().Read(await File.ReadAllTextAsync(manifest, cancellationToken));
    }

    private sealed class NoOwner : IBasicTemplateOwnership
    {
        public Task<BasicTemplateOwner?> GetAsync(string packageId, string version, string sha256, CancellationToken cancellationToken)
            => Task.FromResult<BasicTemplateOwner?>(null);
    }

    private sealed class FixedRuntime : IWorkloadRuntimeIdentifierProvider
    {
        public string Current => "test-rid";
    }

    /// <summary>
    /// Supplies owned archive bytes instead of contacting the fixture's deliberately nonexistent feed.
    /// </summary>
    private sealed class LocalPackages : IWorkloadPackageSource, IBasicPackagePayloadSource
    {
        private readonly Dictionary<string, string> _archives = new(StringComparer.OrdinalIgnoreCase);

        public LocalPackages(string directory)
        {
            foreach (string path in Directory.GetFiles(directory, "*.nupkg"))
            {
                using PackageArchiveReader reader = new(path);
                _archives.Add(Key(reader.NuspecReader.GetId(), reader.NuspecReader.GetVersion().ToNormalizedString()), path);
            }
        }

        public Task<ResolvedPackage> ResolveAsync(string packageId, NuGetVersion? version, string? source,
            bool? includePrerelease, bool exact, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            NuGetVersion requested = version ?? throw new InvalidOperationException("The process fixture requires exact versions.");
            if (!_archives.ContainsKey(Key(packageId, requested.ToNormalizedString())))
            {
                throw new FileNotFoundException("The exact owned archive is missing.");
            }

            return Task.FromResult(new ResolvedPackage(packageId, requested, new PackageSource(source ?? Source)));
        }

        public Task<ResolvedPackage> ResolveImplementationAsync(InspectedWorkloadPackage pointer, WorkloadPointerSelection selection,
            string source, CancellationToken cancellationToken = default)
            => ResolveAsync(selection.PackageId, NuGetVersion.Parse(pointer.Identity.Version), source, false, true, cancellationToken);

        public Task<ResolvedPackage?> ResolveLatestVersionAsync(string packageId, bool? includePrerelease, NuGetVersion currentVersion,
            bool allowMajor, string? source, CancellationToken cancellationToken = default)
            => throw new NotSupportedException("This process qualification resolves exact packages only.");

        public string FindLocalImplementation(string pointerPath, string packageId, string version, string runtimeIdentifier)
            => _archives[Key(packageId, version)];

        public Task<TemporaryWorkloadPackageFile> DownloadAsync(ResolvedPackage package, IProgress<WorkloadInstallProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string destination = Path.Combine(Path.GetTempPath(), $"func-owned-operation-{Guid.NewGuid():N}.nupkg");
            File.Copy(_archives[Key(package.PackageId, package.Version.ToNormalizedString())], destination);
            TestConsole.WriteLine($"DOWNLOAD:{package.PackageId}");
            return Task.FromResult(new TemporaryWorkloadPackageFile(destination));
        }

        public Task<Stream> OpenAsync(BasicTemplateArtifact artifact, bool offline, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            TestConsole.WriteLine($"PAYLOAD:{artifact.Version.ToNormalizedString()}");
            return Task.FromResult<Stream>(File.OpenRead(_archives[Key(artifact.PackageId, artifact.Version.ToNormalizedString())]));
        }

        public bool IsLocal(string source) => false;

        private static string Key(string id, string version) => $"{id}/{version}";
    }
}
