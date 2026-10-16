// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Text.Json.Nodes;
using Azure.Functions.Cli.Templates.Acquisition;
using Azure.Functions.Cli.Workloads.Storage;
using NuGet.Versioning;

namespace Azure.Functions.Cli.Tests.Templates.Acquisition;

public sealed class BasicCompanionManifestTests
{
    [Fact]
    public async Task BundledCatalog_ContainsNoFabricatedProductionApprovals_PlanningIsVisiblyUnavailable()
    {
        var manifest = await new BundledBasicCompanionCatalog(new BasicCompanionManifestReader()).LoadAsync(CancellationToken.None);
        using var fixture = new BasicAcquisitionFixture();

        Action plan = () => new BasicAcquisitionPlanner().Plan(manifest, fixture.Request(), [], BasicAcquisitionFixture.Source);

        manifest.Stacks.Should().BeEmpty();
        manifest.Artifacts.Should().BeEmpty();
        plan.Should().Throw<BasicAcquisitionException>().Which.Failure.Should().Be(BasicAcquisitionFailure.MissingMapping);
    }

    [Theory]
    [InlineData("null")]
    [InlineData("{}")]
    [InlineData("{\"schemaVersion\":2,\"revision\":\"x\",\"stacks\":[],\"artifacts\":[]}")]
    [InlineData("{\"schemaVersion\":1,\"revision\":\"x\",\"revision\":\"y\",\"stacks\":[],\"artifacts\":[]}")]
    [InlineData("{\"schemaVersion\":1,\"revision\":\"x\",\"stacks\":[],\"artifacts\":[],\"publishing\":true}")]
    [InlineData("{\"schemaVersion\":1,\"revision\":\" \",\"stacks\":[],\"artifacts\":[]}")]
    public void Read_InvalidSchemaRevisionDuplicateOrUnknownProperty_FailsExplicitly(string json)
    {
        Action read = () => new BasicCompanionManifestReader().Read(json);

        read.Should().Throw<BasicAcquisitionException>().Which.Failure.Should().Be(BasicAcquisitionFailure.InvalidMapping);
    }

    [Theory]
    [InlineData("null-stack")]
    [InlineData("null-artifact")]
    [InlineData("overlapping-stack")]
    [InlineData("duplicate-artifact")]
    [InlineData("no-basic-qualification")]
    [InlineData("empty-inventory")]
    [InlineData("duplicate-inventory")]
    [InlineData("unsafe-path")]
    [InlineData("floating-range")]
    [InlineData("inverted-range")]
    [InlineData("invalid-hash")]
    [InlineData("invalid-source")]
    public void Read_AmbiguousOrUnqualifiedMapping_FailsExplicitly(string change)
    {
        using var fixture = new BasicAcquisitionFixture();
        JsonNode root = JsonNode.Parse(fixture.Catalog.Json)!;
        switch (change)
        {
            case "null-stack": root["stacks"]!.AsArray().Add(null); break;
            case "null-artifact": root["artifacts"]!.AsArray().Add(null); break;
            case "overlapping-stack": root["stacks"]!.AsArray().Add(root["stacks"]![0]!.DeepClone()); break;
            case "duplicate-artifact": root["artifacts"]!.AsArray().Add(root["artifacts"]![0]!.DeepClone()); break;
            case "no-basic-qualification": root["artifacts"]![0]!["basicOnly"] = false; break;
            case "empty-inventory": root["artifacts"]![0]!["templates"]!.AsArray().Clear(); break;
            case "duplicate-inventory": root["artifacts"]![0]!["templates"]!.AsArray().Add(root["artifacts"]![0]!["templates"]![0]!.DeepClone()); break;
            case "unsafe-path": root["artifacts"]![0]!["templates"]![0]!["path"] = "../basic/.template.config/template.json"; break;
            case "floating-range": root["stacks"]![0]!["versions"] = "1.*"; break;
            case "inverted-range": root["stacks"]![0]!["versions"] = "[2,1)"; break;
            case "invalid-hash": root["artifacts"]![0]!["sha256"] = "x"; break;
            case "invalid-source": root["stacks"]![0]!["sources"]![0] = "https://user:secret@fixtures.invalid/index.json"; break;
        }

        Action read = () => new BasicCompanionManifestReader().Read(root.ToJsonString());

        read.Should().Throw<BasicAcquisitionException>().Which.Failure.Should().Be(BasicAcquisitionFailure.InvalidMapping);
    }

    [Fact]
    public void Plan_LiteralAndLogicalInstalledIdentities_NotAliasesOrSelection_ConstrainSharedRange()
    {
        using var fixture = new BasicAcquisitionFixture();
        var manifest = new BasicCompanionManifestReader().Read(fixture.Catalog.Json);
        WorkloadEntry[] installed =
        [
            new() { PackageId = "Fixture.StackA", PackageVersion = "1.0.0", Aliases = ["arbitrary"] },
            new() { PackageId = "Unrelated.Physical", PackageVersion = "1.0.0", Aliases = ["Fixture.StackB"] },
        ];

        var plan = new BasicAcquisitionPlanner().Plan(manifest, fixture.Request("Fixture.StackC"), installed, BasicAcquisitionFixture.Source);

        plan!.Companions.Should().ContainSingle().Which.Candidates.Should().ContainSingle()
            .Which.Version.Should().Be(NuGetVersion.Parse("1.0.0"));
    }

    [Fact]
    public void Plan_RetainedUnknownApprovedStackVersion_DoesNotSilentlyOmitRequirement()
    {
        using var fixture = new BasicAcquisitionFixture();
        var manifest = new BasicCompanionManifestReader().Read(fixture.Catalog.Json);
        WorkloadEntry[] installed = [new() { PackageId = "Fixture.StackA", PackageVersion = "9.0.0" }];

        Action plan = () => new BasicAcquisitionPlanner().Plan(manifest, fixture.Request("Fixture.StackC"), installed, BasicAcquisitionFixture.Source);

        plan.Should().Throw<BasicAcquisitionException>().Which.Failure.Should().Be(BasicAcquisitionFailure.MissingMapping);
    }

    [Fact]
    public void Plan_PermutingRetainedRows_DoesNotChangeSelectedExactArtifacts()
    {
        using var fixture = new BasicAcquisitionFixture();
        var manifest = new BasicCompanionManifestReader().Read(fixture.Catalog.Json);
        WorkloadEntry[] rows =
        [
            new() { PackageId = "Fixture.StackA", PackageVersion = "1.0.0" },
            new() { PackageId = "Fixture.StackC", PackageVersion = "1.0.0" },
        ];
        var planner = new BasicAcquisitionPlanner();

        var forward = planner.Plan(manifest, fixture.Request("Fixture.StackC"), rows, BasicAcquisitionFixture.Source);
        var reverse = planner.Plan(manifest, fixture.Request("Fixture.StackC"), [.. rows.Reverse()], BasicAcquisitionFixture.Source);

        reverse.Should().BeEquivalentTo(forward);
    }

    [Fact]
    public void ReadAndPlan_NullArguments_RejectProgrammerErrors()
    {
        Action nullJson = () => new BasicCompanionManifestReader().Read(null!);
        Action nullReader = () => new BundledBasicCompanionCatalog(null!);
        Action nullManifest = () => new BasicAcquisitionPlanner().Plan(null!, null!, [], BasicAcquisitionFixture.Source);

        nullJson.Should().Throw<ArgumentNullException>();
        nullReader.Should().Throw<ArgumentNullException>();
        nullManifest.Should().Throw<ArgumentNullException>();
    }
}
