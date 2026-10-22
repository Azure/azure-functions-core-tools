// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.CommandLine;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using Azure.Functions.Cli.Bundles;
using Azure.Functions.Cli.Commands;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Configuration;
using Azure.Functions.Cli.Console;
using Azure.Functions.Cli.Hosting;
using Azure.Functions.Cli.Projects;
using Azure.Functions.Cli.Quickstart;
using Azure.Functions.Cli.Templates;
using Azure.Functions.Cli.Templates.Acquisition;
using Azure.Functions.Cli.Templates.Engine;
using Azure.Functions.Cli.Tests.Templates.Acquisition;
using Azure.Functions.Cli.Tests.Templates.Engine;
using Azure.Functions.Cli.Workloads;
using Azure.Functions.Cli.Workloads.Catalog;
using Azure.Functions.Cli.Workloads.Discovery;
using Azure.Functions.Cli.Workloads.Install;
using Azure.Functions.Cli.Workloads.Storage;
using Microsoft.Extensions.Options;
using NSubstitute;
using NuGet.Packaging;
using NuGet.Versioning;

namespace Azure.Functions.Cli.Tests.Templates;

public sealed partial class ArtifactBindingJourneyTests
{
    [Fact]
    public async Task DefaultInit_PublicParser_BrowseConsentRealAcquisitionFreshBoundCreateAndRepeatInstalledVisibility()
    {
        await SeedAsync(installTemplates: false);
        GuidedInteraction interaction = new(true, ["first", "Browse more templates", "first", "Use this template"], [true, true]);
        var journey = Guided(interaction);
        var command = GuidedCommand(journey.Coordinator, interaction, [NodeInitializer()]);
        RootCommand root = [command];

        var exit = await root.Parse(["init", Target]).InvokeAsync();

        exit.Should().Be(0, interaction.AllOutput);
        journey.Payload.Requested.Should().Equal($"{PackageId}/1.0.0");
        journey.Lifecycle.Prepared.Should().NotBeNull();
        journey.Lifecycle.ObservedFreshBinding.Should().BeTrue();
        File.Exists(journey.Lifecycle.Prepared!.Archive).Should().BeFalse("all prepared payload leases must be disposed before fresh bound init returns");
        File.ReadAllText(Path.Combine(Target, "value.txt")).Should().Be("exact");
        File.Exists(Path.Combine(Target, ".func", "config.json")).Should().BeTrue();
        interaction.Confirmations.Should().Equal(false, false);
        interaction.AllOutput.Should().Contain("Independently qualified").And.Contain("fixture-only").And.Contain(_selection.Artifact.Sha256);
        interaction.AllOutput.Should().Contain("unverified claims");
        interaction.AllOutput.Should().Contain("Select a project stack:");
        interaction.AllOutput.Should().Contain("Basic Functions app");

        var projection = await new QuickstartInstalledTemplates(Factory, _scopes).ProjectAsync(
            await journey.Catalog.SearchAsync(new(), CancellationToken.None), new(DirectoryTarget, _selection.Owner), CancellationToken.None);
        projection.Catalog.Entries.Should().BeEmpty();
        projection.Installed.Should().Contain(item => item.Entry.Identity == Identity && item.Selection != null);

        var secondTarget = WorkingDirectory.FromExplicit(Path.Combine(_root, "second"));
        var repeated = Guided(new(true, ["exact"], [true]));
        var installed = await repeated.Coordinator.ExecuteAsync(new(GuidedRequest() with
        {
            WorkingDirectory = secondTarget,
        }), CancellationToken.None);
        installed.Kind.Should().Be("created");
        journey.Payload.Requested.Should().HaveCount(1, "installed selection never re-acquires");
        repeated.Payload.Requested.Should().BeEmpty();
        File.ReadAllText(Path.Combine(secondTarget.Info.FullName, "value.txt")).Should().Be("exact");
    }

    [Fact]
    public async Task DefaultInit_BasicSelection_UsesExistingInitializerWithoutWritingBeforeSelection()
    {
        await SeedAsync(installTemplates: false);
        GuidedInteraction interaction = new(true, ["first", "Basic Functions app"], []);
        interaction.SelectionMade = () => Directory.Exists(Target).Should().BeFalse();
        IProjectInitializer initializer = NodeInitializer();
        var journey = Guided(interaction);
        var command = GuidedCommand(journey.Coordinator, interaction, [initializer]);
        RootCommand root = [command];

        int exit = await root.Parse(["init", Target]).InvokeAsync();

        exit.Should().Be(0, interaction.AllOutput);
        await initializer.Received(1).InitializeAsync(Arg.Any<InitContext>(), Arg.Any<ParseResult>(), Arg.Any<CancellationToken>());
        File.Exists(Path.Combine(Target, ".func", "config.json")).Should().BeTrue();
        File.Exists(Path.Combine(Target, "value.txt")).Should().BeFalse();
        interaction.AllOutput.Should().Contain("Select a project stack:").And.Contain("Basic Functions app");
        interaction.Confirmations.Should().BeEmpty();
    }

    [Fact]
    public async Task DefaultInit_CancelLeavesTargetAndInstalledTemplateHiveUnchanged()
    {
        await SeedAsync(installTemplates: false);
        GuidedInteraction interaction = new(true, ["first", "Cancel"], []);
        interaction.SelectionMade = () => Directory.Exists(Target).Should().BeFalse();
        var journey = Guided(interaction);
        var command = GuidedCommand(journey.Coordinator, interaction, [NodeInitializer()]);
        RootCommand root = [command];
        var before = TemplateState();

        int exit = await root.Parse(["init", Target]).InvokeAsync();

        exit.Should().Be(1);
        Directory.Exists(Target).Should().BeFalse();
        File.Exists(Path.Combine(Target, ".func", "config.json")).Should().BeFalse();
        TemplateState().Should().BeEquivalentTo(before);
        journey.Payload.Requested.Should().BeEmpty();
        interaction.AllOutput.Should().Contain("canceled");
    }

    [Theory]
    [InlineData("decline", "canceled")]
    [InlineData("back", "canceled")]
    [InlineData("cancel", "canceled")]
    [InlineData("script", "acquisitionAuthorizationRequired")]
    [InlineData("dry-run", "payloadRequired")]
    [InlineData("force", "forceNotQualified")]
    public async Task Guided_RefusalAndReadOnlyModes_NeverAcquireOrCreate(string mode, string expected)
    {
        await SeedAsync(installTemplates: false);
        string[] selections = mode switch
        {
            "back" => ["Browse more templates", "Back"],
            "cancel" => ["Cancel"],
            _ => ["Browse more templates", "first", "Use this template"],
        };
        GuidedInteraction interaction = new(mode != "script", selections, [false]);
        var journey = Guided(interaction);
        var input = GuidedRequest() with { DryRun = mode == "dry-run", Force = mode == "force", NonInteractive = mode == "script" };
        var before = TemplateState();

        var result = await journey.Coordinator.ExecuteAsync(new(input, mode == "script" ? "fixture" : null), CancellationToken.None);

        result.Kind.Should().Be(expected, interaction.AllOutput);
        journey.Payload.Opened.Should().BeEmpty();
        Directory.Exists(Target).Should().BeFalse();
        TemplateState().Should().BeEquivalentTo(before);
    }

    [Theory]
    [InlineData("empty", "qualificationUnavailable")]
    [InlineData("role", "qualificationUnavailable")]
    [InlineData("source", "qualificationUnavailable")]
    [InlineData("digest", "qualificationUnavailable")]
    [InlineData("host", "qualificationUnavailable")]
    [InlineData("ambiguous", "qualificationUnavailable")]
    public async Task Guided_UnqualifiedNomination_NeverUsesInstalledReceiptAsPreAcquisitionApproval(string change, string expected)
    {
        await SeedAsync(installTemplates: false);
        // A genuine receipt without installed bytes still cannot approve a remote nomination.
        await _ownership.RecordAsync((await _catalog.LoadAsync(CancellationToken.None)).Artifacts.Single(), CancellationToken.None);
        JsonNode document = JsonNode.Parse(_catalog.Json)!;
        switch (change)
        {
            case "empty":
                _catalog.Json = """{"schemaVersion":1,"revision":"unqualified","stacks":[],"artifacts":[]}""";
                break;
            case "role":
                document["artifacts"]![0]!["templates"]![0]!["contentRole"] = "function";
                _catalog.Json = document.ToJsonString();
                break;
            case "source":
                document["stacks"]![0]!["sources"] = new JsonArray("https://other.invalid/v3/index.json");
                _catalog.Json = document.ToJsonString();
                break;
            case "digest":
                document["artifacts"]![0]!["sha256"] = new string('a', 64);
                _catalog.Json = document.ToJsonString();
                break;
            case "host":
                document["stacks"]![0]!["engineHostVersions"] = "[99.0.0,100.0.0)";
                _catalog.Json = document.ToJsonString();
                break;
            default:
                document["artifacts"]!.AsArray().Add(document["artifacts"]![0]!.DeepClone());
                _catalog.Json = document.ToJsonString();
                break;
        }
        var journey = Guided(new(false, [], []));

        var result = await journey.Coordinator.ExecuteAsync(new(GuidedRequest(), "fixture", true), CancellationToken.None);

        result.Kind.Should().Be(expected);
        result.Succeeded.Should().BeFalse();
        journey.Payload.Opened.Should().BeEmpty();
        Directory.Exists(Target).Should().BeFalse();
    }

    [Theory]
    [InlineData("revision")]
    [InlineData("inventory")]
    [InlineData("range")]
    [InlineData("owner")]
    [InlineData("live-owner")]
    [InlineData("target")]
    public async Task Guided_ChangedPlanAfterConsent_RejectsWithoutAcquisition(string change)
    {
        await SeedAsync(installTemplates: false);
        GuidedInteraction interaction = new(true, [], [true]);
        interaction.Confirming = async () =>
        {
            if (change is "revision" or "inventory" or "range")
            {
                JsonNode document = JsonNode.Parse(_catalog.Json)!;
                if (change == "revision")
                {
                    document["revision"] = "changed-after-consent";
                }
                else if (change == "range")
                {
                    document["stacks"]![0]!["companions"]![0]!["versions"] = "[1.0.0,3.0.0)";
                }
                else
                {
                    document["artifacts"]![0]!["templates"]![0]!["path"] = "different/.template.config/template.json";
                }
                _catalog.Json = document.ToJsonString();
            }
            else if (change == "live-owner")
            {
                WorkloadEntry owner = (await _workloads.GetWorkloadsAsync(CancellationToken.None)).Single();
                CopyDirectory(_paths.GetInstallDirectory(owner.PackageId, owner.PackageVersion), _paths.GetInstallDirectory(owner.PackageId, "99.0.0"));
                var node = JsonSerializer.SerializeToNode(owner, WorkloadJsonContext.Default.WorkloadEntry)!;
                node["packageVersion"] = "99.0.0";
                await _workloads.SaveWorkloadAsync(JsonSerializer.Deserialize(node, WorkloadJsonContext.Default.WorkloadEntry)!, CancellationToken.None);
                AddStackAuthorityVersion("99.0.0");
            }
            else if (change == "owner")
            {
                await _workloads.SaveWorkloadAsync(new() { PackageId = "Unrelated.Registry.Change", PackageVersion = "1.0.0",
                    Source = Source, Kind = WorkloadKind.Content }, CancellationToken.None);
            }
            else
            {
                Directory.CreateDirectory(Target);
                File.WriteAllText(Path.Combine(Target, "keep.txt"), "user work");
            }
        };
        var journey = Guided(interaction);

        var result = await journey.Coordinator.ExecuteAsync(new(GuidedRequest(), "fixture"), CancellationToken.None);

        result.Succeeded.Should().BeFalse();
        result.Kind.Should().BeOneOf("acquisitionFailed", "targetChanged", "planChanged");
        journey.Payload.Opened.Should().BeEmpty();
        File.Exists(Path.Combine(Target, "value.txt")).Should().BeFalse();
    }

    [Fact]
    public async Task Guided_AuthorityChangesDuringPrivatePreparation_RejectsBeforeCommitAndDisposesPayload()
    {
        await SeedAsync(installTemplates: false);
        var journey = Guided(new(false, [], []));
        journey.Payload.Opening = _ =>
        {
            JsonNode document = JsonNode.Parse(_catalog.Json)!;
            document["artifacts"]![0]!["contentRevision"] = "changed-in-flight";
            _catalog.Json = document.ToJsonString();
        };

        var result = await journey.Coordinator.ExecuteAsync(new(GuidedRequest(), "fixture", true), CancellationToken.None);

        result.Kind.Should().Be("acquisitionFailed");
        result.Diagnostics.Should().Contain(message => message.Contains("PlanChanged", StringComparison.Ordinal));
        journey.Payload.Opened.Should().ContainSingle();
        File.Exists(journey.Lifecycle.Prepared!.Archive).Should().BeFalse();
        (await _ownership.GetRetainedAsync(PackageId, CancellationToken.None)).Should().BeNull();
        Directory.Exists(Target).Should().BeFalse();
    }

    [Theory]
    [InlineData("bytes")]
    [InlineData("payload")]
    public async Task Guided_WrongOrUnavailableArchive_NeverCreatesProject(string failure)
    {
        await SeedAsync(installTemplates: false);
        var journey = Guided(new(false, [], []));
        journey.Payload.Missing = failure == "payload";
        if (failure == "bytes")
        {
            using var stream = File.Open(Path.Combine(_root, "exact.nupkg"), FileMode.Append);
            stream.WriteByte(0);
        }

        var result = await journey.Coordinator.ExecuteAsync(new(GuidedRequest(), "fixture", true), CancellationToken.None);

        result.Kind.Should().Be("acquisitionFailed");
        result.Succeeded.Should().BeFalse();
        Directory.Exists(Target).Should().BeFalse();
    }

    [Fact]
    public async Task Guided_CreationDeclinedAfterAcquisition_PreservesInstalledPackagesAndReportsNoRollback()
    {
        await SeedAsync(installTemplates: false);
        var journey = Guided(new(true, [], [true, false]));

        var result = await journey.Coordinator.ExecuteAsync(new(GuidedRequest(), "fixture"), CancellationToken.None);

        result.Kind.Should().Be("creationDeclined");
        result.Diagnostics.Should().Contain(message => message.Contains("remain installed", StringComparison.Ordinal));
        (await _ownership.GetRetainedAsync(PackageId, CancellationToken.None)).Should().NotBeNull();
        Directory.Exists(Target).Should().BeFalse();
    }

    [Fact]
    public async Task Guided_PublicDirectUnknownTemplate_DoesNotDiscoverOrAcquire()
    {
        await SeedAsync(installTemplates: false);
        var interaction = new GuidedInteraction(false, [], []);
        var journey = Guided(interaction);
        RootCommand root = [GuidedCommand(journey.Coordinator, interaction)];

        var exit = await root.Parse(["init", Target, "--template", "unknown"]).InvokeAsync();

        exit.Should().Be(1);
        journey.Payload.Opened.Should().BeEmpty();
        Directory.Exists(Target).Should().BeFalse();
        interaction.AllOutput.Should().Contain("--guided");
    }

    [Fact]
    public async Task Guided_ScriptExplicitAuthorization_AcquiresAndCreatesWithoutPrompts()
    {
        await SeedAsync(installTemplates: false);
        var interaction = new GuidedInteraction(false, [], []);
        var journey = Guided(interaction);
        RootCommand root = [GuidedCommand(journey.Coordinator, interaction)];

        int exit = await root.Parse(["init", Target, "--guided", "--stack", "node", "--quickstart", "fixture",
            "--acquire-quickstart", "--non-interactive"]).InvokeAsync();

        exit.Should().Be(0, interaction.AllOutput);
        interaction.Confirmations.Should().BeEmpty();
        journey.Payload.Requested.Should().ContainSingle();
        File.ReadAllText(Path.Combine(Target, "value.txt")).Should().Be("exact");
    }

    [Theory]
    [InlineData(BundleChannel.Preview)]
    [InlineData(BundleChannel.Experimental)]
    public async Task Guided_NullChannelNomination_UsesExactQualifiedPrereleaseWithoutTreatingItAsStable(BundleChannel channel)
    {
        await SeedAsync(channel: channel, installTemplates: false);
        var journey = Guided(new(false, [], []));

        var result = await journey.Coordinator.ExecuteAsync(new(GuidedRequest(), "fixture", true), CancellationToken.None);

        result.Kind.Should().Be("created");
        result.SelectedTemplate!.Package!.Version.Should().Be(_selection.Artifact.Version);
        journey.Payload.Requested.Should().Equal($"{PackageId}/{_selection.Artifact.Version}");
        File.ReadAllText(Path.Combine(Target, "value.txt")).Should().Be("exact");
    }

    [Fact]
    public async Task Guided_AuthorityRangeWithNewerQualifiedCandidate_AcquiresOnlyNominatedExactVersion()
    {
        await SeedAsync(installTemplates: false);
        string alternate = Path.Combine(_root, "alternate.nupkg");
        File.Copy(Path.Combine(_root, "exact.nupkg"), alternate);
        using (var archive = ZipFile.Open(alternate, ZipArchiveMode.Update))
        {
            archive.GetEntry(PackageId + ".nuspec")!.Delete();
            Write(archive, PackageId + ".nuspec", $$"""
                <?xml version="1.0"?><package><metadata><id>{{PackageId}}</id><version>2.0.0</version><authors>fixture</authors>
                <description>Exact selection control.</description><packageTypes><packageType name="FuncTemplate"/></packageTypes></metadata></package>
                """);
        }
        JsonNode document = JsonNode.Parse(_catalog.Json)!;
        JsonNode artifact = document["artifacts"]![0]!.DeepClone();
        artifact["version"] = "2.0.0";
        artifact["sha256"] = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(alternate)));
        document["artifacts"]!.AsArray().Add(artifact);
        document["stacks"]![0]!["companions"]![0]!["versions"] = "[1.0.0,3.0.0)";
        _catalog.Json = document.ToJsonString();
        var journey = Guided(new(false, [], []));

        var result = await journey.Coordinator.ExecuteAsync(new(GuidedRequest(), "fixture", true), CancellationToken.None);

        result.Kind.Should().Be("created");
        result.SelectedTemplate!.Package!.Version.Should().Be("1.0.0");
        journey.Payload.Requested.Should().Equal($"{PackageId}/1.0.0");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ApprovedNativeCheck_IsReadOnlyAndRevalidatesConsentAtWriterAdmission(bool changeAfterInitialSelection)
    {
        await SeedAsync(installTemplates: false);
        var journey = Guided(new(false, [], []));
        QuickstartCatalogEntry nomination = (await journey.Catalog.SearchAsync(new(), CancellationToken.None)).Entries.Single();
        WorkloadEntry owner = (await _workloads.GetWorkloadsAsync(CancellationToken.None)).Single();
        var qualified = await journey.Qualification.ResolveAsync(nomination, new(DirectoryTarget, _selection.Owner), owner, CancellationToken.None);
        if (changeAfterInitialSelection)
        {
            journey.Lifecycle.Selecting = () =>
            {
                JsonNode document = JsonNode.Parse(_catalog.Json)!;
                document["revision"] = "changed-before-check-admission";
                _catalog.Json = document.ToJsonString();
            };
        }
        var before = TemplateState();

        var result = await journey.Acquisition.RunAsync(qualified.Acquisition with { Check = true }, CancellationToken.None);

        if (changeAfterInitialSelection)
        {
            result.Failure.Should().Be(BasicAcquisitionFailure.PlanChanged);
        }
        else
        {
            result.Status.Should().Be(BasicAcquisitionStatus.Incomplete);
        }
        journey.Payload.Requested.Should().BeEmpty();
        journey.Lifecycle.Prepared.Should().BeNull();
        Directory.Exists(Target).Should().BeFalse();
        TemplateState().Should().BeEquivalentTo(before);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Guided_CancellationDuringConsentOrPrivateAcquisition_NeverStartsProjectCreation(bool duringAcquisition)
    {
        await SeedAsync(installTemplates: false);
        using CancellationTokenSource cancellation = new();
        var interaction = new GuidedInteraction(true, [], [true]);
        var journey = Guided(interaction);
        if (duringAcquisition)
        {
            journey.Payload.Opening = _ => cancellation.Cancel();
            Func<Task> execute = () => journey.Coordinator.ExecuteAsync(new(GuidedRequest(), "fixture"), cancellation.Token);
            var canceled = await execute.Should().ThrowAsync<OperationCanceledException>();
            canceled.Which.CancellationToken.Should().Be(interaction.ConfirmationToken);
            canceled.Which.InnerException.Should().BeOfType<OperationCanceledException>();
        }
        else
        {
            interaction.Confirming = () =>
            {
                cancellation.Cancel();
                return Task.CompletedTask;
            };
            Func<Task> execute = () => journey.Coordinator.ExecuteAsync(new(GuidedRequest(), "fixture"), cancellation.Token);
            await execute.Should().ThrowAsync<OperationCanceledException>();
        }
        journey.Payload.Opened.Should().BeEmpty();
        Directory.Exists(Target).Should().BeFalse();
    }

    [Theory]
    [InlineData("consent")]
    [InlineData("private-acquisition")]
    [InlineData("prepared-acquisition")]
    public async Task Guided_PublicParser_CancellationPropagatesAfterAcquisitionCleanupWithoutCreating(string phase)
    {
        await SeedAsync(installTemplates: false);
        using CancellationTokenSource cancellation = new();
        var interaction = new GuidedInteraction(true, [], [true]);
        var journey = Guided(interaction);
        OperationCanceledException original = new("Fixture private acquisition canceled.", cancellation.Token);
        if (phase == "consent")
        {
            interaction.Confirming = () =>
            {
                cancellation.Cancel();
                return Task.CompletedTask;
            };
        }
        else if (phase == "private-acquisition")
        {
            journey.Payload.Opening = _ =>
            {
                cancellation.Cancel();
                throw original;
            };
        }
        else
        {
            journey.Lifecycle.PreparedAction = cancellation.Cancel;
        }
        RootCommand root = [GuidedCommand(journey.Coordinator, interaction)];
        Func<Task> invoke = () => root.Parse(["init", Target, "--guided", "--stack", "node", "--quickstart", "fixture"])
            .InvokeAsync(new() { EnableDefaultExceptionHandler = false }, cancellation.Token);

        var canceled = await invoke.Should().ThrowAsync<OperationCanceledException>();

        canceled.Which.CancellationToken.Should().Be(interaction.ConfirmationToken);
        if (phase != "consent")
        {
            canceled.Which.InnerException.Should().BeAssignableTo<OperationCanceledException>();
            interaction.AllOutput.Should().Contain("Acquisition canceled").And.Contain("remain").And.Contain(_selection.Owner.PackageId);
        }
        if (phase == "private-acquisition")
        {
            canceled.Which.InnerException.Should().BeSameAs(original);
        }
        if (phase == "prepared-acquisition")
        {
            journey.Lifecycle.Prepared.Should().NotBeNull();
            File.Exists(journey.Lifecycle.Prepared!.Archive).Should().BeFalse();
        }
        using IWorkloadOperation operation = await new FileWorkloadOperationCoordinator(_paths).AcquireAsync(CancellationToken.None);
        operation.Validate(_paths);
        Directory.Exists(Target).Should().BeFalse();
        journey.Lifecycle.ObservedFreshBinding.Should().BeFalse();
        (await _ownership.GetRetainedAsync(PackageId, CancellationToken.None)).Should().BeNull();
    }

    [Fact]
    public async Task Guided_TemplateCommitFailure_ReportsPartialAcquisitionAndRetainedStackWithoutCreating()
    {
        await SeedAsync(installTemplates: false);
        var journey = Guided(new(false, [], []));
        journey.Lifecycle.CommitFailure = new IOException("Injected template store write failure.");

        var result = await journey.Coordinator.ExecuteAsync(new(GuidedRequest(), "fixture", true), CancellationToken.None);

        result.Kind.Should().Be("acquisitionFailed");
        result.Diagnostics.Should().Contain(message => message.Contains("PartialFailure", StringComparison.Ordinal));
        result.Diagnostics.Should().Contain(message => message.Contains(_selection.Owner.PackageId, StringComparison.Ordinal)
            && message.Contains("remain", StringComparison.Ordinal));
        File.Exists(journey.Lifecycle.Prepared!.Archive).Should().BeFalse();
        Directory.Exists(Target).Should().BeFalse();
    }

    [Fact]
    public async Task Guided_PublicParser_AcquisitionIoFailureRemainsOrdinaryFailureAfterCleanup()
    {
        await SeedAsync(installTemplates: false);
        var interaction = new GuidedInteraction(false, [], []);
        var journey = Guided(interaction);
        journey.Lifecycle.CommitFailure = new IOException("Injected template store write failure.");
        RootCommand root = [GuidedCommand(journey.Coordinator, interaction)];

        int exit = await root.Parse(["init", Target, "--guided", "--stack", "node", "--quickstart", "fixture",
            "--acquire-quickstart", "--non-interactive"]).InvokeAsync(new() { EnableDefaultExceptionHandler = false }, CancellationToken.None);

        exit.Should().Be(1);
        interaction.AllOutput.Should().Contain("PartialFailure").And.Contain("remain").And.Contain(_selection.Owner.PackageId);
        File.Exists(journey.Lifecycle.Prepared!.Archive).Should().BeFalse();
        journey.Lifecycle.ObservedFreshBinding.Should().BeFalse();
        Directory.Exists(Target).Should().BeFalse();
    }

    [Fact]
    public async Task Guided_CancellationAfterRealOutputMove_RetainsCommittedFilesWithoutClaimingConfigurationOrRollback()
    {
        await SeedAsync(installTemplates: false);
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
        var journey = Guided(new(false, [], []), installation);

        var result = await journey.Coordinator.ExecuteAsync(new(GuidedRequest(), "fixture", true), cancellation.Token);

        result.Kind.Should().Be("invocationFailed");
        result.Succeeded.Should().BeFalse();
        result.Invocation!.HasCommittedOutput.Should().BeTrue();
        result.Invocation.ConfiguredProjects.Should().BeEmpty();
        moved.Should().ContainSingle();
        File.Exists(moved[0]).Should().BeTrue();
        File.Exists(Path.Combine(Target, ".func", "config.json")).Should().BeFalse();
    }

    [Fact]
    public async Task Guided_PreCanceledRequest_DoesNotInspectOrAcquire()
    {
        await SeedAsync(installTemplates: false);
        var journey = Guided(new(false, [], []));
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();

        Func<Task> execute = () => journey.Coordinator.ExecuteAsync(new(GuidedRequest()), cancellation.Token);

        await execute.Should().ThrowAsync<OperationCanceledException>();
        Directory.Exists(Target).Should().BeFalse();
    }

    private InitExecutionRequest GuidedRequest() => new(DirectoryTarget, null, null, "node", null, false, false, false, false,
        [], new HashSet<string>(StringComparer.Ordinal));

    private (QuickstartInitCoordinator Coordinator, FixturePayloadSource Payload, TrackingLifecycle Lifecycle,
        IQuickstartCatalogService Catalog, QuickstartQualification Qualification, BasicAcquisitionRunner Acquisition)
        Guided(GuidedInteraction interaction, IFileSystem? installation = null)
    {
        var source = Substitute.For<IQuickstartCatalogSource>();
        using var package = JsonDocument.Parse($$"""
            {"schemaVersion":1,"packageId":"{{PackageId}}","version":"{{_selection.Artifact.Version}}","source":"{{Source}}",
             "templateIdentity":"{{Identity}}","artifact":{"algorithm":"sha256","digest":"{{_selection.Artifact.Sha256}}","evidenceReference":"inert"},
             "projects":[{"path":".","stack":"node","language":"JavaScript"}],"requiredWorkloads":["node"]}
            """);
        var remote = Remote();
        source.GetCatalogDocumentAsync(Arg.Any<CancellationToken>()).Returns(new QuickstartCatalogDocument(
            new([remote.Entry]), [new(remote.Entry, package.RootElement.Clone())], new(QuickstartCatalogOrigin.LocalFile, "fixture-only", null)));
        var catalog = new QuickstartCatalogService(source);
        var renderer = new QuickstartCatalogRenderer(interaction);
        var payload = new FixturePayloadSource(new(StringComparer.OrdinalIgnoreCase)
        {
            [$"{PackageId}/{_selection.Artifact.Version}"] = Path.Combine(_root, "exact.nupkg"),
        });
        var lifecycle = new TrackingLifecycle(new BasicTemplateLifecycle(_templates, payload, new BasicArtifactInspector(_files), _ownership, _files));
        var version = Substitute.For<ICliVersionProvider>();
        version.Version.Returns(AssemblyCliVersionProvider.Instance.Version);
        var qualification = new QuickstartQualification(_catalog, new(), lifecycle, _workloads, version);
        var coordinator = new FileWorkloadOperationCoordinator(_paths);
        var deployment = new WorkloadDeploymentService(_paths, _workloads, new WorkloadMetadataReader(), coordinator, _files,
            new BundledTemplateInstaller(_templates, new BasicArtifactInspector(_files), _files, new(Path.Combine(_root, "cli"))));
        var acquisition = new BasicAcquisitionRunner(_catalog, new(),
            new PackageSourceProvider(Options.Create(new WorkloadCatalogOptions())), _workloads, _paths,
            Substitute.For<IWorkloadInstaller>(), lifecycle, _files, coordinator, deployment);
        var arguments = new TemplateArgumentParser();
        var factory = new TemplaterFactory(_templates, installation ?? _files);
        var init = new InitExecutionRunner(factory, arguments, new(arguments, interaction), interaction, new(interaction), new(_files),
            () => throw new InvalidOperationException("Guided flow must refresh capabilities."),
            () => throw new InvalidOperationException("Guided flow must refresh project support."),
            _files, Substitute.For<ILocalSettingsProvider>(), Substitute.For<IFunctionsProjectResolver>(), new AcquisitionBoundaryScopes(_scopes, lifecycle));
        var browser = new FreshBrowser(catalog, _scopes, Factory, interaction);
        return (new(_scopes, Factory, browser, catalog, qualification, acquisition, init, new(_files), interaction, renderer),
            payload, lifecycle, catalog, qualification, acquisition);
    }

    private InitCommand GuidedCommand(IQuickstartInitCoordinator coordinator, IInteractionService interaction,
        IEnumerable<IProjectInitializer>? initializers = null)
        => new(interaction, Substitute.For<IWorkloadHintRenderer>(), Substitute.For<ILocalSettingsProvider>(),
            Substitute.For<IFunctionsProjectResolver>(), Substitute.For<IHostJsonBundleSectionReader>(),
            new(Substitute.For<IInstalledBundleWorkloads>()), _files, initializers ?? [], coordinator);

    private static IProjectInitializer NodeInitializer()
    {
        IProjectInitializer initializer = Substitute.For<IProjectInitializer>();
        initializer.Stack.Returns("node");
        initializer.SupportedLanguages.Returns(["JavaScript"]);
        initializer.SupportedLanguageAliases.Returns(new Dictionary<string, IReadOnlyList<string>>());
        initializer.WorkerRuntimeAliases.Returns(["node"]);
        initializer.GetInitOptions(Arg.Any<IInitOptionRegistry>()).Returns([]);
        initializer.InitializeAsync(Arg.Any<InitContext>(), Arg.Any<ParseResult>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);
        return initializer;
    }

    private Dictionary<string, byte[]> TemplateState()
        => Directory.Exists(_templates.Hive) ? HiveBytes() : [];

    private async Task<WorkloadEntry> SeedGuidedOwnerAsync(string archive)
    {
        var source = Substitute.For<IWorkloadPackageSource>();
        using var reader = new PackageArchiveReader(archive);
        string id = reader.NuspecReader.GetId();
        NuGetVersion version = reader.NuspecReader.GetVersion();
        source.ResolveAsync(id, version, Source, true, true, Arg.Any<CancellationToken>())
            .Returns(new ResolvedPackage(id, version, new(Source)));
        source.DownloadAsync(Arg.Any<ResolvedPackage>(), Arg.Any<IProgress<WorkloadInstallProgress>?>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                string temporary = Path.Combine(_root, Guid.NewGuid().ToString("N") + ".nupkg");
                File.Copy(archive, temporary);
                return new TemporaryWorkloadPackageFile(temporary);
            });
        var runtime = Substitute.For<IWorkloadRuntimeIdentifierProvider>();
        runtime.Current.Returns("win-x64");
        var selector = new WorkloadRidPackageSelector(runtime);
        var metadata = new WorkloadMetadataReader();
        var operation = new FileWorkloadOperationCoordinator(_paths);
        var deployment = new WorkloadDeploymentService(_paths, _workloads, metadata, operation, _files,
            new BundledTemplateInstaller(_templates, new BasicArtifactInspector(_files), _files, new(Path.Combine(_root, "cli"))));
        var installer = new WorkloadInstaller(source, new WorkloadPackageInspector(metadata, selector), selector, deployment, operation, _workloads, _files);
        return (await installer.InstallFromCatalogAsync(id, version, Source, true, true, false, cancellationToken: CancellationToken.None)).Entry;
    }

    private sealed class GuidedInteraction(bool interactive, string[] selections, bool[] confirmations) : TestInteractionService, IInteractionService
    {
        private readonly Queue<string> _selections = new(selections);
        private readonly Queue<bool> _confirmations = new(confirmations);
        public override bool IsInteractive => interactive;
        public List<bool> Confirmations { get; } = [];
        public CancellationToken ConfirmationToken { get; private set; }
        public Func<Task>? Confirming { get; set; }
        public Action? SelectionMade { get; set; }

        Task<string> IInteractionService.PromptForSelectionAsync(string title, IEnumerable<string> choices, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string[] options = [.. choices];
            string selected = _selections.Dequeue();
            WriteLine($"SELECT: {title} [{string.Join(", ", options)}]");
            SelectionMade?.Invoke();
            return Task.FromResult(selected switch
            {
                "first" => options[0],
                "exact" => options.Single(option => option.Contains(Identity, StringComparison.Ordinal)),
                _ => options.Single(option => option == selected),
            });
        }

        public override async Task<bool> ConfirmAsync(string prompt, bool defaultValue, bool whenInputUnavailable, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ConfirmationToken = cancellationToken;
            Confirmations.Add(defaultValue);
            WriteLine($"CONFIRM: {prompt}");
            if (Confirming is not null)
            {
                await Confirming();
            }
            return _confirmations.Dequeue();
        }
    }

    private sealed class TrackingLifecycle(IBasicTemplateLifecycle inner) : IBasicTemplateLifecycle
    {
        public PreparedBasicTemplate? Prepared { get; private set; }
        public bool ObservedFreshBinding { get; set; }
        public IOException? CommitFailure { get; set; }
        public Action? Selecting { get; set; }
        public Action? PreparedAction { get; set; }
        public Task<BasicTemplateSelection> SelectAsync(BasicCompanionPlan plan, TemplateEngineContext context, CancellationToken cancellationToken)
        {
            Selecting?.Invoke();
            return inner.SelectAsync(plan, context, cancellationToken);
        }
        public Task<bool> EnsureAsync(BasicTemplateArtifact artifact, TemplateEngineContext context, bool offline, CancellationToken cancellationToken)
            => inner.EnsureAsync(artifact, context, offline, cancellationToken);
        public async Task<PreparedBasicTemplate> PrepareAsync(BasicTemplateSelection selection, TemplateEngineContext context, bool offline, CancellationToken cancellationToken)
        {
            Prepared = await inner.PrepareAsync(selection, context, offline, cancellationToken);
            PreparedAction?.Invoke();
            return Prepared;
        }
        public Task<bool> CommitAsync(PreparedBasicTemplate prepared, TemplateEngineContext context, CancellationToken cancellationToken)
            => CommitFailure is null ? inner.CommitAsync(prepared, context, cancellationToken) : throw CommitFailure;
        public Task RevalidatePreparedAsync(PreparedBasicTemplate prepared, TemplateEngineContext context, CancellationToken cancellationToken)
            => inner.RevalidatePreparedAsync(prepared, context, cancellationToken);
    }

    private sealed class AcquisitionBoundaryScopes(IBoundInitScopeFactory inner, TrackingLifecycle lifecycle) : IBoundInitScopeFactory
    {
        public Task<BoundInitScope> CreateAsync(CancellationToken cancellationToken)
        {
            if (lifecycle.Prepared is { } prepared)
            {
                File.Exists(prepared.Archive).Should().BeFalse("private acquisition payloads must already be disposed at fresh bound scope entry");
                lifecycle.ObservedFreshBinding = true;
            }
            return inner.CreateAsync(cancellationToken);
        }
    }

    private sealed class FreshBrowser(IQuickstartCatalogService catalog, IBoundInitScopeFactory scopes,
        ITemplaterFactory templates, IInteractionService interaction) : IQuickstartBrowser
    {
        public async Task<QuickstartCatalogEntry?> BrowseAsync(QuickstartBrowseRequest request, CancellationToken cancellationToken)
        {
            using BoundInitScope scope = await scopes.CreateAsync(cancellationToken);
            QuickstartBrowser browser = new(catalog, scope.Catalog, [], interaction, new(interaction), new(templates, scopes));
            return await browser.BrowseAsync(request, cancellationToken);
        }
    }
}
