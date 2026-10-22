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
    [Fact]
    public void Plan_ExplicitNativeAssociation_AllowsIndependentCompanionVersion()
    {
        using var fixture = new BasicAcquisitionFixture();
        var document = NativeDocument(fixture);
        document["stacks"]![0]!["companions"]![0]!["versions"] = "[2.0.0,3.0.0)";
        var manifest = new BasicCompanionManifestReader().Read(document.ToJsonString());

        var plan = new BasicAcquisitionPlanner().Plan(manifest, NativeRequest(fixture), [], BasicAcquisitionFixture.Source);

        manifest.SchemaVersion.Should().Be(2);
        manifest.Stacks[0].NativePolicy!.HostIdentifier.Should().Be("func");
        plan!.Companions.Should().ContainSingle().Which.Candidates.Should().ContainSingle()
            .Which.Version.Should().Be(NuGetVersion.Parse("2.0.0"));
        manifest.Artifacts[0].ContentRoles.Should().ContainSingle()
            .Which.Role.Should().Be(NativeTemplateContentRole.Function);
    }

    [Theory]
    [InlineData("missing-host")]
    [InlineData("wrong-host")]
    [InlineData("missing-host-range")]
    [InlineData("floating-host-range")]
    [InlineData("inverted-host-range")]
    [InlineData("missing-channel")]
    [InlineData("unknown-channel")]
    [InlineData("duplicate-channel")]
    [InlineData("missing-required-role")]
    [InlineData("unknown-required-role")]
    [InlineData("duplicate-required-role")]
    [InlineData("missing-content-role")]
    [InlineData("wrong-content-role")]
    [InlineData("role-type-mismatch")]
    [InlineData("legacy-basic-qualification")]
    public void Read_UnqualifiedNativePolicy_RejectsExplicitly(string change)
    {
        using var fixture = new BasicAcquisitionFixture();
        var document = NativeDocument(fixture);
        var stack = document["stacks"]![0]!;
        var requirement = stack["companions"]![0]!;
        var artifact = document["artifacts"]![0]!;
        var definition = artifact["templates"]![0]!;
        switch (change)
        {
            case "missing-host": stack.AsObject().Remove("hostIdentifier"); break;
            case "wrong-host": stack["hostIdentifier"] = "dotnetcli"; break;
            case "missing-host-range": stack.AsObject().Remove("engineHostVersions"); break;
            case "floating-host-range": stack["engineHostVersions"] = "5.*"; break;
            case "inverted-host-range": stack["engineHostVersions"] = "[6,5)"; break;
            case "missing-channel": stack.AsObject().Remove("channels"); break;
            case "unknown-channel": stack["channels"] = new JsonArray("unknown"); break;
            case "duplicate-channel": stack["channels"] = new JsonArray("stable", "stable"); break;
            case "missing-required-role": requirement.AsObject().Remove("requiredRoles"); break;
            case "unknown-required-role": requirement["requiredRoles"] = new JsonArray("basic"); break;
            case "duplicate-required-role": requirement["requiredRoles"] = new JsonArray("function", "function"); break;
            case "missing-content-role": definition.AsObject().Remove("contentRole"); break;
            case "wrong-content-role": definition["contentRole"] = "basic"; break;
            case "role-type-mismatch": definition["type"] = "project"; break;
            case "legacy-basic-qualification": artifact["basicOnly"] = true; break;
        }

        Action read = () => new BasicCompanionManifestReader().Read(document.ToJsonString());

        read.Should().Throw<BasicAcquisitionException>().Which.Failure.Should().Be(BasicAcquisitionFailure.InvalidMapping);
    }

    [Theory]
    [InlineData("host", nameof(BasicAcquisitionFailure.HostConflict))]
    [InlineData("host-version", nameof(BasicAcquisitionFailure.HostConflict))]
    [InlineData("channel", nameof(BasicAcquisitionFailure.ChannelConflict))]
    [InlineData("source", nameof(BasicAcquisitionFailure.SourceConflict))]
    [InlineData("role", nameof(BasicAcquisitionFailure.MissingMapping))]
    [InlineData("identity", nameof(BasicAcquisitionFailure.PlanChanged))]
    [InlineData("version", nameof(BasicAcquisitionFailure.PlanChanged))]
    public void Plan_SelectedContextMismatch_FailsWithoutFallback(string change, string expected)
    {
        using var fixture = new BasicAcquisitionFixture();
        var manifest = new BasicCompanionManifestReader().Read(NativeDocument(fixture).ToJsonString());
        var request = NativeRequest(fixture);
        var context = request.NativeContext!;
        string source = BasicAcquisitionFixture.Source;
        switch (change)
        {
            case "host": context = context with { HostIdentifier = "dotnetcli" }; break;
            case "host-version": context = context with { EngineHostVersion = NuGetVersion.Parse("6.0.0") }; break;
            case "channel": context = context with { Channel = BundleChannel.Preview }; break;
            case "source": source = "https://different.invalid/v3/index.json"; break;
            case "role": context = context with { RequiredRoles = [NativeTemplateContentRole.AdditionalProject] }; break;
            case "identity": request = request with { StackPackageId = "Unqualified.Stack" }; break;
            case "version": request = request with { StackVersion = NuGetVersion.Parse("2.0.0") }; break;
        }
        request = request with { NativeContext = context };

        Action plan = () => new BasicAcquisitionPlanner().Plan(manifest, request, [], source);

        plan.Should().Throw<BasicAcquisitionException>().Which.Failure.ToString().Should().Be(expected);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Plan_LegacyMappingOrMissingNativeContext_CannotSatisfyExternalRoles(bool legacy)
    {
        using var fixture = new BasicAcquisitionFixture();
        var reader = new BasicCompanionManifestReader();
        var manifest = reader.Read(legacy ? fixture.Catalog.Json : NativeDocument(fixture).ToJsonString());
        var request = legacy ? NativeRequest(fixture) : fixture.Request();

        Action plan = () => new BasicAcquisitionPlanner().Plan(manifest, request, [], BasicAcquisitionFixture.Source);

        plan.Should().Throw<BasicAcquisitionException>().Which.Failure.Should().Be(BasicAcquisitionFailure.MissingMapping);
    }

    [Fact]
    public async Task Plan_EmptyProductionAuthority_BundledDeclarationDoesNotDischargeExternalRoles()
    {
        using var fixture = new BasicAcquisitionFixture();
        var manifest = await new BundledBasicCompanionCatalog(new BasicCompanionManifestReader()).LoadAsync(CancellationToken.None);
        var request = NativeRequest(fixture);
        request.NativeContext!.SelectedStack.BundledTemplatesDeclared.Should().BeTrue();

        Action plan = () => new BasicAcquisitionPlanner().Plan(manifest, request, [], BasicAcquisitionFixture.Source);

        plan.Should().Throw<BasicAcquisitionException>().Which.Failure.Should().Be(BasicAcquisitionFailure.MissingMapping);
        manifest.Stacks.Should().BeEmpty();
        manifest.Artifacts.Should().BeEmpty();
    }

    [Fact]
    public void Plan_NativeRetainedSharedRequirement_IntersectsCompatibleReleaseRanges()
    {
        using var fixture = new BasicAcquisitionFixture();
        var manifest = new BasicCompanionManifestReader().Read(NativeDocument(fixture).ToJsonString());
        var request = NativeRequest(fixture);
        request = request with
        {
            StackPackageId = "Fixture.StackC",
            NativeContext = request.NativeContext! with
            {
                SelectedStack = new WorkloadEntry { PackageId = "Fixture.StackC", PackageVersion = "1.0.0", Source = BasicAcquisitionFixture.Source },
            },
        };
        WorkloadEntry[] retained = [new() { PackageId = "Fixture.StackB", PackageVersion = "1.0.0", Source = BasicAcquisitionFixture.Source }];

        var plan = new BasicAcquisitionPlanner().Plan(manifest, request, retained, BasicAcquisitionFixture.Source);

        plan!.Companions.Should().ContainSingle().Which.Candidates.Should().ContainSingle()
            .Which.Version.Should().Be(NuGetVersion.Parse("2.0.0"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Plan_NativeRetainedSharedSourceConflict_UsesActualLogicalOrPhysicalOrigin(bool logical)
    {
        using var fixture = new BasicAcquisitionFixture();
        var manifest = new BasicCompanionManifestReader().Read(NativeDocument(fixture).ToJsonString());
        var request = NativeRequest(fixture);
        request = request with
        {
            StackPackageId = "Fixture.StackC",
            NativeContext = request.NativeContext! with
            {
                SelectedStack = new WorkloadEntry { PackageId = "Fixture.StackC", PackageVersion = "1.0.0", Source = BasicAcquisitionFixture.Source },
            },
        };
        var other = new WorkloadEntry
        {
            PackageId = logical ? "Physical.Other" : "Fixture.StackA",
            PackageVersion = "1.0.0",
            Source = logical ? BasicAcquisitionFixture.Source : "https://other.invalid/v3/index.json",
            LogicalPackage = logical ? new LogicalPackage
            {
                PackageId = "Fixture.StackA", PackageVersion = "1.0.0", Source = "https://other.invalid/v3/index.json",
            } : null,
        };

        Action plan = () => new BasicAcquisitionPlanner().Plan(manifest, request, [other], BasicAcquisitionFixture.Source);

        plan.Should().Throw<BasicAcquisitionException>().Which.Failure.Should().Be(BasicAcquisitionFailure.SourceConflict);
    }

    [Fact]
    public void Plan_IndependentNativeRequirement_DoesNotRepairOrConstrainUnrelatedPackageSource()
    {
        using var fixture = new BasicAcquisitionFixture();
        fixture.UseIndependentBCompanion();
        var manifest = new BasicCompanionManifestReader().Read(NativeDocument(fixture).ToJsonString());
        WorkloadEntry[] retained = [new() { PackageId = "Fixture.StackB", PackageVersion = "1.0.0", Source = "https://other.invalid/v3/index.json" }];

        var plan = new BasicAcquisitionPlanner().Plan(manifest, NativeRequest(fixture), retained, BasicAcquisitionFixture.Source);

        plan!.Companions.Should().ContainSingle().Which.PackageId.Should().Be(BasicAcquisitionFixture.BasicId);
        plan.Companions[0].Candidates.Should().ContainSingle().Which.Version.Should().Be(NuGetVersion.Parse("1.0.0"));
    }

    [Fact]
    public void Plan_UnspecifiedChannel_CannotSelectAnUnqualifiedStableFallback()
    {
        using var fixture = new BasicAcquisitionFixture();
        var document = NativeDocument(fixture);
        foreach (var stack in document["stacks"]!.AsArray())
        {
            stack!["channels"] = new JsonArray("preview");
        }
        var manifest = new BasicCompanionManifestReader().Read(document.ToJsonString());
        var request = NativeRequest(fixture);
        request = request with { NativeContext = request.NativeContext! with { Channel = null }, IncludePrerelease = true };

        Action plan = () => new BasicAcquisitionPlanner().Plan(manifest, request, [], BasicAcquisitionFixture.Source);

        plan.Should().Throw<BasicAcquisitionException>().Which.Failure.Should().Be(BasicAcquisitionFailure.MissingMapping);
    }

    [Fact]
    public void Plan_ActualPrereleaseHost_DoesNotCoerceItToAStableRelease()
    {
        using var fixture = new BasicAcquisitionFixture();
        var document = NativeDocument(fixture);
        document["stacks"]![0]!["engineHostVersions"] = "[5.0.0,6.0.0)";
        var manifest = new BasicCompanionManifestReader().Read(document.ToJsonString());

        Action plan = () => new BasicAcquisitionPlanner().Plan(manifest, NativeRequest(fixture), [], BasicAcquisitionFixture.Source);

        plan.Should().Throw<BasicAcquisitionException>().Which.Failure.Should().Be(BasicAcquisitionFailure.HostConflict);
    }

    private static BasicAcquisitionRequest NativeRequest(BasicAcquisitionFixture fixture)
        => fixture.Request() with
        {
            NativeContext = new(
                new WorkloadEntry { PackageId = "Fixture.StackA", PackageVersion = "1.0.0",
                    Source = BasicAcquisitionFixture.Source, BundledTemplatesDeclared = true },
                "func", NuGetVersion.Parse("5.0.0-preview.1"), BundleChannel.Stable, [NativeTemplateContentRole.Function]),
        };

    private static JsonNode NativeDocument(BasicAcquisitionFixture fixture)
    {
        var root = JsonNode.Parse(fixture.Catalog.Json)!;
        root["schemaVersion"] = 2;
        foreach (var stack in root["stacks"]!.AsArray())
        {
            stack!["hostIdentifier"] = "func";
            stack["engineHostVersions"] = "[5.0.0-preview.1,6.0.0)";
            stack["channels"] = new JsonArray("stable");
            foreach (var requirement in stack["companions"]!.AsArray())
            {
                requirement!["requiredRoles"] = new JsonArray("function");
            }
        }
        foreach (var artifact in root["artifacts"]!.AsArray())
        {
            artifact!["basicOnly"] = false;
            foreach (var definition in artifact["templates"]!.AsArray())
            {
                definition!["contentRole"] = "function";
            }
        }
        return root;
    }
}
