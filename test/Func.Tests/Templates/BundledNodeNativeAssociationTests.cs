// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using Azure.Functions.Cli.Commands;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Projects;
using Azure.Functions.Cli.Templates;
using Azure.Functions.Cli.Templates.Acquisition;
using Azure.Functions.Cli.Templates.Engine;
using Azure.Functions.Cli.Tests.Templates.Acquisition;
using Azure.Functions.Cli.Workloads.Catalog;
using Azure.Functions.Cli.Workloads.Install;
using Azure.Functions.Cli.Workloads.Storage;
using NSubstitute;
using NuGet.Packaging;
using NuGet.Versioning;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Azure.Functions.Cli.Tests.Templates;

public sealed partial class BundledNodeTemplateTests
{
    [Fact]
    public async Task NativeAssociationCheck_RealBundleReady_MissingExternalContentRemainsIncomplete()
    {
        var entry = await InstallAssociationStackAsync();
        using var external = new BasicAcquisitionFixture();
        var (runner, request) = NativeAssociationRunner(external, entry);
        var before = NativeState();
        string registry = File.ReadAllText(_paths.WorkloadRegistryPath);

        var result = await runner.RunAsync(request with { Check = true }, CancellationToken.None);

        result.Status.Should().Be(BasicAcquisitionStatus.Incomplete);
        result.Completed.Should().ContainSingle().Which.PackageId.Should().Be(entry.PackageId);
        external.Payload.Opened.Should().BeEmpty();
        NativeState().OrderBy(pair => pair.Key, StringComparer.Ordinal).Should()
            .Equal(before.OrderBy(pair => pair.Key, StringComparer.Ordinal));
        File.ReadAllText(_paths.WorkloadRegistryPath).Should().Be(registry);
    }

    [Fact]
    public async Task NativeAssociation_RealBundleAndIndependentNativeArchive_AcquiresThenChecksAndReusesActualOwnedBytes()
    {
        var entry = await InstallAssociationStackAsync();
        using var external = new BasicAcquisitionFixture();
        var (runner, request) = NativeAssociationRunner(external, entry);
        string registry = File.ReadAllText(_paths.WorkloadRegistryPath);

        var acquired = await runner.RunAsync(request, CancellationToken.None);
        var beforeRepeat = NativeState();
        File.Delete(external.Archive());
        var repeated = await runner.RunAsync(request, CancellationToken.None);
        var check = await runner.RunAsync(request with { Check = true }, CancellationToken.None);

        acquired.Status.Should().Be(BasicAcquisitionStatus.Completed);
        acquired.Completed.Should().HaveCount(2);
        acquired.Completed[1].PackageId.Should().Be(BasicAcquisitionFixture.BasicId);
        acquired.Completed[1].Reused.Should().BeFalse();
        repeated.Status.Should().Be(BasicAcquisitionStatus.Completed);
        repeated.Completed.Should().OnlyContain(step => step.Reused);
        check.Status.Should().Be(BasicAcquisitionStatus.Completed);
        check.Completed.Should().HaveCount(2);
        external.Payload.Opened.Should().Equal("1.0.0");
        File.ReadAllText(_paths.WorkloadRegistryPath).Should().Be(registry);
        NativeState().OrderBy(pair => pair.Key, StringComparer.Ordinal).Should()
            .Equal(beforeRepeat.OrderBy(pair => pair.Key, StringComparer.Ordinal));
        using var reader = await _templates.OpenReadAsync(new(WorkingDirectory.FromExplicit(_root)), CancellationToken.None);
        (await reader.PackageManager.GetManagedTemplatePackagesAsync(false, CancellationToken.None)).Should().HaveCount(2);
        _source.ReceivedCalls().Count(call => call.GetMethodInfo().Name == "DownloadAsync").Should().Be(1);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NativeAssociation_PendingNativeRecovery_DoesNotRecoverDuringPrivatePreparation(bool check)
    {
        var entry = await InstallAssociationStackAsync();
        using var external = new BasicAcquisitionFixture();
        var (runner, request) = NativeAssociationRunner(external, entry);
        SeedPendingNativeRecovery();
        var before = NativeState();

        var result = await runner.RunAsync(request with { Check = check }, CancellationToken.None);

        result.Status.Should().Be(BasicAcquisitionStatus.Rejected);
        result.Failure.Should().Be(BasicAcquisitionFailure.RecoveryRequired);
        result.Completed.Should().BeEmpty();
        external.Payload.Opened.Should().BeEmpty();
        NativeState().OrderBy(pair => pair.Key, StringComparer.Ordinal).Should()
            .Equal(before.OrderBy(pair => pair.Key, StringComparer.Ordinal));
    }

    [Fact]
    public async Task NativeAssociation_SelectedSourceChangesDuringPrivatePreparation_RejectsBeforeNativeAdmission()
    {
        var entry = await InstallAssociationStackAsync();
        using var external = new BasicAcquisitionFixture();
        var gate = new NativeAssociationPayloadGate(external.Payload);
        var (runner, request) = NativeAssociationRunner(external, entry, gate);
        var before = NativeState();
        Task<BasicAcquisitionResult> pending = runner.RunAsync(request, CancellationToken.None);
        await gate.Entered.Task.WaitAsync(TimeSpan.FromSeconds(30));
        var document = JsonSerializer.SerializeToNode(entry, WorkloadJsonContext.Default.WorkloadEntry)!;
        document["source"] = "https://changed.invalid/v3/index.json";
        var changed = JsonSerializer.Deserialize(document, WorkloadJsonContext.Default.WorkloadEntry)!;
        await _workloads.SaveWorkloadAsync(changed);
        gate.Continue.TrySetResult();

        var result = await pending.WaitAsync(TimeSpan.FromSeconds(30));

        result.Status.Should().Be(BasicAcquisitionStatus.Rejected);
        result.Failure.Should().Be(BasicAcquisitionFailure.PlanChanged);
        result.Completed.Should().BeEmpty();
        NativeState().OrderBy(pair => pair.Key, StringComparer.Ordinal).Should()
            .Equal(before.OrderBy(pair => pair.Key, StringComparer.Ordinal));
        (await _workloads.GetWorkloadsAsync()).Should().ContainSingle().Which.Source.Should().Be(changed.Source);
    }

    [Fact]
    public async Task NativeAssociation_CanceledPrivatePreparation_DoesNotGuessACompletedSelectedStack()
    {
        var entry = await InstallAssociationStackAsync();
        using var external = new BasicAcquisitionFixture();
        var gate = new NativeAssociationPayloadGate(external.Payload);
        var (runner, request) = NativeAssociationRunner(external, entry, gate);
        var before = NativeState();
        string registry = File.ReadAllText(_paths.WorkloadRegistryPath);
        using var cancellation = new CancellationTokenSource();
        Task<BasicAcquisitionResult> pending = runner.RunAsync(request, cancellation.Token);
        await gate.Entered.Task.WaitAsync(TimeSpan.FromSeconds(30));
        cancellation.Cancel();

        var result = await pending.WaitAsync(TimeSpan.FromSeconds(30));

        result.Status.Should().Be(BasicAcquisitionStatus.Canceled);
        result.Completed.Should().BeEmpty();
        NativeState().OrderBy(pair => pair.Key, StringComparer.Ordinal).Should()
            .Equal(before.OrderBy(pair => pair.Key, StringComparer.Ordinal));
        File.ReadAllText(_paths.WorkloadRegistryPath).Should().Be(registry);
    }

    [Fact]
    public async Task NativeAssociation_LocalArchiveOrigin_DoesNotBecomeCatalogFeedAuthority()
    {
        var entry = (await _installer.InstallFromPackageAsync(BuiltArchive)).Entry;
        using var external = new BasicAcquisitionFixture();
        var (runner, request) = NativeAssociationRunner(external, entry);

        var result = await runner.RunAsync(request, CancellationToken.None);

        result.Failure.Should().Be(BasicAcquisitionFailure.SourceConflict);
        result.Completed.Should().BeEmpty();
        external.Payload.Opened.Should().BeEmpty();
    }

    [Fact]
    public async Task NativeAssociation_AdditionalProjectArchive_CreatesThroughActualSelectedNodeBinding()
    {
        var entry = await InstallAssociationStackAsync();
        using var external = new BasicAcquisitionFixture();
        var additional = AddNativeProjectFixture(external);
        IBasicCompanionCatalog? selectedCatalog = null;
        var (runner, request) = NativeAssociationRunner(external, entry, additionalProject: additional,
            captureCatalog: catalog => selectedCatalog = catalog);

        var result = await runner.RunAsync(request, CancellationToken.None);

        result.Status.Should().Be(BasicAcquisitionStatus.Completed);
        result.Completed.Should().HaveCount(3);
        result.Completed[2].PackageId.Should().Be(additional.PackageId);
        result.Completed[2].Version.Should().Be("3.0.0");
        selectedCatalog.Should().NotBeNull();
        using var provider = await CreateServicesAsync(nativeCatalog: selectedCatalog);
        using var scope = provider.CreateScope();
        var availability = new TemplateConstraintEvaluationSnapshot([new(entry.PackageId,
            NuGetVersion.Parse(entry.PackageVersion), entry.Aliases, true)]);
        var command = new InitExecutionCommand(scope.ServiceProvider.GetRequiredService<IInitExecutionRunner>(),
            provider.GetRequiredService<InitExecutionRenderer>(), availability);
        string project = Path.Combine(_root, "external-project");
        string[] arguments = [project, "--template", "fixture-additional-project", "--stack", "node", "--language", "js", "--name", "Independent"];
        (await command.ParseInvocation([.. arguments, "--dry-run"]).InvokeAsync()).Should().Be(0, _interaction.AllOutput);
        Directory.Exists(project).Should().BeFalse();
        (await command.ParseInvocation(arguments).InvokeAsync()).Should().Be(0, _interaction.AllOutput);
        using var configuration = JsonDocument.Parse(File.ReadAllText(Path.Combine(project, ".func", "config.json")));
        configuration.RootElement.GetProperty("stack").GetProperty("runtime").GetString().Should().Be("node");
        configuration.RootElement.GetProperty("stack").GetProperty("language").GetString().Should().Be("JavaScript");
        File.ReadAllText(Path.Combine(project, "package.json")).Should().Contain("\"name\": \"independent\"");
    }

    private BasicTemplateArtifact AddNativeProjectFixture(BasicAcquisitionFixture external)
    {
        const string id = "Fixture.Node.AdditionalProject";
        const string identity = "Fixture.Node.AdditionalProject.JavaScript";
        string path = Path.Combine(external.Root, id + ".3.0.0.nupkg");
        using (var outer = new PackageArchiveReader(BuiltArchive))
        using (var bytes = new MemoryStream())
        {
            outer.GetStream("tools/any/templates/node-basic.nupkg").CopyTo(bytes);
            bytes.Position = 0;
            using var source = new ZipArchive(bytes, ZipArchiveMode.Read);
            using var target = ZipFile.Open(path, ZipArchiveMode.Create);
            foreach (var sourceEntry in source.Entries.Where(item =>
                item.FullName.StartsWith("content/project-js/", StringComparison.Ordinal) || item.FullName == "LICENSE"))
            {
                using var input = sourceEntry.Open();
                using var output = target.CreateEntry(sourceEntry.FullName).Open();
                if (sourceEntry.FullName == "content/project-js/.template.config/template.json")
                {
                    var configuration = JsonNode.Parse(input)!;
                    configuration["identity"] = identity;
                    configuration["groupIdentity"] = "Fixture.Node.AdditionalProject";
                    configuration["shortName"] = new JsonArray("fixture-additional-project");
                    using var writer = new StreamWriter(output);
                    writer.Write(configuration.ToJsonString());
                }
                else
                {
                    input.CopyTo(output);
                }
            }
            using var metadata = new StreamWriter(target.CreateEntry(id + ".nuspec").Open());
            metadata.Write($$"""
                <?xml version="1.0"?>
                <package><metadata><id>{{id}}</id><version>3.0.0</version><authors>fixture</authors>
                <description>Scoped owned Node project fixture, not a production release.</description>
                <license type="expression">MIT</license><packageTypes><packageType name="FuncTemplate" /></packageTypes>
                </metadata></package>
                """);
        }
        BasicTemplateArtifact artifact = new(id, NuGetVersion.Parse("3.0.0"), BasicAcquisitionFixture.Source,
            Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path))), "owned-node-project-fixture",
            [new("content/project-js/.template.config/template.json", identity, "project")])
        {
            ContentRoles = [new(identity, NativeTemplateContentRole.AdditionalProject)],
        };
        external.AddNativeArtifact(artifact, path);
        return artifact;
    }

    private async Task<WorkloadEntry> InstallAssociationStackAsync()
    {
        using var archive = new PackageArchiveReader(BuiltArchive);
        string id = archive.NuspecReader.GetId();
        NuGetVersion version = archive.NuspecReader.GetVersion();
        _source.ResolveAsync(id, version, BasicAcquisitionFixture.Source, true, true, Arg.Any<CancellationToken>())
            .Returns(new ResolvedPackage(id, version, new(BasicAcquisitionFixture.Source)));
        _source.DownloadAsync(Arg.Any<ResolvedPackage>(), Arg.Any<IProgress<WorkloadInstallProgress>?>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                string path = Path.Combine(_root, Guid.NewGuid().ToString("N") + ".nupkg");
                File.Copy(BuiltArchive, path);
                return new TemporaryWorkloadPackageFile(path);
            });
        return (await _installer.InstallFromCatalogAsync(id, version, BasicAcquisitionFixture.Source, true, true, false)).Entry;
    }

    private (BasicAcquisitionRunner Runner, BasicAcquisitionRequest Request) NativeAssociationRunner(
        BasicAcquisitionFixture external, WorkloadEntry entry, IBasicPackagePayloadSource? payload = null,
        BasicTemplateArtifact? additionalProject = null, IBasicCompanionCatalog? authority = null,
        IPackageSourceProvider? sources = null, INativeTemplateOwnership? ownership = null,
        Action<IBasicCompanionCatalog>? captureCatalog = null)
    {
        string id = entry.LogicalPackage?.PackageId ?? entry.PackageId;
        string version = entry.LogicalPackage?.PackageVersion ?? entry.PackageVersion;
        string hostVersion = AssemblyCliVersionProvider.Instance.Version;
        var document = JsonNode.Parse(external.Catalog.Json)!;
        document["schemaVersion"] = 2;
        document["stacks"] = new JsonArray(new JsonObject
        {
            ["packageId"] = id,
            ["versions"] = $"[{version}]",
            ["sources"] = new JsonArray(BasicAcquisitionFixture.Source),
            ["hostIdentifier"] = "func",
            ["engineHostVersions"] = $"[{hostVersion}]",
            ["channels"] = new JsonArray("stable"),
            ["companions"] = new JsonArray(new JsonObject
            {
                ["packageId"] = BasicAcquisitionFixture.BasicId,
                ["versions"] = "[1.0.0,2.0.0)",
                ["requiredRoles"] = new JsonArray("function"),
            }),
        });
        if (additionalProject is not null)
        {
            document["stacks"]![0]!["companions"]!.AsArray().Add(new JsonObject
            {
                ["packageId"] = additionalProject.PackageId,
                ["versions"] = $"[{additionalProject.Version.ToNormalizedString()}]",
                ["requiredRoles"] = new JsonArray("additional-project"),
            });
        }
        foreach (var artifact in document["artifacts"]!.AsArray())
        {
            artifact!["basicOnly"] = false;
            foreach (var definition in artifact["templates"]!.AsArray())
            {
                if (definition!["contentRole"] is null)
                {
                    definition["contentRole"] = "function";
                }
            }
        }
        var catalog = new FixtureCatalog(document.ToJsonString());
        captureCatalog?.Invoke(authority ?? catalog);
        INativeTemplateOwnership owner = ownership ?? new NativeTemplateOwnership(_files, new(Home));
        var lifecycle = new BasicTemplateLifecycle(_templates, payload ?? external.Payload, new BasicArtifactInspector(_files), owner, _files);
        var coordinator = new FileWorkloadOperationCoordinator(_paths);
        sources ??= new PackageSourceProvider(Options.Create(new WorkloadCatalogOptions { Source = BasicAcquisitionFixture.Source }));
        BasicAcquisitionRunner runner = new(authority ?? catalog, new(), sources, _workloads, _paths, _installer,
            lifecycle, _files, coordinator, AdmissionDeployment(coordinator));
        BasicAcquisitionRequest request = new(id, NuGetVersion.Parse(version), WorkingDirectory.FromExplicit(_root),
            BasicAcquisitionFixture.Source)
        {
            NativeContext = new(entry, "func", NuGetVersion.Parse(hostVersion), BundleChannel.Stable, [NativeTemplateContentRole.Function]),
        };
        return (runner, request);
    }

    private sealed class NativeAssociationPayloadGate(IBasicPackagePayloadSource inner) : IBasicPackagePayloadSource
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Continue { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<Stream> OpenAsync(BasicTemplateArtifact artifact, bool offline, CancellationToken cancellationToken)
        {
            Entered.TrySetResult();
            await Continue.Task.WaitAsync(cancellationToken);
            return await inner.OpenAsync(artifact, offline, cancellationToken);
        }
    }
}
