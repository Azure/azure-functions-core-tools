// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Configuration;
using Azure.Functions.Cli.Templates.Acquisition;
using Azure.Functions.Cli.Templates.Engine;
using Azure.Functions.Cli.Workloads;
using Azure.Functions.Cli.Workloads.Catalog;
using Azure.Functions.Cli.Workloads.Discovery;
using Azure.Functions.Cli.Workloads.Install;
using Azure.Functions.Cli.Workloads.Storage;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using NuGet.Versioning;
using PackageSource = NuGet.Configuration.PackageSource;

namespace Azure.Functions.Cli.Tests.Templates.Acquisition;

internal sealed class BasicAcquisitionFixture : IDisposable
{
    public const string Source = "https://fixtures.invalid/v3/index.json";
    public const string BasicId = "Fixture.Basic";
    private readonly Dictionary<string, string> _archives = new(StringComparer.OrdinalIgnoreCase);
    private readonly PhysicalFileSystem _fileSystem = new();
    private readonly IWorkloadCatalog _workloadCatalog = Substitute.For<IWorkloadCatalog>();
    private readonly IWorkloadRuntimeIdentifierProvider _runtime = Substitute.For<IWorkloadRuntimeIdentifierProvider>();

    public BasicAcquisitionFixture()
    {
        Root = Directory.CreateTempSubdirectory("func-basic-acquisition-").FullName;
        Paths = new(Path.Combine(Root, "workloads-home"));
        Store = new(Paths);
        Templates = new(new(Path.Combine(Root, "cli-home")), _fileSystem, new FileTemplateStoreLock(), NullLoggerFactory.Instance);
        Payload = new(_archives);
        Ownership = new();
        for (int version = 1; version <= 2; version++)
        {
            string text = $"{version}.0.0";
            WriteBasic(text);
            WriteStack("Fixture.StackA", text);
        }

        WriteStack("Fixture.StackB", "1.0.0");
        WriteStack("Fixture.StackC", "1.0.0");
        _runtime.Current.Returns("test-rid");
        _workloadCatalog.ResolveVersionAsync(Arg.Any<string>(), Arg.Any<NuGetVersion>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                if (StackTransportFailure is not null)
                {
                    throw StackTransportFailure;
                }

                return _archives.ContainsKey(Key(call.ArgAt<string>(0), call.Arg<NuGetVersion>().ToNormalizedString()))
                    ? new ResolvedPackage(call.ArgAt<string>(0), call.Arg<NuGetVersion>(), new PackageSource(call.ArgAt<string>(2))) : null;
            });
        _workloadCatalog.DownloadAsync(Arg.Any<ResolvedPackage>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                call.Arg<CancellationToken>().ThrowIfCancellationRequested();
                ResolvedPackage package = call.Arg<ResolvedPackage>();
                Downloads.Add(Key(package.PackageId, package.Version.ToNormalizedString()));
                return (Stream)File.OpenRead(_archives[Key(package.PackageId, package.Version.ToNormalizedString())]);
            });
        var metadata = new WorkloadMetadataReader();
        var selector = new WorkloadRidPackageSelector(_runtime);
        var inspector = new WorkloadPackageInspector(metadata, selector);
        Installer = new(new WorkloadPackageSource(_workloadCatalog, inspector, Options.Create(new WorkloadCatalogOptions())),
            inspector, selector, new WorkloadDeploymentService(Paths, Store, metadata));
        Catalog = new(ReadFixtureJson());
        SourceProvider = new(Options.Create(new WorkloadCatalogOptions()));
    }

    public string Root { get; }
    public WorkloadPathsOptions Paths { get; }
    public WorkloadStore Store { get; }
    public TemplateStore Templates { get; }
    public WorkloadInstaller Installer { get; }
    public FixtureCatalog Catalog { get; }
    public PackageSourceProvider SourceProvider { get; set; }
    public FixturePayloadSource Payload { get; }
    public FixtureOwnership Ownership { get; }
    public List<string> Downloads { get; } = [];
    public Exception? StackTransportFailure { get; set; }

    public BasicAcquisitionRequest Request(string id = "Fixture.StackA", string version = "1.0.0", bool check = false, bool offline = false)
        => new(id, NuGetVersion.Parse(version), WorkingDirectory.FromExplicit(Root), Source, check, offline);

    public BasicAcquisitionRunner Runner(IBasicArtifactInspector? inspector = null, IBasicTemplateOwnership? ownership = null,
        IPackageSourceProvider? source = null, IBasicPackagePayloadSource? payload = null, IBasicCompanionCatalog? catalog = null)
        => new(catalog ?? Catalog, new BasicAcquisitionPlanner(), source ?? SourceProvider, new WorkloadStore(Paths), Paths, Installer,
            new BasicTemplateLifecycle(Templates, payload ?? Payload, inspector ?? new BasicArtifactInspector(_fileSystem), ownership ?? Ownership, _fileSystem),
            _fileSystem);

    public BasicTemplateArtifact Artifact(string version = "1.0.0")
        => new BasicCompanionManifestReader().Read(Catalog.Json).Artifacts.Single(a => a.Version.ToNormalizedString() == version);

    public string Archive(string id = BasicId, string version = "1.0.0") => _archives[Key(id, version)];

    public async Task SeedStackAsync(string id, string version)
        => await Installer.InstallFromCatalogAsync(id, NuGetVersion.Parse(version), Source, false, true, false, cancellationToken: CancellationToken.None);

    public async Task<IReadOnlyList<TemplateCatalogEntry>> ReadTemplatesAsync()
    {
        using var templater = new Templater(await Templates.OpenReadAsync(new(WorkingDirectory.FromExplicit(Root)), CancellationToken.None));
        return await templater.ListAsync(TemplateType.Item, CancellationToken.None);
    }

    public Dictionary<string, string> HiveHashes()
        => Directory.Exists(Templates.Hive)
            ? Directory.GetFiles(Templates.Hive, "*", SearchOption.AllDirectories).ToDictionary(
                path => Path.GetRelativePath(Templates.Hive, path), path => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))))
            : [];

    public void ReplaceBasicWithCuratedContent(bool approveBytes)
    {
        string archive = Archive();
        using (var zip = ZipFile.Open(archive, ZipArchiveMode.Update))
        {
            WriteEntry(zip, "curated/.template.config/template.json",
                """{"identity":"Fixture.Curated.Project","name":"Unapproved sample","shortName":"fixture-curated","tags":{"type":"project"}}""");
            WriteEntry(zip, "curated/host.json", """{"version":"2.0"}""");
        }

        if (approveBytes)
        {
            JsonNode document = JsonNode.Parse(Catalog.Json)!;
            document["artifacts"]![0]!["sha256"] = Hash(archive);
            Catalog.Json = document.ToJsonString();
        }
    }

    public void UseIndependentBCompanion()
    {
        const string id = "Fixture.Unrelated";
        string archive = Path.Combine(Root, $"{id}.1.0.0.nupkg");
        using (var zip = ZipFile.Open(archive, ZipArchiveMode.Create))
        {
            WriteEntry(zip, $"{id}.nuspec", Nuspec(id, "1.0.0", "FuncTemplate"));
            WriteEntry(zip, "unrelated/.template.config/template.json",
                """{"identity":"Fixture.Unrelated.Item","name":"Independent basic fixture","shortName":"fixture-unrelated","tags":{"type":"item"}}""");
            WriteEntry(zip, "unrelated/Independent.txt", "B-only basic content, not A's missing companion.");
        }

        _archives[Key(id, "1.0.0")] = archive;
        string hash = Hash(archive);
        JsonNode manifest = JsonNode.Parse(Catalog.Json)!;
        JsonNode stackB = manifest["stacks"]!.AsArray().Single(stack => stack!["packageId"]!.GetValue<string>() == "Fixture.StackB")!;
        stackB["companions"]![0]!["packageId"] = id;
        stackB["companions"]![0]!["versions"] = "[1.0.0]";
        manifest["artifacts"]!.AsArray().Add(JsonNode.Parse($$"""
            {
              "packageId":"Fixture.Unrelated","version":"1.0.0","source":"https://fixtures.invalid/v3/index.json",
              "sha256":"{{hash}}","contentRevision":"fixture-independent-basic-source","basicOnly":true,
              "templates":[{"path":"unrelated/.template.config/template.json","identity":"Fixture.Unrelated.Item","type":"item"}]
            }
            """));
        Catalog.Json = manifest.ToJsonString();
        Ownership.OtherEntries[$"{id}/1.0.0/{hash}"] = new(Source, null);
    }

    public void Dispose() => Directory.Delete(Root, recursive: true);

    private string ReadFixtureJson()
    {
        using Stream stream = typeof(BasicAcquisitionFixture).Assembly.GetManifestResourceStream(
            "Azure.Functions.Cli.Tests.Templates.Acquisition.basic-companions.json")!;
        using StreamReader text = new(stream);
        JsonNode fixture = JsonNode.Parse(text.ReadToEnd())!;
        foreach (JsonNode? artifact in fixture["artifacts"]!.AsArray())
        {
            string version = artifact!["version"]!.GetValue<string>();
            string hash = Hash(Archive(version: version));
            artifact["sha256"] = hash;
            Ownership.Entries[version] = new(Source, null);
            Ownership.Digests[version] = hash;
        }

        return fixture.ToJsonString();
    }

    private void WriteBasic(string version)
    {
        string archive = Path.Combine(Root, $"{BasicId}.{version}.nupkg");
        using (var zip = ZipFile.Open(archive, ZipArchiveMode.Create))
        {
            WriteEntry(zip, $"{BasicId}.nuspec", Nuspec(BasicId, version, "FuncTemplate"));
            WriteEntry(zip, "basic/.template.config/template.json",
                """{"identity":"Fixture.Basic.Item","name":"Basic fixture item","shortName":"fixture-basic-item","tags":{"type":"item"},"sourceName":"BasicFunction"}""");
            WriteEntry(zip, "basic/BasicFunction.txt", $"basic-only fixture {version}");
        }

        _archives[Key(BasicId, version)] = archive;
    }

    private void WriteStack(string id, string version)
    {
        string pointer = Path.Combine(Root, $"{id}.{version}.nupkg");
        string physical = id + ".test-rid";
        using (var zip = ZipFile.Open(pointer, ZipArchiveMode.Create))
        {
            WriteEntry(zip, $"{id}.nuspec", Nuspec(id, version, "FuncCliWorkload"));
            WriteEntry(zip, "workload.json",
                $$$"""{"$schema":"{{{WorkloadManifestSchema.PackageManifestV1Schema}}}","kind":"rid-pointer","packages":{"test-rid":"{{{physical}}}"}}""");
        }

        string implementation = Path.Combine(Root, $"{physical}.{version}.nupkg");
        using (var zip = ZipFile.Open(implementation, ZipArchiveMode.Create))
        {
            WriteEntry(zip, $"{physical}.nuspec", Nuspec(physical, version, WorkloadPackageTypes.RuntimeIdentifierPackage, "kind:content rid:test-rid"));
            WriteEntry(zip, "workload.json",
                $$"""{"$schema":"{{WorkloadManifestSchema.PackageManifestV1Schema}}","kind":"content","runtimeIdentifier":"test-rid"}""");
            WriteEntry(zip, "tools/any/fixture.txt", "Installation fixture, not an activated stack implementation.");
        }

        _archives[Key(id, version)] = pointer;
        _archives[Key(physical, version)] = implementation;
    }

    private static string Nuspec(string id, string version, string packageType, string tags = "")
        => $$"""
        <?xml version="1.0"?>
        <package><metadata><id>{{id}}</id><version>{{version}}</version><authors>fixture</authors>
        <description>Owned acquisition qualification fixture.</description><tags>{{tags}}</tags>
        <packageTypes><packageType name="{{packageType}}" /></packageTypes></metadata></package>
        """;

    private static void WriteEntry(ZipArchive archive, string path, string content)
    {
        using StreamWriter writer = new(archive.CreateEntry(path).Open());
        writer.Write(content);
    }

    private static string Key(string id, string version) => $"{id}/{version}";
    private static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();
}

internal sealed class FixtureCatalog(string json) : IBasicCompanionCatalog
{
    public string Json { get; set; } = json;
    public Task<BasicCompanionManifest> LoadAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(new BasicCompanionManifestReader().Read(Json));
    }
}

internal sealed class FixturePayloadSource(Dictionary<string, string> archives) : IBasicPackagePayloadSource
{
    public Action<CancellationToken>? Opening { get; set; }
    public bool Missing { get; set; }
    public List<string> Opened { get; } = [];
    public List<string> Requested { get; } = [];

    public Task<Stream> OpenAsync(BasicTemplateArtifact artifact, bool offline, CancellationToken cancellationToken)
    {
        Opening?.Invoke(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        if (Missing || offline)
        {
            throw new BasicAcquisitionException(BasicAcquisitionFailure.PayloadUnavailable, "The verified local fixture payload is unavailable.");
        }

        Opened.Add(artifact.Version.ToNormalizedString());
        Requested.Add($"{artifact.PackageId}/{artifact.Version.ToNormalizedString()}");
        return Task.FromResult<Stream>(File.OpenRead(archives[$"{artifact.PackageId}/{artifact.Version.ToNormalizedString()}"]));
    }
}

internal sealed class FixtureOwnership : IBasicTemplateOwnership
{
    public Dictionary<string, BasicTemplateOwner> Entries { get; } = [];
    public Dictionary<string, string> Digests { get; } = [];
    public Dictionary<string, BasicTemplateOwner> OtherEntries { get; } = [];
    public Task<BasicTemplateOwner?> GetAsync(string packageId, string version, string sha256, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(packageId.Equals(BasicAcquisitionFixture.BasicId, StringComparison.OrdinalIgnoreCase)
            && Digests.GetValueOrDefault(version) == sha256 ? Entries.GetValueOrDefault(version)
            : OtherEntries.GetValueOrDefault($"{packageId}/{version}/{sha256}"));
    }
}
