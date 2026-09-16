// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Workloads;
using Azure.Functions.Cli.Workloads.Catalog;
using Azure.Functions.Cli.Workloads.Discovery;
using Azure.Functions.Cli.Workloads.Install;
using Azure.Functions.Cli.Workloads.Storage;
using Microsoft.Extensions.Options;
using NSubstitute;
using NuGet.Frameworks;
using NuGet.Packaging;
using NuGet.Packaging.Core;
using NuGet.Versioning;
using PackageSource = NuGet.Configuration.PackageSource;

namespace Azure.Functions.Cli.Tests.Workloads.Install;

public abstract class WorkloadInstallerTestBase : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("workload-installer-").FullName;
    private readonly IWorkloadStore _store = Substitute.For<IWorkloadStore>();
    private readonly IWorkloadMetadataReader _metadataReader = Substitute.For<IWorkloadMetadataReader>();
    private readonly IWorkloadCatalog _catalog = Substitute.For<IWorkloadCatalog>();
    private readonly IWorkloadRuntimeIdentifierProvider _runtimeIdentifierProvider =
        Substitute.For<IWorkloadRuntimeIdentifierProvider>();
    private readonly IWorkloadPaths _paths;

    protected WorkloadInstallerTestBase()
    {
        _paths = new WorkloadPathsOptions(Path.Combine(_root, ".azure-functions"));
        _metadataReader.Read(Arg.Any<string>())
            .Returns(new WorkloadMetadata
            {
                Schema = "https://example/workload.schema.json",
                EntryPoint = new EntryPointSpec { AssemblyPath = "Test.dll", Type = "Test.Type" },
            });
        _runtimeIdentifierProvider.Current.Returns("win-x64");
        _store.GetWorkloadsAsync(Arg.Any<CancellationToken>()).Returns([]);
        _store.AddOwnershipAsync(
                Arg.Any<WorkloadEntry>(),
                Arg.Any<WorkloadOwnershipKind>(),
                Arg.Any<CancellationToken>())
            .Returns(call => call.Arg<WorkloadEntry>());
        _store.MoveExplicitOwnershipAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<WorkloadEntry>(),
                Arg.Any<CancellationToken>())
            .Returns(call => new WorkloadOwnershipMoveResult(call.Arg<WorkloadEntry>(), PreviousEntryRemoved: true));
        _store.MoveLogicalOwnershipAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<WorkloadEntry>(),
                Arg.Any<CancellationToken>())
            .Returns(call => new WorkloadOwnershipMoveResult(call.Arg<WorkloadEntry>(), PreviousEntryRemoved: true));
        _store.RemoveOwnershipAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<WorkloadOwnershipKind>(),
                Arg.Any<CancellationToken>())
            .Returns(new WorkloadOwnershipRemovalResult(false, false, null));
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private protected string Root => _root;

    private protected IWorkloadStore Store => _store;

    private protected IWorkloadMetadataReader MetadataReader => _metadataReader;

    private protected IWorkloadCatalog Catalog => _catalog;

    private protected IWorkloadPaths Paths => _paths;

    private protected static WorkloadEntry ExistingEntry(string id, string version) => new()
    {
        PackageId = id,
        PackageVersion = version,
        Aliases = [],
        Kind = WorkloadKind.Workload,
        EntryPoint = new EntryPointSpec { AssemblyPath = "Test.dll", Type = "Test.Type" },
        InstallRefCount = 1,
    };

    private protected static ResolvedPackage NewResolved(string id, string version)
        => new(id, NuGetVersion.Parse(version), new PackageSource("https://example/v3/index.json", "test"));

    private protected WorkloadInstaller NewInstaller(bool includePrerelease = false, IWorkloadMetadataReader? metadataReader = null)
    {
        IWorkloadMetadataReader reader = metadataReader ?? _metadataReader;
        WorkloadRidPackageSelector selector = new(_runtimeIdentifierProvider);
        WorkloadPackageInspector inspector = new(reader, selector);
        WorkloadPackageSource packageSource =
            new(_catalog, inspector, Options.Create(new WorkloadCatalogOptions { IncludePrerelease = includePrerelease }));
        WorkloadDeploymentService deploymentService = new(_paths, _store, reader);
        return new WorkloadInstaller(packageSource, inspector, selector, deploymentService);
    }

    private protected string BuildNupkg(string? tags = null, bool includeFuncCliWorkloadType = true, string version = "1.0.0",
        string? title = null, string description = "For tests.", string id = "Test.Workload",
        string payloadFileName = "Test.dll", string packageType = WorkloadPackageTypes.Workload,
        bool includePayload = true, IEnumerable<(string SourcePath, string TargetPath)>? extraFiles = null)
    {
        string stubAssembly = Path.Combine(_root, $"stub-{Guid.NewGuid():N}.dll");
        File.WriteAllBytes(stubAssembly, [0x4D, 0x5A]);

        var builder = new PackageBuilder
        {
            Id = id,
            Version = NuGetVersion.Parse(version),
            Description = description,
        };
        builder.Authors.Add("test");
        if (title is not null)
        {
            builder.Title = title;
        }

        if (tags is not null)
        {
            foreach (string tag in tags.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                builder.Tags.Add(tag);
            }
        }

        if (includeFuncCliWorkloadType)
        {
            builder.PackageTypes.Add(new PackageType(packageType, new Version(0, 0)));
        }

        if (includePayload)
        {
            builder.Files.Add(new PhysicalPackageFile
            {
                SourcePath = stubAssembly,
                TargetPath = $"tools/{NuGetFramework.Parse("any").GetShortFolderName()}/{payloadFileName}",
            });
        }

        if (extraFiles is not null)
        {
            foreach ((string source, string target) in extraFiles)
            {
                builder.Files.Add(new PhysicalPackageFile { SourcePath = source, TargetPath = target });
            }
        }

        string path = Path.Combine(_root, $"{id}.{Guid.NewGuid():N}.nupkg");
        using (FileStream stream = File.Create(path))
        {
            builder.Save(stream);
        }

        return path;
    }

    private protected string BuildPointerNupkg(string id, string version, string packagesJson)
    {
        string manifest = $$"""
            {
              "$schema": "{{WorkloadManifestSchema.PackageManifestV1Schema}}",
              "kind": "rid-pointer",
              "displayName": "Example workload",
              "description": "Example pointer.",
              "packages": {{packagesJson}}
            }
            """;
        return BuildNupkg(
            id: id,
            version: version,
            tags: "kind:rid-pointer alias:example",
            includePayload: false,
            extraFiles: [(WriteTempFile("workload.json", manifest), "workload.json")]);
    }

    private protected string BuildRidImplementationNupkg(string id, string version, string runtimeIdentifier,
        string packageType = WorkloadPackageTypes.RuntimeIdentifierPackage)
    {
        string manifest = $$"""
            {
              "$schema": "{{WorkloadManifestSchema.PackageManifestV1Schema}}",
              "kind": "content",
              "runtimeIdentifier": "{{runtimeIdentifier}}"
            }
            """;
        return BuildNupkg(
            id: id,
            version: version,
            tags: $"kind:content rid:{runtimeIdentifier}",
            packageType: packageType,
            extraFiles: [(WriteTempFile("workload.json", manifest), "workload.json")]);
    }

    private protected void InstallImplementationOnDisk(string packageId, string version, string runtimeIdentifier)
    {
        string installDirectory = _paths.GetInstallDirectory(packageId, version);
        Directory.CreateDirectory(installDirectory);
        File.WriteAllText(
            Path.Combine(installDirectory, "workload.json"),
            $$"""
            {
              "$schema": "{{WorkloadManifestSchema.PackageManifestV1Schema}}",
              "kind": "content",
              "runtimeIdentifier": "{{runtimeIdentifier}}"
            }
            """);
    }

    private protected string WriteTempFile(string name, string contents)
    {
        string path = Path.Combine(_root, $"{Guid.NewGuid():N}-{name}");
        File.WriteAllText(path, contents);
        return path;
    }
}

internal sealed class RecordingProgress(List<WorkloadInstallProgress> sink) : IProgress<WorkloadInstallProgress>
{
    public void Report(WorkloadInstallProgress value) => sink.Add(value);
}
