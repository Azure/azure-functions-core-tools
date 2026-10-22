// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Configuration;
using Azure.Functions.Cli.Hosting;
using Azure.Functions.Cli.Projects;
using Azure.Functions.Cli.Quickstart;
using Azure.Functions.Cli.Templates;
using Azure.Functions.Cli.Templates.Acquisition;
using Azure.Functions.Cli.Templates.Engine;
using Azure.Functions.Cli.Tests.Console;
using Azure.Functions.Cli.Tests.Templates.Acquisition;
using Azure.Functions.Cli.Tests.Templates.Engine;
using Azure.Functions.Cli.Workloads;
using Azure.Functions.Cli.Workloads.Storage;
using Azure.Functions.Cli.Workloads.Discovery;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NuGet.Packaging;
using NuGet.Versioning;

namespace Azure.Functions.Cli.Tests.Templates;

public sealed partial class ArtifactBindingJourneyTests : IDisposable
{
    private const string Identity = "Fixture.Exact.Project";
    private const string PackageId = "Fixture.Exact.Templates";
    private const string Source = "https://fixtures.invalid/v3/index.json";
    private readonly string _root = Directory.CreateTempSubdirectory("func-artifact-binding-").FullName;
    private readonly PhysicalFileSystem _files = new();
    private readonly TestInteractionService _interaction = new();
    private readonly WorkloadPathsOptions _paths;
    private readonly WorkloadStore _workloads;
    private readonly TemplateStore _templates;
    private readonly NativeTemplateOwnership _ownership;
    private readonly FixtureCatalog _catalog;
    private readonly NativeTemplateArtifactVerifier _verifier;
    private readonly BoundInitScopeFactory _scopes;
    private NativeTemplateSelection _selection = null!;

    public ArtifactBindingJourneyTests()
    {
        _paths = new(Path.Combine(_root, "workloads"));
        _workloads = new(_paths, new FileWorkloadOperationCoordinator(_paths));
        var cliPaths = new CliConfigurationPathsOptions(Path.Combine(_root, "cli"));
        _templates = new(cliPaths, _files, new FileTemplateStoreLock(), NullLoggerFactory.Instance);
        _ownership = new(_files, cliPaths);
        _catalog = new("{}");
        _verifier = new(_catalog, new(), _ownership, _workloads, new BasicArtifactInspector(_files));
        _scopes = new(_paths, _workloads, _interaction, _files, _verifier);
    }

    private string Target => Path.Combine(_root, "target");
    private WorkingDirectory DirectoryTarget => WorkingDirectory.FromExplicit(Target);
    private TemplateEngineContext Context => new(DirectoryTarget) { RequireCleanStore = true };
    private TemplaterFactory Factory => new(_templates, _files);

    [Fact]
    public async Task BoundInit_ActualPostAcquisitionScope_PreviewsThenCreatesOnlyExactVariantAndConfiguration()
    {
        using BoundInitScope stale = await _scopes.CreateAsync(CancellationToken.None);
        stale.Catalog.Stacks.Should().BeEmpty();
        await SeedAsync();
        var before = HiveBytes();
        var runner = Runner();

        var preview = await runner.ExecuteAsync(Request(dryRun: true), stale.Availability, CancellationToken.None);

        preview.Kind.Should().Be("previewed");
        preview.SelectedTemplate!.Identity.Should().Be(Identity, "the higher-precedence sibling with the same short name is not the authorized full identity");
        preview.Context!.RequireCleanStore.Should().BeTrue();
        preview.Preview!.ProjectConfigurations.Should().ContainSingle().Which.Stack.Should().Be("node");
        preview.Preview.Changes.Should().Contain(change => change.Path.Replace('\\', '/') == ".func/config.json");
        Directory.Exists(Target).Should().BeFalse();
        HiveBytes().Should().BeEquivalentTo(before);

        var result = await runner.ExecuteAsync(Request(), stale.Availability, CancellationToken.None);

        result.Kind.Should().Be("created");
        result.Invocation!.HasCommittedOutput.Should().BeTrue();
        File.ReadAllText(Path.Combine(Target, "value.txt")).Should().Be("exact");
        using var configuration = JsonDocument.Parse(File.ReadAllText(Path.Combine(Target, ".func", "config.json")));
        configuration.RootElement.GetProperty("stack").GetProperty("runtime").GetString().Should().Be("node");
        configuration.RootElement.GetProperty("stack").GetProperty("language").GetString().Should().Be("JavaScript");
        _interaction.AllOutput.Should().NotContain("SELECT:");
    }

    [Theory]
    [InlineData(BundleChannel.Preview)]
    [InlineData(BundleChannel.Experimental)]
    public async Task BoundInit_QualifiedPrereleaseChannel_PreviewsAndCreatesExactArtifact(BundleChannel channel)
    {
        await SeedAsync(channel: channel);
        var runner = Runner();

        var preview = await runner.ExecuteAsync(Request(dryRun: true), new([]), CancellationToken.None);

        preview.Kind.Should().Be("previewed");
        preview.SelectedTemplate!.Identity.Should().Be(Identity);
        preview.SelectedTemplate.Package!.Version.Should().Be(_selection.Artifact.Version);
        Directory.Exists(Target).Should().BeFalse();

        var result = await runner.ExecuteAsync(Request(), new([]), CancellationToken.None);

        result.Kind.Should().Be("created");
        File.ReadAllText(Path.Combine(Target, "value.txt")).Should().Be("exact");
    }

    [Theory]
    [InlineData(BundleChannel.Preview)]
    [InlineData(BundleChannel.Experimental)]
    public async Task BoundInit_ExactPrereleaseArtifactWithNullChannel_PreviewsAndCreates(BundleChannel channel)
    {
        await SeedAsync(channel: channel);
        var request = Request() with { ArtifactBinding = _selection with { Channel = null } };

        var preview = await Runner().ExecuteAsync(request with { DryRun = true }, new([]), CancellationToken.None);

        preview.Kind.Should().Be("previewed");
        preview.SelectedTemplate!.Package!.Version.Should().Be(_selection.Artifact.Version);
        Directory.Exists(Target).Should().BeFalse();

        var result = await Runner().ExecuteAsync(request, new([]), CancellationToken.None);

        result.Kind.Should().Be("created");
        File.ReadAllText(Path.Combine(Target, "value.txt")).Should().Be("exact");
    }

    [Theory]
    [InlineData(BundleChannel.Stable)]
    [InlineData(BundleChannel.Preview)]
    public async Task BoundInit_ExperimentalArtifactWithWrongChannel_RefusesWithoutOutput(BundleChannel channel)
    {
        await SeedAsync(channel: BundleChannel.Experimental);
        var request = Request() with { ArtifactBinding = _selection with { Channel = channel } };

        var result = await Runner().ExecuteAsync(request, new([]), CancellationToken.None);

        result.Kind.Should().Be("bindingMismatch");
        result.Succeeded.Should().BeFalse();
        Directory.Exists(Target).Should().BeFalse();
    }

    [Fact]
    public async Task Projection_QualifiedExperimentalArtifact_SuppressesExactRemoteDuplicate()
    {
        await SeedAsync(channel: BundleChannel.Experimental);
        QuickstartCatalog catalog = new([Remote()], new(QuickstartCatalogOrigin.LocalFile, null, null));

        var projection = await new QuickstartInstalledTemplates(Factory, _scopes).ProjectAsync(catalog,
            new(DirectoryTarget, _selection.Owner, BundleChannel.Experimental), CancellationToken.None);

        projection.Catalog.Entries.Should().BeEmpty();
        projection.Installed.Should().Contain(item => item.Entry.Identity == Identity
            && item.Selection != null && item.Selection.Channel == BundleChannel.Experimental);
        catalog.Entries.Single().MetadataStatus.Should().Be(QuickstartPackageMetadataStatus.Unverified);
    }

    [Fact]
    public async Task Projection_DefaultChannelQuery_ProjectsQualifiedExperimentalDuplicate()
    {
        await SeedAsync(channel: BundleChannel.Experimental);
        QuickstartCatalog catalog = new([Remote()], new(QuickstartCatalogOrigin.LocalFile, null, null));

        var projection = await new QuickstartInstalledTemplates(Factory, _scopes).ProjectAsync(catalog,
            new(DirectoryTarget, _selection.Owner), CancellationToken.None);

        projection.Catalog.Entries.Should().BeEmpty();
        projection.Installed.Should().Contain(item => item.Entry.Identity == Identity
            && item.Selection != null && item.Selection.Channel == null);
        catalog.Entries.Single().MetadataStatus.Should().Be(QuickstartPackageMetadataStatus.Unverified);
    }

    [Theory]
    [InlineData("short-name")]
    [InlineData("other-full-id")]
    [InlineData("package")]
    [InlineData("version")]
    [InlineData("source")]
    [InlineData("digest")]
    [InlineData("owner")]
    [InlineData("owner-kind")]
    [InlineData("owner-rid")]
    public async Task BoundInit_MismatchedCoordinates_NeverFallsBackOrCreates(string change)
    {
        await SeedAsync();
        var selection = change switch
        {
            "short-name" => _selection with { TemplateIdentity = "shared" },
            "other-full-id" => _selection with { TemplateIdentity = "Fixture.Missing.Project" },
            "package" => _selection with { Artifact = _selection.Artifact with { PackageId = "Other.Package" } },
            "version" => _selection with { Artifact = _selection.Artifact with { Version = "9.0.0" } },
            "source" => _selection with { Source = "https://other.invalid/v3/index.json" },
            "digest" => _selection with { Artifact = _selection.Artifact with { Sha256 = new string('a', 64) } },
            "owner-kind" => _selection with { Owner = _selection.Owner with { Kind = WorkloadKind.Content } },
            "owner-rid" => _selection with { Owner = _selection.Owner with { RuntimeIdentifier = "other-rid" } },
            _ => _selection with { Owner = _selection.Owner with { Version = "9.0.0" } },
        };
        var before = HiveBytes();

        var result = await Runner().ExecuteAsync(Request() with { ArtifactBinding = selection }, new([]), CancellationToken.None);

        result.Succeeded.Should().BeFalse();
        result.Kind.Should().Be("bindingMismatch");
        Directory.Exists(Target).Should().BeFalse();
        HiveBytes().Should().BeEquivalentTo(before);
    }

    [Theory]
    [InlineData("filter", "projectFilterConflict")]
    [InlineData("type", "bindingMismatch")]
    [InlineData("metadata", "noCompatibleTemplates")]
    [InlineData("constraint", "noCompatibleTemplates")]
    public async Task BoundInit_TypeFiltersAndEligibility_AreNotBypassed(string change, string expected)
    {
        await SeedAsync(change);
        var request = change == "filter" ? Request() with { ExplicitLanguage = "TypeScript" } : Request();

        var result = await Runner().ExecuteAsync(request, new([]), CancellationToken.None);

        result.Kind.Should().Be(expected);
        result.Succeeded.Should().BeFalse();
        Directory.Exists(Target).Should().BeFalse();
    }

    [Theory]
    [InlineData("receipt")]
    [InlineData("bytes")]
    [InlineData("inventory")]
    [InlineData("authority")]
    [InlineData("duplicate-authority")]
    [InlineData("host")]
    [InlineData("role")]
    public async Task BoundInit_StaleReceiptOrChangedEvidence_FailsWithoutWrites(string change)
    {
        await SeedAsync();
        if (change == "receipt")
        {
            string path = Path.Combine(_root, "cli", "templates", ".native-template-owners", PackageId.ToLowerInvariant() + ".json");
            var receipt = JsonNode.Parse(File.ReadAllText(path))!;
            receipt["sha256"] = new string('a', 64);
            File.WriteAllText(path, receipt.ToJsonString());
        }
        else if (change == "bytes")
        {
            string archive = await InstalledArchiveAsync();
            using var stream = File.Open(archive, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
            stream.WriteByte(0);
        }
        else if (change == "inventory")
        {
            var document = JsonNode.Parse(_catalog.Json)!;
            document["artifacts"]![0]!["templates"]!.AsArray().RemoveAt(1);
            _catalog.Json = document.ToJsonString();
        }
        else if (change is "host" or "role")
        {
            var document = JsonNode.Parse(_catalog.Json)!;
            if (change == "host")
            {
                document["stacks"]![0]!["engineHostVersions"] = "[99.0.0,100.0.0)";
            }
            else
            {
                document["stacks"]![0]!["companions"]![0]!["requiredRoles"] = new JsonArray("function");
            }
            _catalog.Json = document.ToJsonString();
        }
        else if (change == "duplicate-authority")
        {
            var document = JsonNode.Parse(_catalog.Json)!;
            document["artifacts"]!.AsArray().Add(document["artifacts"]![0]!.DeepClone());
            _catalog.Json = document.ToJsonString();
        }
        else
        {
            _catalog.Json = """{"schemaVersion":2,"revision":"empty-production-control","stacks":[],"artifacts":[]}""";
        }
        var before = HiveBytes();

        var result = await Runner().ExecuteAsync(Request(dryRun: true), new([]), CancellationToken.None);

        result.Kind.Should().Be("bindingMismatch");
        result.Diagnostics.Should().NotBeEmpty();
        Directory.Exists(Target).Should().BeFalse();
        HiveBytes().Should().BeEquivalentTo(before);
    }

    [Fact]
    public async Task BoundInit_AbsentFreshScopeOrConflictingReference_RejectsExplicitly()
    {
        await SeedAsync();

        var unavailable = await Runner(scopes: false).ExecuteAsync(Request(), new([]), CancellationToken.None);
        var conflict = await Runner().ExecuteAsync(Request() with { TemplateReference = "shared" }, new([]), CancellationToken.None);

        unavailable.Kind.Should().Be("bindingUnavailable");
        conflict.Kind.Should().Be("bindingConflict");
        Directory.Exists(Target).Should().BeFalse();
    }

    [Fact]
    public async Task Projection_QualifiedCurrentInstallation_OmitsOnlyExactRemoteDuplicateAndPreservesUnverifiedStatus()
    {
        await SeedAsync();
        var exact = Remote();
        QuickstartCatalogEntry[] entries =
        [
            exact,
            exact with { PackageReference = exact.PackageReference! with { TemplateIdentity = "shared" } },
            exact with { PackageReference = exact.PackageReference! with { PackageId = "Other.Package" } },
            exact with { PackageReference = exact.PackageReference! with { Source = "https://other.invalid/v3/index.json" } },
            exact with { PackageReference = exact.PackageReference! with { Artifact = new("sha256", new string('b', 64), "claim") } },
            exact with { PackageReference = exact.PackageReference! with { Artifact = null } },
            exact with { MetadataStatus = QuickstartPackageMetadataStatus.Conflicting },
        ];
        QuickstartCatalog catalog = new(entries, new(QuickstartCatalogOrigin.LocalFile, null, null));
        var before = HiveBytes();

        var projection = await new QuickstartInstalledTemplates(Factory, _scopes).ProjectAsync(catalog,
            new(DirectoryTarget, _selection.Owner), CancellationToken.None);

        projection.Catalog.Entries.Should().Equal(entries.Skip(1));
        projection.Catalog.Entries.Where(entry => entry.MetadataStatus != QuickstartPackageMetadataStatus.Conflicting)
            .Should().OnlyContain(entry => entry.MetadataStatus == QuickstartPackageMetadataStatus.Unverified);
        projection.Installed.Should().Contain(item => item.Entry.Identity == Identity && item.Selection != null);
        HiveBytes().Should().BeEquivalentTo(before);
        Directory.Exists(Target).Should().BeFalse();
    }

    [Fact]
    public async Task Projection_EmptyAuthorityOrUnknownOwnership_KeepsRemoteRowsWithExplicitDiagnostics()
    {
        await SeedAsync();
        _catalog.Json = """{"schemaVersion":2,"revision":"empty-production-control","stacks":[],"artifacts":[]}""";
        QuickstartCatalog catalog = new([Remote()], new(QuickstartCatalogOrigin.LocalFile, null, null));

        var projection = await new QuickstartInstalledTemplates(Factory, _scopes).ProjectAsync(catalog,
            new(DirectoryTarget, _selection.Owner), CancellationToken.None);

        projection.Catalog.Entries.Should().Equal(catalog.Entries);
        projection.Installed.Should().OnlyContain(item => item.Selection == null && item.Diagnostic != null);
    }

    [Fact]
    public async Task Projection_PendingNativeJournal_DoesNotRecoverOrSuppressRows()
    {
        await SeedAsync();
        Directory.CreateDirectory(_templates.ControlDirectory);
        File.WriteAllText(_templates.Journal, "must-not-recover-for-browse");
        var before = HiveBytes();
        QuickstartCatalog catalog = new([Remote()], new(QuickstartCatalogOrigin.LocalFile, null, null));

        Func<Task> project = () => new QuickstartInstalledTemplates(Factory, _scopes).ProjectAsync(catalog,
            new(DirectoryTarget, _selection.Owner), CancellationToken.None);

        await project.Should().ThrowAsync<TemplateStoreRecoveryRequiredException>();
        File.ReadAllText(_templates.Journal).Should().Be("must-not-recover-for-browse");
        HiveBytes().Should().BeEquivalentTo(before);
    }

    [Fact]
    public async Task BoundFlow_ChangedRegistryAfterScopeCapture_RejectsStaleCapabilities()
    {
        await SeedAsync();
        using BoundInitScope scope = await _scopes.CreateAsync(CancellationToken.None);
        using Templater flow = await Factory.CreateProjectAsync(Context, scope.Availability, scope.Support, CancellationToken.None);
        var entry = (await flow.ListAsync(TemplateType.Project, CancellationToken.None)).Single(entry => entry.Identity == Identity);
        var selected = await flow.BindProjectAsync(entry, _selection, scope.Verifier, CancellationToken.None);
        await _workloads.SaveWorkloadAsync(new() { PackageId = "New.Registry.Entry", PackageVersion = "1.0.0", Source = Source,
            Kind = WorkloadKind.Content }, CancellationToken.None);

        var result = await selected.Group![0].InvokeAsync(new(DirectoryTarget, new Dictionary<string, string?>(), DryRun: true), CancellationToken.None);

        result.Status.Should().Be(TemplateInvocationStatus.Failed);
        result.Diagnostic.Should().Contain("workloads changed");
        Directory.Exists(Target).Should().BeFalse();
    }

    [Fact]
    public async Task BoundInit_RetainedOwnerIsNotLiveCapability_RejectsItAndAcceptsQualifiedLiveOwner()
    {
        await SeedAsync();
        WorkloadEntry retained = (await _workloads.GetWorkloadsAsync(CancellationToken.None)).Single();
        const string liveVersion = "99.0.0";
        CopyDirectory(_paths.GetInstallDirectory(retained.PackageId, retained.PackageVersion),
            _paths.GetInstallDirectory(retained.PackageId, liveVersion));
        WorkloadEntry live = new()
        {
            PackageId = retained.PackageId,
            PackageVersion = liveVersion,
            Source = retained.Source,
            Kind = retained.Kind,
            Aliases = retained.Aliases,
            DisplayName = retained.DisplayName,
            Description = retained.Description,
            RuntimeIdentifier = retained.RuntimeIdentifier,
            LogicalPackage = retained.LogicalPackage,
            EntryPoint = retained.EntryPoint,
        };
        await _workloads.SaveWorkloadAsync(live, CancellationToken.None);
        AddStackAuthorityVersion(liveVersion);
        var staleRequest = Request(dryRun: true);

        var stale = await Runner().ExecuteAsync(staleRequest, new([]), CancellationToken.None);

        stale.Kind.Should().Be("bindingMismatch");
        stale.Succeeded.Should().BeFalse();
        Directory.Exists(Target).Should().BeFalse();

        var liveRequest = staleRequest with
        {
            ArtifactBinding = _selection with { Owner = NativeTemplateOwnerIdentity.FromEntry(live) },
        };
        var current = await Runner().ExecuteAsync(liveRequest, new([]), CancellationToken.None);

        current.Kind.Should().Be("previewed");
        current.SelectedTemplate!.Identity.Should().Be(Identity);
        current.Preview!.ProjectConfigurations.Should().ContainSingle().Which.Stack.Should().Be("node");
        Directory.Exists(Target).Should().BeFalse();
    }

    [Fact]
    public async Task BoundFlow_StaleEntryDisposedSessionAndConcurrentUse_RejectWithoutEscapedHandles()
    {
        await SeedAsync();
        using BoundInitScope scope = await _scopes.CreateAsync(CancellationToken.None);
        using Templater first = await Factory.CreateProjectAsync(Context, scope.Availability, scope.Support, CancellationToken.None);
        var entry = (await first.ListAsync(TemplateType.Project, CancellationToken.None)).Single(entry => entry.Identity == Identity);
        using Templater second = await Factory.CreateProjectAsync(Context, scope.Availability, scope.Support, CancellationToken.None);
        Func<Task> stale = () => second.BindProjectAsync(entry, _selection, scope.Verifier, CancellationToken.None);
        await stale.Should().ThrowAsync<ArgumentException>();
        var bound = await first.BindProjectAsync(entry, _selection, scope.Verifier, CancellationToken.None);
        first.Dispose();
        Func<Task> disposed = () => bound.Group![0].InvokeAsync(new(DirectoryTarget, new Dictionary<string, string?>()), CancellationToken.None);
        await disposed.Should().ThrowAsync<ObjectDisposedException>();

        using TemplateEngineSession session = await _templates.OpenReadAsync(Context, CancellationToken.None, scope.Availability);
        using IDisposable operation = session.EnterBoundOperation();
        Action concurrent = () => session.EnterBoundOperation();
        concurrent.Should().Throw<InvalidOperationException>().WithMessage("*concurrently*");
    }

    [Fact]
    public async Task BoundInit_PreCanceled_NoOutputAndNoNativeBootstrap()
    {
        await SeedAsync();
        var before = HiveBytes();
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();

        Func<Task> execute = () => Runner().ExecuteAsync(Request(), new([]), cancellation.Token);

        await execute.Should().ThrowAsync<OperationCanceledException>();
        HiveBytes().Should().BeEquivalentTo(before);
        Directory.Exists(Target).Should().BeFalse();
    }

    [Fact]
    public async Task BoundFlow_ArchiveChangesAfterSelection_RejectsBeforeNativeRender()
    {
        await SeedAsync();
        string archive = await InstalledArchiveAsync();
        using BoundInitScope scope = await _scopes.CreateAsync(CancellationToken.None);
        using Templater flow = await Factory.CreateProjectAsync(Context, scope.Availability, scope.Support, CancellationToken.None);
        var entry = (await flow.ListAsync(TemplateType.Project, CancellationToken.None)).Single(entry => entry.Identity == Identity);
        var bound = await flow.BindProjectAsync(entry, _selection, scope.Verifier, CancellationToken.None);
        using (var stream = File.Open(archive, FileMode.Append, FileAccess.Write, FileShare.ReadWrite))
        {
            stream.WriteByte(0);
        }

        var result = await bound.Group![0].InvokeAsync(new(DirectoryTarget, new Dictionary<string, string?>(), DryRun: true), CancellationToken.None);

        result.Status.Should().Be(TemplateInvocationStatus.Failed);
        result.Diagnostic.Should().Contain("digest");
        Directory.Exists(Target).Should().BeFalse();
    }

    [Fact]
    public async Task BoundFlow_ConcurrentInvocations_RejectsSecondWhileRealVerifierIsAwaitingReceipt()
    {
        await SeedAsync();
        using BoundInitScope scope = await _scopes.CreateAsync(CancellationToken.None);
        using Templater flow = await Factory.CreateProjectAsync(Context, scope.Availability, scope.Support, CancellationToken.None);
        var entry = (await flow.ListAsync(TemplateType.Project, CancellationToken.None)).Single(entry => entry.Identity == Identity);
        TaskCompletionSource entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        bool block = false;
        var ownership = Substitute.For<INativeTemplateOwnership>();
        ownership.GetRetainedAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(async call =>
        {
            if (block)
            {
                entered.TrySetResult();
                await release.Task.WaitAsync(call.Arg<CancellationToken>());
            }
            return await _ownership.GetRetainedAsync(call.Arg<string>(), call.Arg<CancellationToken>());
        });
        IReadOnlyList<WorkloadEntry> entries = await _workloads.GetWorkloadsAsync(CancellationToken.None);
        var verifier = new NativeTemplateArtifactVerifier(_catalog, new(), ownership, _workloads, new BasicArtifactInspector(_files))
            .ForInventory(entries, entries);
        var bound = await flow.BindProjectAsync(entry, _selection, verifier, CancellationToken.None);
        block = true;
        TemplateInvocationRequest request = new(DirectoryTarget, new Dictionary<string, string?>(), DryRun: true);
        Task<TemplateInvocationResult> first = bound.Group![0].InvokeAsync(request, CancellationToken.None);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        try
        {
            Func<Task> second = () => bound.Group[0].InvokeAsync(request, CancellationToken.None);
            await second.Should().ThrowAsync<InvalidOperationException>().WithMessage("*concurrently*");
        }
        finally
        {
            release.TrySetResult();
        }
        (await first).Status.Should().Be(TemplateInvocationStatus.Success);
        Directory.Exists(Target).Should().BeFalse();
    }

    [Fact]
    public async Task BoundInit_CancellationDuringArtifactVerification_DoesNotCommitOutput()
    {
        await SeedAsync();
        using BoundInitScope scope = await _scopes.CreateAsync(CancellationToken.None);
        using Templater flow = await Factory.CreateProjectAsync(Context, scope.Availability, scope.Support, CancellationToken.None);
        var entry = (await flow.ListAsync(TemplateType.Project, CancellationToken.None)).Single(entry => entry.Identity == Identity);
        using CancellationTokenSource cancellation = new();
        var ownership = Substitute.For<INativeTemplateOwnership>();
        ownership.GetRetainedAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(async call =>
        {
            cancellation.Cancel();
            return await _ownership.GetRetainedAsync(call.Arg<string>(), call.Arg<CancellationToken>());
        });
        IReadOnlyList<WorkloadEntry> entries = await _workloads.GetWorkloadsAsync(CancellationToken.None);
        var verifier = new NativeTemplateArtifactVerifier(_catalog, new(), ownership, _workloads, new BasicArtifactInspector(_files))
            .ForInventory(entries, entries);

        Func<Task> bind = () => flow.BindProjectAsync(entry, _selection, verifier, cancellation.Token);

        await bind.Should().ThrowAsync<OperationCanceledException>();
        Directory.Exists(Target).Should().BeFalse();
    }

    [Fact]
    public async Task BoundInit_CancellationAfterRealOutputMove_RetainsPartialCommitTruthWithoutConfiguration()
    {
        await SeedAsync();
        using CancellationTokenSource cancellation = new();
        List<string> moved = [];
        var installation = new RecordingCommitFileSystem(_files)
        {
            AfterMove = path =>
            {
                moved.Add(path);
                cancellation.Cancel();
            },
        };

        var result = await Runner(installation: installation).ExecuteAsync(Request(), new([]), cancellation.Token);

        moved.Should().ContainSingle();
        File.Exists(moved[0]).Should().BeTrue();
        result.Kind.Should().Be("invocationFailed");
        result.Invocation!.HasCommittedOutput.Should().BeTrue();
        result.Invocation.Status.Should().NotBe(TemplateInvocationStatus.Success);
        result.Invocation.ConfiguredProjects.Should().BeEmpty();
        File.Exists(Path.Combine(Target, ".func", "config.json")).Should().BeFalse();
    }

    [Fact]
    public async Task BoundInit_AdoptableProject_RejectsWithoutEnteringArtifactSelection()
    {
        await SeedAsync();
        Directory.CreateDirectory(Target);
        File.WriteAllText(Path.Combine(Target, "host.json"), """{"version":"2.0"}""");

        var result = await Runner().ExecuteAsync(Request(), new([]), CancellationToken.None);

        result.Kind.Should().Be("templateOnExistingProject");
        File.Exists(Path.Combine(Target, ".func", "config.json")).Should().BeFalse();
    }

    [Fact]
    public async Task DirectInit_NullBinding_PreservesShortNameAndPrecedenceWithoutRefreshingScope()
    {
        await SeedAsync();
        using BoundInitScope inputs = await _scopes.CreateAsync(CancellationToken.None);
        var scopes = Substitute.For<IBoundInitScopeFactory>();
        TemplateArgumentParser arguments = new();
        InitExecutionRunner runner = new(Factory, arguments, new(arguments, _interaction), _interaction, new(_interaction), new(_files),
            () => inputs.Catalog, () => inputs.Support, _files, Substitute.For<ILocalSettingsProvider>(),
            Substitute.For<IFunctionsProjectResolver>(), scopes);

        var result = await runner.ExecuteAsync(Request(dryRun: true) with { ArtifactBinding = null, TemplateReference = "shared" },
            inputs.Availability, CancellationToken.None);

        result.Kind.Should().Be("previewed");
        result.SelectedTemplate!.Identity.Should().Be("Fixture.Sibling.Project");
        await scopes.DidNotReceive().CreateAsync(Arg.Any<CancellationToken>());
        Directory.Exists(Target).Should().BeFalse();
    }

    [Fact]
    public async Task FreshScope_UnloadableOwner_RefusesPartialCapabilitySnapshot()
    {
        await SeedAsync();
        var entry = (await _workloads.GetWorkloadsAsync(CancellationToken.None)).Single();
        await _workloads.SaveWorkloadAsync(new()
        {
            PackageId = entry.PackageId, PackageVersion = entry.PackageVersion, Source = entry.Source, Kind = entry.Kind,
            EntryPoint = new() { AssemblyPath = "tools/any/missing.dll", Type = entry.EntryPoint!.Type },
        }, CancellationToken.None);

        Func<Task> create = () => _scopes.CreateAsync(CancellationToken.None);

        await create.Should().ThrowAsync<InvalidWorkloadException>().WithMessage("*fresh capability scope*");
        Directory.Exists(Target).Should().BeFalse();
    }

    [Fact]
    public async Task Projection_RestrictedInstalledTemplate_RetainsAvailabilityDiagnosticWithoutGrantingRemoteTrust()
    {
        await SeedAsync("constraint");
        QuickstartCatalog catalog = new([Remote()], new(QuickstartCatalogOrigin.LocalFile, null, null));

        var result = await new QuickstartInstalledTemplates(Factory, _scopes).ProjectAsync(catalog,
            new(DirectoryTarget, _selection.Owner), CancellationToken.None);

        result.Catalog.Entries.Should().BeEmpty("only a duplicate installation is suppressed, not its eligibility diagnosis");
        var installed = result.Installed.Single(item => item.Entry.Identity == Identity);
        installed.Selection.Should().NotBeNull();
        installed.Entry.ConstraintEvaluation.Status.Should().Be(TemplateConstraintEvaluationStatus.Restricted);
        installed.Diagnostic.Should().NotBeNullOrEmpty();
        catalog.Entries[0].MetadataStatus.Should().Be(QuickstartPackageMetadataStatus.Unverified);
    }

    private InitExecutionRequest Request(bool dryRun = false) => new(DirectoryTarget, null, null, null, null,
        false, true, dryRun, false, [], new HashSet<string>(StringComparer.OrdinalIgnoreCase)) { ArtifactBinding = _selection };

    private InitExecutionRunner Runner(bool scopes = true, IFileSystem? installation = null)
    {
        TemplateArgumentParser arguments = new();
        return new(new TemplaterFactory(_templates, installation ?? _files), arguments, new(arguments, _interaction), _interaction, new(_interaction), new(_files),
            () => throw new InvalidOperationException("The original stack catalog must not serve bound init."),
            () => throw new InvalidOperationException("The original project support must not serve bound init."),
            _files, Substitute.For<ILocalSettingsProvider>(), Substitute.For<IFunctionsProjectResolver>(), scopes ? _scopes : null);
    }

    private async Task SeedAsync(string? change = null, BundleChannel channel = BundleChannel.Stable, bool installTemplates = true)
    {
        string artifactVersion = channel switch
        {
            BundleChannel.Stable => "1.0.0",
            BundleChannel.Preview => "1.0.0-preview.1",
            BundleChannel.Experimental => "1.0.0-experimental.1",
            _ => throw new ArgumentOutOfRangeException(nameof(channel)),
        };
        string channelName = channel switch
        {
            BundleChannel.Stable => "stable",
            BundleChannel.Preview => "preview",
            BundleChannel.Experimental => "experimental",
            _ => throw new ArgumentOutOfRangeException(nameof(channel)),
        };
        string nodeArchive = Directory.GetFiles(AppContext.BaseDirectory, "Azure.Functions.Cli.Workloads.Node.*.nupkg", SearchOption.AllDirectories).Single();
        using PackageArchiveReader reader = new(nodeArchive);
        string ownerId = reader.NuspecReader.GetId();
        string ownerVersion = reader.NuspecReader.GetVersion().ToNormalizedString();
        string install = _paths.GetInstallDirectory(ownerId, ownerVersion);
        WorkloadEntry owner;
        if (installTemplates)
        {
            Directory.CreateDirectory(install);
            ZipFile.ExtractToDirectory(nodeArchive, install);
            var metadata = new WorkloadMetadataReader().Read(install);
            owner = new() { PackageId = ownerId, PackageVersion = ownerVersion, Source = Source,
                Kind = WorkloadKind.Workload, EntryPoint = metadata.EntryPoint, Aliases = ["node"] };
            await _workloads.SaveWorkloadAsync(owner, CancellationToken.None);
        }
        else
        {
            owner = await SeedGuidedOwnerAsync(nodeArchive);
        }
        string archive = Path.Combine(_root, "exact.nupkg");
        using (var zip = ZipFile.Open(archive, ZipArchiveMode.Create))
        {
            Write(zip, PackageId + ".nuspec", $$"""
                <?xml version="1.0"?><package><metadata><id>{{PackageId}}</id><version>{{artifactVersion}}</version><authors>fixture</authors>
                <description>Artifact binding evidence only.</description><packageTypes><packageType name="FuncTemplate"/></packageTypes></metadata></package>
                """);
            WriteProject(zip, "exact", Identity, 0, change);
            WriteProject(zip, "sibling", "Fixture.Sibling.Project", 100, null);
        }
        string digest = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(archive)));
        string type = change == "type" ? "item" : "project";
        var document = JsonNode.Parse($$"""
            {"schemaVersion":2,"revision":"binding-fixture-only","stacks":[
              {"packageId":"{{ownerId}}","versions":"[{{ownerVersion}}]","sources":["{{Source}}"],"hostIdentifier":"func",
               "engineHostVersions":"[1.0.0,99.0.0)","channels":["{{channelName}}"],"companions":[
                 {"packageId":"{{PackageId}}","versions":"[{{artifactVersion}}]","requiredRoles":["additional-project"]}]}],
             "artifacts":[{"packageId":"{{PackageId}}","version":"{{artifactVersion}}","source":"{{Source}}","sha256":"{{digest}}",
               "contentRevision":"fixture-only","basicOnly":false,"templates":[
                 {"path":"exact/.template.config/template.json","identity":"{{Identity}}","type":"{{type}}","contentRole":"additional-project"},
                 {"path":"sibling/.template.config/template.json","identity":"Fixture.Sibling.Project","type":"project","contentRole":"additional-project"}]}]}
            """)!;
        if (change == "type")
        {
            document["artifacts"]![0]!["templates"]![0]!["contentRole"] = "function";
        }
        _catalog.Json = document.ToJsonString();
        var artifact = (await _catalog.LoadAsync(CancellationToken.None)).Artifacts.Single();
        if (installTemplates)
        {
            await TemplateFixtures.InstallAsync(archive, _templates.Hive, CancellationToken.None);
            await _ownership.RecordAsync(artifact, CancellationToken.None);
        }
        _selection = new(Identity, new(PackageId, artifactVersion, digest), Source, NativeTemplateOwnerIdentity.FromEntry(owner), channel);
    }

    private void AddStackAuthorityVersion(string version)
    {
        var document = JsonNode.Parse(_catalog.Json)!.AsObject();
        var stack = (JsonObject)document["stacks"]![0]!.DeepClone();
        stack["versions"] = $"[{version}]";
        document["stacks"]!.AsArray().Add(stack);
        _catalog.Json = document.ToJsonString();
    }

    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (string file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
        {
            string target = Path.Combine(destination, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target);
        }
    }

    private static void WriteProject(ZipArchive zip, string folder, string identity, int precedence, string? change)
    {
        string constraint = change == "constraint" ? ""","missing":{"type":"func-workload","args":"missing"}""" : "";
        Write(zip, folder + "/.template.config/template.json", $$$"""
            {"identity":"{{{identity}}}","name":"{{{identity}}}","shortName":"shared","groupIdentity":"Fixture.Shared",
             "precedence":{{{precedence}}},"tags":{"type":"{{{(change == "type" ? "item" : "project")}}}"},
             "constraints":{"func":{"type":"host","args":[{"hostname":"func"}]},"node":{"type":"func-workload","args":"node"}{{{constraint}}}},
             "primaryOutputs":[{"path":"host.json"}],"postActions":[{"actionId":"{{{TemplateProjectConfigurationPlanner.ConfigurationActionId}}}",
               "args":{"primaryOutputIndex":"0","stack":"node","language":"JavaScript"}}]}
            """);
        if (change == "metadata")
        {
            Write(zip, folder + "/.template.config/func.host.json", "{invalid");
        }
        Write(zip, folder + "/host.json", """{"version":"2.0"}""");
        Write(zip, folder + "/value.txt", folder);
    }

    private static void Write(ZipArchive zip, string path, string content)
    {
        using StreamWriter writer = new(zip.CreateEntry(path).Open());
        writer.Write(content);
    }

    private async Task<string> InstalledArchiveAsync()
    {
        using TemplateEngineSession session = await _templates.OpenReadAsync(Context, CancellationToken.None);
        return (await session.PackageManager.GetManagedTemplatePackagesAsync(false, CancellationToken.None)).Single().MountPointUri;
    }

    private QuickstartCatalogEntry Remote() => new(
        new QuickstartEntry("fixture", "Fixture", "JavaScript", "http", null, "https://example.invalid/repo", "folder",
            null, "Unverified remote claim", null, null, 0),
        QuickstartPackageMetadataStatus.Unverified,
        new(PackageId, _selection.Artifact.Version, Source, Identity, null, new("sha256", _selection.Artifact.Sha256, "inert-claim"), default, default), "unverified");

    private Dictionary<string, byte[]> HiveBytes() => Directory.GetFiles(_templates.Hive, "*", SearchOption.AllDirectories)
        .ToDictionary(path => Path.GetRelativePath(_templates.Hive, path), File.ReadAllBytes);

    public void Dispose()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        Directory.Delete(_root, true);
    }
}
