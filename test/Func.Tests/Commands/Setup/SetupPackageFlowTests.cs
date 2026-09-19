// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Text.Json;
using Azure.Functions.Cli.Bundles;
using Azure.Functions.Cli.Commands.Setup;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Configuration;
using Azure.Functions.Cli.Hosting.FirstRun;
using Azure.Functions.Cli.Profiles;
using Azure.Functions.Cli.Projects;
using Azure.Functions.Cli.Templates;
using Azure.Functions.Cli.Tests.Workloads.Catalog;
using Azure.Functions.Cli.Workers;
using Azure.Functions.Cli.Workloads;
using Azure.Functions.Cli.Workloads.Catalog;
using Azure.Functions.Cli.Workloads.Discovery;
using Azure.Functions.Cli.Workloads.Install;
using Azure.Functions.Cli.Workloads.Storage;
using Microsoft.Extensions.Options;
using Newtonsoft.Json.Linq;
using NSubstitute;
using NuGet.Common;
using NuGet.Packaging;
using NuGet.Packaging.Core;
using NuGet.Protocol;
using NuGet.Protocol.Core.Types;
using NuGet.Versioning;
using PackageSource = NuGet.Configuration.PackageSource;

namespace Azure.Functions.Cli.Tests.Commands.Setup;

public sealed class SetupPackageFlowTests : IDisposable
{
    private const string StackId = "contoso.node.stack";
    private const string TemplatesId = "contoso.node.templates";
    private const string FixtureAssembly = "Azure.Functions.Cli.Workloads.Tests.Fixtures.Default.dll";
    private readonly string _root = Directory.CreateTempSubdirectory("setup-package-flow-").FullName;
    private readonly CancellationTokenSource _cancellation = new();
    private readonly PackageSource _source = new("https://package-flow.test/v3/index.json") { ProtocolVersion = 3 };
    private readonly List<string> _archives = [];
    private readonly List<(string Id, string Version)> _downloads = [];
    private readonly List<Uri> _searches = [];
    private readonly IHostJsonBundleSectionReader _bundle = Substitute.For<IHostJsonBundleSectionReader>();
    private readonly IFirstRunStateStore _marker = Substitute.For<IFirstRunStateStore>();
    private readonly TestInteractionService _interaction = new();
    private readonly WorkloadPathsOptions _paths;
    private readonly WorkloadStore _store;
    private readonly WorkloadCatalog _catalog;
    private readonly WorkloadInstaller _installer;

    public SetupPackageFlowTests()
    {
        _paths = new WorkloadPathsOptions(Path.Combine(_root, "installed"));
        _store = new WorkloadStore(_paths);
        var find = Substitute.For<FindPackageByIdResource>();
        find.GetAllVersionsAsync(Arg.Any<string>(), Arg.Any<SourceCacheContext>(), Arg.Any<ILogger>(), Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult<IEnumerable<NuGetVersion>>([.. ReadArchives()
                .Where(package => package.Id.Equals(call.ArgAt<string>(0), StringComparison.OrdinalIgnoreCase))
                .Select(package => package.Version)]));
        find.CopyNupkgToStreamAsync(Arg.Any<string>(), Arg.Any<NuGetVersion>(), Arg.Any<Stream>(), Arg.Any<SourceCacheContext>(),
                Arg.Any<ILogger>(), Arg.Any<CancellationToken>())
            .Returns(async call =>
            {
                string id = call.ArgAt<string>(0);
                NuGetVersion version = call.ArgAt<NuGetVersion>(1);
                FeedPackage package = ReadArchives().Single(package => package.Id.Equals(id, StringComparison.OrdinalIgnoreCase)
                    && package.Version.Equals(version));
                _downloads.Add((id, version.ToNormalizedString()));
                await using FileStream bytes = File.OpenRead(package.Path);
                await bytes.CopyToAsync(call.ArgAt<Stream>(2), call.ArgAt<CancellationToken>(5));
                return true;
            });
        ServiceIndexResourceV3 index = new(JObject.Parse("""
            {"version":"3.0.0","resources":[{"@id":"https://package-flow.test/query","@type":"SearchQueryService"}]}
            """), DateTime.UnixEpoch);
        FeedClient client = new(TestRepository.Build(_source, index, find), Search);
        var options = Options.Create(new WorkloadCatalogOptions { Source = _source.Source, IncludePrerelease = false });
        _catalog = new WorkloadCatalog(options, new PackageSourceProvider(options), _ => client);
        var runtime = Substitute.For<IWorkloadRuntimeIdentifierProvider>();
        runtime.Current.Returns("win-x64");
        WorkloadRidPackageSelector selector = new(runtime);
        WorkloadMetadataReader metadata = new();
        WorkloadPackageInspector inspector = new(metadata, selector);
        _installer = new WorkloadInstaller(new WorkloadPackageSource(_catalog, inspector, options), inspector, selector,
            new WorkloadDeploymentService(_paths, _store, metadata));
    }

    private SetupRunner CreateRunner()
    {
        SetupStackCatalog stacks = new(_catalog);
        var profiles = Substitute.For<ISetupProfileScopeResolver>();
        profiles.ResolveProfileScopesAsync(Arg.Any<SetupCommandOptions>(), Arg.Any<SetupRenderer>(), Arg.Any<CancellationToken>())
            .Returns([SetupProfileScope.Unconstrained]);
        return new SetupRunner(_interaction,
            new SetupFeatureResolver(_interaction, _store, Substitute.For<ICliConfigurationProvider>(), stacks), profiles,
            new SetupDependencyPlanBuilder(_bundle, stacks), new SetupDependencyInstaller(_interaction, _store, _catalog, _installer), _marker);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Setup_ArchiveMetadataThroughInstalledConsumer_PreservesIdentityAndRepeatState(bool previewBundle)
    {
        PublishPrerequisites(previewBundle);
        Publish(TemplatesId, "1.2.0", WorkloadKind.Content, "kind:content alias:node-templates");
        string bundleId = previewBundle ? BundleHelpers.PreviewBundleId : BundleHelpers.StableBundleId;
        _bundle.ReadAsync(Arg.Any<DirectoryInfo>(), _cancellation.Token).Returns(new HostJsonBundleSection(bundleId, "[4.0.0, 5.0.0)"));

        SetupRunResult first = await CreateRunner().RunAsync(Request(), _cancellation.Token);

        first.ExitCode.Should().Be(0, _interaction.AllOutput);
        IReadOnlyList<WorkloadEntry> installed = await _store.GetWorkloadsAsync(_cancellation.Token);
        installed.Select(entry => entry.PackageId).Should().BeEquivalentTo([
            HostWorkloadPackage.PackageId.ToLowerInvariant(), StackId, "azure.functions.cli.workloads.workers.node",
            TemplatesId, IInstalledBundleWorkloads.BundleWorkloadPackageId.ToLowerInvariant()]);
        installed.Should().ContainSingle(entry => entry.PackageId == StackId && entry.Kind == WorkloadKind.Workload);
        installed.Should().ContainSingle(entry => entry.PackageId == TemplatesId && entry.Kind == WorkloadKind.Content
            && entry.Aliases.Contains("node-templates"));
        string content = Path.Combine(_paths.GetInstallDirectory(TemplatesId, "1.2.0"), "tools", "any", "content", "payload.txt");
        (await File.ReadAllTextAsync(content, _cancellation.Token)).Should().Be($"{TemplatesId}:1.2.0");
        _searches.Select(uri => Query(uri)["prerelease"]).Should().Equal(previewBundle ? ["false", "true"] : ["false"]);
        _searches.Should().OnlyContain(uri => Query(uri)["packageType"] == WorkloadPackageTypes.Workload && Query(uri)["q"] == "");

        NewCommandResolutionResult context = await ResolveNewContextAsync();
        context.Failure.Should().BeNull();
        context.Context!.Workload.InstallDirectory.Should().Be(_paths.GetInstallDirectory(TemplatesId, "1.2.0"));
        context.Context.UsedStableFallback.Should().Be(previewBundle);
        byte[] registryBefore = await File.ReadAllBytesAsync(_paths.WorkloadRegistryPath, _cancellation.Token);
        int downloadsBefore = _downloads.Count;

        SetupRunResult repeated = await CreateRunner().RunAsync(Request(), _cancellation.Token);

        repeated.ExitCode.Should().Be(0);
        _downloads.Count.Should().Be(downloadsBefore);
        (await File.ReadAllBytesAsync(_paths.WorkloadRegistryPath, _cancellation.Token)).Should().Equal(registryBefore);
        await _marker.Received(2).MarkCompleteAsync(_cancellation.Token);
    }

    [Fact]
    public async Task Setup_SelectedArchiveDoesNotClaimDiscoveredAlias_FailsWithoutReportingReady()
    {
        PublishPrerequisites(previewBundle: true);
        Publish(TemplatesId, "1.0.0-preview.1", WorkloadKind.Content, "kind:content alias:java-templates");
        Publish(TemplatesId, "2.0.0-experimental.1", WorkloadKind.Content, "kind:content alias:node-templates");
        _bundle.ReadAsync(Arg.Any<DirectoryInfo>(), _cancellation.Token)
            .Returns(new HostJsonBundleSection(BundleHelpers.PreviewBundleId, "[4.0.0, 5.0.0)"));

        SetupRunResult result = await CreateRunner().RunAsync(Request(), _cancellation.Token);

        result.ExitCode.Should().Be(1);
        _interaction.AllOutput.Should().Contain("does not claim 'node-templates'").And.Contain("No rollback was performed");
        _downloads.Should().Contain((TemplatesId, "1.0.0-preview.1")).And.NotContain((TemplatesId, "2.0.0-experimental.1"));
        (await _store.GetWorkloadsAsync(_cancellation.Token)).Should().ContainSingle(entry => entry.PackageId == TemplatesId
            && entry.PackageVersion == "1.0.0-preview.1" && entry.Aliases.Contains("java-templates"));
        await _marker.DidNotReceive().MarkCompleteAsync(Arg.Any<CancellationToken>());
        NewCommandResolutionResult context = await ResolveNewContextAsync();
        context.Context.Should().BeNull();
        context.Failure!.Kind.Should().Be(NewCommandResolutionFailureKind.NoTemplatesWorkloadForChannel);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Setup_FeedChangesTemplatesOwner_PreservesInstalledConsumerWithoutDownloadingReplacement(bool conventionalTarget)
    {
        string conventionalId = TemplatesWorkloadConstants.GetPackageId("node").ToLowerInvariant();
        string incumbentId = conventionalTarget ? TemplatesId : conventionalId;
        string replacementId = conventionalTarget ? conventionalId : TemplatesId;
        PublishPrerequisites(previewBundle: false);
        Publish(incumbentId, "1.0.0", WorkloadKind.Content, "kind:content alias:node-templates");
        _bundle.ReadAsync(Arg.Any<DirectoryInfo>(), _cancellation.Token)
            .Returns(new HostJsonBundleSection(BundleHelpers.StableBundleId, "[4.0.0, 5.0.0)"));
        (await CreateRunner().RunAsync(Request(), _cancellation.Token)).ExitCode.Should().Be(0, _interaction.AllOutput);
        byte[] registryBefore = await File.ReadAllBytesAsync(_paths.WorkloadRegistryPath, _cancellation.Token);
        int downloadsBefore = _downloads.Count;
        _archives.Clear();
        PublishPrerequisites(previewBundle: false);
        Publish(replacementId, "2.0.0", WorkloadKind.Content, "kind:content alias:node-templates");

        SetupRunResult result = await CreateRunner().RunAsync(Request(), _cancellation.Token);

        result.ExitCode.Should().Be(1);
        _interaction.AllOutput.Should().Contain("Explicit migration");
        _downloads.Count.Should().Be(downloadsBefore);
        (await File.ReadAllBytesAsync(_paths.WorkloadRegistryPath, _cancellation.Token)).Should().Equal(registryBefore);
        NewCommandResolutionResult context = await ResolveNewContextAsync();
        context.Failure.Should().BeNull();
        context.Context!.Workload.InstallDirectory.Should().Be(_paths.GetInstallDirectory(incumbentId, "1.0.0"));
        await _marker.Received(1).MarkCompleteAsync(_cancellation.Token);
    }

    private void PublishPrerequisites(bool previewBundle)
    {
        Publish(HostWorkloadPackage.PackageId, "4.1.0", WorkloadKind.Content, "kind:content alias:host");
        Publish(StackId, "1.0.0", WorkloadKind.Workload, "kind:workload alias:nodealias alias:node stack:node");
        Publish("Azure.Functions.Cli.Workloads.Workers.Node", "1.0.0", WorkloadKind.Content, "kind:content alias:node-worker");
        Publish(IInstalledBundleWorkloads.BundleWorkloadPackageId, previewBundle ? "4.10.0-preview.1" : "4.10.0",
            WorkloadKind.Content, "kind:content alias:bundles");
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Setup_NormalScanConflictHiddenByNewerPrerelease_IsNotErasedBySupplementalDiscovery(bool conflict, bool check)
    {
        PublishPrerequisites(previewBundle: true);
        Publish(TemplatesId, "1.0.0", WorkloadKind.Content, "kind:content alias:node-templates");
        Publish("contoso.competing", "1.0.0", WorkloadKind.Workload, $"kind:workload alias:{(conflict ? "node-templates" : "unrelated")}");
        Publish("contoso.competing", "2.0.0-experimental.1", WorkloadKind.Workload, "kind:workload alias:unrelated");
        _bundle.ReadAsync(Arg.Any<DirectoryInfo>(), _cancellation.Token)
            .Returns(new HostJsonBundleSection(BundleHelpers.PreviewBundleId, "[4.0.0, 5.0.0)"));

        SetupRunResult result = await CreateRunner().RunAsync(Request() with { Check = check }, _cancellation.Token);

        result.ExitCode.Should().Be(conflict || check ? 1 : 0);
        if (conflict)
        {
            _interaction.AllOutput.Should().Contain("alias collision").And.Contain(TemplatesId).And.Contain("contoso.competing");
        }
        else
        {
            _interaction.AllOutput.Should().NotContain("alias collision");
            if (check)
            {
                _interaction.AllOutput.Should().Contain("node worker 1.0.0 is not installed")
                    .And.Contain("node stack 1.0.0 is not installed")
                    .And.Contain("node templates 1.0.0 is not installed");
            }
        }

        IReadOnlyList<WorkloadEntry> installed = await _store.GetWorkloadsAsync(_cancellation.Token);
        if (conflict)
        {
            string[] rejectedRuntimePackages = [StackId, "azure.functions.cli.workloads.workers.node", TemplatesId];
            _downloads.Should().NotContain(download => rejectedRuntimePackages.Contains(download.Id, StringComparer.OrdinalIgnoreCase));
            installed.Should().NotContain(entry => rejectedRuntimePackages.Contains(entry.PackageId, StringComparer.OrdinalIgnoreCase));
        }

        if (conflict || check)
        {
            _downloads.Should().BeEmpty();
            installed.Should().BeEmpty();
            await _marker.DidNotReceive().MarkCompleteAsync(Arg.Any<CancellationToken>());
        }
        else
        {
            _downloads.Should().Contain((TemplatesId, "1.0.0"));
        }
    }

    private SetupCommandOptions Request()
        => new(new DirectoryInfo(_root), ["nodealias"], [], _source.Source, SetupInstallPolicy.IfNeeded,
            false, true, true, false, SetupOutputMode.Plain);

    private async Task<NewCommandResolutionResult> ResolveNewContextAsync()
    {
        WorkingDirectory directory = new(new DirectoryInfo(_root), false);
        var projects = Substitute.For<IFunctionsProjectResolver>();
        projects.ResolveProjectAsync(Arg.Any<ProjectResolutionContext>(), _cancellation.Token)
            .Returns(ProjectResolutionResults.Resolved(new NodeProject(directory), "fixture"));
        var profiles = Substitute.For<IProfileResolver>();
        profiles.ResolveAsync(Arg.Any<ProfileResolutionContext>(), _cancellation.Token).Returns(new ProfileResolution.None([]));
        var options = Substitute.For<IOptionsMonitor<StackOptions>>();
        options.Get(Arg.Any<string>()).Returns(new StackOptions { Runtime = "node", Language = "javascript" });
        NewCommandContextResolver resolver = new(_interaction, projects, profiles, options, [],
            new InstalledTemplatesWorkloads(_store, _paths), _bundle);
        return await resolver.ResolveAsync(new NewInvocation(directory, null, null, false, true), _cancellation.Token);
    }

    private void Publish(string id, string version, WorkloadKind kind, string tags)
    {
        string payload = Path.Combine(_root, Guid.NewGuid().ToString("N"));
        File.WriteAllText(payload, $"{id}:{version}");
        WorkloadMetadata manifest = new()
        {
            Schema = WorkloadManifestSchema.PackageManifestV1Schema,
            Kind = kind,
            EntryPoint = kind == WorkloadKind.Workload ? new EntryPointSpec
            {
                AssemblyPath = FixtureAssembly, Type = "Azure.Functions.Cli.Workloads.Tests.Fixtures.Default.StubWorkload",
            } : null,
        };
        string metadata = payload + ".json";
        File.WriteAllText(metadata, JsonSerializer.Serialize(manifest, WorkloadJsonContext.Default.WorkloadMetadata));
        PackageBuilder package = new() { Id = id, Version = NuGetVersion.Parse(version), Description = "Setup package-flow fixture." };
        package.Authors.Add("test");
        package.Tags.AddRange(tags.Split(' '));
        package.PackageTypes.Add(new PackageType(WorkloadPackageTypes.Workload, new Version(0, 0)));
        package.Files.Add(new PhysicalPackageFile { SourcePath = metadata, TargetPath = "workload.json" });
        package.Files.Add(new PhysicalPackageFile { SourcePath = payload, TargetPath = "tools/any/content/payload.txt" });
        if (kind == WorkloadKind.Workload)
            package.Files.Add(new PhysicalPackageFile
            {
                SourcePath = Path.Combine(AppContext.BaseDirectory, "tools", "any", FixtureAssembly), TargetPath = $"tools/any/{FixtureAssembly}",
            });
        string archive = payload + ".nupkg";
        using (FileStream bytes = File.Create(archive)) package.Save(bytes);
        _archives.Add(archive);
    }

    private IReadOnlyList<FeedPackage> ReadArchives()
        => [.. _archives.Select(path =>
        {
            using PackageArchiveReader reader = new(path);
            NuspecReader nuspec = reader.NuspecReader;
            return new FeedPackage(path, nuspec.GetId(), nuspec.GetVersion(), nuspec.GetTags(),
                [.. nuspec.GetPackageTypes().Select(type => type.Name)]);
        })];

    private JObject Search(Uri uri)
    {
        _searches.Add(uri);
        Dictionary<string, string> query = Query(uri);
        bool prerelease = bool.Parse(query["prerelease"]);
        FeedPackage[] visible = [.. ReadArchives().Where(package => prerelease || !package.Version.IsPrerelease)
            .GroupBy(package => package.Id, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.OrderByDescending(package => package.Version).First())
            .OrderBy(package => package.Id, StringComparer.OrdinalIgnoreCase)];
        FeedPackage[] page = [.. visible.Skip(int.Parse(query["skip"])).Take(int.Parse(query["take"]))];
        return new JObject
        {
            ["totalHits"] = visible.Length,
            ["data"] = new JArray(page.Select(package => new JObject
            {
                ["id"] = package.Id, ["version"] = package.Version.ToNormalizedString(), ["tags"] = package.Tags,
                ["packageTypes"] = new JArray(package.Types.Select(type => new JObject { ["name"] = type })),
            })),
        };
    }

    private static Dictionary<string, string> Query(Uri uri)
        => uri.Query.TrimStart('?').Split('&').Select(part => part.Split('=', 2))
            .ToDictionary(part => part[0], part => Uri.UnescapeDataString(part[1]));

    public void Dispose()
    {
        _cancellation.Dispose();
        Directory.Delete(_root, recursive: true);
    }

    private sealed record FeedPackage(string Path, string Id, NuGetVersion Version, string Tags, IReadOnlyList<string> Types);

    private sealed class FeedClient(SourceRepository repository, Func<Uri, JObject> search) : NuGetProtocolSourceClient(repository)
    {
        internal override Task<JObject?> FetchSearchResponseAsync(Uri searchUri, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult<JObject?>(search(searchUri));
        }
    }

    private sealed class NodeProject(WorkingDirectory directory) : FunctionsProject
    {
        public override WorkingDirectory WorkingDirectory { get; } = directory;
        public override string StackName => "node";
        public override string StackDisplayName => "Node";
        public override bool SupportsExtensionBundles => true;
        public override FunctionsWorkerReference WorkerReference { get; } = FunctionsWorkerReference.FromWorkerInfo("node", "node", "worker.config.json", "1.0.0");
    }
}