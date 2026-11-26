// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.CommandLine;
using Azure.Functions.Cli.Commands.Workload;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Workloads.Catalog;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using NuGet.Versioning;
using PackageSource = NuGet.Configuration.PackageSource;

namespace Azure.Functions.Cli.Tests.Commands.Workload;

public class WorkloadSearchCommandTests
{
    private readonly TestInteractionService _interaction = new();
    private readonly IWorkloadCatalog _catalog = Substitute.For<IWorkloadCatalog>();

    private static readonly Microsoft.Extensions.Options.IOptions<Azure.Functions.Cli.Workloads.Catalog.WorkloadCatalogOptions> _testCatalogOptions
        = Microsoft.Extensions.Options.Options.Create(new Azure.Functions.Cli.Workloads.Catalog.WorkloadCatalogOptions());

    private static readonly PackageSource _stubSource = new("https://example/v3/index.json", "test");

    [Fact]
    public void Search_HasExpectedArgsAndOptions()
    {
        var cmd = new WorkloadSearchCommand(_interaction, _catalog, _testCatalogOptions);
        cmd.Arguments.Should().ContainSingle(a => a.Name == "query");
        cmd.Options.Should().Contain(o => o.Name == "--source");
        cmd.Options.Should().Contain(o => o.Name == "--prerelease");
        cmd.Options.Should().Contain(o => o.Name == "--stack");
    }

    [Fact]
    public async Task Search_StackOption_FiltersToDeclaredStacksUsingActualPackageIds()
    {
        StubSearch(
            new CatalogSearchResult(
                "Contoso.DevKit",
                NuGetVersion.Parse("1.0.0"),
                Title: "Package title",
                Description: null,
                Aliases: ["preview", "comet"],
                Source: _stubSource) { Stack = new("comet", "native", "Comet SDK") },
            new CatalogSearchResult(
                "workloads.comet",
                NuGetVersion.Parse("1.0.0"),
                Title: "Host",
                Description: null,
                Aliases: ["comet"],
                Source: _stubSource) { Kind = "workload" },
            new CatalogSearchResult(
                "workloads.nokind",
                NuGetVersion.Parse("1.0.0"),
                Title: "NoKind",
                Description: null,
                Aliases: [],
                Source: _stubSource));

        var cmd = new WorkloadSearchCommand(_interaction, _catalog, _testCatalogOptions);
        int exit = await InvokeAsync(cmd, "--stack");

        exit.Should().Be(0);
        // Stack workload's display name appears as a card heading.
        _interaction.Lines.Should().Contain("Comet SDK");
        _interaction.Lines.Should().Contain(l => l.StartsWith("Stack ID:") && l.EndsWith("comet"));
        _interaction.Lines.Should().Contain(l => l.StartsWith("Package ID:") && l.EndsWith("Contoso.DevKit"));
        // Filtered-out packages don't appear anywhere.
        _interaction.Lines.Should().NotContain(l => l.Contains("workloads.comet"));
        _interaction.Lines.Should().NotContain(l => l.Contains("workloads.nokind"));
    }

    [Fact]
    public async Task Search_NoResults_WritesWarning()
    {
        StubSearch();

        var cmd = new WorkloadSearchCommand(_interaction, _catalog, _testCatalogOptions);
        int exit = await InvokeAsync(cmd);

        exit.Should().Be(0);
        _interaction.Lines.Should().Contain(l => l.StartsWith("WARNING:") && l.Contains("No workloads found"));
    }

    [Fact]
    public async Task Search_Results_RenderCardWithFields()
    {
        StubSearch(
            new CatalogSearchResult(
                "func.workload.python",
                NuGetVersion.Parse("1.2.3"),
                Title: "Python",
                Description: "Python workload",
                Aliases: ["python"],
                Source: _stubSource));

        var cmd = new WorkloadSearchCommand(_interaction, _catalog, _testCatalogOptions);
        int exit = await InvokeAsync(cmd);

        exit.Should().Be(0);
        _interaction.Lines.Should().NotContain(l => l.StartsWith("TABLE:"));
        _interaction.Lines.Should().Contain("Python");
        _interaction.Lines.Should().Contain(l => l.StartsWith("Version:") && l.EndsWith("1.2.3"));
        _interaction.Lines.Should().Contain(l => l.StartsWith("Package ID:") && l.EndsWith("func.workload.python"));
        _interaction.Lines.Should().Contain(l => l.StartsWith("Alias:") && l.EndsWith("python"));
        _interaction.Lines.Should().Contain(l => l.StartsWith("Description:") && l.EndsWith("Python workload"));
        _interaction.Lines.Should().Contain("HINT: Showing 1 result.");
        _interaction.Lines.Should().Contain("HINT: Run 'func workload install <package-id> --exact' to install a package by ID.");
        _interaction.Lines.Should().Contain("HINT: When several versions or channels exist, pass --version (-v) to install a specific one.");
    }

    [Fact]
    public async Task Search_MultipleAliases_UsesPluralLabelAndJoinsValues()
    {
        StubSearch(
            new CatalogSearchResult(
                "func.workload.dotnet",
                NuGetVersion.Parse("1.0.0"),
                Title: ".NET",
                Description: "C# workload",
                Aliases: ["dotnet", "dotnet-isolated"],
                Source: _stubSource));

        var cmd = new WorkloadSearchCommand(_interaction, _catalog, _testCatalogOptions);
        await InvokeAsync(cmd);

        _interaction.Lines.Should().Contain(l => l.StartsWith("Aliases:") && l.EndsWith("dotnet, dotnet-isolated"));
    }

    [Fact]
    public async Task Search_NoAliases_OmitsAliasValue()
    {
        StubSearch(
            new CatalogSearchResult(
                "no.alias.pkg",
                NuGetVersion.Parse("1.0.0"),
                Title: null,
                Description: null,
                Aliases: [],
                Source: _stubSource));

        var cmd = new WorkloadSearchCommand(_interaction, _catalog, _testCatalogOptions);
        await InvokeAsync(cmd);

        // Title missing: heading falls back to package id.
        _interaction.Lines.Should().Contain("no.alias.pkg");
        // Alias line present, value empty.
        string aliasLine = _interaction.Lines.Single(l => l.StartsWith("Alias:"));
        aliasLine.TrimEnd().Should().Be("Alias:");
        // No description -> placeholder shown on the Description: line.
        _interaction.Lines.Should().Contain(l => l.StartsWith("Description:") && l.EndsWith("(no description)"));
    }

    [Fact]
    public async Task Search_RendersBlankLineBetweenCards()
    {
        StubSearch(
            new CatalogSearchResult("pkg.a", NuGetVersion.Parse("1.0.0"), "A", "first", ["a"], _stubSource),
            new CatalogSearchResult("pkg.b", NuGetVersion.Parse("1.0.0"), "B", "second", ["b"], _stubSource));

        var cmd = new WorkloadSearchCommand(_interaction, _catalog, _testCatalogOptions);
        await InvokeAsync(cmd);

        int firstHeading = _interaction.Lines.ToList().FindIndex(l => l == "A");
        int secondHeading = _interaction.Lines.ToList().FindIndex(l => l == "B");
        firstHeading.Should().BeInRange(0, secondHeading - 1);
        // Blank line separator between cards.
        _interaction.Lines[secondHeading - 1].Should().Be(string.Empty);
    }

    [Fact]
    public async Task Search_LongDescription_RenderedInFullOnDescriptionLine()
    {
        string longDesc = new('x', 500);
        StubSearch(
            new CatalogSearchResult(
                "test.workload",
                NuGetVersion.Parse("1.0.0"),
                Title: "Test",
                Description: longDesc,
                Aliases: ["test"],
                Source: _stubSource));

        var cmd = new WorkloadSearchCommand(_interaction, _catalog, _testCatalogOptions);
        await InvokeAsync(cmd);

        // Card layout keeps the description on its own Description: line and
        // emits the full string verbatim rather than truncating it.
        _interaction.Lines.Should().Contain(l => l.StartsWith("Description:") && l.EndsWith(longDesc));
    }

    [Fact]
    public async Task Search_BeyondFirstPage_RendersAllResults()
    {
        var stubs = Enumerable.Range(0, 21)
            .Select(i => new CatalogSearchResult(
                $"pkg.{i}",
                NuGetVersion.Parse("1.0.0"),
                Title: $"Pkg {i}",
                Description: null,
                Aliases: [$"p{i}"],
                Source: _stubSource))
            .ToArray();
        StubSearch(stubs);

        var cmd = new WorkloadSearchCommand(_interaction, _catalog, _testCatalogOptions);
        await InvokeAsync(cmd);

        _interaction.Lines.Should().Contain("HINT: Showing 21 results.");
        _interaction.Lines.Should().Contain(l => l.StartsWith("Package ID:") && l.EndsWith("pkg.20"));
    }

    [Fact]
    public async Task Search_ForwardsQuerySourceAndPrerelease()
    {
        CatalogSearchQuery? captured = null;
        _catalog.SearchVersionsAsync(Arg.Do<CatalogSearchQuery>(q => captured = q), Arg.Any<CancellationToken>())
            .Returns(Array.Empty<CatalogSearchResult>());

        var cmd = new WorkloadSearchCommand(_interaction, _catalog, _testCatalogOptions);
        int exit = await InvokeAsync(
            cmd,
            "python",
            "--source", "https://example/v3/index.json",
            "--prerelease");

        exit.Should().Be(0);
        captured.Should().NotBeNull();
        captured!.Filter.Should().Be("python");
        captured.IncludePrerelease.Should().BeTrue();
        captured.Source.Should().Be("https://example/v3/index.json");
        captured.Take.Should().Be(20);
    }

    [Fact]
    public async Task Search_DefaultsPageSize20()
    {
        CatalogSearchQuery? captured = null;
        _catalog.SearchVersionsAsync(Arg.Do<CatalogSearchQuery>(q => captured = q), Arg.Any<CancellationToken>())
            .Returns(Array.Empty<CatalogSearchResult>());

        var cmd = new WorkloadSearchCommand(_interaction, _catalog, _testCatalogOptions);
        int exit = await InvokeAsync(cmd);

        exit.Should().Be(0);
        captured.Should().NotBeNull();
        captured!.Filter.Should().BeNull();
        captured.IncludePrerelease.Should().BeFalse();
        captured.Source.Should().BeNull();
        captured.Take.Should().Be(20);
    }

    [Fact]
    public async Task Search_JsonOption_EmitsJsonAndSkipsCards()
    {
        StubSearch(
            new CatalogSearchResult(
                "func.workload.python",
                NuGetVersion.Parse("1.2.3"),
                Title: "Python",
                Description: "Python workload",
                Aliases: ["python"],
                Source: _stubSource));

        var cmd = new WorkloadSearchCommand(_interaction, _catalog, _testCatalogOptions);
        int exit = await InvokeAsync(cmd, "--json");

        exit.Should().Be(0);
        _interaction.Lines.Should().Contain(l => l.StartsWith("JSON:"));
        _interaction.Lines.Should().NotContain(l => l.StartsWith("TABLE:"));
        _interaction.Lines.Should().NotContain(l => l.StartsWith("Version:"));
        string jsonLine = _interaction.Lines.Single(l => l.StartsWith("JSON:"));
        jsonLine.Should().Contain("\"packageId\":\"func.workload.python\"");
        jsonLine.Should().Contain("\"latestVersion\":\"1.2.3\"");
        jsonLine.Should().Contain("\"aliases\":[\"python\"]");
    }

    [Fact]
    public async Task Search_Prerelease_ExpandsResultsByChannel()
    {
        const string id = "Contoso.DevKit";
        StubSearch(
            Row(id, "4.35.0", "Stable label"),
            Row(id, "4.40.0-experimental.2", "Experimental label"),
            Row(id, "4.42.0-preview.1", "Preview label"));

        var cmd = new WorkloadSearchCommand(_interaction, _catalog, _testCatalogOptions);
        int exit = await InvokeAsync(cmd, "--prerelease");

        exit.Should().Be(0);
        // One card per channel. Channel-label line is emitted on expansion
        // so users can tell which row matches `--version` for which channel.
        _interaction.Lines.Should().Contain(l => l.StartsWith("Version:") && l.EndsWith("4.42.0-preview.1"));
        _interaction.Lines.Should().Contain(l => l.StartsWith("Version:") && l.EndsWith("4.40.0-experimental.2"));
        _interaction.Lines.Should().Contain(l => l.StartsWith("Version:") && l.EndsWith("4.35.0"));
        _interaction.Lines.Should().Contain(l => l.StartsWith("Channel:") && l.EndsWith("preview"));
        _interaction.Lines.Should().Contain(l => l.StartsWith("Channel:") && l.EndsWith("experimental"));
        _interaction.Lines.Should().Contain(l => l.StartsWith("Channel:") && l.EndsWith("stable"));
        _interaction.Lines.Should().ContainInOrder("Preview label", "Experimental label", "Stable label");
        _interaction.Lines.Should().Contain("HINT: Showing 3 results.");
    }

    [Fact]
    public async Task Search_Prerelease_PicksLatestPerChannel()
    {
        StubSearch(Row("pkg", "1.5.0"), Row("pkg", "2.0.0"), Row("pkg", "2.0.0-preview.1"),
            Row("pkg", "2.0.0-preview.5"), Row("pkg", "2.1.0-preview.2"));

        var cmd = new WorkloadSearchCommand(_interaction, _catalog, _testCatalogOptions);
        await InvokeAsync(cmd, "--prerelease");

        // Latest stable wins out over earlier stable, latest preview wins
        // out over earlier prereleases on the same channel.
        _interaction.Lines.Should().Contain(l => l.StartsWith("Version:") && l.EndsWith("2.1.0-preview.2"));
        _interaction.Lines.Should().Contain(l => l.StartsWith("Version:") && l.EndsWith("2.0.0"));
        _interaction.Lines.Should().NotContain(l => l.StartsWith("Version:") && l.EndsWith("2.0.0-preview.5"));
        _interaction.Lines.Should().NotContain(l => l.StartsWith("Version:") && l.EndsWith("1.5.0"));
    }

    [Fact]
    public async Task Search_NoPrerelease_DoesNotExpand()
    {
        StubSearch(
            new CatalogSearchResult(
                "pkg",
                NuGetVersion.Parse("1.0.0"),
                Title: "Pkg",
                Description: null,
                Aliases: [],
                Source: _stubSource));

        var cmd = new WorkloadSearchCommand(_interaction, _catalog, _testCatalogOptions);
        await InvokeAsync(cmd);

        _interaction.Lines.Should().NotContain(l => l.StartsWith("Channel:"));
    }

    [Fact]
    public async Task Search_DiscoveryFailure_IsNotAnEmptySuccessfulResult()
    {
        _catalog.SearchVersionsAsync(Arg.Any<CatalogSearchQuery>(), Arg.Any<CancellationToken>()).ThrowsAsync(new WorkloadCatalogException("feed down"));

        var cmd = new WorkloadSearchCommand(_interaction, _catalog, _testCatalogOptions);
        var error = await FluentActions.Awaiting(() => InvokeAsync(cmd, "--stack")).Should().ThrowAsync<GracefulException>();

        error.Which.IsUserError.Should().BeTrue();
        error.Which.Message.Should().Contain("discovery is unavailable").And.Contain("feed down");
        _interaction.Lines.Should().NotContain(l => l.Contains("No workloads found"));
    }

    [Theory]
    [InlineData("1.0.0", "stable")]
    [InlineData("1.0.0-preview.1", "preview")]
    [InlineData("4.42.0-experimental.2", "experimental")]
    [InlineData("1.0.0-alpha", "alpha")]
    [InlineData("1.0.0-rc.1+build.5", "rc")]
    public void GetChannel_ParsesPrereleaseLabel(string version, string expectedChannel)
    {
        WorkloadSearchCommand.GetChannel(NuGetVersion.Parse(version)).Should().Be(expectedChannel);
    }

    private void StubSearch(params CatalogSearchResult[] results)
    {
        _catalog.SearchVersionsAsync(Arg.Any<CatalogSearchQuery>(), Arg.Any<CancellationToken>())
            .Returns(results);
    }

    private static CatalogSearchResult Row(string id, string version, string label = "Comet")
        => new(id, NuGetVersion.Parse(version), label, null, [], _stubSource) { Stack = new("comet", "worker", label) };

    private static Task<int> InvokeAsync(WorkloadSearchCommand cmd, params string[] args)
    {
        var root = new RootCommand();
        root.Subcommands.Add(cmd);
        var config = new InvocationConfiguration { EnableDefaultExceptionHandler = false };
        return root.Parse(new[] { cmd.Name }.Concat(args).ToArray()).InvokeAsync(config);
    }
}
