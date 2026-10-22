// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Text.Json.Nodes;
using Azure.Functions.Cli.Projects;
using Azure.Functions.Cli.Templates.Acquisition;
using Azure.Functions.Cli.Workloads.Storage;
using NuGet.Versioning;

namespace Azure.Functions.Cli.Tests.Templates.Acquisition;

public sealed partial class NativeTemplateAssociationTests
{
    [Theory]
    [InlineData(1, null)]
    [InlineData(1, "required")]
    [InlineData(1, "optional")]
    [InlineData(2, null)]
    [InlineData(2, "Optional")]
    [InlineData(2, "")]
    [InlineData(2, "unknown")]
    public void Read_PresentInvalidKind_RejectsIncludingSchemaOneNull(int schema, string? kind)
    {
        using var fixture = new BasicAcquisitionFixture();
        var document = schema == 1 ? JsonNode.Parse(fixture.Catalog.Json)! : NativeDocument(fixture);
        document["stacks"]![0]!["companions"]![0]!["kind"] = kind;

        Action read = () => new BasicCompanionManifestReader().Read(document.ToJsonString());

        read.Should().Throw<BasicAcquisitionException>().Which.Failure.Should().Be(BasicAcquisitionFailure.InvalidMapping);
    }

    [Theory]
    [InlineData("A")]
    [InlineData("B")]
    [InlineData(null)]
    public void Plan_TwoOptionalProjects_SelectsOnlyExplicitChoiceAndAllRequiredRoles(string? chosen)
    {
        using var fixture = new BasicAcquisitionFixture();
        var manifest = OptionalManifest(fixture);
        var request = NativeRequest(fixture);
        if (chosen is not null)
        {
            request = request with { OptionalChoices = [Choice(manifest, request, chosen)] };
        }

        var plan = new BasicAcquisitionPlanner().Plan(manifest, request, [], BasicAcquisitionFixture.Source)!;

        plan.Companions.Select(companion => companion.PackageId).Should().Equal(chosen is null
            ? [BasicAcquisitionFixture.BasicId, "Fixture.Required.Project"]
            : [BasicAcquisitionFixture.BasicId, "Fixture.Required.Project", $"Fixture.Optional.{chosen}"]);
        plan.Companions[0].Candidates[0].ContentRoles.Should().Contain(content => content.Role == NativeTemplateContentRole.Function);
        plan.Companions[1].Candidates[0].ContentRoles.Should().Contain(content => content.Role == NativeTemplateContentRole.AdditionalProject);
    }

    [Fact]
    public void Plan_UnselectedRetainedOptional_DoesNotConstrainRangesRolesSourceOrHost()
    {
        using var fixture = new BasicAcquisitionFixture();
        var manifest = OptionalManifest(fixture);
        var request = NativeRequest(fixture);
        request = request with { OptionalChoices = [Choice(manifest, request, "A")] };
        var other = manifest.Stacks.First(mapping => mapping.PackageId != request.StackPackageId);
        var retained = other with
        {
            Sources = ["https://foreign.invalid/"],
            NativePolicy = other.NativePolicy! with { HostIdentifier = "foreign" },
            Companions = [new("Fixture.Optional.A", VersionRange.Parse("[99.0.0]"))
            {
                Kind = BasicCompanionKind.Optional,
                RequiredRoles = [NativeTemplateContentRole.Function],
            }],
        };
        manifest = manifest with { Stacks = [manifest.Stacks[0], retained] };

        var plan = new BasicAcquisitionPlanner().Plan(manifest, request,
            [new() { PackageId = retained.PackageId, PackageVersion = "1.0.0", Source = "foreign" }], BasicAcquisitionFixture.Source)!;

        plan.Companions.Last().Candidates.Should().ContainSingle().Which.Version.Should().Be(NuGetVersion.Parse("1.0.0"));
    }

    [Theory]
    [InlineData("owner")]
    [InlineData("logical-owner")]
    [InlineData("source")]
    [InlineData("channel")]
    [InlineData("version")]
    [InlineData("digest")]
    [InlineData("identity")]
    [InlineData("unknown")]
    [InlineData("duplicate")]
    [InlineData("missing-context")]
    [InlineData("host")]
    [InlineData("role")]
    public void Plan_OptionalChoiceMustJoinExactQualifiedCurrentContext(string change)
    {
        using var fixture = new BasicAcquisitionFixture();
        var manifest = OptionalManifest(fixture);
        var request = NativeRequest(fixture);
        var choice = Choice(manifest, request, "A");
        choice = change switch
        {
            "owner" => choice with { Owner = choice.Owner with { PackageId = "Foreign" } },
            "logical-owner" => choice with { Owner = choice.Owner with { LogicalPackageId = "Foreign" } },
            "source" => choice with { Source = "https://foreign.invalid/" },
            "channel" => choice with { Channel = BundleChannel.Preview },
            "version" => choice with { Artifact = choice.Artifact with { Version = "9.0.0" } },
            "digest" => choice with { Artifact = choice.Artifact with { Sha256 = new string('c', 64) } },
            "identity" => choice with { TemplateIdentity = "short-name" },
            "unknown" => choice with { Artifact = choice.Artifact with { PackageId = "Unknown" } },
            _ => choice,
        };
        request = request with { OptionalChoices = change == "duplicate" ? [choice, choice] : [choice] };
        if (change == "missing-context")
        {
            request = request with { NativeContext = null };
        }
        if (change == "host")
        {
            request = request with { NativeContext = request.NativeContext! with { HostIdentifier = "foreign" } };
        }
        if (change == "role")
        {
            manifest = manifest with { Artifacts = [.. manifest.Artifacts.Select(artifact =>
                artifact.PackageId == choice.Artifact.PackageId ? artifact with { ContentRoles = [] } : artifact)] };
        }

        Action plan = () => new BasicAcquisitionPlanner().Plan(manifest, request, [], BasicAcquisitionFixture.Source);

        plan.Should().Throw<BasicAcquisitionException>();
        fixture.Payload.Requested.Should().BeEmpty();
    }

    [Fact]
    public void Plan_ExactNomination_PinsOlderApprovedVersionDespiteNewerCandidate()
    {
        using var fixture = new BasicAcquisitionFixture();
        var manifest = OptionalManifest(fixture);
        var request = NativeRequest(fixture);
        var choice = Choice(manifest, request, "A");
        manifest = manifest with { Artifacts = [.. manifest.Artifacts,
            manifest.Artifacts.Single(artifact => artifact.PackageId == choice.Artifact.PackageId) with
            {
                Version = NuGetVersion.Parse("1.0.1"), Sha256 = new string('b', 64),
            }] };
        request = request with { OptionalChoices = [choice] };

        var plan = new BasicAcquisitionPlanner().Plan(manifest, request, [], BasicAcquisitionFixture.Source)!;

        plan.Companions.Last().Candidates.Should().ContainSingle().Which.Version.Should().Be(NuGetVersion.Parse("1.0.0"));
    }

    [Fact]
    public void Approval_ChoicesAndNestedArtifactInputs_AreFrozenAndConsentCannotReplayDifferentIntent()
    {
        using var fixture = new BasicAcquisitionFixture();
        var manifest = OptionalManifest(fixture);
        var request = NativeRequest(fixture);
        var choice = Choice(manifest, request, "A");
        var choices = new List<NativeTemplateSelection> { choice };
        request = request with { OptionalChoices = choices };
        choices.Clear();
        request.OptionalChoices.Should().Equal(choice);
        var plan = new BasicAcquisitionPlanner().Plan(manifest, request, [], BasicAcquisitionFixture.Source)!;
        var artifacts = plan.Companions.Select(companion => companion.Candidates[0]).ToList();
        var definitions = artifacts[0].Templates.ToList();
        artifacts[0] = artifacts[0] with { Templates = definitions };
        var approval = new BasicAcquisitionApproval(plan, artifacts);
        artifacts.Clear();
        definitions.Clear();
        approval.Validate(plan);
        approval.Artifacts[0].Templates.Should().NotBeEmpty();
        ((IList<NativeTemplateSelection>)plan.OptionalChoices).IsReadOnly.Should().BeTrue();
        var changed = plan with { OptionalChoices = [] };

        Action validate = () => approval.Validate(changed);

        validate.Should().Throw<BasicAcquisitionException>().Which.Failure.Should().Be(BasicAcquisitionFailure.PlanChanged);
        approval.Bind(plan.Companions.Last()).Candidates.Should().ContainSingle()
            .Which.Version.Should().Be(NuGetVersion.Parse("1.0.0"));
    }

    [Fact]
    public void Plan_OmittedAndExplicitRequiredKind_PreserveAllRequiredNoChoiceBehavior()
    {
        using var fixture = new BasicAcquisitionFixture();
        var reader = new BasicCompanionManifestReader();
        var document = NativeDocument(fixture);
        var omitted = new BasicAcquisitionPlanner().Plan(reader.Read(document.ToJsonString()), NativeRequest(fixture), [],
            BasicAcquisitionFixture.Source)!;
        foreach (var stack in document["stacks"]!.AsArray())
        {
            stack!["companions"]![0]!["kind"] = "required";
        }
        var explicitPlan = new BasicAcquisitionPlanner().Plan(reader.Read(document.ToJsonString()), NativeRequest(fixture), [],
            BasicAcquisitionFixture.Source)!;

        new BasicAcquisitionApproval(omitted, [omitted.Companions[0].Candidates[0]]).Validate(explicitPlan);
    }

    [Fact]
    public void Read_AllOptionalMapping_IsValidButCannotDischargeAnUnselectedMandatoryRole()
    {
        using var fixture = new BasicAcquisitionFixture();
        var document = NativeDocument(fixture);
        document["stacks"]![0]!["companions"]![0]!["kind"] = "optional";
        var manifest = new BasicCompanionManifestReader().Read(document.ToJsonString());
        manifest.Stacks[0].Companions.Should().OnlyContain(companion => companion.Kind == BasicCompanionKind.Optional);

        Action plan = () => new BasicAcquisitionPlanner().Plan(manifest, NativeRequest(fixture), [], BasicAcquisitionFixture.Source);

        plan.Should().Throw<BasicAcquisitionException>().Which.Failure.Should().Be(BasicAcquisitionFailure.MissingMapping);
    }

    private static BasicCompanionManifest OptionalManifest(BasicAcquisitionFixture fixture)
    {
        var document = NativeDocument(fixture);
        var companions = document["stacks"]![0]!["companions"]!.AsArray();
        foreach (string id in new[] { "Fixture.Required.Project", "Fixture.Optional.A", "Fixture.Optional.B" })
        {
            companions.Add(new JsonObject
            {
                ["packageId"] = id, ["versions"] = "[1.0.0,2.0.0)", ["requiredRoles"] = new JsonArray("additional-project"),
                ["kind"] = id.Contains("Optional", StringComparison.Ordinal) ? "optional" : "required",
            });
            var artifact = document["artifacts"]![0]!.DeepClone();
            artifact["packageId"] = id;
            artifact["version"] = "1.0.0";
            artifact["templates"]![0]!["identity"] = id + ".Project";
            artifact["templates"]![0]!["type"] = "project";
            artifact["templates"]![0]!["contentRole"] = "additional-project";
            document["artifacts"]!.AsArray().Add(artifact);
        }
        return new BasicCompanionManifestReader().Read(document.ToJsonString());
    }

    private static NativeTemplateSelection Choice(BasicCompanionManifest manifest, BasicAcquisitionRequest request, string name)
    {
        var artifact = manifest.Artifacts.Single(artifact => artifact.PackageId == "Fixture.Optional." + name);
        return new(artifact.Templates.Single().Identity, new(artifact.PackageId, artifact.Version.ToNormalizedString(), artifact.Sha256),
            artifact.Source, NativeTemplateOwnerIdentity.FromEntry(request.NativeContext!.SelectedStack), request.NativeContext.Channel);
    }
}
