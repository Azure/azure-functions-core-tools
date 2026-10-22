// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.CommandLine;
using System.Text.Json;
using System.Text.Json.Nodes;
using Azure.Functions.Cli.Hosting;
using Azure.Functions.Cli.Quickstart;
using Azure.Functions.Cli.Templates;
using Azure.Functions.Cli.Templates.Acquisition;
using Azure.Functions.Cli.Templates.Engine;
using Azure.Functions.Cli.Workloads;
using Azure.Functions.Cli.Workloads.Install;

namespace Azure.Functions.Cli.Tests.Templates;

public sealed partial class ArtifactBindingJourneyTests
{
    [Fact]
    public async Task Optional_PublicBrowseAcquisitionAndRepeatedInstalledReuse_NeverPlansOrAcquiresUnselectedB()
    {
        await SeedAsync(installTemplates: false);
        AddUnselectedOptionalAuthority();
        GuidedInteraction interaction = new(true, ["Browse more templates", "first", "Use this template"], [true, true]);
        var journey = Guided(interaction);
        RootCommand root = [GuidedCommand(journey.Coordinator, interaction, [NodeInitializer()])];

        int exit = await root.Parse(["init", Target]).InvokeAsync();

        exit.Should().Be(0, interaction.AllOutput);
        journey.Payload.Requested.Should().Equal($"{PackageId}/1.0.0");
        File.ReadAllText(Path.Combine(Target, "value.txt")).Should().Be("exact");
        var projection = await new QuickstartInstalledTemplates(Factory, _scopes).ProjectAsync(
            await journey.Catalog.SearchAsync(new(), CancellationToken.None), new(DirectoryTarget, _selection.Owner), CancellationToken.None);
        projection.Catalog.Entries.Should().BeEmpty();
        var expectedSelection = _selection with { Channel = null };
        projection.Installed.Should().Contain(item => item.Selection == expectedSelection);
        GuidedInteraction repeatedInteraction = new(true, ["exact"], [true]);
        var repeated = Guided(repeatedInteraction);
        RootCommand second = [GuidedCommand(repeated.Coordinator, repeatedInteraction, [NodeInitializer()])];
        string secondTarget = Path.Combine(_root, "repeat-optional");

        int repeat = await second.Parse(["init", secondTarget]).InvokeAsync();

        repeat.Should().Be(0, repeatedInteraction.AllOutput);
        repeated.Payload.Requested.Should().BeEmpty();
        File.ReadAllText(Path.Combine(secondTarget, "value.txt")).Should().Be("exact");
    }

    [Theory]
    [InlineData("kind")]
    [InlineData("digest")]
    [InlineData("inventory")]
    public async Task Optional_AuthorityChangesDuringPreparation_RejectsWriterReplanWithoutCommit(string change)
    {
        await SeedAsync(installTemplates: false);
        AddUnselectedOptionalAuthority();
        var journey = Guided(new(false, [], []));
        journey.Payload.Opening = _ =>
        {
            var document = JsonNode.Parse(_catalog.Json)!;
            if (change == "kind")
            {
                document["stacks"]![0]!["companions"]![1]!["kind"] = "required";
            }
            else if (change == "digest")
            {
                document["artifacts"]![0]!["sha256"] = new string('b', 64);
            }
            else
            {
                document["artifacts"]![0]!["templates"]![0]!["identity"] = "Changed.Project";
            }
            _catalog.Json = document.ToJsonString();
        };

        var result = await journey.Coordinator.ExecuteAsync(new(GuidedRequest(), "fixture", true), CancellationToken.None);

        result.Kind.Should().Be("acquisitionFailed");
        journey.Payload.Requested.Should().Equal($"{PackageId}/1.0.0");
        (await _ownership.GetRetainedAsync(PackageId, CancellationToken.None)).Should().BeNull();
        Directory.Exists(Target).Should().BeFalse();
        File.Exists(journey.Lifecycle.Prepared!.Archive).Should().BeFalse();
    }

    [Fact]
    public async Task Optional_IntentWithoutConsent_RefusesBeforePayload()
    {
        await SeedAsync(installTemplates: false);
        AddUnselectedOptionalAuthority();
        var journey = Guided(new(false, [], []));
        var owner = (await _workloads.GetWorkloadsAsync(CancellationToken.None)).Single();
        var qualified = await journey.Qualification.ResolveAsync(Remote(), new(DirectoryTarget, _selection.Owner), owner, CancellationToken.None);

        var result = await journey.Acquisition.RunAsync(qualified.Acquisition with { Approval = null }, CancellationToken.None);

        result.Failure.Should().Be(BasicAcquisitionFailure.PlanChanged);
        journey.Payload.Requested.Should().BeEmpty();
        (await _ownership.GetRetainedAsync(PackageId, CancellationToken.None)).Should().BeNull();
        Directory.Exists(Target).Should().BeFalse();
    }

    [Fact]
    public async Task Bundled_DefaultPublicMenu_SelectsActualBasicArchiveThenRepeatsWithoutExternalReceiptOrAcquisition()
    {
        await SeedAsync(installTemplates: false);
        var scopes = BundledScopes();
        GuidedInteraction interaction = new(true, ["bundled-js"], [true]);
        var journey = Guided(interaction, scopes: scopes);
        RootCommand root = [GuidedCommand(journey.Coordinator, interaction, [NodeInitializer()])];
        var before = TemplateState();

        int exit = await root.Parse(["init", Target]).InvokeAsync();

        exit.Should().Be(0, interaction.AllOutput);
        interaction.SelectionChoices.Should().ContainSingle().Which.Should()
            .Contain(option => option.Contains("Azure.Functions.Node.Basic.JavaScript", StringComparison.Ordinal))
            .And.Contain("Basic Functions app").And.Contain("Browse more templates").And.Contain("Cancel");
        interaction.Confirmations.Should().Equal(false);
        journey.Payload.Requested.Should().BeEmpty();
        File.Exists(Path.Combine(Target, "package.json")).Should().BeTrue();
        using var configuration = JsonDocument.Parse(File.ReadAllText(Path.Combine(Target, ".func", "config.json")));
        configuration.RootElement.GetProperty("stack").GetProperty("runtime").GetString().Should().Be("node");
        configuration.RootElement.GetProperty("stack").GetProperty("language").GetString().Should().Be("JavaScript");
        (await _ownership.GetRetainedAsync("Azure.Functions.Templates.Node.Basic", CancellationToken.None)).Should().BeNull();
        TemplateState().Should().Equal(before);

        GuidedInteraction secondInteraction = new(true, ["bundled-js"], [true]);
        var repeated = Guided(secondInteraction, scopes: scopes);
        RootCommand second = [GuidedCommand(repeated.Coordinator, secondInteraction, [NodeInitializer()])];
        string secondTarget = Path.Combine(_root, "repeat-bundled");

        (await second.Parse(["init", secondTarget]).InvokeAsync()).Should().Be(0, secondInteraction.AllOutput);
        repeated.Payload.Requested.Should().BeEmpty();
        File.Exists(Path.Combine(secondTarget, ".func", "config.json")).Should().BeTrue();
        TemplateState().Should().Equal(before);
    }

    [Theory]
    [InlineData("origin")]
    [InlineData("foreign-owner")]
    [InlineData("missing-receipt")]
    [InlineData("malformed-receipt")]
    [InlineData("duplicate-receipt")]
    [InlineData("declaration")]
    [InlineData("missing-declaration")]
    [InlineData("archive")]
    [InlineData("registry")]
    public async Task Bundled_ChangedProofAfterSelection_RefusesWithoutRepairOrProjectWrites(string change)
    {
        await SeedAsync(installTemplates: false);
        var scopes = BundledScopes();
        BundledBasicSelection selection;
        using (BoundInitScope scope = await scopes.CreateAsync(CancellationToken.None))
        using (Templater flow = await Factory.CreateProjectAsync(Context, scope.Availability, scope.Support, CancellationToken.None))
        {
            selection = (await flow.ProjectArtifactsAsync(scope.Verifier, _selection.Owner, null, CancellationToken.None, scope.BundledVerifier))
                .Single(item => item.Entry.Identity == "Azure.Functions.Node.Basic.JavaScript").BundledSelection!;
        }
        selection.Should().NotBeNull();
        var owner = (await _workloads.GetWorkloadsAsync(CancellationToken.None)).Single();
        string directory = _paths.GetInstallDirectory(owner.PackageId, owner.PackageVersion);
        string receipt = Path.Combine(_root, "cli", "templates", ".bundled-owners", selection.Artifact.PackageId.ToLowerInvariant() + ".json");
        if (change == "origin")
        {
            File.WriteAllText(Path.Combine(directory, BundledTemplateInstaller.OriginName), "{}");
        }
        else if (change == "foreign-owner")
        {
            selection = selection with { Owner = selection.Owner with { LogicalPackageId = "Foreign" } };
        }
        else if (change == "missing-receipt")
        {
            File.Delete(receipt);
        }
        else if (change == "malformed-receipt")
        {
            File.WriteAllText(receipt, "{invalid");
        }
        else if (change == "duplicate-receipt")
        {
            string json = File.ReadAllText(receipt);
            File.WriteAllText(receipt, "{\"packageId\":\"Foreign\"," + json[1..]);
        }
        else if (change == "declaration")
        {
            var document = JsonNode.Parse(File.ReadAllText(Path.Combine(directory, BundledTemplateInstaller.ManifestName)))!;
            document["assets"]![0]!["templates"]![0]!["identity"] = "Changed";
            File.WriteAllText(Path.Combine(directory, BundledTemplateInstaller.ManifestName), document.ToJsonString());
        }
        else if (change == "missing-declaration")
        {
            File.Delete(Path.Combine(directory, BundledTemplateInstaller.ManifestName));
        }
        else if (change == "archive")
        {
            using var stream = File.Open(Path.Combine(directory, "tools", "any", "templates", "node-basic.nupkg"), FileMode.Append);
            stream.WriteByte(0);
        }
        else
        {
            await _workloads.SaveWorkloadAsync(new() { PackageId = "Changed.Registry", PackageVersion = "1.0.0", Kind = WorkloadKind.Content }, CancellationToken.None);
        }
        var before = TemplateState();
        var request = Request(dryRun: true) with { ArtifactBinding = null, BundledBinding = selection };
        if (change == "registry")
        {
            using BoundInitScope stale = await scopes.CreateAsync(CancellationToken.None);
            using Templater flow = await Factory.CreateProjectAsync(Context, stale.Availability, stale.Support, CancellationToken.None);
            var entry = (await flow.ListAsync(TemplateType.Project)).Single(item => item.Identity == selection.TemplateIdentity);
            var bound = await flow.BindProjectAsync(entry, selection, stale.BundledVerifier!, CancellationToken.None);
            await _workloads.SaveWorkloadAsync(new() { PackageId = "Changed.Again", PackageVersion = "1.0.0", Kind = WorkloadKind.Content }, CancellationToken.None);
            var invoked = await bound.Group![0].InvokeAsync(new(DirectoryTarget, new Dictionary<string, string?>(), DryRun: true), CancellationToken.None);
            invoked.Status.Should().Be(TemplateInvocationStatus.Failed);
        }
        else
        {
            var result = await Runner(scopeFactory: scopes).ExecuteAsync(request, new([]), CancellationToken.None);
            result.Kind.Should().Be("bindingMismatch");
        }
        Directory.Exists(Target).Should().BeFalse();
        TemplateState().Should().Equal(before);
    }

    private BoundInitScopeFactory BundledScopes() => new(_paths, _workloads, _interaction, _files, _verifier,
        new(new BundledTemplateInstaller(_templates, new BasicArtifactInspector(_files), _files, new(Path.Combine(_root, "cli"))), _workloads, _files));

    private void AddUnselectedOptionalAuthority()
    {
        var document = JsonNode.Parse(_catalog.Json)!;
        document["stacks"]![0]!["companions"]![0]!["kind"] = "optional";
        var companion = document["stacks"]![0]!["companions"]![0]!.DeepClone();
        companion["packageId"] = "Fixture.Unselected.B";
        document["stacks"]![0]!["companions"]!.AsArray().Add(companion);
        var artifact = document["artifacts"]![0]!.DeepClone();
        artifact["packageId"] = "Fixture.Unselected.B";
        artifact["templates"]![0]!["identity"] = "Fixture.Unselected.B.Project";
        artifact["templates"]![1]!["identity"] = "Fixture.Unselected.B.Sibling";
        document["artifacts"]!.AsArray().Add(artifact);
        _catalog.Json = document.ToJsonString();
    }
}
