// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.CommandLine;
using Azure.Functions.Cli.Commands.Workload;
using Azure.Functions.Cli.Workloads.Catalog;
using NSubstitute;
using NuGet.Versioning;
using Xunit;
using PackageSource = NuGet.Configuration.PackageSource;

namespace Azure.Functions.Cli.Tests.Commands.Workload;

public class WorkloadSearchCommandTests
{
    private readonly TestInteractionService _interaction = new();
    private readonly IWorkloadCatalog _catalog = Substitute.For<IWorkloadCatalog>();

    private static readonly PackageSource _stubSource = new("https://example/v3/index.json", "test");

    [Fact]
    public void Search_HasExpectedArgsAndOptions()
    {
        var cmd = new WorkloadSearchCommand(_interaction, _catalog);
        Assert.Single(cmd.Arguments, a => a.Name == "query");
        Assert.Contains(cmd.Options, o => o.Name == "--source");
        Assert.Contains(cmd.Options, o => o.Name == "--prerelease");
        Assert.Contains(cmd.Options, o => o.Name == "--stack");
    }

    [Fact]
    public async Task Search_StackOption_FiltersToWorkloadKind()
    {
        StubSearch(
            new CatalogSearchResult(
                "workloads.python",
                NuGetVersion.Parse("1.0.0"),
                Title: "Python",
                Description: null,
                Aliases: ["python"],
                Source: _stubSource) { Kind = "workload" },
            new CatalogSearchResult(
                "workloads.host",
                NuGetVersion.Parse("1.0.0"),
                Title: "Host",
                Description: null,
                Aliases: ["host"],
                Source: _stubSource) { Kind = "content" },
            new CatalogSearchResult(
                "workloads.nokind",
                NuGetVersion.Parse("1.0.0"),
                Title: "NoKind",
                Description: null,
                Aliases: [],
                Source: _stubSource));

        var cmd = new WorkloadSearchCommand(_interaction, _catalog);
        int exit = await InvokeAsync(cmd, "--stack");

        Assert.Equal(0, exit);
        // Stack workload's display name appears as a card heading.
        Assert.Contains("Python", _interaction.Lines);
        // Filtered-out packages don't appear anywhere.
        Assert.DoesNotContain(_interaction.Lines, l => l.Contains("workloads.host"));
        Assert.DoesNotContain(_interaction.Lines, l => l.Contains("workloads.nokind"));
    }

    [Fact]
    public async Task Search_NoResults_WritesWarning()
    {
        StubSearch();

        var cmd = new WorkloadSearchCommand(_interaction, _catalog);
        int exit = await InvokeAsync(cmd);

        Assert.Equal(0, exit);
        Assert.Contains(_interaction.Lines, l => l.StartsWith("WARNING:") && l.Contains("No workloads found"));
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

        var cmd = new WorkloadSearchCommand(_interaction, _catalog);
        int exit = await InvokeAsync(cmd);

        Assert.Equal(0, exit);
        Assert.DoesNotContain(_interaction.Lines, l => l.StartsWith("TABLE:"));
        Assert.Contains("Python", _interaction.Lines);
        Assert.Contains(_interaction.Lines, l => l.StartsWith("Version:") && l.EndsWith("1.2.3"));
        Assert.Contains(_interaction.Lines, l => l.StartsWith("Package ID:") && l.EndsWith("func.workload.python"));
        Assert.Contains(_interaction.Lines, l => l.StartsWith("Alias:") && l.EndsWith("python"));
        Assert.Contains("Description:", _interaction.Lines);
        Assert.Contains("Python workload", _interaction.Lines);
        Assert.Contains("HINT: Showing 1 result.", _interaction.Lines);
        Assert.Contains("HINT: Run 'func workload install <alias>' to install one.", _interaction.Lines);
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

        var cmd = new WorkloadSearchCommand(_interaction, _catalog);
        await InvokeAsync(cmd);

        Assert.Contains(
            _interaction.Lines,
            l => l.StartsWith("Aliases:") && l.EndsWith("dotnet, dotnet-isolated"));
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

        var cmd = new WorkloadSearchCommand(_interaction, _catalog);
        await InvokeAsync(cmd);

        // Title missing: heading falls back to package id.
        Assert.Contains("no.alias.pkg", _interaction.Lines);
        // Alias line present, value empty.
        string aliasLine = _interaction.Lines.Single(l => l.StartsWith("Alias:"));
        Assert.Equal("Alias:", aliasLine.TrimEnd());
        // No description -> placeholder.
        Assert.Contains("(no description)", _interaction.Lines);
    }

    [Fact]
    public async Task Search_RendersBlankLineBetweenCards()
    {
        StubSearch(
            new CatalogSearchResult("pkg.a", NuGetVersion.Parse("1.0.0"), "A", "first", ["a"], _stubSource),
            new CatalogSearchResult("pkg.b", NuGetVersion.Parse("1.0.0"), "B", "second", ["b"], _stubSource));

        var cmd = new WorkloadSearchCommand(_interaction, _catalog);
        await InvokeAsync(cmd);

        int firstHeading = _interaction.Lines.ToList().FindIndex(l => l == "A");
        int secondHeading = _interaction.Lines.ToList().FindIndex(l => l == "B");
        Assert.InRange(firstHeading, 0, secondHeading - 1);
        // Blank line separator between cards.
        Assert.Equal(string.Empty, _interaction.Lines[secondHeading - 1]);
    }

    [Fact]
    public async Task Search_LongDescription_RenderedInFullOnItsOwnLine()
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

        var cmd = new WorkloadSearchCommand(_interaction, _catalog);
        await InvokeAsync(cmd);

        // The card layout gives description the full terminal width on its
        // own line, so we keep the full string verbatim instead of truncating.
        Assert.Contains(longDesc, _interaction.Lines);
    }

    [Fact]
    public async Task Search_AtPageLimit_FooterAdvisesRefining()
    {
        var stubs = Enumerable.Range(0, 20)
            .Select(i => new CatalogSearchResult(
                $"pkg.{i}",
                NuGetVersion.Parse("1.0.0"),
                Title: $"Pkg {i}",
                Description: null,
                Aliases: [$"p{i}"],
                Source: _stubSource))
            .ToArray();
        StubSearch(stubs);

        var cmd = new WorkloadSearchCommand(_interaction, _catalog);
        await InvokeAsync(cmd);

        Assert.Contains(
            "HINT: Showing 20 results. More may be available, refine your query.",
            _interaction.Lines);
    }

    [Fact]
    public async Task Search_ForwardsQuerySourceAndPrerelease()
    {
        CatalogSearchQuery? captured = null;
        _catalog.SearchAsync(Arg.Do<CatalogSearchQuery>(q => captured = q), Arg.Any<CancellationToken>())
            .Returns(Array.Empty<CatalogSearchResult>());

        var cmd = new WorkloadSearchCommand(_interaction, _catalog);
        int exit = await InvokeAsync(
            cmd,
            "python",
            "--source", "https://example/v3/index.json",
            "--prerelease");

        Assert.Equal(0, exit);
        Assert.NotNull(captured);
        Assert.Equal("python", captured!.Filter);
        Assert.True(captured.IncludePrerelease);
        Assert.Equal("https://example/v3/index.json", captured.Source);
        Assert.Equal(20, captured.Take);
    }

    [Fact]
    public async Task Search_DefaultsPageSize20()
    {
        // Per spec §4.1 the CLI surface has no pagination flags; the
        // command always asks the catalog for a page of 20.
        CatalogSearchQuery? captured = null;
        _catalog.SearchAsync(Arg.Do<CatalogSearchQuery>(q => captured = q), Arg.Any<CancellationToken>())
            .Returns(Array.Empty<CatalogSearchResult>());

        var cmd = new WorkloadSearchCommand(_interaction, _catalog);
        int exit = await InvokeAsync(cmd);

        Assert.Equal(0, exit);
        Assert.NotNull(captured);
        Assert.Null(captured!.Filter);
        // Default while workloads are in preview: --prerelease is implicitly on.
        Assert.True(captured.IncludePrerelease);
        Assert.Null(captured.Source);
        Assert.Equal(20, captured.Take);
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

        var cmd = new WorkloadSearchCommand(_interaction, _catalog);
        int exit = await InvokeAsync(cmd, "--json");

        Assert.Equal(0, exit);
        Assert.Contains(_interaction.Lines, l => l.StartsWith("JSON:"));
        Assert.DoesNotContain(_interaction.Lines, l => l.StartsWith("TABLE:"));
        Assert.DoesNotContain(_interaction.Lines, l => l.StartsWith("Version:"));
        string jsonLine = _interaction.Lines.Single(l => l.StartsWith("JSON:"));
        Assert.Contains("\"packageId\":\"func.workload.python\"", jsonLine);
        Assert.Contains("\"latestVersion\":\"1.2.3\"", jsonLine);
        Assert.Contains("\"aliases\":[\"python\"]", jsonLine);
    }

    private void StubSearch(params CatalogSearchResult[] results)
    {
        _catalog.SearchAsync(Arg.Any<CatalogSearchQuery>(), Arg.Any<CancellationToken>())
            .Returns(results);
    }

    private static Task<int> InvokeAsync(WorkloadSearchCommand cmd, params string[] args)
    {
        var root = new RootCommand();
        root.Subcommands.Add(cmd);
        var config = new InvocationConfiguration { EnableDefaultExceptionHandler = false };
        return root.Parse(new[] { cmd.Name }.Concat(args).ToArray()).InvokeAsync(config);
    }
}
