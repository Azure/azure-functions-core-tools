// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Text.Json;
using Azure.Functions.Cli.Console;
using Azure.Functions.Cli.Console.Theme;
using Azure.Functions.Cli.Quickstart;
using Azure.Functions.Cli.Tests.Commands.Quickstart;
using Azure.Functions.Cli.Tests.Console;

namespace Azure.Functions.Cli.Tests.Quickstart;

public class QuickstartCatalogRendererTests
{
    [Theory]
    [InlineData("Absent")]
    [InlineData("Null")]
    [InlineData("Invalid")]
    [InlineData("Unsupported")]
    [InlineData("Conflicting")]
    [InlineData("Unverified")]
    public void WriteDetails_AllMetadataStates_ShowStatusWithoutClaimingApproval(string status)
    {
        TestInteractionService interaction = new();
        QuickstartCatalogRenderer renderer = new(interaction);
        QuickstartCatalogEntry entry = new(QuickstartTestHelpers.CreateEntry(),
            Enum.Parse<QuickstartPackageMetadataStatus>(status), null, "Diagnostic from the catalog");

        renderer.WriteDetails(entry, "Python");

        interaction.AllOutput.Should().Contain(status).And.Contain("Diagnostic from the catalog");
        interaction.AllOutput.Should().Contain("Package claims are unverified");
        interaction.AllOutput.Should().NotContain("SUCCESS:");
    }

    [Theory]
    [InlineData("Download")]
    [InlineData("FreshCache")]
    [InlineData("RevalidatedCache")]
    [InlineData("StaleCache")]
    [InlineData("LocalFile")]
    public void WriteProvenance_TransportStates_DoNotEstablishTrust(string origin)
    {
        TestInteractionService interaction = new();
        QuickstartCatalogRenderer renderer = new(interaction);
        DateTimeOffset timestamp = new(2026, 10, 21, 0, 0, 0, TimeSpan.Zero);

        renderer.WriteProvenance(new(Enum.Parse<QuickstartCatalogOrigin>(origin), "https://example.test/catalog", timestamp));

        interaction.AllOutput.Should().Contain(origin).And.Contain("not artifact approval");
        interaction.AllOutput.Should().Contain("https://example.test/catalog").And.Contain("2026-10-21T00:00:00");
        interaction.Lines.Any(line => line.StartsWith("WARNING:", StringComparison.Ordinal)).Should().Be(origin == "StaleCache");
    }

    [Fact]
    public void WriteDetails_AdvertisedMixedTopologyAndEvidence_RemainOpaqueUnverifiedClaims()
    {
        TestInteractionService interaction = new();
        QuickstartCatalogRenderer renderer = new(interaction);
        using var projects = JsonDocument.Parse("""[{"stack":"node"},{"stack":"python"}]""");
        using var workloads = JsonDocument.Parse("""{"future":"unknown"}""");
        QuickstartPackageReference reference = new("Example.Package", "1.2.3", "https://example.test/feed",
            "Example.Template", "Example.Group", new("sha256", new string('a', 64), "https://example.test/evidence"),
            projects.RootElement, workloads.RootElement);
        QuickstartCatalogEntry entry = new(QuickstartTestHelpers.CreateEntry(), QuickstartPackageMetadataStatus.Unverified,
            reference, "Unverified metadata");

        renderer.WriteDetails(entry, "Python");

        interaction.AllOutput.Should().Contain("Claimed package").And.Contain("Example.Package");
        interaction.AllOutput.Should().Contain("Claimed version").And.Contain("1.2.3");
        interaction.AllOutput.Should().Contain("Claimed template identity").And.Contain("Example.Template");
        interaction.AllOutput.Should().Contain("Claimed group identity").And.Contain("Example.Group");
        interaction.AllOutput.Should().Contain("Claimed sha256").And.Contain(new string('a', 64));
        interaction.AllOutput.Should().Contain("not fetched").And.Contain("https://example.test/evidence");
        interaction.AllOutput.Should().Contain("""Claimed projects (uninterpreted): [{"stack":"node"},{"stack":"python"}]""");
        interaction.AllOutput.Should().Contain("""Claimed required workloads (uninterpreted): {"future":"unknown"}""");
    }

    [Fact]
    public void Renderer_RealSpectreOutput_TreatsManifestMarkupAsLiteralText()
    {
        using BufferedConsole stdout = new(false, false, noColor: true);
        using BufferedConsole stderr = new(false, false, noColor: true);
        SpectreInteractionService interaction = new(new DefaultTheme(), stdout, stderr);
        QuickstartCatalogRenderer renderer = new(interaction);
        QuickstartCatalogEntry entry = new(
            QuickstartTestHelpers.CreateEntry(displayName: "[not-a-style]sample[/]", shortDescription: "[red]literal[/]"),
            QuickstartPackageMetadataStatus.Invalid, null, "[bad]diagnostic[/]");

        renderer.WriteResults([entry]);
        renderer.WriteDetails(entry, "Python");

        stdout.Output.Should().Contain("[not-a-style]sample[/]").And.Contain("[red]literal[/]");
        stderr.Output.Should().Contain("[bad]diagnostic[/]");
        (stdout.Output + stderr.Output).Should().NotContain("\u001b[");
        stdout.Reads.Should().Be(0);
        stderr.Reads.Should().Be(0);
    }

    [Fact]
    public void Renderer_NullArguments_Throw()
    {
        QuickstartCatalogRenderer renderer = new(new TestInteractionService());
        QuickstartCatalogEntry entry = new(QuickstartTestHelpers.CreateEntry(), QuickstartPackageMetadataStatus.Absent, null, null);

        FluentActions.Invoking(() => renderer.WriteDetails(null!, "Python")).Should().Throw<ArgumentNullException>();
        FluentActions.Invoking(() => renderer.WriteDetails(entry, " ")).Should().Throw<ArgumentException>();
        FluentActions.Invoking(() => renderer.WriteResults(null!)).Should().Throw<ArgumentNullException>();
        FluentActions.Invoking(() => renderer.WriteProvenance(null!)).Should().Throw<ArgumentNullException>();
    }
}
