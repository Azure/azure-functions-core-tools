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
        Assert.Contains(_interaction.Lines, l => l.Contains("Python"));
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
    public async Task Search_Results_WritesDefaultTableRows()
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
        Assert.Contains("TABLE: [Alias, Display Name, Description, Latest]", _interaction.Lines);
        Assert.Contains("  ROW: [python, Python, Python workload, 1.2.3]", _interaction.Lines);
        Assert.DoesNotContain(_interaction.Lines, l => l.Contains("func.workload.python"));
        Assert.Contains("HINT: Showing 1 result.", _interaction.Lines);
        Assert.Contains("HINT: Run 'func workload install <alias>' to install one.", _interaction.Lines);
    }

    [Fact]
    public async Task Search_Verbose_AddsPackageIdColumn()
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
        int exit = await InvokeAsync(cmd, includeRootVerbose: true, "--verbose");

        Assert.Equal(0, exit);
        Assert.Contains(
            "TABLE: [Alias, Display Name, Description, Latest, Package ID]",
            _interaction.Lines);
        Assert.Contains(
            "  ROW: [python, Python, Python workload, 1.2.3, func.workload.python]",
            _interaction.Lines);
    }

    [Fact]
    public async Task Search_NoAliasesOrTitle_RendersBlankCellsAndPackageIdFallback()
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

        Assert.Contains("  ROW: [, no.alias.pkg, , 1.0.0]", _interaction.Lines);
        Assert.DoesNotContain(_interaction.Lines, l => l.Contains(", -, "));
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
    public async Task Search_LongDescription_TruncatedWithEllipsis()
    {
        string longDesc = new('x', 500);
        StubSearch(
            new CatalogSearchResult(
                "test.workload",
                NuGetVersion.Parse("1.0.0"),
                Title: null,
                Description: longDesc,
                Aliases: [],
                Source: _stubSource));

        var cmd = new WorkloadSearchCommand(_interaction, _catalog);
        await InvokeAsync(cmd);

        Assert.Contains(_interaction.Lines, l => l.Contains("\u2026"));
        Assert.DoesNotContain(_interaction.Lines, l => l.Contains(longDesc));
    }

    [Fact]
    public async Task Search_JsonOption_EmitsJsonAndSkipsTable()
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
        => InvokeAsync(cmd, includeRootVerbose: false, args);

    private static Task<int> InvokeAsync(WorkloadSearchCommand cmd, bool includeRootVerbose, params string[] args)
    {
        var root = new RootCommand();
        if (includeRootVerbose)
        {
            root.Options.Add(new Option<bool>("--verbose") { Recursive = true });
        }

        root.Subcommands.Add(cmd);
        var config = new InvocationConfiguration { EnableDefaultExceptionHandler = false };
        return root.Parse(new[] { cmd.Name }.Concat(args).ToArray()).InvokeAsync(config);
    }
}
